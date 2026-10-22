using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.Util;
using NUnit.Framework;

namespace NexusClientTests
{
    [TestFixture]
    public class Witcher3RemasteredRegistrationTests
    {
        [Test]
        public void RemasteredNativeActivationUsesTheReadOnlyReviewedCodeGate()
        {
            var releaseGate = typeof(DataDrivenGameModeFactory).GetProperty(
                "RemasteredNativeActivationApproved",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(releaseGate, Is.Not.Null, "The dedicated native activation gate must not disappear.");
            Assert.That(releaseGate.GetValue(null, null), Is.EqualTo(true));
            Assert.That(releaseGate.GetSetMethod(true), Is.Null,
                "A game definition must not be able to change the native activation approval.");
        }

        [Test]
        public void RemasteredSharesTheNexusCatalogNotTheNativeModeIdentity()
        {
            Assert.That(GameDomainTranslator.DetermineGameDomain("Witcher3Remastered"), Is.EqualTo("witcher3"));
            Assert.That(GameDomainTranslator.DetermineGameDomain("WITCHER3REMASTERED"), Is.EqualTo("witcher3"));
            Assert.That(GameDomainTranslator.DetermineGameDomain("Witcher3"), Is.EqualTo("Witcher3"));
        }

        [Test]
        public void TheDedicatedDefinitionIsValidWhileItsInstallCapabilitiesRemainDisabled()
        {
            GameModeDefinition definition = CreateDefinition();
            List<GameModeDefinitionIssue> issues = new GameModeDefinitionValidator().Validate(definition).ToList();
            Assert.That(issues.Where(issue => issue.Severity == GameModeDefinitionIssueSeverity.Error), Is.Empty);
            Assert.That(definition.ModInstall.SupportsGameRootInstall, Is.False);
        }

        [Test]
        public void UserEditedDefinitionCannotBypassTheNativeInstallGate()
        {
            GameModeDefinition definition = CreateDefinition();
            definition.ModInstall.SupportsGameRootInstall = true;
            AssertInvalid(definition, "modInstall");
            definition = CreateDefinition();
            definition.ModInstall.PathAdjustmentProfile = "cyberpunk2077";
            AssertInvalid(definition, "modInstall");
            definition = CreateDefinition();
            definition.Storage = new GameModeStorageDefinition { ShareModsStorageWith = new[] { "Witcher3" } };
            AssertInvalid(definition, "storage.shareModsStorageWith");
            definition = CreateDefinition();
            definition.GameExecutables = new[] { @"bin\x64\witcher3.exe", @"bin\x64_dx12\witcher3.exe" };
            AssertInvalid(definition, "gameExecutables");
            definition = CreateDefinition();
            definition.Plugin = new GameModePluginDefinition { UsesPlugins = true };
            AssertInvalid(definition, "plugin");
            definition = CreateDefinition();
            definition.BehaviorProfile = "generic";
            AssertInvalid(definition, "behaviorProfile");
        }

        [Test]
        public void EditionGateNeedsPositiveFiveXEvidence()
        {
            var positive = Witcher3EditionResolver.Resolve(new Witcher3EditionEvidence(false, true, null, 5));
            var oldDx12 = Witcher3EditionResolver.Resolve(new Witcher3EditionEvidence(false, true, null, 4));
            var mixed = Witcher3EditionResolver.Resolve(new Witcher3EditionEvidence(true, true, 4, 5));
            Assert.That(DataDrivenWitcher3RemasteredGameModeDescriptor.IsConfirmedRemastered(positive), Is.True);
            Assert.That(DataDrivenWitcher3RemasteredGameModeDescriptor.IsConfirmedRemastered(oldDx12), Is.False);
            Assert.That(DataDrivenWitcher3RemasteredGameModeDescriptor.IsConfirmedRemastered(mixed), Is.False);
            Assert.That(DataDrivenWitcher3RemasteredGameModeDescriptor.IsConfirmedRemastered(null), Is.False);
        }

        [Test]
        public void ScannerResolvesTheFullRelativePathNotJustTheExecutableName()
        {
            string root = Path.Combine(Path.GetTempPath(), "WitcherGame");
            string found = Path.Combine(root, "bin", "x64_dx12", "witcher3.exe");
            string resolved;
            Assert.That(GameInstallationCandidatePath.TryResolveGameRoot(found,
                @"bin\x64_dx12\witcher3.exe", out resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(root));
            Assert.That(GameInstallationCandidatePath.TryResolveGameRoot(found,
                @"bin\x64\witcher3.exe", out resolved), Is.False);
            Assert.That(GameInstallationCandidatePath.TryResolveGameRoot(found,
                @"witcher3.exe", out resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(Path.Combine(root, "bin", "x64_dx12")));
        }

        [TestCase(@"..\witcher3.exe")]
        [TestCase(@"bin\..\witcher3.exe")]
        [TestCase(@"C:\bin\witcher3.exe")]
        [TestCase(@"bin//witcher3.exe")]
        [TestCase(@"bin\.\witcher3.exe")]
        public void ScannerRejectsUnsafeRelativeExecutablePaths(string relative)
        {
            string resolved;
            Assert.That(GameInstallationCandidatePath.TryResolveGameRoot(
                Path.Combine(Path.GetTempPath(), "bin", "witcher3.exe"), relative, out resolved), Is.False);
        }

        [Test]
        public void ScannerDoesNotAcceptAdjacentFolderNames()
        {
            string root = Path.Combine(Path.GetTempPath(), "Mybin", "x64_dx12", "witcher3.exe");
            string resolved;
            Assert.That(GameInstallationCandidatePath.TryResolveGameRoot(root,
                @"bin\x64_dx12\witcher3.exe", out resolved), Is.False);
            Assert.That(GameInstallationCandidatePath.TryResolveGameRoot("relative.exe",
                @"witcher3.exe", out resolved), Is.False);
        }

        private static void AssertInvalid(GameModeDefinition definition, string property)
        {
            Assert.That(new GameModeDefinitionValidator().Validate(definition).Any(issue =>
                issue.Severity == GameModeDefinitionIssueSeverity.Error && issue.PropertyPath == property), Is.True,
                "Expected the witcher3remastered schema to block " + property);
        }

        private static GameModeDefinition CreateDefinition()
        {
            return new GameModeDefinition
            {
                SchemaVersion = 2,
                ModeId = "Witcher3Remastered",
                Name = "The Witcher 3 Remastered",
                BehaviorProfile = "witcher3remastered",
                GameExecutables = new[] { @"bin\x64_dx12\witcher3.exe" },
                StopFolders = new[] { "Mods", "DLC", "bin" },
                Launcher = new GameModeLauncherDefinition
                {
                    DefaultExecutable = @"bin\x64_dx12\witcher3.exe",
                    AllowCustomCommand = false
                },
                ModInstall = new GameModeModInstallDefinition
                {
                    SupportsGameRootInstall = false,
                    PathAdjustmentProfile = "none"
                },
                Discovery = new GameModeDiscoveryDefinition
                {
                    Stores = new List<GameModeStoreDiscoveryDefinition> {
                        new GameModeStoreDiscoveryDefinition {
                            Store = "Steam", Id = "292030",
                            ExecutableName = @"bin\x64_dx12\witcher3.exe"
                        }
                    }
                }
            };
        }
    }
}
