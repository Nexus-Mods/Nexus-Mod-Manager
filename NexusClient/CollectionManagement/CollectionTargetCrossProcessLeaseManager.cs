using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Coordinates canonical Collection-target mutation across cooperating NMM processes.
	/// </summary>
	/// <remarks>
	/// A named .NET <see cref="Mutex"/> is thread-owned rather than task-owned. Each acquired reservation is therefore held
	/// by one dedicated owner thread until the public lease is disposed. This allows async callers and worker continuations
	/// to move threads without releasing the mutex from a thread that never acquired it.
	/// </remarks>
	public sealed class CollectionTargetCrossProcessLeaseManager
	{
		private const string CoordinationNamePrefix = @"Global\NMMCE.CollectionTarget.v1.";

		/// <summary>
		/// Gets the stateless production coordinator shared by Collection mutation leases.
		/// </summary>
		public static CollectionTargetCrossProcessLeaseManager Shared { get; } = new CollectionTargetCrossProcessLeaseManager();

		/// <summary>
		/// Acquires the named cross-process reservation for one canonical target.
		/// </summary>
		public CollectionTargetCrossProcessLease Acquire(CollectionTargetAuthority authority, CancellationToken cancellationToken)
		{
			return AcquireAsync(authority, cancellationToken).GetAwaiter().GetResult();
		}

		/// <summary>
		/// Acquires the named cross-process reservation for one canonical target.
		/// </summary>
		public CollectionTargetCrossProcessLease Acquire(CollectionTargetAuthority authority)
		{
			return Acquire(authority, CancellationToken.None);
		}

		/// <summary>
		/// Asynchronously waits for the named cross-process reservation without binding mutex ownership to an async continuation.
		/// </summary>
		public Task<CollectionTargetCrossProcessLease> AcquireAsync(CollectionTargetAuthority authority, CancellationToken cancellationToken)
		{
			ValidateAuthority(authority);
			if (cancellationToken.IsCancellationRequested)
			{
				var cancelled = new TaskCompletionSource<CollectionTargetCrossProcessLease>();
				cancelled.SetCanceled();
				return cancelled.Task;
			}

			string coordinationName = BuildCoordinationName(authority.PhysicalGameKey);
			var completion = new TaskCompletionSource<CollectionTargetCrossProcessLease>(TaskCreationOptions.RunContinuationsAsynchronously);
			var request = new CoordinationRequest(authority.Target.Fingerprint, coordinationName, cancellationToken, completion);
			var ownerThread = new Thread(request.Run)
			{
				IsBackground = true,
				Name = "NMM Collection target coordinator"
			};

			try
			{
				ownerThread.Start();
			}
			catch (Exception exception)
			{
				request.FailToStart(exception);
			}

			return completion.Task;
		}

		/// <summary>
		/// Builds a machine-wide mutex name from the canonical physical game identity without exposing that identity.
		/// </summary>
		private static string BuildCoordinationName(string physicalGameKey)
		{
			using (SHA256 sha256 = SHA256.Create())
			{
				byte[] digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(physicalGameKey));
				var builder = new StringBuilder(CoordinationNamePrefix.Length + (digest.Length * 2));
				builder.Append(CoordinationNamePrefix);
				foreach (byte value in digest)
					builder.Append(value.ToString("x2"));
				return builder.ToString();
			}
		}

		/// <summary>
		/// Validates that cross-process coordination is being requested from canonical C4.13 authority.
		/// </summary>
		private static void ValidateAuthority(CollectionTargetAuthority authority)
		{
			if (authority == null)
				throw new ArgumentNullException(nameof(authority));
			if (authority.Target == null || !authority.Target.IsCanonical)
				throw new ArgumentException("Cross-process mutation coordination requires canonical Collection target authority.", nameof(authority));
			CollectionIdentityValidation.RequireOpaqueToken(authority.PhysicalGameKey, nameof(authority.PhysicalGameKey));
		}

		internal sealed class CoordinationRequest
		{
			private readonly string _targetFingerprint;
			private readonly string _coordinationName;
			private readonly CancellationToken _cancellationToken;
			private readonly TaskCompletionSource<CollectionTargetCrossProcessLease> _completion;
			private readonly ManualResetEvent _releaseRequested = new ManualResetEvent(false);
			private readonly ManualResetEvent _ownerCompleted = new ManualResetEvent(false);
			private readonly object _syncRoot = new object();
			private Exception _releaseException;
			private bool _leasePublished;
			private bool _eventsDisposed;

			/// <summary>
			/// Creates one stable-thread mutex ownership request.
			/// </summary>
			public CoordinationRequest(string targetFingerprint, string coordinationName,
				CancellationToken cancellationToken, TaskCompletionSource<CollectionTargetCrossProcessLease> completion)
			{
				_targetFingerprint = targetFingerprint;
				_coordinationName = coordinationName;
				_cancellationToken = cancellationToken;
				_completion = completion;
			}

			/// <summary>
			/// Runs the complete mutex ownership lifetime on one stable thread.
			/// </summary>
			public void Run()
			{
				Mutex mutex = null;
				bool ownsMutex = false;
				bool wasAbandoned = false;
				try
				{
					mutex = new Mutex(false, _coordinationName);
					try
					{
						int signaled = WaitHandle.WaitAny(new WaitHandle[] { mutex, _cancellationToken.WaitHandle });
						if (signaled == 1)
						{
							_completion.TrySetCanceled();
							return;
						}

						ownsMutex = true;
					}
					catch (AbandonedMutexException exception)
					{
						if (exception.MutexIndex != 0 && exception.MutexIndex != -1)
							throw;

						ownsMutex = true;
						wasAbandoned = true;
					}

					var lease = new CollectionTargetCrossProcessLease(this, _targetFingerprint, _coordinationName, wasAbandoned);
					lock (_syncRoot)
						_leasePublished = true;
					_completion.TrySetResult(lease);

					_releaseRequested.WaitOne();
				}
				catch (Exception exception)
				{
					bool published;
					lock (_syncRoot)
						published = _leasePublished;

					if (published)
						RecordReleaseException(exception);
					else
						_completion.TrySetException(exception);
				}
				finally
				{
					if (ownsMutex && mutex != null)
					{
						try
						{
							mutex.ReleaseMutex();
						}
						catch (Exception exception)
						{
							RecordReleaseException(exception);
						}
					}

					if (mutex != null)
						mutex.Dispose();
					_ownerCompleted.Set();
					DisposeEventsWhenUnpublished();
				}
			}

			/// <summary>
			/// Completes a failed thread start without leaking wait handles.
			/// </summary>
			public void FailToStart(Exception exception)
			{
				_completion.TrySetException(exception);
				_ownerCompleted.Set();
				DisposeEvents();
			}

			/// <summary>
			/// Requests release and waits until the stable owner thread has actually released the mutex.
			/// </summary>
			public void ReleaseAndWait()
			{
				_releaseRequested.Set();
				_ownerCompleted.WaitOne();

				Exception releaseException;
				lock (_syncRoot)
					releaseException = _releaseException;

				DisposeEvents();
				if (releaseException != null)
					throw new InvalidOperationException("The cross-process Collection target reservation could not be released cleanly.", releaseException);
			}

			/// <summary>
			/// Records the first failure encountered after a lease has been published.
			/// </summary>
			private void RecordReleaseException(Exception exception)
			{
				lock (_syncRoot)
				{
					if (_releaseException == null)
						_releaseException = exception;
				}
			}

			/// <summary>
			/// Releases request wait handles immediately when no public lease can reference them.
			/// </summary>
			private void DisposeEventsWhenUnpublished()
			{
				bool published;
				lock (_syncRoot)
					published = _leasePublished;
				if (!published)
					DisposeEvents();
			}

			/// <summary>
			/// Idempotently disposes the request wait handles after ownership has ended.
			/// </summary>
			private void DisposeEvents()
			{
				lock (_syncRoot)
				{
					if (_eventsDisposed)
						return;
					_eventsDisposed = true;
				}

				_releaseRequested.Dispose();
				_ownerCompleted.Dispose();
			}
		}
	}
}
