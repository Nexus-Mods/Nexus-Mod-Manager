using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.6 durable native-child preparation and recovery-input coverage.</summary>
	[TestFixture]
	public class CollectionNativeChildPreparationCoordinatorTests
	{
		private const string ManifestSha = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

		[Test]
		public void ArchiveOnly_PreparesDurableIntentAndIncomingRecoveryLeaseWithoutNativeSubmission()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				PlanContext context = CreatePlanContext(fixture, null, new CollectionNativeFileState[0]);
				CollectionVerifiedArchive archive = CreateVerifiedArchive(fixture, context.Plan, context.Member.MemberKey, "incoming-bytes");
				PreparedPlans plans = FinalizePlans(fixture, context, archive);
				CollectionNativeChildPreparationResult prepared = fixture.Preparer.PrepareNext(plans.Operation.Identity,
					plans.Plan, plans.Matches, plans.DependencyPlan, plans.ImpactPlan, plans.State,
					new[] { plans.Preview }, new[] { archive }, fixture.InstallInfoDirectory);

				Assert.AreEqual(CollectionNativeChildCheckpoint.RecoveryInputsReady, prepared.Child.Checkpoint);
				Assert.IsFalse(prepared.Child.HasCrossedNativeBoundary);
				Assert.AreEqual(archive.Artifact.ArtifactId, prepared.RecoveryManifest.IncomingArchive.ArtifactId);
				Assert.IsNull(prepared.RecoveryManifest.PreviousNativeMod);
				Assert.AreEqual(2, fixture.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Operation,
					plans.Operation.Identity.ToString()).Count);

				CollectionNativeChildPreparationResult second = fixture.Preparer.PrepareNext(plans.Operation.Identity,
					plans.Plan, plans.Matches, plans.DependencyPlan, plans.ImpactPlan, plans.State,
					new[] { plans.Preview }, new[] { archive }, fixture.InstallInfoDirectory);
				Assert.AreEqual(prepared.Child.NativeOperation.OperationId, second.Child.NativeOperation.OperationId);
				Assert.AreEqual(prepared.Child.NativeOperation.AttemptId, second.Child.NativeOperation.AttemptId);
				Assert.AreEqual(2, fixture.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Operation,
					plans.Operation.Identity.ToString()).Count);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Reinstall_RetainsPreviousArchiveReplayAndGeneratedPayloadBeforeReadyCheckpoint()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				string previousArchivePath = Path.Combine(root, "previous.7z");
				File.WriteAllText(previousArchivePath, "previous-archive-bytes");
				CollectionNativeModState nativeMod = CreateNativeMod(fixture, previousArchivePath, "previous.7z", true);
				PlanContext context = CreatePlanContext(fixture, nativeMod, new CollectionNativeFileState[0]);
				CollectionVerifiedArchive archive = CreateVerifiedArchive(fixture, context.Plan, context.Member.MemberKey, "new-archive-bytes");
				PreparedPlans plans = FinalizePlans(fixture, context, archive);
				Assert.AreEqual(CollectionMemberMatchDisposition.ReinstallRequired, plans.Matches.Members.Single().Disposition);

				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(nativeMod.FileName, fixture.InstallInfoDirectory);
				Directory.CreateDirectory(Path.GetDirectoryName(replayPath));
				File.WriteAllText(replayPath, "<FileList ReplayVersion=\"2\" />");
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				Directory.CreateDirectory(payloadDirectory);
				File.WriteAllBytes(Path.Combine(payloadDirectory, "payload.bin"), new byte[] { 1, 2, 3, 4 });

				CollectionNativeChildPreparationResult result = fixture.Preparer.PrepareNext(plans.Operation.Identity,
					plans.Plan, plans.Matches, plans.DependencyPlan, plans.ImpactPlan, plans.State,
					new[] { plans.Preview }, new[] { archive }, fixture.InstallInfoDirectory);

				Assert.AreEqual(CollectionNativeChildCheckpoint.RecoveryInputsReady, result.Child.Checkpoint);
				Assert.AreEqual(nativeMod.Identity, result.RecoveryManifest.PreviousNativeMod.Identity);
				Assert.AreEqual(nativeMod.ArchivePath, result.RecoveryManifest.PreviousNativeMod.ArchivePath);
				Assert.AreEqual(nativeMod.NexusModId, result.RecoveryManifest.PreviousNativeMod.NexusModId);
				Assert.AreEqual(nativeMod.NexusFileId, result.RecoveryManifest.PreviousNativeMod.NexusFileId);
				Assert.AreEqual(nativeMod.InstallMethod, result.RecoveryManifest.PreviousNativeMod.InstallMethod);
				Assert.AreEqual(nativeMod.InstallRoot, result.RecoveryManifest.PreviousNativeMod.InstallRoot);
				Assert.IsTrue(result.RecoveryManifest.PreviousNativeMod.HasInstallScript);
				Assert.IsNotNull(result.RecoveryManifest.PreviousArchive);
				Assert.IsTrue(result.RecoveryManifest.ScriptedReplay.ReplayFileExisted);
				Assert.IsTrue(result.RecoveryManifest.ScriptedReplay.PayloadDirectoryExisted);
				Assert.AreEqual(1, result.RecoveryManifest.ScriptedReplay.Payloads.Count);
				Assert.AreEqual("payload.bin", result.RecoveryManifest.ScriptedReplay.Payloads[0].RelativePath);
				Assert.AreEqual(5, fixture.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Operation,
					plans.Operation.Identity.ToString()).Count);
				Assert.AreEqual("previous-archive-bytes", ReadText(fixture.Artifacts, result.RecoveryManifest.PreviousArchive.ArtifactId));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Reinstall_ScriptedPreviousInstallWithoutCompleteReplayBlocksBeforeReadyCheckpoint()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				string previousArchivePath = Path.Combine(root, "previous-scripted.7z");
				File.WriteAllText(previousArchivePath, "previous-scripted-archive");
				CollectionNativeModState nativeMod = CreateNativeMod(fixture, previousArchivePath, "previous-scripted.7z", true);
				PlanContext context = CreatePlanContext(fixture, nativeMod, new CollectionNativeFileState[0]);
				CollectionVerifiedArchive archive = CreateVerifiedArchive(fixture, context.Plan, context.Member.MemberKey, "new-scripted-archive");
				PreparedPlans plans = FinalizePlans(fixture, context, archive);

				Assert.Throws<InvalidOperationException>(() => fixture.Preparer.PrepareNext(plans.Operation.Identity,
					plans.Plan, plans.Matches, plans.DependencyPlan, plans.ImpactPlan, plans.State,
					new[] { plans.Preview }, new[] { archive }, fixture.InstallInfoDirectory));

				CollectionOperation persisted = fixture.OperationStore.GetOperation(plans.Operation.Identity);
				Assert.AreEqual(1, persisted.NativeChildren.Count);
				Assert.AreEqual(CollectionNativeChildCheckpoint.IntentPersisted, persisted.NativeChildren[0].Checkpoint);
				Assert.IsFalse(persisted.HasCrossedNativeBoundary);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Reinstall_MissingPreviousArchiveLeavesIntentPersistedAndCanFailBeforeApply()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionNativeModState nativeMod = CreateNativeMod(fixture, Path.Combine(root, "missing.7z"), "missing.7z", false);
				PlanContext context = CreatePlanContext(fixture, nativeMod, new CollectionNativeFileState[0]);
				CollectionVerifiedArchive archive = CreateVerifiedArchive(fixture, context.Plan, context.Member.MemberKey, "new-archive");
				PreparedPlans plans = FinalizePlans(fixture, context, archive);
				Assert.Throws<FileNotFoundException>(() => fixture.Preparer.PrepareNext(plans.Operation.Identity,
					plans.Plan, plans.Matches, plans.DependencyPlan, plans.ImpactPlan, plans.State,
					new[] { plans.Preview }, new[] { archive }, fixture.InstallInfoDirectory));

				CollectionOperation persisted = fixture.OperationStore.GetOperation(plans.Operation.Identity);
				Assert.AreEqual(1, persisted.NativeChildren.Count);
				Assert.AreEqual(CollectionNativeChildCheckpoint.IntentPersisted, persisted.NativeChildren[0].Checkpoint);
				Assert.IsFalse(persisted.HasCrossedNativeBoundary);
				CollectionOperation failed = fixture.Coordinator.CompleteFailedBeforeApply(plans.Operation.Identity);
				Assert.AreEqual(CollectionOperationResultState.FailedBeforeApply, failed.ResultState);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Reinstall_UnreviewedExistingFileEffectBlocksBeforeChildIntent()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				string archivePath = Path.Combine(root, "previous.7z");
				File.WriteAllText(archivePath, "previous");
				CollectionNativeModState nativeMod = CreateNativeMod(fixture, archivePath, "previous.7z", false);
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "stale\\old.txt");
				var owner = new CollectionNativeOwnerState(nativeMod.Identity.NativeModKey, null, CollectionNativeOwnerKind.NativeMod, null, null, null);
				var fileState = new CollectionNativeFileState(target, String.Empty, false, true, false,
					nativeMod.Identity.NativeModKey, new[] { owner }, new CollectionNativeOwnerState[0], new CollectionNativeOwnerState[0]);
				PlanContext context = CreatePlanContext(fixture, nativeMod, new[] { fileState });
				CollectionVerifiedArchive archive = CreateVerifiedArchive(fixture, context.Plan, context.Member.MemberKey, "incoming");
				PreparedPlans plans = FinalizePlans(fixture, context, archive);
				Assert.Throws<InvalidOperationException>(() => fixture.Preparer.PrepareNext(plans.Operation.Identity,
					plans.Plan, plans.Matches, plans.DependencyPlan, plans.ImpactPlan, plans.State,
					new[] { plans.Preview }, new[] { archive }, fixture.InstallInfoDirectory));
				Assert.AreEqual(0, fixture.OperationStore.GetOperation(plans.Operation.Identity).NativeChildren.Count);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void StaleNativeStateFingerprintBlocksBeforeChildIntent()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				PlanContext context = CreatePlanContext(fixture, null, new CollectionNativeFileState[0]);
				CollectionVerifiedArchive archive = CreateVerifiedArchive(fixture, context.Plan, context.Member.MemberKey, "incoming");
				PreparedPlans plans = FinalizePlans(fixture, context, archive);
				CollectionNativeStateIndex changed = new CollectionNativeStateIndex(fixture.Target, new CollectionNativeRootState[0],
					new CollectionNativeModState[0], new CollectionNativeFileState[0], new CollectionNativeIniState[0],
					new CollectionNativeGameValueState[0], new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
					new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0], CollectionNativeStateCoverage.Complete,
					new CollectionNativeStateIssue[0], 99);
				Assert.Throws<InvalidOperationException>(() => fixture.Preparer.PrepareNext(plans.Operation.Identity,
					plans.Plan, plans.Matches, plans.DependencyPlan, plans.ImpactPlan, changed,
					new[] { plans.Preview }, new[] { archive }, fixture.InstallInfoDirectory));
				Assert.AreEqual(0, fixture.OperationStore.GetOperation(plans.Operation.Identity).NativeChildren.Count);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void ReconciledCommittedChild_AllowsLatestVerifiedSafeBoundaryInsteadOfOriginalReviewState()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				PlanContext context = CreatePlanContext(fixture, null, new CollectionNativeFileState[0]);
				CollectionVerifiedArchive archive = CreateVerifiedArchive(fixture, context.Plan, context.Member.MemberKey, "incoming");
				PreparedPlans plans = FinalizePlans(fixture, context, archive);
				CollectionNativeChildPreparationResult prepared = fixture.Preparer.PrepareNext(plans.Operation.Identity,
					plans.Plan, plans.Matches, plans.DependencyPlan, plans.ImpactPlan, plans.State,
					new[] { plans.Preview }, new[] { archive }, fixture.InstallInfoDirectory);

				CollectionNativeStateIndex safeState = CloneStateWithDeploymentSequence(plans.State, 1);
				var evidence = new CollectionNativeChildExecutionEvidence("game", 100, 200, "incoming.7z", plans.Preview,
					new CollectionNativeFileContentEvidence[0], new CollectionNativeFileContentEvidence[0],
					new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
					new CollectionExpectedReplayOperation[0]);
				CollectionNativeChildRecoveryManifest safeManifest = prepared.RecoveryManifest.WithExecutionEvidence(evidence)
					.WithTerminalStateFingerprint(safeState.Fingerprint).WithSafeBoundaryStateFingerprint(safeState.Fingerprint);
				fixture.ManifestStore.SaveManifest(safeManifest);

				var nativeResult = new ModOperationResult(prepared.Child.NativeOperation, ModOperationReportedStatus.Succeeded,
					ModOperationDurability.VerifiedCommitted, null);
				var reconciledChild = new CollectionNativeChildOperation(prepared.Child.Sequence, prepared.Child.Member,
					prepared.Child.Action, prepared.Child.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, nativeResult);
				var reconciledOperation = new CollectionOperation(prepared.Operation.Identity, prepared.Operation.Kind,
					prepared.Operation.Collection, prepared.Operation.Target, prepared.Operation.Revision, prepared.Operation.PlanIdentity,
					prepared.Operation.CheckpointSequence + 1, prepared.Operation.Phase, prepared.Operation.ResultState,
					new[] { reconciledChild });
				fixture.OperationStore.SaveOperation(reconciledOperation);

				CollectionNativeChildPreparationResult next = fixture.Preparer.PrepareNext(plans.Operation.Identity,
					plans.Plan, plans.Matches, plans.DependencyPlan, plans.ImpactPlan, safeState,
					new[] { plans.Preview }, new[] { archive }, fixture.InstallInfoDirectory);
				Assert.IsNull(next, "The only member is already reconciled; validation should accept the durable safe boundary and find no further child.");
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void ExclusiveRecoveryRole_RejectsDifferentImmutableBytes()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionsRetainedArtifact first = PublishText(fixture.Artifacts, "first");
				CollectionsRetainedArtifact second = PublishText(fixture.Artifacts, "second");
				string owner = Guid.NewGuid().ToString("D");
				fixture.References.AcquireExclusiveRoleReference(first.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation, owner, "recovery-role");
				Assert.Throws<InvalidOperationException>(() => fixture.References.AcquireExclusiveRoleReference(second.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Operation, owner, "recovery-role"));
				Assert.AreEqual(first.ArtifactId, fixture.References.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation,
					owner, "recovery-role").ArtifactId);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void ScriptedReplay_DefaultPathHelperMatchesLegacyConstructorConvention()
		{
			string installInfo = Path.Combine(Path.GetTempPath(), "install-info-" + Guid.NewGuid().ToString("N"));
			string path = ScriptedFileSelectionCache.GetDefaultFilePath(Path.Combine("archive", "Example.Mod.7z"), installInfo);
			Assert.AreEqual(Path.Combine(installInfo, "Scripted", "Example.Mod.xml"), path);
			Assert.Throws<ArgumentException>(() => new CollectionReplayRecoveryPayload("../escape.bin",
				new CollectionRecoveryArtifact("artifact", CollectionContentHash.FromSha256(ManifestSha), 1)));
		}

		private static PlanContext CreatePlanContext(Fixture fixture, CollectionNativeModState nativeMod, IEnumerable<CollectionNativeFileState> files)
		{
			NormalizedCollectionMember member = CreateMember();
			var state = new CollectionNativeStateIndex(fixture.Target, new CollectionNativeRootState[0],
				nativeMod == null ? new CollectionNativeModState[0] : new[] { nativeMod }, files,
				new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
				CollectionNativeStateCoverage.NotApplicable, new CollectionTargetAssociation[0], new CollectionMemberBinding[0],
				new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
			var manifest = new NormalizedCollectionManifest(fixture.Revision,
				new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(ManifestSha), 10, "schema", "normalizer-v3"),
				CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			CollectionOperation operation = fixture.Coordinator.CreateApplyOperation(fixture.Revision.Collection, fixture.Target);
			operation = fixture.Coordinator.BeginResolving(operation.Identity);
			operation = fixture.Coordinator.BeginPreparing(operation.Identity, fixture.Revision);
			CollectionPlanIdentity identity = fixture.Coordinator.CreateNextPlanIdentity(operation.Identity);
			var resolvedMember = new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
			var plan = new ResolvedCollectionPlan(identity, fixture.Target, CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				state.Fingerprint, report, new[] { resolvedMember });
			ModDeploymentTarget plannedTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "planned\\member.txt");
			var preview = new CollectionMemberEffectPreview(member.IdentityResolution.Key, member.RecipeIdentity,
				ModInstallMethod.Virtual, ModInstallRoot.Data, new[] { new CollectionPlannedFileEffect(plannedTarget) }, new CollectionPlannedIniEffect[0],
				new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			return new PlanContext(operation, plan, state, resolvedMember, preview);
		}

		private static PreparedPlans FinalizePlans(Fixture fixture, PlanContext context, CollectionVerifiedArchive archive)
		{
			CollectionMemberMatchSet matches = new CollectionMemberMatchEngine().Match(context.Plan, context.State,
				archive == null ? new CollectionVerifiedArchive[0] : new[] { archive });
			CollectionDependencyPhasePlan dependency = new CollectionDependencyPhasePlanner().Plan(context.Plan, matches);
			CollectionConflictImpactPlan impact = new CollectionConflictImpactPlanner().Plan(context.Plan, matches, dependency,
				context.State, new[] { context.Preview });
			Assert.IsTrue(dependency.IsReady);
			Assert.IsTrue(impact.IsReady);
			PreparedCollectionNativeRecipe preparedRecipe = CreatePreparedRecipe(context, archive);
			CollectionOperation operation = fixture.Coordinator.MarkReadyForReview(context.Operation.Identity, context.Plan, dependency, impact,
				new[] { preparedRecipe });
			operation = fixture.Coordinator.MarkReadyToApply(operation.Identity, context.Plan.Identity);
			operation = fixture.Coordinator.BeginApplying(operation.Identity, context.Plan.Identity);
			return new PreparedPlans(operation, context.Plan, matches, dependency, impact, context.State, context.Member, context.Preview);
		}

		private static PreparedCollectionNativeRecipe CreatePreparedRecipe(PlanContext context, CollectionVerifiedArchive archive)
		{
			if (archive == null) throw new ArgumentNullException(nameof(archive));
			const string sourcePath = "content\\member.txt";
			const string destinationPath = "planned\\member.txt";
			var installContext = new ModInstallContext(context.Preview.InstallMethod, context.Preview.InstallRoot);
			var validation = new ModInstallationRecipeValidation(ModInstallationSimpleFileRecipeAdapter.AdapterId,
				ModInstallationSimpleFileRecipeAdapter.AdapterVersion, installContext,
				new ModInstallationRecipeExpectedContent(archive.Artifact.ContentHash.Value, archive.Artifact.ByteLength),
				new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, ModInstallationSimpleFileRecipeAdapter.CapabilityVersion) },
				new[]
				{
					new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, sourcePath),
					new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, destinationPath)
				});
			var fingerprint = new ModOperationFingerprint(context.Plan.Target.Fingerprint, installContext, context.Member.RecipeIdentity.Fingerprint);
			var operation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, fingerprint);
			var input = new ModInstallationRecipeInput(operation, validation);
			var recipe = new ModInstallationSimpleFileRecipe(new[] { new ModInstallationSimpleFileMapping(sourcePath, destinationPath) });
			ModInstallationRecipeInput translated = new ModInstallationSimpleFileRecipeAdapter().Translate(input, recipe);
			return new PreparedCollectionNativeRecipe(context.Member,
				PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-c6-6-" + archive.Artifact.ContentHash.Value),
				translated, context.Preview, false, new[] { archive.Artifact.ArtifactId });
		}

		private static CollectionNativeStateIndex CloneStateWithDeploymentSequence(CollectionNativeStateIndex state, long sequence)
		{
			return new CollectionNativeStateIndex(state.Target, state.Roots, state.Mods.Values, state.Files.Values, state.IniEdits.Values,
				state.GameValues.Values, state.Plugins.Values, state.PluginCoverage, state.Associations.Values,
				state.BindingsByAssociation.Values.SelectMany(x => x), state.OverridesByAssociation.Values.SelectMany(x => x),
				state.AssociationCoverage, state.Issues, sequence);
		}

		private static NormalizedCollectionMember CreateMember()
		{
			return new NormalizedCollectionMember(0, CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "game/100/200", null),
				CollectionRecipeIdentity.FromFingerprint("recipe-member"), "Member", 0);
		}

		private static CollectionNativeModState CreateNativeMod(Fixture fixture, string archivePath, string fileName, bool hasInstallScript)
		{
			return new CollectionNativeModState(new NativeModInstanceIdentity(fixture.Target, "native-member"), archivePath, fileName,
				"100", "200", "1.0", "1.0", hasInstallScript, ModInstallRoot.Data, ModInstallMethod.Virtual);
		}

		private static CollectionVerifiedArchive CreateVerifiedArchive(Fixture fixture, ResolvedCollectionPlan plan, CollectionMemberKey memberKey, string content)
		{
			CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(Guid.NewGuid(), plan, memberKey);
			CollectionsRetainedArtifact artifact = PublishText(fixture.Artifacts, content);
			CollectionsRetainedArtifactReferenceRecord reference = fixture.References.AcquireReference(artifact.ArtifactId,
				CollectionsRetainedArtifactOwnerKind.Download, request.RequestId.ToString("D"), "verified-archive");
			return new CollectionVerifiedArchive(request, artifact, reference, CollectionVerifiedArchiveSourceKind.RetainedContent,
				CollectionArchiveVerificationBasis.ExistingVerifiedReference);
		}

		private static CollectionsRetainedArtifact PublishText(CollectionsRetainedArtifactStore store, string value)
		{ using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(value))) return store.Publish(stream); }
		private static string ReadText(CollectionsRetainedArtifactStore store, string artifactId)
		{ using (Stream stream = store.OpenRead(artifactId)) using (var reader = new StreamReader(stream)) return reader.ReadToEnd(); }

		private static Fixture CreateFixture(string root)
		{
			var store = new CollectionsStore(root); store.CreateNew();
			var catalog = new CollectionsCatalogStore(store);
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c6-6");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c6-6", 1);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "Collection C6.6", null, null), new CollectionRevision(revision, "Revision", null, 1));
			var operationStore = new CollectionsOperationStore(store);
			var planStore = new CollectionsResolvedPlanStore(store);
			var artifacts = new CollectionsRetainedArtifactStore(store);
			var references = new CollectionsRetainedArtifactReferenceStore(store);
			var manifests = new CollectionsNativeChildRecoveryManifestStore(artifacts, references);
			var coordinator = new CollectionOperationCoordinator(operationStore, planStore);
			var preparer = new CollectionNativeChildPreparationCoordinator(operationStore, planStore, artifacts, references, manifests);
			string installInfo = Path.Combine(root, "InstallInfo"); Directory.CreateDirectory(installInfo);
			return new Fixture(revision, CollectionTargetIdentity.FromFingerprint("target-c6-6-" + Guid.NewGuid().ToString("N")),
				operationStore, planStore, artifacts, references, manifests, coordinator, preparer, installInfo);
		}

		private static string CreateTemporaryDirectory()
		{ string path = Path.Combine(Path.GetTempPath(), "nmm-c6-6-preparation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }

		private sealed class PlanContext
		{
			public PlanContext(CollectionOperation operation, ResolvedCollectionPlan plan, CollectionNativeStateIndex state,
				ResolvedCollectionMemberPlan member, CollectionMemberEffectPreview preview)
			{ Operation = operation; Plan = plan; State = state; Member = member; Preview = preview; }
			public CollectionOperation Operation { get; } public ResolvedCollectionPlan Plan { get; } public CollectionNativeStateIndex State { get; }
			public ResolvedCollectionMemberPlan Member { get; } public CollectionMemberEffectPreview Preview { get; }
		}

		private sealed class PreparedPlans
		{
			public PreparedPlans(CollectionOperation operation, ResolvedCollectionPlan plan, CollectionMemberMatchSet matches,
				CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan, CollectionNativeStateIndex state,
				ResolvedCollectionMemberPlan member, CollectionMemberEffectPreview preview)
			{ Operation = operation; Plan = plan; Matches = matches; DependencyPlan = dependencyPlan; ImpactPlan = impactPlan; State = state; Member = member; Preview = preview; }
			public CollectionOperation Operation { get; } public ResolvedCollectionPlan Plan { get; } public CollectionMemberMatchSet Matches { get; }
			public CollectionDependencyPhasePlan DependencyPlan { get; } public CollectionConflictImpactPlan ImpactPlan { get; }
			public CollectionNativeStateIndex State { get; } public ResolvedCollectionMemberPlan Member { get; } public CollectionMemberEffectPreview Preview { get; }
		}

		private sealed class Fixture
		{
			public Fixture(CollectionRevisionIdentity revision, CollectionTargetIdentity target, CollectionsOperationStore operationStore,
				CollectionsResolvedPlanStore planStore, CollectionsRetainedArtifactStore artifacts, CollectionsRetainedArtifactReferenceStore references,
				CollectionsNativeChildRecoveryManifestStore manifestStore, CollectionOperationCoordinator coordinator,
				CollectionNativeChildPreparationCoordinator preparer, string installInfoDirectory)
			{ Revision = revision; Target = target; OperationStore = operationStore; PlanStore = planStore; Artifacts = artifacts; References = references; ManifestStore = manifestStore; Coordinator = coordinator; Preparer = preparer; InstallInfoDirectory = installInfoDirectory; }
			public CollectionRevisionIdentity Revision { get; } public CollectionTargetIdentity Target { get; } public CollectionsOperationStore OperationStore { get; }
			public CollectionsResolvedPlanStore PlanStore { get; } public CollectionsRetainedArtifactStore Artifacts { get; }
			public CollectionsRetainedArtifactReferenceStore References { get; } public CollectionsNativeChildRecoveryManifestStore ManifestStore { get; }
			public CollectionOperationCoordinator Coordinator { get; } public CollectionNativeChildPreparationCoordinator Preparer { get; } public string InstallInfoDirectory { get; }
		}
	}
}
