using System;
using System.Collections.Generic;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	[Category("CollectionsGateA")]
	public class CollectionReplacementPhaseBarrierCoordinatorTests
	{
		[Test]
		public void ClassifyBarrier_ExactEnvironmentAndRecipeEvidence_IsVerified()
		{
			CollectionReplacementPreparedRecipeApproval approved = Recipe("member-a", "provider-a", "prepared-a", "effects-a",
				ModInstallMethod.Virtual, ModInstallRoot.Data, "adapter-a", 1);
			var effective = new Dictionary<string, CollectionReplacementPreparedRecipeApproval>(StringComparer.Ordinal) {
				{ approved.MemberKey.ToString(), approved }
			};
			string message;

			CollectionReplacementBarrierDisposition result = CollectionReplacementPhaseBarrierCoordinator.ClassifyBarrier(
				"env-a", "env-a", true, null, effective, new[] { approved.MemberKey }, new[] { approved }, out message);

			Assert.That(result, Is.EqualTo(CollectionReplacementBarrierDisposition.Verified));
			Assert.That(message, Does.Contain("exactly match"));
		}

		[Test]
		public void ClassifyBarrier_ChangedPreparedOrEffectIdentity_RequiresExplicitReview()
		{
			CollectionReplacementPreparedRecipeApproval approved = Recipe("member-a", "provider-a", "prepared-a", "effects-a",
				ModInstallMethod.Direct, ModInstallRoot.Data, "adapter-a", 2);
			CollectionReplacementPreparedRecipeApproval observed = Recipe("member-a", "provider-a", "prepared-b", "effects-b",
				ModInstallMethod.Direct, ModInstallRoot.Data, "adapter-a", 2);
			var effective = new Dictionary<string, CollectionReplacementPreparedRecipeApproval>(StringComparer.Ordinal) {
				{ approved.MemberKey.ToString(), approved }
			};
			string message;

			CollectionReplacementBarrierDisposition result = CollectionReplacementPhaseBarrierCoordinator.ClassifyBarrier(
				"env-a", "env-a", true, null, effective, new[] { approved.MemberKey }, new[] { observed }, out message);

			Assert.That(result, Is.EqualTo(CollectionReplacementBarrierDisposition.ExplicitReviewRequired));
			Assert.That(message, Does.Contain("Re-prepared"));
		}

		[Test]
		public void ClassifyBarrier_ChangedConditionEnvironment_RequiresExplicitReview()
		{
			CollectionReplacementPreparedRecipeApproval approved = Recipe("member-a", "provider-a", "prepared-a", "effects-a",
				ModInstallMethod.Virtual, ModInstallRoot.Data, "adapter-a", 1);
			var effective = new Dictionary<string, CollectionReplacementPreparedRecipeApproval>(StringComparer.Ordinal) {
				{ approved.MemberKey.ToString(), approved }
			};
			string message;

			CollectionReplacementBarrierDisposition result = CollectionReplacementPhaseBarrierCoordinator.ClassifyBarrier(
				"env-reviewed", "env-observed", true, null, effective, new[] { approved.MemberKey }, new[] { approved }, out message);

			Assert.That(result, Is.EqualTo(CollectionReplacementBarrierDisposition.ExplicitReviewRequired));
			Assert.That(message, Does.Contain("condition state"));
		}

		[Test]
		public void ClassifyBarrier_ChangedEnvironmentAfterIncomingNativeWork_RequiresRecovery()
		{
			CollectionReplacementPreparedRecipeApproval approved = Recipe("member-a", "provider-a", "prepared-a", "effects-a",
				ModInstallMethod.Virtual, ModInstallRoot.Data, "adapter-a", 1);
			var effective = new Dictionary<string, CollectionReplacementPreparedRecipeApproval>(StringComparer.Ordinal) {
				{ approved.MemberKey.ToString(), approved }
			};
			string message;

			CollectionReplacementBarrierDisposition result = CollectionReplacementPhaseBarrierCoordinator.ClassifyBarrier(
				"env-reviewed", "env-observed", true, null, effective, new[] { approved.MemberKey }, new[] { approved }, false, out message);

			Assert.That(result, Is.EqualTo(CollectionReplacementBarrierDisposition.RecoveryRequired));
			Assert.That(message, Does.Contain("requires recovery"));
		}

		[Test]
		public void ClassifyBarrier_ChangedProviderContextOrAdapter_CannotBeAmended()
		{
			CollectionReplacementPreparedRecipeApproval approved = Recipe("member-a", "provider-a", "prepared-a", "effects-a",
				ModInstallMethod.Virtual, ModInstallRoot.Data, "adapter-a", 1);
			CollectionReplacementPreparedRecipeApproval observed = Recipe("member-a", "provider-b", "prepared-b", "effects-b",
				ModInstallMethod.Direct, ModInstallRoot.GameRoot, "adapter-b", 2);
			var effective = new Dictionary<string, CollectionReplacementPreparedRecipeApproval>(StringComparer.Ordinal) {
				{ approved.MemberKey.ToString(), approved }
			};
			string message;

			CollectionReplacementBarrierDisposition result = CollectionReplacementPhaseBarrierCoordinator.ClassifyBarrier(
				"env-a", "env-b", true, null, effective, new[] { approved.MemberKey }, new[] { observed }, out message);

			Assert.That(result, Is.EqualTo(CollectionReplacementBarrierDisposition.RecoveryRequired));
			Assert.That(message, Does.Contain("cannot be approved"));
		}

		[Test]
		public void ClassifyBarrier_UnprovableObservedEnvironment_RequiresRecovery()
		{
			var effective = new Dictionary<string, CollectionReplacementPreparedRecipeApproval>(StringComparer.Ordinal);
			string message;

			CollectionReplacementBarrierDisposition result = CollectionReplacementPhaseBarrierCoordinator.ClassifyBarrier(
				"env-a", "env-b", false, null, effective, new CollectionMemberKey[0],
				new CollectionReplacementPreparedRecipeApproval[0], out message);

			Assert.That(result, Is.EqualTo(CollectionReplacementBarrierDisposition.RecoveryRequired));
			Assert.That(message, Does.Contain("unprovable"));
		}

		[Test]
		public void ValidateSurvivingNativeDecisions_RejectsMissingProtectedInstanceAfterRemoval()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-5-survivor");
			CollectionNativeStateIndex state = CreateState(target);
			var current = new CollectionReplacementCurrentSetupSnapshot(state,
				new CollectionDriftObservation[0], new NativeModProvenance[0]);
			CollectionIdentity collection = CollectionIdentity.FromNexus("incoming-c8-5");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c8-5", 1);
			var intent = new CollectionReplacementReviewedIntent(CollectionPlanIdentity.From(
				Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-ffffffffffff"), 1), revision, target,
				state.Fingerprint, current.DecisionFingerprint, "diff-c8-5", "env-c8-5",
				CollectionReplacementBackupChoice.ContinueWithoutLocalCollection, true,
				CollectionReplacementProfileProtectionSnapshot.NoCurrentProfile(),
				new[] { new CollectionReplacementNativeApproval("native-protected", CollectionReplacementNativeDecision.Protected) },
				new CollectionReplacementAssociationApproval[0], new CollectionReplacementPreparedRecipeApproval[0], new CollectionReplacementFileWinnerApproval[0]);

			string failure = CollectionReplacementPhaseBarrierCoordinator.ValidateSurvivingNativeDecisions(intent, current);

			Assert.That(failure, Does.Contain("retained/protected"));
		}

		[Test]
		public void ReplacementOperationPhases_DoNotReusePreApplyReviewPhasesAfterOutgoingRemoval()
		{
			Assert.That(CollectionOperationPhase.AwaitingReplacementPhaseAmendment, Is.Not.EqualTo(CollectionOperationPhase.ReadyForReview));
			Assert.That(CollectionOperationPhase.ReadyForIncomingNativeChildren, Is.Not.EqualTo(CollectionOperationPhase.ReadyToApply));
			Assert.That(CollectionOperationPhase.ReplacementBarrierRevalidationRequired, Is.Not.EqualTo(CollectionOperationPhase.Revalidating));
			Assert.That((int)CollectionOperationPhase.AwaitingReplacementPhaseAmendment,
				Is.GreaterThan((int)CollectionOperationPhase.OutgoingRemovalVerified));
			Assert.That((int)CollectionOperationPhase.ReplacementBarrierRevalidationRequired,
				Is.GreaterThan((int)CollectionOperationPhase.ReadyForIncomingNativeChildren));
		}

		private static CollectionReplacementPreparedRecipeApproval Recipe(string member, string provider, string prepared,
			string effects, ModInstallMethod method, ModInstallRoot root, string adapter, int version)
		{
			return new CollectionReplacementPreparedRecipeApproval(CollectionMemberKey.FromProvider(member), provider, prepared,
				effects, method, root, adapter, version);
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new CollectionNativeModState[0],
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
		}
	}
}
