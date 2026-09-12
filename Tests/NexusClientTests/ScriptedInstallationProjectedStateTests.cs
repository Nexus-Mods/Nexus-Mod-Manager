namespace NexusClientTests
{
    using System;
    using System.IO;
    using System.Linq;

    using Nexus.Client.ModManagement.Scripting;
    using Nexus.Client.ModManagement.Scripting.Operations;

    using NUnit.Framework;

    /// <summary>
    /// Verifies that deferred scripted operations are reflected through the projected installation state without committing real changes.
    /// </summary>
    [TestFixture]
    public class ScriptedInstallationProjectedStateTests
    {
        /// <summary>
        /// Verifies that generated files become visible to deferred reads without invoking the operation executor.
        /// </summary>
        [Test]
        public void DeferredSession_GeneratedFileIsVisibleBeforeExecution()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, "12345", false, false, "linked");
                RecordingScriptedInstallOperationExecutor sioExecutor = new RecordingScriptedInstallOperationExecutor(true);
                ScriptedInstallationProjectedState spsState = new ScriptedInstallationProjectedState(ctx.Mod, ctx.GameMode, ctx.Installers);
                ScriptedInstallationSession sisSession = new ScriptedInstallationSession(sioExecutor, ScriptedInstallationSessionMode.Deferred, spsState);
                byte[] bteData = { 4, 5, 6 };

                Assert.IsTrue(sisSession.Submit(new GenerateDataFileOperation("config/generated.bin", bteData)));

                Assert.AreEqual(0, sioExecutor.Operations.Count);
                Assert.IsTrue(spsState.DataFileExists("config/generated.bin"));
                CollectionAssert.AreEqual(bteData, spsState.GetExistingDataFile("config/generated.bin"));
                Assert.AreEqual(0, ctx.FileInstaller.GenerateCallCount);
            }
        }

        /// <summary>
        /// Verifies that archive-backed projected files resolve their contents only when the script requests the file data.
        /// </summary>
        [Test]
        public void InstallModFile_ArchiveDataIsResolvedLazily()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, "12345", false, false, "linked");
                byte[] bteData = { 7, 8, 9 };
                ctx.AddModFile("textures\\foo.dds", bteData);
                ScriptedInstallationProjectedState spsState = new ScriptedInstallationProjectedState(ctx.Mod, ctx.GameMode, ctx.Installers);

                spsState.Apply(new InstallModFileOperation("Textures/Foo.DDS", "textures/foo.dds"));

                Assert.AreEqual(0, ctx.ModGetFileCallCount);
                Assert.IsTrue(spsState.DataFileExists("textures/foo.dds"));
                Assert.AreEqual(0, ctx.ModGetFileCallCount);
                CollectionAssert.AreEqual(bteData, spsState.GetExistingDataFile("textures/foo.dds"));
                Assert.AreEqual(1, ctx.ModGetFileCallCount);
            }
        }

        /// <summary>
        /// Verifies that projected file listings merge current files with matching planned files and respect recursion boundaries.
        /// </summary>
        [Test]
        public void GetExistingDataFileList_MergesProjectedFilesUsingRequestedScope()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, "12345", false, false, "linked");
                string strExistingFile = Path.Combine(tmp.Path, "Game", "Data", "adjusted:meshes", "existing.nif");
                ctx.DataFileUtil.ExistingFiles = new[] { strExistingFile };
                ScriptedInstallationProjectedState spsState = new ScriptedInstallationProjectedState(ctx.Mod, ctx.GameMode, ctx.Installers);
                spsState.Apply(new GenerateDataFileOperation("meshes/new.nif", new byte[] { 1 }));
                spsState.Apply(new GenerateDataFileOperation("meshes/sub/deep.nif", new byte[] { 2 }));
                spsState.Apply(new GenerateDataFileOperation("meshes/ignore.dds", new byte[] { 3 }));

                string[] strTopLevel = spsState.GetExistingDataFileList("meshes", "*.nif", false);
                string[] strRecursive = spsState.GetExistingDataFileList("meshes", "*.nif", true);

                Assert.IsTrue(strTopLevel.Contains(strExistingFile));
                Assert.IsTrue(strTopLevel.Any(p_strPath => p_strPath.EndsWith("new.nif", StringComparison.OrdinalIgnoreCase)));
                Assert.IsFalse(strTopLevel.Any(p_strPath => p_strPath.EndsWith("deep.nif", StringComparison.OrdinalIgnoreCase)));
                Assert.IsFalse(strTopLevel.Any(p_strPath => p_strPath.EndsWith("ignore.dds", StringComparison.OrdinalIgnoreCase)));
                Assert.IsTrue(strRecursive.Any(p_strPath => p_strPath.EndsWith("deep.nif", StringComparison.OrdinalIgnoreCase)));
            }
        }

        /// <summary>
        /// Verifies that projected INI edits shadow current values without modifying the underlying INI installer.
        /// </summary>
        [Test]
        public void EditIni_ProjectedValueShadowsCurrentIniState()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, "12345", false, false, "linked");
                ctx.IniInstaller.EditIni("game.ini", "Display", "Mode", "Windowed");
                ctx.IniInstaller.EditIni("game.ini", "Display", "Width", "1280");
                ScriptedInstallationProjectedState spsState = new ScriptedInstallationProjectedState(ctx.Mod, ctx.GameMode, ctx.Installers);

                spsState.Apply(new EditIniOperation("game.ini", "Display", "Mode", "Borderless"));
                spsState.Apply(new EditIniOperation("game.ini", "Display", "Width", "2560"));

                Assert.AreEqual("Borderless", spsState.GetIniString("game.ini", "Display", "Mode"));
                Assert.AreEqual(2560, spsState.GetIniInt("game.ini", "Display", "Width"));
                Assert.AreEqual("Windowed", ctx.IniInstaller.GetIniString("game.ini", "Display", "Mode"));
                Assert.AreEqual(1280, ctx.IniInstaller.GetIniInt("game.ini", "Display", "Width"));
            }
        }

        /// <summary>
        /// Verifies that projected plugin activation and ordering are visible without changing the current plugin manager.
        /// </summary>
        [Test]
        public void PluginOperations_UpdateProjectedSnapshotOnly()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, "12345", false, false, "linked");
                ctx.AddManagedPlugin("A.esp");
                ctx.AddManagedPlugin("B.esp");
                ScriptedInstallationProjectedState spsState = new ScriptedInstallationProjectedState(ctx.Mod, ctx.GameMode, ctx.Installers);

                spsState.Apply(new SetPluginActivationOperation("A.esp", false));
                spsState.Apply(new SetPluginOrderIndexOperation("B.esp", 0));

                CollectionAssert.AreEqual(new[] { "B.esp", "A.esp" }, spsState.GetAllPlugins());
                CollectionAssert.AreEqual(new[] { "B.esp" }, spsState.GetActivePlugins());
                Assert.AreEqual(0, ctx.PluginOrderCalls.Count);
                Assert.IsNull(ctx.LastPluginActivationState);
            }
        }

        /// <summary>
        /// Verifies that a planned plugin file becomes available to subsequent projected plugin operations.
        /// </summary>
        [Test]
        public void InstallModFile_PluginFileIsAddedToProjectedPluginSnapshot()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, "12345", false, false, "linked", new ScriptedInstallerTestPluginFactory());
                ctx.AddModFile("optional\\newplugin.esp", new byte[] { 1, 2, 3 });
                ScriptedInstallationProjectedState spsState = new ScriptedInstallationProjectedState(ctx.Mod, ctx.GameMode, ctx.Installers);

                spsState.Apply(new InstallModFileOperation("Optional/NewPlugin.esp", "NewPlugin.esp"));
                spsState.Apply(new SetPluginActivationOperation("NewPlugin.esp", true));

                CollectionAssert.AreEqual(new[] { "NewPlugin.esp" }, spsState.GetAllPlugins());
                CollectionAssert.AreEqual(new[] { "NewPlugin.esp" }, spsState.GetActivePlugins());
            }
        }

        /// <summary>
        /// Verifies that immediate sessions do not project failed operations as successful state changes.
        /// </summary>
        [Test]
        public void ImmediateSession_FailedOperationDoesNotChangeProjectedState()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, "12345", false, false, "linked");
                RecordingScriptedInstallOperationExecutor sioExecutor = new RecordingScriptedInstallOperationExecutor(false);
                ScriptedInstallationProjectedState spsState = new ScriptedInstallationProjectedState(ctx.Mod, ctx.GameMode, ctx.Installers);
                ScriptedInstallationSession sisSession = new ScriptedInstallationSession(sioExecutor, ScriptedInstallationSessionMode.Immediate, spsState);

                Assert.IsFalse(sisSession.Submit(new GenerateDataFileOperation("config/failed.bin", new byte[] { 1 })));

                Assert.IsFalse(spsState.DataFileExists("config/failed.bin"));
            }
        }
        /// <summary>
        /// Verifies that the legacy plugin-move operation keeps using the pre-move index space when projecting the new order.
        /// </summary>
        [Test]
        public void MovePluginsInLoadOrder_UsesLegacyPreMoveIndexSpace()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, "12345", false, false, "linked");
                ctx.AddManagedPlugin("A.esp");
                ctx.AddManagedPlugin("B.esp");
                ctx.AddManagedPlugin("C.esp");
                ctx.AddManagedPlugin("D.esp");
                ScriptedInstallationProjectedState spsState = new ScriptedInstallationProjectedState(ctx.Mod, ctx.GameMode, ctx.Installers);

                spsState.Apply(new MovePluginsInLoadOrderOperation(new[] { 1 }, 2));

                CollectionAssert.AreEqual(new[] { "A.esp", "B.esp", "C.esp", "D.esp" }, spsState.GetAllPlugins());
            }
        }

        /// <summary>
        /// Verifies that basic-install macros cannot silently produce an incomplete projected state.
        /// </summary>
        [Test]
        public void PerformBasicInstall_ProjectionRequiresExplicitExpansion()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, "12345", false, false, "linked");
                ScriptedInstallationProjectedState spsState = new ScriptedInstallationProjectedState(ctx.Mod, ctx.GameMode, ctx.Installers);

                Assert.Throws<NotSupportedException>(() => spsState.Apply(new PerformBasicInstallOperation()));
            }
        }

    }
}
