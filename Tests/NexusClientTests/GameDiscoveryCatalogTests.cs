using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using Nexus.Client;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Settings;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class GameDiscoveryCatalogTests
	{
		private string _gameRoot;

		/// <summary>Provides a real executable for admission-policy cases that represent an installed game.</summary>
		[SetUp]
		public void SetUp()
		{
			_gameRoot = Path.Combine(Path.GetTempPath(), "nmm-catalog-game-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_gameRoot);
			File.WriteAllText(Path.Combine(_gameRoot, "game.exe"), "game");
		}

		/// <summary>Removes only the executable fixture created for the current case.</summary>
		[TearDown]
		public void TearDown() { Directory.Delete(_gameRoot, true); }

		[Test]
		public void SessionGate_AllowsOnlyReadyManagedCandidates_AndSerializesFirstLoad()
		{
			var settings = NewSettings();
			settings.CompletedSetup["Configured"] = true;
			settings.ExecutablePaths["Configured"] = _gameRoot;
			settings.InstallationPaths["Configured"] = @"D:\NMM\Configured";
			var catalog = GameDiscoveryCatalogBuilder.Build(new[] { new Descriptor("Configured"), new Descriptor("Unseen") }, settings);
			var managed = catalog.Entries.Single(e => e.ModeId == "Configured");
			var supported = catalog.Entries.Single(e => e.ModeId == "Unseen");
			var gate = new GameSessionEntryGate();
			Assert.That(GameSessionEntryGate.CanManage(managed), Is.True);
			Assert.That(GameSessionEntryGate.CanManage(supported), Is.False);
			Assert.That(gate.TryBegin(supported), Is.False);
			Assert.That(gate.TryBegin(managed), Is.True);
			Assert.That(gate.TryBegin(managed), Is.False);
			Assert.That(gate.State, Is.EqualTo(GameSessionEntryGate.EntryState.Loading));
			Assert.Throws<InvalidOperationException>(() => gate.Finish("Wrong", true));
			gate.Finish("configured", true);
			Assert.That(gate.State, Is.EqualTo(GameSessionEntryGate.EntryState.Ready));
			Assert.That(gate.TryBegin(managed), Is.False);
		}

		[Test]
		public void SessionGate_SwitchRequiresConfirmedExit_ThenAdmitsAnotherManagedGame()
		{
			var settings = NewSettings();
			foreach (string id in new[] { "First", "Second" })
			{
				settings.CompletedSetup[id] = true;
				settings.ExecutablePaths[id] = _gameRoot;
				settings.InstallationPaths[id] = @"D:\NMM\" + id;
			}
			var entries = GameDiscoveryCatalogBuilder.Build(
				new[] { new Descriptor("First"), new Descriptor("Second") }, settings).Managed.ToArray();
			var first = entries.Single(x => x.ModeId == "First");
			var second = entries.Single(x => x.ModeId == "Second");
			var gate = new GameSessionEntryGate();
			Assert.That(gate.TryBegin(first), Is.True);
			Assert.Throws<InvalidOperationException>(() => gate.EndSession("First"));
			gate.Finish("First", true);
			Assert.That(gate.TryBegin(second), Is.False, "A new initializer cannot start before native teardown.");
			Assert.Throws<InvalidOperationException>(() => gate.EndSession("Second"));
			gate.EndSession("first");
			Assert.That(gate.State, Is.EqualTo(GameSessionEntryGate.EntryState.NoGame));
			Assert.That(gate.ModeId, Is.Null);
			Assert.That(gate.TryBegin(second), Is.True);
			gate.Finish("Second", true);
			gate.EndSession("Second");
			Assert.That(gate.TryBegin(first), Is.True, "The first mode can be re-entered after a full A-B exit.");
		}

		[Test]
		public void SessionGate_FailureAllowsRetry_ButNotImplicitConfiguration()
		{
			var settings = NewSettings();
			settings.CompletedSetup["NeedsReview"] = true;
			var blocked = GameDiscoveryCatalogBuilder.Build(new[] { new Descriptor("NeedsReview") }, settings).Entries.Single();
			Assert.That(blocked.NeedsAttention, Is.True);
			Assert.That(GameSessionEntryGate.CanManage(blocked), Is.False);
			var managedSettings = NewSettings();
			managedSettings.CompletedSetup["Ready"] = true;
			managedSettings.ExecutablePaths["Ready"] = _gameRoot;
			managedSettings.InstallationPaths["Ready"] = @"D:\NMM\Ready";
			var ready = GameDiscoveryCatalogBuilder.Build(new[] { new Descriptor("Ready") }, managedSettings).Entries.Single();
			var gate = new GameSessionEntryGate();
			Assert.That(gate.TryBegin(ready), Is.True);
			gate.Finish("Ready", false);
			Assert.That(gate.State, Is.EqualTo(GameSessionEntryGate.EntryState.Failed));
			Assert.That(gate.TryBegin(ready), Is.True);
		}

		[Test]
		public void StartupDefault_ManagedGameIsExclusiveAndMayBeCleared()
		{
			var settings = NewSettings();
			foreach (string id in new[] { "First", "Second" })
			{
				settings.CompletedSetup[id] = true;
				settings.ExecutablePaths[id] = _gameRoot;
				settings.InstallationPaths[id] = @"D:\NMM\" + id;
			}
			var descriptors = new[] { new Descriptor("First"), new Descriptor("Second"), new Descriptor("Supported") };
			var catalog = GameDiscoveryCatalogBuilder.Build(descriptors, settings);
			Assert.That(GameDiscoveryStartupPreference.Toggle(settings, catalog.Entries.Single(e => e.ModeId == "First")), Is.True);
			Assert.That(settings.RememberGameMode, Is.True);
			Assert.That(settings.RememberedGameMode, Is.EqualTo("First"));
			catalog = GameDiscoveryCatalogBuilder.Build(descriptors, settings);
			Assert.That(GameDiscoveryStartupPreference.Toggle(settings, catalog.Entries.Single(e => e.ModeId == "Second")), Is.True);
			catalog = GameDiscoveryCatalogBuilder.Build(descriptors, settings);
			Assert.That(catalog.Entries.Count(e => e.IsStartupDefault), Is.EqualTo(1));
			Assert.That(catalog.Entries.Single(e => e.IsStartupDefault).ModeId, Is.EqualTo("Second"));
			Assert.That(GameDiscoveryStartupPreference.Toggle(settings, catalog.Entries.Single(e => e.ModeId == "Second")), Is.True);
			Assert.That(settings.RememberGameMode, Is.False);
			Assert.That(settings.RememberedGameMode, Is.Empty);
			Assert.That(GameDiscoveryCatalogBuilder.Build(descriptors, settings).Entries.Any(e => e.IsStartupDefault), Is.False);
		}

		[Test]
		public void StartupDefault_DoesNotSelectUnsupportedOrUnconfiguredGame()
		{
			var settings = NewSettings();
			settings.InstalledGames.Add("Found");
			settings.ExecutablePaths["Found"] = @"C:\Games\Found";
			var catalog = GameDiscoveryCatalogBuilder.Build(
				new[] { new Descriptor("Found"), new Descriptor("Supported") }, settings);
			Assert.That(GameDiscoveryStartupPreference.Toggle(settings, catalog.Entries.Single(e => e.ModeId == "Found")), Is.False);
			Assert.That(GameDiscoveryStartupPreference.Toggle(settings, catalog.Entries.Single(e => e.ModeId == "Supported")), Is.False);
			Assert.That(settings.RememberGameMode, Is.False);
		}

		[Test]
		public void ProjectsAllThreeGroups_WithoutMutatingLegacySettings()
		{
			var settings = NewSettings();
			settings.InstalledGames.Add("Found");
			settings.ExecutablePaths["Managed"] = @"C:\Games\Managed";
			settings.InstallationPaths["Managed"] = @"D:\ManagedData";
			settings.CompletedSetup["Managed"] = true;
			settings.ExecutablePaths["Found"] = @"C:\Games\Found";
			var before = settings.ExecutablePaths.ToDictionary(pair => pair.Key, pair => pair.Value);

			var catalog = GameDiscoveryCatalogBuilder.Build(new[]
			{
				new Descriptor("Managed"), new Descriptor("Found"), new Descriptor("Unseen")
			}, settings);

			Assert.That(catalog.Managed.Select(x => x.ModeId), Is.EquivalentTo(new[] { "Managed" }));
			Assert.That(catalog.ToConfigure.Select(x => x.ModeId), Is.EquivalentTo(new[] { "Found" }));
			Assert.That(catalog.Supported.Select(x => x.ModeId), Is.EquivalentTo(new[] { "Unseen" }));
			Assert.That(settings.ExecutablePaths, Has.Count.EqualTo(2));
			Assert.That(settings.ExecutablePaths, Is.EquivalentTo(before));
			Assert.That(settings.CompletedSetup, Has.Count.EqualTo(1));
			Assert.That(settings.InstalledGamesDetected, Is.False);
		}

		[Test]
		public void CompleteLegacyStorage_IsManagedWithoutCompletedSetupOrInstalledRegistration()
		{
			var settings = NewSettings();
			settings.ExecutablePaths["Old"] = @"C:\Games\Old";
			settings.InstallationPaths["Old"] = @"C:\Games\Old";
			settings.InstallInfoFolder["Old"] = @"D:\NMM\InstallInfo";
			settings.ModFolder["Old"] = @"D:\NMM\Mods";
			settings.VirtualFolder["Old"] = @"D:\NMM";
			settings.HDLinkFolder["Old"] = @"C:\NMM\Links";
			settings.MultiHDInstall["Old"] = true;

			var entry = Single(settings, "Old");
			Assert.That(entry.Collection, Is.EqualTo(GameDiscoveryCollection.Managed));
			Assert.That(entry.HasLegacyStoragePaths, Is.True);
			Assert.That(entry.CompletedSetup, Is.False);
			Assert.That(entry.NeedsAttention, Is.False);
			Assert.That(settings.CompletedSetup, Has.Count.EqualTo(0));
		}

		[Test]
		public void IncompleteLegacyPaths_DoNotBecomeManagedAndAreNotReplaced()
		{
			var settings = NewSettings();
			settings.InstallInfoFolder["Old"] = @"D:\NMM\InstallInfo";
			var entry = Single(settings, "Old");
			Assert.That(entry.Collection, Is.EqualTo(GameDiscoveryCollection.ToConfigure));
			Assert.That(entry.NeedsAttention, Is.True);
			Assert.That(settings.ModFolder, Is.Empty);
		}

		[Test]
		public void CompletedSetupWithoutAvailablePath_RemainsManagedAndNeedsAttention()
		{
			var settings = NewSettings();
			settings.CompletedSetup["Old"] = true;
			var entry = Single(settings, "Old");
			Assert.That(entry.Collection, Is.EqualTo(GameDiscoveryCollection.Managed));
			Assert.That(entry.NeedsAttention, Is.True);
			Assert.That(settings.CompletedSetup["Old"], Is.True);
		}

		[Test]
		public void NewCandidatesOnlyMoveUnconfiguredGames_AndNeverOverwriteSavedPaths()
		{
			var settings = NewSettings();
			settings.ExecutablePaths["Old"] = @"C:\OldGame";
			var candidates = new[]
			{
				new GameDiscoveryInstallationCandidate("oLD", @"C:\OldGame\", "Steam"),
				new GameDiscoveryInstallationCandidate("OLD", @"E:\NewGame", "Gog"),
				new GameDiscoveryInstallationCandidate("NEW", @"D:\Game", "Epic")
			};
			var catalog = GameDiscoveryCatalogBuilder.Build(new[] { new Descriptor("Old"), new Descriptor("New") }, settings, candidates);
			var old = catalog.Entries.Single(e => e.ModeId == "Old");
			Assert.That(old.Candidates.Count, Is.EqualTo(2));
			Assert.That(old.Candidates[0].Source, Is.EqualTo("Saved"));
			Assert.That(old.Candidates[1].GameRoot, Is.EqualTo(@"E:\NewGame"));
			Assert.That(catalog.ToConfigure.Count(), Is.EqualTo(2));
			Assert.That(settings.ExecutablePaths["Old"], Is.EqualTo(@"C:\OldGame"));
			Assert.That(settings.ExecutablePaths.ContainsKey("New"), Is.False);
		}

		[Test]
		public void PreviouslySavedUnsupportedGame_ProducesWarningWithoutInventingFourthGroup()
		{
			var settings = NewSettings();
			settings.InstalledGames.Add("RetiredMode");
			settings.CompletedSetup["RetiredMode"] = true;
			var catalog = GameDiscoveryCatalogBuilder.Build(new[] { new Descriptor("Known") }, settings);
			Assert.That(catalog.UnavailableSavedModeIds, Is.EquivalentTo(new[] { "RetiredMode" }));
			Assert.That(catalog.Entries.Count, Is.EqualTo(1));
			Assert.That(settings.InstalledGames.Cast<string>(), Does.Contain("RetiredMode"));
		}

		[Test]
		public void ListingDoesNotReadSideEffectingDescriptorInstallationPath()
		{
			var descriptor = new Descriptor("Example");
			Assert.DoesNotThrow(() => GameDiscoveryCatalogBuilder.Build(new[] { descriptor }, NewSettings()));
			Assert.That(descriptor.InstallationPathReadCount, Is.EqualTo(0));
		}

		/// <summary>Launch history orders managed games by recency, with alphabetical ties and unknown dates last.</summary>
		[Test]
		public void ManagedGames_RecentLaunchesFirst_UnknownDatesLastAlphabetically()
		{
			var settings = NewSettings();
			string[] ids = { "ZuluUnknown", "Oldest", "BravoRecent", "AlphaRecent", "AlphaUnknown", "Invalid", "Empty", "NonUtc" };
			foreach (string id in ids) settings.CompletedSetup[id] = true;
			settings.LastGameLaunchUtc["Oldest"] = "2025-01-01T10:00:00.0000000Z";
			settings.LastGameLaunchUtc["BravoRecent"] = "2026-01-01T10:00:00.0000000Z";
			settings.LastGameLaunchUtc["AlphaRecent"] = "2026-01-01T10:00:00.0000000Z";
			settings.LastGameLaunchUtc["Invalid"] = "not a date";
			settings.LastGameLaunchUtc["Empty"] = String.Empty;
			settings.LastGameLaunchUtc["NonUtc"] = "2026-01-01T10:00:00.0000000";
			var catalog = GameDiscoveryCatalogBuilder.Build(ids.Select(id => new Descriptor(id)), settings);
			Assert.That(catalog.Managed.Select(entry => entry.ModeId), Is.EqualTo(new[]
			{
				"AlphaRecent", "BravoRecent", "Oldest", "AlphaUnknown", "Empty", "Invalid", "NonUtc", "ZuluUnknown"
			}));
			Assert.That(catalog.Managed.First().LastPlayedUtc.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
			Assert.That(catalog.Managed.Where(entry => entry.ModeId == "Invalid").Single().LastPlayedUtc, Is.Null);
			Assert.That(settings.LastGameLaunchUtc["Invalid"], Is.EqualTo("not a date"), "Reading history must not rewrite it.");
		}

		/// <summary>Old settings with no history dictionary remain valid and keep alphabetical ordering.</summary>
		[Test]
		public void ManagedGames_NullHistory_UsesAlphabeticalOrder()
		{
			var settings = NewSettings();
			settings.LastGameLaunchUtc = null;
			settings.CompletedSetup["Zulu"] = true;
			settings.CompletedSetup["Alpha"] = true;
			var catalog = GameDiscoveryCatalogBuilder.Build(new[] { new Descriptor("Zulu"), new Descriptor("Alpha") }, settings);
			Assert.That(catalog.Managed.Select(entry => entry.ModeId), Is.EqualTo(new[] { "Alpha", "Zulu" }));
			Assert.That(catalog.Managed.All(entry => !entry.LastPlayedUtc.HasValue), Is.True);
		}

		/// <summary>Historical launches do not change alphabetical ordering outside the managed section.</summary>
		[Test]
		public void UnmanagedGames_LaunchHistory_DoesNotChangeTheirOrder()
		{
			var settings = NewSettings();
			settings.InstalledGames.Add("ZuluConfigure");
			settings.InstalledGames.Add("AlphaConfigure");
			settings.LastGameLaunchUtc["ZuluConfigure"] = "2026-01-01T10:00:00.0000000Z";
			settings.LastGameLaunchUtc["ZuluSupported"] = "2026-01-01T10:00:00.0000000Z";
			string[] ids = { "ZuluSupported", "ZuluConfigure", "AlphaSupported", "AlphaConfigure" };
			var catalog = GameDiscoveryCatalogBuilder.Build(ids.Select(id => new Descriptor(id)), settings);
			Assert.That(catalog.ToConfigure.Select(entry => entry.ModeId), Is.EqualTo(new[] { "AlphaConfigure", "ZuluConfigure" }));
			Assert.That(catalog.Supported.Select(entry => entry.ModeId), Is.EqualTo(new[] { "AlphaSupported", "ZuluSupported" }));
		}

		/// <summary>Uninstalling a game is visible without discarding its configured storage or reading path getters.</summary>
		[Test]
		public void ManagedGame_MissingExecutable_IsReportedWithoutChangingSavedSetup()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-missing-game-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				var settings = NewSettings();
				settings.CompletedSetup["Example"] = true;
				settings.InstallationPaths["Example"] = root;
				settings.ExecutablePaths["Example"] = root;
				var descriptor = new Descriptor("Example");
				GameDiscoveryEntry missing = GameDiscoveryCatalogBuilder.Build(new[] { descriptor }, settings).Managed.Single();
				Assert.That(missing.IsInstallationMissing, Is.True);
				Assert.That(missing.MissingRequiredFiles, Does.Contain("game.exe"));
				Assert.That(settings.CompletedSetup["Example"], Is.True);
				Assert.That(settings.InstallationPaths["Example"], Is.EqualTo(root));
				Assert.That(descriptor.InstallationPathReadCount, Is.Zero);
				File.WriteAllText(Path.Combine(root, "game.exe"), "game");
				Assert.That(GameDiscoveryCatalogBuilder.Build(new[] { descriptor }, settings).Managed.Single().IsInstallationMissing, Is.False);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Required plugin metadata must be checked without creating the game's missing Data directory.</summary>
		[Test]
		public void ManagedGame_MissingCriticalPlugin_DoesNotCreatePluginDirectory()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-missing-plugin-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				File.WriteAllText(Path.Combine(root, "game.exe"), "game");
				var settings = NewSettings();
				settings.CompletedSetup["Example"] = true;
				settings.InstallationPaths["Example"] = root;
				settings.ExecutablePaths["Example"] = root;
				var definition = new GameModeDefinition { ModeId = "Example", Name = "Example", GameExecutables = new[] { "game.exe" },
					OrderedCriticalPluginNames = new[] { "Game.esm" }, Plugin = new GameModePluginDefinition { PluginDirectorySuffix = "Data" } };
				var descriptor = new DataDrivenGamebryoGameModeDescriptor(null, definition);
				GameDiscoveryEntry missing = GameDiscoveryCatalogBuilder.Build(new[] { descriptor }, settings).Managed.Single();
				Assert.That(missing.MissingRequiredFiles, Does.Contain(Path.Combine(root, "Data", "Game.esm")));
				Assert.That(Directory.Exists(Path.Combine(root, "Data")), Is.False);
				Directory.CreateDirectory(Path.Combine(root, "Data"));
				File.WriteAllText(Path.Combine(root, "Data", "Game.esm"), "master");
				Assert.That(GameDiscoveryCatalogBuilder.Build(new[] { descriptor }, settings).Managed.Single().IsInstallationMissing, Is.False);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Section choices round-trip without changing historical setup or storage, and stop applying after reinstall.</summary>
		[TestCase(GameDiscoveryCollection.ToConfigure)]
		[TestCase(GameDiscoveryCollection.Supported)]
		public void MissingGame_SectionPreferencePreservesSetupAndRestoresManagedOnReinstall(GameDiscoveryCollection destination)
		{
			var settings = NewSettings();
			settings.CompletedSetup["Example"] = true;
			settings.ExecutablePaths["Example"] = _gameRoot;
			settings.InstallationPaths["Example"] = _gameRoot;
			settings.ModFolder["Example"] = "saved mods";
			settings.InstallInfoFolder["Example"] = "saved install info";
			settings.VirtualFolder["Example"] = "saved virtual files";
			settings.InstalledGames.Add("Example");
			settings.GameDiscoverySections["Other"] = "Supported";
			var previous = settings.GameDiscoverySections;
			File.Delete(Path.Combine(_gameRoot, "game.exe"));
			GameDiscoveryEntry missing = Single(settings, "Example");
			Assert.That(GameSessionEntryGate.CanManage(missing), Is.False);
			Assert.That(GameDiscoverySectionPreference.TryMove(settings, missing, destination), Is.True);
			Assert.That(previous.ContainsKey("Example"), Is.False, "Keep the previous dictionary intact for save-failure rollback.");
			var serializer = new System.Xml.Serialization.XmlSerializer(typeof(PerGameModeSettings<string>));
			using (var xml = new StringWriter())
			{
				serializer.Serialize(xml, settings.GameDiscoverySections);
				using (var reader = new StringReader(xml.ToString()))
					settings.GameDiscoverySections = (PerGameModeSettings<string>)serializer.Deserialize(reader);
			}
			GameDiscoveryEntry moved = Single(settings, "Example");
			Assert.That(moved.Collection, Is.EqualTo(destination));
			Assert.That(moved.IsInstallationMissing, Is.True);
			Assert.That(GameDiscoverySectionPreference.TryMove(settings, moved, destination), Is.False);
			Assert.That(settings.CompletedSetup["Example"], Is.True);
			Assert.That(settings.InstallationPaths["Example"], Is.EqualTo(_gameRoot));
			Assert.That(settings.ExecutablePaths["Example"], Is.EqualTo(_gameRoot));
			Assert.That(settings.ModFolder["Example"], Is.EqualTo("saved mods"));
			Assert.That(settings.InstallInfoFolder["Example"], Is.EqualTo("saved install info"));
			Assert.That(settings.VirtualFolder["Example"], Is.EqualTo("saved virtual files"));
			Assert.That(settings.InstalledGames, Does.Contain("Example"));
			Assert.That(settings.GameDiscoverySections["Other"], Is.EqualTo("Supported"));
			File.WriteAllText(Path.Combine(_gameRoot, "game.exe"), "reinstalled");
			GameDiscoveryEntry restored = Single(settings, "Example");
			Assert.That(restored.Collection, Is.EqualTo(GameDiscoveryCollection.Managed));
			Assert.That(restored.IsInstallationMissing, Is.False);
			Assert.That(GameSessionEntryGate.CanManage(restored), Is.True);
			Assert.That(GameDiscoverySectionPreference.TryMove(settings, restored, destination), Is.False);
		}

		/// <summary>Invalid or future section values cannot hide a missing game or manufacture a manageable installation.</summary>
		[TestCase(null)]
		[TestCase("Managed")]
		[TestCase("unknown")]
		[TestCase("1")]
		public void MissingGame_InvalidSectionPreferenceUsesAutomaticCollection(string saved)
		{
			var settings = NewSettings();
			settings.CompletedSetup["Example"] = true;
			settings.ExecutablePaths["Example"] = _gameRoot;
			settings.InstallationPaths["Example"] = _gameRoot;
			settings.GameDiscoverySections["Example"] = saved;
			File.Delete(Path.Combine(_gameRoot, "game.exe"));
			GameDiscoveryEntry entry = Single(settings, "Example");
			Assert.That(entry.Collection, Is.EqualTo(GameDiscoveryCollection.Managed));
			Assert.That(entry.IsInstallationMissing, Is.True);
			Assert.That(GameDiscoverySectionPreference.TryMove(settings, entry, GameDiscoveryCollection.Managed), Is.False);
			Assert.That(settings.GameDiscoverySections["Example"], Is.EqualTo(saved));
		}

		private static GameDiscoveryEntry Single(Nexus.Client.Properties.Settings settings, string id)
		{
			return GameDiscoveryCatalogBuilder.Build(new[] { new Descriptor(id) }, settings).Entries.Single();
		}

		private static Nexus.Client.Properties.Settings NewSettings()
		{
			return new Nexus.Client.Properties.Settings
			{
				InstalledGames = new SettingsList(),
				LastGameLaunchUtc = new PerGameModeSettings<string>(),
				GameDiscoverySections = new PerGameModeSettings<string>(),
				CompletedSetup = new PerGameModeSettings<bool>(),
				InstallationPaths = new PerGameModeSettings<string>(),
				ExecutablePaths = new PerGameModeSettings<string>(),
				InstallInfoFolder = new PerGameModeSettings<string>(),
				ModFolder = new PerGameModeSettings<string>(),
				VirtualFolder = new PerGameModeSettings<string>(),
				HDLinkFolder = new PerGameModeSettings<string>(),
				MultiHDInstall = new PerGameModeSettings<bool>()
			};
		}

		private sealed class Descriptor : GameModeDescriptorBase
		{
			private readonly string _id;
			public Descriptor(string id) : base(null) { _id = id; }
			public int InstallationPathReadCount { get; private set; }
			public override string Name => _id;
			public override string ModeId => _id;
			public override string[] GameExecutables => new[] { "game.exe" };
			public override string InstallationPath
			{
				get { InstallationPathReadCount++; throw new InvalidOperationException("Catalog must not call this."); }
			}
			public override string PluginDirectory => null;
			public override Theme ModeTheme => null;
		}
	}

	[TestFixture]
	public class QuickGameDiscoveryTests
	{
		private string _root;

		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-quick-discovery-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_root);
		}

		[TearDown]
		public void TearDown() { Directory.Delete(_root, true); }

		[Test]
		public void DataDrivenStoreLookup_ValidatesExecutablesAndDoesNotInvokeLegacyFactory()
		{
			string gameRoot = Path.Combine(_root, "Game");
			Directory.CreateDirectory(gameRoot);
			File.WriteAllBytes(Path.Combine(gameRoot, "game.exe"), new byte[] { 1 });
			var definition = new GameModeDefinition
			{
				ModeId = "Fixture", Name = "Fixture", BehaviorProfile = "generic",
				GameExecutables = new[] { "game.exe" },
				Discovery = new GameModeDiscoveryDefinition { SteamAppId = "42" }
			};
			var locator = new QuickGameDiscovery(store => gameRoot,
				factory => { Assert.Fail("Legacy path finder must not run."); return null; });
			var found = locator.Find(new IGameModeFactory[] { new DataDrivenGameModeFactory(new EnvironmentStub(), definition) });
			Assert.That(found.Count, Is.EqualTo(1));
			Assert.That(found[0].ModeId, Is.EqualTo("Fixture"));
			Assert.That(found[0].Source, Is.EqualTo("Steam"));
		}

		[Test]
		public void MultipleStores_AreKeptAsDistinctInstallationCandidates()
		{
			var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string store in new[] { "Epic", "Gog", "Xbox" })
			{
				string path = Path.Combine(_root, store);
				Directory.CreateDirectory(path);
				File.WriteAllBytes(Path.Combine(path, "game.exe"), new byte[] { 1 });
				paths[store] = path;
			}
			var definition = new GameModeDefinition
			{
				ModeId = "Fixture", Name = "Fixture", BehaviorProfile = "generic",
				GameExecutables = new[] { "game.exe" },
				Discovery = new GameModeDiscoveryDefinition
				{
					Stores = new List<GameModeStoreDiscoveryDefinition>
					{
						new GameModeStoreDiscoveryDefinition { Store = "Epic", Id = "epic-id" },
						new GameModeStoreDiscoveryDefinition { Store = "Gog", Id = "gog-id" },
						new GameModeStoreDiscoveryDefinition { Store = "Xbox", Id = "xbox-id" }
					}
				}
			};
			var locator = new QuickGameDiscovery(store => paths[store.Store.ToString()], factory => null);
			var found = locator.Find(new IGameModeFactory[] { new DataDrivenGameModeFactory(new EnvironmentStub(), definition) });
			Assert.That(found.Select(candidate => candidate.Source), Is.EquivalentTo(new[] { "Epic", "Gog", "Xbox" }));
			Assert.That(found.Select(candidate => candidate.GameRoot), Is.EquivalentTo(paths.Values));
		}

		[Test]
		public void OneBrokenStoreDoesNotPreventLaterValidStore()
		{
			string gameRoot = Path.Combine(_root, "Working");
			Directory.CreateDirectory(gameRoot);
			File.WriteAllBytes(Path.Combine(gameRoot, "game.exe"), new byte[] { 1 });
			var definition = new GameModeDefinition
			{
				ModeId = "Fixture", Name = "Fixture", BehaviorProfile = "generic",
				GameExecutables = new[] { "game.exe" },
				Discovery = new GameModeDiscoveryDefinition
				{
					Stores = new List<GameModeStoreDiscoveryDefinition>
					{
						new GameModeStoreDiscoveryDefinition { Store = "Steam", Id = "bad" },
						new GameModeStoreDiscoveryDefinition { Store = "Epic", Id = "working" }
					}
				}
			};
			var locator = new QuickGameDiscovery(store =>
			{
				if (store.Store == GameStore.Steam) throw new IOException("Broken launcher configuration");
				return gameRoot;
			}, factory => null);
			Assert.That(locator.Find(new IGameModeFactory[] { new DataDrivenGameModeFactory(new EnvironmentStub(), definition) })
				.Select(candidate => candidate.Source), Is.EquivalentTo(new[] { "Epic" }));
		}

		[Test]
		public void UnknownFactoryIsNotAllowedToLaunchARecursiveOrUnreviewedLookup()
		{
			int calls = 0;
			var locator = new QuickGameDiscovery(store => { calls++; return null; },
				factory => { calls++; return _root; });
			var found = locator.Find(new IGameModeFactory[] { new FactoryStub() });
			Assert.That(found, Is.Empty);
			Assert.That(calls, Is.Zero);
		}

		[Test]
		public void MissingExecutableOrRejectedEdition_IsNotAccepted()
		{
			var descriptor = new ValidatingDescriptor();
			Assert.That(QuickGameDiscovery.IsVerifiedGameRoot(descriptor, _root), Is.False);
			File.WriteAllBytes(Path.Combine(_root, "game.exe"), new byte[] { 1 });
			descriptor.Accept = false;
			Assert.That(QuickGameDiscovery.IsVerifiedGameRoot(descriptor, _root), Is.False);
			descriptor.Accept = true;
			Assert.That(QuickGameDiscovery.IsVerifiedGameRoot(descriptor, _root), Is.True);
		}

		[Test]
		public void Artwork_UsesPackagedImageThenUserOverrideAndCanReset()
		{
			string definition = Path.Combine(_root, "game.json");
			File.WriteAllText(definition, "{}");
			using (var packaged = new Bitmap(640, 360))
				packaged.Save(Path.Combine(_root, "game-cover.png"), ImageFormat.Png);
			var artwork = new GameDiscoveryArtwork(Path.Combine(_root, "NmmData"), id => definition);
			using (var image = artwork.Load("Fixture"))
			{
				Assert.That(image, Is.Not.Null);
				Assert.That(image.Width, Is.EqualTo(GameDiscoveryArtwork.BannerWidth));
			}
			string squarePath = Path.Combine(_root, "square.png");
			using (var square = new Bitmap(600, 600)) square.Save(squarePath, ImageFormat.Png);
			artwork.Import("Fixture", squarePath);
			Assert.That(artwork.HasOverride("Fixture"), Is.True);
			using (var image = artwork.Load("Fixture"))
			{
				Assert.That(image.Width, Is.EqualTo(GameDiscoveryArtwork.BannerWidth));
				Assert.That(image.Height, Is.EqualTo(GameDiscoveryArtwork.BannerHeight));
			}
			artwork.ClearOverride("Fixture");
			using (var image = artwork.Load("Fixture"))
				Assert.That(image.Width, Is.EqualTo(GameDiscoveryArtwork.BannerWidth));
			Assert.That(artwork.HasOverride("Fixture"), Is.False);
		}

		[Test]
		public void Artwork_RejectsTraversalAndMissingPackagedImageIsOptional()
		{
			var artwork = new GameDiscoveryArtwork(Path.Combine(_root, "NmmData"), id =>
				Path.Combine(_root, "missing", "game.json"));
			Assert.That(artwork.Load("Fixture"), Is.Null);
			Assert.Throws<ArgumentException>(() => artwork.Import("../outside", "anything.png"));
		}

		[Test]
		public void CancellationStopsQuickDiscoveryBeforeAnyLookup()
		{
			var locator = new QuickGameDiscovery(store => { Assert.Fail("Lookup after cancellation"); return null; },
				factory => { Assert.Fail("Legacy lookup after cancellation"); return null; });
			Assert.Throws<OperationCanceledException>(() => locator.Find(
				new IGameModeFactory[] { new FactoryStub() }, new CancellationToken(true)));
		}

		private sealed class EnvironmentStub : IEnvironmentInfo
		{
			public string ApplicationPersonalDataFolderPath => null;
			public string PersonalDataFolderPath => null;
			public bool IsMonoMode => false;
			public string TemporaryPath => null;
			public string ProgrammeInfoDirectory => null;
			public bool Is64BitProcess => true;
			public ISettings Settings => null;
			public Version ApplicationVersion => new Version(1, 0);
		}

		private sealed class ValidatingDescriptor : GameModeDescriptorBase, IGameInstallationCandidateValidator
		{
			public ValidatingDescriptor() : base(null) { }
			public bool Accept { get; set; }
			public override string Name => "Test";
			public override string ModeId => "Test";
			public override string[] GameExecutables => new[] { "game.exe" };
			public override string PluginDirectory => null;
			public override Theme ModeTheme => null;
			public bool IsValidInstallationCandidate(string absoluteGameRoot) => Accept;
		}

		private sealed class FactoryStub : IGameModeFactory
		{
			public IGameModeDescriptor GameModeDescriptor => new ValidatingDescriptor();
			public string GetInstallationPath() { Assert.Fail("Unreviewed factory must not be consulted."); return null; }
			public string GetInstallationPath(string path) => path;
			public string GetExecutablePath(string path) => path;
			public IGameMode BuildGameMode(Nexus.Client.Util.FileUtil util, out Nexus.Client.UI.ViewMessage warning)
			{
				warning = null;
				throw new NotSupportedException();
			}
			public bool PerformInitialSetup(Nexus.Client.UI.ShowViewDelegate showView, Nexus.Client.UI.ShowMessageDelegate showMessage)
			{
				throw new NotSupportedException();
			}
			public bool PerformInitialization(Nexus.Client.UI.ShowViewDelegate showView, Nexus.Client.UI.ShowMessageDelegate showMessage)
			{
				throw new NotSupportedException();
			}
		}
	}
}
