using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.OnlineServices.NexusMods.Collections;

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
	/// Read-only durable presentation data for one installed Collection association.
	/// </summary>
	/// <remarks>
	/// The installed association remains authoritative for applied state. The retained manifest is re-normalized only to
	/// rebuild the read-only Collection details/member list after restart; it does not create a new preparation or mutation.
	/// </remarks>
	public sealed class CollectionManagementAssociationPresentation
	{
		internal CollectionManagementAssociationPresentation(CollectionManagementAssociation association,
			CollectionDefinition definition, CollectionRevision revision, NexusCollectionBundleImportResult retainedManifest,
			IEnumerable<CollectionMemberKey> boundMemberKeys, string nexusGameDomain, string retainedSourceIssue)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			Definition = definition;
			Revision = revision;
			RetainedManifest = retainedManifest;
			BoundMemberKeys = new ReadOnlyCollection<CollectionMemberKey>((boundMemberKeys ?? Enumerable.Empty<CollectionMemberKey>()).ToList());
			NexusGameDomain = String.IsNullOrWhiteSpace(nexusGameDomain) ? null : nexusGameDomain.Trim().ToLowerInvariant();
			RetainedSourceIssue = retainedSourceIssue;
		}

		public CollectionManagementAssociation Association { get; }
		public CollectionDefinition Definition { get; }
		public CollectionRevision Revision { get; }
		public NexusCollectionBundleImportResult RetainedManifest { get; }
		public IReadOnlyList<CollectionMemberKey> BoundMemberKeys { get; }
		public string NexusGameDomain { get; }
		public string RetainedSourceIssue { get; }
		public bool HasRetainedManifest { get { return RetainedManifest != null; } }
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
		private readonly CollectionsStore _store;
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
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

			_store = new CollectionsStore(GetTargetPaths());
			_catalogStore = new CollectionsCatalogStore(_store);
			_associationStore = new CollectionsAssociationStore(_store);
			_operationStore = new CollectionsOperationStore(_store);
			_revisionSourceStore = new CollectionsRevisionSourceStore(_store);
			_uninstallCoordinator = new CollectionUninstallEffectsCoordinator(_services, _gameStorageService,
				_operationStore, _associationStore);
			_localCaptureStore = new CollectionsLocalCaptureStore(_store);
			var artifactStore = new CollectionsRetainedArtifactStore(_store);
			var referenceStore = new CollectionsRetainedArtifactReferenceStore(_store);
			_localRestoreResumeCoordinator = new CollectionLocalRestoreMemberResumeCoordinator(_services, _gameStorageService,
				_operationStore, _associationStore, _localCaptureStore, artifactStore, referenceStore);
			_localRestoreWorkflow = new CollectionLocalRestoreApplicationService(_services, _gameStorageService, _operationStore,
				_associationStore, _localCaptureStore, artifactStore, referenceStore);
		}

		/// <summary>Returns installed Collection associations for the current canonical target.</summary>
		public IReadOnlyList<CollectionManagementAssociation> GetAssociations()
		{
			if (!_store.Exists)
				return new CollectionManagementAssociation[0];
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

		/// <summary>
		/// Rehydrates one installed Collection association for read-only presentation from durable catalog/retained source data.
		/// </summary>
		/// <remarks>
		/// This method performs no provider/network access and no native mutation. Failure to reload the retained manifest is
		/// returned as a presentation issue so the durable association can still be displayed and managed.
		/// </remarks>
		public CollectionManagementAssociationPresentation GetAssociationPresentation(Guid associationId)
		{
			if (associationId == Guid.Empty)
				throw new ArgumentException("A non-empty association identifier is required.", nameof(associationId));
			if (!_store.Exists)
				return null;

			CollectionTargetIdentity target = ResolveCurrentTarget();
			CollectionTargetAssociation association = _associationStore.GetAssociationsForTarget(target)
				.FirstOrDefault(x => x.AssociationId == associationId);
			if (association == null)
				return null;

			CollectionDefinition definition = _catalogStore.GetDefinition(association.Revision.Collection);
			CollectionRevision revision = _catalogStore.GetRevision(association.Revision);
			var managedAssociation = new CollectionManagementAssociation(association,
				definition == null ? null : definition.DisplayName,
				revision == null ? null : revision.RevisionLabel);

			NexusCollectionBundleImportResult retainedManifest = null;
			string retainedSourceIssue = null;
			if (revision == null)
			{
				retainedSourceIssue = "The installed Collection revision metadata is missing from the durable Collections catalog.";
			}
			else
			{
				try
				{
					CollectionRevisionSourceRecord source = _revisionSourceStore.GetSource(revision.Identity);
					if (source == null)
					{
						retainedSourceIssue = "The exact retained Collection manifest is not available for this installed revision.";
					}
					else
					{
						byte[] rawManifest = _revisionSourceStore.LoadManifest(revision.Identity, source.ManifestSource);
						NexusCollectionManifestNormalizationResult normalized = new NexusCollectionManifestNormalizer().Normalize(rawManifest, revision);
						retainedManifest = new NexusCollectionBundleImportResult(
							ToBundleInputKind(source.InputKind), source.BundleContentHash, source.BundleByteLength,
							source.ManifestEntryName, normalized);
					}
				}
				catch (Exception ex)
				{
					retainedSourceIssue = ex.Message;
				}
			}

			IReadOnlyList<CollectionMemberBinding> bindings = _associationStore.GetBindings(associationId);
			string gameDomain = _services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName;
			return new CollectionManagementAssociationPresentation(managedAssociation, definition, revision, retainedManifest,
				bindings.Select(x => x.MemberKey), gameDomain, retainedSourceIssue);
		}

		/// <summary>Returns persisted Local Collection captures belonging to the current canonical target.</summary>
		public IReadOnlyList<CollectionManagementLocalCapture> GetLocalCaptures()
		{
			if (!_store.Exists)
				return new CollectionManagementLocalCapture[0];
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
			return RemoveEffectsAsync(reviewedPlan, cancellationToken, null);
		}

		/// <summary>Executes reviewed removal and cleans obsolete retention while preserving an open incoming Collection preview.</summary>
		public async Task<CollectionUninstallEffectsResult> RemoveEffectsAsync(CollectionUninstallEffectsPlan reviewedPlan,
			CancellationToken cancellationToken, CollectionIdentity retainedPreviewCollection)
		{
			CollectionUninstallEffectsResult result = await _uninstallCoordinator.ExecuteAsync(reviewedPlan,
				GetTargetPaths(), cancellationToken).ConfigureAwait(false);
			if (result.IsSuccessful)
				await CleanupRetainedContentAsync(CancellationToken.None, retainedPreviewCollection).ConfigureAwait(false);
			return result;
		}

		/// <summary>
		/// Releases obsolete removal retention and retries unreferenced Collections content cleanup at an idle target boundary.
		/// </summary>
		/// <remarks>
		/// Cleanup failures never turn a committed removal into a failed uninstall. Locked blobs retain durable tombstones for
		/// the next startup retry. The normal native archive library and live installed effects are outside this cleanup route.
		/// </remarks>
		public async Task CleanupRetainedContentAsync(CancellationToken cancellationToken, CollectionIdentity retainedPreviewCollection = null)
		{
			if (!_store.Exists)
				return;
			try
			{
				CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(GetTargetPaths());
				using (CollectionTargetMutationLease lease = await CollectionTargetMutationLeaseManager.Shared
					.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
				{
					await Task.Run(() =>
					{
						var cleanup = new CollectionsRetainedArtifactCleanupStore(_store);
						if (!cleanup.ReleaseRemovedCollectionContent(authority.Target, retainedPreviewCollection))
							return;
						var attempted = new HashSet<string>(StringComparer.Ordinal);
						foreach (string artifactId in cleanup.GetPendingTombstones(Int32.MaxValue))
							CollectRetainedArtifact(cleanup, artifactId, attempted, cancellationToken);
						while (true)
						{
							cancellationToken.ThrowIfCancellationRequested();
							List<string> candidates = cleanup.GetCleanupCandidates(128).Where(x => !attempted.Contains(x)).ToList();
							if (candidates.Count == 0)
								break;
							foreach (string artifactId in candidates)
								CollectRetainedArtifact(cleanup, artifactId, attempted, cancellationToken);
						}
					}, cancellationToken).ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Trace.TraceWarning("Collection retained-content cleanup deferred: " + ex);
			}
		}

		/// <summary>Collects one tracked blob once per sweep while leaving failed physical deletions available for retry.</summary>
		private static void CollectRetainedArtifact(CollectionsRetainedArtifactCleanupStore cleanup, string artifactId,
			HashSet<string> attempted, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!attempted.Add(artifactId))
				return;
			try
			{
				cleanup.TryCollectUnreferencedArtifact(artifactId);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Trace.TraceWarning("Collection retained artifact cleanup deferred for " + artifactId + ": " + ex);
			}
		}

		/// <summary>
		/// Repairs missing Nexus metadata for already-applied Collection members without changing installed effects or Collection provenance.
		/// </summary>
		public int RefreshAppliedNexusMetadata()
		{
			if (!_store.Exists)
				return 0;
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var bindings = new List<CollectionMemberBinding>();
			foreach (CollectionTargetAssociation association in _associationStore.GetAssociationsForTarget(target)
				.Where(x => x.State == CollectionAssociationState.Applied))
			{
				bindings.AddRange(_associationStore.GetBindings(association.AssociationId));
			}
			return new CollectionNexusMetadataHydrator(_services.ModManager).Enrich(bindings);
		}

		/// <summary>Reconciles persisted Local restore operations through every remaining C7 phase and aggregate final verification.</summary>
		public async Task<IReadOnlyList<CollectionLocalRestoreWorkflowResult>> ReconcileInterruptedLocalRestoresAsync(
			CancellationToken cancellationToken)
		{
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				return new CollectionLocalRestoreWorkflowResult[0];
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
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				return new CollectionLocalRestoreMemberResumeResult[0];
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
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				return new CollectionUninstallEffectsResult[0];
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

		private static NexusCollectionBundleInputKind ToBundleInputKind(CollectionRevisionSourceInputKind inputKind)
		{
			switch (inputKind)
			{
				case CollectionRevisionSourceInputKind.RawManifest:
					return NexusCollectionBundleInputKind.RawManifest;
				case CollectionRevisionSourceInputKind.Archive:
					return NexusCollectionBundleInputKind.Archive;
				default:
					throw new InvalidOperationException("The retained Collection revision has an unsupported source kind.");
			}
		}
	}
}
