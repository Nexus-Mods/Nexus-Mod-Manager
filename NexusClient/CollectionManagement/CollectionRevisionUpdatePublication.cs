using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Immutable feature-metadata publication prepared after C10.8 aggregate verification.</summary>
	public sealed class CollectionRevisionUpdatePublicationPlan
	{
		private readonly ReadOnlyCollection<CollectionMemberBinding> _bindings;
		private readonly ReadOnlyCollection<UserOverride> _overrides;

		internal CollectionRevisionUpdatePublicationPlan(CollectionTargetAssociation oldAssociation,
			CollectionTargetAssociation candidateAssociation, IEnumerable<CollectionMemberBinding> bindings,
			IEnumerable<UserOverride> overrides)
		{
			OldAssociation = oldAssociation ?? throw new ArgumentNullException(nameof(oldAssociation));
			CandidateAssociation = candidateAssociation ?? throw new ArgumentNullException(nameof(candidateAssociation));
			List<CollectionMemberBinding> copiedBindings = (bindings ?? throw new ArgumentNullException(nameof(bindings))).ToList();
			List<UserOverride> copiedOverrides = (overrides ?? throw new ArgumentNullException(nameof(overrides))).ToList();
			if (copiedBindings.Any(x => x == null) || copiedBindings.Select(x => x.MemberKey).Distinct().Count() != copiedBindings.Count)
				throw new ArgumentException("A C10.9 publication cannot contain null or duplicate member bindings.", nameof(bindings));
			if (copiedOverrides.Any(x => x == null) || copiedOverrides.Select(x => x.OverrideId).Distinct().Count() != copiedOverrides.Count)
				throw new ArgumentException("A C10.9 publication cannot contain null or duplicate user overrides.", nameof(overrides));
			_bindings = new ReadOnlyCollection<CollectionMemberBinding>(copiedBindings.OrderBy(x => x.MemberKey.Kind)
				.ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal).ToList());
			_overrides = new ReadOnlyCollection<UserOverride>(copiedOverrides.OrderBy(x => x.OverrideId).ToList());
		}

		public CollectionTargetAssociation OldAssociation { get; }
		public CollectionTargetAssociation CandidateAssociation { get; }
		public ReadOnlyCollection<CollectionMemberBinding> Bindings { get { return _bindings; } }
		public ReadOnlyCollection<UserOverride> Overrides { get { return _overrides; } }
	}

	/// <summary>
	/// Pure C10.9 feature-state builder. It rebases only override kinds whose candidate baseline is explicitly typed;
	/// opaque artifact/recipe/file/plugin/configuration decisions must have been rejected by C10.5 before this point.
	/// </summary>
	public sealed class CollectionRevisionUpdatePublicationBuilder
	{
		public CollectionRevisionUpdatePublicationPlan Build(CollectionRevisionUpdatePlan updatePlan,
			CollectionRevisionUpdateOverridePreservationPlan preservation,
			IReadOnlyDictionary<CollectionMemberKey, NativeModInstanceIdentity> candidateNativeByMember)
		{
			if (updatePlan == null) throw new ArgumentNullException(nameof(updatePlan));
			if (preservation == null) throw new ArgumentNullException(nameof(preservation));
			if (candidateNativeByMember == null) throw new ArgumentNullException(nameof(candidateNativeByMember));
			if (!preservation.IsQualified) throw new InvalidOperationException("C10.9 cannot publish unresolved override preservation decisions.");
			preservation.ReviewedIntent.ValidateCurrentPlan(updatePlan);

			HashSet<CollectionMemberKey> candidateKeys = new HashSet<CollectionMemberKey>(updatePlan.NewPlan.SelectedMembers.Select(x => x.MemberKey));
			HashSet<CollectionMemberKey> plannedCandidateKeys = new HashSet<CollectionMemberKey>(updatePlan.Members
				.Where(x => x.NewMember != null).Select(x => x.MemberKey));
			if (!plannedCandidateKeys.SetEquals(candidateKeys))
				throw new InvalidOperationException("C10.9 requires the update member plan to cover the exact selected candidate member identities.");
			if (candidateNativeByMember.Keys.Any(x => !candidateKeys.Contains(x)))
				throw new InvalidOperationException("C10.9 candidate native evidence contains a member outside the candidate revision.");

			Guid newAssociationId = Guid.NewGuid();
			var associationShell = new CollectionTargetAssociation(newAssociationId, updatePlan.NewPlan.Revision,
				updatePlan.NewPlan.Target, CollectionAssociationState.Applied);
			List<UserOverride> rebasedOverrides = RebaseOverrides(associationShell, preservation);
			CollectionAssociationState finalState = rebasedOverrides.Count == 0
				? CollectionAssociationState.Applied : CollectionAssociationState.Modified;
			var finalAssociation = new CollectionTargetAssociation(newAssociationId, updatePlan.NewPlan.Revision,
				updatePlan.NewPlan.Target, finalState);

			var bindings = new List<CollectionMemberBinding>();
			HashSet<CollectionMemberKey> suppressed = new HashSet<CollectionMemberKey>(preservation.MembersWhoseCandidateMutationIsSuppressed);
			foreach (CollectionRevisionUpdateMemberPlan member in updatePlan.Members.Where(x => x.NewMember != null))
			{
				CollectionMemberKey candidateMemberKey = member.NewMember.MemberKey;
				if (!member.MemberKey.Equals(candidateMemberKey) || !candidateKeys.Contains(candidateMemberKey))
					throw new InvalidOperationException("C10.9 encountered a candidate member plan whose identity is not the exact candidate-revision member key.");

				NativeModInstanceIdentity candidateNative;
				if (candidateNativeByMember.TryGetValue(candidateMemberKey, out candidateNative))
				{
					bindings.Add(new CollectionMemberBinding(finalAssociation, candidateMemberKey, candidateNative,
						member.NewMember.RecipeIdentity, CollectionMemberBindingKind.InstalledForCollection));
					continue;
				}

				// A preserved participation opt-out intentionally has no candidate binding. Check it before considering
				// an old binding so a future logical cross-key plan cannot accidentally republish the predecessor key/state.
				if (IsIntentionalCandidateOmission(candidateMemberKey, preservation))
					continue;

				if (member.Binding != null)
				{
					CollectionRecipeIdentity verifiedRecipe = suppressed.Contains(candidateMemberKey)
						? member.Binding.VerifiedRecipe : member.NewMember.RecipeIdentity;
					bindings.Add(new CollectionMemberBinding(finalAssociation, candidateMemberKey, member.Binding.NativeMod,
						verifiedRecipe, member.Binding.BindingKind));
					continue;
				}

				throw new InvalidOperationException("A candidate member has neither verified candidate native evidence nor a preserved binding.");
			}

			HashSet<CollectionMemberKey> intentionallyOmitted = new HashSet<CollectionMemberKey>(candidateKeys
				.Where(x => IsIntentionalCandidateOmission(x, preservation)));
			HashSet<CollectionMemberKey> expectedBindingKeys = new HashSet<CollectionMemberKey>(candidateKeys.Where(x => !intentionallyOmitted.Contains(x)));
			if (!expectedBindingKeys.SetEquals(bindings.Select(x => x.MemberKey)))
				throw new InvalidOperationException("C10.9 publication bindings do not exactly cover the selected candidate revision after qualified omissions.");

			return new CollectionRevisionUpdatePublicationPlan(updatePlan.Association, finalAssociation, bindings,
				rebasedOverrides.Select(x => new UserOverride(x.OverrideId,
					new CollectionRequirementReference(finalAssociation, x.Requirement.MemberKey, x.Requirement.Aspect, x.Requirement.SubjectKey),
					x.BaselineState, x.UserChosenState, x.Note)));
		}

		private static List<UserOverride> RebaseOverrides(CollectionTargetAssociation association,
			CollectionRevisionUpdateOverridePreservationPlan preservation)
		{
			var result = new List<UserOverride>();
			foreach (CollectionRevisionUpdateOverridePreservationAction action in preservation.Actions)
			{
				if (!action.IsQualified)
					throw new InvalidOperationException("C10.9 encountered an unresolved preservation action.");
				if (action.Disposition == CollectionRevisionUpdateOverridePreservationDisposition.SatisfiedByCandidateRemoval)
					continue;

				UserOverride old = action.UserOverride;
				CollectionRequirementState baseline;
				switch (action.Disposition)
				{
					case CollectionRevisionUpdateOverridePreservationDisposition.PreserveAdditionalManagedContent:
						if (old.Requirement.Aspect != CollectionRequirementAspect.AdditionalManagedContent)
							throw new InvalidOperationException("C10.9 additional-content preservation has an incompatible requirement aspect.");
						baseline = CollectionRequirementState.Absent();
						break;
					case CollectionRevisionUpdateOverridePreservationDisposition.PreserveExistingNativeState:
						if (old.Requirement.Aspect != CollectionRequirementAspect.MemberParticipation ||
							old.UserChosenState.Kind != CollectionRequirementStateKind.Absent)
							throw new InvalidOperationException("C10.9 has no typed candidate-baseline adapter for this preserved native-state override.");
						baseline = CollectionMemberRequirementStates.Included();
						break;
					case CollectionRevisionUpdateOverridePreservationDisposition.ReapplyAfterCandidateExecution:
						if (old.Requirement.Aspect != CollectionRequirementAspect.MemberEnabledState)
							throw new InvalidOperationException("C10.9 has no typed candidate-baseline adapter for this replayed override.");
						bool ignored;
						if (!CollectionMemberRequirementStates.TryGetEnabled(old.UserChosenState, out ignored))
							throw new InvalidOperationException("C10.9 encountered a non-canonical enabled-state override.");
						baseline = CollectionMemberRequirementStates.Enabled(true);
						break;
					default:
						throw new InvalidOperationException("C10.9 encountered an unsupported qualified override disposition.");
				}

				if (baseline.Equals(old.UserChosenState))
					continue;
				CollectionMemberKey candidateMemberKey = old.Requirement.MemberKey == null ? null :
					preservation.UpdatePlan.MemberCorrelations.ResolveCandidateMemberKey(old.Requirement.MemberKey);
				var requirement = new CollectionRequirementReference(association, candidateMemberKey,
					old.Requirement.Aspect, old.Requirement.SubjectKey);
				result.Add(new UserOverride(old.OverrideId, requirement, baseline, old.UserChosenState, old.Note));
			}
			return result;
		}

		private static bool IsIntentionalCandidateOmission(CollectionMemberKey memberKey,
			CollectionRevisionUpdateOverridePreservationPlan preservation)
		{
			return preservation.Actions.Any(x => x.SuppressesCandidateMemberMutation &&
				x.UserOverride.Requirement.MemberKey != null &&
				preservation.UpdatePlan.MemberCorrelations.ResolveCandidateMemberKey(x.UserOverride.Requirement.MemberKey).Equals(memberKey) &&
				x.UserOverride.Requirement.Aspect == CollectionRequirementAspect.MemberParticipation &&
				x.UserOverride.UserChosenState.Kind == CollectionRequirementStateKind.Absent);
		}
	}

	/// <summary>Terminal C10.9 publication result. Native state was already verified by C10.8.</summary>
	public sealed class CollectionRevisionUpdatePublicationResult
	{
		internal CollectionRevisionUpdatePublicationResult(CollectionOperation operation, CollectionTargetAssociation association,
			IEnumerable<CollectionMemberBinding> bindings, IEnumerable<UserOverride> overrides)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Association = association ?? throw new ArgumentNullException(nameof(association));
			Bindings = new ReadOnlyCollection<CollectionMemberBinding>((bindings ?? throw new ArgumentNullException(nameof(bindings))).ToList());
			Overrides = new ReadOnlyCollection<UserOverride>((overrides ?? throw new ArgumentNullException(nameof(overrides))).ToList());
		}
		public CollectionOperation Operation { get; }
		public CollectionTargetAssociation Association { get; }
		public ReadOnlyCollection<CollectionMemberBinding> Bindings { get; }
		public ReadOnlyCollection<UserOverride> Overrides { get; }
	}

	/// <summary>
	/// C10.9 atomically publishes the verified candidate revision in Collections metadata. It performs no native mutation.
	/// The old association, candidate association/bindings/rebased overrides and terminal UpdateRevision journal checkpoint
	/// are switched in one SQLite transaction only after the C10.8 native-state fingerprint is observed unchanged.
	/// </summary>
	public sealed class CollectionRevisionUpdatePublicationCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
		private readonly CollectionRevisionUpdateReviewCoordinator _reviewCoordinator;
		private readonly CollectionRevisionUpdateAggregateVerificationCoordinator _aggregateCoordinator;
		private readonly CollectionNativeStateReader _nativeStateReader;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;
		private readonly CollectionRevisionUpdatePublicationBuilder _builder;

		public CollectionRevisionUpdatePublicationCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore, CollectionsAssociationStore associationStore,
			CollectionsNativeChildRecoveryManifestStore manifestStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
			if (planStore == null) throw new ArgumentNullException(nameof(planStore));
			_reviewCoordinator = new CollectionRevisionUpdateReviewCoordinator(operationStore, planStore, associationStore);
			_aggregateCoordinator = new CollectionRevisionUpdateAggregateVerificationCoordinator(services, gameStorageService,
				operationStore, planStore, associationStore, manifestStore,
				artifactStore ?? throw new ArgumentNullException(nameof(artifactStore)),
				referenceStore ?? throw new ArgumentNullException(nameof(referenceStore)));
			_nativeStateReader = new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, associationStore);
			_mutationLeaseManager = CollectionTargetMutationLeaseManager.Shared;
			_authorityValidator = new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services);
			_builder = new CollectionRevisionUpdatePublicationBuilder();
		}

		public async Task<CollectionRevisionUpdatePublicationResult> PublishAsync(CollectionOperationIdentity operationIdentity,
			CollectionRevisionUpdatePlan updatePlan, CollectionRevisionUpdateOverridePreservationPlan preservation,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (updatePlan == null) throw new ArgumentNullException(nameof(updatePlan));
			if (preservation == null) throw new ArgumentNullException(nameof(preservation));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!preservation.IsQualified) throw new InvalidOperationException("C10.9 cannot publish an update with unresolved override preservation decisions.");
			CollectionRevisionUpdateReviewedIntent reviewed = _reviewCoordinator.LoadReviewedIntent(operationIdentity);
			if (!StringComparer.Ordinal.Equals(reviewed.ReviewFingerprint, preservation.ReviewedIntent.ReviewFingerprint))
				throw new InvalidOperationException("The C10.9 preservation plan does not belong to the exact durable revision-update review.");
			reviewed.ValidateCurrentPlan(updatePlan);

			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			if (operation != null && operation.IsSuccessful)
				return LoadCompleted(operation, updatePlan);
			RequireReadyOperation(operation, updatePlan.NewPlan);
			CollectionRevisionUpdateAggregateVerificationRecord verification = _aggregateCoordinator.LoadVerification(operationIdentity,
				updatePlan.NewPlan.Identity);
			if (!StringComparer.Ordinal.Equals(verification.ReviewFingerprint, reviewed.ReviewFingerprint))
				throw new InvalidDataException("The C10.8 aggregate verification belongs to another revision-update review.");

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(updatePlan.NewPlan.Target))
				throw new InvalidOperationException("The canonical target changed before C10.9 publication.");
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				operation = _operationStore.GetOperation(operationIdentity);
				if (operation != null && operation.IsSuccessful)
					return LoadCompleted(operation, updatePlan);
				RequireReadyOperation(operation, updatePlan.NewPlan);

				CollectionNativeStateIndex current = _nativeStateReader.Capture(updatePlan.NewPlan.Target);
				if (!current.Fingerprint.Equals(verification.StateFingerprint))
					throw new InvalidOperationException("Authoritative native/Collection state changed after C10.8 verification; the candidate revision must be verified again before publication.");
				Dictionary<CollectionMemberKey, NativeModInstanceIdentity> candidateNative = ResolveCandidateNative(operation,
					updatePlan, preservation, current);
				CollectionRevisionUpdatePublicationPlan publication = _builder.Build(updatePlan, preservation, candidateNative);
				CollectionOperation committed = BuildCommitted(operation);
				List<UserOverride> expectedOldOverrides = preservation.Actions.Select(x => x.UserOverride).OrderBy(x => x.OverrideId).ToList();
				_associationStore.FinalizeRevisionUpdateAssociation(reviewed, verification, publication.CandidateAssociation,
					publication.Bindings, expectedOldOverrides, publication.Overrides, committed);

				CollectionOperation persisted = _operationStore.GetOperation(operationIdentity);
				CollectionTargetAssociation association = _associationStore.GetAssociation(publication.CandidateAssociation.AssociationId);
				if (persisted == null || !persisted.IsSuccessful || association == null)
					throw new InvalidDataException("C10.9 atomic publication returned without its terminal operation/association state.");
				return new CollectionRevisionUpdatePublicationResult(persisted, association,
					_associationStore.GetBindings(association.AssociationId), _associationStore.GetOverrides(association.AssociationId));
			}
		}

		private Dictionary<CollectionMemberKey, NativeModInstanceIdentity> ResolveCandidateNative(CollectionOperation operation,
			CollectionRevisionUpdatePlan updatePlan, CollectionRevisionUpdateOverridePreservationPlan preservation,
			CollectionNativeStateIndex current)
		{
			var result = new Dictionary<CollectionMemberKey, NativeModInstanceIdentity>();
			HashSet<CollectionMemberKey> suppressed = new HashSet<CollectionMemberKey>(preservation.MembersWhoseCandidateMutationIsSuppressed);
			foreach (CollectionRevisionUpdateMemberPlan member in updatePlan.Members.Where(x =>
				CollectionRevisionUpdateAggregateVerifier.RequiresCandidateMutation(x) && !suppressed.Contains(x.MemberKey)))
			{
				CollectionNativeChildOperation child = CollectionRevisionUpdateAggregateVerificationCoordinator.RequireCommittedCandidateChild(operation,
					new CollectionOperationMemberReference(updatePlan.NewPlan.Revision, member.MemberKey));
				CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, child);
				if (manifest == null || manifest.ExecutionEvidence == null)
					throw new InvalidDataException("A C10.9 candidate child is missing committed execution evidence.");
				CollectionNativeModState native;
				string failure;
				if (!CollectionNativeChildRestartReconciliationCoordinator.TryVerifyCommittedState(manifest, manifest.ExecutionEvidence,
					current, _services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName,
					_services.ModManager.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, _services.ModManager.GameMode,
					out native, out failure, false))
					throw new InvalidOperationException("The verified C10 candidate child changed before publication: " + (failure ?? "unknown mismatch"));
				result.Add(member.MemberKey, native.Identity);
			}
			return result;
		}

		private CollectionRevisionUpdatePublicationResult LoadCompleted(CollectionOperation operation, CollectionRevisionUpdatePlan updatePlan)
		{
			if (operation.Kind != CollectionOperationKind.UpdateRevision || operation.PlanIdentity == null ||
				!operation.PlanIdentity.Equals(updatePlan.NewPlan.Identity) || operation.Revision == null ||
				!operation.Revision.Equals(updatePlan.NewPlan.Revision) || !operation.Target.Equals(updatePlan.NewPlan.Target))
				throw new InvalidOperationException("The completed operation does not match the requested C10 revision update.");
			List<CollectionTargetAssociation> matches = _associationStore.GetAssociationsForTarget(updatePlan.NewPlan.Target)
				.Where(x => x.Revision.Equals(updatePlan.NewPlan.Revision)).ToList();
			if (matches.Count != 1)
				throw new InvalidDataException("A completed C10 revision update does not resolve to exactly one published candidate association.");
			CollectionTargetAssociation association = matches[0];
			return new CollectionRevisionUpdatePublicationResult(operation, association,
				_associationStore.GetBindings(association.AssociationId), _associationStore.GetOverrides(association.AssociationId));
		}

		private static void RequireReadyOperation(CollectionOperation operation, ResolvedCollectionPlan candidatePlan)
		{
			if (operation == null || operation.IsTerminal || operation.Kind != CollectionOperationKind.UpdateRevision ||
				operation.Phase != CollectionOperationPhase.CandidateRevisionAggregateVerified || operation.ResultState != CollectionOperationResultState.Pending ||
				operation.PlanIdentity == null || !operation.PlanIdentity.Equals(candidatePlan.Identity) || operation.Revision == null ||
				!operation.Revision.Equals(candidatePlan.Revision) || !operation.Target.Equals(candidatePlan.Target))
				throw new InvalidOperationException("C10.9 requires the exact non-terminal CandidateRevisionAggregateVerified operation.");
		}

		private static CollectionOperation BuildCommitted(CollectionOperation operation)
		{
			if (operation.CheckpointSequence == Int64.MaxValue)
				throw new InvalidOperationException("The Collection operation checkpoint sequence cannot be incremented further.");
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target, operation.Revision,
				operation.PlanIdentity, operation.CheckpointSequence + 1, CollectionOperationPhase.Completed,
				CollectionOperationResultState.Committed, operation.NativeChildren);
		}
	}
}
