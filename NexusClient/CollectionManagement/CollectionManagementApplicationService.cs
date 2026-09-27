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

	/// <summary>One persisted Local Collection capture available to the active target for explicit restore.</summary>
	public sealed class CollectionManagementLocalCapture
	{
		internal CollectionManagementLocalCapture(LocalCapture capture, string displayName, string revisionLabel)
		{
			Capture = capture ?? throw new ArgumentNullException(nameof(capture));
			DisplayName = String.IsNullOrWhiteSpace(displayName) ? capture.Revision.Collection.StableId : displayName;
			RevisionLabel = String.IsNullOrWhiteSpace(revisionLabel) ? "Local revision " + capture.Revision.StableRevisionId : revisionLabel;
		}

		public LocalCapture Capture { get; }
		public LocalCaptureIdentity CaptureIdentity { get { return Capture.Identity; } }
		public string DisplayName { get; }
		public string RevisionLabel { get; }
		public LocalCaptureCapability Capability { get { return Capture.Capability; } }

		public override string ToString()
		{
			return DisplayName + " - " + RevisionLabel + " [" + Capability + "]";
		}
	}

	/// <summary>
	/// Application-level route for basic installed-Collection management over the existing C6.13/C6.14 coordinators.
	/// </summary>
	/// <remarks>
	/// This service exposes basic installed-association management, saved Local Collection restore, and bounded startup reconciliation.
	/// Native removal semantics remain owned by <see cref="CollectionUninstallEffectsCoordinator"/>; complete Local restore sequencing
	/// remains owned by <see cref="CollectionLocalRestoreApplicationService"/>.
	/// </remarks>
	public sealed class CollectionManagementApplicationService
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionUninstallEffectsCoordinator _uninstallCoordinator;
		private readonly CollectionsLocalCaptureStore _localCaptureStore;
		private readonly CollectionLocalRestoreMemberResumeCoordinator _localRestoreResumeCoordinator;
		private readonly CollectionLocalRestoreApplicationService _localRestoreWorkflow;

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
			_localCaptureStore = new CollectionsLocalCaptureStore(store);
			var artifactStore = new CollectionsRetainedArtifactStore(store);
			var referenceStore = new CollectionsRetainedArtifactReferenceStore(store);
			_localRestoreResumeCoordinator = new CollectionLocalRestoreMemberResumeCoordinator(_services, _gameStorageService,
				_operationStore, _associationStore, _localCaptureStore, artifactStore, referenceStore);
			_localRestoreWorkflow = new CollectionLocalRestoreApplicationService(_services, _gameStorageService, _operationStore,
				_associationStore, _localCaptureStore, artifactStore, referenceStore);
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

		/// <summary>Returns persisted Local Collection captures belonging to the current canonical target.</summary>
		public IReadOnlyList<CollectionManagementLocalCapture> GetLocalCaptures()
		{
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var result = new List<CollectionManagementLocalCapture>();
			foreach (LocalCapture capture in _localCaptureStore.GetCapturesForTarget(target))
			{
				CollectionDefinition definition = _catalogStore.GetDefinition(capture.Revision.Collection);
				CollectionRevision revision = _catalogStore.GetRevision(capture.Revision);
				result.Add(new CollectionManagementLocalCapture(capture, definition == null ? null : definition.DisplayName,
					revision == null ? null : revision.RevisionLabel));
			}
			return new ReadOnlyCollection<CollectionManagementLocalCapture>(result
				.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(x => x.RevisionLabel, StringComparer.CurrentCultureIgnoreCase).ToList());
		}

		/// <summary>Builds the explicit reviewed C7.9 Local restore plan for one saved capture.</summary>
		public Task<CollectionLocalRestorePreview> PreviewLocalRestoreAsync(LocalCaptureIdentity captureIdentity,
			CancellationToken cancellationToken)
		{
			return _localRestoreWorkflow.PreviewAsync(captureIdentity, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Applies one exact user-reviewed Local restore through complete C7 final verification.</summary>
		public Task<CollectionLocalRestoreWorkflowResult> RestoreLocalCaptureAsync(CollectionLocalRestorePreview preview,
			CancellationToken cancellationToken)
		{
			return _localRestoreWorkflow.ApplyAsync(preview, GetTargetPaths(), cancellationToken);
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

		/// <summary>Reconciles persisted Local restore operations through every remaining C7 phase and aggregate final verification.</summary>
		public async Task<IReadOnlyList<CollectionLocalRestoreWorkflowResult>> ReconcileInterruptedLocalRestoresAsync(
			CancellationToken cancellationToken)
		{
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var results = new List<CollectionLocalRestoreWorkflowResult>();
			foreach (CollectionOperation operation in _operationStore.GetIncompleteOperations(target)
				.Where(x => x.Kind == CollectionOperationKind.RestoreLocalCapture).ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionLocalRestoreWorkflowResult result = await _localRestoreWorkflow
					.ResumeAsync(operation.Identity, GetTargetPaths(), cancellationToken).ConfigureAwait(false);
				results.Add(result);
				if (!result.IsSuccessful)
					break;
			}
			return new ReadOnlyCollection<CollectionLocalRestoreWorkflowResult>(results);
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
