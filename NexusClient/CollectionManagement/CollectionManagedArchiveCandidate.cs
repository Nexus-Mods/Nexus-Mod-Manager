using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes one mutable NMM-managed archive which may be considered for verified Collection reuse.
	/// </summary>
	/// <remarks>
	/// Candidate metadata is only a discovery hint. It is never sufficient proof that the bytes satisfy the Collection;
	/// <see cref="CollectionVerifiedArchiveAdopter"/> seals and verifies the exact bytes before returning them.
	/// </remarks>
	public sealed class CollectionManagedArchiveCandidate
	{
		/// <summary>
		/// Creates a managed archive candidate.
		/// </summary>
		public CollectionManagedArchiveCandidate(string scheme, string stableId, string archivePath)
		{
			Scheme = CollectionIdentityValidation.RequireOpaqueToken(scheme, nameof(scheme));
			StableId = CollectionIdentityValidation.RequireOpaqueToken(stableId, nameof(stableId));
			if (String.IsNullOrWhiteSpace(archivePath))
				throw new ArgumentException("A managed archive path is required.", nameof(archivePath));
			ArchivePath = archivePath;
		}

		/// <summary>Gets the normalized artifact scheme represented by the managed metadata.</summary>
		public string Scheme { get; }

		/// <summary>Gets the normalized stable artifact identity represented by the managed metadata.</summary>
		public string StableId { get; }

		/// <summary>Gets the mutable native archive path which must be snapshotted before verification.</summary>
		public string ArchivePath { get; }
	}
}
