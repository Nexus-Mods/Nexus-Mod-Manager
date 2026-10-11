using System;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>Yes to all / No to all persist exact per-file owner decisions atomically.</summary>
    [TestFixture]
    public class CollectionConflictResolutionBulkStoreTests
    {
        [Test]
        public void SaveMany_PersistsBothKindsForExactFilesAndReplacesOnlyChangedChoice()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                CollectionRevisionIdentity revision = CreateRevision();
                var featureStore = new CollectionsStore(root);
                Seed(featureStore, revision);
                var store = new CollectionsConflictResolutionStore(featureStore);
                CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-bulk-review");
                CollectionMemberKey member = CollectionMemberKey.FromLocal(Guid.NewGuid());
                CollectionConflictResolutionDecision first = Decision(revision, target, member,
                    "textures\\body.dds", "current-mod-A", CollectionConflictResolutionDecisionKind.IncomingCollectionWinsFile);
                CollectionConflictResolutionDecision second = Decision(revision, target, member,
                    "meshes\\body.nif", "current-mod-B", CollectionConflictResolutionDecisionKind.KeepExistingManagedFileWinner);

                store.SaveMany(new[] { first, second });
                var restarted = new CollectionsConflictResolutionStore(new CollectionsStore(root));
                var persisted = restarted.GetForRevisionTarget(revision, target);
                Assert.AreEqual(2, persisted.Count);
                Assert.IsTrue(persisted.Any(x => x.DecisionId == first.DecisionId &&
                    x.Kind == CollectionConflictResolutionDecisionKind.IncomingCollectionWinsFile));
                Assert.IsTrue(persisted.Any(x => x.DecisionId == second.DecisionId &&
                    x.Kind == CollectionConflictResolutionDecisionKind.KeepExistingManagedFileWinner));

                CollectionConflictResolutionDecision changed = Decision(revision, target, member,
                    "textures\\body.dds", "current-mod-A", CollectionConflictResolutionDecisionKind.KeepExistingManagedFileWinner);
                restarted.SaveMany(new[] { changed });
                persisted = store.GetForRevisionTarget(revision, target);
                Assert.AreEqual(2, persisted.Count);
                Assert.IsTrue(persisted.Any(x => x.DecisionId == changed.DecisionId));
                Assert.IsFalse(persisted.Any(x => x.DecisionId == first.DecisionId));
                Assert.IsTrue(persisted.Any(x => x.DecisionId == second.DecisionId));
                Assert.AreEqual(0, store.GetForRevisionTarget(revision,
                    CollectionTargetIdentity.FromFingerprint("different-target")).Count);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void SaveMany_DuplicateIdFailure_RollsBackEveryChange()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                CollectionRevisionIdentity revision = CreateRevision();
                var featureStore = new CollectionsStore(root);
                Seed(featureStore, revision);
                var store = new CollectionsConflictResolutionStore(featureStore);
                CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-rollback");
                CollectionMemberKey member = CollectionMemberKey.FromLocal(Guid.NewGuid());
                CollectionConflictResolutionDecision untouched = Decision(revision, target, member,
                    "textures\\existing.dds", "current-mod", CollectionConflictResolutionDecisionKind.IncomingCollectionWinsFile);
                store.Save(untouched);

                Guid duplicateId = Guid.NewGuid();
                CollectionConflictResolutionDecision first = Decision(revision, target, member,
                    "textures\\first.dds", "current-mod", CollectionConflictResolutionDecisionKind.KeepExistingManagedFileWinner, duplicateId);
                CollectionConflictResolutionDecision second = Decision(revision, target, member,
                    "textures\\second.dds", "current-mod", CollectionConflictResolutionDecisionKind.KeepExistingManagedFileWinner, duplicateId);
                Assert.That(() => store.SaveMany(new[] { first, second }), Throws.Exception);

                var persisted = new CollectionsConflictResolutionStore(new CollectionsStore(root)).GetForRevisionTarget(revision, target);
                Assert.AreEqual(1, persisted.Count, "Bulk failure must not leave partial decisions.");
                Assert.AreEqual(untouched.DecisionId, persisted.Single().DecisionId);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void SaveMany_OwnerChanges_DoNotReusePriorOwnerChoice()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                CollectionRevisionIdentity revision = CreateRevision();
                var featureStore = new CollectionsStore(root);
                Seed(featureStore, revision);
                var store = new CollectionsConflictResolutionStore(featureStore);
                CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-owner-change");
                CollectionMemberKey member = CollectionMemberKey.FromLocal(Guid.NewGuid());
                store.SaveMany(new[]
                {
                    Decision(revision, target, member, "textures\\shared.dds", "old-owner", CollectionConflictResolutionDecisionKind.IncomingCollectionWinsFile),
                    Decision(revision, target, member, "textures\\shared.dds", "new-owner", CollectionConflictResolutionDecisionKind.KeepExistingManagedFileWinner)
                });
                var persisted = store.GetForRevisionTarget(revision, target);
                Assert.AreEqual(2, persisted.Count, "The owner key remains part of every exact authorization.");
                Assert.IsTrue(persisted.Any(x => x.ExistingOwnerKey == "old-owner"));
                Assert.IsTrue(persisted.Any(x => x.ExistingOwnerKey == "new-owner"));
            }
            finally { Directory.Delete(root, true); }
        }

        private static CollectionRevisionIdentity CreateRevision()
        {
            CollectionIdentity collection = CollectionIdentity.FromLocal(Guid.NewGuid());
            return CollectionRevisionIdentity.FromLocal(collection, Guid.NewGuid());
        }

        private static void Seed(CollectionsStore store, CollectionRevisionIdentity revision)
        {
            store.CreateNew();
            var catalog = new CollectionsCatalogStore(store);
            catalog.SaveDefinitionAndRevision(new CollectionDefinition(revision.Collection, "Bulk test", null, null),
                new CollectionRevision(revision, "Revision", null, 0));
        }

        private static CollectionConflictResolutionDecision Decision(CollectionRevisionIdentity revision,
            CollectionTargetIdentity target, CollectionMemberKey member, string path, string owner,
            CollectionConflictResolutionDecisionKind kind, Guid? id = null)
        {
            return new CollectionConflictResolutionDecision(id ?? Guid.NewGuid(), revision, target,
                member, ModDeploymentRoot.Data, path, owner, kind, "Bulk review test");
        }

        private static string CreateTemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "NMM-Conflict-Bulk-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
