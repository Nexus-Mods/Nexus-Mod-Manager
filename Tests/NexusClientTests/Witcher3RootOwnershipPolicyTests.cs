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

		private static Witcher3RootOwnershipDecision CheckWrite(string currentRoot, string otherRoot,
			bool currentIsRemastered, bool otherHasState = false, bool otherConfigured = true,
			string currentInfo = @"C:\NMM\W3R\InstallInfo",
			string otherInfo = @"C:\NMM\W3Legacy\InstallInfo")
		{
			return Witcher3RootOwnershipPolicy.EvaluateNativeMutation(currentRoot, otherRoot,
				currentInfo, otherInfo, otherHasState, otherConfigured, currentIsRemastered);
		}

		[Test]
		public void EmptySharedVirtualInstallIsBlockedForBothNativeOwners()
		{
			var result = Witcher3RootOwnershipPolicy.EvaluateNativeStorage(
				@"C:\NMM\VirtualInstall", @"c:\nmm\virtualinstall\",
				@"C:\NMM\RemasteredInfo", @"C:\NMM\LegacyInfo");
			Assert.That(result.Status, Is.EqualTo(Witcher3RootOwnershipStatus.SharedVirtualInstall));
		}

		[Test]
		public void NestedVirtualInstallFoldersAreBlockedInBothDirections()
		{
			Assert.That(Witcher3RootOwnershipPolicy.EvaluateNativeStorage(
				@"C:\NMM\VirtualInstall", @"C:\NMM\VirtualInstall\Nested", null, null).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.OverlappingVirtualInstall));
			Assert.That(Witcher3RootOwnershipPolicy.EvaluateNativeStorage(
				@"C:\NMM\VirtualInstall\Nested", @"C:\NMM\VirtualInstall", null, null).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.OverlappingVirtualInstall));
		}

		[Test]
		public void NativeStorageRolesCannotNestAcrossGameModes()
		{
			Assert.That(Witcher3RootOwnershipPolicy.EvaluateNativeStorage(
				@"C:\NMM\LegacyInfo\VirtualInstall", null, @"C:\NMM\RemasteredInfo",
				@"C:\NMM\LegacyInfo").Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.OverlappingNativeStorageRoles));
			Assert.That(Witcher3RootOwnershipPolicy.EvaluateNativeStorage(
				@"C:\NMM\RemasteredVirtual", @"C:\NMM\RemasteredInfo\VirtualInstall",
				@"C:\NMM\RemasteredInfo", @"D:\NMM\LegacyInfo").Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.OverlappingNativeStorageRoles));
		}

		[Test]
		public void NestedInstallInfoFoldersAreBlockedEvenWithoutVirtualFolders()
		{
			Assert.That(Witcher3RootOwnershipPolicy.EvaluateNativeStorage(null, null,
				@"C:\NMM\NativeInfo\Child", @"C:\NMM\NativeInfo").Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.OverlappingInstallInfo));
		}

		[Test]
		public void UnknownCurrentVirtualWithKnownOtherVirtualFailsClosed()
		{
			Assert.That(Witcher3RootOwnershipPolicy.EvaluateNativeStorage(
				null, @"C:\NMM\VirtualInstall", null, null).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.UnverifiablePriorOwner));
		}

		[Test]
		public void DifferentNativeStoresAndAdjacentFolderNamesRemainIndependent()
		{
			Assert.That(Witcher3RootOwnershipPolicy.EvaluateNativeStorage(
				@"C:\NMM\W3\VirtualInstall", @"C:\NMM\W3_backup\VirtualInstall",
				@"C:\NMM\InfoA", @"C:\NMM\InfoB").AllowsNativeMutation, Is.True);
			Assert.That(Witcher3RootOwnershipPolicy.EvaluateNativeStorage(
				@"C:\NMM\Remastered\VirtualInstall", null,
				@"C:\NMM\Remastered\Info", @"C:\NMM\Legacy\Info").AllowsNativeMutation, Is.True);
		}

		[Test]
		public void HistoricalInstallInfoClaimsRejectSameOrNestedPhysicalLocations()
		{
			Assert.That(Witcher3RootOwnershipPolicy.EvaluateHistoricalInstallInfo(
				@"C:\NMM\OldInfo", @"c:\nmm\oldinfo\").Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.SharedInstallInfo));
			Assert.That(Witcher3RootOwnershipPolicy.EvaluateHistoricalInstallInfo(
				@"C:\NMM\OldInfo\Child", @"C:\NMM\OldInfo").Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.OverlappingHistoricalInstallInfo));
			Assert.That(Witcher3RootOwnershipPolicy.EvaluateHistoricalInstallInfo(
				@"C:\NMM\NewInfo", @"C:\NMM\OldInfo").AllowsNativeMutation, Is.True);
		}

		[Test]
		public void RemasteredWriteIntoDiscoveredUnownedLegacyRootIsBlocked()
		{
			const string sameRoot = @"C:\Games\Witcher3";
			Assert.That(Check(sameRoot, sameRoot, otherHasState: false).AllowsNormalInitialization, Is.True);
			Assert.That(CheckWrite(sameRoot, sameRoot, currentIsRemastered: true).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.SharedConfiguredGameRoot));
		}

		[Test]
		public void LegacyWritesRemainAllowedWhenRemasteredWasOnlyDiscovered()
		{
			Assert.That(CheckWrite(@"C:\Games\W3", @"c:\games\w3\",
				currentIsRemastered: false).AllowsNormalInitialization, Is.True);
		}

		[Test]
		public void LegacyWriteIsBlockedIfRemasteredAlreadyHasNativeState()
		{
			Assert.That(CheckWrite(@"C:\Games\W3", @"C:\Games\W3",
				currentIsRemastered: false, otherHasState: true).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.SharedManagedGameRoot));
		}

		[Test]
		public void RemasteredWriteRejectsUnownedNestedGameRootInBothDirections()
		{
			Assert.That(CheckWrite(@"C:\Games\W3", @"C:\Games\W3\Mods", true).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.OverlappingConfiguredGameRoots));
			Assert.That(CheckWrite(@"C:\Games\W3\Mods", @"C:\Games\W3", true).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.OverlappingConfiguredGameRoots));
		}

		[Test]
		public void IndependentRootsAndUnconfiguredOtherModeStillAllowWrites()
		{
			Assert.That(CheckWrite(@"C:\Games\W3R", @"D:\Games\W3",
				currentIsRemastered: true).AllowsNormalInitialization, Is.True);
			Assert.That(CheckWrite(@"C:\Games\W3R", null,
				currentIsRemastered: true, otherConfigured: false).AllowsNormalInitialization, Is.True);
		}

		[Test]
		public void NativeMutationRetainsSharedInstallInfoAndUnverifiableOwnerBlockers()
		{
			Assert.That(CheckWrite(@"C:\Games\W3R", @"D:\Games\W3", true,
				currentInfo: @"C:\NMM\Shared", otherInfo: @"c:\nmm\shared").Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.SharedInstallInfo));
			Assert.That(CheckWrite(@"C:\Games\W3R", null, true, otherHasState: true).Status,
				Is.EqualTo(Witcher3RootOwnershipStatus.UnverifiablePriorOwner));
		}

		[Test]
		public void NativeMutationDoesNotMistakeAdjacentRootsForNested()
		{
			Assert.That(CheckWrite(@"C:\Games\W3", @"C:\Games\W3_backup", true)
				.AllowsNormalInitialization, Is.True);
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
