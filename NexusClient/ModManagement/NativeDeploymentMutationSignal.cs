namespace Nexus.Client.ModManagement
{
	using System;
	using System.Diagnostics;
	using System.Threading;

	/// <summary>
	/// In-process notification around a *native* owner mutation. This is not a transaction,
	/// durability result, cross-process lock, or substitute for NMM deployment recovery.
	/// Readers must invalidate on both boundaries, including exception/rollback paths.
	/// An ambient Nexus transaction keeps the signal active until its owning scope
	/// releases the global native authority, not merely until the inner method returns.
	/// </summary>
	internal sealed class NativeDeploymentMutationSignal
	{
		private int _active;

		internal event EventHandler Changed;

		internal bool IsActive => Volatile.Read(ref _active) != 0;

		internal IDisposable Begin()
		{
			// NMM uses Nexus.Transactions, not System.Transactions. The ambient native
			// authority is not safe to read at TransactionCompleted: it is released
			// only on disposal of the outer Nexus TransactionScope.
			var scope = new Scope(this);
			Interlocked.Increment(ref _active);
			Notify();
			return scope;
		}

		private void End()
		{
			Interlocked.Decrement(ref _active);
			Notify();
		}

		private void Notify()
		{
			EventHandler handlers = Changed;
			if (handlers == null) return;
			// A closing UI observer must never make a native mutation fail, and must
			// never prevent another observer from learning that the state is stale.
			foreach (EventHandler handler in handlers.GetInvocationList())
			{
				try { handler(this, EventArgs.Empty); }
				catch (Exception error)
				{
					Trace.TraceWarning("Native ownership mutation observer failed: {0}", error.Message);
				}
			}
		}

		private sealed class Scope : IDisposable
		{
			private readonly NativeDeploymentMutationSignal _owner;
			private readonly bool _hadNativeAmbient;
			private int _disposed;
			private int _ambientReleased;
			private int _ended;

			internal Scope(NativeDeploymentMutationSignal owner)
			{
				_owner = owner;
				_hadNativeAmbient = Nexus.Transactions.Transaction.Current != null;
				if (_hadNativeAmbient)
				{
					// The native transaction's release task completes only after the
					// outer scope clears its ambient authority. No data reads here.
					Nexus.Transactions.Transaction.WaitForAmbientTransactionAsync().ContinueWith(
						ignored =>
						{
							Volatile.Write(ref _ambientReleased, 1);
							FinishIfSafe();
						}, System.Threading.CancellationToken.None,
						System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,
						System.Threading.Tasks.TaskScheduler.Default);
				}
			}

			public void Dispose()
			{
				Volatile.Write(ref _disposed, 1);
				FinishIfSafe();
			}

			private void FinishIfSafe()
			{
				if (Volatile.Read(ref _disposed) == 0 ||
					(_hadNativeAmbient && Volatile.Read(ref _ambientReleased) == 0)) return;
				if (Interlocked.Exchange(ref _ended, 1) == 0)
					_owner.End();
			}
		}

	}
}
