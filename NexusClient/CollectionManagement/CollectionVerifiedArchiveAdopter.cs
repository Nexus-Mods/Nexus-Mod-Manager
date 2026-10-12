using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
	/// Ordinary archives remain in the native archive library. Durable content identities reference those files, verified
	/// by an expected SHA-256 digest or the provider's exact content identity; missing archives return to acquisition.
	/// Filename equality and metadata freshness are never treated as integrity proof.
	/// </remarks>
	public sealed class CollectionVerifiedArchiveAdopter
	{
		internal const string VerifiedReferenceRolePrefix = "verified-acquisition-";
		internal const string MetadataReferenceRolePrefix = "metadata-trusted-acquisition-";
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
			return TryAdopt(request, cancellationToken, false);
		}

		/// <summary>Adopts an existing library archive, optionally trusting exact Nexus metadata and unchanged saved SHA-256 stamps.</summary>
		public CollectionVerifiedArchive TryAdopt(CollectionAcquisitionRequest request, CancellationToken cancellationToken, bool skipExistingArchiveMd5)
		{
			return TryAdopt(request, cancellationToken, skipExistingArchiveMd5, false);
		}

		/// <summary>Uses the first exact matching archive in a stable order when the plan authorizes Automatic Mode.</summary>
		public CollectionVerifiedArchive TryAdopt(CollectionAcquisitionRequest request, CancellationToken cancellationToken,
			bool skipExistingArchiveMd5, bool automaticallySelectArchive)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));

			string referenceRole = CreateReferenceRole(request.SelectedArtifact);
			string ownerId = request.RequestId.ToString("D");
			if (skipExistingArchiveMd5)
			{
				CollectionVerifiedArchive cached = TryAdoptCachedExistingArchive(request, ownerId, referenceRole, cancellationToken, automaticallySelectArchive);
				if (cached != null) return cached;
			}
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
						if (CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, retained.ByteLength) &&
							_artifactStore.VerifyArtifact(retained.ArtifactId, cancellationToken))
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

			IReadOnlyList<CollectionManagedArchiveCandidate> candidates = OrderCandidates(_archiveSource.FindCandidates(request.SelectedArtifact));
			if (candidates == null || candidates.Count == 0)
				return null;

			CollectionsRetainedArtifact verifiedArtifact = null;
			CollectionsRetainedArtifactReferenceRecord verifiedTemporary = null;
			CollectionManagedArchiveCandidate verifiedCandidate = null;
			bool trustedMetadata = false;
			var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				for (int index = 0; index < candidates.Count; index++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					CollectionManagedArchiveCandidate candidate = candidates[index];
					if (candidate == null || !MatchesRequestedIdentity(candidate, request.SelectedArtifact))
						continue;
					// A stale file ID is only a recovery hint. Once an ordinary candidate verifies,
					// unrelated files from the same Nexus mod need no hashing or provider lookup.
					if (verifiedCandidate != null && !verifiedCandidate.IsFallback && candidate.IsFallback)
						continue;

					string fullPath = Path.GetFullPath(candidate.ArchivePath);
					if (!visitedPaths.Add(fullPath) || !File.Exists(fullPath))
						continue;
					// Skipping MD5 trusts repository metadata, not a size contradicting the curator's exact file.
					if (!CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, new FileInfo(fullPath).Length))
						continue;

					// SHA-256 still tracks the observed bytes when the user explicitly trusts existing Nexus metadata.
					string candidateDomain; long candidateModId; long candidateFileId;
					bool skipCandidateMd5 = skipExistingArchiveMd5 && !candidate.IsFallback &&
						NexusCollectionModFileArtifactIdentity.TryParse(request.SelectedArtifact, out candidateDomain, out candidateModId, out candidateFileId);
					bool trustCandidateMetadata = skipCandidateMd5 && expectedHash == null;
					CollectionsRetainedArtifact candidateArtifact = skipCandidateMd5
						? _artifactStore.ReferenceArchiveFileWithoutMd5(fullPath, cancellationToken)
						: _artifactStore.ReferenceArchiveFile(fullPath, cancellationToken);
					if (!CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, candidateArtifact.ByteLength))
						continue;
					CollectionsRetainedArtifactReferenceRecord temporary = AcquireTemporaryReference(candidateArtifact, ownerId);
					bool keepTemporary = false;
					try
					{
						bool exact;
						if (trustCandidateMetadata)
						{
							exact = true; // The captured user preference trusts the already-matching Nexus metadata.
						}
						else if (expectedHash != null)
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

						// Archive registration already SHA-256 hashed these exact bytes. Once identity is accepted,
						// promote that just-published proof so later preparation does not reread multi-gigabyte retained content.
						if (!_artifactStore.PromotePublishedArtifactVerification(candidateArtifact) &&
							!_artifactStore.VerifyArtifact(candidateArtifact.ArtifactId, cancellationToken))
							throw new InvalidDataException("The just-published Collection archive changed during exact identity verification.");

						if (verifiedArtifact != null && !verifiedArtifact.Equals(candidateArtifact))
							throw new CollectionArchiveIdentityConflictException(request.SelectedArtifact, verifiedCandidate.ArchivePath, fullPath);

						if (verifiedArtifact == null)
						{
							verifiedArtifact = candidateArtifact;
							verifiedTemporary = temporary;
							verifiedCandidate = candidate;
							trustedMetadata = trustCandidateMetadata;
							keepTemporary = true;
						}
						if (automaticallySelectArchive)
						{
							ReportAutomaticArchiveSelection(candidate, candidates.Count);
							break;
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
				if (!trustedMetadata && managedSource != null && NexusCollectionModFileArtifactIdentity.TryParse(request.SelectedArtifact, out nexusDomain, out nexusModId, out nexusFileId))
					managedSource.ConfirmVerifiedCandidate(verifiedCandidate, request.SelectedArtifact, verifiedArtifact, cancellationToken);

				return Protect(request, verifiedArtifact, ownerId, referenceRole,
					CollectionVerifiedArchiveSourceKind.ManagedArchive,
					trustedMetadata ? CollectionArchiveVerificationBasis.ManagedArchiveMetadata : expectedHash == null
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
			if (!CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, new FileInfo(fullPath).Length))
				return null;

			CollectionsRetainedArtifact candidateArtifact = _artifactStore.ReferenceArchiveFile(fullPath, cancellationToken);
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
		/// Seals an alternate external archive and pins it briefly while the user considers a new reviewed plan.
		/// The immutable SHA-256 and the actual MD5/length describe the chosen bytes, not a claimed provider version.
		/// </summary>
		public CollectionAlternateArchiveCandidate InspectAlternateExternalFile(CollectionAcquisitionRequest request,
			string filePath, CancellationToken cancellationToken)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			if (String.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("An archive path is required.", nameof(filePath));
			string originalMd5; long originalLength;
			if (!CollectionExternalArtifactIdentity.TryParse(request.SelectedArtifact, out originalMd5, out originalLength) ||
				request.SelectedArtifact.ExpectedContentHash != null)
				throw new NotSupportedException("An alternative can replace only an original exact MD5/length external source.");
			string path = Path.GetFullPath(filePath);
			if (!File.Exists(path)) throw new FileNotFoundException("The alternative archive is missing.", path);
			CollectionsRetainedArtifact artifact = _artifactStore.ReferenceArchiveFile(path, cancellationToken);
			string actualMd5;
			if (!_artifactStore.TryGetPublishedMd5(artifact.ArtifactId, out actualMd5))
				throw new InvalidDataException("NMM could not establish the copied alternative archive's MD5.");
			if (StringComparer.Ordinal.Equals(originalMd5, actualMd5) && originalLength == artifact.ByteLength)
				throw new InvalidOperationException("This archive matches the original requested MD5 and size; use the exact acquisition path.");
			if (!_artifactStore.PromotePublishedArtifactVerification(artifact) &&
				!_artifactStore.VerifyArtifact(artifact.ArtifactId, cancellationToken))
				throw new InvalidDataException("The sealed alternative archive failed integrity verification.");
			// The old request is only a transient holding owner. A new reviewed plan MUST acquire its own reference.
			string ownerId = request.RequestId.ToString("D");
			var hold = _referenceStore.AcquireReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Download,
				ownerId, "alternate-candidate-" + Guid.NewGuid().ToString("N"));
			return new CollectionAlternateArchiveCandidate(request, artifact, hold, actualMd5);
		}

		/// <summary>Releases the transient candidate lifetime reference after the user declines or re-preparation returns.</summary>
		public void ReleaseAlternateExternalFile(CollectionAlternateArchiveCandidate candidate)
		{
			if (candidate == null) throw new ArgumentNullException(nameof(candidate));
			_referenceStore.ReleaseReference(candidate.Reference.ReferenceId);
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

			bool cachedPreparation = verifiedArchive.VerificationBasis == CollectionArchiveVerificationBasis.ManagedArchiveMetadataWithCachedHash &&
				_artifactStore.IsTrustedPreparationArchiveCurrent(verifiedArchive.Artifact);
			if (!CollectionExternalArtifactIdentity.MatchesByteLength(refreshedRequest.SelectedArtifact, verifiedArchive.Artifact.ByteLength))
				throw new InvalidDataException("The previously acquired archive size does not match the Collection's requested file. Acquire the requested archive again.");
			if (!cachedPreparation && !_artifactStore.VerifyArtifact(verifiedArchive.Artifact.ArtifactId, cancellationToken))
				throw new InvalidDataException("The previously verified Collection acquisition artifact is missing or corrupt.");
			if (refreshedRequest.SelectedArtifact.ExpectedContentHash != null &&
				!refreshedRequest.SelectedArtifact.ExpectedContentHash.Equals(verifiedArchive.Artifact.ContentHash))
				throw new InvalidDataException("The retained acquisition artifact no longer matches the refreshed request's expected digest.");

			string ownerId = refreshedRequest.RequestId.ToString("D");
			string role = CreateReferenceRole(refreshedRequest.SelectedArtifact);
			if (IsMetadataTrusted(verifiedArchive.VerificationBasis))
				return Protect(refreshedRequest, verifiedArchive.Artifact, ownerId, role,
					CollectionVerifiedArchiveSourceKind.RetainedContent, verifiedArchive.VerificationBasis);
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

		/// <summary>Trusts only exact existing Nexus candidates whose saved real SHA-256 still describes an unchanged file stamp.</summary>
		private CollectionVerifiedArchive TryAdoptCachedExistingArchive(CollectionAcquisitionRequest request,
			string ownerId, string expectedRole, CancellationToken cancellationToken, bool automaticallySelectArchive)
		{
			string domain; long modId; long fileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(request.SelectedArtifact, out domain, out modId, out fileId)) return null;
			string metadataRole = CreateMetadataReferenceRole(request.SelectedArtifact);
			foreach (CollectionsRetainedArtifactReferenceRecord reference in _referenceStore.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Download, ownerId))
			{
				if ((reference.Role.StartsWith(VerifiedReferenceRolePrefix, StringComparison.Ordinal) ||
					reference.Role.StartsWith(MetadataReferenceRolePrefix, StringComparison.Ordinal)) &&
					!StringComparer.Ordinal.Equals(reference.Role, expectedRole) && !StringComparer.Ordinal.Equals(reference.Role, metadataRole))
					throw new InvalidDataException("An acquisition request identifier is already bound to a different artifact identity.");
			}
			IReadOnlyList<CollectionManagedArchiveCandidate> candidates = OrderCandidates(_archiveSource.FindCandidates(request.SelectedArtifact));
			if (candidates == null) return null;
			CollectionsRetainedArtifact matched = null;
			string matchedPath = null;
			foreach (CollectionManagedArchiveCandidate candidate in candidates)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (candidate == null || candidate.IsFallback || !MatchesRequestedIdentity(candidate, request.SelectedArtifact)) continue;
				if (!File.Exists(candidate.ArchivePath)) continue;
				if (!CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, new FileInfo(candidate.ArchivePath).Length)) continue;
				CollectionsRetainedArtifact artifact = _artifactStore.TryReferenceCachedArchiveFile(candidate.ArchivePath, cancellationToken);
				if (artifact == null) return null; // Unknown or changed files still need their first/current hashing pass.
				if (!CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, artifact.ByteLength) ||
					(request.SelectedArtifact.ExpectedContentHash != null && !request.SelectedArtifact.ExpectedContentHash.Equals(artifact.ContentHash)))
					return null;
				if (matched != null && !matched.Equals(artifact))
					throw new CollectionArchiveIdentityConflictException(request.SelectedArtifact, matchedPath, candidate.ArchivePath);
				matched = artifact;
				matchedPath = candidate.ArchivePath;
				if (automaticallySelectArchive)
				{
					ReportAutomaticArchiveSelection(candidate, candidates.Count);
					break;
				}
			}
			return matched == null ? null : Protect(request, matched, ownerId, expectedRole,
				CollectionVerifiedArchiveSourceKind.ManagedArchive, CollectionArchiveVerificationBasis.ManagedArchiveMetadataWithCachedHash);
		}

		/// <summary>Prefers exact repository metadata, then uses archive paths to make automatic choice independent of discovery order.</summary>
		private static IReadOnlyList<CollectionManagedArchiveCandidate> OrderCandidates(IReadOnlyList<CollectionManagedArchiveCandidate> candidates)
		{
			return candidates == null ? null : candidates.Where(x => x != null).OrderBy(x => x.IsFallback)
				.ThenBy(x => Path.GetFullPath(x.ArchivePath), StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>Reports which archive Automatic Mode selected when several local candidates were discovered.</summary>
		private static void ReportAutomaticArchiveSelection(CollectionManagedArchiveCandidate candidate, int candidateCount)
		{
			if (candidateCount < 2) return;
			System.Diagnostics.Trace.TraceInformation("Collection Automatic Mode selected existing archive: {0}", candidate.ArchivePath);
			CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Format(
				"Collections.Workflow.SelectedExistingArchive", "Automatic Mode selected existing archive: {0}", Path.GetFileName(candidate.ArchivePath)), true);
		}

		/// <summary>Distinguishes explicit metadata trust from provider-verified acquisition results.</summary>
		internal static bool IsMetadataTrusted(CollectionArchiveVerificationBasis basis)
		{
			return basis == CollectionArchiveVerificationBasis.ManagedArchiveMetadata ||
				basis == CollectionArchiveVerificationBasis.ManagedArchiveMetadataWithCachedHash;
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
			// Old acquisitions may have accepted stale native file metadata. The retained manifest's size
			// disqualifies those bytes before they can be reused or treated as a competing exact archive.
			artifactIds.RemoveWhere(artifactId =>
			{
				CollectionsRetainedArtifact recorded = _artifactStore.GetArtifact(artifactId);
				return recorded != null && !CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, recorded.ByteLength);
			});
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
			if (!CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, artifact.ByteLength))
				return null;

			if (_artifactStore.IsArchiveReference(artifact.ArtifactId) && !_artifactStore.VerifyArtifact(artifact.ArtifactId, cancellationToken))
				return null;

			// Download/Prepare does not consume retained payload bytes. At this stage require the sealed metadata/path/length
			// to remain present, but defer the expensive full SHA-256 re-read to the trust-sensitive apply/recovery boundary.
			// This keeps repeated Prepare O(metadata) while the native installer still performs the final archive SHA check.
			try
			{
				using (Stream retained = _artifactStore.OpenRead(artifact.ArtifactId)) cancellationToken.ThrowIfCancellationRequested();
			}
			catch (FileNotFoundException) { return null; }
			catch (DirectoryNotFoundException) { return null; }

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
				CollectionsRetainedArtifact recorded = _artifactStore.GetArtifact(reference.ArtifactId);
				if (recorded != null && !CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, recorded.ByteLength))
				{
					_referenceStore.ReleaseReference(reference.ReferenceId);
					continue;
				}
				if (matchingReference != null && !StringComparer.Ordinal.Equals(matchingReference.ArtifactId, reference.ArtifactId))
					throw new InvalidDataException("An acquisition request has multiple verified retained artifacts for one exact source identity.");
				matchingReference = reference;
			}

			if (matchingReference == null)
				return null;

			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(matchingReference.ArtifactId);
			if (artifact == null || !_artifactStore.VerifyArtifact(artifact.ArtifactId, cancellationToken))
				return null; // A deleted or changed library archive must go through ordinary acquisition again.
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
			if (!CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, artifact.ByteLength))
				throw new InvalidDataException("The acquired archive size does not match the Collection's requested file.");
			bool trustedMetadata = IsMetadataTrusted(verificationBasis);
			if (trustedMetadata) role = CreateMetadataReferenceRole(request.SelectedArtifact);
			foreach (CollectionsRetainedArtifactReferenceRecord previous in _referenceStore.GetReferencesForOwner(
				CollectionsRetainedArtifactOwnerKind.Download, ownerId))
			{
				if (!StringComparer.Ordinal.Equals(previous.Role, role)) continue;
				CollectionsRetainedArtifact recorded = _artifactStore.GetArtifact(previous.ArtifactId);
				if (recorded != null && !CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, recorded.ByteLength))
					_referenceStore.ReleaseReference(previous.ReferenceId);
			}
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.AcquireReference(
				artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Download, ownerId, role);
			// Metadata trust must never become a cached provider/MD5 verification for future default-mode preparations.
			if (_acquisitionStore != null && !trustedMetadata)
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

		/// <summary>Creates a separate durable role for user-trusted Nexus metadata without claiming provider verification.</summary>
		internal static string CreateMetadataReferenceRole(CollectionArtifactReference artifact)
		{
			return MetadataReferenceRolePrefix + CreateReferenceRole(artifact).Substring(VerifiedReferenceRolePrefix.Length);
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

	/// <summary>Identifies the local archives which require an explicit or automatic acquisition choice.</summary>
	internal sealed class CollectionArchiveIdentityConflictException : IOException
	{
		/// <summary>Creates a user-readable conflict without losing the requested identity or candidate paths.</summary>
		internal CollectionArchiveIdentityConflictException(CollectionArtifactReference artifact, string firstPath, string secondPath)
			: base(Nexus.Client.Util.Localization.LanguageManager.Format("Collections.Acquisition.ArchiveConflict",
				"Two existing archives match {0}, but their contents differ:\n{1}\n{2}", artifact.ToString(), firstPath, secondPath))
		{
			Artifact = artifact;
			FirstPath = firstPath;
			SecondPath = secondPath;
		}

		internal CollectionArtifactReference Artifact { get; }
		internal string FirstPath { get; }
		internal string SecondPath { get; }
	}
}
