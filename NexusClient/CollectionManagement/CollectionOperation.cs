using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable collection-operation journal snapshot spanning preparation, native children, verification and recovery.
	/// </summary>
	/// <remarks>
	/// This is a persistence/domain contract, not an executor. A later store/coordinator advances checkpoints only after the
	/// required durable ordering has been satisfied. Native child durability remains authoritative and is never collapsed into
	/// the collection-level result.
	/// </remarks>
	public sealed class CollectionOperation
	{
		private readonly ReadOnlyCollection<CollectionNativeChildOperation> _nativeChildren;

		public CollectionOperation(
			CollectionOperationIdentity identity,
			CollectionOperationKind kind,
			CollectionIdentity collection,
			CollectionTargetIdentity target,
			CollectionRevisionIdentity revision,
			CollectionPlanIdentity planIdentity,
			long checkpointSequence,
			CollectionOperationPhase phase,
			CollectionOperationResultState resultState,
			IEnumerable<CollectionNativeChildOperation> nativeChildren)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));
			if (!Enum.IsDefined(typeof(CollectionOperationKind), kind) || kind == CollectionOperationKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (collection == null)
				throw new ArgumentNullException(nameof(collection));
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (revision != null && !revision.Collection.Equals(collection))
				throw new ArgumentException("The concrete operation revision must belong to the operation collection.", nameof(revision));
			if (planIdentity != null && revision == null)
				throw new ArgumentException("A resolved plan identity cannot be attached before a concrete collection revision is known.", nameof(planIdentity));
			if (checkpointSequence < 0)
				throw new ArgumentOutOfRangeException(nameof(checkpointSequence));
			if (!Enum.IsDefined(typeof(CollectionOperationPhase), phase) || phase == CollectionOperationPhase.Unknown)
				throw new ArgumentOutOfRangeException(nameof(phase));
			if (!Enum.IsDefined(typeof(CollectionOperationResultState), resultState))
				throw new ArgumentOutOfRangeException(nameof(resultState));
			if (nativeChildren == null)
				throw new ArgumentNullException(nameof(nativeChildren));

			ValidatePhaseResult(phase, resultState);

			List<CollectionNativeChildOperation> children = new List<CollectionNativeChildOperation>();
			HashSet<int> sequences = new HashSet<int>();
			HashSet<Guid> logicalNativeOperationIds = new HashSet<Guid>();
			foreach (CollectionNativeChildOperation child in nativeChildren)
			{
				if (child == null)
					throw new ArgumentException("A collection operation cannot contain a null native child.", nameof(nativeChildren));
				if (!sequences.Add(child.Sequence))
					throw new ArgumentException("Native child sequences must be unique within a collection operation.", nameof(nativeChildren));
				if (!logicalNativeOperationIds.Add(child.NativeOperation.OperationId))
					throw new ArgumentException("A logical native operation must be represented by at most one current child snapshot.", nameof(nativeChildren));
				if (!StringComparer.Ordinal.Equals(child.NativeOperation.Fingerprint.TargetFingerprint, target.Fingerprint))
					throw new ArgumentException("Every native child must target the same exact collection operation target.", nameof(nativeChildren));

				ValidateChildOrigin(kind, child, nameof(nativeChildren));
				children.Add(child);
			}

			children.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));

			bool crossedNativeBoundary = false;
			bool hasUnreconciledChild = false;
			foreach (CollectionNativeChildOperation child in children)
			{
				crossedNativeBoundary |= child.HasCrossedNativeBoundary;
				hasUnreconciledChild |= child.HasCrossedNativeBoundary && !child.IsReconciled;
			}

			if ((resultState == CollectionOperationResultState.CancelledBeforeApply ||
				resultState == CollectionOperationResultState.FailedBeforeApply) && crossedNativeBoundary)
				throw new ArgumentException("A before-apply result cannot be reported after any native child crossed the mutation boundary.", nameof(resultState));

			if ((resultState == CollectionOperationResultState.Committed ||
				resultState == CollectionOperationResultState.RolledBack) && hasUnreconciledChild)
				throw new ArgumentException("Committed or rolled-back collection results require every submitted native child to be reconciled.", nameof(nativeChildren));

			Identity = identity;
			Kind = kind;
			Collection = collection;
			Target = target;
			Revision = revision;
			PlanIdentity = planIdentity;
			CheckpointSequence = checkpointSequence;
			Phase = phase;
			ResultState = resultState;
			_nativeChildren = new ReadOnlyCollection<CollectionNativeChildOperation>(children);
		}

		public CollectionOperationIdentity Identity { get; }
		public CollectionOperationKind Kind { get; }
		public CollectionIdentity Collection { get; }
		public CollectionTargetIdentity Target { get; }

		/// <summary>
		/// Gets the concrete primary revision once resolution has completed, otherwise <c>null</c>.
		/// </summary>
		public CollectionRevisionIdentity Revision { get; }

		/// <summary>
		/// Gets the exact immutable resolved-plan snapshot when this operation has one, otherwise <c>null</c>.
		/// </summary>
		public CollectionPlanIdentity PlanIdentity { get; }

		/// <summary>
		/// Gets the monotonically increasing durable checkpoint sequence assigned by the future feature store.
		/// </summary>
		public long CheckpointSequence { get; }

		public CollectionOperationPhase Phase { get; }
		public CollectionOperationResultState ResultState { get; }

		/// <summary>
		/// Gets native child snapshots in stable journal sequence order.
		/// </summary>
		public ReadOnlyCollection<CollectionNativeChildOperation> NativeChildren
		{
			get { return _nativeChildren; }
		}

		/// <summary>
		/// Gets whether at least one child crossed the native submission/mutation boundary.
		/// </summary>
		public bool HasCrossedNativeBoundary
		{
			get
			{
				foreach (CollectionNativeChildOperation child in _nativeChildren)
				{
					if (child.HasCrossedNativeBoundary)
						return true;
				}
				return false;
			}
		}

		/// <summary>
		/// Gets whether authoritative native state verified at least one child as committed.
		/// </summary>
		public bool HasVerifiedCommittedNativeChild
		{
			get
			{
				foreach (CollectionNativeChildOperation child in _nativeChildren)
				{
					if (child.HasVerifiedCommittedNativeState)
						return true;
				}
				return false;
			}
		}

		/// <summary>
		/// Gets whether any submitted child has not yet been reconciled with collection state.
		/// </summary>
		public bool HasUnreconciledNativeChild
		{
			get
			{
				foreach (CollectionNativeChildOperation child in _nativeChildren)
				{
					if (child.HasCrossedNativeBoundary && !child.IsReconciled)
						return true;
				}
				return false;
			}
		}

		/// <summary>
		/// Gets whether any submitted child's durable native outcome is still unknown.
		/// </summary>
		public bool HasUnknownNativeDurability
		{
			get
			{
				foreach (CollectionNativeChildOperation child in _nativeChildren)
				{
					if (child.HasUnknownNativeDurability)
						return true;
				}
				return false;
			}
		}

		/// <summary>
		/// Gets whether the operation is actively recovering or persistently requires recovery.
		/// </summary>
		public bool RequiresRecovery
		{
			get
			{
				return Phase == CollectionOperationPhase.Recovering ||
					Phase == CollectionOperationPhase.RecoveryRequired ||
					ResultState == CollectionOperationResultState.RecoveryRequired;
			}
		}

		/// <summary>
		/// Gets whether this snapshot is terminal at the collection-operation level.
		/// </summary>
		public bool IsTerminal
		{
			get { return Phase == CollectionOperationPhase.Completed; }
		}

		/// <summary>
		/// Gets whether the collection operation finished with a fully verified committed result.
		/// </summary>
		public bool IsSuccessful
		{
			get { return IsTerminal && ResultState == CollectionOperationResultState.Committed; }
		}

		private static void ValidatePhaseResult(CollectionOperationPhase phase, CollectionOperationResultState resultState)
		{
			if (phase == CollectionOperationPhase.Completed)
			{
				if (resultState == CollectionOperationResultState.Pending ||
					resultState == CollectionOperationResultState.RecoveryRequired)
					throw new ArgumentException("A completed collection operation requires a terminal result state.", nameof(resultState));
				return;
			}

			if (phase == CollectionOperationPhase.RecoveryRequired)
			{
				if (resultState != CollectionOperationResultState.RecoveryRequired)
					throw new ArgumentException("The RecoveryRequired phase must retain the explicit RecoveryRequired result state.", nameof(resultState));
				return;
			}

			if (resultState != CollectionOperationResultState.Pending)
				throw new ArgumentException("Only Completed or RecoveryRequired phases may carry a non-pending collection result.", nameof(resultState));
		}

		private static void ValidateChildOrigin(CollectionOperationKind kind, CollectionNativeChildOperation child, string parameterName)
		{
			if (child.NativeOperation.Origin == ModOperationOrigin.Recovery)
				return;

			if (kind == CollectionOperationKind.RestoreLocalCapture)
			{
				if (child.NativeOperation.Origin != ModOperationOrigin.LocalRestore)
					throw new ArgumentException("Local Collection restore children must use LocalRestore or Recovery native-operation origin.", parameterName);
				return;
			}

			if (child.NativeOperation.Origin != ModOperationOrigin.Collection)
				throw new ArgumentException("Collection operation children must use Collection or Recovery native-operation origin.", parameterName);
		}
	}
}
