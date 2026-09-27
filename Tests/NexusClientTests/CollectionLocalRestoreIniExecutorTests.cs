using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsGateL")]
	public class CollectionLocalRestoreIniExecutorTests
	{
		[Test]
		public void ExactIniState_DetectsOwnerHistoryAndEffectiveValueChanges()
		{
			var captured = new CollectionLocalRestoreIniState(new[]
			{
				new CollectionLocalRestoreIniEntry(@"C:\\Game\\settings.ini", "General", "Mode", new[]
				{
					new CollectionLocalRestoreIniOwner("ORIGINAL", CollectionNativeEffectOwnerKind.OriginalValue, "base"),
					new CollectionLocalRestoreIniOwner("MOD-A", CollectionNativeEffectOwnerKind.NativeMod, "captured")
				}, "captured")
			});
			var same = new CollectionLocalRestoreIniState(new[]
			{
				new CollectionLocalRestoreIniEntry(@"c:\\game\\SETTINGS.ini", "general", "mode", new[]
				{
					new CollectionLocalRestoreIniOwner("original", CollectionNativeEffectOwnerKind.OriginalValue, "base"),
					new CollectionLocalRestoreIniOwner("mod-a", CollectionNativeEffectOwnerKind.NativeMod, "captured")
				}, "captured")
			});
			var historyChanged = new CollectionLocalRestoreIniState(new[]
			{
				new CollectionLocalRestoreIniEntry(@"C:\\Game\\settings.ini", "General", "Mode", new[]
				{
					new CollectionLocalRestoreIniOwner("ORIGINAL", CollectionNativeEffectOwnerKind.OriginalValue, "other"),
					new CollectionLocalRestoreIniOwner("MOD-A", CollectionNativeEffectOwnerKind.NativeMod, "captured")
				}, "captured")
			});
			var effectiveChanged = new CollectionLocalRestoreIniState(new[]
			{
				new CollectionLocalRestoreIniEntry(@"C:\\Game\\settings.ini", "General", "Mode", new[]
				{
					new CollectionLocalRestoreIniOwner("ORIGINAL", CollectionNativeEffectOwnerKind.OriginalValue, "base"),
					new CollectionLocalRestoreIniOwner("MOD-A", CollectionNativeEffectOwnerKind.NativeMod, "captured")
				}, "drifted")
			});
			Assert.IsTrue(captured.Equals(same));
			Assert.IsFalse(captured.Equals(historyChanged));
			Assert.IsFalse(captured.Equals(effectiveChanged));
		}
	}
}
