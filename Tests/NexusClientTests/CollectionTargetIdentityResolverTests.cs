using System;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionTargetIdentityResolverTests
	{
		private string _tempRoot;
		private GameStorageService _storageService;

		[SetUp]
		public void SetUp()
		{
			_tempRoot = Path.Combine(Path.GetTempPath(), "NMM_CollectionTarget_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_tempRoot);
			_storageService = new GameStorageService(Path.Combine(_tempRoot, "Registry"), new Version(9, 3));
		}

		[TearDown]
		public void TearDown()
		{
			if (!string.IsNullOrWhiteSpace(_tempRoot) && Directory.Exists(_tempRoot))
				Directory.Delete(_tempRoot, true);
		}

		[Test]
		public void Resolve_InitializedStorageProducesCanonicalPhysicalAndStorageAuthority()
		{
			GameStoragePathSet paths = CreateStorage("SkyrimSE", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			string storageId = _storageService.ValidateStorage(paths).StorageId;

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_storageService).Resolve(paths);

			Assert.That(authority.Target.IsCanonical, Is.True);
			Assert.That(authority.Target.Fingerprint, Does.StartWith("target-sha256:"));
			Assert.That(authority.StorageId, Is.EqualTo(storageId).IgnoreCase);
			Assert.That(authority.GameId, Is.EqualTo("SkyrimSE"));
			Assert.That(authority.CanonicalGameInstallPath, Is.Not.Empty);
			Assert.That(authority.PhysicalGameKey, Does.StartWith("win-"));
		}

		[Test]
		public void Resolve_PathSpellingVariationsForSamePhysicalDirectoryProduceSameTarget()
		{
			GameStoragePathSet paths = CreateStorage("SkyrimSE", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			CollectionTargetIdentityResolver resolver = new CollectionTargetIdentityResolver(_storageService);

			CollectionTargetAuthority first = resolver.Resolve(paths);
			GameStoragePathSet alternate = Clone(paths);
			alternate.GameInstallPath = Path.Combine(paths.GameInstallPath, ".");
			CollectionTargetAuthority second = resolver.Resolve(alternate);

			Assert.That(second.Target, Is.EqualTo(first.Target));
			Assert.That(second.PhysicalGameKey, Is.EqualTo(first.PhysicalGameKey));
		}

		[Test]
		public void Resolve_DifferentPhysicalGameDirectoryChangesTargetEvenWithSameStorageAuthority()
		{
			GameStoragePathSet paths = CreateStorage("SkyrimSE", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			CollectionTargetIdentityResolver resolver = new CollectionTargetIdentityResolver(_storageService);
			CollectionTargetAuthority first = resolver.Resolve(paths);

			string secondGame = Path.Combine(_tempRoot, "GameB");
			Directory.CreateDirectory(secondGame);
			GameStoragePathSet secondPaths = Clone(paths);
			secondPaths.GameInstallPath = secondGame;
			CollectionTargetAuthority second = resolver.Resolve(secondPaths);

			Assert.That(second.StorageId, Is.EqualTo(first.StorageId).IgnoreCase);
			Assert.That(second.Target, Is.Not.EqualTo(first.Target));
		}

		[Test]
		public void Resolve_SamePhysicalGameDirectoryWithDifferentStorageAuthorityChangesTarget()
		{
			GameStoragePathSet firstPaths = CreateStorage("SkyrimSE", "StorageA", "SharedGame");
			GameStoragePathSet secondPaths = CreateStorage("SkyrimSE", "StorageB", "SharedGame");
			_storageService.InitializeMetadataForStorage(firstPaths);
			string firstStorageId = _storageService.ValidateStorage(firstPaths).StorageId;
			var secondStorageService = new GameStorageService(Path.Combine(_tempRoot, "RegistryB"), new Version(9, 3));
			secondStorageService.InitializeMetadataForStorage(secondPaths);
			string secondStorageId = _storageService.ValidateStorage(secondPaths).StorageId;
			CollectionTargetIdentityResolver resolver = new CollectionTargetIdentityResolver(_storageService);

			CollectionTargetAuthority first = resolver.Resolve(firstPaths);
			CollectionTargetAuthority second = resolver.Resolve(secondPaths);

			Assert.That(second.PhysicalGameKey, Is.EqualTo(first.PhysicalGameKey));
			Assert.That(StringComparer.OrdinalIgnoreCase.Equals(secondStorageId, firstStorageId), Is.False);
			Assert.That(second.Target, Is.Not.EqualTo(first.Target));
		}

		[Test]
		public void Resolve_MetadataFreeLegacyStorageFailsInsteadOfUsingTransientOrActiveIdentity()
		{
			GameStoragePathSet active = CreateStorage("Fallout4", "ActiveStorage", "GameA");
			_storageService.InitializeMetadataForStorage(active);
			GameStoragePathSet legacy = CreateStorage("Fallout4", "LegacyStorage", "GameB");
			Assert.That(_storageService.ValidateStorage(legacy).StorageId, Is.Not.Empty);

			CollectionTargetAuthorityException error = Assert.Throws<CollectionTargetAuthorityException>(() =>
				new CollectionTargetIdentityResolver(_storageService).Resolve(legacy));

			Assert.That(error.FailureKind, Is.EqualTo(CollectionTargetAuthorityFailureKind.StorageIdentityUnavailable));
			Assert.That(File.Exists(Path.Combine(legacy.InstallInfoPath, ".nmm-folder.json")), Is.False);
		}

		[Test]
		public void Resolve_ConflictingPersistedStorageMetadataFailsClosed()
		{
			GameStoragePathSet paths = CreateStorage("Fallout4", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			string modsManifest = Path.Combine(paths.ModsPath, ".nmm-folder.json");
			string json = File.ReadAllText(modsManifest);
			string currentStorageId = _storageService.ValidateStorage(paths).StorageId;
			File.SetAttributes(modsManifest, File.GetAttributes(modsManifest) & ~FileAttributes.Hidden & ~FileAttributes.ReadOnly);
			File.WriteAllText(modsManifest, json.Replace(currentStorageId, "conflicting-storage-id"));

			CollectionTargetAuthorityException error = Assert.Throws<CollectionTargetAuthorityException>(() =>
				new CollectionTargetIdentityResolver(_storageService).Resolve(paths));

			Assert.That(error.FailureKind, Is.EqualTo(CollectionTargetAuthorityFailureKind.UnsafeStorageMetadata));
		}

		[Test]
		public void Resolve_MissingPhysicalGameDirectoryFailsClosedWithoutChangingStorageMetadata()
		{
			GameStoragePathSet paths = CreateStorage("Fallout4", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(paths);
			string installManifest = Path.Combine(paths.InstallInfoPath, ".nmm-folder.json");
			string before = File.ReadAllText(installManifest);
			Directory.Delete(paths.GameInstallPath, true);

			CollectionTargetAuthorityException error = Assert.Throws<CollectionTargetAuthorityException>(() =>
				new CollectionTargetIdentityResolver(_storageService).Resolve(paths));

			Assert.That(error.FailureKind, Is.EqualTo(CollectionTargetAuthorityFailureKind.PhysicalTargetUnavailable));
			Assert.That(File.ReadAllText(installManifest), Is.EqualTo(before));
		}

		[Test]
		public void TryResolveExistingStorageId_RequiresPersistedExactEvidenceAndDoesNotBorrowActiveStorage()
		{
			GameStoragePathSet active = CreateStorage("SkyrimSE", "ActiveStorage", "GameA");
			_storageService.InitializeMetadataForStorage(active);
			string activeId;
			Assert.That(_storageService.TryResolveExistingStorageId(active, out activeId), Is.True);

			GameStoragePathSet legacy = CreateStorage("SkyrimSE", "LegacyStorage", "GameB");
			string legacyId;
			Assert.That(_storageService.TryResolveExistingStorageId(legacy, out legacyId), Is.False);
			Assert.That(legacyId, Is.Null);
		}

		[Test]
		public void Resolve_DoesNotUseModsLibraryPathAsTargetIdentity()
		{
			GameStoragePathSet first = CreateStorage("SkyrimSE", "StorageA", "GameA");
			_storageService.InitializeMetadataForStorage(first);
			CollectionTargetIdentityResolver resolver = new CollectionTargetIdentityResolver(_storageService);
			CollectionTargetAuthority firstAuthority = resolver.Resolve(first);

			GameStoragePathSet second = Clone(first);
			second.GameId = "SkyrimGOG";
			second.GameName = "Skyrim GOG";
			second.CompatibleSharedModsGameIds.Add("SkyrimSE");
			string secondGame = Path.Combine(_tempRoot, "GameB");
			Directory.CreateDirectory(secondGame);
			second.GameInstallPath = secondGame;
			// Same Mods path is intentionally retained; target identity must still follow the physical game + native storage authority.
			Assert.That(second.ModsPath, Is.EqualTo(first.ModsPath));

			// The storage metadata is bound to SkyrimSE, so a different game binding must not be silently adopted as authority.
			CollectionTargetAuthorityException error = Assert.Throws<CollectionTargetAuthorityException>(() => resolver.Resolve(second));
			Assert.That(error.FailureKind, Is.EqualTo(CollectionTargetAuthorityFailureKind.UnsafeStorageMetadata));
			Assert.That(firstAuthority.Target.IsCanonical, Is.True);
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
			File.WriteAllText(Path.Combine(installInfo, "InstallLog.xml"), "<installLog />");

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

		private static GameStoragePathSet Clone(GameStoragePathSet source)
		{
			return new GameStoragePathSet
			{
				GameId = source.GameId,
				GameName = source.GameName,
				GameInstallPath = source.GameInstallPath,
				InstallInfoPath = source.InstallInfoPath,
				ModsPath = source.ModsPath,
				VirtualInstallPath = source.VirtualInstallPath,
				LinkFolderPath = source.LinkFolderPath,
				LinkFolderRequired = source.LinkFolderRequired,
				CompatibleSharedModsGameIds = source.CompatibleSharedModsGameIds.ToList()
			};
		}
	}
}
