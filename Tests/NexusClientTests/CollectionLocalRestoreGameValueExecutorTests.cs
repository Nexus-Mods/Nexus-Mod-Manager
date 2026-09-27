using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsGateL")]
	public class CollectionLocalRestoreGameValueExecutorTests
	{
		[Test]
		public void ExactGameSpecificState_DetectsOwnerHistoryAndPhysicalByteChanges()
		{
			var captured = new CollectionLocalRestoreGameValueState(new[]
			{
				new CollectionLocalRestoreGameValueEntry("sdp:1/TestShader", new[]
				{
					new CollectionLocalRestoreGameValueOwner("ORIGINAL", CollectionNativeEffectOwnerKind.OriginalValue, new byte[] { 1, 2 }),
					new CollectionLocalRestoreGameValueOwner("MOD-A", CollectionNativeEffectOwnerKind.NativeMod, new byte[] { 3, 4 })
				}, new byte[] { 3, 4 })
			});
			var same = new CollectionLocalRestoreGameValueState(new[]
			{
				new CollectionLocalRestoreGameValueEntry("sdp:1/TestShader", new[]
				{
					new CollectionLocalRestoreGameValueOwner("original", CollectionNativeEffectOwnerKind.OriginalValue, new byte[] { 1, 2 }),
					new CollectionLocalRestoreGameValueOwner("mod-a", CollectionNativeEffectOwnerKind.NativeMod, new byte[] { 3, 4 })
				}, new byte[] { 3, 4 })
			});
			var historyChanged = new CollectionLocalRestoreGameValueState(new[]
			{
				new CollectionLocalRestoreGameValueEntry("sdp:1/TestShader", new[]
				{
					new CollectionLocalRestoreGameValueOwner("ORIGINAL", CollectionNativeEffectOwnerKind.OriginalValue, new byte[] { 9 }),
					new CollectionLocalRestoreGameValueOwner("MOD-A", CollectionNativeEffectOwnerKind.NativeMod, new byte[] { 3, 4 })
				}, new byte[] { 3, 4 })
			});
			var physicalChanged = new CollectionLocalRestoreGameValueState(new[]
			{
				new CollectionLocalRestoreGameValueEntry("sdp:1/TestShader", new[]
				{
					new CollectionLocalRestoreGameValueOwner("ORIGINAL", CollectionNativeEffectOwnerKind.OriginalValue, new byte[] { 1, 2 }),
					new CollectionLocalRestoreGameValueOwner("MOD-A", CollectionNativeEffectOwnerKind.NativeMod, new byte[] { 3, 4 })
				}, new byte[] { 5, 6 })
			});

			Assert.IsTrue(captured.Equals(same));
			Assert.IsFalse(captured.Equals(historyChanged));
			Assert.IsFalse(captured.Equals(physicalChanged));
		}

		[Test]
		public void ExactGameSpecificState_TreatsKeysAsCaseSensitiveNativeIdentities()
		{
			var first = new CollectionLocalRestoreGameValueState(new[]
			{
				new CollectionLocalRestoreGameValueEntry("sdp:1/TestShader", new CollectionLocalRestoreGameValueOwner[0], null)
			});
			var second = new CollectionLocalRestoreGameValueState(new[]
			{
				new CollectionLocalRestoreGameValueEntry("SDP:1/TestShader", new CollectionLocalRestoreGameValueOwner[0], null)
			});

			Assert.IsFalse(first.Equals(second));
		}
	}
}
