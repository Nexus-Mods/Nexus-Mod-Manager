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

			// Plan IDs intentionally change when a user prepares/reviews again. The provider artifact identity does not.
			// Reuse immutable bytes that a previous acquisition request already proved exact instead of re-copying and
			// re-hashing the same multi-gigabyte managed archive into retained storage for every new plan.
			CollectionVerifiedArchive reusableVerified = TryLoadReusableVerifiedArtifact(request, ownerId, referenceRole, cancellationToken);
			if (reusableVerified != null)
				return reusableVerified;

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
			CollectionManagedArchiveCandidate verifiedCandidate = null;
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
					if (!CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, candidateArtifact.ByteLength))
						continue;
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
							string publishedMd5;
							ICollectionArchiveMd5IdentityVerifier md5Verifier = _identityVerifier as ICollectionArchiveMd5IdentityVerifier;
							if (md5Verifier != null && _artifactStore.TryGetPublishedMd5(candidateArtifact.ArtifactId, out publishedMd5))
								exact = md5Verifier.IsExactMatchByMd5(request.SelectedArtifact, publishedMd5, cancellationToken);
							else
							{
								using (Stream immutableArchive = _artifactStore.OpenRead(candidateArtifact.ArtifactId))
									exact = _identityVerifier.IsExactMatch(request.SelectedArtifact, immutableArchive, cancellationToken);
							}
						}

						if (!exact)
							continue;

						// PublishFile already SHA-256 hashed these exact bytes. Once provider/expected identity agrees,
						// promote that just-published proof so later preparation does not reread multi-gigabyte retained content.
						if (!_artifactStore.PromotePublishedArtifactVerification(candidateArtifact) &&
							!_artifactStore.VerifyArtifact(candidateArtifact.ArtifactId, cancellationToken))
							throw new InvalidDataException("The just-published Collection archive changed during exact identity verification.");

						if (verifiedArtifact != null && !verifiedArtifact.Equals(candidateArtifact))
							throw new InvalidDataException("Multiple managed archives claim the same Collection artifact identity but verify to different immutable bytes.");

						if (verifiedArtifact == null)
						{
							verifiedArtifact = candidateArtifact;
							verifiedTemporary = temporary;
							verifiedCandidate = candidate;
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

				ModManagerCollectionManagedArchiveSource managedSource = _archiveSource as ModManagerCollectionManagedArchiveSource;
				string nexusDomain; long nexusModId; long nexusFileId;
				if (managedSource != null && NexusCollectionModFileArtifactIdentity.TryParse(request.SelectedArtifact, out nexusDomain, out nexusModId, out nexusFileId))
					managedSource.ConfirmVerifiedCandidate(verifiedCandidate, request.SelectedArtifact, verifiedArtifact, cancellationToken);

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
			if (!CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, candidateArtifact.ByteLength))
				return null;
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
					string publishedMd5;
					ICollectionArchiveMd5IdentityVerifier md5Verifier = _identityVerifier as ICollectionArchiveMd5IdentityVerifier;
					if (md5Verifier != null && _artifactStore.TryGetPublishedMd5(candidateArtifact.ArtifactId, out publishedMd5))
						exact = md5Verifier.IsExactMatchByMd5(request.SelectedArtifact, publishedMd5, cancellationToken);
					else
					{
						using (Stream immutableArchive = _artifactStore.OpenRead(candidateArtifact.ArtifactId))
							exact = _identityVerifier.IsExactMatch(request.SelectedArtifact, immutableArchive, cancellationToken);
					}
					verificationBasis = CollectionArchiveVerificationBasis.ProviderContentIdentity;
				}

				if (!exact)
					return null;
				if (!_artifactStore.PromotePublishedArtifactVerification(candidateArtifact) &&
					!_artifactStore.VerifyArtifact(candidateArtifact.ArtifactId, cancellationToken))
					throw new InvalidDataException("The just-published manual Collection archive changed during exact identity verification.");

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
		/// Protects deterministic archive bytes materialized from the exact revision-owned Collection bundle.
		/// </summary>
		/// <remarks>
		/// The caller must already have resolved the embedded Vortex member against the retained manifest and bundle. This method
		/// performs the retained-content integrity/lifetime transition only; it does not accept arbitrary local files as bundle input.
		/// </remarks>
		internal CollectionVerifiedArchive AdoptRevisionBundleMaterialization(CollectionAcquisitionRequest request,
			CollectionsRetainedArtifact artifact, CancellationToken cancellationToken)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			if (artifact == null) throw new ArgumentNullException(nameof(artifact));
			if (!CollectionBundledArtifactIdentity.IsBundle(request.SelectedArtifact))
				throw new ArgumentException("Only a normalized Collection bundle artifact can use revision-bundle materialization.", nameof(request));
			if (request.SelectedArtifact.ExpectedContentHash != null && !request.SelectedArtifact.ExpectedContentHash.Equals(artifact.ContentHash))
				throw new InvalidDataException("The materialized Collection bundle archive does not match the request's expected digest.");
			CollectionsRetainedArtifact stored = _artifactStore.GetArtifact(artifact.ArtifactId);
			if (stored == null || !stored.ContentHash.Equals(artifact.ContentHash) || stored.ByteLength != artifact.ByteLength ||
				!_artifactStore.VerifyArtifact(artifact.ArtifactId, cancellationToken))
				throw new InvalidDataException("The materialized Collection bundle archive is not intact in retained storage.");

			string role = CreateReferenceRole(request.SelectedArtifact);
			string ownerId = request.RequestId.ToString("D");
			CollectionVerifiedArchive existing = TryLoadExistingRequestReference(request, ownerId, role, cancellationToken);
			if (existing != null)
			{
				if (!existing.Artifact.ContentHash.Equals(artifact.ContentHash) || existing.Artifact.ByteLength != artifact.ByteLength)
					throw new InvalidDataException("The same Collection bundle request is already bound to different materialized bytes.");
				return existing;
			}

			return Protect(request, artifact, ownerId, role, CollectionVerifiedArchiveSourceKind.RevisionBundle,
				CollectionArchiveVerificationBasis.RevisionBundleMaterialization);
		}

		/// <summary>
		/// Rebinds already verified immutable bytes to a refreshed plan-version request for the same exact member artifact.
		/// </summary>
		/// <remarks>
		/// This is a lifetime/provenance rebind only. It never changes the selected artifact or recipe and never reuses bytes across
		/// revisions, targets or members. Full retained-blob integrity is rechecked before the new request reference is published.
		/// </remarks>
		public CollectionVerifiedArchive RebindVerified(CollectionVerifiedArchive verifiedArchive,
			CollectionAcquisitionRequest refreshedRequest, CancellationToken cancellationToken)
		{
			if (verifiedArchive == null)
				throw new ArgumentNullException(nameof(verifiedArchive));
			if (refreshedRequest == null)
				throw new ArgumentNullException(nameof(refreshedRequest));

			CollectionAcquisitionRequest previous = verifiedArchive.Request;
			if (!previous.Revision.Equals(refreshedRequest.Revision) ||
				!previous.Target.Equals(refreshedRequest.Target) ||
				!previous.MemberKey.Equals(refreshedRequest.MemberKey) ||
				previous.Requirement != refreshedRequest.Requirement ||
				!previous.SelectedArtifact.Equals(refreshedRequest.SelectedArtifact) ||
				!previous.RecipeIdentity.Equals(refreshedRequest.RecipeIdentity))
			{
				throw new ArgumentException("Verified Collection acquisition bytes may be rebound only to the same exact revision, target, member, artifact and recipe.", nameof(refreshedRequest));
			}

			if (!_artifactStore.VerifyArtifact(verifiedArchive.Artifact.ArtifactId, cancellationToken))
				throw new InvalidDataException("The previously verified Collection acquisition artifact is missing or corrupt.");
			if (refreshedRequest.SelectedArtifact.ExpectedContentHash != null &&
				!refreshedRequest.SelectedArtifact.ExpectedContentHash.Equals(verifiedArchive.Artifact.ContentHash))
				throw new InvalidDataException("The retained acquisition artifact no longer matches the refreshed request's expected digest.");

			string ownerId = refreshedRequest.RequestId.ToString("D");
			string role = CreateReferenceRole(refreshedRequest.SelectedArtifact);
			CollectionVerifiedArchive existing = TryLoadExistingRequestReference(refreshedRequest, ownerId, role, cancellationToken);
			if (existing != null)
				return existing;

			return Protect(refreshedRequest, verifiedArchive.Artifact, ownerId, role,
				CollectionVerifiedArchiveSourceKind.RetainedContent, CollectionArchiveVerificationBasis.ExistingVerifiedReference);
		}

		/// <summary>
		/// Rebinds verified immutable bytes without cancellation.
		/// </summary>
		public CollectionVerifiedArchive RebindVerified(CollectionVerifiedArchive verifiedArchive,
			CollectionAcquisitionRequest refreshedRequest)
		{
			return RebindVerified(verifiedArchive, refreshedRequest, CancellationToken.None);
		}

		/// <summary>
		/// Attempts verified adoption without cancellation.
		/// </summary>
		public CollectionVerifiedArchive TryAdopt(CollectionAcquisitionRequest request)
		{
			return TryAdopt(request, CancellationToken.None);
		}

		private CollectionVerifiedArchive TryLoadReusableVerifiedArtifact(CollectionAcquisitionRequest request,
			string ownerId, string expectedRole, CancellationToken cancellationToken)
		{
			var artifactIds = new HashSet<string>(StringComparer.Ordinal);

			// Verified acquisition rows are the durable identity/result mapping. Unlike request-owned lifetime references,
			// they survive plan/request replacement and therefore remain the primary cross-Prepare reuse source.
			if (_acquisitionStore != null)
			{
				foreach (string artifactId in _acquisitionStore.GetVerifiedArtifactIds(request.SelectedArtifact))
					artifactIds.Add(artifactId);
			}

			// Keep compatibility with older verified rows that may have a retained reference but no acquisition result row.
			if (artifactIds.Count == 0)
			{
				IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> references = _referenceStore.GetReferencesForRole(
					CollectionsRetainedArtifactOwnerKind.Download, expectedRole);
				for (int index = 0; index < references.Count; index++)
					artifactIds.Add(references[index].ArtifactId);
			}

			if (artifactIds.Count == 0)
				return null;
			if (artifactIds.Count != 1)
				throw new InvalidDataException("The same exact Collection provider artifact is durably bound to different retained byte identities.");

			string reusableArtifactId = null;
			foreach (string artifactId in artifactIds)
			{
				reusableArtifactId = artifactId;
				break;
			}

			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(reusableArtifactId);
			if (artifact == null)
				throw new InvalidDataException("Previously verified Collection acquisition content is no longer recorded in retained storage.");

			// Download/Prepare does not consume retained payload bytes. At this stage require the sealed metadata/path/length
			// to remain present, but defer the expensive full SHA-256 re-read to the trust-sensitive apply/recovery boundary.
			// This keeps repeated Prepare O(metadata) while the native installer still performs the final archive SHA check.
			using (Stream retained = _artifactStore.OpenRead(artifact.ArtifactId))
			{
				cancellationToken.ThrowIfCancellationRequested();
			}

			if (request.SelectedArtifact.ExpectedContentHash != null &&
				!request.SelectedArtifact.ExpectedContentHash.Equals(artifact.ContentHash))
				throw new InvalidDataException("Previously verified Collection acquisition content no longer matches the request's expected digest.");

			return Protect(request, artifact, ownerId, expectedRole, CollectionVerifiedArchiveSourceKind.RetainedContent,
				CollectionArchiveVerificationBasis.ExistingVerifiedReference);
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

		/// <summary>Creates the deterministic durable verified-acquisition role for one exact selected artifact.</summary>
		internal static string CreateReferenceRole(CollectionArtifactReference artifact)
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
