namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes whether the selected collection capability set can be handled by the current implementation.
	/// </summary>
	public enum CollectionCompatibilityStatus
	{
		/// <summary>
		/// No compatibility result has been established.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// The selected capability set is supported without unresolved user or provider action.
		/// </summary>
		Supported = 1,

		/// <summary>
		/// The selected capability set is not ready until an explicit action or missing decision/data is resolved.
		/// </summary>
		ActionRequired = 2,

		/// <summary>
		/// The selected capability set contains behavior that the current implementation cannot safely execute.
		/// </summary>
		Unsupported = 3
	}
}
