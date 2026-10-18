using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModActivationMonitoring;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>
	/// An in-process invalidation signal, not a native owner registry or evidence of a successful commit.
	/// Failed/partial operations invalidate the view as well: the only safe response is to re-observe
	/// the CURRENT native services. In particular, DeploymentCommitSequence is not a universal version.
	/// </summary>
	public sealed class NativeComponentLifecycleObserver : IDisposable
	{
		private readonly ModActivationMonitor _monitor;
		private readonly ModManager _manager;
		private readonly VirtualModActivator _virtualActivator;
		private volatile NativeDeploymentMutationSignal _deploymentMutations;
		private volatile NativeDeploymentMutationSignal _virtualOwnerMutations;
		private readonly HashSet<IBackgroundTaskSet> _watched = new HashSet<IBackgroundTaskSet>();
		private readonly object _gate = new object();
		private int _revision;
		private volatile bool _disposed;

		public event EventHandler Changed;

		/// <summary>Observes the existing installer lane and native owner signals without creating a new authority.</summary>
		public NativeComponentLifecycleObserver(ModActivationMonitor monitor, ModManager manager)
		{
			_monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
			_manager = manager;
			_virtualActivator = manager?.VirtualModActivator;
			_monitor.Tasks.CollectionChanged += TasksChanged;
			foreach (IBackgroundTaskSet task in _monitor.Tasks)
				Watch(task);
			if (_manager != null)
			{
				_manager.NativeStateReinitialized += NativeAuthorityReinitialized;
				_manager.NativeFileOwnerChanged += NativeStateChanged;
				BindDeploymentMutations((_manager.DeploymentManager as ModDeploymentManager)?.NativeOwnerMutations);
			}
			if (_virtualActivator != null)
			{
				_virtualActivator.VirtualStoreMutationEnded += NativeStateChanged;
				_virtualOwnerMutations = _virtualActivator.NativeOwnerMutations;
				_virtualOwnerMutations.Changed += NativeStateChanged;
			}
		}

		public int Revision => Volatile.Read(ref _revision);

		/// <summary>Never read native ownership while the shared installer lane is actively changing it.</summary>
		public bool HasRunningNativeTask
		{
			get
			{
				IBackgroundTaskSet task = _monitor.RunningTask;
				return (task != null && !task.IsCompleted) ||
					(_deploymentMutations?.IsActive == true) || (_virtualOwnerMutations?.IsActive == true);
			}
		}

		/// <summary>Tracks installer-lane additions and removals without reading disk in native callbacks.</summary>
		private void TasksChanged(object sender, NotifyCollectionChangedEventArgs args)
		{
			if (args.OldItems != null)
				foreach (IBackgroundTaskSet task in args.OldItems) Unwatch(task);
			if (args.Action == NotifyCollectionChangedAction.Reset)
			{
				lock (_gate)
				{
					foreach (IBackgroundTaskSet task in _watched) task.TaskSetCompleted -= TaskCompleted;
					_watched.Clear();
				}
				foreach (IBackgroundTaskSet task in _monitor.Tasks) Watch(task);
				NotifyExternalChange();
			}
			if (args.NewItems != null)
				foreach (IBackgroundTaskSet task in args.NewItems) Watch(task);
		}

		/// <summary>Subscribes once to native installer/uninstaller completion, including an already-completing task.</summary>
		private void Watch(IBackgroundTaskSet task)
		{
			if (!(task is ModInstaller) && !(task is ModUninstaller)) return;
			lock (_gate)
			{
				if (_disposed || task.IsCompleted || !_watched.Add(task)) return;
				task.TaskSetCompleted += TaskCompleted;
				// The monitor publishes before start, but observing a pre-existing task can race completion.
				if (task.IsCompleted)
				{
					_watched.Remove(task);
					task.TaskSetCompleted -= TaskCompleted;
					NotifyExternalChange();
				}
			}
		}

		/// <summary>Releases a task completion subscription when it is no longer monitored.</summary>
		private void Unwatch(IBackgroundTaskSet task)
		{
			lock (_gate)
				if (_watched.Remove(task)) task.TaskSetCompleted -= TaskCompleted;
		}

		/// <summary>Invalidates detached views after either successful or failed native work.</summary>
		private void TaskCompleted(object sender, TaskSetCompletedEventArgs args)
		{
			Unwatch(sender as IBackgroundTaskSet);
			NotifyExternalChange();
		}

		/// <summary>Forwards a native ownership change as an observation revision.</summary>
		private void NativeStateChanged(object sender, EventArgs args) => NotifyExternalChange();

		/// <summary>Rebinds to the current deployment service after native authority is reloaded.</summary>
		private void NativeAuthorityReinitialized(object sender, EventArgs args)
		{
			// ModManager replaces its deployment service after InstallLog reload. An old
			// service can no longer describe live Collection/restore owner switches.
			BindDeploymentMutations((_manager.DeploymentManager as ModDeploymentManager)?.NativeOwnerMutations);
			NotifyExternalChange();
		}

		/// <summary>Internal test seam and native-authority-reload binding. No disk reads.</summary>
		internal void BindDeploymentMutations(NativeDeploymentMutationSignal source)
		{
			lock (_gate)
			{
				if (_disposed || ReferenceEquals(_deploymentMutations, source)) return;
				if (_deploymentMutations != null) _deploymentMutations.Changed -= NativeStateChanged;
				_deploymentMutations = source;
				if (source != null) source.Changed += NativeStateChanged;
			}
			// Even without a mutation, a rebind invalidates a snapshot from the old
			// InstallLog generation. Do not wait for a future owner switch.
			NotifyExternalChange();
		}

		/// <summary>Can also be used by a future characterized native deployment observer.</summary>
		internal void NotifyExternalChange()
		{
			if (_disposed) return;
			Interlocked.Increment(ref _revision);
			// Observers must marshal to UI and must never perform disk scans in a native callback.
			try { Changed?.Invoke(this, EventArgs.Empty); }
			catch (Exception error)
			{
				Trace.TraceWarning("Native component-index invalidation observer failed: {0}", error.Message);
			}
		}

		/// <summary>Unsubscribes from installer, deployment and Virtual state notifications.</summary>
		public void Dispose()
		{
			lock (_gate)
			{
				if (_disposed) return;
				_disposed = true;
				_monitor.Tasks.CollectionChanged -= TasksChanged;
				foreach (IBackgroundTaskSet task in _watched) task.TaskSetCompleted -= TaskCompleted;
				_watched.Clear();
				if (_manager != null)
				{
					_manager.NativeStateReinitialized -= NativeAuthorityReinitialized;
					_manager.NativeFileOwnerChanged -= NativeStateChanged;
				}
				if (_deploymentMutations != null)
					_deploymentMutations.Changed -= NativeStateChanged;
				_deploymentMutations = null;
				if (_virtualActivator != null)
					_virtualActivator.VirtualStoreMutationEnded -= NativeStateChanged;
				if (_virtualOwnerMutations != null)
					_virtualOwnerMutations.Changed -= NativeStateChanged;
				_virtualOwnerMutations = null;
			}
		}
	}

}
