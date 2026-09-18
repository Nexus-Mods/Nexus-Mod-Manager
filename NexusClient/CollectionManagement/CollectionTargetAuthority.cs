using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes the canonical native authority behind one real Collection mutation target.
	/// </summary>
	/// <remarks>
	/// The physical game directory and Game Storage identity are separate concepts. A shared archive library or an NMM
	/// deployment-relative path is not sufficient to identify this authority.
	/// </remarks>
	public sealed class CollectionTargetAuthority
	{
		/// <summary>
		/// Creates a resolved target authority from canonical physical and native-storage identity components.
		/// </summary>
		internal CollectionTargetAuthority(CollectionTargetIdentity target, string gameId, string canonicalGameInstallPath,
			string physicalGameKey, string storageId)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			GameId = CollectionIdentityValidation.RequireOpaqueToken(gameId, nameof(gameId));
			CanonicalGameInstallPath = CollectionIdentityValidation.RequireOpaqueToken(canonicalGameInstallPath, nameof(canonicalGameInstallPath));
			PhysicalGameKey = CollectionIdentityValidation.RequireOpaqueToken(physicalGameKey, nameof(physicalGameKey));
			StorageId = CollectionIdentityValidation.RequireOpaqueToken(storageId, nameof(storageId));
		}

		/// <summary>
		/// Gets the opaque Collection-domain target identity.
		/// </summary>
		public CollectionTargetIdentity Target { get; }

		/// <summary>
		/// Gets the current NMM game-mode identifier for diagnostics. It is not part of physical target equality.
		/// </summary>
		public string GameId { get; }

		/// <summary>
		/// Gets the final Windows path resolved for the physical game directory.
		/// </summary>
		public string CanonicalGameInstallPath { get; }

		/// <summary>
		/// Gets the stable Windows directory key used as the physical side of target identity.
		/// </summary>
		public string PhysicalGameKey { get; }

		/// <summary>
		/// Gets the persisted Game Storage identity that owns the native NMM state for this target.
		/// </summary>
		public string StorageId { get; }
	}

	/// <summary>
	/// Classifies why a canonical Collection target authority could not be established.
	/// </summary>
	public enum CollectionTargetAuthorityFailureKind
	{
		Unknown = 0,
		PhysicalTargetUnavailable = 1,
		StorageIdentityUnavailable = 2,
		UnsafeStorageMetadata = 3
	}

	/// <summary>
	/// Reports a fail-closed Collection target-authority resolution failure.
	/// </summary>
	public sealed class CollectionTargetAuthorityException : InvalidOperationException
	{
		/// <summary>
		/// Creates a target-authority resolution exception.
		/// </summary>
		public CollectionTargetAuthorityException(CollectionTargetAuthorityFailureKind failureKind, string message)
			: base(message)
		{
			FailureKind = failureKind;
		}

		/// <summary>
		/// Creates a target-authority resolution exception with the underlying physical/storage error.
		/// </summary>
		public CollectionTargetAuthorityException(CollectionTargetAuthorityFailureKind failureKind, string message, Exception innerException)
			: base(message, innerException)
		{
			FailureKind = failureKind;
		}

		/// <summary>
		/// Gets the machine-readable reason canonical target authority could not be established.
		/// </summary>
		public CollectionTargetAuthorityFailureKind FailureKind { get; }
	}
}
