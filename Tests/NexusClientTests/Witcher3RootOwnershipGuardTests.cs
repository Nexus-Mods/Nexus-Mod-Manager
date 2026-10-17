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

		private IEnvironmentInfo CreateEnvironment(string otherRoot, string otherInfo, bool completed)
		{
			var gameRoots = new PerGameModeSettings<string>();
			gameRoots[Witcher3RootOwnershipGuard.LegacyModeId] = otherRoot;
			var installInfo = new PerGameModeSettings<string>();
			installInfo[Witcher3RootOwnershipGuard.LegacyModeId] = otherInfo;
			var setup = new PerGameModeSettings<bool>();
			setup[Witcher3RootOwnershipGuard.LegacyModeId] = completed;
			var virtualFolders = new PerGameModeSettings<string>();
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
