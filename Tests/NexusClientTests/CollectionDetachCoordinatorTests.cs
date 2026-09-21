using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.13 safe detach/provenance coverage.</summary>
	public class CollectionDetachCoordinatorTests
	{
		[Test]
		public void Detach_PreservesBoundNativeModsAsStandaloneAndRemovesOnlyAssociationTracking()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore store = CreateFeatureStore(root);
				CollectionTargetAssociation association = SeedAssociation(store, "detach-a", "rev-1", 1, "target-a");
				var associations = new CollectionsAssociationStore(store);
				var nativeOne = new NativeModInstanceIdentity(association.Target, "native-1");
				var nativeTwo = new NativeModInstanceIdentity(association.Target, "native-2");
				associations.SaveBinding(new CollectionMemberBinding(association, CollectionMemberKey.FromProvider("member-1"),
					nativeOne, CollectionRecipeIdentity.FromFingerprint("recipe-1"), CollectionMemberBindingKind.InstalledForCollection));
				associations.SaveBinding(new CollectionMemberBinding(association, CollectionMemberKey.FromProvider("member-2"),
					nativeTwo, CollectionRecipeIdentity.FromFingerprint("recipe-2"), CollectionMemberBindingKind.AdoptedExisting));
				associations.SaveNativeModProvenance(new NativeModProvenance(nativeOne, StandaloneModUse.NoStandaloneUseVerified));

				var requirement = new CollectionRequirementReference(association, CollectionMemberKey.FromProvider("member-1"),
					CollectionRequirementAspect.MemberEnabledState, null);
				new CollectionPinOverrideCoordinator(associations, association.Target).RecordDecision(requirement,
					CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Absent(),
					CollectionRequirementState.Present("bool-v1", "enabled"), "Keep disabled");

				var coordinator = new CollectionDetachCoordinator(associations, association.Target);
				CollectionDetachResult result = coordinator.Detach(association.AssociationId);

				Assert.AreEqual(CollectionOperationKind.DetachTracking, result.Operation.Kind);
				Assert.IsTrue(result.Operation.IsSuccessful);
				Assert.AreEqual(association.AssociationId, result.DetachedAssociation.AssociationId);
				Assert.AreEqual(2, result.DetachedBindings.Count);
				Assert.AreEqual(2, result.StandaloneProvenance.Count);
				Assert.IsNull(associations.GetAssociation(association.AssociationId));
				Assert.AreEqual(0, associations.GetBindings(association.AssociationId).Count);
				Assert.AreEqual(0, associations.GetOverrides(association.AssociationId).Count);
				Assert.AreEqual(0, associations.GetDriftObservations(association.AssociationId).Count);
				Assert.AreEqual(StandaloneModUse.ExplicitStandaloneUse, associations.GetNativeModProvenance(nativeOne).StandaloneUse);
				Assert.AreEqual(StandaloneModUse.ExplicitStandaloneUse, associations.GetNativeModProvenance(nativeTwo).StandaloneUse);
				Assert.IsNotNull(new CollectionsCatalogStore(store).GetRevision(association.Revision),
					"Detach must not delete the saved Collection revision.");
				CollectionOperation persistedOperation = new CollectionsOperationStore(store).GetOperation(result.Operation.Identity);
				Assert.IsNotNull(persistedOperation);
				Assert.AreEqual(CollectionOperationKind.DetachTracking, persistedOperation.Kind);
				Assert.IsTrue(persistedOperation.IsSuccessful);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Detach_SharedNativeModPreservesOtherAssociationAndStillEstablishesStandaloneUse()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore store = CreateFeatureStore(root);
				CollectionTargetAssociation first = SeedAssociation(store, "detach-shared-a", "rev-1", 1, "target-a");
				CollectionTargetAssociation second = SeedAssociation(store, "detach-shared-b", "rev-1", 1, "target-a");
				var associations = new CollectionsAssociationStore(store);
				var shared = new NativeModInstanceIdentity(first.Target, "shared-native");
				associations.SaveBinding(new CollectionMemberBinding(first, CollectionMemberKey.FromProvider("member-a"), shared,
					CollectionRecipeIdentity.FromFingerprint("recipe-shared"), CollectionMemberBindingKind.InstalledForCollection));
				associations.SaveBinding(new CollectionMemberBinding(second, CollectionMemberKey.FromProvider("member-b"), shared,
					CollectionRecipeIdentity.FromFingerprint("recipe-shared"), CollectionMemberBindingKind.AdoptedExisting));

				new CollectionDetachCoordinator(associations, first.Target).Detach(first.AssociationId);

				Assert.IsNull(associations.GetAssociation(first.AssociationId));
				Assert.IsNotNull(associations.GetAssociation(second.AssociationId));
				Assert.AreEqual(1, associations.GetBindings(second.AssociationId).Count);
				Assert.AreEqual(StandaloneModUse.ExplicitStandaloneUse, associations.GetNativeModProvenance(shared).StandaloneUse,
					"A later uninstall of the surviving Collection must not erase effects the user deliberately kept by detaching the first association.");
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Detach_RejectsRecoveringAssociationWithoutChangingTrackingOrProvenance()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore store = CreateFeatureStore(root);
				CollectionTargetAssociation association = SeedAssociation(store, "detach-recovering", "rev-1", 1, "target-a")
					.WithState(CollectionAssociationState.Recovering);
				var associations = new CollectionsAssociationStore(store);
				associations.SaveAssociation(association);
				var nativeMod = new NativeModInstanceIdentity(association.Target, "native-1");
				associations.SaveBinding(new CollectionMemberBinding(association, CollectionMemberKey.FromProvider("member-1"), nativeMod,
					CollectionRecipeIdentity.FromFingerprint("recipe-1"), CollectionMemberBindingKind.InstalledForCollection));

				var coordinator = new CollectionDetachCoordinator(associations, association.Target);
				Assert.Throws<InvalidOperationException>(() => coordinator.Detach(association.AssociationId));

				Assert.IsNotNull(associations.GetAssociation(association.AssociationId));
				Assert.AreEqual(1, associations.GetBindings(association.AssociationId).Count);
				Assert.AreEqual(StandaloneModUse.Unknown, associations.GetNativeModProvenance(nativeMod).StandaloneUse);
				Assert.AreEqual(0, new CollectionsOperationStore(store).GetIncompleteOperations(association.Target).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Detach_RejectsIncompleteOperationForSameCollectionAndTarget()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore store = CreateFeatureStore(root);
				CollectionTargetAssociation association = SeedAssociation(store, "detach-inflight", "rev-1", 1, "target-a");
				var associations = new CollectionsAssociationStore(store);
				var nativeMod = new NativeModInstanceIdentity(association.Target, "native-1");
				associations.SaveBinding(new CollectionMemberBinding(association, CollectionMemberKey.FromProvider("member-1"), nativeMod,
					CollectionRecipeIdentity.FromFingerprint("recipe-1"), CollectionMemberBindingKind.InstalledForCollection));

				var inFlight = new CollectionOperation(CollectionOperationIdentity.CreateNew(),
					CollectionOperationKind.ApplyResolvedPlan, association.Revision.Collection, association.Target, association.Revision,
					null, 1, CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending,
					new CollectionNativeChildOperation[0]);
				new CollectionsOperationStore(store).SaveOperation(inFlight);

				var coordinator = new CollectionDetachCoordinator(associations, association.Target);
				Assert.Throws<InvalidOperationException>(() => coordinator.Detach(association.AssociationId));

				Assert.IsNotNull(associations.GetAssociation(association.AssociationId));
				Assert.AreEqual(StandaloneModUse.Unknown, associations.GetNativeModProvenance(nativeMod).StandaloneUse);
				Assert.AreEqual(inFlight.Identity.OperationId,
					new CollectionsOperationStore(store).GetIncompleteOperations(association.Target).Single().Identity.OperationId);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Detach_FeatureStoreFailureRollsBackStandaloneProvenanceAndAssociationRemoval()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore store = CreateFeatureStore(root);
				CollectionTargetAssociation association = SeedAssociation(store, "detach-atomic", "rev-1", 1, "target-a");
				var associations = new CollectionsAssociationStore(store);
				var nativeMod = new NativeModInstanceIdentity(association.Target, "native-1");
				associations.SaveBinding(new CollectionMemberBinding(association, CollectionMemberKey.FromProvider("member-1"), nativeMod,
					CollectionRecipeIdentity.FromFingerprint("recipe-1"), CollectionMemberBindingKind.InstalledForCollection));

				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.CommandText = @"
CREATE TRIGGER fail_detach_journal
BEFORE INSERT ON collection_operations
WHEN NEW.kind = 7
BEGIN
	SELECT RAISE(ABORT, 'forced detach journal failure');
END;";
					command.ExecuteNonQuery();
				}

				var coordinator = new CollectionDetachCoordinator(associations, association.Target);
				Assert.Throws<SQLiteException>(() => coordinator.Detach(association.AssociationId));

				Assert.IsNotNull(associations.GetAssociation(association.AssociationId));
				Assert.AreEqual(1, associations.GetBindings(association.AssociationId).Count);
				Assert.AreEqual(StandaloneModUse.Unknown, associations.GetNativeModProvenance(nativeMod).StandaloneUse,
					"Standalone provenance and association removal must roll back together if the detach journal cannot commit.");
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ProvenanceStore_UnknownIsAbsenceAndExplicitStandaloneRoundTrips()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore store = CreateFeatureStore(root);
				var associations = new CollectionsAssociationStore(store);
				var nativeMod = new NativeModInstanceIdentity(CollectionTargetIdentity.FromFingerprint("target-a"), "native-1");

				Assert.AreEqual(StandaloneModUse.Unknown, associations.GetNativeModProvenance(nativeMod).StandaloneUse);
				Assert.Throws<ArgumentOutOfRangeException>(() => associations.SaveNativeModProvenance(
					new NativeModProvenance(nativeMod, StandaloneModUse.Unknown)));

				associations.SaveNativeModProvenance(new NativeModProvenance(nativeMod, StandaloneModUse.ExplicitStandaloneUse));
				Assert.AreEqual(StandaloneModUse.ExplicitStandaloneUse, associations.GetNativeModProvenance(nativeMod).StandaloneUse);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionsStore CreateFeatureStore(string root)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			return store;
		}

		private static CollectionTargetAssociation SeedAssociation(CollectionsStore store, string collectionId,
			string revisionId, long revisionNumber, string targetFingerprint)
		{
			var catalog = new CollectionsCatalogStore(store);
			CollectionIdentity collection = CollectionIdentity.FromNexus(collectionId);
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, revisionId, revisionNumber);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, collectionId, null, null),
				new CollectionRevision(revision, "Revision " + revisionNumber, null, null));
			var association = new CollectionTargetAssociation(Guid.NewGuid(), revision,
				CollectionTargetIdentity.FromFingerprint(targetFingerprint), CollectionAssociationState.Applied);
			new CollectionsAssociationStore(store).SaveAssociation(association);
			return association;
		}

		private static SQLiteConnection OpenDatabase(string databasePath)
		{
			var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
			{
				DataSource = databasePath,
				ForeignKeys = true,
				Pooling = false,
				FailIfMissing = true
			}.ConnectionString);
			connection.Open();
			return connection;
		}

		private static string CreateTemporaryDirectory()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-collections-detach-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			return directory;
		}
	}
}
