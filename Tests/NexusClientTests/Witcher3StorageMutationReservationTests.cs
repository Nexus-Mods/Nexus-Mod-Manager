using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client;
using Nexus.Client.GameStorage;
using Nexus.Client.Settings;
using Newtonsoft.Json;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[NonParallelizable]
	public sealed class Witcher3StorageMutationReservationTests
	{
		private string _root;

		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-w3r-storage-coordination-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_root);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_root)) Directory.Delete(_root, true);
		}

		[Test]
		public void BothWitcherModeIdsShareOneCrossProcessReservation()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT)
				Assert.Ignore("This tests the Windows machine-wide named mutex used by NMM CE.");

			var started = new ManualResetEventSlim(false);
			var acquired = new ManualResetEventSlim(false);
			Task worker;
			using (Witcher3StorageMutationReservation.Acquire("Witcher3"))
			{
				worker = Task.Run(() =>
				{
					started.Set();
					using (Witcher3StorageMutationReservation.Acquire("Witcher3Remastered"))
						acquired.Set();
				});
				Assert.That(started.Wait(TimeSpan.FromSeconds(2)), Is.True);
				Assert.That(acquired.Wait(TimeSpan.FromMilliseconds(100)), Is.False,
					"The other edition must not pass while a Witcher storage write is active.");
			}
			Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
			Assert.That(acquired.IsSet, Is.True);
		}

		[Test]
		public void ConcurrentAttemptTimesOutWithoutTakingOwnership()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT)
				Assert.Ignore("This tests the Windows machine-wide named mutex used by NMM CE.");

			using (Witcher3StorageMutationReservation.Acquire("Witcher3"))
			{
				string error = Task.Run(() =>
				{
					try
					{
						using (Witcher3StorageMutationReservation.Acquire("Witcher3Remastered",
							TimeSpan.FromMilliseconds(50))) { }
						return null;
					}
					catch (InvalidOperationException exception) { return exception.Message; }
				}).GetAwaiter().GetResult();
				Assert.That(error, Does.Contain("Another NMM process"));
				Witcher3StorageMutationReservation.RequireHeld("Witcher3");
			}
		}

		[Test]
		public void NoWitcherReservationCannotAuthorizeMetadataPublication()
		{
			Assert.Throws<InvalidOperationException>(() =>
				Witcher3StorageMutationReservation.RequireHeld("Witcher3Remastered"));
			Assert.That(Witcher3StorageMutationReservation.Acquire("SkyrimSE"), Is.Null);
			Assert.DoesNotThrow(() => Witcher3StorageMutationReservation.RequireHeld("SkyrimSE"));
		}

		[Test]
		public void NestedReservationOnSameThreadDoesNotPrematurelyReleaseMutex()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT)
				Assert.Ignore("This tests the Windows machine-wide named mutex used by NMM CE.");

			using (Witcher3StorageMutationReservation.Acquire("Witcher3"))
			{
				using (Witcher3StorageMutationReservation.Acquire("Witcher3Remastered"))
					Witcher3StorageMutationReservation.RequireHeld("Witcher3");
				Witcher3StorageMutationReservation.RequireHeld("Witcher3Remastered");
				Assert.That(Task.Run(() =>
				{
					try
					{
						using (Witcher3StorageMutationReservation.Acquire("Witcher3",
							TimeSpan.FromMilliseconds(50))) { }
						return false;
					}
					catch (InvalidOperationException) { return true; }
				}).GetAwaiter().GetResult(), Is.True);
			}
			Assert.Throws<InvalidOperationException>(() =>
				Witcher3StorageMutationReservation.RequireHeld("Witcher3"));
		}

		private GameStoragePathSet Paths()
		{
			var paths = new GameStoragePathSet
			{
				GameId = "Witcher3Remastered",
				GameName = "The Witcher 3 Remastered",
				GameInstallPath = Path.Combine(_root, "Game"),
				InstallInfoPath = Path.Combine(_root, "InstallInfo"),
				ModsPath = Path.Combine(_root, "Mods"),
				VirtualInstallPath = Path.Combine(_root, "Virtual", "VirtualInstall")
			};
			Directory.CreateDirectory(paths.GameInstallPath);
			Directory.CreateDirectory(paths.InstallInfoPath);
			Directory.CreateDirectory(paths.ModsPath);
			Directory.CreateDirectory(paths.VirtualInstallPath);
			return paths;
		}

		private IEnvironmentInfo EnvironmentWithUnconfiguredCounterpart()
		{
			var games = new PerGameModeSettings<string>();
			var infos = new PerGameModeSettings<string>();
			var virtualFolders = new PerGameModeSettings<string>();
			var setups = new PerGameModeSettings<bool>();
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_InstallationPaths": return games;
					case "get_InstallInfoFolder": return infos;
					case "get_VirtualFolder": return virtualFolders;
					case "get_CompletedSetup": return setups;
					default: return null;
				}
			});
			return InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
			{
				if (method.Name == "get_Settings") return settings;
				if (method.Name == "get_ApplicationPersonalDataFolderPath") return _root;
				if (method.Name == "get_ApplicationVersion") return new Version(0, 93, 1);
				return null;
			});
		}

		[Test]
		public void ForeignWitcherManifestIsRejectedEvenWhenSettingsKnowNothingOfOtherProcess()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT)
				Assert.Ignore("Physical Windows path identity required for ownership validation.");

			GameStoragePathSet paths = Paths();
			string foreign = Path.Combine(paths.VirtualInstallPath, ".nmm-folder.json");
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new GameStorageFolderManifest
			{
				SchemaVersion = 2,
				FolderRole = GameStorageFolderRole.VirtualInstall,
				GameId = "Witcher3",
				StorageId = "owned-in-a-different-nmm-copy"
			}));
			File.WriteAllBytes(foreign, bytes);
			var storage = new GameStorageService(EnvironmentWithUnconfiguredCounterpart());

			var exception = Assert.Throws<InvalidOperationException>(() =>
				storage.InitializeMetadataForStorage(paths));
			Assert.That(exception.Message, Does.Contain("other Witcher edition"));
			Assert.That(File.ReadAllBytes(foreign), Is.EqualTo(bytes));
			Assert.That(File.Exists(storage.RegistryPath), Is.False);
			Assert.That(File.Exists(Path.Combine(paths.InstallInfoPath, ".nmm-folder.json")), Is.False);
		}

		[Test]
		public void InitialSetupRejectsForeignManifestBeforeCreatingCandidateDirectories()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT)
				Assert.Ignore("Physical Windows path identity required for ownership validation.");

			GameStoragePathSet paths = Paths();
			string manifest = Path.Combine(paths.VirtualInstallPath, ".nmm-folder.json");
			File.WriteAllText(manifest, JsonConvert.SerializeObject(new GameStorageFolderManifest
			{
				SchemaVersion = 2,
				FolderRole = GameStorageFolderRole.VirtualInstall,
				GameId = "Witcher3",
				StorageId = "already-owned"
			}));
			string newMods = Path.Combine(_root, "uncreated-mod-library");
			var candidate = new GameStorageCandidate
			{
				GameId = paths.GameId,
				InstallInfoPath = paths.InstallInfoPath,
				VirtualInstallPath = paths.VirtualInstallPath,
				ModsPath = newMods
			};
			var service = new GameStorageService(EnvironmentWithUnconfiguredCounterpart());
			GameStorageHealthCheck health;

			Assert.That(service.ApplyInitialSetupCandidate(paths, candidate, out health), Is.False);
			Assert.That(health, Is.Not.Null);
			Assert.That(Directory.Exists(newMods), Is.False);
			Assert.That(File.Exists(service.RegistryPath), Is.False);
			Assert.That(File.Exists(manifest), Is.True);
		}

		[Test]
		public void UnreadableExistingWitcherManifestBlocksPublicationInsteadOfBeingOverwritten()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT)
				Assert.Ignore("Physical Windows path identity required for ownership validation.");

			GameStoragePathSet paths = Paths();
			string corrupt = Path.Combine(paths.InstallInfoPath, ".nmm-folder.json");
			File.WriteAllText(corrupt, "{not valid manifest");
			var storage = new GameStorageService(EnvironmentWithUnconfiguredCounterpart());

			Assert.Throws<InvalidOperationException>(() => storage.InitializeMetadataForStorage(paths));
			Assert.That(File.ReadAllText(corrupt), Is.EqualTo("{not valid manifest"));
			Assert.That(File.Exists(storage.RegistryPath), Is.False);
		}
	}
}
