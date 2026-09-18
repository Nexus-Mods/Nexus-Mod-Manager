using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Serializes cooperating managed-state mutations inside the current NMM process and, for canonical Collection targets,
	/// coordinates ownership with other cooperating NMM processes.
	/// </summary>
	/// <remarks>
	/// The initial policy remains conservative: only one managed target mutation may hold the process reservation at a time,
	/// even when two canonical targets differ. Canonical Collection roots additionally acquire the C4.15 named target lock.
	/// A mutex does not prove InstallLog coherence; that validation remains the separate C4.16 boundary.
	/// </remarks>
	public sealed class CollectionTargetMutationLeaseManager
	{
		private readonly SemaphoreSlim _processGate = new SemaphoreSlim(1, 1);
		private readonly object _syncRoot = new object();
		private readonly CollectionTargetCrossProcessLeaseManager _crossProcessLeaseManager;
		private MutationReservation _activeReservation;

		/// <summary>
		/// Gets the process-wide lease manager production mutation paths should share.
		/// </summary>
		public static CollectionTargetMutationLeaseManager Shared { get; } = new CollectionTargetMutationLeaseManager();

		/// <summary>
		/// Creates a mutation lease manager using the shared cross-process target coordinator.
		/// </summary>
		public CollectionTargetMutationLeaseManager()
			: this(CollectionTargetCrossProcessLeaseManager.Shared)
		{
		}

		/// <summary>
		/// Creates a mutation manager over the supplied cross-process coordinator.
		/// </summary>
		internal CollectionTargetMutationLeaseManager(CollectionTargetCrossProcessLeaseManager crossProcessLeaseManager)
		{
			_crossProcessLeaseManager = crossProcessLeaseManager ?? throw new ArgumentNullException(nameof(crossProcessLeaseManager));
		}

		/// <summary>
		/// Acquires the process reservation and named cross-process reservation for the specified canonical target.
		/// </summary>
		/// <param name="authority">The live canonical target authority established by C4.13.</param>
		/// <param name="cancellationToken">Cancellation while waiting for another cooperating mutation to finish.</param>
		/// <returns>An exclusive mutation lease that must be disposed at a safe native boundary.</returns>
		public CollectionTargetMutationLease Acquire(CollectionTargetAuthority authority, CancellationToken cancellationToken)
		{
			ValidateAuthority(authority);
			_processGate.Wait(cancellationToken);

			CollectionTargetCrossProcessLease crossProcessLease = null;
			try
			{
				crossProcessLease = _crossProcessLeaseManager.Acquire(authority, cancellationToken);
				return CreateRootLease(authority.Target.Fingerprint, crossProcessLease);
			}
			catch
			{
				if (crossProcessLease != null)
					crossProcessLease.Dispose();
				_processGate.Release();
				throw;
			}
		}

		/// <summary>
		/// Acquires the process and cross-process mutation reservations for the specified canonical target.
		/// </summary>
		public CollectionTargetMutationLease Acquire(CollectionTargetAuthority authority)
		{
			return Acquire(authority, CancellationToken.None);
		}

		/// <summary>
		/// Asynchronously acquires the process and cross-process reservations without blocking the caller thread.
		/// </summary>
		public async Task<CollectionTargetMutationLease> AcquireAsync(CollectionTargetAuthority authority, CancellationToken cancellationToken)
		{
			ValidateAuthority(authority);
			await _processGate.WaitAsync(cancellationToken).ConfigureAwait(false);

			CollectionTargetCrossProcessLease crossProcessLease = null;
			try
			{
				crossProcessLease = await _crossProcessLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false);
				return CreateRootLease(authority.Target.Fingerprint, crossProcessLease);
			}
			catch
			{
				if (crossProcessLease != null)
					crossProcessLease.Dispose();
				_processGate.Release();
				throw;
			}
		}

		/// <summary>
		/// Creates a nested lease that inherits an already-held reservation for the same canonical target.
		/// </summary>
		/// <remarks>
		/// Collection child operations use this instead of reacquiring either the process gate or named mutex and deadlocking
		/// behind their parent. An abandoned cross-process reservation cannot be inherited until recovery has been acknowledged.
		/// </remarks>
		public CollectionTargetMutationLease Inherit(CollectionTargetMutationLease parentLease, CollectionTargetAuthority authority)
		{
			ValidateAuthority(authority);
			return InheritCore(parentLease, authority.Target.Fingerprint);
		}

		/// <summary>
		/// Acquires only the shared process reservation for an existing native-operation fingerprint.
		/// </summary>
		/// <remarks>
		/// C3 manual/native operations still carry a descriptive pre-C4 target fingerprint. They remain protected by the C4.14
		/// process gate. Collection children inherit their canonical parent's cross-process reservation instead of reacquiring it.
		/// </remarks>
		internal CollectionTargetMutationLease AcquireNativeOperation(string targetFingerprint)
		{
			return AcquireProcessOnlyCore(
				CollectionIdentityValidation.RequireOpaqueToken(targetFingerprint, nameof(targetFingerprint)), CancellationToken.None);
		}

		/// <summary>
		/// Inherits an existing Collection parent reservation for a native child operation on the same target fingerprint.
		/// </summary>
		internal CollectionTargetMutationLease InheritNativeOperation(CollectionTargetMutationLease parentLease, string targetFingerprint)
		{
			return InheritCore(parentLease,
				CollectionIdentityValidation.RequireOpaqueToken(targetFingerprint, nameof(targetFingerprint)));
		}

		private CollectionTargetMutationLease AcquireProcessOnlyCore(string targetFingerprint, CancellationToken cancellationToken)
		{
			_processGate.Wait(cancellationToken);
			try
			{
				return CreateRootLease(targetFingerprint, null);
			}
			catch
			{
				_processGate.Release();
				throw;
			}
		}

		private CollectionTargetMutationLease InheritCore(CollectionTargetMutationLease parentLease, string targetFingerprint)
		{
			if (parentLease == null)
				throw new ArgumentNullException(nameof(parentLease));
			if (!ReferenceEquals(parentLease.Manager, this))
				throw new InvalidOperationException("A target mutation lease can only be inherited through the manager that acquired it.");
			if (parentLease.IsDisposed)
				throw new ObjectDisposedException(nameof(parentLease));

			MutationReservation reservation = parentLease.Reservation;
			if (!StringComparer.Ordinal.Equals(reservation.TargetFingerprint, targetFingerprint))
				throw new InvalidOperationException("A target mutation reservation cannot be inherited by a different target.");

			lock (_syncRoot)
			{
				if (!ReferenceEquals(_activeReservation, reservation) || reservation.ReferenceCount <= 0)
					throw new InvalidOperationException("The target mutation reservation is no longer active.");
				if (reservation.CrossProcessLease != null && reservation.CrossProcessLease.RequiresRecovery)
					throw new CollectionTargetRecoveryRequiredException(reservation.TargetFingerprint);

				reservation.ReferenceCount++;
			}

			return new CollectionTargetMutationLease(this, reservation, false);
		}

		private CollectionTargetMutationLease CreateRootLease(string targetFingerprint, CollectionTargetCrossProcessLease crossProcessLease)
		{
			lock (_syncRoot)
			{
				if (_activeReservation != null)
					throw new InvalidOperationException("The in-process mutation gate was acquired while another reservation was still active.");

				_activeReservation = new MutationReservation(targetFingerprint, crossProcessLease);
				return new CollectionTargetMutationLease(this, _activeReservation, true);
			}
		}

		/// <summary>
		/// Returns whether the active reservation owns the canonical C4.15 cross-process target lock.
		/// </summary>
		internal bool HasCrossProcessReservation(MutationReservation reservation)
		{
			lock (_syncRoot)
			{
				return ReferenceEquals(_activeReservation, reservation) && reservation.ReferenceCount > 0 &&
					reservation.CrossProcessLease != null && !reservation.CrossProcessLease.IsDisposed;
			}
		}

		/// <summary>
		/// Returns whether the active named target reservation still requires abandoned-lock recovery.
		/// </summary>
		internal bool RequiresRecovery(MutationReservation reservation)
		{
			lock (_syncRoot)
			{
				return ReferenceEquals(_activeReservation, reservation) && reservation.ReferenceCount > 0 &&
					reservation.CrossProcessLease != null && reservation.CrossProcessLease.RequiresRecovery;
			}
		}

		/// <summary>
		/// Acknowledges successful target reload/revalidation without releasing the active reservation.
		/// </summary>
		internal void MarkRecoveryCompleted(MutationReservation reservation)
		{
			lock (_syncRoot)
			{
				if (!ReferenceEquals(_activeReservation, reservation) || reservation.ReferenceCount <= 0)
					throw new InvalidOperationException("The target mutation reservation is no longer active.");

				if (reservation.CrossProcessLease != null)
					reservation.CrossProcessLease.MarkRecoveryCompleted();
			}
		}

		internal void Release(MutationReservation reservation)
		{
			CollectionTargetCrossProcessLease crossProcessLease = null;
			bool releaseProcessGate = false;
			lock (_syncRoot)
			{
				if (!ReferenceEquals(_activeReservation, reservation) || reservation.ReferenceCount <= 0)
					return;

				reservation.ReferenceCount--;
				if (reservation.ReferenceCount == 0)
				{
					_activeReservation = null;
					crossProcessLease = reservation.CrossProcessLease;
					releaseProcessGate = true;
				}
			}

			if (!releaseProcessGate)
				return;

			try
			{
				if (crossProcessLease != null)
					crossProcessLease.Dispose();
			}
			finally
			{
				_processGate.Release();
			}
		}

		private static void ValidateAuthority(CollectionTargetAuthority authority)
		{
			if (authority == null)
				throw new ArgumentNullException(nameof(authority));
			if (authority.Target == null || !authority.Target.IsCanonical)
				throw new ArgumentException("A live canonical Collection target authority is required for mutation coordination.", nameof(authority));
		}

		internal sealed class MutationReservation
		{
			public MutationReservation(string targetFingerprint, CollectionTargetCrossProcessLease crossProcessLease)
			{
				ReservationId = Guid.NewGuid();
				TargetFingerprint = targetFingerprint;
				CrossProcessLease = crossProcessLease;
				CrossProcessLeaseWasAbandoned = crossProcessLease != null && crossProcessLease.WasAbandoned;
				ReferenceCount = 1;
			}

			public Guid ReservationId { get; }
			public string TargetFingerprint { get; }
			public CollectionTargetCrossProcessLease CrossProcessLease { get; }
			public bool CrossProcessLeaseWasAbandoned { get; }
			public int ReferenceCount { get; set; }
		}
	}
}
