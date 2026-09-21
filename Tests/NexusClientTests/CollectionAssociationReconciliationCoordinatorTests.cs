using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.10 association/provenance reconciliation coverage.</summary>
	[TestFixture]
	public class CollectionAssociationReconciliationCoordinatorTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void ReconcileVerifiedCommittedChild_WritesIncompleteInstalledBindingAndReconciledCheckpoint()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, false);
				CollectionNativeModState nativeMod = CreateNativeMod(fixture.Plan.Target, "native-installed", "100", "200");
				CollectionNativeStateIndex postState = CreateState(fixture.Plan.Target, new[] { nativeMod }, null, null);
				CollectionNativeChildOperation child = CreateTerminalChild(fixture.Plan, ModOperationDurability.VerifiedCommitted);
				CollectionOperation operation = CreateApplyingOperation(fixture.Plan, child);
				fixture.OperationStore.SaveOperation(operation);
				CollectionNativeChildVerificationResult verification = CreateVerificationResult(operation, child, postState, nativeMod);

				var coordinator = new CollectionAssociationReconciliationCoordinator(
					fixture.OperationStore, fixture.PlanStore, fixture.AssociationStore);
				CollectionAssociationReconciliationResult result = coordinator.ReconcileVerifiedChild(verification, fixture.Plan);

				Assert.AreEqual(CollectionNativeChildCheckpoint.Reconciled, result.Child.Checkpoint);
				Assert.IsTrue(result.Child.HasVerifiedCommittedNativeState);
				Assert.IsNotNull(result.Association);
				Assert.AreEqual(CollectionAssociationState.Incomplete, result.Association.State);
				Assert.IsNotNull(result.Binding);
				Assert.AreEqual(CollectionMemberBindingKind.InstalledForCollection, result.Binding.BindingKind);
				Assert.AreEqual(nativeMod.Identity, result.Binding.NativeMod);
				Assert.AreEqual(fixture.Plan.SelectedMembers[0].RecipeIdentity, result.Binding.VerifiedRecipe);
				Assert.AreEqual(CollectionNativeChildCheckpoint.Reconciled,
					fixture.OperationStore.GetOperation(operation.Identity).NativeChildren.Single().Checkpoint);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReconcileVerifiedRollback_ReconcilesJournalWithoutInventingAssociationProvenance()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, false);
				CollectionNativeStateIndex postState = CreateState(fixture.Plan.Target, new CollectionNativeModState[0], null, null);
				CollectionNativeChildOperation child = CreateTerminalChild(fixture.Plan, ModOperationDurability.VerifiedRolledBack);
				CollectionOperation operation = CreateApplyingOperation(fixture.Plan, child);
				fixture.OperationStore.SaveOperation(operation);
				CollectionNativeChildVerificationResult verification = CreateVerificationResult(operation, child, postState, null);

				var coordinator = new CollectionAssociationReconciliationCoordinator(
					fixture.OperationStore, fixture.PlanStore, fixture.AssociationStore);
				CollectionAssociationReconciliationResult result = coordinator.ReconcileVerifiedChild(verification, fixture.Plan);

				Assert.AreEqual(CollectionNativeChildCheckpoint.Reconciled, result.Child.Checkpoint);
				Assert.IsNull(result.Association);
				Assert.IsNull(result.Binding);
				Assert.AreEqual(0, fixture.AssociationStore.GetAssociationsForTarget(fixture.Plan.Target).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ChildReconciliationStore_RollsBackAssociationAndBindingWhenJournalCheckpointFails()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, false);
				CollectionNativeChildOperation child = CreateTerminalChild(fixture.Plan, ModOperationDurability.VerifiedCommitted);
				CollectionOperation operation = CreateApplyingOperation(fixture.Plan, child);
				fixture.OperationStore.SaveOperation(operation);
				var association = new CollectionTargetAssociation(Guid.NewGuid(), fixture.Plan.Revision, fixture.Plan.Target,
					CollectionAssociationState.Incomplete);
				var binding = new CollectionMemberBinding(association, fixture.Plan.SelectedMembers[0].MemberKey,
					new NativeModInstanceIdentity(fixture.Plan.Target, "native-atomic"), fixture.Plan.SelectedMembers[0].RecipeIdentity,
					CollectionMemberBindingKind.InstalledForCollection);
				var reconciled = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
					CollectionNativeChildCheckpoint.Reconciled, child.NativeResult);
				var invalidSameSequence = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, operation.PlanIdentity, operation.CheckpointSequence, operation.Phase, operation.ResultState,
					new[] { reconciled });

				MethodInfo method = typeof(CollectionsAssociationStore).GetMethod("SaveChildReconciliation",
					BindingFlags.Instance | BindingFlags.NonPublic);
				Assert.IsNotNull(method);
				TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
					method.Invoke(fixture.AssociationStore, new object[] { invalidSameSequence, association, binding }));
				Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
				Assert.AreEqual(0, fixture.AssociationStore.GetAssociationsForTarget(fixture.Plan.Target).Count);
				Assert.AreEqual(CollectionNativeChildCheckpoint.NativeTerminalObserved,
					fixture.OperationStore.GetOperation(operation.Identity).NativeChildren.Single().Checkpoint);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void FinalizeAppliedAssociation_ArchiveOnlyInstalledMemberRecordsVerifiedNoStandaloneUse()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, false);
				CollectionVerifiedArchive archive = CreateVerifiedArchive(fixture.Plan);
				CollectionMemberMatchSet matches = new CollectionMemberMatchEngine().Match(fixture.Plan, fixture.InitialState,
					new[] { archive });
				Assert.AreEqual(CollectionMemberMatchDisposition.ArchiveOnlyReuse, matches.Members.Single().Disposition);

				CollectionNativeModState nativeMod = CreateNativeMod(fixture.Plan.Target, "native-collection-only", "100", "200");
				CollectionNativeStateIndex postState = CreateState(fixture.Plan.Target, new[] { nativeMod }, null, null);
				CollectionNativeChildOperation child = CreateTerminalChild(fixture.Plan, ModOperationDurability.VerifiedCommitted);
				CollectionOperation operation = CreateApplyingOperation(fixture.Plan, child);
				fixture.OperationStore.SaveOperation(operation);
				CollectionNativeChildVerificationResult verification = CreateVerificationResult(operation, child, postState, nativeMod);
				var coordinator = new CollectionAssociationReconciliationCoordinator(
					fixture.OperationStore, fixture.PlanStore, fixture.AssociationStore);
				CollectionAssociationReconciliationResult reconciled = coordinator.ReconcileVerifiedChild(verification, fixture.Plan);

				CollectionAssociationFinalizationResult result = coordinator.FinalizeAppliedAssociation(reconciled.Operation.Identity,
					fixture.Plan, matches);

				Assert.AreEqual(CollectionOperationResultState.Committed, result.Operation.ResultState);
				Assert.AreEqual(StandaloneModUse.NoStandaloneUseVerified,
					fixture.AssociationStore.GetNativeModProvenance(nativeMod.Identity).StandaloneUse);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void FinalizeAppliedAssociation_AdoptsInstalledCompatibleMemberAndCommitsOperation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, true);
				CollectionMemberMatchSet matches = new CollectionMemberMatchEngine().Match(fixture.Plan, fixture.InitialState);
				Assert.AreEqual(CollectionMemberMatchDisposition.InstalledCompatible, matches.Members.Single().Disposition);
				CollectionOperation operation = CreateApplyingOperation(fixture.Plan, null);
				fixture.OperationStore.SaveOperation(operation);

				var coordinator = new CollectionAssociationReconciliationCoordinator(
					fixture.OperationStore, fixture.PlanStore, fixture.AssociationStore);
				CollectionAssociationFinalizationResult result = coordinator.FinalizeAppliedAssociation(operation.Identity,
					fixture.Plan, matches);

				Assert.AreEqual(CollectionAssociationState.Applied, result.Association.State);
				Assert.AreEqual(1, result.Bindings.Count);
				Assert.AreEqual(CollectionMemberBindingKind.AdoptedExisting, result.Bindings[0].BindingKind);
				Assert.AreEqual(CollectionOperationPhase.Completed, result.Operation.Phase);
				Assert.AreEqual(CollectionOperationResultState.Committed, result.Operation.ResultState);
				Assert.AreEqual(2, fixture.AssociationStore.GetBindingsForNativeMod(result.Bindings[0].NativeMod).Count);
				Assert.AreEqual(StandaloneModUse.Unknown,
					fixture.AssociationStore.GetNativeModProvenance(result.Bindings[0].NativeMod).StandaloneUse);

				CollectionAssociationFinalizationResult retry = coordinator.FinalizeAppliedAssociation(operation.Identity,
					fixture.Plan, matches);
				Assert.AreEqual(result.Association.AssociationId, retry.Association.AssociationId);
				Assert.AreEqual(CollectionOperationResultState.Committed, retry.Operation.ResultState);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static Fixture CreateFixture(string root, bool seedCompatibleAssociation)
		{
			var featureStore = new CollectionsStore(root);
			featureStore.CreateNew();
			var catalog = new CollectionsCatalogStore(featureStore);
			var operationStore = new CollectionsOperationStore(featureStore);
			var planStore = new CollectionsResolvedPlanStore(featureStore);
			var associationStore = new CollectionsAssociationStore(featureStore);
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c610-" + Guid.NewGuid().ToString("N"));
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(
				CollectionIdentity.FromNexus("collection-c610"), "revision-1", 1);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(revision.Collection, "Collection", null, null),
				new CollectionRevision(revision, "Revision", null, 1));

			CollectionRecipeIdentity recipe = CollectionRecipeIdentity.FromFingerprint("recipe-c610");
			NormalizedCollectionMember normalized = new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-1")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/100/200", null),
				recipe, "Member");

			CollectionNativeStateIndex initialState;
			if (seedCompatibleAssociation)
			{
				CollectionRevisionIdentity otherRevision = CollectionRevisionIdentity.FromNexus(
					CollectionIdentity.FromNexus("other-c610"), "other-revision", 1);
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(otherRevision.Collection, "Other", null, null),
					new CollectionRevision(otherRevision, "Other revision", null, 1));
				CollectionNativeModState nativeMod = CreateNativeMod(target, "native-existing", "100", "200");
				var association = new CollectionTargetAssociation(Guid.NewGuid(), otherRevision, target, CollectionAssociationState.Applied);
				associationStore.SaveAssociation(association);
				var binding = new CollectionMemberBinding(association, CollectionMemberKey.FromProvider("other-member"),
					nativeMod.Identity, recipe, CollectionMemberBindingKind.InstalledForCollection);
				associationStore.SaveBinding(binding);
				initialState = CreateState(target, new[] { nativeMod }, new[] { association }, new[] { binding });
			}
			else
			{
				initialState = CreateState(target, new CollectionNativeModState[0], null, null);
			}

			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(Sha256A), 100, "schema-v1", "normalizer-v1");
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(revision, source,
				CollectionManifestMemberSetCompleteness.Complete, null, new[] { normalized });
			ResolvedCollectionPlan plan = new ResolvedCollectionPlan(
				CollectionPlanIdentity.From(Guid.NewGuid(), 1), target, CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				initialState.Fingerprint, CollectionCapabilityReport.Create(manifest),
				new[] { new ResolvedCollectionMemberPlan(normalized, CollectionResolvedArtifactChoice.Exact(normalized.Artifact)) });
			planStore.SavePlan(plan, "c610-test/1", new byte[] { 1 });
			return new Fixture(plan, initialState, operationStore, planStore, associationStore);
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target, CollectionNativeModState[] mods,
			CollectionTargetAssociation[] associations, CollectionMemberBinding[] bindings)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], mods,
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				associations ?? new CollectionTargetAssociation[0], bindings ?? new CollectionMemberBinding[0],
				new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
		}

		private static CollectionNativeModState CreateNativeMod(CollectionTargetIdentity target, string nativeKey,
			string nexusModId, string nexusFileId)
		{
			return new CollectionNativeModState(new NativeModInstanceIdentity(target, nativeKey),
				"C:\\Mods\\Example.7z", "Example.7z", nexusModId, nexusFileId, "1.0", "1.0.0.0",
				ModInstallRoot.Data, ModInstallMethod.Virtual);
		}

		private static CollectionNativeChildOperation CreateTerminalChild(ResolvedCollectionPlan plan, ModOperationDurability durability)
		{
			ResolvedCollectionMemberPlan member = plan.SelectedMembers.Single();
			ModOperationIdentity nativeOperation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(plan.Target.Fingerprint,
					new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), member.RecipeIdentity.Fingerprint));
			var result = new ModOperationResult(nativeOperation, ModOperationReportedStatus.Succeeded, durability, null);
			return new CollectionNativeChildOperation(1,
				new CollectionOperationMemberReference(plan.Revision, member.MemberKey),
				CollectionNativeChildAction.ActivateOrReinstall, nativeOperation,
				CollectionNativeChildCheckpoint.NativeTerminalObserved, result);
		}

		private static CollectionOperation CreateApplyingOperation(ResolvedCollectionPlan plan, CollectionNativeChildOperation child)
		{
			return new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ApplyResolvedPlan,
				plan.Revision.Collection, plan.Target, plan.Revision, plan.Identity, 10,
				CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending,
				child == null ? new CollectionNativeChildOperation[0] : new[] { child });
		}

		private static CollectionNativeChildVerificationResult CreateVerificationResult(CollectionOperation operation,
			CollectionNativeChildOperation child, CollectionNativeStateIndex nativeState, CollectionNativeModState nativeMod)
		{
			ConstructorInfo constructor = typeof(CollectionNativeChildVerificationResult).GetConstructors(
				BindingFlags.Instance | BindingFlags.NonPublic).Single();
			return (CollectionNativeChildVerificationResult)constructor.Invoke(new object[]
			{
				operation, child, nativeState, nativeMod, child.NativeResult.Durability
			});
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c610-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static CollectionVerifiedArchive CreateVerifiedArchive(ResolvedCollectionPlan plan)
		{
			CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(Guid.NewGuid(), plan,
				plan.SelectedMembers[0].MemberKey);
			var artifact = new CollectionsRetainedArtifact("artifact-c610", CollectionContentHash.FromSha256(Sha256A), 100);
			var reference = new CollectionsRetainedArtifactReferenceRecord("reference-c610", artifact.ArtifactId,
				CollectionsRetainedArtifactOwnerKind.Download, request.RequestId.ToString("D"), "verified-c610");
			return new CollectionVerifiedArchive(request, artifact, reference,
				CollectionVerifiedArchiveSourceKind.RetainedContent, CollectionArchiveVerificationBasis.ExistingVerifiedReference);
		}

		private sealed class Fixture
		{
			public Fixture(ResolvedCollectionPlan plan, CollectionNativeStateIndex initialState,
				CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
				CollectionsAssociationStore associationStore)
			{
				Plan = plan;
				InitialState = initialState;
				OperationStore = operationStore;
				PlanStore = planStore;
				AssociationStore = associationStore;
			}

			public ResolvedCollectionPlan Plan { get; }
			public CollectionNativeStateIndex InitialState { get; }
			public CollectionsOperationStore OperationStore { get; }
			public CollectionsResolvedPlanStore PlanStore { get; }
			public CollectionsAssociationStore AssociationStore { get; }
		}
	}
}
