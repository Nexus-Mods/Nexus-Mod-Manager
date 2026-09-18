namespace Nexus.Client.ModManagement.Operations
{
	/// <summary>
	/// Describes what has been verified about durable native state after an operation terminates.
	/// </summary>
	public enum ModOperationDurability
	{
		/// <summary>
		/// Durable state has not yet been determined or cannot be proven.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// The native operation was verified not to have started mutating durable state.
		/// </summary>
		NotStarted = 1,

		/// <summary>
		/// The intended native mutation was verified as committed.
		/// </summary>
		VerifiedCommitted = 2,

		/// <summary>
		/// Native state was verified as rolled back to the required pre-operation state.
		/// </summary>
		VerifiedRolledBack = 3
	}
}
