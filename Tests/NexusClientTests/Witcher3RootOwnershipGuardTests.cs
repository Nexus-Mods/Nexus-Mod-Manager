using System;
using System.IO;
using Nexus.Client.ModManagement.Witcher3;
using Nexus.Client.GameStorage;
using Nexus.Client;
using Nexus.Client.Settings;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3RootOwnershipGuardTests
	{
		private string _fixture;

		[SetUp]
		public void SetUp()
		{
			_fixture = Path.Combine(Path.GetTempPath(), "nmm-w3r-root-guard-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_fixture);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_fixture)) Directory.Delete(_fixture, true);
		}

		[Test]
		public void RecognizesOnlyTheTwoKnownWitcherModeIds()
		{
			Assert.That(Witcher3RootOwnershipGuard.IsWitcherMode("Witcher3"), Is.True);
			Assert.That(Witcher3RootOwnershipGuard.IsWitcherMode("WITCHER3REMASTERED"), Is.True);
			Assert.That(Witcher3RootOwnershipGuard.IsWitcherMode("SkyrimSE"), Is.False);
			Assert.That(Witcher3RootOwnershipGuard.CounterpartModeId("Witcher3"), Is.EqualTo("Witcher3Remastered"));
			Assert.That(Witcher3RootOwnershipGuard.CounterpartModeId("Witcher3Remastered"), Is.EqualTo("Witcher3"));
			Assert.Throws<ArgumentException>(() => Witcher3RootOwnershipGuard.CounterpartModeId("SkyrimSE"));
		}

		[Test]
		public void MissingStoreDoesNotFabricateNativeOwnership()
		{
			Assert.That(Witcher3RootOwnershipGuard.HasNativeStoreFootprint(_fixture), Is.False);
		}

		[TestCase("InstallLog.xml")]
		[TestCase("InstallLog.sqlite")]
		[TestCase("InstallLog.sqlite.authority")]
		[TestCase("InstallLog.sqlite.bak")]
		[TestCase("InstallLog.sqlite-wal")]
		[TestCase("InstallLog.xml.pre-sqlite")]
		public void NativeStoreAndInterruptedMigrationEvidenceAreProtected(string filename)
		{
			File.WriteAllBytes(Path.Combine(_fixture, filename), new byte[] { 1, 2, 3 });
			Assert.That(Witcher3RootOwnershipGuard.HasNativeStoreFootprint(_fixture), Is.True);
		}

		[Test]
		public void RelativeNativeInfoFolderIsRejected()
		{
			Assert.Throws<ArgumentException>(() => Witcher3RootOwnershipGuard.HasNativeStoreFootprint("relative-directory"));
		}

		[Test]
		public void MissingGameStorageRegistryDoesNotInventPreviousOwnership()
		{
			var service = new GameStorageService(_fixture, new Version(0, 93));
			Assert.That(service.HasKnownNativeOwnershipEvidence("Witcher3"), Is.False);
		}

		[Test]
		public void GameStorageRegistryOnlyCountsNativeOwnershipNotDownloads()
		{
			var service = new GameStorageService(_fixture, new Version(0, 93));
			File.WriteAllText(service.RegistryPath,
				"{\"SchemaVersion\":1,\"KnownStorages\":[{\"GameId\":\"Witcher3\",\"LastKnownArchiveCount\":10}]}");
			Assert.That(service.HasKnownNativeOwnershipEvidence("Witcher3"), Is.False);
			Assert.That(service.HasKnownNativeOwnershipEvidence("SkyrimSE"), Is.False);
		}

		[Test]
		public void GameStorageRegistryDetectsPriorInstallLogEvenIfCurrentSettingMoved()
		{
			var service = new GameStorageService(_fixture, new Version(0, 93));
			File.WriteAllText(service.RegistryPath,
				"{\"SchemaVersion\":1,\"KnownStorages\":[{\"GameId\":\"witcher3\",\"LastKnownInstallLogPresent\":true}]}");
			Assert.That(service.HasKnownNativeOwnershipEvidence("Witcher3"), Is.True);
		}

		[Test]
		public void GameStorageRegistryUsesValidFallbackWhenPrimaryIsCorrupt()
		{
			var service = new GameStorageService(_fixture, new Version(0, 93));
			File.WriteAllText(service.RegistryPath, "garbage");
			File.WriteAllText(service.LastKnownGoodPath,
				"{\"SchemaVersion\":1,\"KnownStorages\":[{\"GameId\":\"Witcher3\",\"LastKnownVirtualFileCount\":2}]}");
			Assert.That(service.HasKnownNativeOwnershipEvidence("Witcher3"), Is.True);
		}

		[Test]
		public void GameStorageRegistryCorruptionNeverProvesAbsenceOfOwnership()
		{
			var service = new GameStorageService(_fixture, new Version(0, 93));
			File.WriteAllText(service.RegistryPath, "garbage");
			Assert.Throws<InvalidDataException>(() => service.HasKnownNativeOwnershipEvidence("Witcher3"));
		}

		private IEnvironmentInfo CreateEnvironment(string otherRoot, string otherInfo, bool completed,
			string otherModeId = Witcher3RootOwnershipGuard.LegacyModeId,
			string otherVirtualRoot = null, string currentVirtualRoot = null)
		{
			var gameRoots = new PerGameModeSettings<string>();
			gameRoots[otherModeId] = otherRoot;
			var installInfo = new PerGameModeSettings<string>();
			installInfo[otherModeId] = otherInfo;
			var setup = new PerGameModeSettings<bool>();
			setup[otherModeId] = completed;
			var modFolders = new PerGameModeSettings<string>();
			var linkFolders = new PerGameModeSettings<string>();
			var multiDrive = new PerGameModeSettings<bool>();
			var virtualFolders = new PerGameModeSettings<string>();
			virtualFolders[otherModeId] = otherVirtualRoot;
			virtualFolders[Witcher3RootOwnershipGuard.CounterpartModeId(otherModeId)] = currentVirtualRoot;
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_InstallationPaths": return gameRoots;
					case "get_InstallInfoFolder": return installInfo;
					case "get_CompletedSetup": return setup;
					case "get_VirtualFolder": return virtualFolders;
					case "get_ModFolder": return modFolders;
					case "get_HDLinkFolder": return linkFolders;
					case "get_MultiHDInstall": return multiDrive;
					default: return null;
				}
			});
			return InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
			{
				if (method.Name == "get_Settings") return settings;
				if (method.Name == "get_ApplicationPersonalDataFolderPath") return _fixture;
				if (method.Name == "get_ApplicationVersion") return new Version(0, 93, 1);
				return null;
			});
		}

		/// <summary>A completed metadata-only setup does not reserve a physical game root, even when its empty InstallLog still exists.</summary>
		[TestCase("xml")]
		[TestCase("sqlite")]
		[TestCase("metadata")]
		public void EmptyPriorSetupDoesNotBlockSameGameRoot(string format)
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string root = Path.Combine(_fixture, "ReusedGame");
			string info = Path.Combine(_fixture, "LegacyInfo");
			string oldVirtual = Path.Combine(_fixture, "LegacyStaging");
			Directory.CreateDirectory(root);
			Directory.CreateDirectory(info);
			Directory.CreateDirectory(Path.Combine(oldVirtual, "VirtualInstall"));
			string log = Path.Combine(info, "InstallLog.xml");
			if (format == "xml") File.WriteAllText(log, "<installLog fileVersion='0.6.0.0'><modList/></installLog>");
			if (format == "sqlite")
			{
				Nexus.Client.ModManagement.InstallationLog.InstallLogSqliteStore.CreateFromDocument(Path.ChangeExtension(log, ".sqlite"),
					new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("installLog", new System.Xml.Linq.XAttribute("fileVersion", "0.6.0.0"), new System.Xml.Linq.XElement("modList"))));
				Nexus.Client.ModManagement.NativeStoreAuthority.Mark(Path.ChangeExtension(log, ".sqlite"));
			}
			IEnvironmentInfo environment = CreateEnvironment(root, info, true, otherVirtualRoot: oldVirtual, currentVirtualRoot: Path.Combine(_fixture, "NewStaging"));
			string[] files = Directory.GetFiles(_fixture, "*", SearchOption.AllDirectories);
			Assert.That(Witcher3RootOwnershipGuard.Check(environment, "Witcher3Remastered", root, Path.Combine(_fixture, "NewInfo")).AllowsNormalInitialization, Is.True);
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment, "Witcher3Remastered", root, Path.Combine(_fixture, "NewInfo")).AllowsNativeMutation, Is.True);
			Assert.That(Directory.GetFiles(_fixture, "*", SearchOption.AllDirectories), Is.EquivalentTo(files));
		}

		/// <summary>Real, unreadable, lost and interrupted native state still produce a warning until the user explicitly continues.</summary>
		[TestCase("installed-mod")]
		[TestCase("unreadable")]
		[TestCase("missing-authority")]
		[TestCase("virtual-payload")]
		public void ContinueHonorsReviewedPathsAcrossAllOwnershipChecks(string conflict)
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string root = Path.Combine(_fixture, "ReusedGame");
			string info = Path.Combine(_fixture, "LegacyInfo");
			string targetInfo = Path.Combine(_fixture, "RemasteredInfo");
			string virtualParent = Path.Combine(_fixture, "NewStaging");
			string topology = Path.Combine(virtualParent, "VirtualInstall");
			string legacyStaging = Path.Combine(_fixture, "LegacyStaging");
			Directory.CreateDirectory(root);
			Directory.CreateDirectory(info);
			Directory.CreateDirectory(Path.Combine(legacyStaging, "VirtualInstall"));
			string log = Path.Combine(info, "InstallLog.xml");
			File.WriteAllText(log, conflict == "unreadable" ? "broken" : "<installLog fileVersion='0.6.0.0'><modList>" +
				(conflict == "installed-mod" ? "<mod path='mod.7z'/>" : "") + "</modList></installLog>");
			if (conflict == "missing-authority") File.WriteAllText(Path.ChangeExtension(log, ".sqlite.authority"), "keep missing authority evidence");
			if (conflict == "virtual-payload") File.WriteAllText(Path.Combine(legacyStaging, "VirtualInstall", "keep.esp"), "mod");
			IEnvironmentInfo environment = CreateEnvironment(root, info, true, otherVirtualRoot: legacyStaging, currentVirtualRoot: virtualParent);
			Assert.That(Witcher3RootOwnershipGuard.Check(environment, "Witcher3Remastered", root, targetInfo).AllowsNormalInitialization, Is.False);
			Action approve = Witcher3RootOwnershipGuard.CreateSessionApproval(environment, "Witcher3Remastered", root, targetInfo, topology);
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment, "Witcher3Remastered", root, targetInfo).AllowsNativeMutation, Is.False, "Displaying a warning or choosing Cancel must not authorize anything.");
			approve();
			Assert.That(Witcher3RootOwnershipGuard.Check(environment, "Witcher3Remastered", root, targetInfo).AllowsNormalInitialization, Is.True);
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment, "Witcher3Remastered", root, targetInfo).AllowsNativeMutation, Is.True);
			Assert.That(Witcher3RootOwnershipGuard.CheckForStorageTransition(environment, "Witcher3Remastered", root, targetInfo, topology).AllowsNativeMutation, Is.True);
			Assert.That(Witcher3RootOwnershipGuard.Check(environment, "Witcher3Remastered", root, targetInfo + "Changed").AllowsNormalInitialization, Is.False);
			Assert.That(Witcher3RootOwnershipGuard.CheckForStorageTransition(environment, "Witcher3Remastered", root, targetInfo, topology + "Changed").AllowsNativeMutation, Is.False);
			IEnvironmentInfo independentSession = CreateEnvironment(root, info, true, otherVirtualRoot: legacyStaging, currentVirtualRoot: virtualParent);
			Assert.That(Witcher3RootOwnershipGuard.Check(independentSession, "Witcher3Remastered", root, targetInfo).AllowsNormalInitialization, Is.False);
			Assert.That(File.ReadAllText(log), Is.EqualTo(conflict == "unreadable" ? "broken" : "<installLog fileVersion='0.6.0.0'><modList>" +
				(conflict == "installed-mod" ? "<mod path='mod.7z'/>" : "") + "</modList></installLog>"));
			environment.Settings.InstallInfoFolder["Witcher3"] = info + "Changed";
			Assert.That(Witcher3RootOwnershipGuard.Check(environment, "Witcher3Remastered", root, targetInfo).AllowsNormalInitialization, Is.False);
			environment.Settings.InstallInfoFolder["Witcher3"] = info;
			Witcher3RootOwnershipGuard.RevokeSessionApproval(environment.Settings, "Witcher3Remastered");
			Assert.That(Witcher3RootOwnershipGuard.Check(environment, "Witcher3Remastered", root, targetInfo).AllowsNormalInitialization, Is.False);
		}

		/// <summary>Continuing with conflicted storage records an explicit path choice without rewriting the original manifests.</summary>
		[Test]
		public void ApprovedStorageConfirmationPreservesCounterpartManifests()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string root = Path.Combine(_fixture, "ReusedGame");
			string info = Path.Combine(_fixture, "SharedInfo");
			string mods = Path.Combine(_fixture, "Downloads");
			string staging = Path.Combine(_fixture, "SelectedStaging");
			string topology = Path.Combine(staging, "VirtualInstall");
			foreach (string folder in new[] { root, info, mods, topology }) Directory.CreateDirectory(folder);
			string manifest = Path.Combine(info, ".nmm-folder.json");
			string original = Newtonsoft.Json.JsonConvert.SerializeObject(new GameStorageFolderManifest { SchemaVersion = 2,
				GameId = "Witcher3", StorageId = "keep-original", FolderRole = GameStorageFolderRole.InstallInfo });
			File.WriteAllText(manifest, original);
			File.WriteAllText(Path.Combine(info, "InstallLog.xml"), "<installLog fileVersion='0.6.0.0'><modList><mod path='mod.7z'/></modList></installLog>");
			IEnvironmentInfo environment = CreateEnvironment(root, info, true, otherVirtualRoot: Path.Combine(_fixture, "OtherStaging"), currentVirtualRoot: staging);
			var paths = new GameStoragePathSet { GameId = "Witcher3Remastered", GameInstallPath = root, InstallInfoPath = info,
				ModsPath = mods, VirtualInstallPath = topology };
			var storage = new GameStorageService(environment);
			GameStorageSessionConsent.CreateStorageApproval(environment, paths)();
			GameStorageHealthCheck health;
			Assert.That(storage.ConfirmSelectedStoragePaths(paths, out health), Is.True);
			Assert.That(health.HasConfirmedPaths, Is.True);
			Assert.That(File.ReadAllText(manifest), Is.EqualTo(original));
			Assert.That(File.Exists(Path.Combine(topology, ".nmm-folder.json")), Is.False);
		}

		/// <summary>Both editions can reopen one physical installation using the same native records.</summary>
		[TestCase("Witcher3", "Witcher3Remastered", false)]
		[TestCase("Witcher3Remastered", "Witcher3", false)]
		[TestCase("Witcher3Remastered", "Witcher3", true)]
		public void SharedNativeFoldersKeepOneAuthority(string currentMode, string otherMode, bool directOnly)
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string root = Path.Combine(_fixture, "Game");
			string info = Path.Combine(_fixture, "Shared", "InstallInfo");
			string virtualParent = directOnly ? null : Path.Combine(_fixture, "Shared");
			Directory.CreateDirectory(root);
			Directory.CreateDirectory(info);
			File.WriteAllText(Path.Combine(info, "InstallLog.xml"), "<installLog />");
			IEnvironmentInfo environment = CreateEnvironment(root, info, true, otherMode,
				virtualParent, virtualParent);

			Assert.That(Witcher3RootOwnershipGuard.Check(environment, currentMode, root, info).AllowsNormalInitialization, Is.True);
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment, currentMode, root, info).AllowsNativeMutation, Is.True);
			Assert.That(Witcher3RootOwnershipGuard.CheckForStorageTransition(environment, currentMode, root, info,
				directOnly ? null : Path.Combine(virtualParent, "VirtualInstall")).AllowsNativeMutation, Is.True);
			Assert.That(File.ReadAllText(Path.Combine(info, "InstallLog.xml")), Is.EqualTo("<installLog />"));
		}

		/// <summary>Switching editions preserves one storage ID, all manifest bindings and the original link namespace.</summary>
		[TestCase("Witcher3", "Witcher3Remastered")]
		[TestCase("Witcher3Remastered", "Witcher3")]
		public void SharedStoragePreservesIdentityAndNativeFiles(string originalMode, string secondMode)
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string root = Path.Combine(_fixture, "Game");
			string storageRoot = Path.Combine(_fixture, "Shared");
			string info = Path.Combine(storageRoot, "InstallInfo");
			string mods = Path.Combine(storageRoot, "Mods");
			string virtualPath = Path.Combine(storageRoot, "VirtualInstall");
			Directory.CreateDirectory(root);
			Directory.CreateDirectory(info);
			Directory.CreateDirectory(mods);
			Directory.CreateDirectory(virtualPath);
			string log = Path.Combine(info, "InstallLog.xml");
			string payload = Path.Combine(virtualPath, "existing-staged-mod.bin");
			string archive = Path.Combine(mods, "existing-mod.7z");
			File.WriteAllText(log, "<installLog />");
			File.WriteAllText(payload, "staged mod");
			File.WriteAllText(archive, "archive");
			IEnvironmentInfo environment = CreateEnvironment(root, info, true, secondMode, storageRoot, storageRoot);
			environment.Settings.InstallationPaths[originalMode] = root;
			environment.Settings.InstallInfoFolder[originalMode] = info;
			environment.Settings.ModFolder[originalMode] = mods;
			environment.Settings.ModFolder[secondMode] = mods;
			var service = new GameStorageService(environment);
			var paths = new GameStoragePathSet { GameId = originalMode, GameName = originalMode,
				GameInstallPath = root, InstallInfoPath = info, ModsPath = mods, VirtualInstallPath = virtualPath,
				CompatibleSharedModsGameIds = new System.Collections.Generic.List<string> { secondMode } };
			service.InitializeMetadataForStorage(paths);
			string originalId;
			Assert.That(service.TryResolveExistingStorageId(paths, out originalId), Is.True);
			paths.GameId = secondMode;
			paths.CompatibleSharedModsGameIds = new System.Collections.Generic.List<string> { originalMode };

			Assert.That(service.ValidateStorage(paths).IsHealthy, Is.True);
			service.InitializeMetadataForStorage(paths);

			string sharedId;
			Assert.That(service.TryResolveExistingStorageId(paths, out sharedId), Is.True);
			Assert.That(sharedId, Is.EqualTo(originalId));
			Assert.That(service.GetNativeStorageModeId(paths), Is.EqualTo(originalMode));
			foreach (string folder in new[] { info, mods, virtualPath })
			{
				GameStorageFolderManifest manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<GameStorageFolderManifest>(
					File.ReadAllText(Path.Combine(folder, ".nmm-folder.json")));
				Assert.That(manifest.Bindings.Count, Is.EqualTo(2));
			}
			Assert.That(File.ReadAllText(log), Is.EqualTo("<installLog />"));
			Assert.That(File.ReadAllText(payload), Is.EqualTo("staged mod"));
			Assert.That(File.ReadAllText(archive), Is.EqualTo("archive"));
			GameStorageCandidate candidate = service.DiscoverRecoveryCandidates(paths).Find(item =>
				item.InstallInfoPath == info && item.VirtualInstallPath == virtualPath);
			Assert.That(candidate, Is.Not.Null);
			Assert.That(candidate.InstallInfoPath, Is.EqualTo(info));
			Assert.That(candidate.VirtualInstallPath, Is.EqualTo(virtualPath));
		}

		/// <summary>First-time metadata adoption keeps an older setup's existing mode-specific Link Folder.</summary>
		[Test]
		public void SharedLegacyStorageWithoutManifestsKeepsExistingLinkNamespace()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string root = Path.Combine(_fixture, "Game");
			string info = Path.Combine(_fixture, "Shared", "InstallInfo");
			string mods = Path.Combine(_fixture, "Shared", "Mods");
			string virtualParent = Path.Combine(_fixture, "Shared");
			string link = Path.Combine(_fixture, "Links");
			string oldLinks = Path.Combine(link, "Witcher3", "NMMLink");
			Directory.CreateDirectory(root);
			Directory.CreateDirectory(info);
			Directory.CreateDirectory(mods);
			Directory.CreateDirectory(Path.Combine(virtualParent, "VirtualInstall"));
			Directory.CreateDirectory(oldLinks);
			File.WriteAllText(Path.Combine(info, "InstallLog.xml"), "<installLog />");
			File.WriteAllText(Path.Combine(oldLinks, "existing-mod.bin"), "existing links");
			IEnvironmentInfo environment = CreateEnvironment(root, info, true, "Witcher3", virtualParent, virtualParent);
			environment.Settings.HDLinkFolder["Witcher3"] = link;
			environment.Settings.HDLinkFolder["Witcher3Remastered"] = link;
			var service = new GameStorageService(environment);
			var paths = new GameStoragePathSet { GameId = "Witcher3Remastered", GameName = "Remastered",
				GameInstallPath = root, InstallInfoPath = info, ModsPath = mods,
				VirtualInstallPath = Path.Combine(virtualParent, "VirtualInstall"), LinkFolderPath = link,
				CompatibleSharedModsGameIds = new System.Collections.Generic.List<string> { "Witcher3" } };

			Assert.That(service.GetNativeStorageModeId(paths), Is.EqualTo("Witcher3"));
			service.InitializeMetadataForStorage(paths);
			Assert.That(service.GetNativeStorageModeId(paths), Is.EqualTo("Witcher3"));
			Assert.That(File.ReadAllText(Path.Combine(oldLinks, "existing-mod.bin")), Is.EqualTo("existing links"));
			Assert.That(Directory.Exists(Path.Combine(link, "Witcher3Remastered", "NMMLink")), Is.False);
		}

		[Test]
		public void HistoricalNativeLocationsAreReadOnlyAndExcludeDownloadOnlyStorage()
		{
			var storage = new GameStorageService(_fixture, new Version(0, 93));
			File.WriteAllText(storage.RegistryPath,
				"{\"SchemaVersion\":1,\"KnownStorages\":[" +
				"{\"GameId\":\"Witcher3\",\"LastKnownArchiveCount\":4,\"VirtualInstallPath\":\"D:\\\\OnlyDownloads\"}," +
				"{\"GameId\":\"Witcher3\",\"LastKnownVirtualFileCount\":2," +
				"\"VirtualInstallPath\":\"D:\\\\Native\",\"InstallInfoPath\":\"C:\\\\NativeInfo\"}]}" );
			var claims = storage.GetKnownNativeOwnershipLocations("witcher3");
			Assert.That(claims.Count, Is.EqualTo(1));
			Assert.That(claims[0].HadVirtualPayload, Is.True);
			Assert.That(claims[0].HadInstallLog, Is.False);
			Assert.That(claims[0].VirtualInstallPath, Does.Contain("Native"));
			Assert.That(storage.GetKnownNativeOwnershipLocations("SkyrimSE"), Is.Empty);
			Assert.That(Directory.GetFiles(_fixture).Length, Is.EqualTo(1));
		}

		[Test]
		public void HistoricalLocationsFallBackToLastKnownGoodWhenPrimaryIsCorrupt()
		{
			var storage = new GameStorageService(_fixture, new Version(0, 93));
			File.WriteAllText(storage.RegistryPath, "garbage");
			File.WriteAllText(storage.LastKnownGoodPath,
				"{\"SchemaVersion\":1,\"KnownStorages\":[{\"GameId\":\"Witcher3\"," +
				"\"LastKnownInstallLogPresent\":true,\"InstallInfoPath\":\"C:\\\\PriorInfo\"}]}");
			var claims = storage.GetKnownNativeOwnershipLocations("Witcher3");
			Assert.That(claims.Count, Is.EqualTo(1));
			Assert.That(claims[0].HadInstallLog, Is.True);
			Assert.That(claims[0].InstallInfoPath, Does.Contain("PriorInfo"));
		}

		[Test]
		public void RemasteredWriteRejectsSharedEmptyVirtualInstallOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string classicGame = Path.Combine(_fixture, "Classic");
			string remasteredGame = Path.Combine(_fixture, "Remastered");
			Directory.CreateDirectory(classicGame);
			Directory.CreateDirectory(remasteredGame);
			string virtualParent = Path.Combine(_fixture, "VirtualParent");
			var environment = CreateEnvironment(classicGame, Path.Combine(_fixture, "ClassicInfo"), false,
				otherVirtualRoot: virtualParent,
				currentVirtualRoot: Path.Combine(virtualParent, "VirtualInstall"));
			string currentInfo = Path.Combine(_fixture, "RemasteredInfo");
			Assert.That(Witcher3RootOwnershipGuard.Check(environment, "Witcher3Remastered",
				remasteredGame, currentInfo).AllowsNormalInitialization, Is.True);
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment,
				"Witcher3Remastered", remasteredGame, currentInfo).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.SharedVirtualInstall));
			Assert.That(Directory.Exists(virtualParent), Is.False);
		}

		[Test]
		public void HistoricalVirtualPayloadClaimBlocksOldStorageAfterSettingsMovedOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string classic = Path.Combine(_fixture, "Classic");
			string remastered = Path.Combine(_fixture, "Remastered");
			Directory.CreateDirectory(classic);
			Directory.CreateDirectory(remastered);
			string oldVirtual = Path.Combine(_fixture, "OldVirtual", "VirtualInstall");
			var environment = CreateEnvironment(classic, Path.Combine(_fixture, "ClassicInfo"), false,
				otherVirtualRoot: Path.Combine(_fixture, "MovedVirtual"),
				currentVirtualRoot: oldVirtual);
			var storage = new GameStorageService(environment);
			Directory.CreateDirectory(Path.GetDirectoryName(storage.RegistryPath));
			File.WriteAllText(storage.RegistryPath, "{\"SchemaVersion\":1,\"KnownStorages\":[" +
				"{\"GameId\":\"Witcher3\",\"LastKnownVirtualFileCount\":1," +
				"\"VirtualInstallPath\":\"" + oldVirtual.Replace("\\", "\\\\") + "\"}]}");
			// Current settings alone look independent, but the old claimed native location is not.
			Assert.That(Witcher3RootOwnershipGuard.Check(environment, "Witcher3Remastered", remastered,
				Path.Combine(_fixture, "RemasteredInfo")).AllowsNormalInitialization, Is.True);
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment,
				"Witcher3Remastered", remastered, Path.Combine(_fixture, "RemasteredInfo")).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.SharedVirtualInstall));
		}

		[Test]
		public void ProposedStorageRecoveryPathIsCheckedInsteadOfOldSettingOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string classic = Path.Combine(_fixture, "Classic");
			string remastered = Path.Combine(_fixture, "Remastered");
			Directory.CreateDirectory(classic);
			Directory.CreateDirectory(remastered);
			string otherVirtual = Path.Combine(_fixture, "ClassicVirtual");
			var environment = CreateEnvironment(classic, Path.Combine(_fixture, "ClassicInfo"), false,
				otherVirtualRoot: otherVirtual,
				currentVirtualRoot: Path.Combine(_fixture, "RemasteredVirtual"));
			string newInfo = Path.Combine(_fixture, "NewInfo");
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment, "Witcher3Remastered",
				remastered, newInfo).AllowsNativeMutation, Is.True);
			Assert.That(Witcher3RootOwnershipGuard.CheckForStorageTransition(environment,
				"Witcher3Remastered", remastered, newInfo,
				Path.Combine(otherVirtual, "VirtualInstall")).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.SharedVirtualInstall));
			Assert.That(Witcher3RootOwnershipGuard.CheckForStorageTransition(environment,
				"Witcher3Remastered", remastered, newInfo, null).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.UnverifiablePriorOwner));
		}

		[Test]
		public void HistoricalMissingNativeLocationFailsClosedOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string classic = Path.Combine(_fixture, "Classic");
			string remastered = Path.Combine(_fixture, "Remastered");
			Directory.CreateDirectory(classic);
			Directory.CreateDirectory(remastered);
			var environment = CreateEnvironment(classic, Path.Combine(_fixture, "ClassicInfo"), false,
				currentVirtualRoot: Path.Combine(_fixture, "RemasteredVirtual"));
			var storage = new GameStorageService(environment);
			Directory.CreateDirectory(Path.GetDirectoryName(storage.RegistryPath));
			File.WriteAllText(storage.RegistryPath,
				"{\"SchemaVersion\":1,\"KnownStorages\":[{" +
				"\"GameId\":\"Witcher3\",\"LastKnownVirtualFileCount\":1}]}");
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment,
				"Witcher3Remastered", remastered, Path.Combine(_fixture, "RemasteredInfo")).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.UnverifiablePriorOwner));
		}

		[Test]
		public void IndependentVirtualInstallPathsPermitNativeMutationOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string classic = Path.Combine(_fixture, "Classic");
			string remastered = Path.Combine(_fixture, "Remastered");
			Directory.CreateDirectory(classic);
			Directory.CreateDirectory(remastered);
			var environment = CreateEnvironment(classic, Path.Combine(_fixture, "OldInfo"), false,
				otherVirtualRoot: Path.Combine(_fixture, "ClassicVirtual"),
				currentVirtualRoot: Path.Combine(_fixture, "RemasteredVirtual"));
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment, "Witcher3Remastered",
				remastered, Path.Combine(_fixture, "NewInfo")).AllowsNativeMutation, Is.True);
		}

		[Test]
		public void RemasteredStartupAllowsDiscoveryButNativeWriteIsRejectedOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string root = Path.Combine(_fixture, "Game");
			Directory.CreateDirectory(root);
			IEnvironmentInfo environment = CreateEnvironment(root, Path.Combine(_fixture, "LegacyInfo"), false);
			string newInfo = Path.Combine(_fixture, "NewInfo");
			Assert.That(Witcher3RootOwnershipGuard.Check(environment, "Witcher3Remastered", root, newInfo)
				.AllowsNormalInitialization, Is.True);
			Witcher3RootOwnershipDecision write = Witcher3RootOwnershipGuard.CheckForNativeMutation(
				environment, "Witcher3Remastered", root, newInfo);
			Assert.That(write.Status, Is.EqualTo(Witcher3RootOwnershipStatus.SharedConfiguredGameRoot));
			Assert.Throws<InvalidDataException>(() => Witcher3RootOwnershipGuard.RequireNativeMutation(
				environment, "Witcher3Remastered", root, newInfo));
			Assert.That(Directory.Exists(newInfo), Is.False);
		}

		[Test]
		public void LegacyMayStillWriteWhenRemasteredOnlyDiscoveredRootOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string root = Path.Combine(_fixture, "Game");
			Directory.CreateDirectory(root);
			IEnvironmentInfo environment = CreateEnvironment(root, Path.Combine(_fixture, "RemasteredInfo"), false,
				Witcher3RootOwnershipGuard.RemasteredModeId);
			Assert.DoesNotThrow(() => Witcher3RootOwnershipGuard.RequireNativeMutation(
				environment, "Witcher3", root, Path.Combine(_fixture, "LegacyInfo")));
		}

		[Test]
		public void LegacyWriteSeesOwnerCreatedAfterPriorSuccessfulPreflightOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string root = Path.Combine(_fixture, "Game");
			string otherInfo = Path.Combine(_fixture, "RemasteredInfo");
			Directory.CreateDirectory(root);
			Directory.CreateDirectory(otherInfo);
			IEnvironmentInfo environment = CreateEnvironment(root, otherInfo, false,
				Witcher3RootOwnershipGuard.RemasteredModeId);
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment,
				"Witcher3", root, Path.Combine(_fixture, "LegacyInfo")).AllowsNormalInitialization, Is.True);
			string log = Path.Combine(otherInfo, "InstallLog.xml");
			File.WriteAllText(log, "<installLog />");
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment,
				"Witcher3", root, Path.Combine(_fixture, "LegacyInfo")).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.SharedManagedGameRoot));
			Assert.That(File.ReadAllText(log), Is.EqualTo("<installLog />"));
		}

		[Test]
		public void IntegratedPreflightBlocksSharedRootWithOldInstallLogOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string game = Path.Combine(_fixture, "Game");
			string legacyInfo = Path.Combine(_fixture, "LegacyInfo");
			Directory.CreateDirectory(game);
			Directory.CreateDirectory(legacyInfo);
			string oldLog = Path.Combine(legacyInfo, "InstallLog.xml");
			File.WriteAllText(oldLog, "<installLog />");
			IEnvironmentInfo environment = CreateEnvironment(game, legacyInfo, true);
			string before = File.ReadAllText(oldLog);

			Witcher3RootOwnershipDecision result = Witcher3RootOwnershipGuard.Check(environment,
				Witcher3RootOwnershipGuard.RemasteredModeId, game, Path.Combine(_fixture, "NewInfo"));

			Assert.That(result.Status, Is.EqualTo(Witcher3RootOwnershipStatus.SharedManagedGameRoot));
			Assert.That(File.ReadAllText(oldLog), Is.EqualTo(before));
			Assert.That(Directory.Exists(Path.Combine(_fixture, "NewInfo")), Is.False);
		}

		/// <summary>Removing the old game directory must not require reinstalling it before opening an independent edition.</summary>
		[Test]
		public void RemovedLegacyGameRootDoesNotBlockIndependentRemasteredStorage()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string removed = Path.Combine(_fixture, "RemovedLegacyGame");
			string root = Path.Combine(_fixture, "Remastered");
			string oldInfo = Path.Combine(_fixture, "OldInfo");
			Directory.CreateDirectory(root);
			Directory.CreateDirectory(oldInfo);
			File.WriteAllText(Path.Combine(oldInfo, "InstallLog.xml"), "<installLog />");
			IEnvironmentInfo environment = CreateEnvironment(removed, oldInfo, true,
				currentVirtualRoot: Path.Combine(_fixture, "NewVirtual"));

			Assert.That(Witcher3RootOwnershipGuard.Check(environment, "Witcher3Remastered", root,
				Path.Combine(_fixture, "NewInfo")).AllowsNormalInitialization, Is.True);
			Assert.That(Witcher3RootOwnershipGuard.CheckForNativeMutation(environment, "Witcher3Remastered", root,
				Path.Combine(_fixture, "NewInfo")).AllowsNativeMutation, Is.True);
			Assert.That(Directory.Exists(removed), Is.False);
			Assert.That(File.ReadAllText(Path.Combine(oldInfo, "InstallLog.xml")), Is.EqualTo("<installLog />"));
		}

		/// <summary>Forgotten setup retains location-specific history without reserving unrelated new installations.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void ForgottenLegacySetupChecksHistoricalLocationsInsteadOfGlobalOwnership(bool overlapsHistory)
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string root = Path.Combine(_fixture, "Remastered");
			Directory.CreateDirectory(root);
			string historicalInfo = Path.Combine(_fixture, "OldInfo");
			IEnvironmentInfo environment = CreateEnvironment(null, null, false,
				currentVirtualRoot: Path.Combine(_fixture, "NewVirtual"));
			var storage = new GameStorageService(environment);
			Directory.CreateDirectory(storage.RegistryDirectory);
			string registry = Newtonsoft.Json.JsonConvert.SerializeObject(new GameStorageRegistry {
				KnownStorages = new System.Collections.Generic.List<GameStorageRegistryEntry> {
					new GameStorageRegistryEntry { GameId = "Witcher3", LastKnownInstallLogPresent = true,
						InstallInfoPath = historicalInfo } } });
			File.WriteAllText(storage.RegistryPath, registry);

			Witcher3RootOwnershipDecision decision = Witcher3RootOwnershipGuard.CheckForNativeMutation(environment,
				"Witcher3Remastered", root, overlapsHistory ? historicalInfo : Path.Combine(_fixture, "NewInfo"));

			Assert.That(decision.AllowsNativeMutation, Is.EqualTo(!overlapsHistory));
			Assert.That(File.ReadAllText(storage.RegistryPath), Is.EqualTo(registry));
		}

		[Test]
		public void IntegratedPreflightAllowsSeparatePhysicalInstallationsOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string legacyGame = Path.Combine(_fixture, "Classic");
			string newGame = Path.Combine(_fixture, "Remastered");
			Directory.CreateDirectory(legacyGame);
			Directory.CreateDirectory(newGame);
			IEnvironmentInfo environment = CreateEnvironment(legacyGame, Path.Combine(_fixture, "OldInfo"), true);

			Witcher3RootOwnershipDecision result = Witcher3RootOwnershipGuard.Check(environment,
				Witcher3RootOwnershipGuard.RemasteredModeId, newGame, Path.Combine(_fixture, "NewInfo"));

			Assert.That(result.AllowsNormalInitialization, Is.True);
		}

		[Test]
		public void PhysicalRootIdentityIsStableForNormalizedPathVariantsOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string one = Witcher3PhysicalPathIdentity.Resolve(_fixture, true);
			string two = Witcher3PhysicalPathIdentity.Resolve(Path.Combine(_fixture, "."), true);
			Assert.That(two, Is.EqualTo(one).IgnoreCase);
		}

		[Test]
		public void PhysicalRootIdentityRequiresExistingGameDirectoryOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			Assert.Throws<DirectoryNotFoundException>(() =>
				Witcher3PhysicalPathIdentity.Resolve(Path.Combine(_fixture, "missing"), true));
		}

		[Test]
		public void PhysicalInstallInfoIdentityProjectsAbsentChildWithoutCreatingItOnWindows()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows filesystem identity API");
			string root = Witcher3PhysicalPathIdentity.Resolve(_fixture, true);
			string missing = Path.Combine(_fixture, "missing", "InstallInfo");
			string projected = Witcher3PhysicalPathIdentity.Resolve(missing, false);
			Assert.That(projected, Is.EqualTo(root + @"\missing\InstallInfo").IgnoreCase);
			Assert.That(Directory.Exists(missing), Is.False);
		}
	}
}
