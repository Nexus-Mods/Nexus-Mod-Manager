using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>One installed Collection association exposed by the basic management application route.</summary>
	public sealed class CollectionManagementAssociation
	{
		internal CollectionManagementAssociation(CollectionTargetAssociation association, string displayName, string revisionLabel)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			DisplayName = String.IsNullOrWhiteSpace(displayName) ? association.Revision.Collection.StableId : displayName;
			RevisionLabel = String.IsNullOrWhiteSpace(revisionLabel) ? FormatRevision(association.Revision) : revisionLabel;
		}

		public CollectionTargetAssociation Association { get; }
		public Guid AssociationId { get { return Association.AssociationId; } }
		public string DisplayName { get; }
		public string RevisionLabel { get; }
		public CollectionAssociationState State { get { return Association.State; } }

		public override string ToString()
		{
			return DisplayName + " - " + RevisionLabel + " [" + State + "]";
		}

		private static string FormatRevision(CollectionRevisionIdentity revision)
		{
			if (revision.NexusRevisionNumber.HasValue)
				return "Revision " + revision.NexusRevisionNumber.Value;
			return "Local revision " + revision.StableRevisionId;
		}
	}

	/// <summary>
	/// Application-level route for basic installed-Collection management over the existing C6.13/C6.14 coordinators.
	/// </summary>
	/// <remarks>
	/// This service exposes basic installed-association management plus bounded startup reconciliation for its owned management/restore routes.
	/// Native removal semantics remain owned by <see cref="CollectionUninstallEffectsCoordinator"/> and Local restore resume remains
	/// owned by <see cref="CollectionLocalRestoreMemberResumeCoordinator"/>.
	/// </remarks>
	public sealed class CollectionManagementApplicationService
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionUninstallEffectsCoordinator _uninstallCoordinator;
		private readonly CollectionLocalRestoreMemberResumeCoordinator _localRestoreResumeCoordinator;

		/// <summary>Creates the production management route for the active game/storage target.</summary>
		public CollectionManagementApplicationService(ServiceManager services, GameStorageService gameStorageService)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			if (_services.ModManager == null)
				throw new InvalidOperationException("Collection management requires the active native ModManager.");

			var store = new CollectionsStore(GetTargetPaths());
			_catalogStore = new CollectionsCatalogStore(store);
			_associationStore = new CollectionsAssociationStore(store);
			_operationStore = new CollectionsOperationStore(store);
			_uninstallCoordinator = new CollectionUninstallEffectsCoordinator(_services, _gameStorageService,
				_operationStore, _associationStore);
			var localCaptureStore = new CollectionsLocalCaptureStore(store);
			var artifactStore = new CollectionsRetainedArtifactStore(store);
			var referenceStore = new CollectionsRetainedArtifactReferenceStore(store);
			_localRestoreResumeCoordinator = new CollectionLocalRestoreMemberResumeCoordinator(_services, _gameStorageService,
				_operationStore, _associationStore, localCaptureStore, artifactStore, referenceStore);
		}

		/// <summary>Returns installed Collection associations for the current canonical target.</summary>
		public IReadOnlyList<CollectionManagementAssociation> GetAssociations()
		{
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var result = new List<CollectionManagementAssociation>();
			foreach (CollectionTargetAssociation association in _associationStore.GetAssociationsForTarget(target))
			{
				CollectionDefinition definition = _catalogStore.GetDefinition(association.Revision.Collection);
				CollectionRevision revision = _catalogStore.GetRevision(association.Revision);
				result.Add(new CollectionManagementAssociation(association,
					definition == null ? null : definition.DisplayName,
					revision == null ? null : revision.RevisionLabel));
			}
			return new ReadOnlyCollection<CollectionManagementAssociation>(result
				.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(x => x.RevisionLabel, StringComparer.CurrentCultureIgnoreCase).ToList());
		}

		/// <summary>Detaches Collection tracking while preserving every native mod/effect.</summary>
		public CollectionDetachResult Detach(Guid associationId)
		{
			CollectionTargetIdentity target = ResolveCurrentTarget();
			return new CollectionDetachCoordinator(_associationStore, target).Detach(associationId);
		}

		/// <summary>Builds the reviewed safe-effect-removal plan for one installed association.</summary>
		public Task<CollectionUninstallEffectsPlan> PreviewEffectRemovalAsync(Guid associationId, CancellationToken cancellationToken)
		{
			return _uninstallCoordinator.PreviewAsync(associationId, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Executes an explicitly reviewed safe-effect-removal plan after native revalidation.</summary>
		public Task<CollectionUninstallEffectsResult> RemoveEffectsAsync(CollectionUninstallEffectsPlan reviewedPlan,
			CancellationToken cancellationToken)
		{
			return _uninstallCoordinator.ExecuteAsync(reviewedPlan, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Reconciles and resumes only persisted RestoreLocalCapture member phases for the current target.</summary>
		public async Task<IReadOnlyList<CollectionLocalRestoreMemberResumeResult>> ReconcileInterruptedLocalRestoreMembersAsync(
			CancellationToken cancellationToken)
		{
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var results = new List<CollectionLocalRestoreMemberResumeResult>();
			foreach (CollectionOperation operation in _operationStore.GetIncompleteOperations(target)
				.Where(x => x.Kind == CollectionOperationKind.RestoreLocalCapture).ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				results.Add(await _localRestoreResumeCoordinator.ResumeAsync(operation.Identity,
					GetTargetPaths(), cancellationToken).ConfigureAwait(false));
			}
			return new ReadOnlyCollection<CollectionLocalRestoreMemberResumeResult>(results);
		}

		/// <summary>Reconciles only interrupted safe-effect-removal operations for the current target.</summary>
		public async Task<IReadOnlyList<CollectionUninstallEffectsResult>> ReconcileInterruptedEffectRemovalAsync(
			CancellationToken cancellationToken)
		{
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var results = new List<CollectionUninstallEffectsResult>();
			foreach (CollectionOperation operation in _operationStore.GetIncompleteOperations(target)
				.Where(x => x.Kind == CollectionOperationKind.UninstallCollectionEffects).ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				results.Add(await _uninstallCoordinator.ReconcileInterruptedAsync(operation.Identity,
					GetTargetPaths(), cancellationToken).ConfigureAwait(false));
			}
			return new ReadOnlyCollection<CollectionUninstallEffectsResult>(results);
		}

		private GameStoragePathSet GetTargetPaths()
		{
			return _gameStorageService.FromGameMode(_services.ModManager.GameMode);
		}

		private CollectionTargetIdentity ResolveCurrentTarget()
		{
			return new CollectionTargetIdentityResolver(_gameStorageService).Resolve(GetTargetPaths()).Target;
		}
	}
}
