using System;

namespace Nexus.Client.ModManagement.Operations
{
	/// <summary>
	/// Records a terminal native-operation report separately from the durability observed in authoritative native state.
	/// </summary>
	public sealed class ModOperationResult
	{
		/// <summary>
		/// Initializes a terminal native-operation result.
		/// </summary>
		/// <param name="identity">The operation and attempt that produced the result.</param>
		/// <param name="reportedStatus">The terminal status reported by the native workflow.</param>
		/// <param name="durability">The independently verified durable-state outcome.</param>
		/// <param name="message">An optional human-readable result message.</param>
		public ModOperationResult(ModOperationIdentity identity, ModOperationReportedStatus reportedStatus,
			ModOperationDurability durability, string message)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));
			if (!Enum.IsDefined(typeof(ModOperationReportedStatus), reportedStatus) || reportedStatus == ModOperationReportedStatus.Unknown)
				throw new ArgumentOutOfRangeException(nameof(reportedStatus));
			if (!Enum.IsDefined(typeof(ModOperationDurability), durability))
				throw new ArgumentOutOfRangeException(nameof(durability));

			Identity = identity;
			ReportedStatus = reportedStatus;
			Durability = durability;
			Message = message;
		}

		/// <summary>
		/// Gets the operation and concrete attempt that produced the result.
		/// </summary>
		public ModOperationIdentity Identity { get; }

		/// <summary>
		/// Gets the terminal status reported by the native workflow.
		/// </summary>
		public ModOperationReportedStatus ReportedStatus { get; }

		/// <summary>
		/// Gets the independently observed durability of native state.
		/// </summary>
		public ModOperationDurability Durability { get; }

		/// <summary>
		/// Gets the optional human-readable result message.
		/// </summary>
		public string Message { get; }
	}
}
