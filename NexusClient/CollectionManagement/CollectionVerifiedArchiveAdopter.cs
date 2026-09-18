using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Reuses or adopts exact verified archive bytes for one Collection acquisition request.
	/// </summary>
	/// <remarks>
	/// Mutable archive-library paths are never returned as acquisition inputs. Candidate bytes are first sealed into C4.10
	/// retained storage, then verified by an expected SHA-256 digest or by the provider's exact content-identity lookup.
	/// Filename equality and metadata freshness are never treated as integrity proof.
	/// </remarks>
	public sealed class CollectionVerifiedArchiveAdopter
	{
		internal const string VerifiedReferenceRolePrefix = "verified-acquisition-";
		private readonly ICollectionManagedArchiveSource _archiveSource;
		private readonly ICollectionArchiveIdentityVerifier _identityVerifier;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionsAcquisitionStore _acquisitionStore;

		/// <summary>
		/// Creates an archive reuse/adoption coordinator over existing native archive discovery and Collections retained storage.
		/// </summary>
		public CollectionVerifiedArchiveAdopter(ICollectionManagedArchiveSource archiveSource,
			ICollectionArchiveIdentityVerifier identityVerifier, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(archiveSource, identityVerifier, artifactStore, referenceStore, null)
		{
		}

		/// <summary>
		/// Creates an archive adopter which also checkpoints verified acquisition state for restart reconciliation.
		/// </summary>
		public CollectionVerifiedArchiveAdopter(ICollectionManagedArchiveSource archiveSource,
			ICollectionArchiveIdentityVerifier identityVerifier, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, CollectionsAcquisitionStore acquisitionStore)
		{
			_archiveSource = archiveSource ?? throw new ArgumentNullException(nameof(archiveSource));
			_identityVerifier = identityVerifier ?? throw new ArgumentNullException(nameof(identityVerifier));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			_acquisitionStore = acquisitionStore;
		}

		/// <summary>
		/// Attempts to satisfy an acquisition request from already-verified retained content or an exact existing managed archive.
		/// </summary>
		/// <returns>A verified immutable result, or <c>null</c> when no candidate can be proven exact.</returns>
		public CollectionVerifiedArchive TryAdopt(CollectionAcquisitionRequest request, CancellationToken cancellationToken)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));

			string referenceRole = CreateReferenceRole(request.SelectedArtifact);
			string ownerId = request.RequestId.ToString("D");
			CollectionVerifiedArchive alreadyVerified = TryLoadExistingRequestReference(request, ownerId, referenceRole, cancellationToken);
			if (alreadyVerified != null)
				return alreadyVerified;

			CollectionContentHash expectedHash = request.SelectedArtifact.ExpectedContentHash;
			if (expectedHash != null)
			{
				CollectionsRetainedArtifact retained = _artifactStore.GetArtifact(expectedHash);
				if (retained != null)
				{
					CollectionsRetainedArtifactReferenceRecord temporary = AcquireTemporaryReference(retained, ownerId);
					try
					{
						if (_artifactStore.VerifyArtifact(retained.ArtifactId, cancellationToken))
							return Protect(request, retained, ownerId, referenceRole,
								CollectionVerifiedArchiveSourceKind.RetainedContent,
								CollectionArchiveVerificationBasis.ExpectedContentHash);
					}
					finally
					{
						_referenceStore.ReleaseReference(temporary.ReferenceId);
					}
				}
			}

			IReadOnlyList<CollectionManagedArchiveCandidate> candidates = _archiveSource.FindCandidates(request.SelectedArtifact);
			if (candidates == null || candidates.Count == 0)
				return null;

			CollectionsRetainedArtifact verifiedArtifact = null;
			CollectionsRetainedArtifactReferenceRecord verifiedTemporary = null;
			var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				for (int index = 0; index < candidates.Count; index++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					CollectionManagedArchiveCandidate candidate = candidates[index];
					if (candidate == null || !MatchesRequestedIdentity(candidate, request.SelectedArtifact))
						continue;

					string fullPath = Path.GetFullPath(candidate.ArchivePath);
					if (!visitedPaths.Add(fullPath) || !File.Exists(fullPath))
						continue;

					// Seal the mutable native archive first. All trust-sensitive verification below observes immutable bytes,
					// so a writer cannot swap the archive between provider verification and retained publication.
					CollectionsRetainedArtifact candidateArtifact = _artifactStore.PublishFile(fullPath, cancellationToken);
					CollectionsRetainedArtifactReferenceRecord temporary = AcquireTemporaryReference(candidateArtifact, ownerId);
					bool keepTemporary = false;
					try
					{
						bool exact;
						if (expectedHash != null)
						{
							exact = expectedHash.Equals(candidateArtifact.ContentHash);
						}
						else
						{
							using (Stream immutableArchive = _artifactStore.OpenRead(candidateArtifact.ArtifactId))
								exact = _identityVerifier.IsExactMatch(request.SelectedArtifact, immutableArchive, cancellationToken);
						}

						if (!exact)
							continue;

						if (verifiedArtifact != null && !verifiedArtifact.Equals(candidateArtifact))
							throw new InvalidDataException("Multiple managed archives claim the same Collection artifact identity but verify to different immutable bytes.");

						if (verifiedArtifact == null)
						{
							verifiedArtifact = candidateArtifact;
							verifiedTemporary = temporary;
							keepTemporary = true;
						}
					}
					finally
					{
						if (!keepTemporary)
							_referenceStore.ReleaseReference(temporary.ReferenceId);
					}
				}

				if (verifiedArtifact == null)
					return null;

				return Protect(request, verifiedArtifact, ownerId, referenceRole,
					CollectionVerifiedArchiveSourceKind.ManagedArchive,
					expectedHash == null
						? CollectionArchiveVerificationBasis.ProviderContentIdentity
						: CollectionArchiveVerificationBasis.ExpectedContentHash);
			}
			finally
			{
				if (verifiedTemporary != null)
					_referenceStore.ReleaseReference(verifiedTemporary.ReferenceId);
			}
		}

		/// <summary>
		/// Attempts verified adoption of one user-selected local archive candidate.
		/// </summary>
		/// <remarks>
		/// The mutable source path is never trusted directly. Its bytes are first sealed into retained storage, protected from
		/// GC while verification runs, and then matched by expected SHA-256 or exact provider content identity.
		/// </remarks>
		public CollectionVerifiedArchive TryAdoptFile(CollectionAcquisitionRequest request, string filePath,
			CancellationToken cancellationToken)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			if (String.IsNullOrWhiteSpace(filePath))
				throw new ArgumentException("A local archive path is required.", nameof(filePath));

			string referenceRole = CreateReferenceRole(request.SelectedArtifact);
			string ownerId = request.RequestId.ToString("D");
			CollectionVerifiedArchive alreadyVerified = TryLoadExistingRequestReference(request, ownerId, referenceRole, cancellationToken);
			if (alreadyVerified != null)
				return alreadyVerified;

			string fullPath = Path.GetFullPath(filePath);
			if (!File.Exists(fullPath))
				throw new FileNotFoundException("The selected manual acquisition file no longer exists.", fullPath);

			CollectionsRetainedArtifact candidateArtifact = _artifactStore.PublishFile(fullPath, cancellationToken);
			CollectionsRetainedArtifactReferenceRecord temporary = AcquireTemporaryReference(candidateArtifact, ownerId);
			try
			{
				CollectionContentHash expectedHash = request.SelectedArtifact.ExpectedContentHash;
				bool exact;
				CollectionArchiveVerificationBasis verificationBasis;
				if (expectedHash != null)
				{
					exact = expectedHash.Equals(candidateArtifact.ContentHash);
					verificationBasis = CollectionArchiveVerificationBasis.ExpectedContentHash;
				}
				else
				{
					using (Stream immutableArchive = _artifactStore.OpenRead(candidateArtifact.ArtifactId))
						exact = _identityVerifier.IsExactMatch(request.SelectedArtifact, immutableArchive, cancellationToken);
					verificationBasis = CollectionArchiveVerificationBasis.ProviderContentIdentity;
				}

				if (!exact)
					return null;

				return Protect(request, candidateArtifact, ownerId, referenceRole,
					CollectionVerifiedArchiveSourceKind.ManualFile, verificationBasis);
			}
			finally
			{
				_referenceStore.ReleaseReference(temporary.ReferenceId);
			}
		}

		/// <summary>
		/// Attempts verified adoption of one user-selected local archive candidate without cancellation.
		/// </summary>
		public CollectionVerifiedArchive TryAdoptFile(CollectionAcquisitionRequest request, string filePath)
		{
			return TryAdoptFile(request, filePath, CancellationToken.None);
		}

		/// <summary>
		/// Attempts verified adoption without cancellation.
		/// </summary>
		public CollectionVerifiedArchive TryAdopt(CollectionAcquisitionRequest request)
		{
			return TryAdopt(request, CancellationToken.None);
		}

		private CollectionVerifiedArchive TryLoadExistingRequestReference(CollectionAcquisitionRequest request,
			string ownerId, string expectedRole, CancellationToken cancellationToken)
		{
			IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> references = _referenceStore.GetReferencesForOwner(
				CollectionsRetainedArtifactOwnerKind.Download, ownerId);
			CollectionsRetainedArtifactReferenceRecord matchingReference = null;
			for (int index = 0; index < references.Count; index++)
			{
				CollectionsRetainedArtifactReferenceRecord reference = references[index];
				if (!reference.Role.StartsWith(VerifiedReferenceRolePrefix, StringComparison.Ordinal))
					continue;
				if (!StringComparer.Ordinal.Equals(reference.Role, expectedRole))
					throw new InvalidDataException("An acquisition request identifier is already bound to a different verified artifact identity.");
				if (matchingReference != null && !StringComparer.Ordinal.Equals(matchingReference.ArtifactId, reference.ArtifactId))
					throw new InvalidDataException("An acquisition request has multiple verified retained artifacts for one exact source identity.");
				matchingReference = reference;
			}

			if (matchingReference == null)
				return null;

			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(matchingReference.ArtifactId);
			if (artifact == null || !_artifactStore.VerifyArtifact(artifact.ArtifactId, cancellationToken))
				throw new InvalidDataException("A verified acquisition reference no longer points to intact immutable retained content.");
			if (request.SelectedArtifact.ExpectedContentHash != null &&
				!request.SelectedArtifact.ExpectedContentHash.Equals(artifact.ContentHash))
				throw new InvalidDataException("The retained acquisition content no longer matches the request's expected digest.");

			if (_acquisitionStore != null)
				_acquisitionStore.MarkVerified(request, artifact.ArtifactId);
			return new CollectionVerifiedArchive(request, artifact, matchingReference,
				CollectionVerifiedArchiveSourceKind.RetainedContent,
				CollectionArchiveVerificationBasis.ExistingVerifiedReference);
		}

		private CollectionsRetainedArtifactReferenceRecord AcquireTemporaryReference(CollectionsRetainedArtifact artifact, string ownerId)
		{
			string role = "acquisition-verification-inflight-" + Guid.NewGuid().ToString("N");
			return _referenceStore.AcquireReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Download, ownerId, role);
		}

		private CollectionVerifiedArchive Protect(CollectionAcquisitionRequest request, CollectionsRetainedArtifact artifact,
			string ownerId, string role, CollectionVerifiedArchiveSourceKind sourceKind,
			CollectionArchiveVerificationBasis verificationBasis)
		{
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.AcquireReference(
				artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Download, ownerId, role);
			if (_acquisitionStore != null)
				_acquisitionStore.MarkVerified(request, artifact.ArtifactId);
			return new CollectionVerifiedArchive(request, artifact, reference, sourceKind, verificationBasis);
		}

		private static bool MatchesRequestedIdentity(CollectionManagedArchiveCandidate candidate,
			CollectionArtifactReference requestedArtifact)
		{
			if (!StringComparer.Ordinal.Equals(candidate.Scheme, requestedArtifact.Scheme))
				return false;

			string requestedDomain;
			long requestedModId;
			long requestedFileId;
			string candidateDomain;
			long candidateModId;
			long candidateFileId;
			if (NexusCollectionModFileArtifactIdentity.TryParse(requestedArtifact, out requestedDomain, out requestedModId, out requestedFileId))
			{
				return NexusCollectionModFileArtifactIdentity.TryParseStableId(candidate.StableId,
						out candidateDomain, out candidateModId, out candidateFileId) &&
					StringComparer.OrdinalIgnoreCase.Equals(candidateDomain, requestedDomain) &&
					candidateModId == requestedModId && candidateFileId == requestedFileId;
			}

			return StringComparer.Ordinal.Equals(candidate.StableId, requestedArtifact.StableId);
		}

		private static string CreateReferenceRole(CollectionArtifactReference artifact)
		{
			string expectedHash = artifact.ExpectedContentHash == null ? String.Empty : artifact.ExpectedContentHash.ToString();
			string payload = artifact.Scheme + "\n" + artifact.StableId + "\n" + expectedHash;
			using (SHA256 sha256 = SHA256.Create())
			{
				byte[] digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
				var builder = new StringBuilder(VerifiedReferenceRolePrefix.Length + (digest.Length * 2));
				builder.Append(VerifiedReferenceRolePrefix);
				for (int index = 0; index < digest.Length; index++)
					builder.Append(digest[index].ToString("x2"));
				return builder.ToString();
			}
		}
	}
}
