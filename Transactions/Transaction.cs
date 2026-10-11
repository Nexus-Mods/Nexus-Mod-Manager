using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nexus.Transactions
{
	/// <summary>
	/// The possible options for enlistment.
	/// </summary>
	public enum EnlistmentOptions
	{
		None
	}

	/// <summary>
	/// A transaction.
	/// </summary>
	/// <remarks>
	/// This transaction class has no timeout.
	/// 
	/// This class allows sharing of transactions across threads, as the abient transaction
	/// is not restricted to a single thread.
	/// </remarks>
	public class Transaction : IDisposable
	{
		private static Transaction m_trnAmbient = null;
		private static readonly object m_objAmbientLock = new object();
		private static TaskCompletionSource<object> m_tcsAmbientReleased;

		/// <summary>
		/// Gets or sets the ambient transaction.
		/// </summary>
		/// <value>The ambient transaction.</value>
		public static Transaction Current
		{
			get
			{
				return m_trnAmbient;
			}
			internal set
			{
				TaskCompletionSource<object> released = null;
				lock (m_objAmbientLock)
				{
					if (value != null && m_trnAmbient == null)
						m_tcsAmbientReleased = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
					else if (value == null)
					{
						released = m_tcsAmbientReleased;
						m_tcsAmbientReleased = null;
					}
					m_trnAmbient = value;
				}
				released?.TrySetResult(null);
			}
		}

		/// <summary>Waits until the owning scope has disposed and released the current ambient transaction.</summary>
		/// <remarks>TransactionCompleted precedes scope disposal and is too early for committed-state readers.</remarks>
		public static Task WaitForAmbientTransactionAsync()
		{
			lock (m_objAmbientLock)
				return m_tcsAmbientReleased == null ? Task.FromResult<object>(null) : m_tcsAmbientReleased.Task;
		}

		private List<IEnlistmentNotification> m_lstNotifications = new List<IEnlistmentNotification>();
		private TransactionInformation m_tinInfo = new TransactionInformation();

		/// <summary>
		/// Gets the information about this transaction.
		/// </summary>
		/// <value>The information about this transaction.</value>
		public TransactionInformation TransactionInformation
		{
			get
			{
				return m_tinInfo;
			}
		}

		/// <summary>
		/// Raised when the transaction reaches a terminal committed or aborted state.
		/// </summary>
		public event EventHandler TransactionCompleted;
		
		/// <summary>
		/// Enlists a resource manager in this transaction.
		/// </summary>
		/// <param name="p_entNotification">The resource manager to enlist.</param>
		/// <param name="p_eopOptions">The enlistment options. This value must be <see cref="EnlistmentOptions.None"/>.</param>
		/// <exception cref="ArgumentException">Thrown if <paramref name="p_eopOptions"/> is not
		/// <see cref="EnlistmentOptions.None"/>.</exception>
		public void EnlistVolatile(IEnlistmentNotification p_entResourceManager, EnlistmentOptions p_eopOptions)
		{
			if (p_eopOptions != EnlistmentOptions.None)
				throw new ArgumentException("EnlistmentOptions must be None.", "p_eopOptions");

			m_lstNotifications.Add(p_entResourceManager);
		}

		/// <summary>
		/// Prepares the enlisted resource managers for committal.
		/// </summary>
		/// <returns><c>true</c> if all polled participants voted to commit;
		/// <c>false</c> otherwise.</returns>
		internal bool Prepare()
		{
			/*if (TransactionInformation.Status == TransactionStatus.Aborted)
				return false;

			if (TransactionInformation.Status != TransactionStatus.Active)
				throw new TransactionException("Cannot prepare transaction, as it is not active. Trasnaction Status: " + TransactionInformation.Status);
			*/
			bool booVoteToCommit = true;

			PreparingEnlistment lpeEnlistment = null;
			IEnlistmentNotification entNotification = null;
			for (Int32 i = m_lstNotifications.Count - 1; i >= 0; i--)
			{
				entNotification = m_lstNotifications[i];
				lpeEnlistment = new PreparingEnlistment();
				entNotification.Prepare(lpeEnlistment);
				if (lpeEnlistment.VoteToCommit.HasValue)
				{
					booVoteToCommit &= lpeEnlistment.VoteToCommit.Value;
					if (lpeEnlistment.DoneProcessing)
						m_lstNotifications.RemoveAt(i);
				}
				else
				{
					booVoteToCommit = false;
					TransactionInformation.Status = TransactionStatus.InDoubt;
				}
			}
			if (TransactionInformation.Status == TransactionStatus.InDoubt)
				NotifyInDoubt();
			return booVoteToCommit;
		}

		/// <summary>
		/// Tells al participanting resource managers to commit their changes.
		/// </summary>
		internal void Commit()
		{
			if (TransactionInformation.Status != TransactionStatus.Active)
				throw new TransactionException("Cannot commit transaction, as it is not active. Trasnaction Status: " + TransactionInformation.Status);

			PreparingEnlistment lpeEnlistment = null;
			IEnlistmentNotification entNotification = null;
			for (Int32 i = m_lstNotifications.Count - 1; i >= 0; i--)
			{
				entNotification = m_lstNotifications[i];
				lpeEnlistment = new PreparingEnlistment();
				entNotification.Commit(lpeEnlistment);
				if (lpeEnlistment.DoneProcessing)
					m_lstNotifications.RemoveAt(i);
			}
			if (m_lstNotifications.Count > 0)
			{
				TransactionInformation.Status = TransactionStatus.InDoubt;
				NotifyInDoubt();
			}
			else
			{
				TransactionInformation.Status = TransactionStatus.Committed;
				OnTransactionCompleted();
			}
		}

		/// <summary>
		/// Tells the participating resource managers that the transaction status is in doubt.
		/// </summary>
		protected void NotifyInDoubt()
		{
			if (TransactionInformation.Status != TransactionStatus.InDoubt)
				return;

			List<Exception> failures = new List<Exception>();
			Enlistment eltEnlistment = null;
			IEnlistmentNotification entNotification = null;
			for (Int32 i = m_lstNotifications.Count - 1; i >= 0; i--)
			{
				entNotification = m_lstNotifications[i];
				eltEnlistment = new Enlistment();
				try
				{
					entNotification.InDoubt(eltEnlistment);
				}
				catch (Exception exception)
				{
					// Continue recovery for the other resource managers even if one cannot recover.
					failures.Add(exception);
				}
				if (eltEnlistment.DoneProcessing)
					m_lstNotifications.RemoveAt(i);
			}
			if (failures.Count > 0)
				throw new TransactionException("One or more transaction resources could not recover.", new AggregateException(failures));
		}

		/// <summary>
		/// Tells the participating resource managers to rollback their changes.
		/// </summary>
		public void Rollback()
		{
			if (TransactionInformation.Status == TransactionStatus.Aborted)
				return;

			List<RollbackException.ExceptedResourceManager> lstExceptions = new List<RollbackException.ExceptedResourceManager>();
			Enlistment eltEnlistment = null;
			IEnlistmentNotification entNotification = null;
			for (Int32 i = m_lstNotifications.Count - 1; i >= 0; i--)
			{
				entNotification = m_lstNotifications[i];
				eltEnlistment = new Enlistment();
				try
				{
					entNotification.Rollback(eltEnlistment);
				}
				catch (Exception e)
				{
					lstExceptions.Add(new RollbackException.ExceptedResourceManager(entNotification, e));
				}
				if (eltEnlistment.DoneProcessing)
					m_lstNotifications.RemoveAt(i);
			}
			if (m_lstNotifications.Count > 0)
			{
				TransactionInformation.Status = TransactionStatus.InDoubt;
				NotifyInDoubt();
			}
			else
			{
				TransactionInformation.Status = TransactionStatus.Aborted;
				OnTransactionCompleted();
			}

			if (lstExceptions.Count > 0)
				throw new RollbackException(lstExceptions);
		}

		/// <summary>
		/// Raises the terminal transaction notification after commit or rollback completes.
		/// </summary>
		private void OnTransactionCompleted()
		{
			EventHandler handler = TransactionCompleted;
			if (handler != null)
				handler(this, EventArgs.Empty);
		}

		#region IDisposable Members

		/// <summary>
		/// Disposes of the transaction.
		/// </summary>
		public void Dispose()
		{
			if (TransactionInformation.Status == TransactionStatus.Active)
				Rollback();
		}

		#endregion
	}
}
