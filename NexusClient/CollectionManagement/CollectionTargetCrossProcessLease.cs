using System;
using System.Threading;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Represents one handle on the named cross-process reservation for a canonical Collection target.
	/// </summary>
	/// <remarks>
	/// The underlying named mutex remains owned by a dedicated stable thread. Disposing this handle may therefore happen
	/// on any caller thread without violating the thread-affinity rules of <see cref="Mutex"/>.
	/// </remarks>
	public sealed class CollectionTargetCrossProcessLease : IDisposable
	{
		private readonly CollectionTargetCrossProcessLeaseManager.CoordinationRequest _request;
		private int _disposed;
		private int _recoveryRequired;

		/// <summary>
		/// Creates the public lease returned after the stable owner thread has acquired the named mutex.
		/// </summary>
		internal CollectionTargetCrossProcessLease(CollectionTargetCrossProcessLeaseManager.CoordinationRequest request,
			string targetFingerprint, string coordinationName, bool wasAbandoned)
		{
			_request = request ?? throw new ArgumentNullException(nameof(request));
			TargetFingerprint = targetFingerprint ?? throw new ArgumentNullException(nameof(targetFingerprint));
			CoordinationName = coordinationName ?? throw new ArgumentNullException(nameof(coordinationName));
			WasAbandoned = wasAbandoned;
			_recoveryRequired = wasAbandoned ? 1 : 0;
		}

		/// <summary>
		/// Gets the canonical target fingerprint protected by this cross-process reservation.
		/// </summary>
		public string TargetFingerprint { get; }

		/// <summary>
		/// Gets the deterministic operating-system coordination name used for this target.
		/// </summary>
		/// <remarks>
		/// This is exposed for diagnostics and interoperability tests. It contains only a one-way hash of the canonical
		/// physical game identity and does not reveal game or storage paths.
		/// </remarks>
		public string CoordinationName { get; }

		/// <summary>
		/// Gets whether this acquisition recovered ownership from a thread or process that terminated while holding the mutex.
		/// </summary>
		public bool WasAbandoned { get; }

		/// <summary>
		/// Gets whether abandoned-lock recovery/revalidation must complete before this mutex may be cleanly released.
		/// </summary>
		public bool RequiresRecovery => Volatile.Read(ref _recoveryRequired) != 0;

		/// <summary>
		/// Gets whether this lease has already requested release of the cross-process reservation.
		/// </summary>
		public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

		/// <summary>
		/// Marks abandoned-lock recovery/revalidation complete while retaining the named target reservation.
		/// </summary>
		public void MarkRecoveryCompleted()
		{
			if (IsDisposed)
				throw new ObjectDisposedException(nameof(CollectionTargetCrossProcessLease));

			Interlocked.Exchange(ref _recoveryRequired, 0);
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (RequiresRecovery)
				throw new CollectionTargetRecoveryRequiredException(TargetFingerprint);
			if (Interlocked.Exchange(ref _disposed, 1) != 0)
				return;

			_request.ReleaseAndWait();
		}
	}
}
