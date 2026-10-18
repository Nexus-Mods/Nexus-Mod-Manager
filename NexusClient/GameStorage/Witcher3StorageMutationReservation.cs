using System;
using System.Threading;
using Nexus.Client.ModManagement.Witcher3;

namespace Nexus.Client.GameStorage
{
	/// <summary>
	/// Serializes Witcher 3 Game Storage setup, repair, recovery, and binding changes across
	/// cooperating NMM processes, including processes whose game roots are different but
	/// whose proposed InstallInfo/VirtualInstall locations overlap.
	/// </summary>
	/// <remarks>
	/// Game Storage methods are synchronous: mutex acquisition/release intentionally stays
	/// on the calling thread. This is not an alternative native InstallLog ownership
	/// authority and does not grant native mod-install permission. The machine-wide name
	/// is deliberately independent of a game root, ModeId, settings file, or NMM executable.
	/// Never await inside this reservation.
	/// </remarks>
	public sealed class Witcher3StorageMutationReservation : IDisposable
	{
		private const string MutexName = @"Global\NMMCE.Witcher3.GameStorage.v1";
		private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(8);
		[ThreadStatic] private static int s_threadReservationDepth;
		private readonly Mutex _mutex;
		private readonly int _ownerThreadId;
		private bool _disposed;

		private Witcher3StorageMutationReservation(Mutex mutex)
		{
			_mutex = mutex;
			_ownerThreadId = Thread.CurrentThread.ManagedThreadId;
			s_threadReservationDepth++;
		}

		/// <summary>
		/// Returns null for non-Witcher games (which retain their existing behavior).
		/// An abandoned reservation is not authorization to continue an interrupted write.
		/// </summary>
		public static Witcher3StorageMutationReservation Acquire(string modeId)
		{
			return Acquire(modeId, DefaultWait);
		}

		/// <summary>
		/// Bounded, non-throwing entry for UI commands that return a failure result.
		/// Only reservation failures are translated; native write failures still propagate.
		/// </summary>
		public static bool TryAcquire(string modeId, out Witcher3StorageMutationReservation reservation,
			out string failure)
		{
			try
			{
				reservation = Acquire(modeId);
				failure = null;
				return true;
			}
			catch (InvalidOperationException exception)
			{
				reservation = null;
				failure = exception.Message;
				return false;
			}
		}

		/// <summary>
		/// Acquires the same named mutex for both Witcher ModeIds and every NMM instance.
		/// Timeout is bounded so the Game Storage UI cannot hang indefinitely.
		/// </summary>
		public static Witcher3StorageMutationReservation Acquire(string modeId, TimeSpan timeout)
		{
			if (!Witcher3RootOwnershipGuard.IsWitcherMode(modeId)) return null;
			if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
				throw new ArgumentOutOfRangeException(nameof(timeout));

			Mutex mutex = null;
			bool acquired = false;
			try
			{
				mutex = new Mutex(false, MutexName);
				try
				{
					acquired = mutex.WaitOne(timeout);
				}
				catch (AbandonedMutexException)
				{
					// The dying process could have written one of several metadata files.
					// Do not publish more claims until recovery has been inspected explicitly.
					mutex.ReleaseMutex();
					throw new InvalidOperationException("An interrupted Witcher 3 Game Storage write may need recovery. Inspect the existing manifests before retrying.");
				}
				if (!acquired)
					throw new InvalidOperationException("Another NMM process is changing Witcher 3 Game Storage. No storage changes were made; retry after it finishes.");
				return new Witcher3StorageMutationReservation(mutex);
			}
			catch (Exception exception)
			{
				if (acquired) mutex.ReleaseMutex();
				if (mutex != null) mutex.Dispose();
				if (exception is UnauthorizedAccessException ||
					exception is System.Security.SecurityException ||
					exception is WaitHandleCannotBeOpenedException)
					throw new InvalidOperationException("Witcher 3 Game Storage cannot obtain the machine-wide reservation. No metadata changes were made.", exception);
				throw;
			}
		}

		/// <summary>
		/// A leaf publication must be contained in an outer storage operation whose
		/// registry reads, collision checks, and writes all share the same reservation.
		/// </summary>
		public static void RequireHeld(string modeId)
		{
			if (Witcher3RootOwnershipGuard.IsWitcherMode(modeId) && s_threadReservationDepth == 0)
				throw new InvalidOperationException("Witcher 3 Game Storage metadata publication requires the cross-process storage reservation.");
		}

		public void Dispose()
		{
			if (_disposed) return;
			if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
				throw new InvalidOperationException("The Witcher 3 Game Storage reservation must be released on its acquiring thread.");
			_disposed = true;
			s_threadReservationDepth--;
			try { _mutex.ReleaseMutex(); }
			finally { _mutex.Dispose(); }
		}
	}
}
