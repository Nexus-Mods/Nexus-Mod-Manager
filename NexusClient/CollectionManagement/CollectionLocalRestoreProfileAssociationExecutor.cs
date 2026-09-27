using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Result of C7.11 profile preservation and outgoing Collection-association reconciliation.</summary>
	public sealed class CollectionLocalRestoreProfileAssociationExecutionResult
	{
		internal CollectionLocalRestoreProfileAssociationExecutionResult(CollectionOperation operation, int outgoingAssociationCount,
			bool hadOutgoingProfile)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			OutgoingAssociationCount = outgoingAssociationCount;
			HadOutgoingProfile = hadOutgoingProfile;
		}

		public CollectionOperation Operation { get; }
		public int OutgoingAssociationCount { get; }
		public bool HadOutgoingProfile { get; }
		public bool IsReadyForFinalVerification
		{
			get { return Operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary && !Operation.HasUnreconciledNativeChild; }
		}
	}

	/// <summary>
	/// C7.11 final profile/association phase. The outgoing profile remains detached/preserved and superseded associations
	/// are explicitly left Incomplete instead of falsely claiming their previous revision is still Applied.
	/// </summary>
	public sealed class CollectionLocalRestoreProfileAssociationExecutor
	{
		private const string CompleteFormat = "nmm-ce.collections.local-restore-profile-association-phase/1";
		private const string CompleteRole = "local-restore-profile-association-phase-complete-v1";

		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;
		private readonly CollectionLocalRestoreProfileBoundaryCoordinator _boundaryCoordinator;

		/// <summary>Creates the production C7.11 profile/association reconciliation executor.</summary>
		public CollectionLocalRestoreProfileAssociationExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, gameStorageService, operationStore, associationStore, artifactStore, referenceStore,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionLocalRestoreProfileAssociationExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore,
			CollectionTargetMutationLeaseManager mutationLeaseManager, CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
			_boundaryCoordinator = new CollectionLocalRestoreProfileBoundaryCoordinator(_services, _operationStore,
				_associationStore, _artifactStore, _referenceStore);
		}

		/// <summary>Verifies profile preservation and finalizes outgoing associations after logical user metadata restoration.</summary>
		public Task<CollectionLocalRestoreProfileAssociationExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreUserMetadataExecutionResult userMetadataPhase,
			GameStoragePathSet paths)
		{
			return ExecuteAsync(sealedCapture, reviewedPlan, userMetadataPhase, paths, CancellationToken.None);
		}

		/// <summary>Runs C7.11 with cooperative cancellation while holding the canonical target mutation lease.</summary>
		public async Task<CollectionLocalRestoreProfileAssociationExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreUserMetadataExecutionResult userMetadataPhase,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			ValidateInputs(sealedCapture, reviewedPlan, userMetadataPhase, paths);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The live canonical target no longer matches the reviewed Local Collection restore.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				CollectionOperation operation = RequireOperation(userMetadataPhase.Operation.Identity, reviewedPlan);
				try
				{
					CollectionLocalRestoreProfileBoundaryIntent intent = _boundaryCoordinator.EnsurePrepared(operation, sealedCapture, reviewedPlan);
					_boundaryCoordinator.VerifyProfilePreserved(intent);
					string ownerId = operation.Identity.OperationId.ToString("D");
					CollectionsRetainedArtifactReferenceRecord existing = _referenceStore.GetReferenceForOwnerRole(
						CollectionsRetainedArtifactOwnerKind.Operation, ownerId, CompleteRole);
					ProfileAssociationCompletion completion;
					if (existing == null)
					{
						_associationStore.FinalizeLocalRestoreOutgoingAssociations(operation.Target,
							intent.Associations.Select(x => x.AssociationId));
						VerifyAssociationsFinalized(operation.Target, intent);
						_boundaryCoordinator.VerifyProfilePreserved(intent);
						completion = new ProfileAssociationCompletion(sealedCapture.Capture.Identity.ToString(),
							reviewedPlan.PlanFingerprint, ownerId, operation.CheckpointSequence,
							intent.Associations.Count, intent.Profile == null ? null : intent.Profile.ProfileId);
						PersistCompletion(ownerId, completion);
					}
					else
					{
						completion = ReadCompletion(existing.ArtifactId);
						ValidateCompletion(completion, sealedCapture, reviewedPlan, operation, intent);
						VerifyAssociationsFinalized(operation.Target, intent);
						_boundaryCoordinator.VerifyProfilePreserved(intent);
					}

					CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
					if (persisted.CheckpointSequence == completion.CheckpointBefore)
					{
						persisted = new CollectionOperation(persisted.Identity, persisted.Kind, persisted.Collection, persisted.Target,
							persisted.Revision, persisted.PlanIdentity, checked(persisted.CheckpointSequence + 1),
							CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending, persisted.NativeChildren);
						_operationStore.SaveOperation(persisted);
					}
					else if (persisted.CheckpointSequence < completion.CheckpointBefore + 1 ||
						persisted.Phase != CollectionOperationPhase.PausedAtSafeBoundary ||
						persisted.ResultState != CollectionOperationResultState.Pending)
					{
						MarkRecoveryRequired(persisted);
						throw new InvalidOperationException("The C7.11 completion checkpoint no longer matches the durable Local restore journal.");
					}

					return new CollectionLocalRestoreProfileAssociationExecutionResult(persisted,
						intent.Associations.Count, intent.Profile != null);
				}
				catch
				{
					CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
					if (!current.IsTerminal) MarkRecoveryRequired(current);
					throw;
				}
			}
		}

		/// <summary>Verifies that the C7.11 profile boundary and superseded association state still hold at final commit.</summary>
		internal void VerifyFinalState(CollectionOperation operation, CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			CollectionLocalRestoreProfileBoundaryIntent intent = _boundaryCoordinator.RequireIntent(operation, sealedCapture, reviewedPlan);
			_boundaryCoordinator.VerifyProfilePreserved(intent);
			VerifyAssociationsFinalized(operation.Target, intent);
		}

		private static void ValidateInputs(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreUserMetadataExecutionResult userMetadataPhase, GameStoragePathSet paths)
		{
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (userMetadataPhase == null) throw new ArgumentNullException(nameof(userMetadataPhase));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!userMetadataPhase.IsReadyForProfileReconciliation)
				throw new InvalidOperationException("C7.11 requires the logical user-metadata phase to be durably verified first.");
			if (!sealedCapture.Capture.Identity.Equals(reviewedPlan.CaptureIdentity))
				throw new InvalidOperationException("The C7.11 inputs belong to different Local Collection captures.");
		}

		private void VerifyAssociationsFinalized(CollectionTargetIdentity target, CollectionLocalRestoreProfileBoundaryIntent intent)
		{
			IReadOnlyList<CollectionTargetAssociation> current = _associationStore.GetAssociationsForTarget(target);
			var expectedIds = new HashSet<Guid>(intent.Associations.Select(x => x.AssociationId));
			if (!expectedIds.SetEquals(current.Select(x => x.AssociationId)))
				throw new InvalidOperationException("The outgoing Collection association set changed after the Local restore profile boundary was established.");
			foreach (CollectionTargetAssociation association in current)
			{
				if (association.State != CollectionAssociationState.Incomplete)
					throw new InvalidOperationException("A superseded outgoing Collection association was not durably reconciled as Incomplete.");
			}
		}

		private void PersistCompletion(string ownerId, ProfileAssociationCompletion completion)
		{
			byte[] bytes = SerializeCompletion(completion);
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(bytes, false)) artifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				ownerId, CompleteRole);
		}

		private static byte[] SerializeCompletion(ProfileAssociationCompletion completion)
		{
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject();
				writer.WritePropertyName("format"); writer.WriteValue(CompleteFormat);
				writer.WritePropertyName("captureId"); writer.WriteValue(completion.CaptureId);
				writer.WritePropertyName("planFingerprint"); writer.WriteValue(completion.PlanFingerprint);
				writer.WritePropertyName("operationId"); writer.WriteValue(completion.OperationId);
				writer.WritePropertyName("checkpointBefore"); writer.WriteValue(completion.CheckpointBefore);
				writer.WritePropertyName("associationCount"); writer.WriteValue(completion.AssociationCount);
				writer.WritePropertyName("profileId"); writer.WriteValue(completion.ProfileId);
				writer.WriteEndObject(); writer.Flush(); text.Flush(); return stream.ToArray();
			}
		}

		private ProfileAssociationCompletion ReadCompletion(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable C7.11 completion marker no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], CompleteFormat))
					throw new InvalidDataException("Unsupported C7.11 completion-marker format.");
				JToken profileId = root["profileId"];
				return new ProfileAssociationCompletion((string)root["captureId"], (string)root["planFingerprint"],
					(string)root["operationId"], (long)root["checkpointBefore"], (int)root["associationCount"],
					profileId == null || profileId.Type == JTokenType.Null ? null : (string)profileId);
			}
		}

		private static void ValidateCompletion(ProfileAssociationCompletion completion, CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan plan, CollectionOperation operation, CollectionLocalRestoreProfileBoundaryIntent intent)
		{
			string expectedProfileId = intent.Profile == null ? null : intent.Profile.ProfileId;
			if (completion == null || !StringComparer.Ordinal.Equals(completion.CaptureId, sealedCapture.Capture.Identity.ToString()) ||
				!StringComparer.Ordinal.Equals(completion.PlanFingerprint, plan.PlanFingerprint) ||
				!StringComparer.Ordinal.Equals(completion.OperationId, operation.Identity.OperationId.ToString("D")) ||
				completion.AssociationCount != intent.Associations.Count ||
				!StringComparer.OrdinalIgnoreCase.Equals(completion.ProfileId ?? String.Empty, expectedProfileId ?? String.Empty))
				throw new InvalidDataException("The durable C7.11 completion marker differs from the reviewed Local restore boundary.");
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionLocalRestorePlan plan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal ||
				!operation.Target.Equals(plan.Target))
				throw new InvalidOperationException("The active Local restore operation journal is unavailable at C7.11.");
			return operation;
		}

		private void MarkRecoveryRequired(CollectionOperation operation)
		{
			CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (current.IsTerminal || (current.Phase == CollectionOperationPhase.RecoveryRequired &&
				current.ResultState == CollectionOperationResultState.RecoveryRequired)) return;
			_operationStore.SaveOperation(new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target,
				current.Revision, current.PlanIdentity, checked(current.CheckpointSequence + 1), CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.RecoveryRequired, current.NativeChildren));
		}

		private sealed class ProfileAssociationCompletion
		{
			internal ProfileAssociationCompletion(string captureId, string planFingerprint, string operationId,
				long checkpointBefore, int associationCount, string profileId)
			{
				CaptureId = captureId; PlanFingerprint = planFingerprint; OperationId = operationId;
				CheckpointBefore = checkpointBefore; AssociationCount = associationCount; ProfileId = profileId;
			}
			internal string CaptureId { get; }
			internal string PlanFingerprint { get; }
			internal string OperationId { get; }
			internal long CheckpointBefore { get; }
			internal int AssociationCount { get; }
			internal string ProfileId { get; }
		}
	}
}
