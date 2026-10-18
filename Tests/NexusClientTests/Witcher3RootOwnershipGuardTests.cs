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
