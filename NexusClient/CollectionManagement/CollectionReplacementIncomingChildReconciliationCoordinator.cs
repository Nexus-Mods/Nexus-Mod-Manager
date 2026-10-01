using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Reconciles verified C8.6 incoming native children without publishing incoming Collection association state.
	/// </summary>
	/// <remarks>
	/// C8.8 owns aggregate association publication. C8.6 only closes each native child at a durable safe boundary so
	/// later incoming preparation can use the authoritative post-child fingerprint without creating premature provenance.
	/// </remarks>
	public sealed class CollectionReplacementIncomingChildReconciliationCoordinator
	{
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;

		public CollectionReplacementIncomingChildReconciliationCoordinator(CollectionsOperationStore operationStore,
			CollectionsNativeChildRecoveryManifestStore manifestStore)
		{
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
		}

		internal CollectionOperation ReconcileVerifiedChild(CollectionNativeChildVerificationResult result,
			ResolvedCollectionPlan executionPlan)
		{
			if (result == null) throw new ArgumentNullException(nameof(result));
			return Reconcile(result.Operation, result.Child, result.NativeState, result.VerifiedNativeMod, executionPlan);
		}

		internal CollectionOperation ReconcileRestartedChild(CollectionNativeChildRestartReconciliationResult result,
			ResolvedCollectionPlan executionPlan)
		{
			if (result == null) throw new ArgumentNullException(nameof(result));
			return Reconcile(result.Operation, result.Child, result.NativeState, result.VerifiedNativeMod, executionPlan);
		}

		private CollectionOperation Reconcile(CollectionOperation observedOperation, CollectionNativeChildOperation observedChild,
			CollectionNativeStateIndex nativeState, CollectionNativeModState verifiedNativeMod, ResolvedCollectionPlan executionPlan)
		{
			if (observedOperation == null) throw new ArgumentNullException(nameof(observedOperation));
			if (observedChild == null) throw new ArgumentNullException(nameof(observedChild));
			if (nativeState == null) throw new ArgumentNullException(nameof(nativeState));
			if (executionPlan == null) throw new ArgumentNullException(nameof(executionPlan));
			if (executionPlan.Policy.Kind != CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup)
				throw new ArgumentException("C8.6 child reconciliation requires an explicit replacement execution plan.", nameof(executionPlan));

			CollectionOperation operation = _operationStore.GetOperation(observedOperation.Identity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || operation.IsTerminal ||
				(operation.Phase != CollectionOperationPhase.InstallingIncomingNativeChildren && operation.Phase != CollectionOperationPhase.Recovering) ||
				operation.PlanIdentity == null || !operation.PlanIdentity.Equals(executionPlan.Identity) || operation.Revision == null ||
				!operation.Revision.Equals(executionPlan.Revision) || !operation.Target.Equals(executionPlan.Target))
				throw new InvalidOperationException("C8.6 child reconciliation requires the active replacement incoming phase for the exact plan.");
			if (!nativeState.Target.Equals(executionPlan.Target))
				throw new ArgumentException("The verified native state belongs to a different replacement target.", nameof(nativeState));

			CollectionNativeChildOperation child = operation.NativeChildren.SingleOrDefault(x => x.Sequence == observedChild.Sequence);
			if (child == null || child.Action != CollectionNativeChildAction.ActivateOrReinstall ||
				!Matches(child.NativeOperation, observedChild.NativeOperation) || child.NativeResult == null ||
				child.NativeResult.Durability == ModOperationDurability.Unknown)
				throw new InvalidOperationException("The supplied C8.6 native child is not a durable known terminal attempt.");
			if (child.IsReconciled)
				return operation;
			if (child.Checkpoint != CollectionNativeChildCheckpoint.NativeTerminalObserved)
				throw new InvalidOperationException("C8.6 requires NativeTerminalObserved before child reconciliation.");

			ResolvedCollectionMemberPlan member = executionPlan.SelectedMembers.SingleOrDefault(x => x.MemberKey.Equals(child.Member.MemberKey));
			if (member == null || !child.Member.Revision.Equals(executionPlan.Revision) ||
				!StringComparer.Ordinal.Equals(child.NativeOperation.Fingerprint.RecipeFingerprint, member.RecipeIdentity.Fingerprint))
				throw new InvalidOperationException("The durable C8.6 native child does not map to the exact selected incoming recipe.");

			if (child.NativeResult.Durability == ModOperationDurability.VerifiedCommitted)
			{
				if (verifiedNativeMod == null)
					throw new InvalidOperationException("A VerifiedCommitted C8.6 child requires the exact verified native mod instance.");
				CollectionNativeModState indexed;
				if (!nativeState.Mods.TryGetValue(verifiedNativeMod.Identity, out indexed))
					throw new InvalidOperationException("The C8.6 verified native mod is absent from authoritative terminal state.");
				CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, child);
				if (manifest == null || manifest.TerminalStateFingerprint == null ||
					!manifest.TerminalStateFingerprint.Equals(nativeState.Fingerprint))
					throw new InvalidDataException("The committed C8.6 child recovery manifest no longer matches authoritative terminal state.");
				if (manifest.SafeBoundaryStateFingerprint == null)
					_manifestStore.SaveManifest(manifest.WithSafeBoundaryStateFingerprint(nativeState.Fingerprint));
				else if (!manifest.SafeBoundaryStateFingerprint.Equals(nativeState.Fingerprint))
					throw new InvalidDataException("The retained C8.6 safe-boundary fingerprint contradicts authoritative terminal state.");
			}

			if (operation.CheckpointSequence == Int64.MaxValue)
				throw new InvalidOperationException("The Collection operation checkpoint sequence cannot be incremented further.");
			var reconciledChild = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action,
				child.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, child.NativeResult);
			List<CollectionNativeChildOperation> children = operation.NativeChildren
				.Select(x => x.Sequence == child.Sequence ? reconciledChild : x).ToList();
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1, operation.Phase, operation.ResultState, children);
			_operationStore.SaveOperation(updated);
			return _operationStore.GetOperation(operation.Identity);
		}

		private static bool Matches(Nexus.Client.ModManagement.Operations.ModOperationIdentity left,
			Nexus.Client.ModManagement.Operations.ModOperationIdentity right)
		{
			return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId &&
				left.Origin == right.Origin && left.Fingerprint.Equals(right.Fingerprint);
		}
	}
}
