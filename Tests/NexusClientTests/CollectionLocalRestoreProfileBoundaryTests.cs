using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C7.11 profile-preservation and outgoing-association boundary coverage.</summary>
	[Category("CollectionsGateL")]
	[Category("CollectionsC12Workflow")]
	public class CollectionLocalRestoreProfileBoundaryTests
	{
		[Test]
		public void OutgoingAssociations_TransitionRecoveringThenIncompleteWithoutIdentityRebinding()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				var associations = new CollectionsAssociationStore(store);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("profile-boundary-target");
				CollectionTargetAssociation applied = SeedAssociation(catalog, associations, target, "profile-boundary-a", 1, CollectionAssociationState.Applied);
				CollectionTargetAssociation modified = SeedAssociation(catalog, associations, target, "profile-boundary-b", 2, CollectionAssociationState.Modified);
				var expected = new Dictionary<Guid, CollectionAssociationState>
				{
					{ applied.AssociationId, CollectionAssociationState.Applied },
					{ modified.AssociationId, CollectionAssociationState.Modified }
				};

				associations.MarkLocalRestoreOutgoingAssociationsRecovering(target, expected);
				Assert.AreEqual(CollectionAssociationState.Recovering, associations.GetAssociation(applied.AssociationId).State);
				Assert.AreEqual(CollectionAssociationState.Recovering, associations.GetAssociation(modified.AssociationId).State);

				associations.FinalizeLocalRestoreOutgoingAssociations(target, expected.Keys);
				CollectionTargetAssociation appliedFinal = associations.GetAssociation(applied.AssociationId);
				CollectionTargetAssociation modifiedFinal = associations.GetAssociation(modified.AssociationId);
				Assert.AreEqual(CollectionAssociationState.Incomplete, appliedFinal.State);
				Assert.AreEqual(CollectionAssociationState.Incomplete, modifiedFinal.State);
				Assert.AreEqual(applied.Revision, appliedFinal.Revision);
				Assert.AreEqual(modified.Revision, modifiedFinal.Revision);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Retires partial restore and quarantined associations atomically while preserving operation-owned recovery inputs.</summary>
		[Test]
		public void StoppedRestore_ReleasesQuarantineWithoutReportingSuccessOrDroppingRecoveryInputs()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				var associations = new CollectionsAssociationStore(store);
				var operations = new CollectionsOperationStore(store);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("local-restore-stop-target");
				CollectionTargetAssociation outgoing = SeedAssociation(catalog, associations, target, "outgoing", 1, CollectionAssociationState.Recovering);
				CollectionIdentity local = CollectionIdentity.FromLocal(Guid.NewGuid());
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(local, Guid.NewGuid());
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(local, "Saved source", null, null),
					new CollectionRevision(revision, "Captured setup", null, null));
				var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.RestoreLocalCapture,
					local, target, revision, null, 1, CollectionOperationPhase.PausedAtSafeBoundary,
					CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
				operations.SaveOperation(operation);
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				CollectionsRetainedArtifact artifact;
				using (var input = new MemoryStream(new byte[] { 1, 2, 3 })) artifact = artifacts.Publish(input);
				references.AcquireReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
					operation.Identity.OperationId.ToString("D"), "local-restore-plan-v1");
				var stale = new CollectionOperation(operation.Identity, operation.Kind, local, target, revision, null, 3,
					CollectionOperationPhase.Completed, CollectionOperationResultState.StoppedPartial, operation.NativeChildren);
				Assert.Throws<InvalidOperationException>(() => associations.SaveLocalRestoreStoppedPartial(stale, new[] { outgoing.AssociationId }));
				Assert.AreEqual(CollectionAssociationState.Recovering, associations.GetAssociation(outgoing.AssociationId).State);
				Assert.IsFalse(operations.GetOperation(operation.Identity).IsTerminal);
				var stopped = new CollectionOperation(operation.Identity, operation.Kind, local, target, revision, null, 2,
					CollectionOperationPhase.Completed, CollectionOperationResultState.StoppedPartial, operation.NativeChildren);
				associations.SaveLocalRestoreStoppedPartial(stopped, new[] { outgoing.AssociationId });
				Assert.AreEqual(CollectionAssociationState.Incomplete, associations.GetAssociation(outgoing.AssociationId).State);
				Assert.IsTrue(operations.GetOperation(operation.Identity).IsTerminal);
				Assert.IsFalse(operations.GetOperation(operation.Identity).IsSuccessful);
				Assert.AreEqual(0, operations.GetIncompleteOperations(target).Count);
				Assert.AreEqual(1, references.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Operation,
					operation.Identity.OperationId.ToString("D")).Count);
				Assert.IsTrue(artifacts.VerifyArtifact(artifact.ArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Commits restored bindings and participation together while retaining other drift, local decisions and conservative provenance.</summary>
		[TestCase(false, false, false)]
		[TestCase(true, false, false)]
		[TestCase(true, false, true)]
		[TestCase(true, true, false)]
		public void VerifiedRestore_ReconcilesRestoredParticipationAtCommit(bool recreated, bool localOmission, bool standalone)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				var associations = new CollectionsAssociationStore(store);
				var operations = new CollectionsOperationStore(store);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("verified-local-restore-target");
				CollectionTargetAssociation outgoing = SeedAssociation(catalog, associations, target, "outgoing", 1, CollectionAssociationState.Incomplete);
				var binding = new CollectionMemberBinding(outgoing, CollectionMemberKey.FromValidatedMatch("exact-artifact"),
					new NativeModInstanceIdentity(target, "captured-native"), CollectionRecipeIdentity.FromFingerprint("verified-recipe"), CollectionMemberBindingKind.AdoptedExisting);
				associations.SaveBinding(binding);
				if (standalone) associations.SaveNativeModProvenance(new NativeModProvenance(binding.NativeMod, StandaloneModUse.ExplicitStandaloneUse));
				var participation = new CollectionRequirementReference(outgoing, binding.MemberKey, CollectionRequirementAspect.MemberParticipation, null);
				CollectionRequirementState included = CollectionMemberRequirementStates.Included();
				UserOverride omission = localOmission ? new UserOverride(Guid.NewGuid(), participation, included, CollectionRequirementState.Absent(), "Keep omitted") : null;
				if (omission != null)
					associations.SaveUserOverrideDecision(participation, included, omission, null, Guid.Empty, Guid.Empty);
				else
					associations.SaveManualMutationDrift(new CollectionTargetAssociation[0], new[]
					{
						new CollectionDriftObservation(Guid.NewGuid(), participation, included, CollectionRequirementState.Absent(), "Removed before restore")
					}, new CollectionRequirementReference[0]);
				var enabled = new CollectionDriftObservation(Guid.NewGuid(), new CollectionRequirementReference(outgoing, binding.MemberKey,
					CollectionRequirementAspect.MemberEnabledState, null), CollectionMemberRequirementStates.Enabled(true), CollectionMemberRequirementStates.Enabled(false), "Other drift");
				var otherMissing = new CollectionDriftObservation(Guid.NewGuid(), new CollectionRequirementReference(outgoing,
					CollectionMemberKey.FromValidatedMatch("uncaptured-member"), CollectionRequirementAspect.MemberParticipation, null), included, CollectionRequirementState.Absent(), "Unverified member");
				associations.SaveManualMutationDrift(new CollectionTargetAssociation[0], new[] { enabled, otherMissing }, new CollectionRequirementReference[0]);
				CollectionOperation pending = SeedFinalRestore(catalog, operations, target);
				CollectionsAssociationTargetSnapshot snapshot = associations.GetTargetSnapshot(target);
				string restoredNativeKey = recreated ? "restored-native" : "captured-native";
				IReadOnlyList<CollectionMemberBinding> verified = CreateVerifiedBindings(snapshot, binding, outgoing.Revision, binding.VerifiedRecipe, restoredNativeKey);
				Assert.AreEqual(1, verified.Count);
				CollectionOperation completed = CompleteRestore(pending, 1);
				associations.SaveVerifiedLocalRestore(completed, snapshot, verified);

				CollectionMemberBinding restored = associations.GetBindings(outgoing.AssociationId).Single();
				Assert.AreEqual(restoredNativeKey, restored.NativeMod.NativeModKey);
				Assert.AreEqual(binding.VerifiedRecipe, restored.VerifiedRecipe);
				Assert.AreEqual(binding.BindingKind, restored.BindingKind);
				Assert.AreEqual(CollectionAssociationState.Incomplete, associations.GetAssociation(outgoing.AssociationId).State);
				Assert.IsTrue(operations.GetOperation(pending.Identity).IsSuccessful);
				CollectionsAssociationTargetSnapshot final = associations.GetTargetSnapshot(target);
				Assert.IsTrue(final.DriftObservations.Any(x => x.ObservationId == enabled.ObservationId));
				Assert.IsTrue(final.DriftObservations.Any(x => x.ObservationId == otherMissing.ObservationId));
				Assert.AreEqual(localOmission ? 3 : 2, final.DriftObservations.Count);
				if (localOmission)
				{
					Assert.AreEqual(omission.OverrideId, final.Overrides.Single().OverrideId);
					CollectionDriftObservation observed = final.DriftObservations.Single(x => x.Requirement.Equals(participation));
					Assert.AreEqual(CollectionRequirementState.Absent(), observed.ExpectedState);
					Assert.AreEqual(included, observed.ObservedState);
				}
				Assert.AreEqual(standalone ? StandaloneModUse.ExplicitStandaloneUse : StandaloneModUse.Unknown,
					associations.GetNativeModProvenance(restored.NativeMod).StandaloneUse);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Rejects stale publication atomically and avoids rebinding a different association revision or verified recipe.</summary>
		[Test]
		public void VerifiedRestore_RejectsStaleJournalAndChangedAssociationBaseline()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				var associations = new CollectionsAssociationStore(store);
				var operations = new CollectionsOperationStore(store);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("stale-local-restore-target");
				CollectionTargetAssociation outgoing = SeedAssociation(catalog, associations, target, "outgoing", 1, CollectionAssociationState.Incomplete);
				var binding = new CollectionMemberBinding(outgoing, CollectionMemberKey.FromValidatedMatch("exact-artifact"),
					new NativeModInstanceIdentity(target, "captured-native"), CollectionRecipeIdentity.FromFingerprint("verified-recipe"), CollectionMemberBindingKind.AdoptedExisting);
				associations.SaveBinding(binding);
				CollectionOperation pending = SeedFinalRestore(catalog, operations, target);
				CollectionsAssociationTargetSnapshot snapshot = associations.GetTargetSnapshot(target);
				IReadOnlyList<CollectionMemberBinding> verified = CreateVerifiedBindings(snapshot, binding, outgoing.Revision, binding.VerifiedRecipe, "restored-native");
				Assert.Throws<InvalidOperationException>(() => associations.SaveVerifiedLocalRestore(CompleteRestore(pending, 2), snapshot, verified));
				Assert.IsFalse(operations.GetOperation(pending.Identity).IsTerminal);
				Assert.AreEqual("captured-native", associations.GetBindings(outgoing.AssociationId).Single().NativeMod.NativeModKey);
				Assert.AreEqual(0, CreateVerifiedBindings(snapshot, binding,
					CollectionRevisionIdentity.FromNexus(outgoing.Revision.Collection, "other-revision", 2), binding.VerifiedRecipe, "restored-native").Count);
				Assert.AreEqual(0, CreateVerifiedBindings(snapshot, binding, outgoing.Revision, CollectionRecipeIdentity.FromFingerprint("other-recipe"), "restored-native").Count);
				associations.SaveBinding(new CollectionMemberBinding(outgoing, binding.MemberKey, new NativeModInstanceIdentity(target, "changed-native"),
					binding.VerifiedRecipe, binding.BindingKind));
				Assert.Throws<InvalidOperationException>(() => associations.SaveVerifiedLocalRestore(CompleteRestore(pending, 1), snapshot, verified));
				Assert.IsFalse(operations.GetOperation(pending.Identity).IsTerminal);
				Assert.AreEqual("changed-native", associations.GetBindings(outgoing.AssociationId).Single().NativeMod.NativeModKey);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Creates a captured member with trusted association provenance and the independently verified final native remap.</summary>
		private static IReadOnlyList<CollectionMemberBinding> CreateVerifiedBindings(CollectionsAssociationTargetSnapshot snapshot,
			CollectionMemberBinding binding, CollectionRevisionIdentity capturedRevision, CollectionRecipeIdentity capturedRecipe, string restoredNativeKey)
		{
			var context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data);
			var captured = new CollectionInstalledModIdentity("captured-native", new CollectionInstalledArchiveReference("", "", false, null),
				"", "", "1.0", "", false, context, new[]
				{
					new CollectionInstalledMemberProvenance(binding.Association.AssociationId, capturedRevision, CollectionAssociationState.Applied,
						binding.MemberKey, capturedRecipe, binding.BindingKind)
				});
			var identities = new CollectionInstalledIdentitySnapshot(snapshot.Target, 1, new[] { captured }, NativeStateCaptureCoverage.Complete,
				new CollectionInstalledIdentityIssue[0]);
			var plan = new CollectionLocalRestorePlan(LocalCaptureIdentity.From(Guid.NewGuid()), snapshot.Target,
				new CollectionCurrentStateFingerprint("state-v1", "verified"), 1, "original", "verified-plan", new[]
				{
					new CollectionLocalRestoreMemberPlan(CollectionMemberKey.FromLocal(Guid.NewGuid()), "captured-native",
						CollectionLocalRestoreMemberAction.ReuseExistingNative, restoredNativeKey, context, null)
				}, new CollectionLocalRestoreDeploymentPlan[0], new string[0], new CollectionLocalRestorePlanIssue[0]);
			return CollectionLocalRestoreFinalStateVerifier.CreateVerifiedAssociationBindings(identities, plan, snapshot);
		}

		/// <summary>Persists a Local restore at its final verified safe boundary.</summary>
		private static CollectionOperation SeedFinalRestore(CollectionsCatalogStore catalog, CollectionsOperationStore operations, CollectionTargetIdentity target)
		{
			CollectionIdentity local = CollectionIdentity.FromLocal(Guid.NewGuid());
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(local, Guid.NewGuid());
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(local, "Saved source", null, null), new CollectionRevision(revision, "Captured setup", null, null));
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.RestoreLocalCapture,
				local, target, revision, null, 13, CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
			operations.SaveOperation(operation);
			return operation;
		}

		/// <summary>Creates the terminal success checkpoint submitted for atomic publication.</summary>
		private static CollectionOperation CompleteRestore(CollectionOperation pending, int advance)
		{
			return new CollectionOperation(pending.Identity, pending.Kind, pending.Collection, pending.Target, pending.Revision, pending.PlanIdentity,
				pending.CheckpointSequence + advance, CollectionOperationPhase.Completed, CollectionOperationResultState.Committed, pending.NativeChildren);
		}

		[Test]
		public void OutgoingAssociations_RejectStateChangedAfterReview()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				var associations = new CollectionsAssociationStore(store);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("profile-boundary-stale-target");
				CollectionTargetAssociation association = SeedAssociation(catalog, associations, target, "profile-boundary-stale", 1, CollectionAssociationState.Applied);
				associations.SaveAssociation(association.WithState(CollectionAssociationState.Modified));
				var expected = new Dictionary<Guid, CollectionAssociationState>
				{
					{ association.AssociationId, CollectionAssociationState.Applied }
				};

				Assert.Throws<InvalidOperationException>(() =>
					associations.MarkLocalRestoreOutgoingAssociationsRecovering(target, expected));
				Assert.AreEqual(CollectionAssociationState.Modified, associations.GetAssociation(association.AssociationId).State);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void OutgoingAssociations_RejectAssociationAddedAfterReview()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				var associations = new CollectionsAssociationStore(store);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("profile-boundary-added-target");
				CollectionTargetAssociation reviewed = SeedAssociation(catalog, associations, target, "profile-boundary-reviewed", 1, CollectionAssociationState.Applied);
				var expected = new Dictionary<Guid, CollectionAssociationState>
				{
					{ reviewed.AssociationId, CollectionAssociationState.Applied }
				};
				SeedAssociation(catalog, associations, target, "profile-boundary-added", 2, CollectionAssociationState.Applied);

				Assert.Throws<InvalidOperationException>(() =>
					associations.MarkLocalRestoreOutgoingAssociationsRecovering(target, expected));
				Assert.AreEqual(CollectionAssociationState.Applied, associations.GetAssociation(reviewed.AssociationId).State);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ProfileFingerprint_ChangesWhenPreservedProfileContentChanges()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Directory.CreateDirectory(Path.Combine(root, "Scripted"));
				File.WriteAllText(Path.Combine(root, "mods.txt"), "before");
				File.WriteAllBytes(Path.Combine(root, "Scripted", "selection.bin"), new byte[] { 1, 2, 3 });
				string before = CollectionLocalRestoreProfileBoundaryCoordinator.ComputeProfileContentFingerprint(root);
				string repeated = CollectionLocalRestoreProfileBoundaryCoordinator.ComputeProfileContentFingerprint(root);
				Assert.AreEqual(before, repeated);

				File.WriteAllText(Path.Combine(root, "mods.txt"), "after");
				string after = CollectionLocalRestoreProfileBoundaryCoordinator.ComputeProfileContentFingerprint(root);
				Assert.AreNotEqual(before, after);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionTargetAssociation SeedAssociation(CollectionsCatalogStore catalog, CollectionsAssociationStore associations,
			CollectionTargetIdentity target, string collectionSlug, long revisionNumber, CollectionAssociationState state)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus(collectionSlug);
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "rev-" + revisionNumber, revisionNumber);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, collectionSlug, null, null),
				new CollectionRevision(revision, "Revision " + revisionNumber, null, null));
			var association = new CollectionTargetAssociation(Guid.NewGuid(), revision, target, state);
			associations.SaveAssociation(association);
			return association;
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c711-profile-boundary-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}
	}
}
