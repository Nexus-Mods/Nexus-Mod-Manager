using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Games.ManagedContent;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;
using Nexus.Client.Settings;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Characterizes the first configured layout capability and its existing native planning boundary.</summary>
	[TestFixture]
	public class ManagedContentLayoutTests
	{
		/// <summary>Loads the exact shipped/sample JSON through the production strict loader.</summary>
		private static GameModeDefinition Load(string filename)
		{
			GameModeDefinitionLoadResult result = new GameModeDefinitionLoader().LoadFromPath(
				Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", filename));
			Assert.That(result.Issues.Where(issue => issue.Severity == GameModeDefinitionIssueSeverity.Error), Is.Empty);
			return result.Definitions.Single();
		}

		/// <summary>Represents actual selected paths; the source spelling remains untouched.</summary>
		private static KeyValuePair<string, string>[] Files(params string[] paths) => paths.Select(path => new KeyValuePair<string, string>(path, null)).ToArray();

		/// <summary>Creates an ordinary native install context.</summary>
		private static ModInstallContext Context(ModInstallMethod method = ModInstallMethod.Direct) => new ModInstallContext(method, ModInstallRoot.Data);

		/// <summary>Proves the configured Witcher subset has the same exact source/destination contract as the existing planner.</summary>
		[TestCase("Mods/modA/content/a.ws")]
		[TestCase("modA/content/a.ws")]
		[TestCase("DLC/dlcA/content/a.bundle")]
		[TestCase("dlcA/content/a.bundle")]
		[TestCase("content/scripts/a.ws")]
		[TestCase("wrapper/Mods/modA/content/a.ws")]
		[TestCase("wrapper/nested/content/scripts/a.ws")]
		[TestCase("mods/modA/CONTENT/a.ws")]
		public void ConfiguredWitcherLayoutsMatchCharacterizedMappings(string path)
		{
			ManagedContentLayoutPlanner planner = DataDrivenManagedContentRules.Create(Load("Witcher3Remastered.game.json").ManagedContent);
			var selection = Files(path);
			ManagedContentLayoutPlan actual = planner.Plan(selection, "stable", "Armor Set", Context());
			IReadOnlyList<KeyValuePair<string, string>> expected = Witcher3BasicInstallMapping.Map(selection, "stable", "Armor Set", Witcher3Edition.Remastered, Context());
			Assert.That(actual.IsSupported, Is.True, String.Join("; ", actual.Issues));
			Assert.That(actual.Files.Select(file => new KeyValuePair<string, string>(file.SourcePath, file.DestinationPath)), Is.EqualTo(expected));
		}

		/// <summary>Checks a mixed archive produces several components and suppresses only explicit documentation.</summary>
		[Test]
		public void MixedWitcherArchiveMatchesExistingPlanWithoutChangingComponentOwnership()
		{
			var selection = Files("Pack/Mods/modA/content/a.ws", "Pack/Mods/modB/content/b.ws", "Pack/DLC/dlcC/content/c.bundle", "Pack/README.md");
			ManagedContentLayoutPlan plan = DataDrivenManagedContentRules.Create(Load("Witcher3Remastered.game.json").ManagedContent).Plan(selection, "id", "Name", Context());
			Assert.That(plan.IsSupported, Is.True, String.Join("; ", plan.Issues));
			Assert.That(plan.Files.Select(file => file.DestinationPath), Is.EqualTo(Witcher3BasicInstallMapping.Map(selection, "id", "Name", Witcher3Edition.Remastered, Context()).Select(file => file.Value)));
			Assert.That(plan.Files.Select(file => file.ComponentKind), Is.EqualTo(new[] { "mod-folder", "mod-folder", "dlc-folder" }));
			Assert.That(plan.IgnoredFiles, Is.EqualTo(new[] { "Pack/README.md" }));
		}

		/// <summary>Rejects unsupported/unsafe content instead of partially deploying the recognized payload.</summary>
		[TestCase("../escape")]
		[TestCase("C:/absolute")]
		[TestCase("Mods/modA/content/a:stream")]
		[TestCase("Mods/modA/content/NUL.txt")]
		[TestCase("Mods/modA/content/trailing.")]
		[TestCase("Mods//modA/content/a.ws")]
		[TestCase("Mods/modA/content/../a.ws")]
		[TestCase("Mods/modA/content/config.part.txt")]
		[TestCase("bin/config/r4game/user_config_matrix/pc/hud.xml")]
		[TestCase("Documents/The Witcher 3/mods.settings")]
		[TestCase("fomod/ModuleConfig.xml")]
		[TestCase("manual.ini")]
		public void UnsafeAndUnsupportedPayloadNeverProducesExecutablePlan(string path)
		{
			ManagedContentLayoutPlan result = DataDrivenManagedContentRules.Create(Load("Witcher3Remastered.game.json").ManagedContent)
				.Plan(Files("Mods/modGood/content/good.ws", path), "id", "Name", Context());
			Assert.That(result.IsSupported, Is.False);
			Assert.That(result.Issues, Is.Not.Empty);
		}

		/// <summary>Rejects Windows aliases, parent files, repeated sources, inconsistent component case and wrappers.</summary>
		[TestCase("Mods/modA/content/a.ws", "mods/modA/content/A.ws")]
		[TestCase("Mods/modA/content/a.ws", "Mods/modA/content/a.ws/child")]
		[TestCase("Mods/modA/content/a.ws", "Mods/moda/content/b.ws")]
		[TestCase("A/Mods/modA/content/a.ws", "B/Mods/modB/content/b.ws")]
		public void CollisionsAndInconsistentRootsBlockWholePlan(string first, string second)
		{
			Assert.That(DataDrivenManagedContentRules.Create(Load("Witcher3Remastered.game.json").ManagedContent)
				.Plan(Files(first, second), "id", "Name", Context()).IsSupported, Is.False);
		}

		/// <summary>Honors real selected destinations and rejects ambiguity rather than selecting a rule by list order.</summary>
		[Test]
		public void ActualSelectionAndRuleAmbiguityAreExplicit()
		{
			GameModeDefinition definition = Load("ManagedContentSample.game.json");
			var selection = new[] { new KeyValuePair<string, string>("choices/a.bin", "Packages/pkgSelected/assets/a.bin") };
			ManagedContentLayoutPlan plan = DataDrivenManagedContentRules.Create(definition.ManagedContent).Plan(selection, "id", "Name", Context());
			Assert.That(plan.IsSupported, Is.True);
			Assert.That(plan.Files.Single().DestinationPath, Is.EqualTo(@"Packages\pkgSelected\assets\a.bin"));
			GameModeInstallLayoutRuleDefinition duplicate = definition.ManagedContent.InstallLayout.Rules[0];
			definition.ManagedContent.InstallLayout.Rules.Add(new GameModeInstallLayoutRuleDefinition
			{
				Id = "overlap", Kind = duplicate.Kind, SourceRoot = duplicate.SourceRoot, ComponentPrefix = duplicate.ComponentPrefix,
				ContentFolder = duplicate.ContentFolder, TargetRoot = duplicate.TargetRoot, TargetFolder = duplicate.TargetFolder,
				ComponentKind = duplicate.ComponentKind, MaximumComponentNameLength = duplicate.MaximumComponentNameLength
			});
			Assert.That(DataDrivenManagedContentRules.Create(definition.ManagedContent).Plan(selection, "id", "Name", Context()).IsSupported, Is.False);
		}

		/// <summary>Changing settings after resolution cannot mutate a prepared provider; plan identity binds configuration and method.</summary>
		[Test]
		public void ProviderSnapshotAndPlanIdentityBindConfigurationSelectionAndMethod()
		{
			GameModeDefinition definition = Load("ManagedContentSample.game.json");
			ManagedContentLayoutPlanner planner = DataDrivenManagedContentRules.Create(definition.ManagedContent);
			var selection = Files("assets/a.bin");
			ManagedContentLayoutPlan direct = planner.Plan(selection, "id", "Name", Context());
			Assert.That(direct.Identity, Is.EqualTo(planner.Plan(selection, "id", "Name", Context()).Identity));
			Assert.That(direct.Identity, Is.Not.EqualTo(planner.Plan(selection, "id", "Name", Context(ModInstallMethod.Virtual)).Identity));
			definition.ManagedContent.InstallLayout.Revision++;
			Assert.That(direct.Identity, Is.Not.EqualTo(DataDrivenManagedContentRules.Create(definition.ManagedContent).Plan(selection, "id", "Name", Context()).Identity));
			definition.ManagedContent.InstallLayout.Rules[1].TargetFolder = "Changed";
			Assert.That(planner.Plan(selection, "id", "Name", Context()).Files.Single().DestinationPath, Does.StartWith(@"Packages\pkgName_"));
			Assert.That(planner.Plan(selection, null, "Name", Context()).IsSupported, Is.False);
			Assert.That(planner.Plan(selection, "id", "Name", new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot)).IsSupported, Is.False);
		}

		/// <summary>A second game constructs from JSON alone and feeds the actual native BasicInstall planner in both methods.</summary>
		[TestCase(ModInstallMethod.Direct)]
		[TestCase(ModInstallMethod.Virtual)]
		public void JsonOnlySampleUsesNativeExactMappingAndStablePathProjection(ModInstallMethod method)
		{
			GameModeDefinition definition = Load("ManagedContentSample.game.json");
			string target = Path.Combine(Path.GetTempPath(), "nmm-layout-no-write-" + Guid.NewGuid().ToString("N"));
			IEnvironmentInfo environment = EnvironmentFor(definition.ModeId, target);
			Nexus.Client.UI.ViewMessage warning;
			IGameMode mode = new DataDrivenGameModeFactory(environment, definition).BuildGameMode(null, out warning);
			Assert.That(warning, Is.Null); Assert.That(mode, Is.TypeOf<DataDrivenManagedContentGameMode>());
			IMod mod = Mod("Pack/Packages/pkgA/assets/a.bin", "Pack/Packages/pkgB/assets/b.bin", "Pack/readme.md");
			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(mod, mode, Context(method), false);
			Assert.That(result.IsSupported, Is.True, result.Message);
			Assert.That(result.Plan.Files.Select(file => file.DestinationPath), Is.EqualTo(new[] { @"Packages\pkgA\assets\a.bin", @"Packages\pkgB\assets\b.bin" }));
			Assert.That(result.Plan.Files.All(file => file.DeploymentTarget.Root == ModDeploymentRoot.Data), Is.True);
			if (method == ModInstallMethod.Virtual) Assert.That(result.Plan.Files.Select(file => file.VirtualStoragePath), Is.EqualTo(result.Plan.Files.Select(file => file.DestinationPath)));
			string path = result.Plan.Files[0].DestinationPath;
			Assert.That(mode.GetModFormatAdjustedPath(null, path, false), Is.EqualTo(path));
			Assert.Throws<InvalidDataException>(() => mode.GetModFormatAdjustedPath(null, "assets/a.bin", false));
			Assert.Throws<InvalidDataException>(() => mode.GetModFormatAdjustedPath(null, "Pack/Packages/pkgA/assets/a.bin", false));
			Assert.That(Directory.Exists(target), Is.False, "Planning/initialization must not create a live target.");
		}

		/// <summary>Invalid capability requests cannot silently downgrade to generic extraction or unlock reserved Witcher identity.</summary>
		[TestCase("provider")]
		[TestCase("version")]
		[TestCase("rule-kind")]
		[TestCase("duplicate-id")]
		[TestCase("root")]
		[TestCase("path")]
		[TestCase("profile")]
		[TestCase("reserved-id")]
		[TestCase("missing")]
		public void InvalidCapabilitiesAreRejected(string invalid)
		{
			GameModeDefinition definition = Load("ManagedContentSample.game.json");
			switch (invalid)
			{
				case "provider": definition.ManagedContent.InstallLayout.Provider = "unknown"; break;
				case "version": definition.ManagedContent.SchemaVersion = 2; break;
				case "rule-kind": definition.ManagedContent.InstallLayout.Rules[0].Kind = "script"; break;
				case "duplicate-id": definition.ManagedContent.InstallLayout.Rules[1].Id = definition.ManagedContent.InstallLayout.Rules[0].Id.ToUpperInvariant(); break;
				case "root": definition.ManagedContent.InstallLayout.Rules[0].TargetRoot = "Documents"; break;
				case "path": definition.ManagedContent.InstallLayout.Rules[0].TargetFolder = "../outside"; break;
				case "profile": definition.BehaviorProfile = "generic"; break;
				case "reserved-id": definition.ModeId = "Witcher3Remastered"; break;
				case "missing": definition.ManagedContent = null; break;
			}
			Assert.That(new GameModeDefinitionValidator().Validate(definition).Any(issue => issue.Severity == GameModeDefinitionIssueSeverity.Error), Is.True);
			Nexus.Client.UI.ViewMessage warning;
			Assert.That(new DataDrivenGameModeFactory(EnvironmentFor(definition.ModeId, null), definition).BuildGameMode(null, out warning), Is.Null);
			Assert.That(warning, Is.Not.Null);
		}

		/// <summary>Unknown optional capabilities are rejected, and one invalid definition cannot hide valid definitions.</summary>
		[Test]
		public void LoaderRejectsUnknownCapabilitiesWithoutLosingValidGame()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-layout-loader-" + Guid.NewGuid().ToString("N"));
			string json = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ManagedContentSample.game.json"));
			try
			{
				Directory.CreateDirectory(Path.Combine(directory, "valid"));
				Directory.CreateDirectory(Path.Combine(directory, "invalid"));
				File.WriteAllText(Path.Combine(directory, "valid", "game.json"), json);
				File.WriteAllText(Path.Combine(directory, "invalid", "game.json"), json.Replace("\"installLayout\": {", "\"mergeEffects\": {}, \"installLayout\": {"));
				GameModeDefinitionLoadResult result = new GameModeDefinitionLoader().LoadFromDirectory(directory);
				Assert.That(result.Definitions.Select(definition => definition.ModeId), Is.EqualTo(new[] { "ManagedContentSample" }));
				Assert.That(result.Issues.Any(issue => issue.Severity == GameModeDefinitionIssueSeverity.Error && issue.PropertyPath.Contains("mergeEffects")), Is.True);
			}
			finally { Directory.Delete(directory, true); }
		}

		/// <summary>Existing schema-two ordinary modes keep their original runtime type and do not gain a mapping capability.</summary>
		[Test]
		public void OrdinaryGenericModeDoesNotGainManagedMappingOrChangeInitialization()
		{
			GameModeDefinition definition = Load("ManagedContentSample.game.json");
			definition.ManagedContent = null; definition.BehaviorProfile = "generic";
			Nexus.Client.UI.ViewMessage warning;
			IGameMode mode = new DataDrivenGameModeFactory(EnvironmentFor(definition.ModeId, null), definition).BuildGameMode(null, out warning);
			Assert.That(warning, Is.Null); Assert.That(mode, Is.TypeOf<DataDrivenGameMode>());
			Assert.That(mode is IReviewedBasicInstallMappingProvider, Is.False);
		}

		/// <summary>Configuration cannot bypass reserved mode identity or activate a generic copy of the Remastered game.</summary>
		[Test]
		public void FactoryStillBlocksRemasteredEvenWithManagedProfileOverride()
		{
			GameModeDefinition definition = Load("Witcher3Remastered.game.json");
			definition.BehaviorProfile = "managed-content";
			Nexus.Client.UI.ViewMessage warning;
			IGameMode mode = new DataDrivenGameModeFactory(EnvironmentFor(definition.ModeId, null), definition).BuildGameMode(null, out warning);
			Assert.That(mode, Is.Null); Assert.That(warning, Is.Not.Null);
		}

		/// <summary>Bounds folder names, wrapper depth and the number of paths examined even when every input is invalid.</summary>
		[Test]
		public void BoundedPlanningRejectsOverlongAndUnboundedSelections()
		{
			ManagedContentLayoutPlanner planner = DataDrivenManagedContentRules.Create(Load("Witcher3Remastered.game.json").ManagedContent);
			Assert.That(planner.Plan(Files("Mods/mod" + new string('A', 61) + "/content/a.ws"), "id", "Name", Context()).IsSupported, Is.False);
			Assert.That(planner.Plan(Files("one/two/three/Mods/modA/content/a.ws"), "id", "Name", Context()).IsSupported, Is.False);
			Assert.That(planner.Plan(Enumerable.Repeat(new KeyValuePair<string, string>("../invalid", null), 100001), "id", "Name", Context()).Issues.Any(issue => issue.Contains("selection limit")), Is.True);
			Assert.That(planner.Plan(Files("Mods/modA/content/a.ws", "README.md/nested.txt"), "id", "Name", Context()).IsSupported, Is.False);
		}

		/// <summary>Provides configured paths but fails if initialization attempts to save settings.</summary>
		private static IEnvironmentInfo EnvironmentFor(string modeId, string path)
		{
			var paths = new PerGameModeSettings<string>(); paths[modeId] = path;
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				if (method.Name == "Save") Assert.Fail("Read-only initialization cannot save settings.");
				if (method.Name == "get_InstallationPaths" || method.Name == "get_ExecutablePaths") return paths;
				return null;
			});
			return InterfaceStub<IEnvironmentInfo>.Create((method, args) => method.Name == "get_Settings" ? settings : null);
		}

		/// <summary>Creates a bounded non-scripted archive inventory through the existing test proxy.</summary>
		private static IMod Mod(params string[] files) => InterfaceStub<IMod>.Create((method, args) =>
		{
			if (method.Name == "GetFileList") return files.ToList();
			if (method.Name == "get_Filename") return @"C:\Downloads\Example.7z";
			if (method.Name == "get_ModName") return "Example";
			return null;
		});
	}
}
