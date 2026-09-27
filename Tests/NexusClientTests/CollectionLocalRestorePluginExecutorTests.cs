using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionLocalRestorePluginExecutorTests
	{
		[Test]
		public void ExactPluginState_DetectsActivationAndOrderChanges()
		{
			var captured = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\A.esm", true, 0, 0, "00"),
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\B.esp", false, 1, null, "")
			});
			var same = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry(@"c:\\game\\data\\A.esm", true, 0, 0, "00"),
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\B.esp", false, 1, null, "")
			});
			var activationChanged = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\A.esm", true, 0, 0, "00"),
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\B.esp", true, 1, 1, "01")
			});
			var orderChanged = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\B.esp", false, 0, null, ""),
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\A.esm", true, 1, 0, "00")
			});

			Assert.IsTrue(captured.Equals(same));
			Assert.IsFalse(captured.Equals(activationChanged));
			Assert.IsFalse(captured.Equals(orderChanged));
		}

		[Test]
		public void ExactPluginState_DetectsAllocatedIndexOrModIndexChanges()
		{
			var captured = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry("A.esm", true, 0, 0, "00")
			});
			var allocatedChanged = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry("A.esm", true, 0, 1, "01")
			});

			Assert.IsFalse(captured.Equals(allocatedChanged));
		}
	}
}
