namespace Nexus.Client.ModManagement.Operations
{
	/// <summary>
	/// Describes the terminal status reported by the native operation workflow.
	/// </summary>
	public enum ModOperationReportedStatus
	{
		/// <summary>
		/// No terminal status has been reported. Completed results must not use this value.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// The native workflow reported success.
		/// </summary>
		Succeeded = 1,

		/// <summary>
		/// The native workflow reported failure.
		/// </summary>
		Failed = 2,

		/// <summary>
		/// The operation was cancelled.
		/// </summary>
		Cancelled = 3,

		/// <summary>
		/// No native mutation was required after the requested state was verified.
		/// </summary>
		NoOp = 4,

		/// <summary>
		/// The request was rejected before execution.
		/// </summary>
		Rejected = 5
	}
}
