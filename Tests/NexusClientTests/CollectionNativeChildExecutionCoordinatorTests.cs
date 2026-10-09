using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.7 execution-boundary coverage that does not invoke the native installer.
	/// </summary>
	[TestFixture]
	public class CollectionNativeChildExecutionCoordinatorTests
	{
		[Test]
		public void ValidateExecutableFilePriorities_SingleWriterIsRepresentableByCurrentC5Plan()
		{
			CollectionMemberKey member = CollectionMemberKey.FromProvider("member-a");
			CollectionFileImpact impact = CreateImpact(new[] { member }, member);

			Assert.DoesNotThrow(() => InvokeFilePriorityValidation(new[] { impact }));
		}

		[Test]
		public void ValidateExecutableFilePriorities_MultipleSelectedWritersWithReviewedWinnerAreAllowedForC61511Reconciliation()
		{
			CollectionMemberKey lower = CollectionMemberKey.FromProvider("member-a");
			CollectionMemberKey winner = CollectionMemberKey.FromProvider("member-b");
			CollectionFileImpact impact = CreateImpact(new[] { lower, winner }, winner);

			Assert.DoesNotThrow(() => InvokeFilePriorityValidation(new[] { impact }));
		}

		[Test]
		public void ValidateExecutableFilePriorities_MultipleSelectedWritersWithoutReviewedWinnerStillBlock()
		{
			CollectionMemberKey first = CollectionMemberKey.FromProvider("member-a");
			CollectionMemberKey second = CollectionMemberKey.FromProvider("member-b");
			CollectionFileImpact impact = CreateImpact(new[] { first, second }, null);

			TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
				InvokeFilePriorityValidation(new[] { impact }));

			Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
			StringAssert.Contains("no deterministic C6.4 winner", error.InnerException.Message);
		}

		[TestCase(false, ModInstallMethod.Virtual, false)]
		[TestCase(true, ModInstallMethod.Virtual, false)]
		[TestCase(false, ModInstallMethod.Virtual, true)]
		[TestCase(true, ModInstallMethod.Virtual, true)]
		[TestCase(false, ModInstallMethod.Direct, true)]
		[TestCase(true, ModInstallMethod.Direct, true)]
		public void ReviewedFileDeployment_PassesApprovedOverwriteToNativeBackend(bool generated, ModInstallMethod method, bool promoted)
		{
			CollectionMemberKey member = CollectionMemberKey.FromProvider("preloader");
			CollectionMemberKey winner = CollectionMemberKey.FromProvider("settings");
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.GameRoot, "xSE PluginPreloader.xml");
			var effect = new CollectionPlannedFileEffect(target, CollectionContentHash.FromSha256(new string('a', 64)), 3);
			var impact = new CollectionFileImpact(target, new[] { member, winner }, winner, "owner-settings", new Guid[0]);
			ScriptedInstallOperation input = generated ? (ScriptedInstallOperation)new GenerateDataFileOperation(target.RelativePath, new byte[] { 1, 2, 3 }, "source.xml") :
				new InstallModFileOperation("source.xml", target.RelativePath);

			ScriptedInstallOperation resolved = CollectionNativeChildExecutionCoordinator.AuthorizeReviewedFileDeployment(
				input, effect, impact, member, method, @"C:\stage\preloader.xml", promoted);
			ScriptedFileDeploymentDecision decision = generated ? ((GenerateDataFileOperation)resolved).DeploymentDecision :
				((InstallModFileOperation)resolved).DeploymentDecision;

			if (generated)
			{
				Assert.That(((GenerateDataFileOperation)resolved).PreparationSourcePath, Is.EqualTo("source.xml"));
				Assert.That(((GenerateDataFileOperation)resolved).Data, Is.EqualTo(new byte[] { 1, 2, 3 }));
			}
			Assert.That(decision.Method, Is.EqualTo(method));
			Assert.That(decision.WritePayload, Is.True);
			Assert.That(decision.Activate, Is.True);
			Assert.That(impact.PlannedWinner, Is.EqualTo(winner));
			if (method == ModInstallMethod.Virtual && !promoted)
			{
				Assert.That(decision.LinkDecision.IsResolved, Is.True);
				Assert.That(decision.LinkDecision.Overwrite, Is.True);
				Assert.That(decision.LinkDecision.LinkOutcome, Is.True);
			}
			else Assert.That(decision.UseDeploymentCoordinator, Is.True);
		}

		[TestCase(true)]
		[TestCase(false)]
		public void ReviewedFileDeployment_PreservesExactCacheReuseAndResolvedInactiveChoice(bool keepInactive)
		{
			CollectionMemberKey member = CollectionMemberKey.FromProvider("preloader");
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.GameRoot, "shared.xml");
			var effect = new CollectionPlannedFileEffect(target, CollectionContentHash.FromSha256(new string('a', 64)), 3);
			var impact = new CollectionFileImpact(target, new[] { member }, member, null, new Guid[0]);
			ModLinkInstallDecision choice = keepInactive ? new ModLinkInstallDecision(false).WithLinkOutcome(false, false) : null;
			var input = new InstallModFileOperation("source.xml", target.RelativePath,
				ScriptedFileDeploymentDecision.ForVirtual(@"C:\stage\shared.xml", false, choice));

			var resolved = (InstallModFileOperation)CollectionNativeChildExecutionCoordinator.AuthorizeReviewedFileDeployment(
				input, effect, impact, member, ModInstallMethod.Virtual, @"C:\other\shared.xml", false);

			Assert.That(resolved.StageFile, Is.False);
			Assert.That(resolved.StagingPath, Is.EqualTo(input.StagingPath));
			Assert.That(resolved.LinkDecision.IsResolved, Is.True);
			Assert.That(resolved.LinkDecision.Overwrite, Is.EqualTo(!keepInactive));
			if (keepInactive) Assert.That(resolved, Is.SameAs(input));
		}

		[TestCase(true)]
		[TestCase(false)]
		public void ReviewedFileDeployment_RejectsDifferentRootOrUnreviewedMember(bool wrongRoot)
		{
			CollectionMemberKey member = CollectionMemberKey.FromProvider("preloader");
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.GameRoot, "shared.xml");
			var effect = new CollectionPlannedFileEffect(target, CollectionContentHash.FromSha256(new string('a', 64)), 3);
			ModDeploymentTarget other = wrongRoot ? ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "shared.xml") : target;
			CollectionMemberKey writer = wrongRoot ? member : CollectionMemberKey.FromProvider("unreviewed");
			var impact = new CollectionFileImpact(other, new[] { writer }, writer, null, new Guid[0]);

			Assert.Throws<InvalidOperationException>(() => CollectionNativeChildExecutionCoordinator.AuthorizeReviewedFileDeployment(
				new InstallModFileOperation("source.xml", target.RelativePath), effect, impact, member, ModInstallMethod.Virtual, @"C:\stage\shared.xml", false));
		}

		[Test]
		public void CreateExpectedFileEvidence_ReusesReviewedExactContentIdentity()
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\a.dds");
			CollectionContentHash hash = CollectionContentHash.FromSha256(new string('a', 64));
			var effect = new CollectionPlannedFileEffect(target, hash, 123);

			CollectionNativeFileContentEvidence evidence = CollectionNativeChildExecutionCoordinator.CreateExpectedFileEvidence(effect);

			Assert.That(evidence.Target, Is.EqualTo(target));
			Assert.That(evidence.Existed, Is.True);
			Assert.That(evidence.ContentHash, Is.EqualTo(hash));
			Assert.That(evidence.ByteLength, Is.EqualTo(123));
		}

		[Test]
		public void CreateExpectedFileEvidence_RejectsPreviewWithoutExactContentIdentity()
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\a.dds");
			Assert.Throws<System.IO.InvalidDataException>(() =>
				CollectionNativeChildExecutionCoordinator.CreateExpectedFileEvidence(new CollectionPlannedFileEffect(target)));
		}

		/// <summary>Legacy reviews can recover missing hashes from the same archive-bound recipe at submission.</summary>
		[Test]
		public void SubmissionPreview_LegacyTargetsUseExactLiveContent()
		{
			CollectionMemberEffectPreview reviewed = CreateSubmissionPreview("mod.dll", null, null);
			CollectionMemberEffectPreview live = CreateSubmissionPreview("mod.dll", new string('a', 64), 123);
			Assert.That(CollectionNativeChildExecutionCoordinator.ResolveExactSubmissionPreview(reviewed, live), Is.SameAs(live));
		}

		/// <summary>Existing reviewed identities and destination coverage cannot be widened during submission.</summary>
		[TestCase("hash")]
		[TestCase("length")]
		[TestCase("target")]
		[TestCase("missing-content")]
		public void SubmissionPreview_RejectsChangedOrUncharacterizedEffects(string condition)
		{
			CollectionMemberEffectPreview reviewed = CreateSubmissionPreview("mod.dll", new string('a', 64), 123);
			CollectionMemberEffectPreview live = CreateSubmissionPreview(condition == "target" ? "other.dll" : "mod.dll",
				condition == "missing-content" ? null : new string(condition == "hash" ? 'b' : 'a', 64),
				condition == "missing-content" ? (long?)null : condition == "length" ? 124 : 123);
			Assert.Throws<InvalidOperationException>(() => CollectionNativeChildExecutionCoordinator.ResolveExactSubmissionPreview(reviewed, live));
		}

		/// <summary>Creates a detached submission preview with optional exact file bytes.</summary>
		private static CollectionMemberEffectPreview CreateSubmissionPreview(string path, string hash, long? length)
		{
			return new CollectionMemberEffectPreview(CollectionMemberKey.FromProvider("submission-preview"),
				CollectionRecipeIdentity.FromFingerprint("submission-recipe"), ModInstallMethod.Virtual, ModInstallRoot.Data,
				new[] { new CollectionPlannedFileEffect(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, path),
					hash == null ? null : CollectionContentHash.FromSha256(hash), length) },
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0],
				new CollectionEffectPreviewIssue[0]);
		}

		/// <summary>Exact cached bytes skip staging while retaining the native game-file overwrite decision.</summary>
		[Test]
		public void StagingReuse_ExactPayloadKeepsGameDeploymentUnresolved()
		{
			string path = System.IO.Path.GetTempFileName();
			try
			{
				byte[] payload = { 1, 2, 3, 4 };
				System.IO.File.WriteAllBytes(path, payload);
				InstallModFileOperation install = new InstallModFileOperation("archive.dll", "F4SE\\Plugins\\mod.dll");
				InstallModFileOperation reused = CollectionNativeChildExecutionCoordinator.ReuseExactReviewedStagingFile(
					install, CreateStagingEffect(install.DestinationPath, payload), path);
				Assert.That(reused, Is.Not.SameAs(install));
				Assert.That(reused.SourcePath, Is.EqualTo(install.SourcePath));
				Assert.That(reused.DestinationPath, Is.EqualTo(install.DestinationPath));
				Assert.That(reused.StageFile, Is.False);
				Assert.That(reused.StagingPath, Is.EqualTo(path));
				Assert.That(reused.LinkDecision, Is.Null);
				Assert.That(reused.DeploymentDecision.UseDeploymentCoordinator, Is.False);
				Assert.That(System.IO.File.ReadAllBytes(path), Is.EqualTo(payload));
			}
			finally { System.IO.File.Delete(path); }
		}

		/// <summary>Missing, different or unreviewed cache files never supply an overwrite decision.</summary>
		[TestCase("missing")]
		[TestCase("different-length")]
		[TestCase("different-hash")]
		[TestCase("unreviewed")]
		[TestCase("existing-decision")]
		public void StagingReuse_UnsafePayloadRetainsOriginalOperation(string condition)
		{
			string path = System.IO.Path.GetTempFileName();
			try
			{
				byte[] payload = { 1, 2, 3, 4 };
				System.IO.File.WriteAllBytes(path, payload);
				InstallModFileOperation install = condition == "existing-decision"
					? new InstallModFileOperation("archive.dll", "mod.dll", ScriptedFileDeploymentDecision.ForVirtual(path, true, null))
					: new InstallModFileOperation("archive.dll", "mod.dll");
				CollectionPlannedFileEffect effect = condition == "unreviewed"
					? new CollectionPlannedFileEffect(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "mod.dll"))
					: CreateStagingEffect("mod.dll", payload);
				if (condition == "missing") System.IO.File.Delete(path);
				if (condition == "different-length") System.IO.File.WriteAllBytes(path, new byte[] { 1, 2 });
				if (condition == "different-hash") System.IO.File.WriteAllBytes(path, new byte[] { 4, 3, 2, 1 });
				Assert.That(CollectionNativeChildExecutionCoordinator.ReuseExactReviewedStagingFile(install, effect, path), Is.SameAs(install));
			}
			finally { System.IO.File.Delete(path); }
		}

		/// <summary>Builds reviewed content evidence from exact payload bytes.</summary>
		private static CollectionPlannedFileEffect CreateStagingEffect(string destination, byte[] payload)
		{
			using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
				return new CollectionPlannedFileEffect(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, destination),
					CollectionContentHash.FromSha256(BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", String.Empty).ToLowerInvariant()), payload.Length);
		}

		/// <summary>A rollback-safe retry retains the exact state fingerprint instead of requiring its earlier attempt to have committed.</summary>
		[TestCase(ModOperationDurability.VerifiedRolledBack)]
		[TestCase(ModOperationDurability.NotStarted)]
		public void RevisionSubmission_SafeRetryPreservesPriorBoundary(ModOperationDurability durability)
		{
			CollectionNativeChildOperation previous = CreateRevisionChild(31, "weapon-debris", durability);
			CollectionNativeChildOperation retry = CreateRevisionChild(32, "weapon-debris", null);
			CollectionOperation operation = CreateRevisionOperation(previous, retry);
			var expected = new CollectionCurrentStateFingerprint("state-v1", "after-child-30");
			CollectionNativeChildRecoveryManifest manifest = CreateRetryManifest(operation, previous, expected);

			Assert.That(CollectionNativeChildExecutionCoordinator.ResolveEarlierChildSafeBoundary(operation, previous, retry,
				manifest, expected, CollectionNativeChildWorkflowMode.RevisionUpdate), Is.SameAs(expected));
		}

		/// <summary>Revision-specific rollback retry must not relax the additive or replacement submission gates.</summary>
		[TestCase((int)CollectionNativeChildWorkflowMode.Additive)]
		[TestCase((int)CollectionNativeChildWorkflowMode.Replacement)]
		public void RevisionSubmission_SafeRetryDoesNotChangeOtherWorkflows(int mode)
		{
			CollectionNativeChildOperation previous = CreateRevisionChild(31, "weapon-debris", ModOperationDurability.VerifiedRolledBack);
			CollectionNativeChildOperation retry = CreateRevisionChild(32, "weapon-debris", null);
			CollectionOperation operation = CreateRevisionOperation(previous, retry);
			var expected = new CollectionCurrentStateFingerprint("state-v1", "after-child-30");
			Assert.Throws<InvalidOperationException>(() => CollectionNativeChildExecutionCoordinator.ResolveEarlierChildSafeBoundary(
				operation, previous, retry, CreateRetryManifest(operation, previous, expected), expected, (CollectionNativeChildWorkflowMode)mode));
		}

		/// <summary>Unknown durability and observed but unreconciled rollbacks remain blocked.</summary>
		[TestCase(ModOperationDurability.Unknown, true)]
		[TestCase(ModOperationDurability.VerifiedRolledBack, false)]
		public void RevisionSubmission_UnresolvedAttemptStillBlocks(ModOperationDurability durability, bool reconciled)
		{
			CollectionNativeChildOperation previous = CreateRevisionChild(31, "weapon-debris", durability, reconciled);
			CollectionNativeChildOperation retry = CreateRevisionChild(32, "weapon-debris", null);
			CollectionOperation operation = CreateRevisionOperation(previous, retry);
			var expected = new CollectionCurrentStateFingerprint("state-v1", "after-child-30");
			Assert.Throws<InvalidOperationException>(() => CollectionNativeChildExecutionCoordinator.ResolveEarlierChildSafeBoundary(
				operation, previous, retry, CreateRetryManifest(operation, previous, expected), expected, CollectionNativeChildWorkflowMode.RevisionUpdate));
		}

		/// <summary>Retry requires the retained manifest for the exact attempt at the expected state boundary.</summary>
		[TestCase("missing")]
		[TestCase("other-state")]
		[TestCase("other-attempt")]
		public void RevisionSubmission_InvalidRollbackEvidenceStillBlocks(string mismatch)
		{
			CollectionNativeChildOperation previous = CreateRevisionChild(31, "weapon-debris", ModOperationDurability.VerifiedRolledBack);
			CollectionNativeChildOperation retry = CreateRevisionChild(32, "weapon-debris", null);
			CollectionOperation operation = CreateRevisionOperation(previous, retry);
			var expected = new CollectionCurrentStateFingerprint("state-v1", "after-child-30");
			CollectionNativeChildRecoveryManifest manifest = mismatch == "missing" ? null : CreateRetryManifest(operation,
				mismatch == "other-attempt" ? CreateRevisionChild(31, "weapon-debris", ModOperationDurability.VerifiedRolledBack) : previous,
				mismatch == "other-state" ? new CollectionCurrentStateFingerprint("state-v1", "changed") : expected);

			Assert.Throws<System.IO.InvalidDataException>(() => CollectionNativeChildExecutionCoordinator.ResolveEarlierChildSafeBoundary(
				operation, previous, retry, manifest, expected, CollectionNativeChildWorkflowMode.RevisionUpdate));
		}

		/// <summary>The next member may proceed after a successful retry, while the earlier rolled-back attempt remains recorded.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void RevisionSubmission_NextMemberRequiresCommittedRetry(bool retryCommitted)
		{
			CollectionNativeChildOperation previous = CreateRevisionChild(31, "weapon-debris", ModOperationDurability.VerifiedRolledBack);
			CollectionNativeChildOperation retry = CreateRevisionChild(32, "weapon-debris",
				retryCommitted ? ModOperationDurability.VerifiedCommitted : ModOperationDurability.VerifiedRolledBack);
			CollectionNativeChildOperation next = CreateRevisionChild(33, "next-member", null);
			CollectionOperation operation = CreateRevisionOperation(previous, retry, next);
			var expected = new CollectionCurrentStateFingerprint("state-v1", "after-child-30");
			CollectionNativeChildRecoveryManifest manifest = CreateRetryManifest(operation, previous, expected);
			if (retryCommitted)
				Assert.That(CollectionNativeChildExecutionCoordinator.ResolveEarlierChildSafeBoundary(operation, previous, next,
					manifest, expected, CollectionNativeChildWorkflowMode.RevisionUpdate), Is.SameAs(expected));
			else
				Assert.Throws<InvalidOperationException>(() => CollectionNativeChildExecutionCoordinator.ResolveEarlierChildSafeBoundary(
					operation, previous, next, manifest, expected, CollectionNativeChildWorkflowMode.RevisionUpdate));
		}

		/// <summary>Creates one attempt of an exact candidate member without invoking native installation.</summary>
		private static CollectionNativeChildOperation CreateRevisionChild(int sequence, string member,
			ModOperationDurability? durability, bool reconciled = true)
		{
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("229114"), "791454", 6);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-revision-retry", new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), "recipe-" + member));
			ModOperationResult result = durability.HasValue ? new ModOperationResult(native,
				durability == ModOperationDurability.VerifiedCommitted ? ModOperationReportedStatus.Succeeded : ModOperationReportedStatus.Failed,
				durability.Value, null) : null;
			return new CollectionNativeChildOperation(sequence, new CollectionOperationMemberReference(revision, CollectionMemberKey.FromProvider(member)),
				CollectionNativeChildAction.ActivateOrReinstall, native, result == null ? CollectionNativeChildCheckpoint.RecoveryInputsReady
					: reconciled ? CollectionNativeChildCheckpoint.Reconciled : CollectionNativeChildCheckpoint.NativeTerminalObserved, result);
		}

		/// <summary>Creates a pending revision journal with the supplied historical and current attempts.</summary>
		private static CollectionOperation CreateRevisionOperation(params CollectionNativeChildOperation[] children)
		{
			CollectionRevisionIdentity revision = children[0].Member.Revision;
			return new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.UpdateRevision,
				revision.Collection, CollectionTargetIdentity.FromFingerprint("target-revision-retry"), revision,
				CollectionPlanIdentity.From(Guid.NewGuid(), 1), 178, CollectionOperationPhase.InstallingCandidateRevisionChildren,
				CollectionOperationResultState.Pending, children);
		}

		/// <summary>Retains the exact pre-attempt boundary and correlation used to authorize a retry.</summary>
		private static CollectionNativeChildRecoveryManifest CreateRetryManifest(CollectionOperation operation,
			CollectionNativeChildOperation child, CollectionCurrentStateFingerprint fingerprint)
		{
			return new CollectionNativeChildRecoveryManifest(operation.Identity, child.Sequence, operation.PlanIdentity, child.Member,
				child.Action, child.NativeOperation, fingerprint,
				new CollectionRecoveryArtifact("incoming", CollectionContentHash.FromSha256(new string('a', 64)), 1), null, null,
				new CollectionScriptedReplayRecoverySnapshot(false, null, false, new CollectionReplayRecoveryPayload[0]));
		}

		private static CollectionFileImpact CreateImpact(IEnumerable<CollectionMemberKey> writers, CollectionMemberKey winner)
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\shared.dds");
			return (CollectionFileImpact)Activator.CreateInstance(typeof(CollectionFileImpact),
				BindingFlags.Instance | BindingFlags.NonPublic, null,
				new object[] { target, writers, winner, null, new Guid[0] }, CultureInfo.InvariantCulture);
		}

		private static void InvokeFilePriorityValidation(IEnumerable<CollectionFileImpact> impacts)
		{
			MethodInfo method = typeof(CollectionNativeChildExecutionCoordinator).GetMethod(
				"ValidateExecutableFilePriorities", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			method.Invoke(null, new object[] { impacts });
		}
	}
}
