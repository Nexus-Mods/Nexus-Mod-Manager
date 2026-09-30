using System;
using System.Threading;
using Nexus.Client.ModAuthoring;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Bridges characterized embedded Collection members into NMM's normal AddMod archive registry.</summary>
	public interface ICollectionBundledMemberAcquisitionCoordinator
	{
		bool Supports(CollectionArtifactReference artifact);
		CollectionBundledMemberAcquisitionResult Begin(CollectionAcquisitionRequest request,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy, ConfirmOverwriteCallback confirmOverwriteCallback,
			CancellationToken cancellationToken);
		CollectionVerifiedArchive TryComplete(CollectionAcquisitionRequest request, CancellationToken cancellationToken);
	}

	/// <summary>Result of starting one embedded-bundle AddMod import.</summary>
	public sealed class CollectionBundledMemberAcquisitionResult
	{
		public CollectionBundledMemberAcquisitionResult(CollectionVerifiedArchive verifiedArchive,
			CollectionAcquisitionQueueCorrelation queueCorrelation)
		{
			VerifiedArchive = verifiedArchive ?? throw new ArgumentNullException(nameof(verifiedArchive));
			QueueCorrelation = queueCorrelation;
		}

		public CollectionVerifiedArchive VerifiedArchive { get; }
		public CollectionAcquisitionQueueCorrelation QueueCorrelation { get; }
		public bool IsManagedArchiveReady { get { return QueueCorrelation == null; } }
	}
}
