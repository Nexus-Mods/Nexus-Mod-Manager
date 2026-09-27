using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C7.11 profile-preservation and outgoing-association boundary coverage.</summary>
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
