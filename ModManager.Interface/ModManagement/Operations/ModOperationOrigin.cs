namespace Nexus.Client.ModManagement.Operations
{
	/// <summary>
	/// Identifies the feature or workflow that requested a native mod operation.
	/// </summary>
	public enum ModOperationOrigin
	{
		/// <summary>
		/// No trusted origin was supplied. Operation identities must not use this value.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// The operation was requested through an ordinary user mod-management action.
		/// </summary>
		Manual = 1,

		/// <summary>
		/// The operation was requested while applying or managing a Collection.
		/// </summary>
		Collection = 2,

		/// <summary>
		/// The operation was requested while restoring a Local Collection capture.
		/// </summary>
		LocalRestore = 3,

		/// <summary>
		/// The operation was requested while reconciling persisted recovery intent.
		/// </summary>
		Recovery = 4,

		/// <summary>
		/// The operation was requested by an NMM profile workflow.
		/// </summary>
		Profile = 5
	}
}
