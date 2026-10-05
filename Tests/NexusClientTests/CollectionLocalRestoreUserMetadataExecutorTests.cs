using Nexus.Client.CollectionManagement;
using Nexus.Client.Mods.Formats.FOMod;
using NUnit.Framework;

namespace NexusClientTests
{
	[Category("CollectionsGateL")]
	[Category("CollectionsC12Workflow")]
	public class CollectionLocalRestoreUserMetadataExecutorTests
	{
		[Test]
		public void RecognizedTransition_AllowsPerResourcePreimageOrDesiredMix()
		{
			CollectionLocalRestoreUserMetadataState preimage = State(
				new CollectionLocalRestoreUserSortState(true, 10, ModSortOrderAssignmentState.ExplicitNumeric, "100", "200"),
				CollectionLocalRestoreUserScreenshotState.None());
			CollectionLocalRestoreUserMetadataState desired = State(
				new CollectionLocalRestoreUserSortState(true, 25, ModSortOrderAssignmentState.InheritedNumeric, "100", "200"),
				CollectionLocalRestoreUserScreenshotState.CreatePresent(FOModScreenshotOverrideReadState.Current, "fomod\\shot.png",
					new string('a', 64), 4, 100, 200, 300, "artifact-shot"));
			CollectionLocalRestoreUserMetadataState transition = State(
				desired.Entries[0].Sort, preimage.Entries[0].Screenshot);

			Assert.IsTrue(CollectionLocalRestoreUserMetadataExecutor.IsRecognizedTransition(transition, preimage, desired));
		}

		[Test]
		public void RecognizedTransition_RejectsForeignMetadataValue()
		{
			CollectionLocalRestoreUserMetadataState preimage = State(
				new CollectionLocalRestoreUserSortState(true, 10, ModSortOrderAssignmentState.ExplicitNumeric, "100", "200"),
				CollectionLocalRestoreUserScreenshotState.None());
			CollectionLocalRestoreUserMetadataState desired = State(
				new CollectionLocalRestoreUserSortState(true, 25, ModSortOrderAssignmentState.InheritedNumeric, "100", "200"),
				CollectionLocalRestoreUserScreenshotState.None());
			CollectionLocalRestoreUserMetadataState foreign = State(
				new CollectionLocalRestoreUserSortState(true, 77, ModSortOrderAssignmentState.ExplicitNumeric, "100", "200"),
				CollectionLocalRestoreUserScreenshotState.None());

			Assert.IsFalse(CollectionLocalRestoreUserMetadataExecutor.IsRecognizedTransition(foreign, preimage, desired));
		}

		private static CollectionLocalRestoreUserMetadataState State(CollectionLocalRestoreUserSortState sort,
			CollectionLocalRestoreUserScreenshotState screenshot)
		{
			return new CollectionLocalRestoreUserMetadataState(new[]
			{
				new CollectionLocalRestoreUserMetadataEntry("captured-key", "live-key", @"C:\mods\example.7z", sort, screenshot)
			});
		}
	}
}
