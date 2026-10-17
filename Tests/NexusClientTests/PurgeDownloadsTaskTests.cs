using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.DownloadMonitoring;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Exercises purge cancellation without network requests, user settings or file deletion.</summary>
	[TestFixture]
	public class PurgeDownloadsTaskTests
	{
		/// <summary>Finishes the current download cleanup, then leaves all later downloads in the monitor.</summary>
		[Test]
		public void CancelDuringCleanup_StopsBeforeNextDownloadAndRaisesCancelledCompletion()
		{
			DownloadMonitor monitor = new DownloadMonitor();
			List<ControlledDownload> downloads = CreateDownloads(monitor, 9);
			var purge = new PurgeDownloadsTask(downloads.Cast<IBackgroundTask>().ToList(), monitor);
			downloads[4].OnCancel = purge.Cancel;

			RunToEnd(purge, TaskStatus.Cancelled);

			Assert.That(purge.OverallProgress, Is.EqualTo(5));
			Assert.That(downloads.Take(5).All(x => x.CancelCount == 1), Is.True);
			Assert.That(downloads.Skip(5).All(x => x.CancelCount == 0 && x.Status == TaskStatus.Queued), Is.True);
			CollectionAssert.AreEqual(downloads.Skip(5).ToArray(), monitor.Tasks.ToArray());
		}

		/// <summary>An early request must not be replaced by Running when the worker initializes.</summary>
		[Test]
		public void CancelDuringInitialization_DoesNotPurgeAnyDownload()
		{
			DownloadMonitor monitor = new DownloadMonitor();
			List<ControlledDownload> downloads = CreateDownloads(monitor, 3);
			var purge = new PurgeDownloadsTask(downloads.Cast<IBackgroundTask>().ToList(), monitor);
			purge.PropertyChanged += (sender, args) =>
			{
				if (args.PropertyName == "OverallMessage") purge.Cancel();
			};

			RunToEnd(purge, TaskStatus.Cancelled);

			Assert.That(purge.OverallProgress, Is.Zero);
			Assert.That(downloads.All(x => x.CancelCount == 0), Is.True);
			CollectionAssert.AreEqual(downloads.ToArray(), monitor.Tasks.ToArray());
		}

		/// <summary>A cancellation received while presenting an item prevents its cleanup from starting.</summary>
		[Test]
		public void CancelWhenItemIsPresented_LeavesThatDownloadAndRemainingItemsUntouched()
		{
			DownloadMonitor monitor = new DownloadMonitor();
			List<ControlledDownload> downloads = CreateDownloads(monitor, 3);
			var purge = new PurgeDownloadsTask(downloads.Cast<IBackgroundTask>().ToList(), monitor);
			int messages = 0;
			purge.PropertyChanged += (sender, args) =>
			{
				if (args.PropertyName == "OverallMessage" && ++messages == 2) purge.Cancel();
			};

			RunToEnd(purge, TaskStatus.Cancelled);

			Assert.That(purge.OverallProgress, Is.Zero);
			Assert.That(downloads.All(x => x.CancelCount == 0), Is.True);
			CollectionAssert.AreEqual(downloads.ToArray(), monitor.Tasks.ToArray());
		}

		/// <summary>Normal and empty purges still finish through the standard background-task completion path.</summary>
		[TestCase(0)]
		[TestCase(3)]
		public void PurgeWithoutCancellation_Completes(int count)
		{
			DownloadMonitor monitor = new DownloadMonitor();
			List<ControlledDownload> downloads = CreateDownloads(monitor, count);
			var purge = new PurgeDownloadsTask(downloads.Cast<IBackgroundTask>().ToList(), monitor);

			RunToEnd(purge, TaskStatus.Complete);

			Assert.That(purge.OverallProgress, Is.EqualTo(count));
			Assert.That(downloads.All(x => x.CancelCount == 1), Is.True);
			Assert.That(monitor.Tasks, Is.Empty);
		}

		/// <summary>Waits for the same terminal event that closes the progress dialog, with a bounded timeout.</summary>
		private static void RunToEnd(PurgeDownloadsTask purge, TaskStatus expected)
		{
			using (ManualResetEvent ended = new ManualResetEvent(false))
			{
				TaskEndedEventArgs completion = null;
				purge.TaskEnded += (sender, args) => { completion = args; ended.Set(); };
				purge.Update(null);
				Assert.That(ended.WaitOne(3000), Is.True, "Purge did not signal completion to its progress dialog.");
				Assert.That(completion.Status, Is.EqualTo(expected));
				Assert.That(purge.Status, Is.EqualTo(expected));
			}
		}

		/// <summary>Registers queued downloads whose cancellation has no persisted or network effects.</summary>
		private static List<ControlledDownload> CreateDownloads(DownloadMonitor monitor, int count)
		{
			var downloads = new List<ControlledDownload>();
			for (int index = 0; index < count; index++)
			{
				var download = new ControlledDownload();
				downloads.Add(download);
				monitor.AddActivity(download);
			}
			return downloads;
		}

		/// <summary>Allows a cancellation request at the exact per-download cleanup boundary.</summary>
		private sealed class ControlledDownload : AddModTask
		{
			internal Action OnCancel;
			internal int CancelCount;

			/// <summary>Creates a queued task without accessing real game paths or settings.</summary>
			internal ControlledDownload()
				: base(null, null, null, null, null, null, new Uri("https://example.invalid/" + Guid.NewGuid().ToString("N") + ".zip"), null)
			{
				Status = TaskStatus.Queued;
			}

			/// <summary>Records cleanup and invokes the test's cancellation boundary callback.</summary>
			public override void Cancel()
			{
				CancelCount++;
				OnCancel?.Invoke();
				Status = TaskStatus.Cancelled;
			}
		}
	}
}
