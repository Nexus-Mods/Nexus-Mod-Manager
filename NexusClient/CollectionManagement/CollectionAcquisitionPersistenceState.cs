using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies the durable acquisition route used for one Collection request.
	/// </summary>
	public enum CollectionAcquisitionPersistenceMode
	{
		Unknown = 0,
		PremiumNxm = 1,
		Manual = 2,
		VerifiedReuse = 3
	}

	/// <summary>
	/// Describes the durable acquisition checkpoint used to reconcile one request after restart.
	/// </summary>
	public enum CollectionAcquisitionPersistenceState
	{
		Unknown = 0,
		PendingUserAction = 1,
		Queued = 2,
		ProducerCompletedUnverified = 3,
		Verified = 4,
		Cancelled = 5,
		Failed = 6
	}
}
