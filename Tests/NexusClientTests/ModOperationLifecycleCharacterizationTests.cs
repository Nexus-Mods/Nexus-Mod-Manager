using System;
using System.IO;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>
    /// Verifies native installer terminal lifecycle behavior used by the shared operation-submission seam.
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
            installer.AssignIdentity();
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
                Assert.That(installer.OperationResult.ReportedStatus, Is.EqualTo(ModOperationReportedStatus.Failed));
                Assert.That(installer.OperationResult.Durability, Is.EqualTo(ModOperationDurability.NotStarted));
                Assert.That(completed.Wait(TimeSpan.FromSeconds(5)), Is.True, "The asynchronous completion event was not delivered.");
                Assert.That(Volatile.Read(ref completionCount), Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Verifies that preparation cancellation now publishes one terminal task-set completion before RunTasks returns.
        /// </summary>
        [Test]
        public void ModInstaller_PreparationCancellation_CompletesTaskSet()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new BlockingPreparationMod("Cancelled preparation", archive);
                var installer = new LifecycleCharacterizationInstaller(mod);
                installer.AssignIdentity();
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

                    Assert.That(installer.IsCompleted, Is.True);
                    Assert.That(installer.Succeeded, Is.False);
                    Assert.That(installer.CompletionMessage, Is.EqualTo("The mod activation was cancelled."));
                    Assert.That(installer.OperationResult.ReportedStatus, Is.EqualTo(ModOperationReportedStatus.Cancelled));
                    Assert.That(installer.OperationResult.Durability, Is.EqualTo(ModOperationDurability.NotStarted));
                    Assert.That(completed.Wait(TimeSpan.FromSeconds(5)), Is.True,
                        "Preparation cancellation did not publish terminal task-set completion.");
                }
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// Verifies that selected exceptions still escape RunTasks but first mark the task set terminal.
        /// </summary>
        [Test]
        public void ModInstaller_RethrownPreparationException_CompletesTaskSetBeforeRethrow()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new ThrowingPreparationMod("Throwing preparation", archive);
                var installer = new LifecycleCharacterizationInstaller(mod);
                installer.AssignIdentity();
                using (var completed = new ManualResetEventSlim(false))
                {
                    installer.TaskSetCompleted += (sender, args) => completed.Set();

                    Assert.Throws<ObjectDisposedException>(() => installer.RunSynchronously());

                    Assert.That(installer.IsCompleted, Is.True);
                    Assert.That(installer.Succeeded, Is.False);
                    Assert.That(installer.OperationResult.ReportedStatus, Is.EqualTo(ModOperationReportedStatus.Failed));
                    Assert.That(installer.OperationResult.Durability, Is.EqualTo(ModOperationDurability.NotStarted));
                    Assert.That(completed.Wait(TimeSpan.FromSeconds(5)), Is.True,
                        "The rethrown preparation exception did not publish terminal task-set completion.");
                }
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// Verifies that a cleanup exception cannot bypass terminal task-set completion.
        /// </summary>
        [Test]
        public void ModInstaller_CleanupException_CompletesTaskSetBeforeRethrow()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new ThrowingCleanupMod("Throwing cleanup", archive);
                var installer = new LifecycleCharacterizationInstaller(mod);
                installer.AssignIdentity();
                using (var completed = new ManualResetEventSlim(false))
                {
                    installer.TaskSetCompleted += (sender, args) => completed.Set();

                    InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => installer.RunSynchronously());

                    Assert.That(exception.Message, Is.EqualTo("characterization cleanup"));
                    Assert.That(installer.IsCompleted, Is.True);
                    Assert.That(installer.Succeeded, Is.False);
                    Assert.That(installer.OperationResult.ReportedStatus, Is.EqualTo(ModOperationReportedStatus.Failed));
                    Assert.That(installer.OperationResult.Durability, Is.EqualTo(ModOperationDurability.Unknown));
                    Assert.That(completed.Wait(TimeSpan.FromSeconds(5)), Is.True,
                        "The cleanup exception bypassed terminal task-set completion.");
                }
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// Verifies that live replay XML and payloads remain intact while native preparation is still in progress.
        /// </summary>
        [Test]
        public void ModInstaller_ReplayArtifacts_AreDeletedOnlyAfterPreparationSucceeds()
        {
            string root = Path.Combine(Path.GetTempPath(), "NMM-C3-Replay-" + Guid.NewGuid().ToString("N"));
            string archive = Path.Combine(root, "Replay.7z");
            Directory.CreateDirectory(root);
            File.WriteAllText(archive, "archive");

            try
            {
                IGameMode gameMode = CreateGameMode(Path.Combine(root, "InstallInfo"));
                var mod = new BlockingPreparationMod("Replay preparation", archive);
                var cache = new ScriptedFileSelectionCache(mod, gameMode);
                CreateReplayArtifacts(cache.FilePath);
                string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(cache.FilePath);
                var installer = new LifecycleCharacterizationInstaller(mod, gameMode);
                Assert.That(File.Exists(cache.FilePath), Is.True, "Constructing the installer deleted the existing replay XML.");
                Assert.That(Directory.Exists(payloadDirectory), Is.True, "Constructing the installer deleted generated replay payloads.");

                System.Threading.Tasks.Task run = System.Threading.Tasks.Task.Run(() => installer.RunSynchronously());
                try
                {
                    Assert.That(mod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(File.Exists(cache.FilePath), Is.True, "Replay XML was deleted before preparation completed.");
                    Assert.That(Directory.Exists(payloadDirectory), Is.True, "Replay payloads were deleted before preparation completed.");

                    mod.AllowPreparationToContinue.Set();
                    Assert.That(run.Wait(TimeSpan.FromSeconds(5)), Is.True);

                    Assert.That(File.Exists(cache.FilePath), Is.False, "Replay XML survived after successful preparation entered the install boundary.");
                    Assert.That(Directory.Exists(payloadDirectory), Is.False, "Replay payloads survived after successful preparation entered the install boundary.");
                }
                finally
                {
                    mod.AllowPreparationToContinue.Set();
                }
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// Verifies that cancelling native preparation leaves the previous replay XML and generated payloads untouched.
        /// </summary>
        [Test]
        public void ModInstaller_PreparationCancellation_PreservesReplayArtifacts()
        {
            string root = Path.Combine(Path.GetTempPath(), "NMM-C3-ReplayCancel-" + Guid.NewGuid().ToString("N"));
            string archive = Path.Combine(root, "Replay.7z");
            Directory.CreateDirectory(root);
            File.WriteAllText(archive, "archive");

            try
            {
                IGameMode gameMode = CreateGameMode(Path.Combine(root, "InstallInfo"));
                var mod = new BlockingPreparationMod("Cancelled replay preparation", archive);
                var cache = new ScriptedFileSelectionCache(mod, gameMode);
                CreateReplayArtifacts(cache.FilePath);
                string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(cache.FilePath);
                var installer = new LifecycleCharacterizationInstaller(mod, gameMode);
                PrepareModTask prepareTask = null;
                using (var prepareObserved = new ManualResetEventSlim(false))
                {
                    installer.TaskStarted += (sender, args) =>
                    {
                        PrepareModTask observed = args.Argument as PrepareModTask;
                        if (observed == null)
                            return;

                        prepareTask = observed;
                        prepareObserved.Set();
                    };

                    System.Threading.Tasks.Task run = System.Threading.Tasks.Task.Run(() => installer.RunSynchronously());
                    try
                    {
                        Assert.That(mod.PreparationEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                        Assert.That(prepareObserved.Wait(TimeSpan.FromSeconds(5)), Is.True);
                        prepareTask.Cancel();
                        mod.AllowPreparationToContinue.Set();
                        Assert.That(run.Wait(TimeSpan.FromSeconds(5)), Is.True);

                        Assert.That(File.Exists(cache.FilePath), Is.True, "Cancelled preparation deleted the previous replay XML.");
                        Assert.That(Directory.Exists(payloadDirectory), Is.True, "Cancelled preparation deleted generated replay payloads.");
                    }
                    finally
                    {
                        mod.AllowPreparationToContinue.Set();
                    }
                }
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// Verifies that uninstalling an already inactive/unmanaged mod is an explicit no-op rather than a fake committed mutation.
        /// </summary>
        [Test]
        public void ModUninstaller_AlreadyInactive_ReportsNoOpNotStarted()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new InstallLog.DummyMod("Inactive", archive);
                var activeMods = new ThreadSafeObservableList<IMod>();
                var readOnlyActiveMods = new ReadOnlyObservableList<IMod>(activeMods);
                IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
                {
                    if (method.Name == "get_ActiveMods")
                        return readOnlyActiveMods;
                    return null;
                });
                var uninstaller = new LifecycleCharacterizationUninstaller(mod, installLog, readOnlyActiveMods);
                uninstaller.AssignIdentity();

                uninstaller.Install();

                Assert.That(uninstaller.IsCompleted, Is.True);
                Assert.That(uninstaller.Succeeded, Is.True);
                Assert.That(uninstaller.OperationResult.ReportedStatus, Is.EqualTo(ModOperationReportedStatus.NoOp));
                Assert.That(uninstaller.OperationResult.Durability, Is.EqualTo(ModOperationDurability.NotStarted));
            }
            finally
            {
                File.Delete(archive);
            }
        }

        /// <summary>
        /// Verifies that a failure after native uninstall state changed does not get mistaken for a rollback.
        /// </summary>
        [Test]
        public void ModUninstaller_PostNativeCleanupFailure_ReportsFailedButVerifiedCommitted()
        {
            string archive = Path.GetTempFileName();
            try
            {
                var mod = new InstallLog.DummyMod("Committed before cleanup failure", archive);
                var activeMods = new ThreadSafeObservableList<IMod>();
                var readOnlyActiveMods = new ReadOnlyObservableList<IMod>(activeMods);
                bool hasActiveLinks = true;
                IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
                {
                    if (method.Name == "get_ActiveMods")
                        return readOnlyActiveMods;
                    if (method.Name == "GetModKey")
                        return null;
                    return null;
                });
                IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
                {
                    if (method.Name == "CheckHasActiveLinks")
                        return hasActiveLinks;
                    if (method.Name == "DisableMod")
                    {
                        hasActiveLinks = false;
                        return null;
                    }
                    return null;
                });
                var uninstaller = new LifecycleCharacterizationUninstaller(mod, installLog, readOnlyActiveMods, virtualModActivator);
                uninstaller.AssignIdentity();
                using (var completed = new ManualResetEventSlim(false))
                {
                    uninstaller.TaskSetCompleted += (sender, args) => completed.Set();

                    uninstaller.Install();

                    Assert.That(completed.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(uninstaller.Succeeded, Is.False, "The forced post-native cleanup failure should remain a reported failure.");
                    Assert.That(uninstaller.OperationResult.ReportedStatus, Is.EqualTo(ModOperationReportedStatus.Failed));
                    Assert.That(uninstaller.OperationResult.Durability, Is.EqualTo(ModOperationDurability.VerifiedCommitted));
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
                : this(p_modMod, CreateGameMode(Path.Combine(Path.GetTempPath(), "NMM-C3-Lifecycle")))
            {
            }

            public LifecycleCharacterizationInstaller(IMod p_modMod, IGameMode p_gmdGameMode)
                : base(p_modMod, p_gmdGameMode, null, null, null, null, null, null, null, null, null)
            {
            }

            public void AssignIdentity()
            {
                AssignOperationIdentity(ModOperationIdentity.CreateNew(ModOperationOrigin.Manual,
                    new ModOperationFingerprint("lifecycle-test", new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Default), null)));
            }

            public void RunSynchronously()
            {
                RunTasks();
            }
        }

        private sealed class LifecycleCharacterizationUninstaller : ModUninstaller
        {
            public LifecycleCharacterizationUninstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, ReadOnlyObservableList<IMod> p_rolActiveMods)
                : this(p_modMod, p_ilgInstallLog, p_rolActiveMods, null)
            {
            }

            public LifecycleCharacterizationUninstaller(IMod p_modMod, IInstallLog p_ilgInstallLog,
                ReadOnlyObservableList<IMod> p_rolActiveMods, IVirtualModActivator p_ivaVirtualModActivator)
                : base(p_modMod, null, null, p_ivaVirtualModActivator, null, p_ilgInstallLog, null, p_rolActiveMods)
            {
            }

            public void AssignIdentity()
            {
                AssignOperationIdentity(ModOperationIdentity.CreateNew(ModOperationOrigin.Manual,
                    new ModOperationFingerprint("lifecycle-test", new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Default), null)));
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
        /// Throws while leaving read-only mode so cleanup cannot bypass terminal reporting.
        /// </summary>
        private sealed class ThrowingCleanupMod : InstallLog.DummyMod, IMod
        {
            public ThrowingCleanupMod(string p_strName, string p_strFileName)
                : base(p_strName, p_strFileName)
            {
            }

            void IMod.EndReadOnlyTransaction()
            {
                throw new InvalidOperationException("characterization cleanup");
            }
        }

        private static IGameMode CreateGameMode(string p_strInstallInfoDirectory)
        {
            IGameModeEnvironmentInfo environment = InterfaceStub<IGameModeEnvironmentInfo>.Create((method, args) =>
            {
                if (method.Name == "get_InstallInfoDirectory")
                    return p_strInstallInfoDirectory;
                if (method.Name == "get_InstallationPath")
                    return p_strInstallInfoDirectory;
                return null;
            });

            return InterfaceStub<IGameMode>.Create((method, args) =>
            {
                if (method.Name == "get_GameModeEnvironmentInfo")
                    return environment;
                if (method.Name == "get_InstallationPath")
                    return p_strInstallInfoDirectory;
                if (method.Name == "get_UsesPlugins")
                    return false;
                return null;
            });
        }

        private static void CreateReplayArtifacts(string p_strReplayPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(p_strReplayPath));
            File.WriteAllText(p_strReplayPath, "<FileList ReplayVersion=\"2\"><File FileFrom=\"a\" FileTo=\"b\" /></FileList>");
            string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(p_strReplayPath);
            Directory.CreateDirectory(payloadDirectory);
            File.WriteAllBytes(Path.Combine(payloadDirectory, "payload.bin"), new byte[] { 1, 2, 3 });
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
