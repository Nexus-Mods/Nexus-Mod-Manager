using System;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Classifies what must happen next for one acquisition request after process restart.
	/// </summary>
	public enum CollectionAcquisitionRestartDisposition
	{
		NotTracked = 0,
		VerifiedInputReady = 1,
		PremiumResumeReady = 2,
		PremiumUnavailable = 3,
		ManualAuthorizationExpired = 4,
		ManualAuthorizationRefreshRequired = 5,
		ManualInputRequired = 6,
		ReacquisitionRequired = 7,
		VerifiedContentMissing = 8,
		NativeStateMismatch = 9
	}

	/// <summary>
	/// Immutable result of reconciling one durable acquisition request with retained content, native queue state and current account state.
	/// </summary>
	public sealed class CollectionAcquisitionRestartResult
	{
		internal CollectionAcquisitionRestartResult(CollectionAcquisitionRestartDisposition disposition,
			CollectionAcquisitionRecord record, CollectionVerifiedArchive verifiedArchive,
			CollectionPersistedAddModState nativeState, CollectionPremiumAcquisitionAvailability? premiumAvailability)
		{
			Disposition = disposition;
			Record = record;
			VerifiedArchive = verifiedArchive;
			NativeState = nativeState;
			PremiumAvailability = premiumAvailability;
		}

		public CollectionAcquisitionRestartDisposition Disposition { get; }
		public CollectionAcquisitionRecord Record { get; }
		public CollectionVerifiedArchive VerifiedArchive { get; }
		public CollectionPersistedAddModState NativeState { get; }
		public CollectionPremiumAcquisitionAvailability? PremiumAvailability { get; }
		public bool HasPartialNativeData => NativeState != null && NativeState.HasPartialData;
	}
}
