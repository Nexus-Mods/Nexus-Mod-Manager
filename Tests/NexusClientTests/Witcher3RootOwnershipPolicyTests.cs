using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3RootOwnershipPolicyTests
	{
		private static Witcher3RootOwnershipDecision Check(string currentRoot, string otherRoot,
			bool otherHasState = true, bool otherConfigured = true,
			string currentInfo = @"C:\NMM\W3R\InstallInfo",
			string otherInfo = @"C:\NMM\W3Legacy\InstallInfo")
		{
			return Witcher3RootOwnershipPolicy.Evaluate(currentRoot, otherRoot,
				currentInfo, otherInfo, otherHasState, otherConfigured);
		}

		[Test]
		public void SamePhysicalRootWithLegacyOwnership_IsBlocked()
		{
			var result = Check(@"C:\Games\Witcher3", @"c:\games\WITCHER3\");
			Assert.That(result.Status, Is.EqualTo(Witcher3RootOwnershipStatus.SharedManagedGameRoot));
			Assert.That(result.AllowsNormalInitialization, Is.False);
		}

		[Test]
		public void SamePhysicalRootWithOnlyOtherEditionDiscovered_DoesNotDisableWorkingLegacyMode()
		{
			var result = Check(@"C:\Games\Witcher3", @"C:\Games\Witcher3", otherHasState: false);
			Assert.That(result.AllowsNormalInitialization, Is.True);
		}

		[Test]
		public void DifferentPhysicalInstallsWithIndependentStorage_AreAllowed()
		{
			var result = Check(@"C:\Games\Classic", @"D:\Games\Remastered");
			Assert.That(result.AllowsNormalInitialization, Is.True);
		}

		[Test]
		public void DifferentRootsButSamePhysicalInstallInfo_AreBlocked()
		{
			var result = Check(@"C:\Classic", @"D:\Remastered", currentInfo: @"C:\Shared\Info",
				otherInfo: @"c:\shared\info\");
			Assert.That(result.Status, Is.EqualTo(Witcher3RootOwnershipStatus.SharedInstallInfo));
		}

		[Test]
		public void EmptyOtherConfigurationWithoutState_IsAllowed()
		{
			var result = Check(@"C:\Games\W3", null, otherHasState: false, otherConfigured: false);
			Assert.That(result.AllowsNormalInitialization, Is.True);
		}

		[Test]
		public void PriorOwnerWithUnknownRoot_IsFailClosed()
		{
			var result = Check(@"C:\Games\W3", null);
			Assert.That(result.Status, Is.EqualTo(Witcher3RootOwnershipStatus.UnverifiablePriorOwner));
		}

		[Test]
		public void UnknownCurrentRootWithOtherConfigured_IsFailClosed()
		{
			var result = Check(null, @"C:\Games\W3", otherHasState: false);
			Assert.That(result.Status, Is.EqualTo(Witcher3RootOwnershipStatus.UnverifiablePriorOwner));
		}

		[Test]
		public void NestedRootsWithNativeOwner_AreBlockedInBothDirections()
		{
			Assert.That(Check(@"C:\Games\W3", @"C:\Games\W3\Mods").Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.OverlappingManagedGameRoots));
			Assert.That(Check(@"C:\Games\W3\Mods", @"C:\Games\W3").Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.OverlappingManagedGameRoots));
		}

		[Test]
		public void AdjacentNamesDoNotFalsePositiveAsNested()
		{
			Assert.That(Check(@"C:\Games\W3", @"C:\Games\W3_backup").AllowsNormalInitialization, Is.True);
		}

		[Test]
		public void PhysicalIdentityHandlesAliasWhenAlreadyResolved()
		{
			// Windows resolver can produce the same NT device path for a junction and its target.
			Assert.That(Check(@"\Device\HarddiskVolume3\W3", @"\device\harddiskvolume3\w3").AllowsNormalInitialization,
				Is.False);
		}

		[Test]
		public void InitialSetupWithNoNativeOwnerAndDifferentInfo_IsAllowed()
		{
			Assert.That(Check(@"C:\Games\W3", @"C:\Games\W3", otherHasState: false,
					currentInfo: @"C:\NMM\New", otherInfo: @"C:\NMM\Old").AllowsNormalInitialization,
				Is.True);
		}

		[Test]
		public void ExistingOwnerWithCaseInsensitiveInfoAlias_IsBlocked()
		{
			Assert.That(Check(@"C:\Games\W3Old", @"C:\Games\W3New",
					currentInfo: @"\Device\Volume2\NMM", otherInfo: @"\device\volume2\nmm").Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.SharedInstallInfo));
		}
	}
}
