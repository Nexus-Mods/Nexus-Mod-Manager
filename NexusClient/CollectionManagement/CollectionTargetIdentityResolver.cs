using System;
using System.IO;
using System.Linq;
using Nexus.Client.GameStorage;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Resolves the canonical physical game and native Game Storage authority for Collection operations.
	/// </summary>
	/// <remarks>
	/// Resolution establishes identity only. It does not certify writeability, ownership-log coherence or mutation safety;
	/// those checks belong to the later target-coordination and ownership-authority layers.
	/// </remarks>
	public sealed class CollectionTargetIdentityResolver
	{
		private readonly GameStorageService _gameStorageService;

		/// <summary>
		/// Creates a resolver over the existing read-only Game Storage identity services.
		/// </summary>
		public CollectionTargetIdentityResolver(GameStorageService gameStorageService)
		{
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
		}

		/// <summary>
		/// Resolves a native operation's target, attempting safe metadata setup once before
		/// reporting a storage error. Read-only callers continue to use Resolve.
		/// </summary>
		internal CollectionTargetAuthority ResolveForMutation(GameStoragePathSet paths)
		{
			try
			{
				return Resolve(paths);
			}
			catch (CollectionTargetAuthorityException exception) when (
				exception.FailureKind == CollectionTargetAuthorityFailureKind.StorageIdentityUnavailable ||
				exception.FailureKind == CollectionTargetAuthorityFailureKind.UnsafeStorageMetadata)
			{
				_gameStorageService.PrepareMetadataForMutation(paths);
				return Resolve(paths);
			}
		}

		/// <summary>
		/// Resolves one canonical target authority without modifying Game Storage metadata.
		/// </summary>
		public CollectionTargetAuthority Resolve(GameStoragePathSet paths)
		{
			if (paths == null)
				throw new ArgumentNullException(nameof(paths));

			string gameId = CollectionIdentityValidation.RequireOpaqueToken(paths.GameId, nameof(paths.GameId));
			GameStorageHealthCheck health = _gameStorageService.ValidateStorage(paths);
			if (HasUnsafeIdentityMetadata(health))
			{
				throw new CollectionTargetAuthorityException(CollectionTargetAuthorityFailureKind.UnsafeStorageMetadata,
					LanguageManager.Get("GameStorage.Identity.ConflictingFolders", "NMM could not confirm which mod folders belong to this game. Open Game Storage Manager and select your existing folders."));
			}

			string storageId;
			if (!_gameStorageService.TryResolveExistingStorageId(paths, out storageId))
			{
				throw new CollectionTargetAuthorityException(CollectionTargetAuthorityFailureKind.StorageIdentityUnavailable,
					LanguageManager.Get("GameStorage.Identity.Unavailable", "NMM could not prepare this game's storage folders. Check that the drive is connected and the folders are writable, then try again."));
			}

			CollectionPhysicalDirectoryIdentity physicalDirectory;
			try
			{
				physicalDirectory = CollectionPhysicalDirectoryIdentity.Resolve(paths.GameInstallPath);
			}
			catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException ||
				exception is System.Security.SecurityException || exception is System.ComponentModel.Win32Exception)
			{
				throw new CollectionTargetAuthorityException(CollectionTargetAuthorityFailureKind.PhysicalTargetUnavailable,
					LanguageManager.Get("GameStorage.Identity.GameFolderUnavailable", "NMM could not access this game's installation folder. Check that the drive is connected and the game folder still exists, then restart NMM."), exception);
			}

			CollectionTargetIdentity target = CollectionTargetIdentity.FromCanonicalAuthority(physicalDirectory.StableKey, storageId);
			return new CollectionTargetAuthority(target, gameId, physicalDirectory.CanonicalPath, physicalDirectory.StableKey, storageId);
		}

		/// <summary>
		/// Returns whether Game Storage metadata contains an identity ambiguity that must fail closed.
		/// </summary>
		internal static bool HasUnsafeIdentityMetadata(GameStorageHealthCheck health)
		{
			if (health == null)
				return true;

			return health.Items.Any(item => item.Status == GameStorageHealthStatus.MismatchedGame ||
				item.Status == GameStorageHealthStatus.MismatchedStorageId ||
				item.Status == GameStorageHealthStatus.InvalidManifest ||
				item.Status == GameStorageHealthStatus.UnsupportedManifestVersion ||
				item.Status == GameStorageHealthStatus.FolderRoleCollision ||
				item.Status == GameStorageHealthStatus.PartialMatch ||
				item.Status == GameStorageHealthStatus.Unknown);
		}
	}
}
