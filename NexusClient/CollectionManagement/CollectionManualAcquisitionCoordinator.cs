using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Stable user-facing mediation metadata reloaded from retained Collection source; never part of artifact identity.</summary>
	public sealed class CollectionManualAcquisitionHint
	{
		public CollectionManualAcquisitionHint(Uri browserUri, string instructions)
		{
			if (browserUri != null && (!browserUri.IsAbsoluteUri ||
				(!StringComparer.OrdinalIgnoreCase.Equals(browserUri.Scheme, Uri.UriSchemeHttp) &&
				 !StringComparer.OrdinalIgnoreCase.Equals(browserUri.Scheme, Uri.UriSchemeHttps))))
				throw new ArgumentException("A manual acquisition browser page must be absolute HTTP(S).", nameof(browserUri));
			BrowserUri = browserUri;
			Instructions = String.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim();
		}
		public Uri BrowserUri { get; }
		public string Instructions { get; }
	}

	/// <summary>Resolves transient/user-facing acquisition hints from retained source without widening artifact identity.</summary>
	public interface ICollectionManualAcquisitionHintProvider
	{
		CollectionManualAcquisitionHint GetHint(CollectionAcquisitionRequest request);
	}
	/// <summary>
	/// A sealed, user-selected external archive that does not match the curator's original file.
	/// This is only a proposed choice; no installation is authorized before a new reviewed plan.
	/// </summary>
	public sealed class CollectionAlternateArchiveCandidate
	{
		internal CollectionAlternateArchiveCandidate(CollectionAcquisitionRequest request,
			CollectionsRetainedArtifact artifact, CollectionsRetainedArtifactReferenceRecord reference, string md5,
			IEnumerable<CollectionResolvedArtifactChoice> previousAlternatives = null,
			CollectionManifestSourceSnapshot previousNexusManifestSource = null,
			IEnumerable<CollectionResolvedArtifactChoice> previousNexusChoices = null)
		{
			Request = request ?? throw new ArgumentNullException(nameof(request));
			Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
			Reference = reference ?? throw new ArgumentNullException(nameof(reference));
			Md5 = md5 ?? throw new ArgumentNullException(nameof(md5));
			SelectedArtifact = new CollectionArtifactReference(CollectionExternalArtifactIdentity.Scheme,
				CollectionExternalArtifactIdentity.Format(md5, artifact.ByteLength), artifact.ContentHash);
			PreviousAlternatives = Array.AsReadOnly((previousAlternatives ?? Enumerable.Empty<CollectionResolvedArtifactChoice>()).ToArray());
			PreviousNexusManifestSource = previousNexusManifestSource;
			PreviousNexusChoices = Array.AsReadOnly((previousNexusChoices ?? Enumerable.Empty<CollectionResolvedArtifactChoice>()).ToArray());
			if (PreviousNexusChoices.Count != 0 && PreviousNexusManifestSource == null)
				throw new ArgumentException("Previously resolved Nexus policies must be bound to the exact retained manifest source.", nameof(previousNexusManifestSource));
		}
		/// <summary>Carries earlier user-approved alternate choices into the next immutable review.</summary>
		internal CollectionAlternateArchiveCandidate WithPreviousAlternatives(IEnumerable<CollectionResolvedArtifactChoice> choices)
		{
			return new CollectionAlternateArchiveCandidate(Request, Artifact, Reference, Md5, choices,
				PreviousNexusManifestSource, PreviousNexusChoices);
		}
		/// <summary>
		/// Carries already resolved Nexus prefer choices through a user-approved EXTERNAL archive change.
		/// These are same-manifest read-only decisions, not permission to install or bypass archive verification.
		/// </summary>
		internal CollectionAlternateArchiveCandidate WithPreviouslyResolvedNexusPolicies(
			CollectionManifestSourceSnapshot source, IEnumerable<CollectionResolvedArtifactChoice> choices)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (choices == null) throw new ArgumentNullException(nameof(choices));
			return new CollectionAlternateArchiveCandidate(Request, Artifact, Reference, Md5,
				PreviousAlternatives, source, choices);
		}
		internal IReadOnlyList<CollectionResolvedArtifactChoice> PreviousAlternatives { get; }
		internal CollectionManifestSourceSnapshot PreviousNexusManifestSource { get; }
		internal IReadOnlyList<CollectionResolvedArtifactChoice> PreviousNexusChoices { get; }
		public CollectionAcquisitionRequest Request { get; }
		public CollectionArtifactReference SelectedArtifact { get; }
		public long ByteLength { get { return Artifact.ByteLength; } }
		public string Md5 { get; }
		public string Sha256 { get { return Artifact.ContentHash.Value; } }
		internal CollectionsRetainedArtifact Artifact { get; }
		internal CollectionsRetainedArtifactReferenceRecord Reference { get; }
	}

	/// <summary>
	/// Coordinates free/manual Collection acquisition without automating provider web interactions.
	/// </summary>
	/// <remarks>
	/// Browser/NXM/local-file inputs remain pending until exact archive bytes have been verified through C4.18. Signed
	/// NXM authorization and local paths are consumed transiently and are never copied into durable Collection identity.
	/// </remarks>
	public sealed class CollectionManualAcquisitionCoordinator
	{
		private readonly CollectionAcquisitionRequestCoordinator _requestCoordinator;
		private readonly CollectionVerifiedArchiveAdopter _archiveAdopter;
		private readonly ICollectionManualAcquisitionHintProvider _hintProvider;

		/// <summary>
		/// Creates a manual acquisition coordinator over the existing AddMod request seam and verified archive adopter.
		/// </summary>
		public CollectionManualAcquisitionCoordinator(
			CollectionAcquisitionRequestCoordinator requestCoordinator,
			CollectionVerifiedArchiveAdopter archiveAdopter)
			: this(requestCoordinator, archiveAdopter, null)
		{
		}

		/// <summary>Creates a manual coordinator with retained-source hint reconstruction for characterized external artifacts.</summary>
		public CollectionManualAcquisitionCoordinator(
			CollectionAcquisitionRequestCoordinator requestCoordinator,
			CollectionVerifiedArchiveAdopter archiveAdopter,
			ICollectionManualAcquisitionHintProvider hintProvider)
		{
			_requestCoordinator = requestCoordinator ?? throw new ArgumentNullException(nameof(requestCoordinator));
			_archiveAdopter = archiveAdopter ?? throw new ArgumentNullException(nameof(archiveAdopter));
			_hintProvider = hintProvider;
		}

		/// <summary>
		/// Creates the safe user-mediated actions currently available for the exact unresolved acquisition request.
		/// </summary>
		public CollectionManualAcquisitionPendingAction CreatePendingAction(CollectionAcquisitionRequest request)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));

			_requestCoordinator.TrackPending(request, CollectionAcquisitionPersistenceMode.Manual);

			string gameDomain;
			long modId;
			long fileId;
			if (NexusCollectionModFileArtifactIdentity.TryParse(request.SelectedArtifact, out gameDomain, out modId, out fileId) &&
				Uri.CheckHostName(gameDomain) != UriHostNameType.Unknown)
			{
				Uri browserUri = NexusModLinkParser.CreateModUri(
					gameDomain,
					modId.ToString(CultureInfo.InvariantCulture),
					fileId.ToString(CultureInfo.InvariantCulture));
				if (browserUri == null)
					throw new InvalidOperationException("The exact Nexus artifact cannot be represented as a safe manual-download page.");

				return new CollectionManualAcquisitionPendingAction(
					request,
					CollectionManualAcquisitionActionKind.Browser |
					CollectionManualAcquisitionActionKind.Nxm |
					CollectionManualAcquisitionActionKind.LocalFile,
					browserUri);
			}

			string externalMd5;
			long externalByteLength;
			if (CollectionExternalArtifactIdentity.TryParse(request.SelectedArtifact, out externalMd5, out externalByteLength))
			{
				CollectionManualAcquisitionHint hint = _hintProvider == null ? null : _hintProvider.GetHint(request);
				CollectionManualAcquisitionActionKind actions = CollectionManualAcquisitionActionKind.LocalFile;
				if (hint != null && hint.BrowserUri != null)
					actions |= CollectionManualAcquisitionActionKind.Browser;
				return new CollectionManualAcquisitionPendingAction(request, actions, hint == null ? null : hint.BrowserUri,
					hint == null ? null : hint.Instructions);
			}

			// Other non-Nexus manual imports are safe only when the plan itself carries exact cryptographic content identity.
			if (request.SelectedArtifact.ExpectedContentHash != null)
			{
				return new CollectionManualAcquisitionPendingAction(
					request,
					CollectionManualAcquisitionActionKind.LocalFile,
					null);
			}

			throw new InvalidOperationException("The requested artifact has no safe manual verification path. A provider identity or expected content hash is required.");
		}

		/// <summary>
		/// Queues a user-returned Nexus NXM callback through the existing AddMod pipeline.
		/// </summary>
		/// <remarks>
		/// Manual NXM return requires Nexus' temporary key/expiry/user tuple. Unsigned exact-file NXM acquisition belongs to
		/// the Premium path in C4.19 and is not accepted here as evidence of user-mediated authorization.
		/// </remarks>
		public CollectionAcquisitionQueueCorrelation QueueReturnedNxm(
			CollectionManualAcquisitionPendingAction pendingAction,
			Uri returnedNxmUri,
			ConfirmOverwriteCallback confirmOverwriteCallback)
		{
			return QueueReturnedNxm(pendingAction, returnedNxmUri, confirmOverwriteCallback, CollectionArchiveOverwritePolicy.Prompt);
		}

		/// <summary>Queues a user-returned Nexus NXM callback with explicit in-memory archive-overwrite consent.</summary>
		public CollectionAcquisitionQueueCorrelation QueueReturnedNxm(
			CollectionManualAcquisitionPendingAction pendingAction,
			Uri returnedNxmUri,
			ConfirmOverwriteCallback confirmOverwriteCallback,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy)
		{
			ValidatePendingAction(pendingAction, CollectionManualAcquisitionActionKind.Nxm);
			if (returnedNxmUri == null)
				throw new ArgumentNullException(nameof(returnedNxmUri));
			if (archiveOverwritePolicy == null)
				throw new ArgumentNullException(nameof(archiveOverwritePolicy));
			if (!returnedNxmUri.IsAbsoluteUri || !StringComparer.OrdinalIgnoreCase.Equals(returnedNxmUri.Scheme, "nxm"))
				throw new ArgumentException("A Nexus NXM callback is required.", nameof(returnedNxmUri));

			NexusUrl nexusUrl = new NexusUrl(returnedNxmUri);
			if (String.IsNullOrWhiteSpace(nexusUrl.Key) || nexusUrl.Expiry <= 0 || nexusUrl.UserId <= 0)
				throw new ArgumentException("The returned NXM URI does not contain Nexus' temporary user-mediated download authorization.", nameof(returnedNxmUri));

			return _requestCoordinator.Queue(pendingAction.Request, returnedNxmUri, confirmOverwriteCallback, archiveOverwritePolicy);
		}

		/// <summary>
		/// Verifies the archive registered by a completed user-mediated NXM/AddMod operation.
		/// </summary>
		/// <returns>The verified immutable archive, or <c>null</c> when the returned bytes cannot satisfy the request.</returns>
		public CollectionVerifiedArchive VerifyReturnedNxm(
			CollectionManualAcquisitionPendingAction pendingAction,
			CollectionAcquisitionQueueCorrelation correlation,
			CancellationToken cancellationToken)
		{
			ValidatePendingAction(pendingAction, CollectionManualAcquisitionActionKind.Nxm);
			ValidateCorrelation(pendingAction, correlation);
			if (correlation.Task.Status != TaskStatus.Complete)
				throw new InvalidOperationException("The user-mediated AddMod operation must complete before its resulting archive can be verified.");

			return _archiveAdopter.TryAdopt(pendingAction.Request, cancellationToken);
		}

		/// <summary>
		/// Verifies the archive registered by a completed user-mediated NXM/AddMod operation without cancellation.
		/// </summary>
		public CollectionVerifiedArchive VerifyReturnedNxm(
			CollectionManualAcquisitionPendingAction pendingAction,
			CollectionAcquisitionQueueCorrelation correlation)
		{
			return VerifyReturnedNxm(pendingAction, correlation, CancellationToken.None);
		}

		/// <summary>
		/// Verifies a user-selected local archive candidate for the pending acquisition request.
		/// </summary>
		/// <returns>The verified immutable archive, or <c>null</c> when the selected bytes do not match.</returns>
		public CollectionVerifiedArchive VerifyLocalFile(
			CollectionManualAcquisitionPendingAction pendingAction,
			string localFilePath,
			CancellationToken cancellationToken)
		{
			ValidatePendingAction(pendingAction, CollectionManualAcquisitionActionKind.LocalFile);
			if (String.IsNullOrWhiteSpace(localFilePath))
				throw new ArgumentException("A local archive path is required.", nameof(localFilePath));

			return _archiveAdopter.TryAdoptFile(pendingAction.Request, localFilePath, cancellationToken);
		}

		/// <summary>
		/// Verifies a user-selected local archive candidate without cancellation.
		/// </summary>
		public CollectionVerifiedArchive VerifyLocalFile(
			CollectionManualAcquisitionPendingAction pendingAction,
			string localFilePath)
		{
			return VerifyLocalFile(pendingAction, localFilePath, CancellationToken.None);
		}

		/// <summary>Seals and inspects a different external archive for an explicit user decision; does not accept it.</summary>
		public CollectionAlternateArchiveCandidate InspectAlternateLocalFile(
			CollectionManualAcquisitionPendingAction pendingAction, string localFilePath, CancellationToken cancellationToken)
		{
			ValidatePendingAction(pendingAction, CollectionManualAcquisitionActionKind.LocalFile);
			string md5; long length;
			if (!CollectionExternalArtifactIdentity.TryParse(pendingAction.Request.SelectedArtifact, out md5, out length))
				throw new NotSupportedException("Only manual/browse external archive sources can use a user-approved alternative.");
			return _archiveAdopter.InspectAlternateExternalFile(pendingAction.Request, localFilePath, cancellationToken);
		}

		/// <summary>Releases a proposed alternative once declined or a new acquisition request protects it.</summary>
		public void ReleaseAlternateLocalFile(CollectionAlternateArchiveCandidate candidate)
		{
			_archiveAdopter.ReleaseAlternateExternalFile(candidate);
		}

		private static void ValidatePendingAction(
			CollectionManualAcquisitionPendingAction pendingAction,
			CollectionManualAcquisitionActionKind requiredAction)
		{
			if (pendingAction == null)
				throw new ArgumentNullException(nameof(pendingAction));
			if (!pendingAction.Supports(requiredAction))
				throw new InvalidOperationException("The requested manual acquisition action is not available for this artifact.");
		}

		private static void ValidateCorrelation(
			CollectionManualAcquisitionPendingAction pendingAction,
			CollectionAcquisitionQueueCorrelation correlation)
		{
			if (correlation == null)
				throw new ArgumentNullException(nameof(correlation));
			if (correlation.Request.RequestId != pendingAction.Request.RequestId ||
				!correlation.Request.SelectedArtifact.Equals(pendingAction.Request.SelectedArtifact))
			{
				throw new ArgumentException("The AddMod correlation does not belong to this pending acquisition request.", nameof(correlation));
			}
		}
	}
}
