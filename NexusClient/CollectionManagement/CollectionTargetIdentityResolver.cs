using System;
using System.IO;
using System.Linq;
using Nexus.Client.GameStorage;

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
					"Game Storage metadata is inconsistent and cannot be used as Collection target authority.");
			}

			string storageId;
			if (!_gameStorageService.TryResolveExistingStorageId(paths, out storageId))
			{
				throw new CollectionTargetAuthorityException(CollectionTargetAuthorityFailureKind.StorageIdentityUnavailable,
					"The selected Game Storage has no single persisted identity. Initialize or repair Game Storage before Collection mutation.");
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
					"The physical game installation directory could not be resolved safely.", exception);
			}

			CollectionTargetIdentity target = CollectionTargetIdentity.FromCanonicalAuthority(physicalDirectory.StableKey, storageId);
			return new CollectionTargetAuthority(target, gameId, physicalDirectory.CanonicalPath, physicalDirectory.StableKey, storageId);
		}

		/// <summary>
		/// Returns whether Game Storage metadata contains an identity ambiguity that must fail closed.
		/// </summary>
		private static bool HasUnsafeIdentityMetadata(GameStorageHealthCheck health)
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
