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
		[Test]
		public void SessionGate_AllowsOnlyReadyManagedCandidates_AndSerializesFirstLoad()
		{
			var settings = NewSettings();
			settings.CompletedSetup["Configured"] = true;
			settings.ExecutablePaths["Configured"] = @"C:\Games\Configured";
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
				settings.ExecutablePaths[id] = @"C:\Games\" + id;
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
			managedSettings.ExecutablePaths["Ready"] = @"C:\Games\Ready";
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
				settings.ExecutablePaths[id] = @"C:\Games\" + id;
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

		private static GameDiscoveryEntry Single(Nexus.Client.Properties.Settings settings, string id)
		{
			return GameDiscoveryCatalogBuilder.Build(new[] { new Descriptor(id) }, settings).Entries.Single();
		}

		private static Nexus.Client.Properties.Settings NewSettings()
		{
			return new Nexus.Client.Properties.Settings
			{
				InstalledGames = new SettingsList(),
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
