using System;
using System.IO;
using System.Threading;
using Nexus.Client.ModActivationMonitoring;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>
    /// Verifies that native mod-operation submission and serialized start no longer depend on the monitor UI control.
    /// </summary>
    [TestFixture]
    public class ModOperationSubmissionTests
    {
        /// <summary>
        /// A submitted installer starts even when no activation-monitor control exists.
        /// </summary>
        [Test]
        public void Submit_StartsWithoutUiControl()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new BlockingSubmissionMod("Standalone submission", archive);
                var installer = new SubmissionTestInstaller(mod);
                var monitor = new ModActivationMonitor();
                using (var completed = new ManualResetEventSlim(false))
                {
                    installer.TaskSetCompleted += (sender, args) => completed.Set();

                    Assert.That(monitor.Submit(installer), Is.True);
                    Assert.That(mod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                        "The monitor did not start the submitted installer without a UI observer.");
                    Assert.That(monitor.RunningTask, Is.SameAs(installer));
                    Assert.That(installer.IsQueued, Is.False);

                    mod.AllowPreparationToContinue.Set();
                    Assert.That(completed.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(SpinWait.SpinUntil(() => monitor.RunningTask == null, TimeSpan.FromSeconds(5)), Is.True,
                        "The monitor did not release the running slot after completion.");
                }
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// The second accepted task remains queued until the first task reaches its terminal notification.
        /// </summary>
        [Test]
        public void Submit_QueuesSecondTask_AndStartsItAfterFirstCompletes()
        {
            string firstArchive = Path.GetTempFileName();
            string secondArchive = Path.GetTempFileName();
            try
            {
                var firstMod = new BlockingSubmissionMod("First", firstArchive);
                var secondMod = new BlockingSubmissionMod("Second", secondArchive);
                var first = new SubmissionTestInstaller(firstMod);
                var second = new SubmissionTestInstaller(secondMod);
                var monitor = new ModActivationMonitor();
                using (var secondCompleted = new ManualResetEventSlim(false))
                {
                    second.TaskSetCompleted += (sender, args) => secondCompleted.Set();

                    Assert.That(monitor.Submit(first), Is.True);
                    Assert.That(firstMod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(monitor.Submit(second), Is.True);
                    Assert.That(second.IsQueued, Is.True);
                    Assert.That(secondMod.PreparationEntered.Wait(TimeSpan.FromMilliseconds(250)), Is.False,
                        "The queued task started before the current task completed.");

                    firstMod.AllowPreparationToContinue.Set();
                    Assert.That(secondMod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                        "The next queued task was not started after the first task completed.");
                    Assert.That(second.IsQueued, Is.False);
                    Assert.That(monitor.RunningTask, Is.SameAs(second));

                    secondMod.AllowPreparationToContinue.Set();
                    Assert.That(secondCompleted.Wait(TimeSpan.FromSeconds(5)), Is.True);
                }
            }
            finally
            {
                File.Delete(firstArchive);
                File.Delete(secondArchive);
            }
        }

        /// <summary>
        /// Preparation cancellation is terminal and releases the serialized submission queue.
        /// </summary>
        [Test]
        public void Submit_PreparationCancellation_StartsNextQueuedTask()
        {
            string firstArchive = Path.GetTempFileName();
            string secondArchive = Path.GetTempFileName();
            try
            {
                var firstMod = new BlockingSubmissionMod("Cancelled first", firstArchive);
                var secondMod = new BlockingSubmissionMod("Second after cancellation", secondArchive);
                var first = new SubmissionTestInstaller(firstMod);
                var second = new SubmissionTestInstaller(secondMod);
                var monitor = new ModActivationMonitor();
                PrepareModTask prepareTask = null;
                using (var prepareObserved = new ManualResetEventSlim(false))
                {
                    first.TaskStarted += (sender, args) =>
                    {
                        PrepareModTask observed = args.Argument as PrepareModTask;
                        if (observed == null)
                            return;

                        prepareTask = observed;
                        prepareObserved.Set();
                    };

                    Assert.That(monitor.Submit(first), Is.True);
                    Assert.That(firstMod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(prepareObserved.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(monitor.Submit(second), Is.True);
                    Assert.That(second.IsQueued, Is.True);

                    prepareTask.Cancel();
                    firstMod.AllowPreparationToContinue.Set();

                    Assert.That(secondMod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                        "Preparation cancellation left the submission queue blocked.");
                    Assert.That(first.IsCompleted, Is.True);
                    Assert.That(first.Succeeded, Is.False);
                    Assert.That(monitor.RunningTask, Is.SameAs(second));

                    secondMod.AllowPreparationToContinue.Set();
                    Assert.That(SpinWait.SpinUntil(() => second.IsCompleted, TimeSpan.FromSeconds(5)), Is.True);
                }
            }
            finally
            {
                File.Delete(firstArchive);
                File.Delete(secondArchive);
            }
        }

        /// <summary>
        /// Re-submitting the same task object cannot start it a second time.
        /// </summary>
        [Test]
        public void Submit_SameTaskTwice_IsAcceptedOnlyOnce()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new BlockingSubmissionMod("Same task", archive);
                var installer = new SubmissionTestInstaller(mod);
                var monitor = new ModActivationMonitor();

                Assert.That(monitor.Submit(installer), Is.True);
                Assert.That(mod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(monitor.Submit(installer), Is.False);
                Assert.That(monitor.Tasks.Count, Is.EqualTo(1));

                mod.AllowPreparationToContinue.Set();
                Assert.That(SpinWait.SpinUntil(() => installer.IsCompleted, TimeSpan.FromSeconds(5)), Is.True);
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// C3.3 preserves the legacy filename duplicate rule while moving ownership out of the UI control.
        /// </summary>
        [Test]
        public void Submit_LegacyFilenameDuplicateWhileRunning_IsRejectedBeforePublication()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var firstMod = new BlockingSubmissionMod("First", archive);
                var duplicateMod = new BlockingSubmissionMod("Duplicate", archive);
                var first = new SubmissionTestInstaller(firstMod);
                var duplicate = new SubmissionTestInstaller(duplicateMod);
                var monitor = new ModActivationMonitor();

                Assert.That(monitor.Submit(first), Is.True);
                Assert.That(firstMod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(monitor.Submit(duplicate), Is.False);
                Assert.That(monitor.Tasks.Count, Is.EqualTo(1));
                Assert.That(duplicate.IsQueued, Is.False);
                Assert.That(duplicateMod.PreparationEntered.Wait(TimeSpan.FromMilliseconds(250)), Is.False);

                firstMod.AllowPreparationToContinue.Set();
                Assert.That(SpinWait.SpinUntil(() => first.IsCompleted, TimeSpan.FromSeconds(5)), Is.True);
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// Removing a queued operation removes it from both the visible monitor and the execution queue.
        /// </summary>
        [Test]
        public void RemoveQueuedTask_PreventsLaterStart()
        {
            string firstArchive = Path.GetTempFileName();
            string secondArchive = Path.GetTempFileName();
            try
            {
                var firstMod = new BlockingSubmissionMod("First", firstArchive);
                var secondMod = new BlockingSubmissionMod("Second", secondArchive);
                var first = new SubmissionTestInstaller(firstMod);
                var second = new SubmissionTestInstaller(secondMod);
                var monitor = new ModActivationMonitor();

                Assert.That(monitor.Submit(first), Is.True);
                Assert.That(firstMod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(monitor.Submit(second), Is.True);
                Assert.That(second.IsQueued, Is.True);

                monitor.RemoveQueuedTask(second);
                Assert.That(second.IsQueued, Is.False);
                Assert.That(monitor.Tasks.Count, Is.EqualTo(1));

                firstMod.AllowPreparationToContinue.Set();
                Assert.That(SpinWait.SpinUntil(() => first.IsCompleted, TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(secondMod.PreparationEntered.Wait(TimeSpan.FromMilliseconds(500)), Is.False,
                    "A task removed from the submission queue was started later.");
            }
            finally
            {
                File.Delete(firstArchive);
                File.Delete(secondArchive);
            }
        }

        /// <summary>
        /// Anonymous native tasks are rejected before publication or worker start.
        /// </summary>
        [Test]
        public void Submit_WithoutOperationIdentity_IsRejectedBeforePublication()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new BlockingSubmissionMod("Anonymous", archive);
                var installer = new SubmissionTestInstaller(mod, false, ModOperationOrigin.Manual);
                var monitor = new ModActivationMonitor();

                Assert.Throws<InvalidOperationException>(() => monitor.Submit(installer));
                Assert.That(monitor.Tasks.Count, Is.EqualTo(0));
                Assert.That(monitor.RunningTask, Is.Null);
                Assert.That(mod.PreparationEntered.Wait(TimeSpan.FromMilliseconds(250)), Is.False);
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// The submission seam preserves a caller-assigned origin and identity instead of rewriting it.
        /// </summary>
        [Test]
        public void Submit_PreservesAssignedOperationIdentityAndOrigin()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new BlockingSubmissionMod("Collection-origin", archive);
                var installer = new SubmissionTestInstaller(mod, true, ModOperationOrigin.Collection);
                ModOperationIdentity identity = installer.OperationIdentity;
                var monitor = new ModActivationMonitor();

                Assert.That(monitor.Submit(installer), Is.True);
                Assert.That(mod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(installer.OperationIdentity, Is.SameAs(identity));
                Assert.That(installer.OperationIdentity.Origin, Is.EqualTo(ModOperationOrigin.Collection));

                mod.AllowPreparationToContinue.Set();
                Assert.That(SpinWait.SpinUntil(() => installer.IsCompleted, TimeSpan.FromSeconds(5)), Is.True);
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// The C6 submission seam invokes its durable acceptance callback before native worker start.
        /// </summary>
        [Test]
        public void SubmitWhenIdle_CallbackRunsBeforeWorkerStart()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new BlockingSubmissionMod("Exclusive", archive);
                var installer = new SubmissionTestInstaller(mod, true, ModOperationOrigin.Collection);
                var monitor = new ModActivationMonitor();
                bool callbackObserved = false;

                Assert.That(monitor.SubmitWhenIdle(installer, () => callbackObserved = true), Is.True);
                Assert.That(callbackObserved, Is.True);
                Assert.That(mod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

                mod.AllowPreparationToContinue.Set();
                Assert.That(SpinWait.SpinUntil(() => installer.IsCompleted, TimeSpan.FromSeconds(5)), Is.True);
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// The C6 seam never queues behind existing native work and therefore never invokes its durable callback while busy.
        /// </summary>
        [Test]
        public void SubmitWhenIdle_BusyLaneRejectsWithoutCallbackOrStart()
        {
            string firstArchive = Path.GetTempFileName();
            string secondArchive = Path.GetTempFileName();
            try
            {
                var firstMod = new BlockingSubmissionMod("First", firstArchive);
                var secondMod = new BlockingSubmissionMod("Second", secondArchive);
                var first = new SubmissionTestInstaller(firstMod);
                var second = new SubmissionTestInstaller(secondMod, true, ModOperationOrigin.Collection);
                var monitor = new ModActivationMonitor();
                bool callbackObserved = false;

                Assert.That(monitor.Submit(first), Is.True);
                Assert.That(firstMod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(monitor.SubmitWhenIdle(second, () => callbackObserved = true), Is.False);
                Assert.That(callbackObserved, Is.False);
                Assert.That(secondMod.PreparationEntered.Wait(TimeSpan.FromMilliseconds(250)), Is.False);
                Assert.That(monitor.Tasks.Count, Is.EqualTo(1));

                firstMod.AllowPreparationToContinue.Set();
                Assert.That(SpinWait.SpinUntil(() => first.IsCompleted, TimeSpan.FromSeconds(5)), Is.True);
            }
            finally
            {
                File.Delete(firstArchive);
                File.Delete(secondArchive);
            }
        }

        /// <summary>
        /// A failed durable-before-start callback unpublishes the task without ever entering native preparation.
        /// </summary>
        [Test]
        public void SubmitWhenIdle_CallbackFailureDoesNotStartNativeTask()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new BlockingSubmissionMod("Callback failure", archive);
                var installer = new SubmissionTestInstaller(mod, true, ModOperationOrigin.Collection);
                var monitor = new ModActivationMonitor();

                Assert.Throws<InvalidOperationException>(() => monitor.SubmitWhenIdle(installer, () =>
                {
                    throw new InvalidOperationException("durable callback failed");
                }));

                Assert.That(mod.PreparationEntered.Wait(TimeSpan.FromMilliseconds(250)), Is.False);
                Assert.That(monitor.RunningTask, Is.Null);
                Assert.That(monitor.Tasks.Count, Is.EqualTo(0));
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// A native task cannot be relabelled with a different logical operation after its identity is assigned.
        /// </summary>
        [Test]
        public void OperationIdentity_CannotBeAssignedTwice()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new BlockingSubmissionMod("Identity", archive);
                var installer = new SubmissionTestInstaller(mod);
                var replacement = ModOperationIdentity.CreateNew(ModOperationOrigin.Recovery, CreateTestFingerprint());

                Assert.Throws<InvalidOperationException>(() => installer.ReplaceIdentityForTest(replacement));
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// Installer used to exercise the real ModInstaller.Install worker start without game-specific dependencies.
        /// </summary>
        private sealed class SubmissionTestInstaller : ModInstaller
        {
            public SubmissionTestInstaller(IMod p_modMod)
                : this(p_modMod, true, ModOperationOrigin.Manual)
            {
            }

            public SubmissionTestInstaller(IMod p_modMod, bool p_booAssignIdentity, ModOperationOrigin p_mooOrigin)
                : base(p_modMod, null, null, null, null, null, null, null, null, null, null)
            {
                if (p_booAssignIdentity)
                    AssignOperationIdentity(ModOperationIdentity.CreateNew(p_mooOrigin, CreateTestFingerprint()));
            }

            public void ReplaceIdentityForTest(ModOperationIdentity p_moiIdentity)
            {
                AssignOperationIdentity(p_moiIdentity);
            }
        }

        private static ModOperationFingerprint CreateTestFingerprint()
        {
            return new ModOperationFingerprint("test-target",
                new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Default), null);
        }

        /// <summary>
        /// Holds native preparation at a deterministic point so queue ordering can be observed.
        /// </summary>
        private sealed class BlockingSubmissionMod : InstallLog.DummyMod, IMod
        {
            public BlockingSubmissionMod(string p_strName, string p_strFileName)
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
                    throw new TimeoutException("The submission test did not release mod preparation.");

                base.BeginReadOnlyTransaction(p_futFileUtility);
            }
        }
    }
}
