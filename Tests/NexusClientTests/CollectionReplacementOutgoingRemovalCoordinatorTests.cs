using System;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsGateA")]
	public class CollectionReplacementOutgoingRemovalCoordinatorTests
	{
		[Test]
		public void BuildReviewedRemovalSet_UsesOnlyExplicitRemoveDecisionsAndRecordedInstallContext()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-4-removal-set");
			CollectionNativeModState remove = CreateNativeMod(target, "native-remove", ModInstallMethod.Direct, ModInstallRoot.Data);
			CollectionNativeModState protect = CreateNativeMod(target, "native-protect", ModInstallMethod.Virtual, ModInstallRoot.Data);
			CollectionNativeStateIndex state = CreateState(target, remove, protect);
			var current = new CollectionReplacementCurrentSetupSnapshot(state,
				new CollectionDriftObservation[0], new NativeModProvenance[0]);
			CollectionReplacementReviewedIntent intent = CreateIntent(target, current,
				new CollectionReplacementNativeApproval("native-remove", CollectionReplacementNativeDecision.Remove),
				new CollectionReplacementNativeApproval("native-protect", CollectionReplacementNativeDecision.Protected));

			var requests = CollectionReplacementOutgoingRemovalCoordinator.BuildReviewedRemovalSet(intent, current);

			Assert.That(requests.Count, Is.EqualTo(1));
			Assert.That(requests[0].NativeMod.Identity.NativeModKey, Is.EqualTo("native-remove"));
			Assert.That(requests[0].InstallContext.Method, Is.EqualTo(ModInstallMethod.Direct));
			Assert.That(requests[0].InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.Data));
		}

		[Test]
		public void BuildReviewedRemovalSet_RejectsApprovalForNativeInstanceMissingFromExactCurrentSetup()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-4-missing");
			CollectionNativeModState present = CreateNativeMod(target, "native-present", ModInstallMethod.Virtual, ModInstallRoot.Data);
			CollectionNativeStateIndex state = CreateState(target, present);
			var current = new CollectionReplacementCurrentSetupSnapshot(state,
				new CollectionDriftObservation[0], new NativeModProvenance[0]);
			CollectionReplacementReviewedIntent intent = CreateIntent(target, current,
				new CollectionReplacementNativeApproval("native-missing", CollectionReplacementNativeDecision.Remove));

			Assert.Throws<InvalidOperationException>(() =>
				CollectionReplacementOutgoingRemovalCoordinator.BuildReviewedRemovalSet(intent, current));
		}

		[Test]
		public void RemovalJournalKey_IsDeterministicPerOperationAndNativeKey()
		{
			CollectionOperationIdentity operation = CollectionOperationIdentity.From(Guid.Parse("11111111-2222-3333-4444-555555555555"));

			CollectionMemberKey first = CollectionReplacementOutgoingRemovalCoordinator.CreateRemovalJournalKey(operation, "native-a");
			CollectionMemberKey same = CollectionReplacementOutgoingRemovalCoordinator.CreateRemovalJournalKey(operation, "native-a");
			CollectionMemberKey different = CollectionReplacementOutgoingRemovalCoordinator.CreateRemovalJournalKey(operation, "native-b");

			Assert.That(first, Is.EqualTo(same));
			Assert.That(first, Is.Not.EqualTo(different));
			Assert.That(first.Kind, Is.EqualTo(CollectionMemberKeyKind.Local));
		}

		[Test]
		public void DetermineDurability_AuthoritativeAbsenceWinsOverReportedFailure()
		{
			ModOperationIdentity identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-c8-4-durability", new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), null));
			var reported = new ModOperationResult(identity, ModOperationReportedStatus.Failed, ModOperationDurability.Unknown, null);

			ModOperationDurability durability = CollectionReplacementOutgoingRemovalCoordinator.DetermineDurability(
				reported, true, true, false);

			Assert.That(durability, Is.EqualTo(ModOperationDurability.VerifiedCommitted));
		}

		[Test]
		public void ReplacementOperationPhases_DistinguishOutgoingExecutionFromVerifiedBarrier()
		{
			Assert.That(CollectionOperationPhase.RemovingOutgoingNativeChildren, Is.Not.EqualTo(CollectionOperationPhase.ApplyingNativeChildren));
			Assert.That(CollectionOperationPhase.OutgoingRemovalVerified, Is.Not.EqualTo(CollectionOperationPhase.PausedAtSafeBoundary));
		}

		private static CollectionReplacementReviewedIntent CreateIntent(CollectionTargetIdentity target,
			CollectionReplacementCurrentSetupSnapshot current, params CollectionReplacementNativeApproval[] approvals)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("incoming-c8-4");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c8-4", 1);
			return new CollectionReplacementReviewedIntent(CollectionPlanIdentity.From(
				Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), 1), revision, target,
				current.NativeState.Fingerprint, current.DecisionFingerprint, "diff-c8-4", "environment-c8-4",
				CollectionReplacementBackupChoice.ContinueWithoutLocalCollection, true,
				CollectionReplacementProfileProtectionSnapshot.NoCurrentProfile(), approvals,
				new CollectionReplacementAssociationApproval[0], new CollectionReplacementPreparedRecipeApproval[0], new CollectionReplacementFileWinnerApproval[0]);
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target,
			params CollectionNativeModState[] mods)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], mods,
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
		}

		private static CollectionNativeModState CreateNativeMod(CollectionTargetIdentity target, string nativeKey,
			ModInstallMethod method, ModInstallRoot root)
		{
			return new CollectionNativeModState(new NativeModInstanceIdentity(target, nativeKey),
				"C:\\Mods\\" + nativeKey + ".7z", nativeKey + ".7z", "100", "200", "1.0", "1.0.0.0", root, method);
		}
	}
}
