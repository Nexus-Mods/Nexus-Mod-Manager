using System;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Sanitized restart snapshot of one persisted native AddMod operation.
	/// </summary>
	/// <remarks>
	/// Temporary Nexus authorization values and resolved CDN URLs are deliberately not exposed by this model.
	/// </remarks>
	public sealed class CollectionPersistedAddModState
	{
		public CollectionPersistedAddModState(Guid queueOperationId, TaskStatus status, bool isNexusSource,
			string gameDomain, long modId, long fileId, bool hasTemporaryAuthorization,
			DateTime? authorizationExpiresUtc, bool hasPartialData)
		{
			QueueOperationId = queueOperationId;
			Status = status;
			IsNexusSource = isNexusSource;
			GameDomain = gameDomain;
			ModId = modId;
			FileId = fileId;
			HasTemporaryAuthorization = hasTemporaryAuthorization;
			AuthorizationExpiresUtc = authorizationExpiresUtc;
			HasPartialData = hasPartialData;
		}

		public Guid QueueOperationId { get; }
		public TaskStatus Status { get; }
		public bool IsNexusSource { get; }
		public string GameDomain { get; }
		public long ModId { get; }
		public long FileId { get; }
		public bool HasTemporaryAuthorization { get; }
		public DateTime? AuthorizationExpiresUtc { get; }
		public bool HasPartialData { get; }
	}

	/// <summary>
	/// Supplies persisted native AddMod restart state without restarting the native task as a side effect.
	/// </summary>
	public interface ICollectionPersistedAddModStateSource
	{
		/// <summary>Finds the persisted native state for one durable queue-operation identity.</summary>
		CollectionPersistedAddModState Find(Guid queueOperationId);
	}
}
