using System;
using System.Globalization;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;

namespace Nexus.Client.CollectionManagement
{
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

		/// <summary>
		/// Creates a manual acquisition coordinator over the existing AddMod request seam and verified archive adopter.
		/// </summary>
		public CollectionManualAcquisitionCoordinator(
			CollectionAcquisitionRequestCoordinator requestCoordinator,
			CollectionVerifiedArchiveAdopter archiveAdopter)
		{
			_requestCoordinator = requestCoordinator ?? throw new ArgumentNullException(nameof(requestCoordinator));
			_archiveAdopter = archiveAdopter ?? throw new ArgumentNullException(nameof(archiveAdopter));
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

			// Non-Nexus manual imports are safe only when the plan itself carries exact cryptographic content identity.
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
			ValidatePendingAction(pendingAction, CollectionManualAcquisitionActionKind.Nxm);
			if (returnedNxmUri == null)
				throw new ArgumentNullException(nameof(returnedNxmUri));
			if (!returnedNxmUri.IsAbsoluteUri || !StringComparer.OrdinalIgnoreCase.Equals(returnedNxmUri.Scheme, "nxm"))
				throw new ArgumentException("A Nexus NXM callback is required.", nameof(returnedNxmUri));

			NexusUrl nexusUrl = new NexusUrl(returnedNxmUri);
			if (String.IsNullOrWhiteSpace(nexusUrl.Key) || nexusUrl.Expiry <= 0 || nexusUrl.UserId <= 0)
				throw new ArgumentException("The returned NXM URI does not contain Nexus' temporary user-mediated download authorization.", nameof(returnedNxmUri));

			return _requestCoordinator.Queue(pendingAction.Request, returnedNxmUri, confirmOverwriteCallback);
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
