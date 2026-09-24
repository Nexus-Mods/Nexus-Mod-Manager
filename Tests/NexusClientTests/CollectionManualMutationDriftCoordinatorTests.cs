using System;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.11 ordinary/manual native mutation drift coverage.</summary>
	[TestFixture]
	public class CollectionManualMutationDriftCoordinatorTests
	{
		[Test]
		public void CommittedRemoval_MarksEveryBindingForSharedNativeMemberIncompleteAndPersistsCurrentDrift()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation first = fixture.AddAssociation("collection-a", "revision-a", "member-a", "native-shared");
				CollectionTargetAssociation second = fixture.AddAssociation("collection-b", "revision-b", "member-b", "native-shared");
				var coordinator = new CollectionManualMutationDriftCoordinator(fixture.Associations, fixture.Target);

				CollectionManualMutationCapture capture = coordinator.BeginNativeModMutation(
					CollectionManualMutationKind.Deactivate, "native-shared", null);
				coordinator.RecordCommittedNativeMutation(capture, null);
				// A repeated observation replaces the same current requirement instead of accumulating stale drift rows.
				coordinator.RecordCommittedNativeMutation(capture, null);

				Assert.AreEqual(CollectionAssociationState.Incomplete, fixture.Associations.GetAssociation(first.AssociationId).State);
				Assert.AreEqual(CollectionAssociationState.Incomplete, fixture.Associations.GetAssociation(second.AssociationId).State);
				AssertRemovalDrift(fixture.Associations.GetDriftObservations(first.AssociationId).Single(), "member-a");
				AssertRemovalDrift(fixture.Associations.GetDriftObservations(second.AssociationId).Single(), "member-b");
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void VirtualDisable_RecordsEnabledStateDriftWithoutTreatingMemberAsRemoved()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation association = fixture.AddAssociation("collection-a", "revision-a", "member-a", "native-a");
				var coordinator = new CollectionManualMutationDriftCoordinator(fixture.Associations, fixture.Target);

				CollectionManualMutationCapture capture = coordinator.BeginNativeModMutation(
					CollectionManualMutationKind.VirtualDisable, "native-a", null);
				coordinator.RecordCommittedNativeMutation(capture, null);

				Assert.AreEqual(CollectionAssociationState.Incomplete, fixture.Associations.GetAssociation(association.AssociationId).State);
				CollectionDriftObservation drift = fixture.Associations.GetDriftObservations(association.AssociationId).Single();
				Assert.AreEqual(CollectionRequirementAspect.MemberEnabledState, drift.Requirement.Aspect);
				Assert.AreEqual(CollectionRequirementStateKind.Present, drift.ExpectedState.Kind);
				Assert.AreEqual("enabled", drift.ExpectedState.Fingerprint);
				Assert.AreEqual(CollectionRequirementStateKind.Present, drift.ObservedState.Kind);
				Assert.AreEqual("disabled", drift.ObservedState.Fingerprint);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void VirtualDisableMatchingExplicitOverride_DoesNotInventDriftAndMarksAssociationModified()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation association = fixture.AddAssociation("collection-a", "revision-a", "member-a", "native-a");
				var requirement = new CollectionRequirementReference(association, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.MemberEnabledState, null);
				fixture.Associations.SaveOverride(new UserOverride(Guid.NewGuid(), requirement,
					CollectionRequirementState.Present("bool-v1", "enabled"),
					CollectionRequirementState.Present("bool-v1", "disabled"), "Keep disabled"));
				var coordinator = new CollectionManualMutationDriftCoordinator(fixture.Associations, fixture.Target);

				CollectionManualMutationCapture capture = coordinator.BeginNativeModMutation(
					CollectionManualMutationKind.VirtualDisable, "native-a", null);
				coordinator.RecordCommittedNativeMutation(capture, null);

				Assert.AreEqual(CollectionAssociationState.Modified, fixture.Associations.GetAssociation(association.AssociationId).State);
				Assert.AreEqual(0, fixture.Associations.GetDriftObservations(association.AssociationId).Count);
				Assert.AreEqual(1, fixture.Associations.GetOverrides(association.AssociationId).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Reinstall_InvalidatesDirectAndSharedOwnerAssociationsWithoutFabricatingExactRecipeDrift()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation direct = fixture.AddAssociation("collection-a", "revision-a", "member-a", "native-a");
				CollectionTargetAssociation neighbor = fixture.AddAssociation("collection-b", "revision-b", "member-b", "native-b");
				var coordinator = new CollectionManualMutationDriftCoordinator(fixture.Associations, fixture.Target);

				CollectionManualMutationCapture capture = coordinator.BeginNativeModMutation(
					CollectionManualMutationKind.Reinstall, "native-a", new[] { "native-b" });
				coordinator.RecordCommittedNativeMutation(capture, new[] { "native-b" });

				Assert.AreEqual(CollectionAssociationState.Modified, fixture.Associations.GetAssociation(direct.AssociationId).State);
				Assert.AreEqual(CollectionAssociationState.Modified, fixture.Associations.GetAssociation(neighbor.AssociationId).State);
				Assert.AreEqual(0, fixture.Associations.GetDriftObservations(direct.AssociationId).Count);
				Assert.AreEqual(0, fixture.Associations.GetDriftObservations(neighbor.AssociationId).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void FileOwnerChange_InvalidatesBothNativeOwnersWithoutInventingWinnerIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation previous = fixture.AddAssociation("collection-a", "revision-a", "member-a", "native-a");
				CollectionTargetAssociation selected = fixture.AddAssociation("collection-b", "revision-b", "member-b", "native-b");
				CollectionTargetAssociation otherCandidate = fixture.AddAssociation("collection-c", "revision-c", "member-c", "native-c");
				var coordinator = new CollectionManualMutationDriftCoordinator(fixture.Associations, fixture.Target);

				coordinator.RecordFileOwnerChange("native-a", "native-b", new[] { "native-a", "native-b", "native-c" });

				Assert.AreEqual(CollectionAssociationState.Modified, fixture.Associations.GetAssociation(previous.AssociationId).State);
				Assert.AreEqual(CollectionAssociationState.Modified, fixture.Associations.GetAssociation(selected.AssociationId).State);
				Assert.AreEqual(CollectionAssociationState.Modified, fixture.Associations.GetAssociation(otherCandidate.AssociationId).State);
				Assert.AreEqual(0, fixture.Associations.GetDriftObservations(previous.AssociationId).Count);
				Assert.AreEqual(0, fixture.Associations.GetDriftObservations(selected.AssociationId).Count);
				Assert.AreEqual(0, fixture.Associations.GetDriftObservations(otherCandidate.AssociationId).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AmbiguousMutation_MarksAffectedAssociationsRecoveringWithoutFabricatingDrift()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation direct = fixture.AddAssociation("collection-a", "revision-a", "member-a", "native-a");
				CollectionTargetAssociation neighbor = fixture.AddAssociation("collection-b", "revision-b", "member-b", "native-b");
				var coordinator = new CollectionManualMutationDriftCoordinator(fixture.Associations, fixture.Target);

				CollectionManualMutationCapture capture = coordinator.BeginNativeModMutation(
					CollectionManualMutationKind.Reinstall, "native-a", new[] { "native-b" });
				coordinator.RecordAmbiguousNativeMutation(capture, new[] { "native-b" });

				Assert.AreEqual(CollectionAssociationState.Recovering, fixture.Associations.GetAssociation(direct.AssociationId).State);
				Assert.AreEqual(CollectionAssociationState.Recovering, fixture.Associations.GetAssociation(neighbor.AssociationId).State);
				Assert.AreEqual(0, fixture.Associations.GetDriftObservations(direct.AssociationId).Count);
				Assert.AreEqual(0, fixture.Associations.GetDriftObservations(neighbor.AssociationId).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static void AssertRemovalDrift(CollectionDriftObservation drift, string memberKey)
		{
			Assert.AreEqual(CollectionRequirementAspect.MemberParticipation, drift.Requirement.Aspect);
			Assert.AreEqual(CollectionMemberKey.FromProvider(memberKey), drift.Requirement.MemberKey);
			Assert.AreEqual(CollectionRequirementStateKind.Present, drift.ExpectedState.Kind);
			Assert.AreEqual("included", drift.ExpectedState.Fingerprint);
			Assert.AreEqual(CollectionRequirementStateKind.Absent, drift.ObservedState.Kind);
			Assert.IsFalse(drift.IsUserIntent);
			Assert.IsTrue(drift.RequiresRepairPreview);
		}

		private static Fixture CreateFixture(string root)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			return new Fixture(store, CollectionTargetIdentity.FromFingerprint("target-c611"));
		}

		private static string CreateTemporaryDirectory()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-collections-c611-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			return directory;
		}

		private sealed class Fixture
		{
			private readonly CollectionsStore _store;

			public Fixture(CollectionsStore store, CollectionTargetIdentity target)
			{
				_store = store;
				Target = target;
				Associations = new CollectionsAssociationStore(store);
			}

			public CollectionTargetIdentity Target { get; }
			public CollectionsAssociationStore Associations { get; }

			public CollectionTargetAssociation AddAssociation(string collectionId, string revisionId,
				string memberKey, string nativeModKey)
			{
				CollectionIdentity collection = CollectionIdentity.FromNexus(collectionId);
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, revisionId, 1);
				new CollectionsCatalogStore(_store).SaveDefinitionAndRevision(
					new CollectionDefinition(collection, collectionId, null, null),
					new CollectionRevision(revision, revisionId, null, 1));
				var association = new CollectionTargetAssociation(Guid.NewGuid(), revision, Target, CollectionAssociationState.Applied);
				Associations.SaveAssociation(association);
				Associations.SaveBinding(new CollectionMemberBinding(association, CollectionMemberKey.FromProvider(memberKey),
					new NativeModInstanceIdentity(Target, nativeModKey), CollectionRecipeIdentity.FromFingerprint("recipe-" + memberKey),
					CollectionMemberBindingKind.InstalledForCollection));
				return association;
			}
		}
	}
}
