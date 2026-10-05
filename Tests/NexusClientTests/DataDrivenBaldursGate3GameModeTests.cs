using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.ModManagement;
using Nexus.Client.Games.DataDriven;
using NUnit.Framework;

namespace Nexus.Client.Tests
{
    [TestFixture]
    public class DataDrivenBaldursGate3GameModeTests
    {
        [Test]
        public void ValidatorAcceptsBaldursGate3BehaviorProfile()
        {
            GameModeDefinition definition = CreateDefinition();

            List<GameModeDefinitionIssue> issues = new GameModeDefinitionValidator().Validate(definition).ToList();

            Assert.That(issues.Where(x => x.Severity == GameModeDefinitionIssueSeverity.Error), Is.Empty);
        }

        [Test]
        public void ValidatorKeepsBaldursGate3Pluginless()
        {
            GameModeDefinition definition = CreateDefinition();
            definition.Plugin = new GameModePluginDefinition { UsesPlugins = false };

            List<GameModeDefinitionIssue> issues = new GameModeDefinitionValidator().Validate(definition).ToList();

            Assert.That(issues.Any(x => string.Equals(x.PropertyPath, "plugin", StringComparison.Ordinal)), Is.True);
        }

        [TestCase("bin\\NativeMods\\example.dll", true)]
        [TestCase("wrapper\\bin\\example.dll", true)]
        [TestCase("Data\\Generated\\Public\\example.pak", true)]
        [TestCase("Mods\\Example.pak", false)]
        [TestCase("binoculars\\example.txt", false)]
        public void SecondaryInstallRoutingMatchesLegacyFolders(string path, bool expected)
        {
            Assert.That(DataDrivenBaldursGate3GameMode.IsSecondaryInstallPath(path), Is.EqualTo(expected));
        }

        [TestCase("info.json", true)]
        [TestCase("Wrapper\\INFO.JSON", true)]
        [TestCase("manifest.json", false)]
        public void ManifestMetadataDetectionMatchesLegacyInfoJsonRule(string path, bool expected)
        {
            Assert.That(DataDrivenBaldursGate3GameMode.IsManifestMetadataFile(path), Is.EqualTo(expected));
        }

        [Test]
        public void DeterministicModSettingsPlanPreservesLegacyProfileAndModuleOrder()
        {
            string root = Path.Combine(Path.GetTempPath(), "nmm-bg3-plan-" + Guid.NewGuid().ToString("N"));
            try
            {
                string alpha = Path.Combine(root, "Alpha");
                string zulu = Path.Combine(root, "Zulu");
                Directory.CreateDirectory(alpha);
                Directory.CreateDirectory(zulu);
                File.WriteAllText(Path.Combine(alpha, "modsettings.lsx"), CreateManifest("11111111-1111-1111-1111-111111111111", "Existing Alpha"));
                File.WriteAllText(Path.Combine(zulu, "modsettings.lsx"), CreateManifest(null, null));
                byte[] info = Encoding.ASCII.GetBytes(
                    "{\"Mods\":[" +
                    "{\"Name\":\"First New Name\",\"Folder\":\"FirstFolder\",\"Version\":\"2\",\"UUID\":\"11111111-1111-1111-1111-111111111111\"}," +
                    "{\"Name\":\"Second\",\"Folder\":\"SecondFolder\",\"Version\":\"3\",\"UUID\":\"22222222-2222-2222-2222-222222222222\"}]}" );

                IReadOnlyList<BasicInstallGameSpecificValue> plan =
                    DataDrivenBaldursGate3ModSettingsValue.BuildPlanFromInfoBytes(info, root);

                Assert.That(plan.Count, Is.EqualTo(4));
                Assert.That(plan.Select(x => x.Key), Is.EqualTo(new[]
                {
                    "bg3-modsettings-v1|Alpha|11111111-1111-1111-1111-111111111111",
                    "bg3-modsettings-v1|Alpha|22222222-2222-2222-2222-222222222222",
                    "bg3-modsettings-v1|Zulu|11111111-1111-1111-1111-111111111111",
                    "bg3-modsettings-v1|Zulu|22222222-2222-2222-2222-222222222222"
                }));
                StringAssert.Contains("Existing Alpha", Encoding.UTF8.GetString(plan[0].Value));
                StringAssert.Contains("Second", Encoding.UTF8.GetString(plan[1].Value));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        private static string CreateManifest(string uuid, string name)
        {
            string module = String.IsNullOrEmpty(uuid) ? String.Empty :
                "<node id=\"ModuleShortDesc\"><attribute id=\"Name\" type=\"LSString\" value=\"" + name +
                "\"/><attribute id=\"UUID\" type=\"FixedString\" value=\"" + uuid + "\"/></node>";
            return "<save><node id=\"Mods\"><children>" + module + "</children></node></save>";
        }

        private static GameModeDefinition CreateDefinition()
        {
            return new GameModeDefinition
            {
                SchemaVersion = 2,
                ModeId = "BaldursGate3",
                Name = "Baldur's Gate 3",
                BehaviorProfile = "baldursgate3",
                GameExecutables = new[] { @"Launcher\LariLauncher.exe" },
                StopFolders = new[] { "bin", "Data" },
                SupportedFormats = new[] { "fomod" },
                Discovery = new GameModeDiscoveryDefinition
                {
                    Stores = new List<GameModeStoreDiscoveryDefinition>
                    {
                        new GameModeStoreDiscoveryDefinition
                        {
                            Store = "Steam",
                            Id = "1086940",
                            InstallFolderName = "Baldurs Gate 3",
                            ExecutableName = @"Launcher\LariLauncher.exe"
                        }
                    }
                },
                Launcher = new GameModeLauncherDefinition
                {
                    DefaultExecutable = @"Launcher\LariLauncher.exe",
                    AllowCustomCommand = true
                },
                Theme = new GameModeThemeDefinition { PrimaryColor = "#D1AB5E" },
                Setup = new GameModeSetupDefinition { UseGenericSetup = true },
                Settings = new GameModeSettingsDefinition { UseGenericSettings = true },
                ModInstall = new GameModeModInstallDefinition
                {
                    PathAdjustmentProfile = "none",
                    RealFileRequiredExtensions = new[] { ".exe", ".jar", ".dll", ".gr2" },
                    SupportsGameRootInstall = false
                }
            };
        }
    }
}
