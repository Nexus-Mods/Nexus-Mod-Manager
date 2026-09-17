using System;
using System.IO;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>
    /// Characterizes the native installer task lifecycle before the Collections operation-submission seam is introduced.
    /// </summary>
    [TestFixture]
    public class ModOperationLifecycleCharacterizationTests
    {
        /// <summary>
        /// Verifies that an ordinary handled installer failure reports one terminal task-set completion.
        /// </summary>
        [Test]
        public void ModInstaller_HandledFailure_CompletesTaskSet()
        {
            string missingArchive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".7z");
            var mod = new InstallLog.DummyMod("Missing archive", missingArchive);
            var installer = new LifecycleCharacterizationInstaller(mod);
            using (var completed = new ManualResetEventSlim(false))
            {
                int completionCount = 0;
                installer.TaskSetCompleted += (sender, args) =>
                {
                    Interlocked.Increment(ref completionCount);
                    completed.Set();
                };

                installer.RunSynchronously();

                Assert.That(installer.IsCompleted, Is.True);
                Assert.That(installer.Succeeded, Is.False);
                Assert.That(completed.Wait(TimeSpan.FromSeconds(5)), Is.True, "The asynchronous completion event was not delivered.");
                Assert.That(Volatile.Read(ref completionCount), Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Verifies the current preparation-cancellation hole: RunTasks returns before task-set completion is published.
        /// </summary>
        [Test]
        public void ModInstaller_PreparationCancellation_ReturnsWithoutTaskSetCompletion()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new BlockingPreparationMod("Cancelled preparation", archive);
                var installer = new LifecycleCharacterizationInstaller(mod);
                using (var completed = new ManualResetEventSlim(false))
                {
                    installer.TaskStarted += (sender, args) =>
                    {
                        var prepareTask = args.Argument as PrepareModTask;
                        if (prepareTask == null)
                            return;

                        prepareTask.Cancel();
                        mod.AllowPreparationToContinue.Set();
                    };
                    installer.TaskSetCompleted += (sender, args) => completed.Set();

                    System.Threading.Tasks.Task run = System.Threading.Tasks.Task.Run(() => installer.RunSynchronously());
                    try
                    {
                        Assert.That(mod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True, "Preparation did not reach the controlled cancellation point.");
                        Assert.That(run.Wait(TimeSpan.FromSeconds(5)), Is.True, "RunTasks did not return after preparation cancellation.");
                    }
                    finally
                    {
                        mod.AllowPreparationToContinue.Set();
                    }

                    Assert.That(installer.IsCompleted, Is.False);
                    Assert.That(completed.Wait(TimeSpan.FromMilliseconds(250)), Is.False,
                        "Current behavior unexpectedly published task-set completion after the preparation cancellation path.");
                }
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// Verifies the current rethrow hole: selected terminal exceptions escape RunTasks without task-set completion.
        /// </summary>
        [Test]
        public void ModInstaller_RethrownPreparationException_LeavesTaskSetIncomplete()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new ThrowingPreparationMod("Throwing preparation", archive);
                var installer = new LifecycleCharacterizationInstaller(mod);
                using (var completed = new ManualResetEventSlim(false))
                {
                    installer.TaskSetCompleted += (sender, args) => completed.Set();

                    Assert.Throws<ObjectDisposedException>(() => installer.RunSynchronously());

                    Assert.That(installer.IsCompleted, Is.False);
                    Assert.That(completed.Wait(TimeSpan.FromMilliseconds(250)), Is.False,
                        "Current behavior unexpectedly published task-set completion after a rethrown exception.");
                }
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// Verifies that task-set completion is an event notification, not retained/replayed completion data for late subscribers.
        /// </summary>
        [Test]
        public void ModInstallerBase_LateCompletionSubscriber_DoesNotReceivePriorCompletion()
        {
            var taskSet = new LifecycleProbeTaskSet();
            using (var firstSubscriber = new ManualResetEventSlim(false))
            using (var lateSubscriber = new ManualResetEventSlim(false))
            {
                taskSet.TaskSetCompleted += (sender, args) => firstSubscriber.Set();

                taskSet.Complete();

                Assert.That(taskSet.IsCompleted, Is.True);
                Assert.That(firstSubscriber.Wait(TimeSpan.FromSeconds(5)), Is.True, "Initial completion notification was not delivered.");

                taskSet.TaskSetCompleted += (sender, args) => lateSubscriber.Set();

                Assert.That(lateSubscriber.Wait(TimeSpan.FromMilliseconds(250)), Is.False,
                    "Completion events are not expected to be replayed to subscribers attached after delivery.");
            }
        }

        /// <summary>
        /// Exposes synchronous execution of ModInstaller.RunTasks for deterministic characterization.
        /// </summary>
        private sealed class LifecycleCharacterizationInstaller : ModInstaller
        {
            public LifecycleCharacterizationInstaller(IMod p_modMod)
                : base(p_modMod, null, null, null, null, null, null, null, null, null, null)
            {
            }

            public void RunSynchronously()
            {
                RunTasks();
            }
        }

        /// <summary>
        /// Blocks preparation until the test has cancelled the PrepareModTask, then uses the normal dummy-mod initialization path.
        /// </summary>
        private sealed class BlockingPreparationMod : InstallLog.DummyMod, IMod
        {
            public BlockingPreparationMod(string p_strName, string p_strFileName)
                : base(p_strName, p_strFileName)
            {
                PreparationEntered = new ManualResetEventSlim(false);
                AllowPreparationToContinue = new ManualResetEventSlim(false);
            }

            public ManualResetEventSlim PreparationEntered { get; }

            public ManualResetEventSlim AllowPreparationToContinue { get; }

            void IMod.BeginReadOnlyTransaction(FileUtil p_futFileUtility)
            {
                PreparationEntered.Set();
                if (!AllowPreparationToContinue.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The characterization test did not release mod preparation.");

                base.BeginReadOnlyTransaction(p_futFileUtility);
            }
        }

        /// <summary>
        /// Throws one of the exception types ModInstaller.RunTasks deliberately rethrows.
        /// </summary>
        private sealed class ThrowingPreparationMod : InstallLog.DummyMod, IMod
        {
            public ThrowingPreparationMod(string p_strName, string p_strFileName)
                : base(p_strName, p_strFileName)
            {
            }

            void IMod.BeginReadOnlyTransaction(FileUtil p_futFileUtility)
            {
                throw new ObjectDisposedException("characterization");
            }
        }

        /// <summary>
        /// Minimal task set used to characterize completion-event delivery independently from installer execution.
        /// </summary>
        private sealed class LifecycleProbeTaskSet : ModInstallerBase
        {
            public void Complete()
            {
                OnTaskSetCompleted(false, "characterization", null);
            }
        }
    }
}
