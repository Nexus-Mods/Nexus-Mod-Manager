using System;

namespace Nexus.Transactions
{
	/// <summary>
	/// An interface used by resource managers that want to participate in a transaction.
	/// </summary>
	public interface IEnlistmentNotification
	{
		/// <summary>
		/// Used to notify an enlisted resource manager that the transaction is being committed.
		/// </summary>
		/// <param name="enlistment">The enlistment class used to communicate with the resource manager.</param>
		void Commit(Enlistment enlistment);

		/// <summary>
		/// Used to notify an enlisted resource manager that the transaction is in doubt.
		/// </summary>
		/// <remarks>
		/// A transaction is in doubt if it has not received votes from all enlisted resource managers
		/// as to the state of the transaciton.
		/// </remarks>
		/// <param name="enlistment">The enlistment class used to communicate with the resource manager.</param>
		void InDoubt(Enlistment enlistment);

		/// <summary>
		/// Used to notify an enlisted resource manager that the transaction is being prepared for commitment.
		/// </summary>
		/// <param name="preparingEnlistment">The enlistment class used to communicate with the resource manager.</param>
		void Prepare(PreparingEnlistment preparingEnlistment);

		/// <summary>
		/// Used to notify an enlisted resource manager that the transaction is being rolled back.
		/// </summary>
		/// <param name="enlistment">The enlistment class used to communicate with the resource manager.</param>
		void Rollback(Enlistment enlistment);
	}
}
