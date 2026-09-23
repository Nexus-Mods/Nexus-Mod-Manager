using System;
using System.IO;
using System.Reflection;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.9 restart reconciliation and durable execution-evidence coverage.</summary>
	[TestFixture]
	public class CollectionNativeChildRestartReconciliationCoordinatorTests
	{
		private const string ShaA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void DetermineRestartDurability_CommittedRealityWinsAfterMissingTerminalCheckpoint()
		{
			Assert.AreEqual(ModOperationDurability.VerifiedCommitted,
				InvokeDurability(ModOperationDurability.Unknown, true, false));
		}

		[Test]
		public void DetermineRestartDurability_PriorCommittedCheckpointCannotBeDowngradedToRollback()
		{
			Assert.AreEqual(ModOperationDurability.Unknown,
				InvokeDurability(ModOperationDurability.VerifiedCommitted, false, true));
		}



		[Test]
		public void DetermineRestartDurability_IndistinguishablePreAndPostStateRemainsUnknown()
		{
			Assert.AreEqual(ModOperationDurability.Unknown,
				InvokeDurability(ModOperationDurability.Unknown, true, true));
		}

		[Test]
		public void DetermineRestartDurability_PriorNonCommitCannotBeRewrittenAsCommitByLaterMatchingState()
		{
			Assert.AreEqual(ModOperationDurability.Unknown,
				InvokeDurability(ModOperationDurability.NotStarted, true, false));
			Assert.AreEqual(ModOperationDurability.Unknown,
				InvokeDurability(ModOperationDurability.VerifiedRolledBack, true, false));
		}

		[Test]
		public void DetermineRestartDurability_ExactPreStateDistinguishesNotStartedFromRollback()
		{
			Assert.AreEqual(ModOperationDurability.NotStarted,
				InvokeDurability(ModOperationDurability.NotStarted, false, true));
			Assert.AreEqual(ModOperationDurability.VerifiedRolledBack,
				InvokeDurability(ModOperationDurability.Unknown, false, true));
		}

		[Test]
		public void MatchesContentFile_RejectsChangedBytesWithSameLength()
		{
			string path = Path.GetTempFileName();
			try
			{
				File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
				string hash = ComputeSha256(path);
				Assert.IsTrue(InvokeMatchesContent(path, CollectionContentHash.FromSha256(hash), 4));
				File.WriteAllBytes(path, new byte[] { 1, 9, 3, 4 });
				Assert.IsFalse(InvokeMatchesContent(path, CollectionContentHash.FromSha256(hash), 4));
			}
			finally { File.Delete(path); }
		}

		[Test]
		public void RecoveryManifestV2_RoundTripsExactRestartEvidence()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c69-test");
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c69");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c69", 1);
			CollectionMemberKey memberKey = CollectionMemberKey.FromProvider("member-c69");
			var member = new CollectionOperationMemberReference(revision, memberKey);
			CollectionPlanIdentity plan = CollectionPlanIdentity.From(Guid.NewGuid(), 1);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(target.Fingerprint, new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), "recipe-c69"));
			var child = new CollectionNativeChildOperation(1, member, CollectionNativeChildAction.ActivateOrReinstall,
				native, CollectionNativeChildCheckpoint.NativeSubmitted, null);
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ApplyResolvedPlan,
				collection, target, revision, plan, 7, CollectionOperationPhase.ApplyingNativeChildren,
				CollectionOperationResultState.Pending, new[] { child });
			var preview = new CollectionMemberEffectPreview(memberKey, CollectionRecipeIdentity.FromFingerprint("recipe-c69"),
				ModInstallMethod.Direct, ModInstallRoot.Data, new CollectionPlannedFileEffect[0],
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0],
				new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			var evidence = new CollectionNativeChildExecutionEvidence("game", 10, 20, "incoming.7z", preview,
				new CollectionNativeFileContentEvidence[0], new CollectionNativeFileContentEvidence[0],
				new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
				new CollectionExpectedReplayOperation[0]);
			var manifest = new CollectionNativeChildRecoveryManifest(operation.Identity, 1, plan, member,
				CollectionNativeChildAction.ActivateOrReinstall, native,
				new CollectionCurrentStateFingerprint("c6-native-state/1", "state-c69"),
				new CollectionRecoveryArtifact("incoming-artifact", CollectionContentHash.FromSha256(ShaA), 1),
				null, null, new CollectionScriptedReplayRecoverySnapshot(false, null, false, new CollectionReplayRecoveryPayload[0]), evidence);

			byte[] bytes = InvokeSerialize(manifest);
			using (var stream = new MemoryStream(bytes, false))
			using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
				Assert.AreEqual("nmm-ce.collections.child-recovery/2", reader.ReadString());

			CollectionNativeChildRecoveryManifest roundTrip = InvokeDeserialize(bytes, operation, child);
			Assert.IsNotNull(roundTrip.ExecutionEvidence);
			Assert.AreEqual("game", roundTrip.ExecutionEvidence.NexusGameDomain);
			Assert.AreEqual(10, roundTrip.ExecutionEvidence.NexusModId);
			Assert.AreEqual(20, roundTrip.ExecutionEvidence.NexusFileId);
			Assert.AreEqual("incoming.7z", roundTrip.ExecutionEvidence.IncomingFileName);
			Assert.AreEqual("recipe-c69", roundTrip.ExecutionEvidence.ReviewedEffects.RecipeIdentity.Fingerprint);
		}

		[Test]
		public void RecoveryManifestV3_RoundTripsCommittedTerminalFingerprint()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c69-terminal");
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c69-terminal");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c69-terminal", 1);
			CollectionMemberKey memberKey = CollectionMemberKey.FromProvider("member-c69-terminal");
			var member = new CollectionOperationMemberReference(revision, memberKey);
			CollectionPlanIdentity plan = CollectionPlanIdentity.From(Guid.NewGuid(), 1);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(target.Fingerprint, new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), "recipe-c69-terminal"));
			var child = new CollectionNativeChildOperation(1, member, CollectionNativeChildAction.ActivateOrReinstall,
				native, CollectionNativeChildCheckpoint.NativeTerminalObserved,
				new ModOperationResult(native, ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, null));
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ApplyResolvedPlan,
				collection, target, revision, plan, 8, CollectionOperationPhase.ApplyingNativeChildren,
				CollectionOperationResultState.Pending, new[] { child });
			var preview = new CollectionMemberEffectPreview(memberKey, CollectionRecipeIdentity.FromFingerprint("recipe-c69-terminal"),
				ModInstallMethod.Direct, ModInstallRoot.Data, new CollectionPlannedFileEffect[0],
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0],
				new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			var evidence = new CollectionNativeChildExecutionEvidence("game", 10, 20, "incoming.7z", preview,
				new CollectionNativeFileContentEvidence[0], new CollectionNativeFileContentEvidence[0],
				new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
				new CollectionExpectedReplayOperation[0]);
			var terminal = new CollectionCurrentStateFingerprint("c6-native-state/1", "terminal-c69");
			var manifest = new CollectionNativeChildRecoveryManifest(operation.Identity, 1, plan, member,
				CollectionNativeChildAction.ActivateOrReinstall, native,
				new CollectionCurrentStateFingerprint("c6-native-state/1", "state-c69"),
				new CollectionRecoveryArtifact("incoming-artifact", CollectionContentHash.FromSha256(ShaA), 1),
				null, null, new CollectionScriptedReplayRecoverySnapshot(false, null, false, new CollectionReplayRecoveryPayload[0]),
				evidence, terminal);

			byte[] bytes = InvokeSerialize(manifest);
			using (var stream = new MemoryStream(bytes, false))
			using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
				Assert.AreEqual("nmm-ce.collections.child-recovery/3", reader.ReadString());

			CollectionNativeChildRecoveryManifest roundTrip = InvokeDeserialize(bytes, operation, child);
			Assert.AreEqual(terminal, roundTrip.TerminalStateFingerprint);
			Assert.IsNull(roundTrip.SafeBoundaryStateFingerprint);
		}

		[Test]
		public void RecoveryManifestV4_RoundTripsCommittedTerminalAndSafeBoundaryFingerprints()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c69-safe-boundary");
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c69-safe-boundary");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c69-safe-boundary", 1);
			CollectionMemberKey memberKey = CollectionMemberKey.FromProvider("member-c69-safe-boundary");
			var member = new CollectionOperationMemberReference(revision, memberKey);
			CollectionPlanIdentity plan = CollectionPlanIdentity.From(Guid.NewGuid(), 1);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(target.Fingerprint, new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), "recipe-c69-safe-boundary"));
			var child = new CollectionNativeChildOperation(1, member, CollectionNativeChildAction.ActivateOrReinstall,
				native, CollectionNativeChildCheckpoint.Reconciled, new ModOperationResult(native, ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, null));
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ApplyResolvedPlan,
				collection, target, revision, plan, 8, CollectionOperationPhase.ApplyingNativeChildren,
				CollectionOperationResultState.Pending, new[] { child });
			var preview = new CollectionMemberEffectPreview(memberKey, CollectionRecipeIdentity.FromFingerprint("recipe-c69-safe-boundary"),
				ModInstallMethod.Direct, ModInstallRoot.Data, new CollectionPlannedFileEffect[0],
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0],
				new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			var evidence = new CollectionNativeChildExecutionEvidence("game", 10, 20, "incoming.7z", preview,
				new CollectionNativeFileContentEvidence[0], new CollectionNativeFileContentEvidence[0],
				new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
				new CollectionExpectedReplayOperation[0]);
			var terminal = new CollectionCurrentStateFingerprint("c6-native-state/1", "terminal-c69");
			var safeBoundary = new CollectionCurrentStateFingerprint("c6-native-state/1", "safe-c69");
			var manifest = new CollectionNativeChildRecoveryManifest(operation.Identity, 1, plan, member,
				CollectionNativeChildAction.ActivateOrReinstall, native,
				new CollectionCurrentStateFingerprint("c6-native-state/1", "state-c69"),
				new CollectionRecoveryArtifact("incoming-artifact", CollectionContentHash.FromSha256(ShaA), 1),
				null, null, new CollectionScriptedReplayRecoverySnapshot(false, null, false, new CollectionReplayRecoveryPayload[0]),
				evidence, terminal, safeBoundary);

			byte[] bytes = InvokeSerialize(manifest);
			using (var stream = new MemoryStream(bytes, false))
			using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
				Assert.AreEqual("nmm-ce.collections.child-recovery/4", reader.ReadString());

			CollectionNativeChildRecoveryManifest roundTrip = InvokeDeserialize(bytes, operation, child);
			Assert.AreEqual(terminal, roundTrip.TerminalStateFingerprint);
			Assert.AreEqual(safeBoundary, roundTrip.SafeBoundaryStateFingerprint);
		}

		[Test]
		public void ExecutionEvidence_RequiresExactReviewedFileCoverage()
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\a.dds");
			var preview = new CollectionMemberEffectPreview(CollectionMemberKey.FromProvider("member"),
				CollectionRecipeIdentity.FromFingerprint("recipe"), ModInstallMethod.Direct, ModInstallRoot.Data,
				new[] { new CollectionPlannedFileEffect(target) }, new CollectionPlannedIniEffect[0],
				new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			Assert.Throws<ArgumentException>(() => new CollectionNativeChildExecutionEvidence("game", 1, 2, "a.7z", preview,
				new CollectionNativeFileContentEvidence[0], new CollectionNativeFileContentEvidence[0],
				new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
				new CollectionExpectedReplayOperation[0]));
		}

		private static ModOperationDurability InvokeDurability(ModOperationDurability prior, bool committed, bool rolledBack)
		{
			MethodInfo method = typeof(CollectionNativeChildRestartReconciliationCoordinator).GetMethod(
				"DetermineRestartDurability", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			return (ModOperationDurability)method.Invoke(null, new object[] { prior, committed, rolledBack });
		}

		private static bool InvokeMatchesContent(string path, CollectionContentHash hash, long length)
		{
			MethodInfo method = typeof(CollectionNativeChildRestartReconciliationCoordinator).GetMethod(
				"MatchesContentFile", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			return (bool)method.Invoke(null, new object[] { path, hash, length });
		}

		private static byte[] InvokeSerialize(CollectionNativeChildRecoveryManifest manifest)
		{
			MethodInfo method = typeof(CollectionsNativeChildRecoveryManifestStore).GetMethod(
				"Serialize", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			return (byte[])method.Invoke(null, new object[] { manifest });
		}

		private static CollectionNativeChildRecoveryManifest InvokeDeserialize(byte[] bytes, CollectionOperation operation,
			CollectionNativeChildOperation child)
		{
			MethodInfo method = typeof(CollectionsNativeChildRecoveryManifestStore).GetMethod(
				"Deserialize", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			using (var stream = new MemoryStream(bytes, false))
			using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
				return (CollectionNativeChildRecoveryManifest)method.Invoke(null, new object[] { reader, operation, child });
		}

		private static string ComputeSha256(string path)
		{
			using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
				return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", String.Empty).ToLowerInvariant();
		}
	}
}
