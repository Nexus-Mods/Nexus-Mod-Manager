using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading;
using Nexus.Client;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Covers duplicate queue starts and disappearing download sources without network or native-store access.
	/// </summary>
	[TestFixture]
	public class AddModTaskConcurrencyTests
	{
		/// <summary>
		/// Two scheduler requests must share one start, while a later intentional retry remains possible.
		/// </summary>
		[Test]
		public void Resume_ConcurrentAndStaleRequests_StartOnlyOnce()
		{
			using (ManualResetEvent entered = new ManualResetEvent(false))
			using (ManualResetEvent release = new ManualResetEvent(false))
			{
				ControlledAddModTask task = new ControlledAddModTask(entered, release);
				Exception workerError = null;
				Thread worker = new Thread(() =>
				{
					try { task.Resume(); }
					catch (Exception ex) { workerError = ex; }
				});
				try
				{
					worker.Start();
					Assert.IsTrue(entered.WaitOne(3000), "The first resume did not claim the task.");
					Assert.DoesNotThrow(() => task.Resume());
				}
				finally
				{
					release.Set();
					Assert.IsTrue(worker.Join(3000), "The resume worker did not finish.");
				}

				Assert.IsNull(workerError);
				Assert.AreEqual(1, task.StartCount);
				Assert.AreEqual(1, task.EndCount);
				Assert.AreEqual(TaskStatus.Error, task.Status);
				Assert.DoesNotThrow(() => task.Resume());
				Assert.AreEqual(1, task.StartCount, "A stale request restarted a terminal task.");

				task.SetPaused();
				task.Resume();
				Assert.AreEqual(2, task.StartCount, "The start claim was not released for a later retry.");
				task.Dispose();
			}
		}

		/// <summary>
		/// A duplicate child completion must not process the same download result twice.
		/// </summary>
		[Test]
		public void DownloaderTaskEnded_RepeatedCallback_ProcessesResultOnce()
		{
			ControlledAddModTask task = new ControlledAddModTask(null, null);
			BackgroundTask child = new BackgroundTask();
			List<IBackgroundTask> children = (List<IBackgroundTask>)typeof(AddModTask)
				.GetField("_runningTasks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(task);
			children.Add(child);
			MethodInfo callback = typeof(AddModTask).GetMethod("Downloader_TaskEnded", BindingFlags.Instance | BindingFlags.NonPublic);
			TaskEndedEventArgs args = new TaskEndedEventArgs(TaskStatus.Queued, null, new[] { "download.partial" });

			callback.Invoke(task, new object[] { child, args });
			callback.Invoke(task, new object[] { child, args });

			Assert.AreEqual(1, task.PausedFileCount);
			Assert.IsEmpty(children);
			task.Dispose();
		}

		/// <summary>
		/// Losing a ZIP between the caller's existence check and the builder is a task error, not an application crash.
		/// </summary>
		[Test]
		public void BuildFromFile_MissingDownload_ReportsOneTaskErrorWithoutThrowing()
		{
			string missing = Path.Combine(Path.GetTempPath(), "NMM_missing_" + Guid.NewGuid().ToString("N") + ".zip");
			using (ModBuilder builder = new ModBuilder(null, null, null))
			{
				int ended = 0;
				TaskEndedEventArgs result = null;
				builder.TaskEnded += (sender, args) => { ended++; result = args; };

				Assert.DoesNotThrow(() => builder.BuildFromFile(null, missing, null, true));

				Assert.AreEqual(1, ended);
				Assert.AreEqual(TaskStatus.Error, result.Status);
				StringAssert.Contains(missing, result.Message);
			}
		}

		/// <summary>Archive-named legacy directories cannot become file destinations or be removed during import.</summary>
		[Test]
		public void ConfirmOverwrite_DirectoryCollision_SkipsOccupiedNamesAndPreservesExistingContent()
		{
			using (TemporaryDirectory temporary = new TemporaryDirectory())
			using (ModBuilder builder = new ModBuilder(null, null, null))
			{
				string blocked = Path.Combine(temporary.Path, "mod.7z");
				Directory.CreateDirectory(blocked);
				string original = Path.Combine(blocked, "mod.7z");
				File.WriteAllText(original, "existing download");
				string occupiedFile = Path.Combine(temporary.Path, "mod (2).7z");
				File.WriteAllText(occupiedFile, "another archive");
				Directory.CreateDirectory(Path.Combine(temporary.Path, "mod (3).7z"));
				string confirmedPath = null;
				ConfirmOverwriteCallback confirm = (string path, out string selected) => { confirmedPath = path; selected = path; return true; };
				MethodInfo method = typeof(ModBuilder).GetMethod("ConfirmOverwrite", BindingFlags.Instance | BindingFlags.NonPublic);
				string destination = (string)method.Invoke(builder, new object[] { confirm, blocked });
				Assert.AreEqual(Path.Combine(temporary.Path, "mod (4).7z"), destination);
				Assert.AreEqual(destination, confirmedPath);
				Assert.AreEqual("existing download", File.ReadAllText(original));
				Assert.AreEqual("another archive", File.ReadAllText(occupiedFile));
				Assert.IsTrue(Directory.Exists(Path.Combine(temporary.Path, "mod (3).7z")));
			}
		}

		/// <summary>Ordinary archive collisions still reach the existing overwrite callback with the original path.</summary>
		[Test]
		public void ConfirmOverwrite_ExistingFile_PreservesCallbackDecision()
		{
			using (TemporaryDirectory temporary = new TemporaryDirectory())
			using (ModBuilder builder = new ModBuilder(null, null, null))
			{
				string existing = Path.Combine(temporary.Path, "mod.7z");
				File.WriteAllText(existing, "existing archive");
				ConfirmOverwriteCallback confirm = (string path, out string selected) => { Assert.AreEqual(existing, path); selected = null; return false; };
				MethodInfo method = typeof(ModBuilder).GetMethod("ConfirmOverwrite", BindingFlags.Instance | BindingFlags.NonPublic);
				Assert.IsNull(method.Invoke(builder, new object[] { confirm, existing }));
				Assert.AreEqual("existing archive", File.ReadAllText(existing));
			}
		}

		/// <summary>
		/// Retains the real Resume and import preflight while isolating persisted settings and UI observers.
		/// </summary>
		private sealed class ControlledAddModTask : AddModTask
		{
			private readonly ManualResetEvent _entered;
			private readonly ManualResetEvent _release;
			private int _starts;
			private int _ends;

			/// <summary>
			/// Supplies an already resolved descriptor whose absent source finishes before archive or game access.
			/// </summary>
			public ControlledAddModTask(ManualResetEvent entered, ManualResetEvent release)
				: base(null, null, null, null, null, null, new Uri("file:///C:/NMM-missing-" + Guid.NewGuid().ToString("N") + ".zip"), null)
			{
				_entered = entered;
				_release = release;
				Uri source = new Uri(SourceUri);
				AddModDescriptor descriptor = new AddModDescriptor(source, source.LocalPath, null, TaskStatus.Running, new List<string>(), "Missing archive", "missing.zip");
				typeof(AddModTask).GetProperty("Descriptor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(this, descriptor, null);
				typeof(AddModTask).GetProperty("ModInfo").SetValue(this, new ModInfo { FileName = "missing.zip" }, null);
				Status = TaskStatus.Paused;
			}

			public int StartCount => Volatile.Read(ref _starts);
			public int EndCount => Volatile.Read(ref _ends);
			public int PausedFileCount => Descriptor.PausedFiles.Count;

			/// <summary>
			/// Simulates a later intentional retry after the previous start has ended.
			/// </summary>
			public void SetPaused()
			{
				Status = TaskStatus.Paused;
			}

			/// <summary>
			/// Blocks the first Running notification so a second scheduler can race the native Resume entry point.
			/// </summary>
			protected override void OnPropertyChanged(PropertyChangedEventArgs e)
			{
				if (e.PropertyName == "Status" && Status == TaskStatus.Running)
				{
					Interlocked.Increment(ref _starts);
					_entered?.Set();
					if (_release != null && !_release.WaitOne(3000))
						throw new TimeoutException("The test did not release the task start.");
				}
			}

			/// <summary>
			/// Counts results without touching user settings or download cleanup.
			/// </summary>
			protected override void OnTaskEnded(TaskEndedEventArgs e)
			{
				Interlocked.Increment(ref _ends);
			}
		}
	}
}
