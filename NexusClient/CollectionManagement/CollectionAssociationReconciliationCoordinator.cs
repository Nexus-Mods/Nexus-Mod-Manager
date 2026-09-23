using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Reconciles C6.8/C6.9 verified native outcomes into Collection association/member provenance.
	/// </summary>
	/// <remarks>
	/// This coordinator never writes native ownership. It creates or refreshes feature provenance only after native
	/// durability is independently known, and it keeps a partially applied association Incomplete until every selected
	/// member is satisfied and the collection operation can enter final verification.
	/// </remarks>
	public sealed class CollectionAssociationReconciliationCoordinator
	{
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
		private readonly CollectionOperationCoordinator _operationCoordinator;

		/// <summary>Creates a C6.10 provenance coordinator over the existing Collections stores/state machine.</summary>
		public CollectionAssociationReconciliationCoordinator(CollectionsOperationStore operationStore,
			CollectionsResolvedPlanStore planStore, CollectionsAssociationStore associationStore,
			CollectionsNativeChildRecoveryManifestStore manifestStore)
		{
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			if (planStore == null) throw new ArgumentNullException(nameof(planStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
			_operationCoordinator = new CollectionOperationCoordinator(operationStore, planStore);
		}

		/// <summary>Reconciles one live C6.8 terminal verification result into Collection provenance.</summary>
		public CollectionAssociationReconciliationResult ReconcileVerifiedChild(
			CollectionNativeChildVerificationResult verificationResult, ResolvedCollectionPlan plan)
		{
			if (verificationResult == null) throw new ArgumentNullException(nameof(verificationResult));
			return ReconcileVerifiedChild(verificationResult.Operation, verificationResult.Child,
				verificationResult.NativeState, verificationResult.VerifiedNativeMod, plan);
		}

		/// <summary>Reconciles one C6.9 restart result into Collection provenance without replaying native work.</summary>
		public CollectionAssociationReconciliationResult ReconcileRestartedChild(
			CollectionNativeChildRestartReconciliationResult reconciliationResult, ResolvedCollectionPlan plan)
		{
			if (reconciliationResult == null) throw new ArgumentNullException(nameof(reconciliationResult));
			return ReconcileVerifiedChild(reconciliationResult.Operation, reconciliationResult.Child,
				reconciliationResult.NativeState, reconciliationResult.VerifiedNativeMod, plan);
		}

		/// <summary>
		/// Publishes the complete Applied association after every selected member is satisfied, including adopted no-op members.
		/// </summary>
		/// <remarks>
		/// Mutating members must already have C6.10 InstalledForCollection bindings from verified child reconciliation.
		/// InstalledCompatible members are added as AdoptedExisting only from the exact C6.2 verified binding evidence.
		/// </remarks>
		public CollectionAssociationFinalizationResult FinalizeAppliedAssociation(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, CollectionMemberMatchSet matches)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (matches == null) throw new ArgumentNullException(nameof(matches));
			ValidatePlan(plan);
			if (!matches.PlanIdentity.Equals(plan.Identity) || !matches.Target.Equals(plan.Target))
				throw new ArgumentException("The C6.2 match set does not belong to the exact Collection plan.", nameof(matches));
			if (matches.HasBlockedMembers || matches.HasAcquisitionRequired)
				throw new InvalidOperationException("An unresolved C6.2 match set cannot publish an Applied Collection association.");

			CollectionOperation alreadyCompleted = _operationStore.GetOperation(operationIdentity);
			if (alreadyCompleted != null && alreadyCompleted.IsSuccessful)
				return LoadCompletedFinalization(alreadyCompleted, plan);

			CollectionOperation operation = RequireOperation(operationIdentity, plan,
				CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationPhase.Recovering, CollectionOperationPhase.Verifying);
			foreach (CollectionNativeChildOperation child in operation.NativeChildren)
			{
				if (!child.IsReconciled || !child.HasVerifiedCommittedNativeState)
					throw new InvalidOperationException("An Applied Collection association requires every submitted native child to be reconciled as VerifiedCommitted.");
			}

			CollectionTargetAssociation association = ResolveExactAssociation(plan.Revision, plan.Target);
			if (association == null)
			{
				if (operation.NativeChildren.Count > 0)
					throw new InvalidOperationException("Verified native children are missing the C6.10 association created by child reconciliation.");
				association = new CollectionTargetAssociation(Guid.NewGuid(), plan.Revision, plan.Target, CollectionAssociationState.Incomplete);
			}
			else
			{
				if (association.State == CollectionAssociationState.Modified || association.State == CollectionAssociationState.Recovering)
					throw new InvalidOperationException("C6.10 will not silently clear a pre-existing Modified/Recovering association state; later override/drift policy must reconcile it explicitly.");
				if (_associationStore.GetOverrides(association.AssociationId).Count > 0)
					throw new InvalidOperationException("C6.10 will not mark an association Applied while deliberate user overrides remain unresolved.");
			}

			Dictionary<CollectionMemberKey, CollectionMemberBinding> existingBindings = _associationStore
				.GetBindings(association.AssociationId).ToDictionary(x => x.MemberKey);
			var selectedKeys = new HashSet<CollectionMemberKey>(plan.SelectedMembers.Select(x => x.MemberKey));
			if (existingBindings.Keys.Any(x => !selectedKeys.Contains(x)))
				throw new InvalidOperationException("The existing Collection association contains member provenance outside the exact selected plan; C6.10 will not silently detach it.");

			var finalBindings = new List<CollectionMemberBinding>(plan.SelectedMembers.Count);
			var provenanceByNativeMod = new Dictionary<NativeModInstanceIdentity, NativeModProvenance>();
			foreach (ResolvedCollectionMemberPlan member in plan.SelectedMembers)
			{
				CollectionMemberMatchResult match;
				if (!matches.MembersByKey.TryGetValue(member.MemberKey, out match))
					throw new InvalidOperationException("The exact C6.2 match set is missing a selected Collection member.");

				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
				{
					CollectionNativeModState nativeMod = match.MatchedNativeMod;
					if (nativeMod == null || !nativeMod.Identity.Target.Equals(plan.Target) ||
						!HasAppliedVerifiedBindingEvidence(match, nativeMod.Identity, member.RecipeIdentity))
						throw new InvalidOperationException("An InstalledCompatible member no longer has the exact applied binding evidence required for non-destructive adoption.");
					CollectionMemberBinding existingBinding;
					CollectionMemberBindingKind bindingKind = CollectionMemberBindingKind.AdoptedExisting;
					if (existingBindings.TryGetValue(member.MemberKey, out existingBinding))
					{
						if (!existingBinding.NativeMod.Equals(nativeMod.Identity) || !existingBinding.VerifiedRecipe.Equals(member.RecipeIdentity))
							throw new InvalidOperationException("An existing association member binding contradicts the verified installed-compatible instance.");
						bindingKind = existingBinding.BindingKind;
					}
					finalBindings.Add(new CollectionMemberBinding(association, member.MemberKey, nativeMod.Identity,
						member.RecipeIdentity, bindingKind));
					if (!existingBindings.ContainsKey(member.MemberKey) && match.ExistingBindings.Count == 0)
						AddProvenanceCandidate(provenanceByNativeMod, nativeMod.Identity, StandaloneModUse.ExplicitStandaloneUse);
					continue;
				}

				if (match.Disposition != CollectionMemberMatchDisposition.ArchiveOnlyReuse &&
					match.Disposition != CollectionMemberMatchDisposition.ReinstallRequired)
					throw new InvalidOperationException("Only installed-compatible, archive-only and reviewed reinstall members can finalize an additive association.");

				CollectionMemberBinding binding;
				if (!existingBindings.TryGetValue(member.MemberKey, out binding) ||
					binding.BindingKind != CollectionMemberBindingKind.InstalledForCollection ||
					!binding.VerifiedRecipe.Equals(member.RecipeIdentity) || !binding.NativeMod.Target.Equals(plan.Target))
					throw new InvalidOperationException("A mutating Collection member is missing its exact verified C6.10 InstalledForCollection binding.");
				finalBindings.Add(new CollectionMemberBinding(association, member.MemberKey, binding.NativeMod,
					member.RecipeIdentity, CollectionMemberBindingKind.InstalledForCollection));
				if (match.Disposition == CollectionMemberMatchDisposition.ArchiveOnlyReuse)
					AddProvenanceCandidate(provenanceByNativeMod, binding.NativeMod, StandaloneModUse.NoStandaloneUseVerified);
			}

			if (operation.Phase == CollectionOperationPhase.ApplyingNativeChildren)
				operation = _operationCoordinator.BeginVerifying(operation.Identity);
			else if (operation.Phase == CollectionOperationPhase.Recovering)
				operation = _operationCoordinator.ResumeVerifyingAfterRecovery(operation.Identity, plan.Identity);

			association = association.WithState(CollectionAssociationState.Applied);
			finalBindings = finalBindings.Select(x => new CollectionMemberBinding(association, x.MemberKey, x.NativeMod,
				x.VerifiedRecipe, x.BindingKind)).ToList();
			_associationStore.SaveAppliedAssociation(association, finalBindings, provenanceByNativeMod.Values);

			operation = _operationStore.GetOperation(operation.Identity);
			if (operation == null || operation.Phase != CollectionOperationPhase.Verifying)
				throw new InvalidOperationException("The Collection operation left Verifying before C6.10 could finalize provenance.");
			foreach (CollectionNativeChildOperation child in operation.NativeChildren)
				if (!child.IsReconciled || !child.HasVerifiedCommittedNativeState)
					throw new InvalidOperationException("Native child state changed before the Collection operation could be committed.");
			operation = _operationCoordinator.CompleteCommitted(operation.Identity);

			return new CollectionAssociationFinalizationResult(operation, association, finalBindings);
		}

		private CollectionAssociationFinalizationResult LoadCompletedFinalization(CollectionOperation operation, ResolvedCollectionPlan plan)
		{
			if (operation.Kind != CollectionOperationKind.ApplyResolvedPlan || operation.PlanIdentity == null ||
				!operation.PlanIdentity.Equals(plan.Identity) || operation.Revision == null || !operation.Revision.Equals(plan.Revision) ||
				!operation.Target.Equals(plan.Target) || !operation.Collection.Equals(plan.Revision.Collection))
				throw new InvalidOperationException("The completed Collection operation does not belong to the exact C6.10 plan.");

			CollectionTargetAssociation association = ResolveExactAssociation(plan.Revision, plan.Target);
			if (association == null || association.State != CollectionAssociationState.Applied)
				throw new InvalidOperationException("A committed Collection operation is missing its Applied association provenance.");
			List<CollectionMemberBinding> bindings = _associationStore.GetBindings(association.AssociationId).ToList();
			if (bindings.Count != plan.SelectedMembers.Count)
				throw new InvalidOperationException("A committed Collection association does not contain the exact selected member binding set.");
			foreach (ResolvedCollectionMemberPlan member in plan.SelectedMembers)
			{
				CollectionMemberBinding binding = bindings.SingleOrDefault(x => x.MemberKey.Equals(member.MemberKey));
				if (binding == null || !binding.VerifiedRecipe.Equals(member.RecipeIdentity) || !binding.NativeMod.Target.Equals(plan.Target))
					throw new InvalidOperationException("A committed Collection association contains stale or incomplete member provenance.");
			}
			return new CollectionAssociationFinalizationResult(operation, association, bindings);
		}

		private CollectionAssociationReconciliationResult ReconcileVerifiedChild(CollectionOperation observedOperation,
			CollectionNativeChildOperation observedChild, CollectionNativeStateIndex nativeState,
			CollectionNativeModState verifiedNativeMod, ResolvedCollectionPlan plan)
		{
			if (observedOperation == null) throw new ArgumentNullException(nameof(observedOperation));
			if (observedChild == null) throw new ArgumentNullException(nameof(observedChild));
			if (nativeState == null) throw new ArgumentNullException(nameof(nativeState));
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			ValidatePlan(plan);
			if (!observedOperation.Identity.Equals(_operationStore.GetOperation(observedOperation.Identity)?.Identity))
				throw new InvalidOperationException("The observed C6.8/C6.9 Collection operation is no longer present in the durable journal.");
			if (!nativeState.Target.Equals(plan.Target))
				throw new ArgumentException("The verified native state belongs to a different Collection target.", nameof(nativeState));

			CollectionOperation operation = RequireOperation(observedOperation.Identity, plan,
				CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationPhase.Recovering);
			CollectionNativeChildOperation child = operation.NativeChildren.SingleOrDefault(x => x.Sequence == observedChild.Sequence);
			if (child == null || !Matches(child.NativeOperation, observedChild.NativeOperation))
				throw new InvalidOperationException("The verified C6.8/C6.9 native child no longer matches the durable Collection journal attempt.");
			if (child.NativeResult == null || child.NativeResult.Durability == ModOperationDurability.Unknown)
				throw new InvalidOperationException("C6.10 cannot reconcile provenance while native durability remains Unknown.");
			if (observedChild.NativeResult == null || observedChild.NativeResult.Durability != child.NativeResult.Durability)
				throw new InvalidOperationException("The supplied C6.8/C6.9 result does not match the durable child durability.");

			ResolvedCollectionMemberPlan member = plan.SelectedMembers.SingleOrDefault(x => x.MemberKey.Equals(child.Member.MemberKey));
			if (member == null || !child.Member.Revision.Equals(plan.Revision) ||
				!StringComparer.Ordinal.Equals(child.NativeOperation.Fingerprint.RecipeFingerprint, member.RecipeIdentity.Fingerprint))
				throw new InvalidOperationException("The durable native child does not map to the exact selected Collection member recipe.");

			if (child.IsReconciled)
			{
				if (child.NativeResult.Durability == ModOperationDurability.VerifiedCommitted)
				{
					CollectionNativeChildRecoveryManifest reconciledManifest = _manifestStore.GetManifest(operation, child);
					if (reconciledManifest == null || reconciledManifest.SafeBoundaryStateFingerprint == null)
						throw new InvalidDataException("A reconciled committed Collection child is missing its durable C6.10 safe-boundary fingerprint.");
				}
				return LoadReconciledResult(operation, child, plan, member);
			}
			if (child.Checkpoint != CollectionNativeChildCheckpoint.NativeTerminalObserved)
				throw new InvalidOperationException("C6.10 requires NativeTerminalObserved before provenance can be reconciled.");
			if (operation.CheckpointSequence == Int64.MaxValue)
				throw new InvalidOperationException("The Collection operation checkpoint sequence cannot be incremented further.");

			CollectionTargetAssociation association = null;
			CollectionMemberBinding binding = null;
			if (child.NativeResult.Durability == ModOperationDurability.VerifiedCommitted)
			{
				ValidateVerifiedNativeMod(verifiedNativeMod, nativeState, plan.Target);
				CollectionNativeChildRecoveryManifest terminalManifest = _manifestStore.GetManifest(operation, child);
				if (terminalManifest == null || terminalManifest.TerminalStateFingerprint == null ||
					!terminalManifest.TerminalStateFingerprint.Equals(nativeState.Fingerprint))
					throw new InvalidDataException("C6.10 requires the exact committed terminal native-state fingerprint retained by C6.8/C6.9.");
				association = ResolveExactAssociation(plan.Revision, plan.Target);
				ValidateAssociationBaseline(nativeState, plan.Revision, association);
				if (association == null)
				{
					// Use the durable operation ID so a retry after safe-boundary publication but before the atomic
					// association/journal transaction reconstructs the exact same intended provenance fingerprint.
					association = new CollectionTargetAssociation(operation.Identity.OperationId, plan.Revision, plan.Target, CollectionAssociationState.Incomplete);
				}
				else if (association.State != CollectionAssociationState.Incomplete)
					association = association.WithState(CollectionAssociationState.Incomplete);
				binding = new CollectionMemberBinding(association, member.MemberKey, verifiedNativeMod.Identity,
					member.RecipeIdentity, CollectionMemberBindingKind.InstalledForCollection);
			}

			if (association != null)
				PersistSafeBoundaryFingerprint(operation, child, nativeState, association, binding);

			var reconciledChild = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action,
				child.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, child.NativeResult);
			List<CollectionNativeChildOperation> children = operation.NativeChildren
				.Select(x => x.Sequence == child.Sequence ? reconciledChild : x).ToList();
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
				operation.ResultState, children);
			_associationStore.SaveChildReconciliation(updated, association, binding);

			CollectionOperation persisted = _operationStore.GetOperation(updated.Identity);
			if (persisted == null)
				throw new InvalidOperationException("The Collection operation disappeared after C6.10 provenance reconciliation.");
			CollectionNativeChildOperation persistedChild = persisted.NativeChildren.Single(x => x.Sequence == child.Sequence);
			CollectionTargetAssociation persistedAssociation = association == null ? null : _associationStore.GetAssociation(association.AssociationId);
			CollectionMemberBinding persistedBinding = persistedAssociation == null ? null : _associationStore.GetBindings(persistedAssociation.AssociationId)
				.SingleOrDefault(x => x.MemberKey.Equals(member.MemberKey));
			return new CollectionAssociationReconciliationResult(persisted, persistedChild, persistedAssociation, persistedBinding);
		}


		private static void ValidateAssociationBaseline(CollectionNativeStateIndex terminalState,
			CollectionRevisionIdentity revision, CollectionTargetAssociation liveAssociation)
		{
			List<CollectionTargetAssociation> observed = terminalState.Associations.Values.Where(x => x.Revision.Equals(revision)).ToList();
			if (liveAssociation == null)
			{
				if (observed.Count != 0)
					throw new InvalidOperationException("Collection association state changed after C6.8/C6.9 verification and before C6.10 reconciliation.");
				return;
			}
			if (observed.Count != 1 || observed[0].AssociationId != liveAssociation.AssociationId || observed[0].State != liveAssociation.State)
				throw new InvalidOperationException("Collection association state changed after C6.8/C6.9 verification and before C6.10 reconciliation.");
		}

		private void PersistSafeBoundaryFingerprint(CollectionOperation operation, CollectionNativeChildOperation child,
			CollectionNativeStateIndex terminalState, CollectionTargetAssociation association, CollectionMemberBinding binding)
		{
			CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, child);
			if (manifest == null || manifest.TerminalStateFingerprint == null ||
				!manifest.TerminalStateFingerprint.Equals(terminalState.Fingerprint))
				throw new InvalidDataException("The committed child recovery manifest no longer matches the C6.8/C6.9 terminal state.");

			CollectionCurrentStateFingerprint safeBoundary = BuildPostReconciliationState(terminalState, association, binding).Fingerprint;
			if (manifest.SafeBoundaryStateFingerprint != null)
			{
				if (!manifest.SafeBoundaryStateFingerprint.Equals(safeBoundary))
					throw new InvalidDataException("The retained C6.10 safe-boundary fingerprint contradicts the deterministic provenance reconciliation result.");
				return;
			}
			_manifestStore.SaveManifest(manifest.WithSafeBoundaryStateFingerprint(safeBoundary));
		}

		private static CollectionNativeStateIndex BuildPostReconciliationState(CollectionNativeStateIndex terminalState,
			CollectionTargetAssociation association, CollectionMemberBinding binding)
		{
			List<CollectionTargetAssociation> associations = terminalState.Associations.Values
				.Where(x => x.AssociationId != association.AssociationId).ToList();
			associations.Add(association);

			List<CollectionMemberBinding> bindings = terminalState.BindingsByAssociation.Values.SelectMany(x => x)
				.Where(x => x.Association.AssociationId != association.AssociationId || !x.MemberKey.Equals(binding.MemberKey)).ToList();
			bindings.Add(binding);
			List<UserOverride> overrides = terminalState.OverridesByAssociation.Values.SelectMany(x => x).ToList();

			return new CollectionNativeStateIndex(terminalState.Target, terminalState.Roots, terminalState.Mods.Values,
				terminalState.Files.Values, terminalState.IniEdits.Values, terminalState.GameValues.Values, terminalState.Plugins.Values,
				terminalState.PluginCoverage, associations, bindings, overrides, terminalState.AssociationCoverage, terminalState.Issues,
				terminalState.DeploymentCommitSequence);
		}

		private CollectionAssociationReconciliationResult LoadReconciledResult(CollectionOperation operation,
			CollectionNativeChildOperation child, ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member)
		{
			if (child.NativeResult.Durability != ModOperationDurability.VerifiedCommitted)
				return new CollectionAssociationReconciliationResult(operation, child, null, null);
			CollectionTargetAssociation association = ResolveExactAssociation(plan.Revision, plan.Target);
			if (association == null)
				throw new InvalidOperationException("A reconciled committed Collection child is missing its target association provenance.");
			CollectionMemberBinding binding = _associationStore.GetBindings(association.AssociationId)
				.SingleOrDefault(x => x.MemberKey.Equals(member.MemberKey));
			if (binding == null || !binding.VerifiedRecipe.Equals(member.RecipeIdentity))
				throw new InvalidOperationException("A reconciled committed Collection child is missing its exact verified member binding.");
			return new CollectionAssociationReconciliationResult(operation, child, association, binding);
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, ResolvedCollectionPlan plan,
			params CollectionOperationPhase[] allowedPhases)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null)
				throw new InvalidOperationException("The Collection operation is not present in the durable operation journal.");
			if (operation.Kind != CollectionOperationKind.ApplyResolvedPlan || operation.ResultState != CollectionOperationResultState.Pending ||
				!allowedPhases.Contains(operation.Phase))
				throw new InvalidOperationException("C6.10 requires an active additive Collection operation at the expected reconciliation phase.");
			if (operation.PlanIdentity == null || !operation.PlanIdentity.Equals(plan.Identity) || operation.Revision == null ||
				!operation.Revision.Equals(plan.Revision) || !operation.Target.Equals(plan.Target) || !operation.Collection.Equals(plan.Revision.Collection))
				throw new InvalidOperationException("The C6.10 inputs do not belong to the exact durable Collection operation plan.");
			return operation;
		}

		private CollectionTargetAssociation ResolveExactAssociation(CollectionRevisionIdentity revision,
			CollectionTargetIdentity target)
		{
			List<CollectionTargetAssociation> matches = _associationStore.GetAssociationsForTarget(target)
				.Where(x => x.Revision.Equals(revision)).ToList();
			if (matches.Count > 1)
				throw new InvalidOperationException("Multiple Collection associations exist for the same exact revision/target baseline; provenance is ambiguous.");
			return matches.Count == 0 ? null : matches[0];
		}

		private static bool HasAppliedVerifiedBindingEvidence(CollectionMemberMatchResult match,
			NativeModInstanceIdentity nativeMod, CollectionRecipeIdentity recipe)
		{
			return match.ExistingBindings.Any(x => x.NativeMod.Equals(nativeMod) && x.VerifiedRecipe.Equals(recipe) &&
				x.Association.State == CollectionAssociationState.Applied);
		}

		private static void ValidateVerifiedNativeMod(CollectionNativeModState verifiedNativeMod,
			CollectionNativeStateIndex nativeState, CollectionTargetIdentity target)
		{
			if (verifiedNativeMod == null)
				throw new InvalidOperationException("VerifiedCommitted C6.10 reconciliation requires the native mod instance proven by C6.8/C6.9.");
			if (!verifiedNativeMod.Identity.Target.Equals(target))
				throw new InvalidOperationException("The verified native mod belongs to a different target.");
			CollectionNativeModState indexed;
			if (!nativeState.Mods.TryGetValue(verifiedNativeMod.Identity, out indexed) || !SameNativeMod(indexed, verifiedNativeMod))
				throw new InvalidOperationException("The supplied C6.8/C6.9 verified native instance is absent from its authoritative state snapshot.");
		}

		private static bool SameNativeMod(CollectionNativeModState left, CollectionNativeModState right)
		{
			return left != null && right != null && left.Identity.Equals(right.Identity) &&
				StringComparer.OrdinalIgnoreCase.Equals(left.ArchivePath, right.ArchivePath) &&
				StringComparer.OrdinalIgnoreCase.Equals(left.FileName, right.FileName) &&
				StringComparer.Ordinal.Equals(left.NexusModId, right.NexusModId) &&
				StringComparer.Ordinal.Equals(left.NexusFileId, right.NexusFileId) &&
				left.InstallMethod == right.InstallMethod && left.InstallRoot == right.InstallRoot;
		}

		private static void AddProvenanceCandidate(IDictionary<NativeModInstanceIdentity, NativeModProvenance> candidates,
			NativeModInstanceIdentity nativeMod, StandaloneModUse standaloneUse)
		{
			NativeModProvenance existing;
			if (candidates.TryGetValue(nativeMod, out existing) &&
				existing.StandaloneUse == StandaloneModUse.ExplicitStandaloneUse)
				return;
			if (existing != null && standaloneUse != StandaloneModUse.ExplicitStandaloneUse)
				return;
			candidates[nativeMod] = new NativeModProvenance(nativeMod, standaloneUse);
		}

		private static bool Matches(ModOperationIdentity left, ModOperationIdentity right)
		{
			return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId &&
				left.Origin == right.Origin && left.Fingerprint.Equals(right.Fingerprint);
		}

		private static void ValidatePlan(ResolvedCollectionPlan plan)
		{
			if (plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new ArgumentException("C6.10 only reconciles additive InstallIntoCurrentSetup operations.", nameof(plan));
		}
	}

	/// <summary>Result of reconciling one independently verified native child into Collection provenance.</summary>
	public sealed class CollectionAssociationReconciliationResult
	{
		internal CollectionAssociationReconciliationResult(CollectionOperation operation, CollectionNativeChildOperation child,
			CollectionTargetAssociation association, CollectionMemberBinding binding)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Child = child ?? throw new ArgumentNullException(nameof(child));
			Association = association;
			Binding = binding;
		}

		public CollectionOperation Operation { get; }
		public CollectionNativeChildOperation Child { get; }
		public CollectionTargetAssociation Association { get; }
		public CollectionMemberBinding Binding { get; }
		public bool CreatedOrUpdatedBinding { get { return Binding != null; } }
	}

	/// <summary>Result of publishing the complete applied Collection association after provenance reconciliation.</summary>
	public sealed class CollectionAssociationFinalizationResult
	{
		private readonly ReadOnlyCollection<CollectionMemberBinding> _bindings;

		internal CollectionAssociationFinalizationResult(CollectionOperation operation, CollectionTargetAssociation association,
			IEnumerable<CollectionMemberBinding> bindings)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Association = association ?? throw new ArgumentNullException(nameof(association));
			_bindings = new ReadOnlyCollection<CollectionMemberBinding>((bindings ?? throw new ArgumentNullException(nameof(bindings))).ToList());
		}

		public CollectionOperation Operation { get; }
		public CollectionTargetAssociation Association { get; }
		public ReadOnlyCollection<CollectionMemberBinding> Bindings { get { return _bindings; } }
	}
}
