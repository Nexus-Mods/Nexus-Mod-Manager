using System;
using System.IO;
using System.Linq;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionTargetOwnershipAuthorityValidatorTests
	{
		private string _tempRoot;
		private GameStorageService _storageService;
		private CollectionTargetOwnershipAuthorityStore _authorityStore;

		[SetUp]
		public void SetUp()
		{
			_tempRoot = Path.Combine(Path.GetTempPath(), "NMM_CollectionOwnershipAuthority_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_tempRoot);
			_storageService = new GameStorageService(Path.Combine(_tempRoot, "Registry"), new Version(9, 3));
			_authorityStore = new CollectionTargetOwnershipAuthorityStore(Path.Combine(_tempRoot, "MachineAuthority"));
		}

		[TearDown]
		public void TearDown()
		{
			if (!string.IsNullOrWhiteSpace(_tempRoot) && Directory.Exists(_tempRoot))
				Directory.Delete(_tempRoot, true);
		}

		[Test]
		public void ValidateAndReload_FirstAuthorityBindsTargetAndReloadsNativeState()
		{
			GameStoragePathSet paths = CreateStorage("SkyrimSE", "StorageA", "SharedGame");
			_storageService.InitializeMetadataForStorage(paths);
			CollectionTargetAuthority authority = Resolve(paths);
			var reloader = new RecordingReloader();
			var validator = new CollectionTargetOwnershipAuthorityValidator(_storageService, reloader, _authorityStore);
			var leaseManager = new CollectionTargetMutationLeaseManager();

			using (CollectionTargetMutationLease lease = leaseManager.Acquire(authority))
			{
				CollectionTargetOwnershipAuthorityBinding binding = validator.ValidateAndReload(lease, authority, paths);

				Assert.That(binding.TargetFingerprint, Is.EqualTo(authority.Target.Fingerprint));
				Assert.That(binding.StorageId, Is.EqualTo(authority.StorageId).IgnoreCase);
				Assert.That(binding.InstallInfoPhysicalKey, Is.Not.Empty);
				Assert.That(reloader.ReloadCount, Is.EqualTo(1));
				Assert.That(reloader.LastInstallLogPath, Is.EqualTo(Path.Combine(paths.InstallInfoPath, "InstallLog.xml")).IgnoreCase);
			}
		}

		[Test]
		public void ValidateAndReload_SamePhysicalGameDifferentInstallLogsIsRejectedAcrossIndependentContexts()
		{
			GameStoragePathSet firstPaths = CreateStorage("SkyrimSE", "StorageA", "SharedGame");
			GameStoragePathSet secondPaths = CreateStorage("SkyrimSE", "StorageB", "SharedGame");
			_storageService.InitializeMetadataForStorage(firstPaths);
			_storageService.InitializeMetadataForStorage(secondPaths);
			CollectionTargetAuthority firstAuthority = Resolve(firstPaths);
			CollectionTargetAuthority secondAuthority = Resolve(secondPaths);
			var firstValidator = new CollectionTargetOwnershipAuthorityValidator(_storageService, new RecordingReloader(), _authorityStore);
			var secondValidator = new CollectionTargetOwnershipAuthorityValidator(_storageService, new RecordingReloader(),
				new CollectionTargetOwnershipAuthorityStore(Path.Combine(_tempRoot, "MachineAuthority")));

			using (CollectionTargetMutationLease firstLease = new CollectionTargetMutationLeaseManager().Acquire(firstAuthority))
				firstValidator.ValidateAndReload(firstLease, firstAuthority, firstPaths);

			using (CollectionTargetMutationLease secondLease = new CollectionTargetMutationLeaseManager().Acquire(secondAuthority))
			{
				CollectionTargetOwnershipAuthorityException error = Assert.Throws<CollectionTargetOwnershipAuthorityException>(() =>
					secondValidator.ValidateAndReload(secondLease, secondAuthority, secondPaths));
				Assert.That(error.FailureKind, Is.EqualTo(CollectionTargetOwnershipAuthorityFailureKind.AuthorityConflict));
			}
		}

		[Test]
		public void BindOrValidate_SameStorageIdButDifferentInstallInfoPhysicalAuthorityIsRejected()
		{
			GameStoragePathSet paths = CreateStorage("Fallout4", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			CollectionTargetAuthority authority = Resolve(paths);

			_authorityStore.BindOrValidate(authority, "win-dir-v1:11111111:0000000000000001");
			CollectionTargetOwnershipAuthorityException error = Assert.Throws<CollectionTargetOwnershipAuthorityException>(() =>
				_authorityStore.BindOrValidate(authority, "win-dir-v1:11111111:0000000000000002"));

			Assert.That(error.FailureKind, Is.EqualTo(CollectionTargetOwnershipAuthorityFailureKind.AuthorityConflict));
		}

		[Test]
		public void ValidateAndReload_RevalidatesLiveTargetAfterLeaseAcquisition()
		{
			GameStoragePathSet firstPaths = CreateStorage("SkyrimSE", "StorageA", "SharedGame");
			GameStoragePathSet secondPaths = CreateStorage("SkyrimSE", "StorageB", "SharedGame");
			_storageService.InitializeMetadataForStorage(firstPaths);
			var secondStorageService = new GameStorageService(Path.Combine(_tempRoot, "RegistryB"), new Version(9, 3));
			secondStorageService.InitializeMetadataForStorage(secondPaths);
			CollectionTargetAuthority firstAuthority = Resolve(firstPaths);
			var validator = new CollectionTargetOwnershipAuthorityValidator(_storageService, new RecordingReloader(), _authorityStore);

			using (CollectionTargetMutationLease lease = new CollectionTargetMutationLeaseManager().Acquire(firstAuthority))
			{
				CollectionTargetOwnershipAuthorityException error = Assert.Throws<CollectionTargetOwnershipAuthorityException>(() =>
					validator.ValidateAndReload(lease, firstAuthority, secondPaths));
				Assert.That(error.FailureKind, Is.EqualTo(CollectionTargetOwnershipAuthorityFailureKind.TargetChanged));
			}
		}

		[Test]
		public void ValidateAndReload_InheritedChildCannotPerformRootOwnershipValidation()
		{
			GameStoragePathSet paths = CreateStorage("SkyrimSE", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			CollectionTargetAuthority authority = Resolve(paths);
			var validator = new CollectionTargetOwnershipAuthorityValidator(_storageService, new RecordingReloader(), _authorityStore);
			var manager = new CollectionTargetMutationLeaseManager();

			using (CollectionTargetMutationLease rootLease = manager.Acquire(authority))
			using (CollectionTargetMutationLease childLease = manager.Inherit(rootLease, authority))
			{
				CollectionTargetOwnershipAuthorityException error = Assert.Throws<CollectionTargetOwnershipAuthorityException>(() =>
					validator.ValidateAndReload(childLease, authority, paths));
				Assert.That(error.FailureKind, Is.EqualTo(CollectionTargetOwnershipAuthorityFailureKind.MutationLeaseRequired));
			}
		}

		[Test]
		public void ValidateAndReload_MissingInstallLogFailsBeforeNativeReloadOrBinding()
		{
			GameStoragePathSet paths = CreateStorage("Fallout4", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			CollectionTargetAuthority authority = Resolve(paths);
			File.Delete(Path.Combine(paths.InstallInfoPath, "InstallLog.xml"));
			var reloader = new RecordingReloader();
			var validator = new CollectionTargetOwnershipAuthorityValidator(_storageService, reloader, _authorityStore);

			using (CollectionTargetMutationLease lease = new CollectionTargetMutationLeaseManager().Acquire(authority))
			{
				CollectionTargetOwnershipAuthorityException error = Assert.Throws<CollectionTargetOwnershipAuthorityException>(() =>
					validator.ValidateAndReload(lease, authority, paths));
				Assert.That(error.FailureKind, Is.EqualTo(CollectionTargetOwnershipAuthorityFailureKind.InstallLogMissing));
				Assert.That(reloader.ReloadCount, Is.EqualTo(0));
				Assert.That(Directory.Exists(Path.Combine(_tempRoot, "MachineAuthority")) &&
					Directory.EnumerateFiles(Path.Combine(_tempRoot, "MachineAuthority")).Any(), Is.False);
			}
		}

		[Test]
		public void ValidateAndReload_NativeReloadFailureDoesNotPublishAuthorityBinding()
		{
			GameStoragePathSet paths = CreateStorage("Fallout4", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			CollectionTargetAuthority authority = Resolve(paths);
			var validator = new CollectionTargetOwnershipAuthorityValidator(_storageService,
				new ThrowingReloader(), _authorityStore);

			using (CollectionTargetMutationLease lease = new CollectionTargetMutationLeaseManager().Acquire(authority))
			{
				CollectionTargetOwnershipAuthorityException error = Assert.Throws<CollectionTargetOwnershipAuthorityException>(() =>
					validator.ValidateAndReload(lease, authority, paths));
				Assert.That(error.FailureKind, Is.EqualTo(CollectionTargetOwnershipAuthorityFailureKind.NativeReloadFailed));
			}

			string authorityRoot = Path.Combine(_tempRoot, "MachineAuthority");
			Assert.That(!Directory.Exists(authorityRoot) || !Directory.EnumerateFiles(authorityRoot).Any(), Is.True);
		}

		[Test]
		public void ValidateAndReload_CorruptPersistedBindingFailsClosed()
		{
			GameStoragePathSet paths = CreateStorage("SkyrimSE", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			CollectionTargetAuthority authority = Resolve(paths);
			var validator = new CollectionTargetOwnershipAuthorityValidator(_storageService, new RecordingReloader(), _authorityStore);
			using (CollectionTargetMutationLease lease = new CollectionTargetMutationLeaseManager().Acquire(authority))
				validator.ValidateAndReload(lease, authority, paths);

			string bindingPath = Directory.EnumerateFiles(Path.Combine(_tempRoot, "MachineAuthority"), "*.json").Single();
			File.WriteAllText(bindingPath, "{ definitely not valid json");

			using (CollectionTargetMutationLease lease = new CollectionTargetMutationLeaseManager().Acquire(authority))
			{
				CollectionTargetOwnershipAuthorityException error = Assert.Throws<CollectionTargetOwnershipAuthorityException>(() =>
					validator.ValidateAndReload(lease, authority, paths));
				Assert.That(error.FailureKind, Is.EqualTo(CollectionTargetOwnershipAuthorityFailureKind.AuthorityBindingInvalid));
			}
		}

		[Test]
		public void ValidateAndReload_AbandonedCrossProcessReservationClearsRecoveryOnlyAfterSuccessfulReloadAndAuthorityValidation()
		{
			GameStoragePathSet paths = CreateStorage("SkyrimSE", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			CollectionTargetAuthority authority = Resolve(paths);
			var crossProcessManager = new CollectionTargetCrossProcessLeaseManager();
			string coordinationName;
			using (CollectionTargetCrossProcessLease discovery = crossProcessManager.Acquire(authority))
				coordinationName = discovery.CoordinationName;

			using (var abandonedMutex = new Mutex(false, coordinationName))
			using (var owned = new ManualResetEventSlim(false))
			{
				var abandoningThread = new Thread(() =>
				{
					abandonedMutex.WaitOne();
					owned.Set();
				});
				abandoningThread.IsBackground = true;
				abandoningThread.Start();
				Assert.That(owned.Wait(TimeSpan.FromSeconds(2)), Is.True);
				Assert.That(abandoningThread.Join(TimeSpan.FromSeconds(2)), Is.True);

				var mutationManager = new CollectionTargetMutationLeaseManager();
				using (CollectionTargetMutationLease lease = mutationManager.Acquire(authority))
				{
					Assert.That(lease.RequiresRecovery, Is.True);
					var validator = new CollectionTargetOwnershipAuthorityValidator(_storageService, new RecordingReloader(), _authorityStore);
					validator.ValidateAndReload(lease, authority, paths);
					Assert.That(lease.RequiresRecovery, Is.False);
				}
			}
		}

		[Test]
		public void ValidateAndReload_SecondContextWithSameAuthorityIsAcceptedAfterFreshReload()
		{
			GameStoragePathSet paths = CreateStorage("Fallout4", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			CollectionTargetAuthority authority = Resolve(paths);
			var firstReloader = new RecordingReloader();
			var secondReloader = new RecordingReloader();

			using (CollectionTargetMutationLease first = new CollectionTargetMutationLeaseManager().Acquire(authority))
				new CollectionTargetOwnershipAuthorityValidator(_storageService, firstReloader, _authorityStore).ValidateAndReload(first, authority, paths);

			using (CollectionTargetMutationLease second = new CollectionTargetMutationLeaseManager().Acquire(authority))
				new CollectionTargetOwnershipAuthorityValidator(_storageService, secondReloader,
					new CollectionTargetOwnershipAuthorityStore(Path.Combine(_tempRoot, "MachineAuthority"))).ValidateAndReload(second, authority, paths);

			Assert.That(firstReloader.ReloadCount, Is.EqualTo(1));
			Assert.That(secondReloader.ReloadCount, Is.EqualTo(1), "Every cross-process acquisition must reload native reality, even for the same authority.");
		}

		private CollectionTargetAuthority Resolve(GameStoragePathSet paths)
		{
			return new CollectionTargetIdentityResolver(_storageService).Resolve(paths);
		}

		private GameStoragePathSet CreateStorage(string gameId, string storageName, string gameDirectoryName)
		{
			string root = Path.Combine(_tempRoot, storageName);
			string installInfo = Path.Combine(root, "InstallInfo");
			string mods = Path.Combine(root, "Mods");
			string virtualInstall = Path.Combine(root, "VirtualInstall");
			string game = Path.Combine(_tempRoot, gameDirectoryName);
			Directory.CreateDirectory(installInfo);
			Directory.CreateDirectory(mods);
			Directory.CreateDirectory(virtualInstall);
			Directory.CreateDirectory(game);
			File.WriteAllText(Path.Combine(installInfo, "InstallLog.xml"), "<installLog fileVersion=\"0.6.0.0\" />");

			return new GameStoragePathSet
			{
				GameId = gameId,
				GameName = gameId,
				GameInstallPath = game,
				InstallInfoPath = installInfo,
				ModsPath = mods,
				VirtualInstallPath = virtualInstall,
				LinkFolderRequired = false
			};
		}

		private sealed class RecordingReloader : ICollectionNativeStateReloader
		{
			public int ReloadCount { get; private set; }
			public string LastInstallLogPath { get; private set; }

			public void Reload(string installLogPath)
			{
				ReloadCount++;
				LastInstallLogPath = installLogPath;
			}
		}

		private sealed class ThrowingReloader : ICollectionNativeStateReloader
		{
			public void Reload(string installLogPath)
			{
				throw new IOException("Synthetic native reload failure.");
			}
		}
	}
}
