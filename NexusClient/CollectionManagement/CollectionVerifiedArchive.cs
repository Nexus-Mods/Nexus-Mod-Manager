using System;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes how exact immutable bytes were verified for a Collection acquisition request.
	/// </summary>
	public enum CollectionArchiveVerificationBasis
	{
		Unknown = 0,
		ExistingVerifiedReference = 1,
		ExpectedContentHash = 2,
		ProviderContentIdentity = 3
	}

	/// <summary>
	/// Identifies where verified immutable acquisition bytes originated.
	/// </summary>
	public enum CollectionVerifiedArchiveSourceKind
	{
		Unknown = 0,
		RetainedContent = 1,
		ManagedArchive = 2,

		/// <summary>Verified bytes supplied explicitly by the user as a local archive candidate.</summary>
		ManualFile = 3
	}

	/// <summary>
	/// Immutable verified acquisition input backed by a C4.10 retained blob and a C4.11 lifetime reference.
	/// </summary>
	public sealed class CollectionVerifiedArchive
	{
		/// <summary>
		/// Creates a verified immutable acquisition result.
		/// </summary>
		public CollectionVerifiedArchive(CollectionAcquisitionRequest request, CollectionsRetainedArtifact artifact,
			CollectionsRetainedArtifactReferenceRecord reference, CollectionVerifiedArchiveSourceKind sourceKind,
			CollectionArchiveVerificationBasis verificationBasis)
		{
			Request = request ?? throw new ArgumentNullException(nameof(request));
			Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
			Reference = reference ?? throw new ArgumentNullException(nameof(reference));
			if (!Enum.IsDefined(typeof(CollectionVerifiedArchiveSourceKind), sourceKind) || sourceKind == CollectionVerifiedArchiveSourceKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(sourceKind));
			if (!Enum.IsDefined(typeof(CollectionArchiveVerificationBasis), verificationBasis) || verificationBasis == CollectionArchiveVerificationBasis.Unknown)
				throw new ArgumentOutOfRangeException(nameof(verificationBasis));
			SourceKind = sourceKind;
			VerificationBasis = verificationBasis;
		}

		/// <summary>Gets the acquisition request satisfied by these bytes.</summary>
		public CollectionAcquisitionRequest Request { get; }

		/// <summary>Gets the immutable retained artifact which later acquisition stages may consume.</summary>
		public CollectionsRetainedArtifact Artifact { get; }

		/// <summary>Gets the durable download-owner reference protecting the retained artifact.</summary>
		public CollectionsRetainedArtifactReferenceRecord Reference { get; }

		/// <summary>Gets where the verified immutable bytes came from.</summary>
		public CollectionVerifiedArchiveSourceKind SourceKind { get; }

		/// <summary>Gets the evidence used to establish exact artifact identity.</summary>
		public CollectionArchiveVerificationBasis VerificationBasis { get; }
	}
}
