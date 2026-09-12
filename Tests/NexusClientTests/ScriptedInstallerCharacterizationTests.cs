namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Xml.Linq;

    using Nexus.Client.ModManagement;

    using NUnit.Framework;

    /// <summary>
    /// Characterizes scripted-installer behavior that must remain stable while the installation pipeline is refactored.
    /// </summary>
    [TestFixture]
    public class ScriptedInstallerCharacterizationTests
    {
        /// <summary>
        /// Verifies that ScriptFunctionProxy stages archive files under the mod download identifier when one is available.
        /// </summary>
        [Test]
        public void ScriptProxy_InstallFile_UsesDownloadIdForVirtualStaging()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, "12345", false, false, "linked");

                bool booResult = ctx.Proxy.InstallFileFromMod("Textures/Foo.DDS", "textures/Foo.dds");

                Assert.IsTrue(booResult);
                Assert.AreEqual(NormalizePath("textures/foo.dds"), ctx.FileInstaller.LastModFilePath);
                Assert.AreEqual(Path.Combine(ctx.VirtualPath, "12345", NormalizePath("textures/Foo.dds")), ctx.FileInstaller.LastInstallPath);
                Assert.AreEqual(NormalizePath("textures/Foo.dds"), ctx.LastLinkedDestination);
                Assert.AreEqual(ctx.FileInstaller.LastInstallPath, ctx.LastLinkedSource);
            }
        }

        /// <summary>
        /// Verifies that executable files use the HD-link staging root while MultiHD mode is active.
        /// </summary>
        [Test]
        public void ScriptProxy_InstallFile_MultiHdExecutableUsesHdLinkStaging()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, true, false, "linked");

                ctx.Proxy.InstallFileFromMod("tools/Tool.EXE", "tools/Tool.exe");

                Assert.AreEqual(Path.Combine(ctx.HdLinkPath, "ExampleMod", NormalizePath("tools/Tool.exe")), ctx.FileInstaller.LastInstallPath);
            }
        }

        /// <summary>
        /// Verifies that globally ignored files do not reach either the file installer or virtual link installer.
        /// </summary>
        [Test]
        public void ScriptProxy_InstallFile_IgnoredFileSkipsInstallAndLink()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, "12345", false, false, "linked");

                bool booResult = ctx.Proxy.InstallFileFromMod("meta.ini", "meta.ini");

                Assert.IsTrue(booResult);
                Assert.AreEqual(0, ctx.FileInstaller.InstallCallCount);
                Assert.AreEqual(0, ctx.LinkCallCount);
                Assert.AreEqual(0, ctx.Proxy.ShadowPlan.Count);
            }
        }

        /// <summary>
        /// Verifies the legacy behavior where ScriptFunctionProxy reports success even when the underlying file installer returns false.
        /// </summary>
        [Test]
        public void ScriptProxy_InstallFile_FileInstallerFalseStillLinksAndReturnsTrue()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, "linked");
                ctx.FileInstaller.InstallResult = false;

                bool booResult = ctx.Proxy.InstallFileFromMod("meshes/foo.nif", "meshes/foo.nif");

                Assert.IsTrue(booResult);
                Assert.AreEqual(1, ctx.LinkCallCount);
            }
        }

        /// <summary>
        /// Verifies that a successful link result records the original source and destination in the scripted replay cache.
        /// </summary>
        [Test]
        public void ScriptProxy_InstallFile_LinkResultWritesOriginalReplayMapping()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, "linked");
                const string strSource = "Textures/MixedCase.DDS";
                const string strDestination = "Textures/Target.DDS";

                ctx.Proxy.InstallFileFromMod(strSource, strDestination);

                string strLogPath = Path.Combine(tmp.Path, "InstallInfo", "Scripted", "ExampleMod.xml");
                XDocument docLog = XDocument.Load(strLogPath);
                XElement xelFile = docLog.Root.Element("File");
                Assert.AreEqual(strSource, (string)xelFile.Attribute("FileFrom"));
                Assert.AreEqual(strDestination, (string)xelFile.Attribute("FileTo"));
                Assert.AreEqual("Example Mod", (string)docLog.Root.Attribute("ModName"));
                Assert.AreEqual("1.2.3", (string)docLog.Root.Attribute("ModVersion"));
            }
        }

        /// <summary>
        /// Verifies that an empty link result does not create the scripted file-selection replay cache.
        /// </summary>
        [Test]
        public void ScriptProxy_InstallFile_EmptyLinkResultDoesNotWriteReplayCache()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, String.Empty);

                ctx.Proxy.InstallFileFromMod("meshes/foo.nif", "meshes/foo.nif");

                string strLogPath = Path.Combine(tmp.Path, "InstallInfo", "Scripted", "ExampleMod.xml");
                Assert.IsFalse(File.Exists(strLogPath));
            }
        }

        /// <summary>
        /// Verifies the legacy difference where archive-file installation treats the sentinel download identifier as a real staging identifier.
        /// </summary>
        [Test]
        public void ScriptProxy_InstallFile_MinusOneDownloadIdUsesDownloadIdStaging()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, "-1", false, false, "linked");

                ctx.Proxy.InstallFileFromMod("meshes/foo.nif", "meshes/foo.nif");

                Assert.AreEqual(Path.Combine(ctx.VirtualPath, "-1", NormalizePath("meshes/foo.nif")), ctx.FileInstaller.LastInstallPath);
            }
        }

        /// <summary>
        /// Verifies that generated files treat the legacy sentinel download identifier as unavailable and fall back to filename-based staging.
        /// </summary>
        [Test]
        public void ScriptProxy_GenerateDataFile_MinusOneDownloadIdUsesFilenameStaging()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, "-1", false, false, "linked");
                byte[] bteData = { 1, 2, 3 };

                bool booResult = ctx.Proxy.GenerateDataFile("config/settings.bin", bteData);

                Assert.IsTrue(booResult);
                Assert.AreEqual(Path.Combine(ctx.VirtualPath, "ExampleMod", NormalizePath("config/settings.bin")), ctx.FileInstaller.LastGeneratedPath);
                CollectionAssert.AreEqual(bteData, ctx.FileInstaller.LastGeneratedData);
            }
        }

        /// <summary>
        /// Verifies that existing-data queries normalize the script path and apply the game-mode path adjustment before querying storage.
        /// </summary>
        [Test]
        public void ScriptProxy_DataFileExists_NormalizesAndAdjustsPath()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, "linked");
                ctx.DataFileUtil.ExistsResult = true;

                bool booExists = ctx.Proxy.DataFileExists("  Config/File.ini  ");

                Assert.IsTrue(booExists);
                Assert.AreEqual("adjusted:" + NormalizePath("Config/File.ini"), ctx.DataFileUtil.LastPath);
            }
        }

        /// <summary>
        /// Verifies the current immediate read-after-write semantics of scripted INI edits.
        /// </summary>
        [Test]
        public void ScriptProxy_EditIni_SubsequentReadSeesEditedValue()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, "linked");

                Assert.IsTrue(ctx.Proxy.EditIni("game.ini", "Display", "Mode", "Borderless"));
                Assert.AreEqual("Borderless", ctx.Proxy.GetIniString("game.ini", "Display", "Mode"));
            }
        }

        /// <summary>
        /// Verifies that plugin activation is forwarded using the game-mode-adjusted plugin path.
        /// </summary>
        [Test]
        public void ScriptProxy_SetPluginActivation_AdjustsPathBeforeForwarding()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, "linked");

                ctx.Proxy.SetPluginActivation("plugins/MyPlugin.esp", true);

                Assert.AreEqual("adjusted:" + NormalizePath("plugins/MyPlugin.esp"), ctx.LastPluginActivationPath);
                Assert.IsTrue(ctx.LastPluginActivationState.Value);
            }
        }

        /// <summary>
        /// Verifies the legacy SetLoadOrder overload that validates the supplied permutation but reapplies the current plugin sequence by index.
        /// </summary>
        [Test]
        public void ScriptProxy_SetLoadOrder_PermutationValuesDoNotChangeAppliedSequence()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, "linked");
                ctx.AddManagedPlugin("A.esp");
                ctx.AddManagedPlugin("B.esp");
                ctx.AddManagedPlugin("C.esp");

                ctx.Proxy.SetLoadOrder(new[] { 2, 0, 1 });

                Assert.AreEqual(3, ctx.PluginOrderCalls.Count);
                Assert.AreEqual("A.esp", Path.GetFileName(ctx.PluginOrderCalls[0].Key.Filename));
                Assert.AreEqual(0, ctx.PluginOrderCalls[0].Value);
                Assert.AreEqual("B.esp", Path.GetFileName(ctx.PluginOrderCalls[1].Key.Filename));
                Assert.AreEqual(1, ctx.PluginOrderCalls[1].Value);
                Assert.AreEqual("C.esp", Path.GetFileName(ctx.PluginOrderCalls[2].Key.Filename));
                Assert.AreEqual(2, ctx.PluginOrderCalls[2].Value);
            }
        }

        /// <summary>
        /// Verifies that a representative large sequence of immediate scripted file operations completes within the test timeout.
        /// </summary>
        [Test]
        [Timeout(10000)]
        public void ScriptProxy_InstallFile_LargeImmediateBatchCompletesWithinTimeout()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                // Returning an empty link result avoids replay-cache disk writes so the test measures the immediate install/link path itself.
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, "12345", false, false, String.Empty);

                for (int i = 0; i < 1000; i++)
                    ctx.Proxy.InstallFileFromMod("textures/file" + i + ".dds", "textures/file" + i + ".dds");

                Assert.AreEqual(1000, ctx.FileInstaller.InstallCallCount);
                Assert.AreEqual(1000, ctx.LinkCallCount);
            }
        }

        /// <summary>
        /// Verifies that XML scripted installation uses the source filename when the destination is empty.
        /// </summary>
        [Test]
        public void XmlInstaller_InstallFile_EmptyDestinationUsesSourceFilename()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, "12345", false, false, "linked");
                TestableXmlScriptInstaller installer = new TestableXmlScriptInstaller(ctx.Mod, ctx.GameMode, ctx.Installers, ctx.VirtualModActivator);

                bool booResult = installer.InstallSingleFile("folder/MyPlugin.esp", String.Empty);

                Assert.IsTrue(booResult);
                Assert.AreEqual(1, installer.PlannedOperationCount);
                Assert.AreEqual(0, ctx.FileInstaller.InstallCallCount);

                Assert.IsTrue(installer.ExecutePlannedOperations());
                Assert.AreEqual(Path.Combine(ctx.VirtualPath, "12345", "MyPlugin.esp"), ctx.FileInstaller.LastInstallPath);
                Assert.AreEqual("MyPlugin.esp", ctx.LastLinkedDestination);
            }
        }

        /// <summary>
        /// Verifies that the XML installer records its original source and destination in the scripted file-selection replay cache.
        /// </summary>
        [Test]
        public void XmlInstaller_InstallFileEntry_WritesOriginalReplayMapping()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, "linked");
                TestableXmlScriptInstaller installer = new TestableXmlScriptInstaller(ctx.Mod, ctx.GameMode, ctx.Installers, ctx.VirtualModActivator);
                Nexus.Client.ModManagement.Scripting.XmlScript.InstallableFile ilfFile =
                    new Nexus.Client.ModManagement.Scripting.XmlScript.InstallableFile("Textures/Source.DDS", "Textures/Destination.DDS", false, 0, false, false);

                Assert.IsTrue(installer.InstallFileEntry(ilfFile, false));

                string strLogPath = Path.Combine(tmp.Path, "InstallInfo", "Scripted", "ExampleMod.xml");
                Assert.IsFalse(File.Exists(strLogPath));
                Assert.IsTrue(installer.ExecutePlannedOperations());
                XDocument docLog = XDocument.Load(strLogPath);
                XElement xelFile = docLog.Root.Element("File");
                Assert.AreEqual("Textures/Source.DDS", (string)xelFile.Attribute("FileFrom"));
                Assert.AreEqual("Textures/Destination.DDS", (string)xelFile.Attribute("FileTo"));
            }
        }

        /// <summary>
        /// Verifies that XML plugin activatability is evaluated only after the planned file has been installed.
        /// </summary>
        [Test]
        public void XmlInstaller_PluginActivation_IsEvaluatedAfterFileExecution()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, "linked");
                ctx.ActivatablePluginResult = true;
                TestableXmlScriptInstaller installer = new TestableXmlScriptInstaller(ctx.Mod, ctx.GameMode, ctx.Installers, ctx.VirtualModActivator);
                Nexus.Client.ModManagement.Scripting.XmlScript.InstallableFile ilfFile =
                    new Nexus.Client.ModManagement.Scripting.XmlScript.InstallableFile("Plugin/Test.esp", "Test.esp", false, 0, false, false);

                Assert.IsTrue(installer.InstallFileEntry(ilfFile, true));
                Assert.AreEqual(2, installer.PlannedOperationCount);
                Assert.IsNull(ctx.LastPluginActivationState);

                Assert.IsTrue(installer.ExecutePlannedOperations());
                Assert.AreEqual(1, ctx.PluginActivationQueryInstallCallCount);
                Assert.AreEqual("adjusted:Test.esp", ctx.LastPluginActivationPath);
                Assert.IsTrue(ctx.LastPluginActivationState.Value);
            }
        }

        /// <summary>
        /// Verifies that XML scripted installation also treats the legacy sentinel download identifier as unavailable.
        /// </summary>
        [Test]
        public void XmlInstaller_InstallFile_MinusOneDownloadIdUsesFilenameStaging()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, "-1", false, false, "linked");
                TestableXmlScriptInstaller installer = new TestableXmlScriptInstaller(ctx.Mod, ctx.GameMode, ctx.Installers, ctx.VirtualModActivator);

                bool booResult = installer.InstallSingleFile("meshes/foo.nif", "meshes/foo.nif");

                Assert.IsTrue(booResult);
                Assert.AreEqual(0, ctx.FileInstaller.InstallCallCount);
                Assert.IsTrue(installer.ExecutePlannedOperations());
                Assert.AreEqual(
                    NormalizePath(Path.Combine(ctx.VirtualPath, "ExampleMod", "meshes/foo.nif")),
                    NormalizePath(ctx.FileInstaller.LastInstallPath));
            }
        }

        /// <summary>
        /// Verifies that the mod installer detects the default scripted replay-cache location.
        /// </summary>
        [Test]
        public void ModInstaller_ReplayCache_DefaultLocationIsDetected()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, "linked");
                string strScriptedDirectory = Path.Combine(tmp.Path, "InstallInfo", "Scripted");
                Directory.CreateDirectory(strScriptedDirectory);
                File.WriteAllText(Path.Combine(strScriptedDirectory, "ExampleMod.xml"), "<FileList />");
                TestableModInstaller installer = new TestableModInstaller(ctx.Mod, ctx.GameMode, null);

                Assert.IsTrue(installer.HasScriptedReplayCache());
            }
        }

        /// <summary>
        /// Verifies that replay-cache loading preserves mapping order and ignores entries with an empty archive source.
        /// </summary>
        [Test]
        public void ModInstaller_ReplayCache_LoadPreservesOrderAndSkipsEmptySources()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, "linked");
                string strScriptedDirectory = Path.Combine(tmp.Path, "InstallInfo", "Scripted");
                Directory.CreateDirectory(strScriptedDirectory);
                new XDocument(
                    new XElement("FileList",
                        new XElement("File", new XAttribute("FileFrom", "A.txt"), new XAttribute("FileTo", "Data/A.txt")),
                        new XElement("File", new XAttribute("FileFrom", String.Empty), new XAttribute("FileTo", "ignored.txt")),
                        new XElement("File", new XAttribute("FileFrom", "B.txt"), new XAttribute("FileTo", "Data/B.txt"))))
                    .Save(Path.Combine(strScriptedDirectory, "ExampleMod.xml"));
                TestableModInstaller installer = new TestableModInstaller(ctx.Mod, ctx.GameMode, null);

                List<KeyValuePair<string, string>> lstFiles = installer.LoadScriptedReplayFiles();

                Assert.AreEqual(2, lstFiles.Count);
                Assert.AreEqual("A.txt", lstFiles[0].Key);
                Assert.AreEqual("Data/A.txt", lstFiles[0].Value);
                Assert.AreEqual("B.txt", lstFiles[1].Key);
                Assert.AreEqual("Data/B.txt", lstFiles[1].Value);
            }
        }

        /// <summary>
        /// Verifies that a profile-specific scripted replay cache takes precedence over the default install-info cache.
        /// </summary>
        [Test]
        public void ModInstaller_ReplayCache_ProfileSpecificPathTakesPrecedence()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = CreateScriptProxyContext(tmp.Path, null, false, false, "linked");
                string strProfileLog = Path.Combine(tmp.Path, "ProfileScripted.xml");
                new XDocument(
                    new XElement("FileList",
                        new XElement("File", new XAttribute("FileFrom", "Profile.txt"), new XAttribute("FileTo", "Data/Profile.txt"))))
                    .Save(strProfileLog);

                IProfileManager prmProfileManager = InterfaceStub<IProfileManager>.Create((p_mifMethod, p_objArgs) =>
                {
                    if (p_mifMethod.Name == "IsScriptedLogPresent")
                        return strProfileLog;
                    return null;
                });
                TestableModInstaller installer = new TestableModInstaller(ctx.Mod, ctx.GameMode, prmProfileManager);

                List<KeyValuePair<string, string>> lstFiles = installer.LoadScriptedReplayFiles();

                Assert.AreEqual(1, lstFiles.Count);
                Assert.AreEqual("Profile.txt", lstFiles[0].Key);
            }
        }

        /// <summary>
        /// Creates a fully isolated scripted-installer context with recordable dependencies.
        /// </summary>
        /// <param name="p_strRootPath">The temporary root used for virtual and install-info paths.</param>
        /// <param name="p_strDownloadId">The mod download identifier returned by the test mod.</param>
        /// <param name="p_booMultiHd">Whether the virtual activator reports MultiHD mode.</param>
        /// <param name="p_booGameRequiresHardlink">Whether the game mode reports that arbitrary files require hardlinks.</param>
        /// <param name="p_strLinkResult">The value returned by the mod-link installer.</param>
        /// <returns>A configured scripted-installer test context.</returns>
        private static ScriptProxyContext CreateScriptProxyContext(string p_strRootPath, string p_strDownloadId, bool p_booMultiHd, bool p_booGameRequiresHardlink, string p_strLinkResult)
        {
            return new ScriptProxyContext(p_strRootPath, p_strDownloadId, p_booMultiHd, p_booGameRequiresHardlink, p_strLinkResult);
        }

        /// <summary>
        /// Converts installer paths to the platform-specific separator representation used by the production implementation.
        /// </summary>
        /// <param name="p_strPath">The installer-relative path to normalize.</param>
        /// <returns>The normalized path.</returns>
        private static string NormalizePath(string p_strPath)
        {
            return p_strPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
    }

}
