using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.9 restart reconciliation and durable execution-evidence coverage.</summary>
	[TestFixture]
	[Category("CollectionsC12FailureInjection")]
	[Category("CollectionsGateA")]
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
		public void BuildVerificationDiagnostics_ReportsBothFailedRecoveryPredicates()
		{
			MethodInfo method = typeof(CollectionNativeChildRestartReconciliationCoordinator).GetMethod(
				"BuildVerificationDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			string diagnostics = (string)method.Invoke(null, new object[]
			{
				false, "native candidate count is 0",
				false, "preparation fingerprint mismatch",
				false, "unsafe residue"
			});

			StringAssert.Contains("committed verification: FAIL - native candidate count is 0", diagnostics);
			StringAssert.Contains("rollback verification: FAIL - preparation fingerprint mismatch", diagnostics);
			StringAssert.Contains("exact rollback residue repair: NOT APPLIED - unsafe residue", diagnostics);
		}

		[Test]
		public void BuildVerificationDiagnostics_ReportsAppliedExactRollbackResidueRepair()
		{
			MethodInfo method = typeof(CollectionNativeChildRestartReconciliationCoordinator).GetMethod(
				"BuildVerificationDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			string diagnostics = (string)method.Invoke(null, new object[]
			{
				false, "native candidate count is 0",
				true, null,
				true, "Removed exact child postimage residue"
			});

			StringAssert.Contains("rollback verification: PASS", diagnostics);
			StringAssert.Contains("exact rollback residue repair: APPLIED - Removed exact child postimage residue", diagnostics);
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

		[TestCase("previous", true, 1)]
		[TestCase("replacement", true, 0)]
		[TestCase("foreign-bytes", false, 0)]
		[TestCase("missing-file", false, 0)]
		[TestCase("changed-cache", false, 0)]
		[TestCase("foreign-owner", false, 0)]
		[TestCase("different-source", false, 0)]
		[TestCase("promoted", false, 0)]
		[TestCase("missing-install-log", false, 0)]
		[TestCase("unreviewed-file", false, 0)]
		[TestCase("direct", false, 0)]
		public void ExactVirtualRelink_RequiresReviewedCacheAndUnchangedOwnedGameBytes(string scenario, bool accepted, int count)
		{
			string directory = Path.Combine(Path.GetTempPath(), "c12-exact-relink-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				string deployed = Path.Combine(directory, "game.bin");
				string source = Path.Combine(directory, "staged.bin");
				File.WriteAllBytes(deployed, new byte[] { 1, 2, 3, 4 });
				File.WriteAllBytes(source, new byte[] { 5, 6, 7, 8 });
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "mod.bin");
				var preimage = new CollectionNativeFileContentEvidence(target, true, CollectionContentHash.FromSha256(ComputeSha256(deployed)), 4);
				var expected = new CollectionNativeFileContentEvidence(target, true, CollectionContentHash.FromSha256(ComputeSha256(source)), 4);
				if (scenario == "replacement") File.Copy(source, deployed, true);
				if (scenario == "foreign-bytes") File.WriteAllBytes(deployed, new byte[] { 1, 9, 3, 4 });
				if (scenario == "missing-file") File.Delete(deployed);
				if (scenario == "changed-cache") File.WriteAllBytes(source, new byte[] { 5, 9, 7, 8 });

				var preview = new CollectionMemberEffectPreview(CollectionMemberKey.FromProvider("member-relink"),
					CollectionRecipeIdentity.FromFingerprint("recipe-relink"), scenario == "direct" ? ModInstallMethod.Direct : ModInstallMethod.Virtual,
					ModInstallRoot.Data, new[] { new CollectionPlannedFileEffect(target, expected.ContentHash, expected.ByteLength) },
					new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0],
					new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
				var evidence = new CollectionNativeChildExecutionEvidence("game", 10, 20, "incoming.7z", preview,
					new[] { preimage }, new[] { expected },
					new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
					new CollectionExpectedReplayOperation[0]);
				var owner = new CollectionNativeOwnerState("owner", null, CollectionNativeOwnerKind.NativeMod, null, null, null);
				var link = new CollectionNativeOwnerState(scenario == "foreign-owner" ? "foreign" : "owner", null,
					CollectionNativeOwnerKind.NativeMod, true, 0, scenario == "different-source" ? source + ".other" : source);
				var files = new List<CollectionNativeFileState>
				{
					new CollectionNativeFileState(target, deployed, scenario == "promoted", scenario != "missing-install-log", true,
						"owner", new[] { owner }, new CollectionNativeOwnerState[0], new[] { link })
				};
				if (scenario == "unreviewed-file")
					files.Add(new CollectionNativeFileState(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "extra.bin"),
						deployed, false, true, true, "owner", new[] { owner }, new CollectionNativeOwnerState[0], new[] { link }));
				CollectionNativeStateIndex state = CreateRelinkState(files);
				IReadOnlyList<ModDeploymentTarget> targets;
				string failure;
				Assert.AreEqual(accepted, CollectionNativeChildRestartReconciliationCoordinator.TryCollectExactVirtualRelinkTargets(
					evidence, state, "owner", (deploymentTarget, ownerKey) => source, out targets, out failure), failure);
				Assert.AreEqual(count, targets.Count);
			}
			finally { Directory.Delete(directory, true); }
		}

		/// <summary>Recovers only missing replay metadata when a first activation already has exact installed bytes and native ownership.</summary>
		[TestCase("exact", true)]
		[TestCase("changed-bytes", false)]
		[TestCase("missing-file", false)]
		[TestCase("changed-archive", false)]
		[TestCase("changed-staging", false)]
		[TestCase("missing-staging", false)]
		[TestCase("foreign-owner", false)]
		[TestCase("missing-install-log", false)]
		[TestCase("inactive-link", false)]
		[TestCase("unreviewed-file", false)]
		[TestCase("missing-registration", false)]
		[TestCase("duplicate-registration", false)]
		[TestCase("wrong-file-id", false)]
		[TestCase("wrong-domain", false)]
		[TestCase("direct", false)]
		[TestCase("existing-replay", false)]
		[TestCase("existing-payloads", false)]
		[TestCase("previous-replay", false)]
		[TestCase("previous-mod", false)]
		[TestCase("generated", false)]
		public void MissingActivationReplay_RequiresExactCommittedPostimage(string scenario, bool accepted)
		{
			string directory = Path.Combine(Path.GetTempPath(), "collection-activation-replay-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				string archivePath = Path.Combine(directory, "incoming.7z");
				string deployed = Path.Combine(directory, "mod.dll");
				string staged = Path.Combine(directory, "staged.dll");
				File.WriteAllBytes(staged, new byte[] { 1, 2, 3, 4 });
				File.WriteAllBytes(archivePath, new byte[] { 5, 6, 7, 8 });
				File.WriteAllBytes(deployed, new byte[] { 1, 2, 3, 4 });
				CollectionContentHash archiveHash = CollectionContentHash.FromSha256(ComputeSha256(archivePath));
				CollectionContentHash fileHash = CollectionContentHash.FromSha256(ComputeSha256(deployed));
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-missing-replay");
				ModInstallMethod method = scenario == "direct" ? ModInstallMethod.Direct : ModInstallMethod.Virtual;
				var nativeMod = new CollectionNativeModState(new NativeModInstanceIdentity(target, "owner"), archivePath,
					"incoming.7z", "10", scenario == "wrong-file-id" ? "21" : "20", "1.0", "1.0", ModInstallRoot.Data, method);
				var mods = new List<CollectionNativeModState>();
				if (scenario != "missing-registration") mods.Add(nativeMod);
				if (scenario == "duplicate-registration")
					mods.Add(new CollectionNativeModState(new NativeModInstanceIdentity(target, "other"), archivePath,
						"incoming.7z", "10", "20", "1.0", "1.0", ModInstallRoot.Data, method));
				ModDeploymentTarget deployment = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "mod.dll");
				var owner = new CollectionNativeOwnerState("owner", null, CollectionNativeOwnerKind.NativeMod, null, null, null);
				var link = new CollectionNativeOwnerState("owner", null, CollectionNativeOwnerKind.NativeMod,
					scenario != "inactive-link", 0, staged);
				var files = new List<CollectionNativeFileState>
				{
					new CollectionNativeFileState(deployment, deployed, false, scenario != "missing-install-log", true,
						scenario == "foreign-owner" ? "foreign" : "owner", new[] { owner }, new CollectionNativeOwnerState[0], new[] { link })
				};
				if (scenario == "unreviewed-file")
					files.Add(new CollectionNativeFileState(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "extra.dll"),
						deployed, false, true, true, "owner", new[] { owner }, new CollectionNativeOwnerState[0], new[] { link }));
				var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], mods, files,
					new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
					CollectionNativeStateCoverage.NotApplicable, new CollectionTargetAssociation[0], new CollectionMemberBinding[0],
					new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 1);
				var preview = new CollectionMemberEffectPreview(CollectionMemberKey.FromProvider("replay-member"),
					CollectionRecipeIdentity.FromFingerprint("replay-recipe"), method, ModInstallRoot.Data,
					new[] { new CollectionPlannedFileEffect(deployment, fileHash, 4) }, new CollectionPlannedIniEffect[0],
					new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
				CollectionExpectedReplayOperation expected = scenario == "generated"
					? new CollectionExpectedReplayOperation(ScriptedReplayOperationKind.GeneratedFile, null, "mod.dll", 4, fileHash.Value)
					: new CollectionExpectedReplayOperation(ScriptedReplayOperationKind.ArchiveFile, "files/mod.dll", "mod.dll", 0, null);
				var evidence = new CollectionNativeChildExecutionEvidence("game", 10, 20, "incoming.7z", preview,
					new[] { new CollectionNativeFileContentEvidence(deployment, false, null, 0) },
					new[] { new CollectionNativeFileContentEvidence(deployment, true, fileHash, 4) },
					new CollectionReplayContentEvidence(scenario == "previous-replay", scenario == "previous-replay" ? fileHash : null,
						scenario == "previous-replay" ? 4 : 0, false, new CollectionReplayPayloadContentEvidence[0]), new[] { expected });
				CollectionIdentity collection = CollectionIdentity.FromNexus("activation-replay-collection");
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "activation-replay-revision", 1);
				ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
					new ModOperationFingerprint(target.Fingerprint, new ModInstallContext(method, ModInstallRoot.Data), "replay-recipe"));
				var artifact = new CollectionRecoveryArtifact("incoming", archiveHash, 4);
				var recovery = new CollectionNativeChildRecoveryManifest(CollectionOperationIdentity.CreateNew(), 1,
					CollectionPlanIdentity.From(Guid.NewGuid(), 1), new CollectionOperationMemberReference(revision, preview.MemberKey),
					CollectionNativeChildAction.ActivateOrReinstall, native,
					new CollectionCurrentStateFingerprint(state.Fingerprint.FormatVersion, "pre-activation-state"), artifact,
					scenario == "previous-mod" ? nativeMod : null, scenario == "previous-mod" ? artifact : null,
					new CollectionScriptedReplayRecoverySnapshot(false, null, false, new CollectionReplayRecoveryPayload[0]), evidence);
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(evidence.IncomingFileName, directory);
				if (scenario == "existing-replay")
				{
					Directory.CreateDirectory(Path.GetDirectoryName(replayPath));
					File.WriteAllText(replayPath, "existing record");
				}
				if (scenario == "existing-payloads") Directory.CreateDirectory(ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath));
				if (scenario == "changed-bytes") File.WriteAllBytes(deployed, new byte[] { 1, 9, 3, 4 });
				if (scenario == "missing-file") File.Delete(deployed);
				if (scenario == "changed-archive") File.WriteAllBytes(archivePath, new byte[] { 5, 9, 7, 8 });
				if (scenario == "changed-staging") File.WriteAllBytes(staged, new byte[] { 1, 9, 3, 4 });
				if (scenario == "missing-staging") File.Delete(staged);
				byte[] originalBytes = File.Exists(deployed) ? File.ReadAllBytes(deployed) : null;
				IGameMode gameMode = InterfaceStub<IGameMode>.Create((invoked, arguments) => null);
				string detail;
				Assert.AreEqual(accepted, CollectionNativeChildRestartReconciliationCoordinator.TryRestoreExactNewActivationReplay(
					recovery, evidence, state, scenario == "wrong-domain" ? "other-game" : "game", directory, gameMode, out detail), detail);
				Assert.AreEqual(originalBytes != null, File.Exists(deployed));
				if (originalBytes != null) CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(deployed));
				if (accepted)
				{
					CollectionNativeModState verified;
					Assert.IsTrue(CollectionNativeChildRestartReconciliationCoordinator.TryVerifyCommittedState(
						recovery, evidence, state, "game", directory, gameMode, out verified, out detail), detail);
					IReadOnlyList<ScriptedReplayOperation> replay = new ScriptedFileSelectionCache(replayPath).LoadReplayOperations();
					Assert.AreEqual(1, replay.Count);
					Assert.AreEqual(expected.SourcePath, replay[0].SourcePath);
					Assert.AreEqual(expected.DestinationPath, replay[0].DestinationPath);
					Assert.IsFalse(CollectionNativeChildRestartReconciliationCoordinator.TryRestoreExactNewActivationReplay(
						recovery, evidence, state, "game", directory, gameMode, out detail));
				}
				else if (scenario == "existing-replay") Assert.AreEqual("existing record", File.ReadAllText(replayPath));
				else Assert.IsFalse(File.Exists(replayPath));
				Assert.AreEqual(0, Directory.GetFiles(directory, "*.recovery.tmp", SearchOption.AllDirectories).Length);
			}
			finally { Directory.Delete(directory, true); }
		}

		[TestCase(ModOperationReportedStatus.Succeeded, ModOperationDurability.Unknown, true)]
		[TestCase(ModOperationReportedStatus.Failed, ModOperationDurability.Unknown, false)]
		[TestCase(ModOperationReportedStatus.Cancelled, ModOperationDurability.Unknown, false)]
		[TestCase(ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, false)]
		[TestCase(ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedRolledBack, false)]
		public void RevisionRecoveryAction_OffersRecheckOnlyForReportedSuccessWithUnknownDurability(
			ModOperationReportedStatus status, ModOperationDurability durability, bool offered)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-relink-action");
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-relink-action");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-relink-action", 5);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(target.Fingerprint, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), "recipe-relink-action"));
			var child = new CollectionNativeChildOperation(1,
				new CollectionOperationMemberReference(revision, CollectionMemberKey.FromProvider("member-relink-action")),
				CollectionNativeChildAction.ActivateOrReinstall, native, CollectionNativeChildCheckpoint.NativeTerminalObserved,
				new ModOperationResult(native, status, durability, null));
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.UpdateRevision,
				collection, target, revision, CollectionPlanIdentity.From(Guid.NewGuid(), 1), 1,
				CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired, new[] { child });
			MethodInfo method = typeof(Nexus.Client.CollectionManagement.UI.CollectionsPreviewControl).GetMethod(
				"CanRecheckReportedSuccessfulRevisionChild", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			Assert.AreEqual(offered, method.Invoke(null, new object[] { operation }));
		}

		/// <summary>Creates the detached ownership observation used to check the exact relink safety boundary.</summary>
		private static CollectionNativeStateIndex CreateRelinkState(IEnumerable<CollectionNativeFileState> files)
		{
			return new CollectionNativeStateIndex(CollectionTargetIdentity.FromFingerprint("target-relink"),
				new CollectionNativeRootState[0], new CollectionNativeModState[0], files, new CollectionNativeIniState[0],
				new CollectionNativeGameValueState[0], new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0], CollectionNativeStateCoverage.Complete,
				new CollectionNativeStateIssue[0], 1);
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
		public void RecoveryManifestV5_RoundTripsCollectionBundleIdentityAndFingerprints()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c69-bundle");
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c69-bundle");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c69-bundle", 1);
			CollectionMemberKey memberKey = CollectionMemberKey.FromProvider("member-c69-bundle");
			var member = new CollectionOperationMemberReference(revision, memberKey);
			CollectionPlanIdentity plan = CollectionPlanIdentity.From(Guid.NewGuid(), 1);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(target.Fingerprint, new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot), "recipe-c69-bundle"));
			var child = new CollectionNativeChildOperation(1, member, CollectionNativeChildAction.ActivateOrReinstall,
				native, CollectionNativeChildCheckpoint.Reconciled, new ModOperationResult(native, ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, null));
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ApplyResolvedPlan,
				collection, target, revision, plan, 8, CollectionOperationPhase.ApplyingNativeChildren,
				CollectionOperationResultState.Pending, new[] { child });
			var preview = new CollectionMemberEffectPreview(memberKey, CollectionRecipeIdentity.FromFingerprint("recipe-c69-bundle"),
				ModInstallMethod.Direct, ModInstallRoot.GameRoot, new CollectionPlannedFileEffect[0],
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0],
				new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			var artifact = new CollectionArtifactReference("collection-bundle", "revision-fingerprint/bundleTag", null);
			var evidence = new CollectionNativeChildExecutionEvidence(artifact, "bundle-member.7z", preview,
				new CollectionNativeFileContentEvidence[0], new CollectionNativeFileContentEvidence[0],
				new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
				new CollectionExpectedReplayOperation[0]);
			var terminal = new CollectionCurrentStateFingerprint("c6-native-state/1", "terminal-c69-bundle");
			var safeBoundary = new CollectionCurrentStateFingerprint("c6-native-state/1", "safe-c69-bundle");
			var manifest = new CollectionNativeChildRecoveryManifest(operation.Identity, 1, plan, member,
				CollectionNativeChildAction.ActivateOrReinstall, native,
				new CollectionCurrentStateFingerprint("c6-native-state/1", "state-c69-bundle"),
				new CollectionRecoveryArtifact("incoming-artifact", CollectionContentHash.FromSha256(ShaA), 1),
				null, null, new CollectionScriptedReplayRecoverySnapshot(false, null, false, new CollectionReplayRecoveryPayload[0]),
				evidence, terminal, safeBoundary);

			byte[] bytes = InvokeSerialize(manifest);
			using (var stream = new MemoryStream(bytes, false))
			using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
				Assert.AreEqual("nmm-ce.collections.child-recovery/5", reader.ReadString());

			CollectionNativeChildRecoveryManifest roundTrip = InvokeDeserialize(bytes, operation, child);
			Assert.IsTrue(roundTrip.ExecutionEvidence.IsCollectionBundleArtifact);
			Assert.AreEqual("collection-bundle", roundTrip.ExecutionEvidence.SelectedArtifact.Scheme);
			Assert.AreEqual("revision-fingerprint/bundleTag", roundTrip.ExecutionEvidence.SelectedArtifact.StableId);
			Assert.IsNull(roundTrip.ExecutionEvidence.NexusGameDomain);
			Assert.AreEqual(0, roundTrip.ExecutionEvidence.NexusModId);
			Assert.AreEqual(0, roundTrip.ExecutionEvidence.NexusFileId);
			Assert.AreEqual(terminal, roundTrip.TerminalStateFingerprint);
			Assert.AreEqual(safeBoundary, roundTrip.SafeBoundaryStateFingerprint);
		}

		/// <summary>Keeps the incoming owner payload distinct from the preserved physical winner across restart.</summary>
		[TestCase(ModInstallMethod.Virtual)]
		[TestCase(ModInstallMethod.Direct)]
		public void RecoveryManifestV6_RetainsIncomingBytesSeparatelyFromThePreservedGameFolderWinner(ModInstallMethod method)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("root-correction-restart");
			CollectionIdentity collection = CollectionIdentity.FromNexus("root-correction");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision", 1);
			CollectionMemberKey memberKey = CollectionMemberKey.FromProvider("preloader");
			var member = new CollectionOperationMemberReference(revision, memberKey);
			CollectionPlanIdentity plan = CollectionPlanIdentity.From(Guid.NewGuid(), 1);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(target.Fingerprint, new ModInstallContext(method, ModInstallRoot.GameRoot), "root-recipe"));
			var child = new CollectionNativeChildOperation(1, member, CollectionNativeChildAction.ActivateOrReinstall,
				native, CollectionNativeChildCheckpoint.NativeSubmitted, null);
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ApplyResolvedPlan,
				collection, target, revision, plan, 7, CollectionOperationPhase.ApplyingNativeChildren,
				CollectionOperationResultState.Pending, new[] { child });
			var oldTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "preloader.xml");
			var newTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.GameRoot, "preloader.xml");
			var oldBefore = new CollectionNativeFileContentEvidence(oldTarget, true, CollectionContentHash.FromSha256(ShaA), 4);
			var oldAfter = new CollectionNativeFileContentEvidence(oldTarget, false, null, 0);
			var winner = new CollectionNativeFileContentEvidence(newTarget, true, CollectionContentHash.FromSha256(new string('b', 64)), 8);
			var correction = new CollectionInstallRootCorrection("preloader", method,
				new[] { new CollectionInstallRootFileRemoval(oldBefore, oldAfter, new[] { "preloader" }, new string[0]) },
				new[] { new CollectionInstallRootDestination(winner, "settings", true) });
			var preview = new CollectionMemberEffectPreview(memberKey, CollectionRecipeIdentity.FromFingerprint("root-recipe"),
				method, ModInstallRoot.GameRoot, new[] { new CollectionPlannedFileEffect(newTarget, oldBefore.ContentHash, 4) },
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0],
				new CollectionEffectPreviewIssue[0], correction);
			var evidence = new CollectionNativeChildExecutionEvidence("game", 10, 20, "preloader.7z", preview,
				new[] { oldBefore, winner }, new[] { oldAfter, winner },
				new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
				new[] { new CollectionExpectedReplayOperation(ScriptedReplayOperationKind.ArchiveFile, "preloader.xml", "preloader.xml", 0, null) });
			var manifest = new CollectionNativeChildRecoveryManifest(operation.Identity, 1, plan, member,
				CollectionNativeChildAction.ActivateOrReinstall, native,
				new CollectionCurrentStateFingerprint("c6-native-state/1", "before-root-correction"),
				new CollectionRecoveryArtifact("incoming-artifact", CollectionContentHash.FromSha256(ShaA), 1),
				null, null, new CollectionScriptedReplayRecoverySnapshot(false, null, false, new CollectionReplayRecoveryPayload[0]), evidence);
			byte[] bytes = InvokeSerialize(manifest);
			using (var stream = new MemoryStream(bytes, false))
			using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
				Assert.AreEqual("nmm-ce.collections.child-recovery/6", reader.ReadString());
			CollectionNativeChildExecutionEvidence restored = InvokeDeserialize(bytes, operation, child).ExecutionEvidence;
			Assert.AreEqual(oldBefore.ContentHash, restored.ReviewedEffects.Files[0].ExpectedContentHash);
			Assert.AreEqual(4, restored.ReviewedEffects.Files[0].ExpectedByteLength);
			Assert.AreEqual(winner.ContentHash, restored.ExpectedFileContents[1].ContentHash);
			Assert.AreEqual(8, restored.ExpectedFileContents[1].ByteLength);
			Assert.AreEqual(method, restored.ReviewedEffects.InstallRootCorrection.InstallMethod);
			Assert.IsTrue(restored.ReviewedEffects.InstallRootCorrection.Destinations[0].PreserveWinner);
			Assert.IsFalse(restored.ExpectedFileContents[0].Existed);
			Assert.AreEqual(1, restored.ExpectedReplayOperations.Count);
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
