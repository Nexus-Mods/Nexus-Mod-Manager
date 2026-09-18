using System;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// In-memory correlation between one Collection acquisition consumer and one existing AddMod queue operation.
	/// </summary>
	/// <remarks>
	/// The source URI is deliberately not retained here because an NXM URI may contain temporary authorization material.
	/// Several correlations may reference one shared queue operation; <see cref="Task"/> is consumer-scoped so cancelling one
	/// correlation cannot directly cancel work still required by another consumer. Durable restart reconciliation belongs to C4.22.
	/// </remarks>
	public sealed class CollectionAcquisitionQueueCorrelation
	{
		internal CollectionAcquisitionQueueCorrelation(
			CollectionAcquisitionRequest request,
			Guid queueOperationId,
			IBackgroundTask task)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			if (queueOperationId == Guid.Empty)
				throw new ArgumentException("A non-empty AddMod queue-operation identifier is required.", nameof(queueOperationId));
			if (task == null)
				throw new ArgumentNullException(nameof(task));

			Request = request;
			QueueOperationId = queueOperationId;
			Task = task;
		}

		/// <summary>Gets the Collection member acquisition intent.</summary>
		public CollectionAcquisitionRequest Request { get; }

		/// <summary>Gets the existing AddMod queue operation correlated with the request.</summary>
		public Guid QueueOperationId { get; }

		/// <summary>Gets the consumer-scoped task view for the shared NMM acquisition/import producer.</summary>
		public IBackgroundTask Task { get; }

		/// <summary>
		/// Cancels this Collection consumer. Shared native work continues while another consumer still requires it.
		/// </summary>
		public void Cancel()
		{
			Task.Cancel();
		}
	}
}
