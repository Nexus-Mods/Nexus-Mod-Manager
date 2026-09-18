using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Classifies why a Collection mutation could not establish one coherent native ownership authority.
	/// </summary>
	public enum CollectionTargetOwnershipAuthorityFailureKind
	{
		Unknown = 0,
		MutationLeaseRequired = 1,
		TargetChanged = 2,
		InstallInfoUnavailable = 3,
		InstallLogMissing = 4,
		InstallLogInvalid = 5,
		InstallLogUnsupported = 6,
		AuthorityConflict = 7,
		AuthorityBindingInvalid = 8,
		AuthorityStoreUnavailable = 9,
		NativeReloadFailed = 10
	}

	/// <summary>
	/// Reports a fail-closed ownership-authority validation failure for a canonical Collection target.
	/// </summary>
	public sealed class CollectionTargetOwnershipAuthorityException : InvalidOperationException
	{
		/// <summary>
		/// Creates an ownership-authority validation exception.
		/// </summary>
		public CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind failureKind, string message)
			: base(message)
		{
			FailureKind = failureKind;
		}

		/// <summary>
		/// Creates an ownership-authority validation exception with the underlying native/storage error.
		/// </summary>
		public CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind failureKind, string message, Exception innerException)
			: base(message, innerException)
		{
			FailureKind = failureKind;
		}

		/// <summary>
		/// Gets the machine-readable validation failure reason.
		/// </summary>
		public CollectionTargetOwnershipAuthorityFailureKind FailureKind { get; }
	}
}
