namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;

    using Nexus.Client.Games;
    using Nexus.Client.ModManagement;
    using Nexus.Client.ModManagement.InstallationLog;
    using Nexus.Client.ModManagement.InstallationLog.Upgraders;
    using Nexus.Client.Settings;
    using Nexus.Transactions;

    using NUnit.Framework;

    /// <summary>
    /// Verifies the Step 1 domain and persistence foundation without exercising Direct deployment behavior.
    /// </summary>
    [TestFixture]
    public class DirectInstallFoundationTests
    {
        [Test]
        public void DeploymentTarget_IsRootAwareCanonicalAndCaseInsensitive()
        {
            ModDeploymentTarget data = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"Textures/./Armor/Foo.dds");
            ModDeploymentTarget same = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"textures\armor\foo.DDS");
            ModDeploymentTarget gameRoot = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.GameRoot, @"Textures\Armor\Foo.dds");

            Assert.AreEqual(@"Textures\Armor\Foo.dds", data.RelativePath);
            Assert.AreEqual(data, same);
            Assert.AreEqual(data.GetHashCode(), same.GetHashCode());
            Assert.AreNotEqual(data, gameRoot);
        }

        [Test]
        public void DeploymentTargetResolver_UsesExistingDataAdjustmentAndSecondaryRules()
        {
            int adjustmentCalls = 0;
            IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) =>
            {
                if (method.Name == "GetModFormatAdjustedPath")
                {
                    adjustmentCalls++;
                    return @"Scripts\Resolved.pex";
                }
                if (method.Name == "get_HasSecondaryInstallPath")
                    return true;
                if (method.Name == "CheckSecondaryInstall")
                    return true;
                return null;
            });
            var mod = new InstallLog.DummyMod("Resolver Test", "ResolverTest.7z");

            ModDeploymentTarget dataTarget = ModDeploymentTargetResolver.Resolve(gameMode, mod, @"Data\Scripts\Source.pex", ModInstallRoot.Data);
            ModDeploymentTarget rootTarget = ModDeploymentTargetResolver.Resolve(gameMode, mod, @"Data\Scripts\Source.pex", ModInstallRoot.GameRoot);

            Assert.AreEqual(ModDeploymentRoot.Secondary, dataTarget.Root);
            Assert.AreEqual(@"Scripts\Resolved.pex", dataTarget.RelativePath);
            Assert.AreEqual(ModDeploymentRoot.GameRoot, rootTarget.Root);
            Assert.AreEqual(@"Data\Scripts\Source.pex", rootTarget.RelativePath);
            Assert.AreEqual(1, adjustmentCalls, "GameRoot targets must bypass Data-format adjustment.");
        }

        [TestCase(@"..\outside.dll")]
        [TestCase(@"Data\..\..\outside.dll")]
        [TestCase(@"C:\outside.dll")]
        [TestCase(@"\\server\share\outside.dll")]
        [TestCase(@"/outside.dll")]
        public void DeploymentTarget_RejectsRootEscapeAndAbsolutePaths(string path)
        {
            Assert.Throws<InvalidDataException>(() => ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, path));
        }

        [Test]
        public void PreferredInstallMethod_MissingAndInvalidValuesRemainVirtual()
        {
            var values = new PerGameModeSettings<string>();
            ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
                method.Name == "get_PreferredInstallMethod" ? values : null);

            Assert.AreEqual(ModInstallMethod.Virtual, settings.GetPreferredInstallMethod("SkyrimSE"));

            values["SkyrimSE"] = "not-a-method";
            Assert.AreEqual(ModInstallMethod.Virtual, settings.GetPreferredInstallMethod("SkyrimSE"));

            settings.SetPreferredInstallMethod("SkyrimSE", ModInstallMethod.Direct);
            Assert.AreEqual("Direct", values["SkyrimSE"]);
            Assert.AreEqual(ModInstallMethod.Direct, settings.GetPreferredInstallMethod("SkyrimSE"));
        }

        [Test]
        public void Upgrade0500_OnlyBumpsVersionAndKeepsExistingRecordsImplicitVirtual()
        {
            string directory = CreateTempDirectory();
            try
            {
                string logPath = Path.Combine(directory, "InstallLog.xml");
                File.WriteAllText(logPath,
                    "<installLog fileVersion=\"0.5.0.0\">" +
                    "<modList><mod path=\"Example.7z\" key=\"example\"><version machineVersion=\"1.0\">1.0</version><name>Example</name><installDate>date</installDate></mod></modList>" +
                    "<dataFiles><file path=\"Textures\\Foo.dds\"><installingMods><mod key=\"example\" /></installingMods></file></dataFiles>" +
                    "<iniEdits /><gameSpecificEdits /></installLog>");

                Assert.IsTrue(new InstallLogUpgrader().CanUpgrade(logPath));

                XDocument before = XDocument.Load(logPath);
                string modList = before.Root.Element("modList").ToString(SaveOptions.DisableFormatting);
                string dataFiles = before.Root.Element("dataFiles").ToString(SaveOptions.DisableFormatting);

                new ExposedUpgrade0500Task().Run(logPath);

                XDocument after = XDocument.Load(logPath);
                Assert.AreEqual("0.6.0.0", after.Root.Attribute("fileVersion").Value);
                Assert.AreEqual(modList, after.Root.Element("modList").ToString(SaveOptions.DisableFormatting));
                Assert.AreEqual(dataFiles, after.Root.Element("dataFiles").ToString(SaveOptions.DisableFormatting));
                Assert.IsNull(after.Descendants("mod").First().Attribute("installMethod"));
                Assert.IsNull(after.Root.Element("deploymentFiles"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void InstallLog_PersistsDirectMethodSparseDeploymentAndInverseIndex()
        {
            string directory = CreateTempDirectory();
            try
            {
                string modDirectory = Path.Combine(directory, "Mods");
                Directory.CreateDirectory(modDirectory);
                string logPath = Path.Combine(directory, "InstallLog.xml");
                InstallLog log = CreateInstallLog(modDirectory, logPath);
                var virtualMod = new InstallLog.DummyMod("Virtual Test", Path.Combine(modDirectory, "VirtualTest.7z"));
                log.AddActiveMod(virtualMod, ModInstallRoot.Data, ModInstallMethod.Virtual);
                string virtualKey = log.GetModKey(virtualMod);

                XDocument virtualOnly = XDocument.Load(logPath);
                XElement virtualElement = virtualOnly.Descendants("mod").First(x => (string)x.Attribute("key") == virtualKey);
                Assert.IsNull(virtualElement.Attribute("installMethod"));
                Assert.IsNull(virtualOnly.Root.Element("deploymentFiles"));

                var mod = new InstallLog.DummyMod("Direct Test", Path.Combine(modDirectory, "DirectTest.7z"));
                log.AddActiveMod(mod, ModInstallRoot.Data, ModInstallMethod.Direct);
                string modKey = log.GetModKey(mod);
                ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"Textures\Foo.dds");
                log.SetDeploymentOwners(target, new[] { log.OriginalValuesKey, modKey });

                Assert.AreEqual(ModInstallMethod.Direct, log.GetModInstallMethod(mod));
                CollectionAssert.AreEqual(new[] { log.OriginalValuesKey, modKey }, log.GetDeploymentOwnerKeys(target));
                CollectionAssert.Contains(log.GetDeploymentTargetsForMod(modKey).ToArray(), target);

                XDocument persisted = XDocument.Load(logPath);
                XElement modElement = persisted.Descendants("mod").First(x => (string)x.Attribute("key") == modKey);
                Assert.AreEqual("Direct", (string)modElement.Attribute("installMethod"));
                Assert.IsFalse(persisted.Descendants("dataFiles").Descendants("file").Any(x => (string)x.Attribute("path") == target.RelativePath));

                XElement deployment = persisted.Root.Element("deploymentFiles").Elements("file").Single();
                Assert.AreEqual("Data", (string)deployment.Attribute("root"));
                Assert.AreEqual(@"Textures\Foo.dds", (string)deployment.Attribute("path"));
                CollectionAssert.AreEqual(
                    new[] { log.OriginalValuesKey, modKey },
                    deployment.Descendants("installingMods").Elements("mod").Select(x => (string)x.Attribute("key")).ToArray());
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void InstallLog_ReplacePreservesMethodAndDeploymentChangesRollback()
        {
            string directory = CreateTempDirectory();
            try
            {
                string modDirectory = Path.Combine(directory, "Mods");
                Directory.CreateDirectory(modDirectory);
                string logPath = Path.Combine(directory, "InstallLog.xml");
                InstallLog log = CreateInstallLog(modDirectory, logPath);
                var first = new InstallLog.DummyMod("First", Path.Combine(modDirectory, "First.7z"));
                var replacement = new InstallLog.DummyMod("Replacement", Path.Combine(modDirectory, "Replacement.7z"));

                log.AddActiveMod(first, ModInstallRoot.GameRoot, ModInstallMethod.Direct);
                string modKey = log.GetModKey(first);
                log.ReplaceActiveMod(first, replacement);

                Assert.AreEqual(modKey, log.GetModKey(replacement));
                Assert.AreEqual(ModInstallRoot.GameRoot, log.GetModInstallRoot(replacement));
                Assert.AreEqual(ModInstallMethod.Direct, log.GetModInstallMethod(replacement));

                ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.GameRoot, "loader.dll");
                using (var scope = new TransactionScope())
                {
                    log.SetDeploymentOwners(target, new[] { modKey });
                    Assert.IsTrue(log.IsDeploymentTargetPromoted(target));
                }

                Assert.IsFalse(log.IsDeploymentTargetPromoted(target));
                Assert.IsNull(XDocument.Load(logPath).Root.Element("deploymentFiles"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static InstallLog CreateInstallLog(string modDirectory, string logPath)
        {
            var registry = new ModRegistry(null, null);
            ConstructorInfo constructor = typeof(InstallLog).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(ModRegistry), typeof(Nexus.Client.Games.IGameMode), typeof(string), typeof(string) },
                null);

            Assert.NotNull(constructor);
            return (InstallLog)constructor.Invoke(new object[] { registry, null, modDirectory, logPath });
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "NMM-Step1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private sealed class ExposedUpgrade0500Task : Upgrade0500Task
        {
            public void Run(string logPath)
            {
                UpgradeInstallLog(logPath, null, null);
            }
        }
    }
}
