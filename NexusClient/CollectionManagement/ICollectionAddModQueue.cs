using System;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModAuthoring;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Narrow adapter over NMM's existing AddMod/download queue used by Collection acquisition.
	/// </summary>
	/// <remarks>
	/// The adapter does not create a second downloader. The explicit queue-operation identifier exists only so feature-owned
	/// acquisition intent can be correlated with the existing native queue/task lifecycle.
	/// </remarks>
	public interface ICollectionAddModQueue
	{
		/// <summary>
		/// Queues one source through the existing AddMod pipeline using the supplied stable in-memory queue-operation identity.
		/// </summary>
		IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId);
	}
}
