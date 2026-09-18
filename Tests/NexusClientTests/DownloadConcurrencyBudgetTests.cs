using System;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.DownloadMonitoring;
using Nexus.Client.DownloadMonitoring.UI;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.Settings;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Locks down the existing outer download concurrency budget before Collections starts sharing the downloader.
	/// </summary>
	[TestFixture]
	public class DownloadConcurrencyBudgetTests
	{
		/// <summary>
		/// The user-selected cap is enforced when it is lower than the repository allowance.
		/// </summary>
		[Test]
		public void ResumeTask_UserLimitBelowRepositoryLimit_QueuesRemoteTaskAtUserBudget()
		{
			IModRepository repository = CreateRepository(10);
			DownloadMonitor monitor = new DownloadMonitor();
			DownloadMonitorVM viewModel = CreateViewModel(monitor, repository, 2);

			monitor.AddActivity(CreateTask(repository, TaskStatus.Running, true));
			monitor.AddActivity(CreateTask(repository, TaskStatus.Running, true));
			ControllableAddModTask candidate = CreateTask(repository, TaskStatus.Paused, true);
			monitor.AddActivity(candidate);

			viewModel.ResumeTask(candidate);

			Assert.AreEqual(1, candidate.QueueCount);
			Assert.AreEqual(0, candidate.ResumeCount);
			Assert.AreEqual(TaskStatus.Queued, candidate.Status);
		}

		/// <summary>
		/// The repository allowance is enforced when it is lower than the user-selected cap.
		/// </summary>
		[Test]
		public void ResumeTask_RepositoryLimitBelowUserLimit_QueuesRemoteTaskAtRepositoryBudget()
		{
			IModRepository repository = CreateRepository(1);
			DownloadMonitor monitor = new DownloadMonitor();
			DownloadMonitorVM viewModel = CreateViewModel(monitor, repository, 5);

			monitor.AddActivity(CreateTask(repository, TaskStatus.Running, true));
			ControllableAddModTask candidate = CreateTask(repository, TaskStatus.Paused, true);
			monitor.AddActivity(candidate);

			viewModel.ResumeTask(candidate);

			Assert.AreEqual(1, candidate.QueueCount);
			Assert.AreEqual(0, candidate.ResumeCount);
			Assert.AreEqual(TaskStatus.Queued, candidate.Status);
		}

		/// <summary>
		/// A retrying download still owns its outer download slot.
		/// </summary>
		[Test]
		public void ResumeTask_RetryingTask_CountsAgainstEffectiveBudget()
		{
			IModRepository repository = CreateRepository(10);
			DownloadMonitor monitor = new DownloadMonitor();
			DownloadMonitorVM viewModel = CreateViewModel(monitor, repository, 2);

			monitor.AddActivity(CreateTask(repository, TaskStatus.Running, true));
			monitor.AddActivity(CreateTask(repository, TaskStatus.Retrying, true));
			ControllableAddModTask candidate = CreateTask(repository, TaskStatus.Paused, true);
			monitor.AddActivity(candidate);

			viewModel.ResumeTask(candidate);

			Assert.AreEqual(2, viewModel.RunningTasks.Count);
			Assert.AreEqual(1, candidate.QueueCount);
			Assert.AreEqual(0, candidate.ResumeCount);
		}

		/// <summary>
		/// Paused and queued work does not consume a running slot.
		/// </summary>
		[Test]
		public void RunningTasks_IncludesRunningAndRetrying_ButNotPausedOrQueued()
		{
			IModRepository repository = CreateRepository(10);
			DownloadMonitor monitor = new DownloadMonitor();
			DownloadMonitorVM viewModel = CreateViewModel(monitor, repository, 5);

			ControllableAddModTask running = CreateTask(repository, TaskStatus.Running, true);
			ControllableAddModTask retrying = CreateTask(repository, TaskStatus.Retrying, true);
			ControllableAddModTask paused = CreateTask(repository, TaskStatus.Paused, true);
			ControllableAddModTask queued = CreateTask(repository, TaskStatus.Queued, true);
			monitor.AddActivity(running);
			monitor.AddActivity(retrying);
			monitor.AddActivity(paused);
			monitor.AddActivity(queued);

			Assert.AreEqual(2, viewModel.RunningTasks.Count);
			CollectionAssert.Contains(viewModel.RunningTasks, running);
			CollectionAssert.Contains(viewModel.RunningTasks, retrying);
			CollectionAssert.DoesNotContain(viewModel.RunningTasks, paused);
			CollectionAssert.DoesNotContain(viewModel.RunningTasks, queued);
		}

		/// <summary>
		/// A free slot resumes the requested task instead of creating an extra queued layer.
		/// </summary>
		[Test]
		public void ResumeTask_BelowEffectiveBudget_ResumesRemoteTask()
		{
			IModRepository repository = CreateRepository(10);
			DownloadMonitor monitor = new DownloadMonitor();
			DownloadMonitorVM viewModel = CreateViewModel(monitor, repository, 2);

			monitor.AddActivity(CreateTask(repository, TaskStatus.Running, true));
			ControllableAddModTask candidate = CreateTask(repository, TaskStatus.Paused, true);
			monitor.AddActivity(candidate);

			viewModel.ResumeTask(candidate);

			Assert.IsTrue(SpinWait.SpinUntil(() => candidate.ResumeCount == 1, 3000),
				"Resume worker did not start the task within the test timeout.");
			Assert.AreEqual(0, candidate.QueueCount);
			Assert.AreEqual(TaskStatus.Running, candidate.Status);
			Assert.AreEqual(2, viewModel.RunningTasks.Count);
		}

		private static DownloadMonitorVM CreateViewModel(DownloadMonitor monitor, IModRepository repository, int userLimit)
		{
			ISettings settings = new InterfaceProxy<ISettings>(call =>
			{
				if (call.MethodName == "get_MaxConcurrentDownloads")
					return userLimit;
				return GetDefaultValue(((MethodInfo)call.MethodBase).ReturnType);
			}).Object;

			return new DownloadMonitorVM(monitor, settings, null, repository);
		}

		private static IModRepository CreateRepository(int repositoryLimit)
		{
			return new InterfaceProxy<IModRepository>(call =>
			{
				switch (call.MethodName)
				{
					case "get_IsOffline":
						return false;
					case "get_MaxConcurrentDownloads":
						return repositoryLimit;
					case "get_SupportsUnauthenticatedDownload":
						return true;
					default:
						return GetDefaultValue(((MethodInfo)call.MethodBase).ReturnType);
				}
			}).Object;
		}

		private static ControllableAddModTask CreateTask(IModRepository repository, TaskStatus status, bool isRemote)
		{
			return new ControllableAddModTask(repository, status, isRemote);
		}

		private static object GetDefaultValue(Type type)
		{
			return type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
		}

		private sealed class ControllableAddModTask : AddModTask
		{
			private int m_resumeCount;
			private int m_queueCount;

			public ControllableAddModTask(IModRepository repository, TaskStatus status, bool isRemote)
				: base(null, null, null, null, null, repository,
					new Uri("https://example.invalid/" + Guid.NewGuid().ToString("N") + ".7z"), null)
			{
				IsRemote = isRemote;
				Status = status;
			}

			public int ResumeCount => Volatile.Read(ref m_resumeCount);

			public int QueueCount => Volatile.Read(ref m_queueCount);

			public override void Resume()
			{
				Interlocked.Increment(ref m_resumeCount);
				Status = TaskStatus.Running;
			}

			public override void Queue()
			{
				Interlocked.Increment(ref m_queueCount);
				Status = TaskStatus.Queued;
			}
		}

		private sealed class InterfaceProxy<T> : RealProxy where T : class
		{
			private readonly Func<IMethodCallMessage, object> m_handler;

			public InterfaceProxy(Func<IMethodCallMessage, object> handler)
				: base(typeof(T))
			{
				m_handler = handler;
			}

			public T Object => (T)GetTransparentProxy();

			public override IMessage Invoke(IMessage msg)
			{
				IMethodCallMessage call = (IMethodCallMessage)msg;
				try
				{
					object result = m_handler(call);
					return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
				}
				catch (Exception ex)
				{
					return new ReturnMessage(ex, call);
				}
			}
		}
	}
}
