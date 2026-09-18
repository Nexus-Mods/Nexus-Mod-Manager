using System;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Reconciles durable Collection acquisition intent with immutable verified inputs and native AddMod restart state.
	/// </summary>
	/// <remarks>
	/// Previous-session signed NXM authorization is never replayed. Premium work is resumed only by re-entering C4.19 so
	/// the current account/session resolves fresh authorized links. Manual work requires fresh user mediation after restart.
	/// </remarks>
	public sealed class CollectionAcquisitionRestartCoordinator
	{
		private readonly CollectionsAcquisitionStore _store;
		private readonly ICollectionPersistedAddModStateSource _nativeState;
		private readonly CollectionVerifiedArchiveAdopter _archiveAdopter;
		private readonly CollectionPremiumAcquisitionCoordinator _premiumCoordinator;

		/// <summary>Creates a restart reconciler over the C4 acquisition primitives.</summary>
		public CollectionAcquisitionRestartCoordinator(CollectionsAcquisitionStore store,
			ICollectionPersistedAddModStateSource nativeState, CollectionVerifiedArchiveAdopter archiveAdopter,
			CollectionPremiumAcquisitionCoordinator premiumCoordinator)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
			_nativeState = nativeState ?? throw new ArgumentNullException(nameof(nativeState));
			_archiveAdopter = archiveAdopter ?? throw new ArgumentNullException(nameof(archiveAdopter));
			_premiumCoordinator = premiumCoordinator ?? throw new ArgumentNullException(nameof(premiumCoordinator));
		}

		/// <summary>
		/// Reconciles one immutable request after restart without automatically starting network work or installation.
		/// </summary>
		public CollectionAcquisitionRestartResult Reconcile(CollectionAcquisitionRequest request, CancellationToken cancellationToken)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			CollectionAcquisitionRecord record = _store.Get(request.RequestId);
			if (record == null)
				return Result(CollectionAcquisitionRestartDisposition.NotTracked, null, null, null, null);
			if (!record.Matches(request))
				throw new InvalidOperationException("The persisted acquisition request does not match the immutable request reconstructed from the resolved Collection plan.");

			CollectionVerifiedArchive verified = _archiveAdopter.TryAdopt(request, cancellationToken);
			if (verified != null)
			{
				_store.MarkVerified(request, verified.Artifact.ArtifactId);
				return Result(CollectionAcquisitionRestartDisposition.VerifiedInputReady, record, verified, null, null);
			}
			if (record.State == CollectionAcquisitionPersistenceState.Verified)
				return Result(CollectionAcquisitionRestartDisposition.VerifiedContentMissing, record, null, null, null);
			if (record.State == CollectionAcquisitionPersistenceState.Cancelled || record.State == CollectionAcquisitionPersistenceState.Failed)
				return Result(CollectionAcquisitionRestartDisposition.ReacquisitionRequired, record, null, null, null);

			CollectionPersistedAddModState persisted = record.QueueOperationId.HasValue
				? _nativeState.Find(record.QueueOperationId.Value)
				: null;
			if (persisted != null && !MatchesArtifact(persisted, request.SelectedArtifact))
				return Result(CollectionAcquisitionRestartDisposition.NativeStateMismatch, record, null, persisted, null);

			if (persisted != null && IsNonResumableTerminalState(persisted.Status))
				return Result(CollectionAcquisitionRestartDisposition.ReacquisitionRequired, record, null, persisted, null);

			if (persisted != null && persisted.HasTemporaryAuthorization)
			{
				bool expired = persisted.AuthorizationExpiresUtc.HasValue && persisted.AuthorizationExpiresUtc.Value <= DateTime.UtcNow;
				return Result(expired
					? CollectionAcquisitionRestartDisposition.ManualAuthorizationExpired
					: CollectionAcquisitionRestartDisposition.ManualAuthorizationRefreshRequired,
					record, null, persisted, null);
			}

			if (record.Mode == CollectionAcquisitionPersistenceMode.PremiumNxm ||
				(persisted != null && persisted.IsNexusSource && !persisted.HasTemporaryAuthorization))
			{
				CollectionPremiumAcquisitionAvailability availability = _premiumCoordinator.GetAvailability(request);
				return Result(availability == CollectionPremiumAcquisitionAvailability.Available
					? CollectionAcquisitionRestartDisposition.PremiumResumeReady
					: CollectionAcquisitionRestartDisposition.PremiumUnavailable,
					record, null, persisted, availability);
			}

			if (record.Mode == CollectionAcquisitionPersistenceMode.Manual)
				return Result(CollectionAcquisitionRestartDisposition.ManualInputRequired, record, null, persisted, null);

			return Result(CollectionAcquisitionRestartDisposition.ReacquisitionRequired, record, null, persisted, null);
		}

		/// <summary>Reconciles one request without cancellation.</summary>
		public CollectionAcquisitionRestartResult Reconcile(CollectionAcquisitionRequest request)
		{
			return Reconcile(request, CancellationToken.None);
		}

		private static bool IsNonResumableTerminalState(Nexus.Client.BackgroundTasks.TaskStatus status)
		{
			return status == Nexus.Client.BackgroundTasks.TaskStatus.Complete ||
				status == Nexus.Client.BackgroundTasks.TaskStatus.Cancelled ||
				status == Nexus.Client.BackgroundTasks.TaskStatus.Cancelling ||
				status == Nexus.Client.BackgroundTasks.TaskStatus.Error;
		}

		private static bool MatchesArtifact(CollectionPersistedAddModState persisted, CollectionArtifactReference artifact)
		{
			if (!persisted.IsNexusSource)
				return false;
			string gameDomain;
			long modId;
			long fileId;
			return NexusCollectionModFileArtifactIdentity.TryParse(artifact, out gameDomain, out modId, out fileId) &&
				StringComparer.OrdinalIgnoreCase.Equals(gameDomain, persisted.GameDomain) &&
				modId == persisted.ModId && fileId == persisted.FileId;
		}

		private static CollectionAcquisitionRestartResult Result(CollectionAcquisitionRestartDisposition disposition,
			CollectionAcquisitionRecord record, CollectionVerifiedArchive verifiedArchive,
			CollectionPersistedAddModState nativeState, CollectionPremiumAcquisitionAvailability? premiumAvailability)
		{
			return new CollectionAcquisitionRestartResult(disposition, record, verifiedArchive, nativeState, premiumAvailability);
		}
	}
}
