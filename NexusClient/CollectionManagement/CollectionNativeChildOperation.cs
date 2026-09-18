using System;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable persisted correlation between one collection member action and one logical native mod operation.
	/// </summary>
	/// <remarks>
	/// The native result retains its own reported status and independently verified durability. Collection recovery must
	/// reconcile that reality; it must never infer that a failed collection checkpoint means the native child did not commit.
	/// </remarks>
	public sealed class CollectionNativeChildOperation
	{
		public CollectionNativeChildOperation(
			int sequence,
			CollectionOperationMemberReference member,
			CollectionNativeChildAction action,
			ModOperationIdentity nativeOperation,
			CollectionNativeChildCheckpoint checkpoint,
			ModOperationResult nativeResult)
		{
			if (sequence <= 0)
				throw new ArgumentOutOfRangeException(nameof(sequence), "Native child sequence must be greater than zero.");
			if (member == null)
				throw new ArgumentNullException(nameof(member));
			if (!Enum.IsDefined(typeof(CollectionNativeChildAction), action) || action == CollectionNativeChildAction.Unknown)
				throw new ArgumentOutOfRangeException(nameof(action));
			if (nativeOperation == null)
				throw new ArgumentNullException(nameof(nativeOperation));
			if (!IsCollectionOwnedOrigin(nativeOperation.Origin))
				throw new ArgumentException("A collection native child must use Collection, LocalRestore or Recovery origin.", nameof(nativeOperation));
			if (!Enum.IsDefined(typeof(CollectionNativeChildCheckpoint), checkpoint) || checkpoint == CollectionNativeChildCheckpoint.Unknown)
				throw new ArgumentOutOfRangeException(nameof(checkpoint));

			bool terminalObserved = checkpoint == CollectionNativeChildCheckpoint.NativeTerminalObserved ||
				checkpoint == CollectionNativeChildCheckpoint.Reconciled;
			if (terminalObserved && nativeResult == null)
				throw new ArgumentNullException(nameof(nativeResult), "A terminal/reconciled native child requires the observed native result.");
			if (!terminalObserved && nativeResult != null)
				throw new ArgumentException("A native result cannot be attached before the terminal native report is observed.", nameof(nativeResult));
			if (nativeResult != null && !Matches(nativeOperation, nativeResult.Identity))
				throw new ArgumentException("The native result must belong to the exact native operation attempt recorded by the child.", nameof(nativeResult));

			Sequence = sequence;
			Member = member;
			Action = action;
			NativeOperation = nativeOperation;
			Checkpoint = checkpoint;
			NativeResult = nativeResult;
		}

		/// <summary>
		/// Gets the stable ordering/correlation sequence assigned by the collection journal.
		/// </summary>
		/// <remarks>This is not dependency, download or plugin load order.</remarks>
		public int Sequence { get; }

		/// <summary>
		/// Gets the exact revision/member correlated with this native action.
		/// </summary>
		public CollectionOperationMemberReference Member { get; }

		/// <summary>
		/// Gets the intended native action.
		/// </summary>
		public CollectionNativeChildAction Action { get; }

		/// <summary>
		/// Gets the logical native operation and concrete attempt currently represented by this child snapshot.
		/// </summary>
		public ModOperationIdentity NativeOperation { get; }

		/// <summary>
		/// Gets the persisted safety checkpoint reached by the child.
		/// </summary>
		public CollectionNativeChildCheckpoint Checkpoint { get; }

		/// <summary>
		/// Gets the independently durable native result once it has been observed, otherwise <c>null</c>.
		/// </summary>
		public ModOperationResult NativeResult { get; }

		/// <summary>
		/// Gets whether the child has crossed from prepared intent into the native submission boundary.
		/// </summary>
		public bool HasCrossedNativeBoundary
		{
			get { return Checkpoint >= CollectionNativeChildCheckpoint.NativeSubmitted; }
		}

		/// <summary>
		/// Gets whether collection state has been reconciled with the observed native result.
		/// </summary>
		public bool IsReconciled
		{
			get { return Checkpoint == CollectionNativeChildCheckpoint.Reconciled; }
		}

		/// <summary>
		/// Gets whether authoritative native state verified that this child committed.
		/// </summary>
		public bool HasVerifiedCommittedNativeState
		{
			get { return NativeResult != null && NativeResult.Durability == ModOperationDurability.VerifiedCommitted; }
		}

		/// <summary>
		/// Gets whether authoritative native state verified that this child rolled back.
		/// </summary>
		public bool HasVerifiedRolledBackNativeState
		{
			get { return NativeResult != null && NativeResult.Durability == ModOperationDurability.VerifiedRolledBack; }
		}

		/// <summary>
		/// Gets whether the child crossed the native boundary but its durable result remains unresolved.
		/// </summary>
		public bool HasUnknownNativeDurability
		{
			get
			{
				return HasCrossedNativeBoundary &&
					(NativeResult == null || NativeResult.Durability == ModOperationDurability.Unknown);
			}
		}

		private static bool IsCollectionOwnedOrigin(ModOperationOrigin origin)
		{
			return origin == ModOperationOrigin.Collection ||
				origin == ModOperationOrigin.LocalRestore ||
				origin == ModOperationOrigin.Recovery;
		}

		private static bool Matches(ModOperationIdentity expected, ModOperationIdentity actual)
		{
			return actual != null &&
				expected.OperationId == actual.OperationId &&
				expected.AttemptId == actual.AttemptId &&
				expected.Origin == actual.Origin &&
				expected.Fingerprint.Equals(actual.Fingerprint);
		}
	}
}
