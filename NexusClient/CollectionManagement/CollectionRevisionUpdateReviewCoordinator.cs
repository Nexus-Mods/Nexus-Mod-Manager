using System;
using System.IO;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// C10.2 durable review/approval coordinator for an existing association moving to a candidate Collection revision.
	/// </summary>
	/// <remarks>
	/// This coordinator deliberately stops at ReadyToApply. It does not acquire archives, prepare missing native recipes,
	/// remove old effects, install candidate members, repair native state or publish the new association revision.
	/// </remarks>
	public sealed class CollectionRevisionUpdateReviewCoordinator
	{
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionsAssociationStore _associationStore;

		public CollectionRevisionUpdateReviewCoordinator(CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore)
		{
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
		}

		/// <summary>Persists the exact C10.1 comparison as a durable review and creates an UpdateRevision journal operation.</summary>
		public CollectionOperation CreateReviewedOperation(CollectionRevisionUpdatePlan plan)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			ValidateCurrentAssociation(plan.Association);
			CollectionRevisionUpdateReviewedIntent intent = CollectionRevisionUpdateReviewedIntent.Create(plan);
			byte[] payload = CollectionRevisionUpdateReviewedIntentCodec.Serialize(intent);
			_planStore.SavePlan(plan.NewPlan, CollectionRevisionUpdateReviewedIntentCodec.PayloadFormat, payload);
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.UpdateRevision,
				plan.NewPlan.Revision.Collection, plan.NewPlan.Target, plan.NewPlan.Revision, plan.NewPlan.Identity, 1,
				CollectionOperationPhase.ReadyForReview, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
			_operationStore.SaveOperation(operation);
			return operation;
		}

		/// <summary>
		/// Records explicit approval only when a freshly recomputed C10.1 plan still exactly matches the persisted review.
		/// Existing C9 override identities are thereby frozen as customization to preserve; unresolved drift/ambiguity blocks approval.
		/// </summary>
		public CollectionOperation Approve(CollectionOperationIdentity identity, CollectionPlanIdentity expectedPlan,
			CollectionRevisionUpdatePlan currentPlan)
		{
			CollectionOperation operation = RequireOperation(identity, CollectionOperationPhase.ReadyForReview);
			CollectionRevisionUpdateReviewedIntent intent = RequireIntent(operation, expectedPlan);
			if (currentPlan == null) throw new ArgumentNullException(nameof(currentPlan));
			ValidateCurrentAssociation(currentPlan.Association);
			intent.ValidateCurrentPlan(currentPlan);
			if (!intent.IsApprovable)
				throw new InvalidOperationException("The revision-update review still contains unresolved drift, ambiguous native state or unsafe removal decisions.");
			return Save(operation, CollectionOperationPhase.ReadyToApply, CollectionOperationResultState.Pending);
		}

		/// <summary>
		/// Revalidates an already approved review before C10.3 acquisition/preparation. This does not advance the operation
		/// and remains valid only while the exact UpdateRevision operation is still at ReadyToApply.
		/// </summary>
		public CollectionRevisionUpdateReviewedIntent ValidateApprovedPlan(CollectionOperationIdentity identity,
			CollectionPlanIdentity expectedPlan, CollectionRevisionUpdatePlan currentPlan)
		{
			CollectionOperation operation = RequireOperation(identity, CollectionOperationPhase.ReadyToApply);
			CollectionRevisionUpdateReviewedIntent intent = RequireIntent(operation, expectedPlan);
			ValidateApprovedCurrentPlan(intent, currentPlan);
			return intent;
		}

		/// <summary>
		/// Persists a pre-mutation AwaitingInput boundary before C10.3 queues Premium/bundled work or exposes manual acquisition.
		/// </summary>
		public CollectionOperation AwaitPreparationInput(CollectionOperationIdentity identity, CollectionPlanIdentity expectedPlan,
			CollectionRevisionUpdatePlan currentPlan)
		{
			CollectionOperation operation = RequireOperation(identity, CollectionOperationPhase.ReadyToApply);
			CollectionRevisionUpdateReviewedIntent intent = RequireIntent(operation, expectedPlan);
			ValidateApprovedCurrentPlan(intent, currentPlan);
			if (operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException("Revision-update acquisition cannot pause for input after native mutation began.");
			return Save(operation, CollectionOperationPhase.AwaitingInput, CollectionOperationResultState.Pending);
		}

		/// <summary>
		/// Revalidates the exact approved old/current/new review after acquisition/user input and returns the operation to ReadyToApply.
		/// </summary>
		public CollectionRevisionUpdateReviewedIntent ResumeApprovedPreparationInput(CollectionOperationIdentity identity,
			CollectionPlanIdentity expectedPlan, CollectionRevisionUpdatePlan currentPlan)
		{
			CollectionOperation operation = RequireOperation(identity, CollectionOperationPhase.AwaitingInput);
			CollectionRevisionUpdateReviewedIntent intent = RequireIntent(operation, expectedPlan);
			ValidateApprovedCurrentPlan(intent, currentPlan);
			if (operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException("Revision-update acquisition cannot resume as pre-apply after native mutation began.");
			Save(operation, CollectionOperationPhase.ReadyToApply, CollectionOperationResultState.Pending);
			return intent;
		}

		private void ValidateApprovedCurrentPlan(CollectionRevisionUpdateReviewedIntent intent, CollectionRevisionUpdatePlan currentPlan)
		{
			if (currentPlan == null) throw new ArgumentNullException(nameof(currentPlan));
			ValidateCurrentAssociation(currentPlan.Association);
			intent.ValidateCurrentPlan(currentPlan);
			if (!intent.IsApprovable)
				throw new InvalidOperationException("The approved revision-update review now contains unresolved drift, ambiguity or unsafe removal decisions.");
		}

		/// <summary>Cancels an unapproved C10 update review without changing native or association state.</summary>
		public CollectionOperation CancelBeforeApply(CollectionOperationIdentity identity, CollectionPlanIdentity expectedPlan)
		{
			CollectionOperation operation = RequireOperation(identity, CollectionOperationPhase.ReadyForReview);
			RequireIntent(operation, expectedPlan);
			if (operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException("A revision update cannot be cancelled as pre-apply after native mutation has begun.");
			return Save(operation, CollectionOperationPhase.Completed, CollectionOperationResultState.CancelledBeforeApply);
		}

		/// <summary>Loads and validates the immutable reviewed intent for UI/restart use.</summary>
		public CollectionRevisionUpdateReviewedIntent LoadReviewedIntent(CollectionOperationIdentity identity)
		{
			CollectionOperation operation = RequireOperation(identity, null);
			if (operation.PlanIdentity == null) throw new InvalidDataException("The revision-update operation does not reference a reviewed plan.");
			return RequireIntent(operation, operation.PlanIdentity);
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionOperationPhase? requiredPhase)
		{
			if (identity == null) throw new ArgumentNullException(nameof(identity));
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null) throw new InvalidOperationException("The revision-update operation is not present in the durable journal.");
			if (operation.Kind != CollectionOperationKind.UpdateRevision)
				throw new InvalidOperationException("C10.2 accepts only UpdateRevision operation journal entries.");
			if (requiredPhase.HasValue && operation.Phase != requiredPhase.Value)
				throw new InvalidOperationException("Invalid revision-update operation phase " + operation.Phase + "; expected " + requiredPhase.Value + ".");
			return operation;
		}

		private CollectionRevisionUpdateReviewedIntent RequireIntent(CollectionOperation operation, CollectionPlanIdentity expectedPlan)
		{
			if (expectedPlan == null) throw new ArgumentNullException(nameof(expectedPlan));
			if (operation.PlanIdentity == null || !operation.PlanIdentity.Equals(expectedPlan))
				throw new InvalidOperationException("Revision-update continuation does not refer to the exact reviewed candidate plan.");
			CollectionResolvedPlanRecord record = _planStore.GetPlan(expectedPlan);
			if (record == null || !StringComparer.Ordinal.Equals(record.PayloadFormat, CollectionRevisionUpdateReviewedIntentCodec.PayloadFormat))
				throw new InvalidDataException("The immutable revision-update reviewed intent is missing or uses an unsupported version.");
			CollectionRevisionUpdateReviewedIntent intent = CollectionRevisionUpdateReviewedIntentCodec.Deserialize(record.Payload);
			if (!intent.CandidatePlanIdentity.Equals(expectedPlan) || !intent.CandidateRevision.Equals(operation.Revision) ||
				!intent.Target.Equals(operation.Target) || !intent.CandidateRevision.Collection.Equals(operation.Collection) ||
				record.PolicyKind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup ||
				!record.Revision.Equals(intent.CandidateRevision) || !record.Target.Equals(intent.Target) ||
				!record.CurrentStateFingerprint.Equals(intent.ObservedStateFingerprint))
				throw new InvalidDataException("Persisted revision-update review metadata no longer matches its operation/plan record.");
			return intent;
		}


		private void ValidateCurrentAssociation(CollectionTargetAssociation association)
		{
			if (association == null) throw new ArgumentNullException(nameof(association));
			CollectionTargetAssociation persisted = _associationStore.GetAssociation(association.AssociationId);
			if (persisted == null || !persisted.Revision.Equals(association.Revision) || !persisted.Target.Equals(association.Target) || persisted.State != association.State)
				throw new InvalidOperationException("The revision-update review no longer refers to the exact persisted old association state.");
		}

		private CollectionOperation Save(CollectionOperation current, CollectionOperationPhase phase, CollectionOperationResultState result)
		{
			if (current.CheckpointSequence == Int64.MaxValue) throw new InvalidOperationException("The revision-update checkpoint sequence cannot be incremented further.");
			var next = new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target, current.Revision,
				current.PlanIdentity, current.CheckpointSequence + 1, phase, result, current.NativeChildren);
			_operationStore.SaveOperation(next);
			return next;
		}
	}
}
