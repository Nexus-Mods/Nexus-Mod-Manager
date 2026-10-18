using System;
using Nexus.Client.ModActivationMonitoring;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>Preserves Witcher's existing lifecycle API over the shared native component observer.</summary>
	public sealed class Witcher3NativeLifecycleObserver : IDisposable
	{
		private readonly NativeComponentLifecycleObserver _observer;
		public event EventHandler Changed;

		/// <summary>Attaches the same native task, deployment and Virtual ownership signals used by the component view.</summary>
		public Witcher3NativeLifecycleObserver(ModActivationMonitor monitor, ModManager manager)
		{
			_observer = new NativeComponentLifecycleObserver(monitor, manager);
			_observer.Changed += ForwardChange;
		}

		public int Revision => _observer.Revision;
		public bool HasRunningNativeTask => _observer.HasRunningNativeTask;

		/// <summary>Retains the Witcher observer as the sender presented to existing subscribers.</summary>
		private void ForwardChange(object sender, EventArgs args) => Changed?.Invoke(this, args);

		/// <summary>Preserves native-authority reload binding for existing Witcher callers.</summary>
		internal void BindDeploymentMutations(NativeDeploymentMutationSignal source) => _observer.BindDeploymentMutations(source);

		/// <summary>Preserves explicit invalidation without reading native state or disk.</summary>
		internal void NotifyExternalChange() => _observer.NotifyExternalChange();

		/// <summary>Releases the shared observer's native subscriptions.</summary>
		public void Dispose()
		{
			_observer.Changed -= ForwardChange;
			_observer.Dispose();
		}
	}

	/// <summary>Pure revision decisions shared by UI refresh and review approval.</summary>
	internal static class Witcher3NativeRefreshPolicy
	{
		internal static bool IsStale(int observedRevision, int currentRevision) =>
			observedRevision != currentRevision;

		internal static bool CanReadAutomatically(bool active, bool busy, bool draftDirty,
			bool nativeOperationRunning, bool refreshNeeded) =>
			active && !busy && !draftDirty && !nativeOperationRunning && refreshNeeded;

		internal static bool CanApprove(int observedRevision, int currentRevision,
			bool nativeOperationRunning) =>
			!nativeOperationRunning && !IsStale(observedRevision, currentRevision);
	}
}
