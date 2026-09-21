using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.Tests
{
	[TestFixture]
	public class CollectionPinOverrideCoordinatorTests
	{
		[Test]
		public void GetMemberPins_ReturnsEveryAssociationSharingNativeMemberAndExistingLocalDecisions()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation first = fixture.AddAssociation("collection-a", "revision-a", "member-a",
					"native-shared", "recipe-shared");
				CollectionTargetAssociation second = fixture.AddAssociation("collection-b", "revision-b", "member-b",
					"native-shared", "recipe-shared");
				var requirement = new CollectionRequirementReference(first, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.InstallerRecipe, null);
				var coordinator = new CollectionPinOverrideCoordinator(fixture.Associations, fixture.Target);
				coordinator.RecordDecision(requirement,
					CollectionRequirementState.Present("recipe-v1", "recipe-shared"),
					CollectionRequirementState.Present("recipe-v1", "recipe-local"),
					CollectionRequirementState.Present("recipe-v1", "recipe-observed"), "Keep local choices");

				CollectionMemberPinImpact[] impacts = coordinator.GetMemberPins("native-shared").ToArray();

				Assert.AreEqual(2, impacts.Length);
				CollectionMemberPinImpact firstImpact = impacts.Single(x => x.Binding.Association.AssociationId == first.AssociationId);
				CollectionMemberPinImpact secondImpact = impacts.Single(x => x.Binding.Association.AssociationId == second.AssociationId);
				Assert.AreEqual("recipe-shared", firstImpact.VerifiedRecipe.Fingerprint);
				Assert.IsTrue(firstImpact.RequiresExplicitMutationDecision);
				Assert.IsTrue(firstImpact.HasExplicitLocalDecision);
				Assert.IsTrue(firstImpact.HasDetectedDrift);
				Assert.AreEqual(1, firstImpact.MemberOverrides.Count);
				Assert.AreEqual(1, firstImpact.MemberDrift.Count);
				Assert.IsFalse(secondImpact.HasExplicitLocalDecision);
				Assert.IsFalse(secondImpact.HasDetectedDrift);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcceptCurrentDrift_ConvertsObservedDifferenceToExplicitOverrideWithoutRepairingNativeState()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation association = fixture.AddAssociation("collection-a", "revision-a", "member-a",
					"native-a", "recipe-a");
				var driftCoordinator = new CollectionManualMutationDriftCoordinator(fixture.Associations, fixture.Target);
				CollectionManualMutationCapture capture = driftCoordinator.BeginNativeModMutation(
					CollectionManualMutationKind.VirtualDisable, "native-a", null);
				driftCoordinator.RecordCommittedNativeMutation(capture, null);
				CollectionDriftObservation detected = fixture.Associations.GetDriftObservations(association.AssociationId).Single();
				var coordinator = new CollectionPinOverrideCoordinator(fixture.Associations, fixture.Target);

				CollectionOverrideDecisionResult result = coordinator.AcceptCurrentDrift(
					association.AssociationId, detected.ObservationId, "Keep this member disabled");

				Assert.IsTrue(result.HasOverride);
				Assert.IsFalse(result.HasDrift);
				Assert.AreEqual(CollectionRequirementAspect.MemberEnabledState, result.UserOverride.Requirement.Aspect);
				Assert.AreEqual("enabled", result.UserOverride.BaselineState.Fingerprint);
				Assert.AreEqual("disabled", result.UserOverride.UserChosenState.Fingerprint);
				Assert.AreEqual("Keep this member disabled", result.UserOverride.Note);
				Assert.AreEqual(CollectionAssociationState.Incomplete, result.Association.State,
					"Accepting a deliberate omission must not pretend the required baseline effect is complete.");
				Assert.AreEqual(0, fixture.Associations.GetDriftObservations(association.AssociationId).Count);
				Assert.AreEqual(1, fixture.Associations.GetOverrides(association.AssociationId).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RecordDecision_TracksChosenPinAndObservedMismatchSeparately()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation association = fixture.AddAssociation("collection-a", "revision-a", "member-a",
					"native-a", "recipe-a");
				var requirement = new CollectionRequirementReference(association, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.InstallerRecipe, null);
				CollectionRequirementState baseline = CollectionRequirementState.Present("recipe-v1", "recipe-a");
				CollectionRequirementState chosen = CollectionRequirementState.Present("recipe-v1", "recipe-local");
				var coordinator = new CollectionPinOverrideCoordinator(fixture.Associations, fixture.Target);

				CollectionOverrideDecisionResult result = coordinator.RecordDecision(requirement, baseline, chosen,
					baseline, "Pin my local installer choices");

				Assert.IsTrue(result.HasOverride);
				Assert.IsTrue(result.HasDrift);
				Assert.AreEqual(chosen, result.UserOverride.UserChosenState);
				Assert.AreEqual(chosen, result.Drift.ExpectedState);
				Assert.AreEqual(baseline, result.Drift.ObservedState);
				Assert.AreEqual(CollectionAssociationState.Modified, result.Association.State);
				Assert.IsTrue(result.Customization.RequiresReview);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ClearOverride_DoesNotPretendRealityReturnedToBaseline()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation association = fixture.AddAssociation("collection-a", "revision-a", "member-a",
					"native-a", "recipe-a");
				var requirement = new CollectionRequirementReference(association, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.MemberEnabledState, null);
				CollectionRequirementState enabled = CollectionRequirementState.Present("bool-v1", "enabled");
				CollectionRequirementState disabled = CollectionRequirementState.Present("bool-v1", "disabled");
				var coordinator = new CollectionPinOverrideCoordinator(fixture.Associations, fixture.Target);
				CollectionOverrideDecisionResult overridden = coordinator.RecordDecision(requirement, enabled, disabled,
					disabled, "Keep disabled");

				CollectionOverrideDecisionResult cleared = coordinator.ClearOverride(association.AssociationId,
					overridden.UserOverride.OverrideId, disabled);

				Assert.IsFalse(cleared.HasOverride);
				Assert.IsTrue(cleared.HasDrift);
				Assert.AreEqual(enabled, cleared.Drift.ExpectedState);
				Assert.AreEqual(disabled, cleared.Drift.ObservedState);
				Assert.AreEqual(CollectionAssociationState.Modified, cleared.Association.State,
					"Clearing tracking must not silently repair native state or restore Applied status.");
				Assert.AreEqual(0, fixture.Associations.GetOverrides(association.AssociationId).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcceptCurrentDrift_UpdatesExistingOverrideWithoutChangingItsDurableIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation association = fixture.AddAssociation("collection-a", "revision-a", "member-a",
					"native-a", "recipe-a");
				var requirement = new CollectionRequirementReference(association, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.InstallerRecipe, null);
				CollectionRequirementState baseline = CollectionRequirementState.Present("recipe-v1", "recipe-a");
				CollectionRequirementState chosen = CollectionRequirementState.Present("recipe-v1", "recipe-b");
				CollectionRequirementState observed = CollectionRequirementState.Present("recipe-v1", "recipe-c");
				var coordinator = new CollectionPinOverrideCoordinator(fixture.Associations, fixture.Target);
				CollectionOverrideDecisionResult initial = coordinator.RecordDecision(requirement, baseline, chosen, observed,
					"Keep local recipe");

				CollectionOverrideDecisionResult accepted = coordinator.AcceptCurrentDrift(association.AssociationId,
					initial.Drift.ObservationId, "Adopt the observed recipe");

				Assert.IsTrue(accepted.HasOverride);
				Assert.IsFalse(accepted.HasDrift);
				Assert.AreEqual(initial.UserOverride.OverrideId, accepted.UserOverride.OverrideId);
				Assert.AreEqual(baseline, accepted.UserOverride.BaselineState);
				Assert.AreEqual(observed, accepted.UserOverride.UserChosenState);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcceptCurrentDrift_RejectsStaleObservationIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation association = fixture.AddAssociation("collection-a", "revision-a", "member-a",
					"native-a", "recipe-a");
				var driftCoordinator = new CollectionManualMutationDriftCoordinator(fixture.Associations, fixture.Target);
				CollectionManualMutationCapture firstCapture = driftCoordinator.BeginNativeModMutation(
					CollectionManualMutationKind.VirtualDisable, "native-a", null);
				driftCoordinator.RecordCommittedNativeMutation(firstCapture, null);
				Guid staleId = fixture.Associations.GetDriftObservations(association.AssociationId).Single().ObservationId;
				CollectionManualMutationCapture secondCapture = driftCoordinator.BeginNativeModMutation(
					CollectionManualMutationKind.VirtualDisable, "native-a", null);
				driftCoordinator.RecordCommittedNativeMutation(secondCapture, null);
				Assert.AreNotEqual(staleId, fixture.Associations.GetDriftObservations(association.AssociationId).Single().ObservationId);
				var coordinator = new CollectionPinOverrideCoordinator(fixture.Associations, fixture.Target);

				Assert.Throws<InvalidOperationException>(() => coordinator.AcceptCurrentDrift(
					association.AssociationId, staleId, "Stale choice"));
				Assert.AreEqual(0, fixture.Associations.GetOverrides(association.AssociationId).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RecordDecision_RejectsRecoveringAssociationInsteadOfMaskingRecovery()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionTargetAssociation association = fixture.AddAssociation("collection-a", "revision-a", "member-a",
					"native-a", "recipe-a", CollectionAssociationState.Recovering);
				var requirement = new CollectionRequirementReference(association, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.MemberEnabledState, null);
				CollectionRequirementState enabled = CollectionRequirementState.Present("bool-v1", "enabled");
				CollectionRequirementState disabled = CollectionRequirementState.Present("bool-v1", "disabled");
				var coordinator = new CollectionPinOverrideCoordinator(fixture.Associations, fixture.Target);

				Assert.Throws<InvalidOperationException>(() => coordinator.RecordDecision(requirement, enabled, disabled,
					disabled, "Do not hide recovery"));
				Assert.AreEqual(0, fixture.Associations.GetOverrides(association.AssociationId).Count);
				Assert.AreEqual(CollectionAssociationState.Recovering,
					fixture.Associations.GetAssociation(association.AssociationId).State);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static Fixture CreateFixture(string root)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			return new Fixture(store, CollectionTargetIdentity.FromFingerprint("target-c612"));
		}

		private static string CreateTemporaryDirectory()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-collections-c612-tests-" + Guid.NewGuid().ToString("N"));
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

			public CollectionTargetAssociation AddAssociation(string collectionId, string revisionId, string memberKey,
				string nativeModKey, string recipeFingerprint,
				CollectionAssociationState state = CollectionAssociationState.Applied)
			{
				CollectionIdentity collection = CollectionIdentity.FromNexus(collectionId);
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, revisionId, 1);
				new CollectionsCatalogStore(_store).SaveDefinitionAndRevision(
					new CollectionDefinition(collection, collectionId, null, null),
					new CollectionRevision(revision, revisionId, null, 1));
				var association = new CollectionTargetAssociation(Guid.NewGuid(), revision, Target, state);
				Associations.SaveAssociation(association);
				Associations.SaveBinding(new CollectionMemberBinding(association, CollectionMemberKey.FromProvider(memberKey),
					new NativeModInstanceIdentity(Target, nativeModKey), CollectionRecipeIdentity.FromFingerprint(recipeFingerprint),
					CollectionMemberBindingKind.InstalledForCollection));
				return association;
			}
		}
	}
}
