using System;
using System.Threading;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Represents one handle on a managed-target mutation reservation.
	/// </summary>
	public sealed class CollectionTargetMutationLease : IDisposable
	{
		private int _disposed;

		internal CollectionTargetMutationLease(CollectionTargetMutationLeaseManager manager,
			CollectionTargetMutationLeaseManager.MutationReservation reservation, bool isRoot)
		{
			Manager = manager ?? throw new ArgumentNullException(nameof(manager));
			Reservation = reservation ?? throw new ArgumentNullException(nameof(reservation));
			IsRoot = isRoot;
		}

		/// <summary>
		/// Gets the stable identity of this process-local reservation.
		/// </summary>
		public Guid ReservationId => Reservation.ReservationId;

		/// <summary>
		/// Gets the target fingerprint protected by the reservation.
		/// </summary>
		public string TargetFingerprint => Reservation.TargetFingerprint;

		/// <summary>
		/// Gets whether this handle originally acquired the process reservation rather than inheriting it.
		/// </summary>
		public bool IsRoot { get; }

		/// <summary>
		/// Gets whether this reservation owns the canonical C4.15 cross-process target lock.
		/// </summary>
		public bool HasCrossProcessReservation => Manager.HasCrossProcessReservation(Reservation);

		/// <summary>
		/// Gets whether the named cross-process target lock was found abandoned when this root reservation was acquired.
		/// </summary>
		public bool CrossProcessLeaseWasAbandoned => Reservation.CrossProcessLeaseWasAbandoned;

		/// <summary>
		/// Gets whether native target state must be reloaded/reconciled before a child mutation may inherit this reservation.
		/// </summary>
		public bool RequiresRecovery => Manager.RequiresRecovery(Reservation);

		/// <summary>
		/// Gets whether this handle has already released its reference to the reservation.
		/// </summary>
		public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

		/// <summary>
		/// Marks abandoned-lock recovery/revalidation complete while retaining the same cross-process reservation.
		/// </summary>
		/// <remarks>
		/// Only the root owner may acknowledge recovery. C4.15 supplies the safety gate; the authoritative native-state reload
		/// and ownership validation are performed by the later recovery/C4.16 layers before they call this method.
		/// </remarks>
		public void MarkRecoveryCompleted()
		{
			if (IsDisposed)
				throw new ObjectDisposedException(nameof(CollectionTargetMutationLease));
			if (!IsRoot)
				throw new InvalidOperationException("Only the root target mutation lease can acknowledge abandoned-lock recovery.");

			Manager.MarkRecoveryCompleted(Reservation);
		}

		internal CollectionTargetMutationLeaseManager Manager { get; }
		internal CollectionTargetMutationLeaseManager.MutationReservation Reservation { get; }

		/// <inheritdoc />
		public void Dispose()
		{
			if (IsRoot && RequiresRecovery)
				throw new CollectionTargetRecoveryRequiredException(TargetFingerprint);
			if (Interlocked.Exchange(ref _disposed, 1) != 0)
				return;

			Manager.Release(Reservation);
		}
	}
}
