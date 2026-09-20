using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Coordinates the durable C6 Collection operation lifecycle without executing native children.
	/// </summary>
	/// <remarks>
	/// C6.5 owns legal collection-level state transitions and exact plan/review correlation only. C6.6 prepares durable
	/// child intent/recovery inputs, C6.7 submits native work and C6.8-C6.9 verify/reconcile native outcomes.
	/// </remarks>
	public sealed class CollectionOperationCoordinator
	{
		private const string PersistedPlanPayloadFormat = "nmm-ce.collections.coordinator-plan/1";
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;

		/// <summary>
		/// Creates a coordinator over existing Collections operation and resolved-plan stores.
		/// </summary>
		public CollectionOperationCoordinator(CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore)
		{
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
		}

		/// <summary>
		/// Creates and durably records a new additive apply operation in the Created phase.
		/// </summary>
		public CollectionOperation CreateApplyOperation(CollectionIdentity collection, CollectionTargetIdentity target)
		{
			if (collection == null)
				throw new ArgumentNullException(nameof(collection));
			if (target == null)
				throw new ArgumentNullException(nameof(target));

			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(),
				CollectionOperationKind.ApplyResolvedPlan, collection, target, null, null, 1,
				CollectionOperationPhase.Created, CollectionOperationResultState.Pending,
				new CollectionNativeChildOperation[0]);
			_operationStore.SaveOperation(operation);
			return operation;
		}

		/// <summary>
		/// Creates the identity for the next immutable plan snapshot associated with an operation.
		/// </summary>
		/// <remarks>
		/// A plan identity is not persisted or approved by this call. It becomes durable only when a ready review snapshot is recorded.
		/// </remarks>
		public CollectionPlanIdentity CreateNextPlanIdentity(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.Preparing);
			RequireBeforeNativeBoundary(current, "A Collection plan cannot be regenerated after the native mutation boundary has been crossed.");
			return current.PlanIdentity == null
				? CollectionPlanIdentity.From(Guid.NewGuid(), 1)
				: current.PlanIdentity.NextVersion();
		}

		/// <summary>Moves Created to Resolving.</summary>
		public CollectionOperation BeginResolving(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.Created);
			return Advance(current, CollectionOperationPhase.Resolving, CollectionOperationResultState.Pending,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Attaches the concrete revision selected during resolution and enters Preparing.</summary>
		public CollectionOperation BeginPreparing(CollectionOperationIdentity operationIdentity, CollectionRevisionIdentity revision)
		{
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.Resolving);
			if (!revision.Collection.Equals(current.Collection))
				throw new ArgumentException("The resolved revision must belong to the operation Collection.", nameof(revision));
			if (current.Revision != null && !current.Revision.Equals(revision))
				throw new InvalidOperationException("A Collection operation cannot change its concrete revision once resolved.");
			return Advance(current, CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending,
				revision, current.PlanIdentity);
		}

		/// <summary>Pauses resolution/preparation for explicit user or external input before native mutation.</summary>
		public CollectionOperation AwaitInput(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequireAnyPhase(current, CollectionOperationPhase.Resolving, CollectionOperationPhase.Preparing);
			RequireBeforeNativeBoundary(current, "Awaiting pre-apply input is not a valid transition after native mutation began.");
			return Advance(current, CollectionOperationPhase.AwaitingInput, CollectionOperationResultState.Pending,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Begins revalidation after previously awaited input has been satisfied.</summary>
		public CollectionOperation BeginRevalidation(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.AwaitingInput);
			RequireBeforeNativeBoundary(current, "Pre-apply revalidation cannot start after native mutation began.");
			return Advance(current, CollectionOperationPhase.Revalidating, CollectionOperationResultState.Pending,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Returns an input-revalidated operation to Resolving when no concrete revision has been selected yet.</summary>
		public CollectionOperation ResumeResolving(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.Revalidating);
			if (current.Revision != null)
				throw new InvalidOperationException("A Collection operation with a concrete revision must resume preparation rather than resolution.");
			return Advance(current, CollectionOperationPhase.Resolving, CollectionOperationResultState.Pending,
				null, current.PlanIdentity);
		}

		/// <summary>Returns an input-revalidated operation to Preparing.</summary>
		public CollectionOperation ResumePreparing(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.Revalidating);
			if (current.Revision == null)
				throw new InvalidOperationException("A Collection operation cannot resume preparation before a concrete revision is resolved.");
			return Advance(current, CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>
		/// Returns a reviewed, still pre-apply operation to Preparing so changed choices/prerequisites can produce a new plan version.
		/// </summary>
		public CollectionOperation ReopenPreparation(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequireAnyPhase(current, CollectionOperationPhase.ReadyForReview, CollectionOperationPhase.ReadyToApply);
			RequireBeforeNativeBoundary(current, "A reviewed Collection plan cannot be reopened for replanning after native mutation began.");
			return Advance(current, CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>
		/// Persists the exact ready C6.1-C6.4 review snapshot and moves Preparing to ReadyForReview.
		/// </summary>
		public CollectionOperation MarkReadyForReview(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));
			if (dependencyPlan == null)
				throw new ArgumentNullException(nameof(dependencyPlan));
			if (impactPlan == null)
				throw new ArgumentNullException(nameof(impactPlan));

			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.Preparing);
			RequireBeforeNativeBoundary(current, "A Collection review plan cannot be replaced after native mutation began.");
			ValidateReviewInputs(current, plan, dependencyPlan, impactPlan);

			byte[] payload = SerializeReviewSnapshot(plan, dependencyPlan, impactPlan);
			_planStore.SavePlan(plan, PersistedPlanPayloadFormat, payload);
			return Advance(current, CollectionOperationPhase.ReadyForReview, CollectionOperationResultState.Pending,
				plan.Revision, plan.Identity);
		}

		/// <summary>Records explicit approval of the exact reviewed plan and moves to ReadyToApply.</summary>
		public CollectionOperation MarkReadyToApply(CollectionOperationIdentity operationIdentity, CollectionPlanIdentity expectedPlan)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.ReadyForReview);
			RequireExactPlan(current, expectedPlan);
			return Advance(current, CollectionOperationPhase.ReadyToApply, CollectionOperationResultState.Pending,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>
		/// Enters ApplyingNativeChildren for the exact approved plan. C6.5 does not create or submit a native child.
		/// </summary>
		public CollectionOperation BeginApplying(CollectionOperationIdentity operationIdentity, CollectionPlanIdentity expectedPlan)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.ReadyToApply);
			RequireExactPlan(current, expectedPlan);
			RequireBeforeNativeBoundary(current, "Applying cannot begin from a journal snapshot that already crossed the native mutation boundary.");
			return Advance(current, CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Records a safe native-child boundary pause without claiming that prior committed work was undone.</summary>
		public CollectionOperation PauseAtSafeBoundary(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.ApplyingNativeChildren);
			return Advance(current, CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Resumes native-child coordination from an explicitly safe boundary.</summary>
		public CollectionOperation ResumeApplying(CollectionOperationIdentity operationIdentity, CollectionPlanIdentity expectedPlan)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.PausedAtSafeBoundary);
			RequireExactPlan(current, expectedPlan);
			return Advance(current, CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Moves from native-child coordination to authoritative final verification.</summary>
		public CollectionOperation BeginVerifying(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.ApplyingNativeChildren);
			return Advance(current, CollectionOperationPhase.Verifying, CollectionOperationResultState.Pending,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Enters active recovery/reconciliation after native mutation may have occurred.</summary>
		public CollectionOperation BeginRecovery(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequireAnyPhase(current, CollectionOperationPhase.ApplyingNativeChildren,
				CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationPhase.Verifying,
				CollectionOperationPhase.RecoveryRequired);
			if (!current.HasCrossedNativeBoundary)
				throw new InvalidOperationException("Collection recovery is only entered after this operation crossed the native mutation boundary.");
			return Advance(current, CollectionOperationPhase.Recovering, CollectionOperationResultState.Pending,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Persists an explicit non-terminal RecoveryRequired state for unresolved native durability/reconciliation.</summary>
		public CollectionOperation MarkRecoveryRequired(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequireAnyPhase(current, CollectionOperationPhase.ApplyingNativeChildren,
				CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationPhase.Verifying,
				CollectionOperationPhase.Recovering);
			if (!current.HasCrossedNativeBoundary)
				throw new InvalidOperationException("RecoveryRequired cannot be used as a generic pre-apply failure state.");
			if (!current.HasUnknownNativeDurability && !current.HasUnreconciledNativeChild)
				throw new InvalidOperationException("RecoveryRequired requires unresolved native durability or unreconciled submitted work.");
			return Advance(current, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Completes a fully verified operation as committed.</summary>
		public CollectionOperation CompleteCommitted(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.Verifying);
			RequireFullyReconciledNativeState(current, "A Collection operation cannot commit while native child durability or reconciliation remains unresolved.");
			foreach (CollectionNativeChildOperation child in current.NativeChildren)
				if (!child.IsReconciled)
					throw new InvalidOperationException("A Collection operation cannot commit while a prepared native child has not reached its reconciled checkpoint.");
			return Advance(current, CollectionOperationPhase.Completed, CollectionOperationResultState.Committed,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Completes cancellation before any native child crossed the mutation boundary.</summary>
		public CollectionOperation CompleteCancelledBeforeApply(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePreApplyTerminalPhase(current);
			RequireBeforeNativeBoundary(current, "CancelledBeforeApply cannot hide native work that already crossed the mutation boundary.");
			return Advance(current, CollectionOperationPhase.Completed, CollectionOperationResultState.CancelledBeforeApply,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Completes a preparation failure before any native child crossed the mutation boundary.</summary>
		public CollectionOperation CompleteFailedBeforeApply(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePreApplyTerminalPhase(current);
			RequireBeforeNativeBoundary(current, "FailedBeforeApply cannot hide native work that already crossed the mutation boundary.");
			return Advance(current, CollectionOperationPhase.Completed, CollectionOperationResultState.FailedBeforeApply,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Completes a deliberately stopped partial operation after all submitted native reality is reconciled.</summary>
		public CollectionOperation CompleteStoppedPartial(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequireAnyPhase(current, CollectionOperationPhase.ApplyingNativeChildren,
				CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationPhase.Verifying,
				CollectionOperationPhase.Recovering);
			if (!current.HasCrossedNativeBoundary)
				throw new InvalidOperationException("StoppedPartial requires native work to have crossed the mutation boundary.");
			RequireFullyReconciledNativeState(current, "StoppedPartial requires every submitted native child to have known, reconciled durability.");
			return Advance(current, CollectionOperationPhase.Completed, CollectionOperationResultState.StoppedPartial,
				current.Revision, current.PlanIdentity);
		}

		/// <summary>Completes recovery after the protected rollback result was independently verified.</summary>
		public CollectionOperation CompleteRolledBack(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation current = RequireCurrent(operationIdentity);
			RequirePhase(current, CollectionOperationPhase.Recovering);
			if (!current.HasCrossedNativeBoundary)
				throw new InvalidOperationException("RolledBack requires an operation that previously crossed the native mutation boundary.");
			RequireFullyReconciledNativeState(current, "RolledBack requires every submitted native child to have known, reconciled durability.");
			return Advance(current, CollectionOperationPhase.Completed, CollectionOperationResultState.RolledBack,
				current.Revision, current.PlanIdentity);
		}

		private CollectionOperation RequireCurrent(CollectionOperationIdentity identity)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));
			CollectionOperation current = _operationStore.GetOperation(identity);
			if (current == null)
				throw new InvalidOperationException("The Collection operation is not present in the durable operation journal.");
			return current;
		}

		private CollectionOperation Advance(CollectionOperation current, CollectionOperationPhase phase,
			CollectionOperationResultState resultState, CollectionRevisionIdentity revision, CollectionPlanIdentity planIdentity)
		{
			if (current.CheckpointSequence == Int64.MaxValue)
				throw new InvalidOperationException("The Collection operation checkpoint sequence cannot be incremented further.");
			var next = new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target,
				revision, planIdentity, current.CheckpointSequence + 1, phase, resultState, current.NativeChildren);
			_operationStore.SaveOperation(next);
			return next;
		}

		private static void ValidateReviewInputs(CollectionOperation operation, ResolvedCollectionPlan plan,
			CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan)
		{
			if (operation.Kind != CollectionOperationKind.ApplyResolvedPlan)
				throw new InvalidOperationException("C6.5 currently coordinates only the additive ApplyResolvedPlan workflow.");
			if (plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new ArgumentException("C6.5 additive coordination does not authorize replacement policy.", nameof(plan));
			if (operation.Revision == null || !operation.Revision.Equals(plan.Revision) ||
				!operation.Collection.Equals(plan.Revision.Collection) || !operation.Target.Equals(plan.Target))
				throw new ArgumentException("The resolved plan must match the exact operation Collection, revision and target.", nameof(plan));
			if (!dependencyPlan.PlanIdentity.Equals(plan.Identity) || !impactPlan.PlanIdentity.Equals(plan.Identity) ||
				!dependencyPlan.Target.Equals(plan.Target) || !impactPlan.Target.Equals(plan.Target) ||
				!dependencyPlan.StateFingerprint.Equals(plan.CurrentStateFingerprint) ||
				!impactPlan.StateFingerprint.Equals(plan.CurrentStateFingerprint))
				throw new ArgumentException("The C6.3/C6.4 review inputs must belong to the exact resolved plan and native-state fingerprint.");
			if (!dependencyPlan.IsReady)
				throw new InvalidOperationException("A blocked dependency/phase plan cannot enter ReadyForReview.");
			if (!impactPlan.IsReady)
				throw new InvalidOperationException("A conflict/impact plan requiring preparation, action or blocking cannot enter ReadyForReview.");
			if (operation.PlanIdentity != null && operation.PlanIdentity.PlanId == plan.Identity.PlanId &&
				plan.Identity.Version < operation.PlanIdentity.Version)
				throw new InvalidOperationException("A Collection operation cannot attach an older version of the same logical resolved plan.");
		}

		private static void RequireExactPlan(CollectionOperation operation, CollectionPlanIdentity expectedPlan)
		{
			if (expectedPlan == null)
				throw new ArgumentNullException(nameof(expectedPlan));
			if (operation.PlanIdentity == null || !operation.PlanIdentity.Equals(expectedPlan))
				throw new InvalidOperationException("The requested transition does not refer to the exact currently reviewed Collection plan snapshot.");
		}

		private static void RequireBeforeNativeBoundary(CollectionOperation operation, string message)
		{
			if (operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException(message);
		}

		private static void RequireFullyReconciledNativeState(CollectionOperation operation, string message)
		{
			if (operation.HasUnknownNativeDurability || operation.HasUnreconciledNativeChild)
				throw new InvalidOperationException(message);
		}

		private static void RequirePreApplyTerminalPhase(CollectionOperation operation)
		{
			switch (operation.Phase)
			{
				case CollectionOperationPhase.Created:
				case CollectionOperationPhase.Resolving:
				case CollectionOperationPhase.Preparing:
				case CollectionOperationPhase.AwaitingInput:
				case CollectionOperationPhase.Revalidating:
				case CollectionOperationPhase.ReadyForReview:
				case CollectionOperationPhase.ReadyToApply:
				case CollectionOperationPhase.ApplyingNativeChildren:
				case CollectionOperationPhase.PausedAtSafeBoundary:
					return;
				default:
					throw new InvalidOperationException("This Collection operation phase cannot terminate as a before-apply result.");
			}
		}

		private static void RequirePhase(CollectionOperation operation, CollectionOperationPhase required)
		{
			if (operation.Phase != required)
				throw new InvalidOperationException("Invalid Collection operation transition from " + operation.Phase + "; expected " + required + ".");
		}

		private static void RequireAnyPhase(CollectionOperation operation, params CollectionOperationPhase[] allowed)
		{
			foreach (CollectionOperationPhase phase in allowed)
				if (operation.Phase == phase)
					return;
			throw new InvalidOperationException("Invalid Collection operation transition from " + operation.Phase + ".");
		}

		private static byte[] SerializeReviewSnapshot(ResolvedCollectionPlan plan,
			CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan)
		{
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				writer.Write(1);
				WriteRevision(writer, plan.Revision);
				writer.Write(plan.Target.Fingerprint);
				writer.Write((int)plan.Policy.Kind);
				writer.Write((int)plan.Policy.ReplacementBackupChoice);
				writer.Write(plan.CurrentStateFingerprint.FormatVersion);
				writer.Write(plan.CurrentStateFingerprint.Value);
				writer.Write((int)plan.ManifestSource.ContentHash.Algorithm);
				writer.Write(plan.ManifestSource.ContentHash.Value);
				writer.Write(plan.ManifestSource.ByteLength);
				writer.Write(plan.ManifestSource.SchemaIdentity);
				writer.Write(plan.ManifestSource.NormalizerVersion);

				writer.Write(plan.SelectedMembers.Count);
				foreach (ResolvedCollectionMemberPlan member in plan.SelectedMembers)
				{
					WriteMemberKey(writer, member.MemberKey);
					writer.Write(member.SourceOrdinal);
					writer.Write((int)member.Requirement);
					writer.Write(member.InstallationPhase);
					writer.Write(member.RecipeIdentity.Fingerprint);
					writer.Write((int)member.ArtifactChoice.Kind);
					WriteArtifact(writer, member.ArtifactChoice.RequestedArtifact);
					WriteArtifact(writer, member.ArtifactChoice.SelectedArtifact);
					WriteNullableString(writer, member.ArtifactChoice.SubstitutionRuleId);
				}

				List<CollectionMemberDependency> dependencies = plan.CapabilityReport.Manifest.Dependencies
					.OrderBy(x => x.PrerequisiteMemberKey.Kind).ThenBy(x => x.PrerequisiteMemberKey.Value, StringComparer.Ordinal)
					.ThenBy(x => x.DependentMemberKey.Kind).ThenBy(x => x.DependentMemberKey.Value, StringComparer.Ordinal)
					.ThenBy(x => x.Kind).ToList();
				writer.Write(dependencies.Count);
				foreach (CollectionMemberDependency dependency in dependencies)
				{
					WriteMemberKey(writer, dependency.PrerequisiteMemberKey);
					WriteMemberKey(writer, dependency.DependentMemberKey);
					writer.Write((int)dependency.Kind);
				}

				List<CollectionFilePriorityRule> priorityRules = plan.CapabilityReport.Manifest.FilePriorityRules
					.OrderBy(x => x.LowerPriorityMemberKey.Kind).ThenBy(x => x.LowerPriorityMemberKey.Value, StringComparer.Ordinal)
					.ThenBy(x => x.HigherPriorityMemberKey.Kind).ThenBy(x => x.HigherPriorityMemberKey.Value, StringComparer.Ordinal).ToList();
				writer.Write(priorityRules.Count);
				foreach (CollectionFilePriorityRule rule in priorityRules)
				{
					WriteMemberKey(writer, rule.LowerPriorityMemberKey);
					WriteMemberKey(writer, rule.HigherPriorityMemberKey);
				}

				writer.Write(dependencyPlan.Phases.Count);
				foreach (CollectionExecutionPhase phase in dependencyPlan.Phases)
				{
					writer.Write(phase.PhaseNumber);
					writer.Write(phase.Members.Count);
					foreach (CollectionPlannedPhaseMember member in phase.Members)
					{
						WriteMemberKey(writer, member.MemberKey);
						WriteMatchEvidence(writer, member.Match);
					}
				}

				writer.Write(dependencyPlan.Barriers.Count);
				foreach (CollectionPhaseBarrier barrier in dependencyPlan.Barriers)
				{
					writer.Write(barrier.CompletedPhase);
					writer.Write(barrier.NextPhase);
					writer.Write((int)barrier.Kind);
				}

				List<CollectionFileImpact> files = impactPlan.FileImpacts.OrderBy(x => x.Target.Root)
					.ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
				writer.Write(files.Count);
				foreach (CollectionFileImpact impact in files)
				{
					WriteDeploymentTarget(writer, impact.Target);
					writer.Write(impact.Writers.Count);
					foreach (CollectionMemberKey writerKey in impact.Writers)
						WriteMemberKey(writer, writerKey);
					WriteNullableMemberKey(writer, impact.PlannedWinner);
					WriteNullableString(writer, impact.CurrentOwnerKey);
					WriteGuids(writer, impact.AffectedAssociationIds);
				}

				List<CollectionPluginImpact> plugins = impactPlan.PluginImpacts
					.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal)
					.ThenBy(x => x.Effect.Kind).ThenBy(x => String.Join("\u001f", x.Effect.PluginPaths), StringComparer.OrdinalIgnoreCase).ToList();
				writer.Write(plugins.Count);
				foreach (CollectionPluginImpact impact in plugins)
				{
					WriteMemberKey(writer, impact.MemberKey);
					writer.Write((int)impact.Effect.Kind);
					writer.Write(impact.Effect.PluginPaths.Count);
					foreach (string pluginPath in impact.Effect.PluginPaths)
						writer.Write(pluginPath);
					writer.Write(impact.Effect.Active.HasValue);
					if (impact.Effect.Active.HasValue) writer.Write(impact.Effect.Active.Value);
					writer.Write(impact.Effect.AbsoluteIndex.HasValue);
					if (impact.Effect.AbsoluteIndex.HasValue) writer.Write(impact.Effect.AbsoluteIndex.Value);
					WriteGuids(writer, impact.AffectedAssociationIds);
				}

				List<CollectionConfigurationImpact> configs = impactPlan.ConfigurationImpacts
					.OrderBy(x => x.Kind).ThenBy(x => x.SubjectKey, StringComparer.Ordinal)
					.ThenBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal).ToList();
				writer.Write(configs.Count);
				foreach (CollectionConfigurationImpact impact in configs)
				{
					WriteMemberKey(writer, impact.MemberKey);
					writer.Write((int)impact.Kind);
					writer.Write(impact.SubjectKey);
					WriteNullableString(writer, impact.CurrentOwnerKey);
					writer.Write(impact.ChangesRecordedValue);
					WriteGuids(writer, impact.AffectedAssociationIds);
				}

				List<CollectionAssociationImpact> associations = impactPlan.AssociationImpacts
					.OrderBy(x => x.Association.AssociationId).ToList();
				writer.Write(associations.Count);
				foreach (CollectionAssociationImpact impact in associations)
				{
					writer.Write(impact.Association.AssociationId.ToByteArray());
					writer.Write((int)impact.Kind);
				}

				writer.Flush();
				return stream.ToArray();
			}
		}

		private static void WriteMatchEvidence(BinaryWriter writer, CollectionMemberMatchResult match)
		{
			writer.Write((int)match.Disposition);
			writer.Write((int)match.Reason);

			List<CollectionNativeModState> candidates = match.NativeCandidates
				.OrderBy(x => x.Identity.Target.Fingerprint, StringComparer.Ordinal)
				.ThenBy(x => x.Identity.NativeModKey, StringComparer.Ordinal).ToList();
			writer.Write(candidates.Count);
			foreach (CollectionNativeModState candidate in candidates)
			{
				writer.Write(candidate.Identity.Target.Fingerprint);
				writer.Write(candidate.Identity.NativeModKey);
				writer.Write(candidate.NexusModId);
				writer.Write(candidate.NexusFileId);
				writer.Write((int)candidate.InstallRoot);
				writer.Write((int)candidate.InstallMethod);
			}

			List<CollectionMemberBinding> bindings = match.ExistingBindings
				.OrderBy(x => x.Association.AssociationId)
				.ThenBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal).ToList();
			writer.Write(bindings.Count);
			foreach (CollectionMemberBinding binding in bindings)
			{
				writer.Write(binding.Association.AssociationId.ToByteArray());
				writer.Write((int)binding.Association.State);
				WriteMemberKey(writer, binding.MemberKey);
				writer.Write(binding.NativeMod.Target.Fingerprint);
				writer.Write(binding.NativeMod.NativeModKey);
				writer.Write(binding.VerifiedRecipe.Fingerprint);
				writer.Write((int)binding.BindingKind);
			}

			CollectionVerifiedArchive archive = match.VerifiedArchive;
			writer.Write(archive != null);
			if (archive != null)
			{
				writer.Write(archive.Artifact.ArtifactId);
				writer.Write((int)archive.Artifact.ContentHash.Algorithm);
				writer.Write(archive.Artifact.ContentHash.Value);
				writer.Write(archive.Artifact.ByteLength);
				writer.Write((int)archive.SourceKind);
				writer.Write((int)archive.VerificationBasis);
			}
		}

		private static void WriteRevision(BinaryWriter writer, CollectionRevisionIdentity revision)
		{
			writer.Write((int)revision.Collection.Origin);
			writer.Write(revision.Collection.StableId);
			writer.Write(revision.StableRevisionId);
			writer.Write(revision.NexusRevisionNumber.HasValue);
			if (revision.NexusRevisionNumber.HasValue)
				writer.Write(revision.NexusRevisionNumber.Value);
		}

		private static void WriteMemberKey(BinaryWriter writer, CollectionMemberKey key)
		{
			writer.Write((int)key.Kind);
			writer.Write(key.Value);
		}

		private static void WriteNullableMemberKey(BinaryWriter writer, CollectionMemberKey key)
		{
			writer.Write(key != null);
			if (key != null)
				WriteMemberKey(writer, key);
		}

		private static void WriteArtifact(BinaryWriter writer, CollectionArtifactReference artifact)
		{
			writer.Write(artifact.Scheme);
			writer.Write(artifact.StableId);
			writer.Write(artifact.ExpectedContentHash != null);
			if (artifact.ExpectedContentHash != null)
			{
				writer.Write((int)artifact.ExpectedContentHash.Algorithm);
				writer.Write(artifact.ExpectedContentHash.Value);
			}
		}

		private static void WriteDeploymentTarget(BinaryWriter writer, ModDeploymentTarget target)
		{
			writer.Write((int)target.Root);
			writer.Write(target.RelativePath);
		}

		private static void WriteNullableString(BinaryWriter writer, string value)
		{
			writer.Write(value != null);
			if (value != null)
				writer.Write(value);
		}

		private static void WriteGuids(BinaryWriter writer, IEnumerable<Guid> values)
		{
			List<Guid> ordered = values.OrderBy(x => x).ToList();
			writer.Write(ordered.Count);
			foreach (Guid value in ordered)
				writer.Write(value.ToByteArray());
		}
	}
}
