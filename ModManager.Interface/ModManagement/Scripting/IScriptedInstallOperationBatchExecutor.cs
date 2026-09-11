using System;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Exposes optional batching support for executors that can defer repeated deployment maintenance while a scripted plan is committed.
	/// </summary>
	public interface IScriptedInstallOperationBatchExecutor
	{
		/// <summary>
		/// Begins an execution batch for a set of pending scripted installation operations.
		/// </summary>
		/// <param name="p_intExpectedFileOperations">The number of pending operations expected to create or update virtual file links.</param>
		/// <returns>A scope that completes the batch when disposed.</returns>
		IDisposable BeginExecutionBatch(int p_intExpectedFileOperations);
	}
}
