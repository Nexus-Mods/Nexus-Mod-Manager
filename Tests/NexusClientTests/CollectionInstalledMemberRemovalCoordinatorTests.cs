using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	[Category("CollectionsGateA")]
	[Category("CollectionsManagement")]
	public class CollectionInstalledMemberRemovalCoordinatorTests
	{
		[Test]
		public void Plan_SoleMemberWithVerifiedNoStandaloneUse_RemovesNativeMod()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "remove-native");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));

				CollectionInstalledMemberRemovalPlan plan = CollectionInstalledMemberRemovalCoordinator.BuildPlanForState(
					fixture.Associations, fixture.Association, fixture.Binding, CreateState(fixture, true));

				Assert.That(plan.Disposition, Is.EqualTo(CollectionInstalledMemberRemovalDisposition.RemoveNativeMod));
				Assert.That(plan.RequiresNativeMutation, Is.True);
				Assert.That(plan.HasBlockedImpact, Is.False);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_SharedWithAnotherCollection_PreservesNativeMod()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "shared");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				CollectionTargetAssociation other = SeedAssociation(fixture.Store, "shared-other", fixture.Target);
				var otherBinding = new CollectionMemberBinding(other, CollectionMemberKey.FromProvider("other-member"),
					fixture.NativeMod, fixture.Binding.VerifiedRecipe, CollectionMemberBindingKind.AdoptedExisting);
				fixture.Associations.SaveBinding(otherBinding);

				CollectionInstalledMemberRemovalPlan plan = CollectionInstalledMemberRemovalCoordinator.BuildPlanForState(
					fixture.Associations, fixture.Association, fixture.Binding,
					CreateState(fixture, true, new[] { other }, new[] { otherBinding }));

				Assert.That(plan.Disposition, Is.EqualTo(CollectionInstalledMemberRemovalDisposition.PreserveSharedCollection));
				Assert.That(plan.RequiresNativeMutation, Is.False);
				CollectionAssert.Contains(plan.SurvivingAssociationIds, other.AssociationId);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_SecondMemberInSameAssociation_PreservesNativeMod()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "same-association");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				var second = new CollectionMemberBinding(fixture.Association, CollectionMemberKey.FromProvider("member-2"),
					fixture.NativeMod, fixture.Binding.VerifiedRecipe, CollectionMemberBindingKind.AdoptedExisting);
				fixture.Associations.SaveBinding(second);

				CollectionInstalledMemberRemovalPlan plan = CollectionInstalledMemberRemovalCoordinator.BuildPlanForState(
					fixture.Associations, fixture.Association, fixture.Binding, CreateState(fixture, true));

				Assert.That(plan.Disposition, Is.EqualTo(CollectionInstalledMemberRemovalDisposition.PreserveOtherMember));
				Assert.That(plan.RequiresNativeMutation, Is.False);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_MemberCustomization_PreservesNativeMod()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "customized");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				var requirement = new CollectionRequirementReference(fixture.Association, fixture.Binding.MemberKey,
					CollectionRequirementAspect.MemberEnabledState, null);
				fixture.Associations.SaveOverride(new UserOverride(Guid.NewGuid(), requirement,
					CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Absent(), "keep local"));
				fixture.Associations.SaveAssociation(fixture.Association.WithState(CollectionAssociationState.Modified));
				CollectionTargetAssociation modified = fixture.Associations.GetAssociation(fixture.Association.AssociationId);

				CollectionInstalledMemberRemovalPlan plan = CollectionInstalledMemberRemovalCoordinator.BuildPlanForState(
					fixture.Associations, modified, fixture.Binding, CreateState(fixture, true));

				Assert.That(plan.Disposition, Is.EqualTo(CollectionInstalledMemberRemovalDisposition.PreserveCustomized));
				Assert.That(plan.RequiresNativeMutation, Is.False);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_AlreadyAbsentWithUnknownProvenance_RemovesOnlyCollectionBinding()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "already-absent");

				CollectionInstalledMemberRemovalPlan plan = CollectionInstalledMemberRemovalCoordinator.BuildPlanForState(
					fixture.Associations, fixture.Association, fixture.Binding, CreateState(fixture, false));

				Assert.That(plan.Disposition, Is.EqualTo(CollectionInstalledMemberRemovalDisposition.AlreadyAbsent));
				Assert.That(plan.RequiresNativeMutation, Is.False);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_ModifiedAssociationWithoutSelectedMemberCustomization_CanStillRemoveExclusiveNativeMod()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "modified-membership");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				fixture.Associations.SaveAssociation(fixture.Association.WithState(CollectionAssociationState.Modified));
				CollectionTargetAssociation modified = fixture.Associations.GetAssociation(fixture.Association.AssociationId);

				CollectionInstalledMemberRemovalPlan plan = CollectionInstalledMemberRemovalCoordinator.BuildPlanForState(
					fixture.Associations, modified, fixture.Binding, CreateState(fixture, true));

				Assert.That(plan.Disposition, Is.EqualTo(CollectionInstalledMemberRemovalDisposition.RemoveNativeMod));
				Assert.That(plan.RequiresNativeMutation, Is.True);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void MetadataOnlyCompletion_RemovesOnlySelectedBindingAndEstablishesStandaloneUse()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "metadata-only");
				CollectionOperation operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(),
					CollectionOperationKind.RemoveCollectionMemberEffects, fixture.Association.Revision.Collection, fixture.Target,
					fixture.Association.Revision, null, 1, CollectionOperationPhase.Completed,
					CollectionOperationResultState.Committed, new CollectionNativeChildOperation[0]);

				fixture.Associations.CompleteMemberRemovalWithoutNativeMutation(fixture.Association, fixture.Binding,
					operation, true, false);

				Assert.That(fixture.Associations.GetBindings(fixture.Association.AssociationId), Is.Empty);
				Assert.That(fixture.Associations.GetAssociation(fixture.Association.AssociationId).State,
					Is.EqualTo(CollectionAssociationState.Modified));
				Assert.That(fixture.Associations.GetNativeModProvenance(fixture.NativeMod).StandaloneUse,
					Is.EqualTo(StandaloneModUse.ExplicitStandaloneUse));
				Assert.That(new CollectionsOperationStore(fixture.Store).GetOperation(operation.Identity).IsSuccessful, Is.True);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Submission_SharingAppearingAfterReview_IsRejectedBeforeNativeBoundary()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "sharing-race");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				CollectionOperation operation = CreatePreparedOperation(fixture);
				fixture.Associations.BeginMemberRemoval(fixture.Association, fixture.Binding, operation);

				CollectionTargetAssociation other = SeedAssociation(fixture.Store, "sharing-race-other", fixture.Target);
				fixture.Associations.SaveBinding(new CollectionMemberBinding(other, CollectionMemberKey.FromProvider("other-member"),
					fixture.NativeMod, fixture.Binding.VerifiedRecipe, CollectionMemberBindingKind.AdoptedExisting));
				CollectionNativeChildOperation child = operation.NativeChildren.Single();
				var submitted = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
					CollectionNativeChildCheckpoint.NativeSubmitted, null);
				var submittedOperation = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, operation.PlanIdentity, 2, operation.Phase, operation.ResultState, new[] { submitted });

				Assert.Throws<InvalidOperationException>(() => fixture.Associations.SaveMemberRemovalChildSubmission(
					fixture.Association.AssociationId, fixture.Binding.MemberKey, submittedOperation, fixture.NativeMod));
				CollectionOperation persisted = new CollectionsOperationStore(fixture.Store).GetOperation(operation.Identity);
				Assert.That(persisted.NativeChildren.Single().Checkpoint, Is.EqualTo(CollectionNativeChildCheckpoint.RecoveryInputsReady));
				Assert.That(fixture.Associations.GetAssociation(fixture.Association.AssociationId).State,
					Is.EqualTo(CollectionAssociationState.Applied));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Completion_SharingAppearingAfterNativeBoundary_MarksSurvivingAssociationIncomplete()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "completion-sharing-race");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				CollectionOperation operation = CreatePreparedOperation(fixture);
				fixture.Associations.BeginMemberRemoval(fixture.Association, fixture.Binding, operation);
				CollectionNativeChildOperation child = operation.NativeChildren.Single();
				var submitted = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
					CollectionNativeChildCheckpoint.NativeSubmitted, null);
				operation = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, operation.PlanIdentity, 2, operation.Phase, operation.ResultState, new[] { submitted });
				fixture.Associations.SaveMemberRemovalChildSubmission(fixture.Association.AssociationId, fixture.Binding.MemberKey,
					operation, fixture.NativeMod);

				CollectionTargetAssociation other = SeedAssociation(fixture.Store, "completion-sharing-race-other", fixture.Target);
				fixture.Associations.SaveBinding(new CollectionMemberBinding(other, CollectionMemberKey.FromProvider("other-member"),
					fixture.NativeMod, fixture.Binding.VerifiedRecipe, CollectionMemberBindingKind.AdoptedExisting));

				var result = new ModOperationResult(child.NativeOperation, ModOperationReportedStatus.Succeeded,
					ModOperationDurability.VerifiedCommitted, "verified absent");
				var reconciled = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member, submitted.Action,
					submitted.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, result);
				operation = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, operation.PlanIdentity, 3, CollectionOperationPhase.Completed,
					CollectionOperationResultState.Committed, new[] { reconciled });
				fixture.Associations.CompleteMemberRemovalAfterNativeRemoval(fixture.Association.AssociationId,
					fixture.Binding.MemberKey, fixture.NativeMod, operation);

				Assert.That(fixture.Associations.GetBindings(fixture.Association.AssociationId), Is.Empty);
				Assert.That(fixture.Associations.GetAssociation(fixture.Association.AssociationId).State,
					Is.EqualTo(CollectionAssociationState.Modified));
				Assert.That(fixture.Associations.GetAssociation(other.AssociationId).State,
					Is.EqualTo(CollectionAssociationState.Incomplete));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void RemovalJournalKey_IsStableAndAssociationScoped()
		{
			CollectionMemberKey member = CollectionMemberKey.FromProvider("member");
			Guid associationA = Guid.NewGuid();
			Guid associationB = Guid.NewGuid();

			CollectionMemberKey first = CollectionInstalledMemberRemovalCoordinator.CreateRemovalJournalKey(associationA, member);
			CollectionMemberKey second = CollectionInstalledMemberRemovalCoordinator.CreateRemovalJournalKey(associationA, member);
			CollectionMemberKey other = CollectionInstalledMemberRemovalCoordinator.CreateRemovalJournalKey(associationB, member);

			Assert.That(first, Is.EqualTo(second));
			Assert.That(first, Is.Not.EqualTo(other));
		}

		private static CollectionOperation CreatePreparedOperation(Fixture fixture)
		{
			CollectionOperationIdentity identity = CollectionOperationIdentity.CreateNew();
			var nativeOperation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(fixture.Target.Fingerprint,
					new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), null));
			var journalKey = CollectionInstalledMemberRemovalCoordinator.CreateRemovalJournalKey(
				fixture.Association.AssociationId, fixture.Binding.MemberKey);
			var child = new CollectionNativeChildOperation(1,
				new CollectionOperationMemberReference(fixture.Association.Revision, journalKey),
				CollectionNativeChildAction.Deactivate, nativeOperation, CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
			return new CollectionOperation(identity, CollectionOperationKind.RemoveCollectionMemberEffects,
				fixture.Association.Revision.Collection, fixture.Target, fixture.Association.Revision, null, 1,
				CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending, new[] { child });
		}

		private static CollectionNativeStateIndex CreateState(Fixture fixture, bool includeNativeMod,
			IEnumerable<CollectionTargetAssociation> extraAssociations = null,
			IEnumerable<CollectionMemberBinding> extraBindings = null)
		{
			var associations = new List<CollectionTargetAssociation> { fixture.Association };
			if (extraAssociations != null) associations.AddRange(extraAssociations);
			var bindings = new List<CollectionMemberBinding>(fixture.Associations.GetBindings(fixture.Association.AssociationId));
			if (extraBindings != null) bindings.AddRange(extraBindings);
			return new CollectionNativeStateIndex(fixture.Target, new CollectionNativeRootState[0],
				includeNativeMod ? new[] { fixture.NativeState } : new CollectionNativeModState[0],
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				associations, bindings, new UserOverride[0], CollectionNativeStateCoverage.Complete,
				new CollectionNativeStateIssue[0], 0);
		}

		private static Fixture CreateFixture(string root, string collectionId)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c911-" + collectionId);
			CollectionTargetAssociation association = SeedAssociation(store, collectionId, target);
			var associations = new CollectionsAssociationStore(store);
			var nativeMod = new NativeModInstanceIdentity(target, "native-1");
			var binding = new CollectionMemberBinding(association, CollectionMemberKey.FromProvider("member-1"), nativeMod,
				CollectionRecipeIdentity.FromFingerprint("recipe-1"), CollectionMemberBindingKind.InstalledForCollection);
			associations.SaveBinding(binding);
			var nativeState = new CollectionNativeModState(nativeMod, "C:\\Mods\\Example.7z", "Example.7z", "100", "200",
				"1.0", "1.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
			return new Fixture(store, associations, target, association, binding, nativeMod, nativeState);
		}

		private static CollectionTargetAssociation SeedAssociation(CollectionsStore store, string collectionId,
			CollectionTargetIdentity target)
		{
			var catalog = new CollectionsCatalogStore(store);
			CollectionIdentity collection = CollectionIdentity.FromNexus(collectionId);
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-1", 1);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, collectionId, null, null),
				new CollectionRevision(revision, "Revision", null, 1));
			var association = new CollectionTargetAssociation(Guid.NewGuid(), revision, target, CollectionAssociationState.Applied);
			new CollectionsAssociationStore(store).SaveAssociation(association);
			return association;
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c911-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private sealed class Fixture
		{
			public Fixture(CollectionsStore store, CollectionsAssociationStore associations, CollectionTargetIdentity target,
				CollectionTargetAssociation association, CollectionMemberBinding binding, NativeModInstanceIdentity nativeMod,
				CollectionNativeModState nativeState)
			{
				Store = store;
				Associations = associations;
				Target = target;
				Association = association;
				Binding = binding;
				NativeMod = nativeMod;
				NativeState = nativeState;
			}

			public CollectionsStore Store { get; }
			public CollectionsAssociationStore Associations { get; }
			public CollectionTargetIdentity Target { get; }
			public CollectionTargetAssociation Association { get; }
			public CollectionMemberBinding Binding { get; }
			public NativeModInstanceIdentity NativeMod { get; }
			public CollectionNativeModState NativeState { get; }
		}
	}
}
