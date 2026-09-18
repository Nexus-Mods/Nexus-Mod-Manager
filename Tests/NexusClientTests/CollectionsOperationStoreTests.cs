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
	/// <summary>
	/// C4.8 durable Collection operation journal coverage.
	/// </summary>
	public class CollectionsOperationStoreTests
	{
		[Test]
		public void Operation_RoundTripsIntentAndNativeAttemptIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionRevisionIdentity revision = SeedNexusRevision(featureStore, "operation-roundtrip", "rev-a", 1);
				CollectionOperationIdentity operationId = CollectionOperationIdentity.CreateNew();
				ModOperationIdentity native = CreateNativeIdentity("target-a", ModOperationOrigin.Collection,
					ModInstallMethod.Direct, ModInstallRoot.GameRoot, "recipe-v1");
				CollectionNativeChildOperation child = CreateChild(1, revision, "member-a", native,
					CollectionNativeChildAction.ActivateOrReinstall, CollectionNativeChildCheckpoint.IntentPersisted, null);
				CollectionOperation operation = CreateOperation(operationId, CollectionOperationKind.ApplyResolvedPlan,
					revision.Collection, CollectionTargetIdentity.FromFingerprint("target-a"), revision, 1,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new[] { child });

				var journal = new CollectionsOperationStore(featureStore);
				journal.SaveOperation(operation);
				CollectionOperation loaded = journal.GetOperation(operationId);

				Assert.IsNotNull(loaded);
				Assert.AreEqual(operationId.OperationId, loaded.Identity.OperationId);
				Assert.AreEqual(revision, loaded.Revision);
				Assert.AreEqual(1, loaded.CheckpointSequence);
				Assert.AreEqual(CollectionOperationPhase.Preparing, loaded.Phase);
				Assert.AreEqual(1, loaded.NativeChildren.Count);
				CollectionNativeChildOperation loadedChild = loaded.NativeChildren.Single();
				Assert.AreEqual(native.OperationId, loadedChild.NativeOperation.OperationId);
				Assert.AreEqual(native.AttemptId, loadedChild.NativeOperation.AttemptId);
				Assert.AreEqual(ModInstallMethod.Direct, loadedChild.NativeOperation.Fingerprint.InstallMethod);
				Assert.AreEqual(ModInstallRoot.GameRoot, loadedChild.NativeOperation.Fingerprint.InstallRoot);
				Assert.AreEqual("recipe-v1", loadedChild.NativeOperation.Fingerprint.RecipeFingerprint);
				Assert.AreEqual(CollectionNativeChildCheckpoint.IntentPersisted, loadedChild.Checkpoint);
				Assert.IsNull(loadedChild.NativeResult);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Operation_SameCheckpointIsIdempotentButChangedSnapshotMustAdvance()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionRevisionIdentity revision = SeedNexusRevision(featureStore, "operation-checkpoint", "rev-a", 1);
				var journal = new CollectionsOperationStore(featureStore);
				CollectionOperationIdentity id = CollectionOperationIdentity.CreateNew();
				CollectionOperation first = CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, revision.Collection,
					CollectionTargetIdentity.FromFingerprint("target-a"), revision, 3, CollectionOperationPhase.Preparing,
					CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);

				journal.SaveOperation(first);
				journal.SaveOperation(first);

				CollectionOperation conflict = CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, revision.Collection,
					first.Target, revision, 3, CollectionOperationPhase.ReadyForReview,
					CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(conflict));

				CollectionOperation advanced = CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, revision.Collection,
					first.Target, revision, 4, CollectionOperationPhase.ReadyForReview,
					CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
				journal.SaveOperation(advanced);
				Assert.AreEqual(4, journal.GetOperation(id).CheckpointSequence);
				Assert.AreEqual(CollectionOperationPhase.ReadyForReview, journal.GetOperation(id).Phase);

				CollectionOperation stale = CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, revision.Collection,
					first.Target, revision, 2, CollectionOperationPhase.Preparing,
					CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(stale));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Operation_RevisionMayResolveOnceButCannotBeRemovedOrRebound()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionIdentity collection = CollectionIdentity.FromNexus("operation-revision");
				var catalog = new CollectionsCatalogStore(featureStore);
				catalog.SaveDefinition(new CollectionDefinition(collection, "Operation revision", null, null));
				CollectionRevisionIdentity firstRevision = CollectionRevisionIdentity.FromNexus(collection, "rev-1", 1);
				CollectionRevisionIdentity secondRevision = CollectionRevisionIdentity.FromNexus(collection, "rev-2", 2);
				catalog.SaveRevision(new CollectionRevision(firstRevision, "First", null, null));
				catalog.SaveRevision(new CollectionRevision(secondRevision, "Second", null, null));
				var journal = new CollectionsOperationStore(featureStore);
				CollectionOperationIdentity id = CollectionOperationIdentity.CreateNew();
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-a");

				journal.SaveOperation(CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, collection, target, null, 0,
					CollectionOperationPhase.Created, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]));
				journal.SaveOperation(CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, collection, target, firstRevision, 1,
					CollectionOperationPhase.Resolving, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]));

				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(CreateOperation(id,
					CollectionOperationKind.ApplyResolvedPlan, collection, target, null, 2,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0])));
				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(CreateOperation(id,
					CollectionOperationKind.ApplyResolvedPlan, collection, target, secondRevision, 2,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0])));
				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(CreateOperation(id,
					CollectionOperationKind.DetachTracking, collection, target, firstRevision, 2,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0])));
				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(CreateOperation(id,
					CollectionOperationKind.ApplyResolvedPlan, collection, CollectionTargetIdentity.FromFingerprint("target-b"), firstRevision, 2,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0])));
				Assert.AreEqual(firstRevision, journal.GetOperation(id).Revision);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Child_CheckpointAndDurabilityAdvanceWithoutRebindingNativeIntent()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionRevisionIdentity revision = SeedNexusRevision(featureStore, "child-progress", "rev-a", 1);
				var journal = new CollectionsOperationStore(featureStore);
				CollectionOperationIdentity id = CollectionOperationIdentity.CreateNew();
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-a");
				ModOperationIdentity native = CreateNativeIdentity(target.Fingerprint, ModOperationOrigin.Collection,
					ModInstallMethod.Virtual, ModInstallRoot.Data, "recipe-v1");
				CollectionOperationMemberReference member = new CollectionOperationMemberReference(revision,
					CollectionMemberKey.FromProvider("member-a"));

				journal.SaveOperation(CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, revision.Collection, target, revision, 1,
					CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending,
					new[] { new CollectionNativeChildOperation(1, member, CollectionNativeChildAction.ActivateOrReinstall,
						native, CollectionNativeChildCheckpoint.IntentPersisted, null) }));
				journal.SaveOperation(CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, revision.Collection, target, revision, 2,
					CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending,
					new[] { new CollectionNativeChildOperation(1, member, CollectionNativeChildAction.ActivateOrReinstall,
						native, CollectionNativeChildCheckpoint.NativeSubmitted, null) }));

				var unresolvedResult = new ModOperationResult(native, ModOperationReportedStatus.Failed, ModOperationDurability.Unknown, "diagnostic only");
				journal.SaveOperation(CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, revision.Collection, target, revision, 3,
					CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending,
					new[] { new CollectionNativeChildOperation(1, member, CollectionNativeChildAction.ActivateOrReinstall,
						native, CollectionNativeChildCheckpoint.NativeTerminalObserved, unresolvedResult) }));

				var committedAfterFailure = new ModOperationResult(native, ModOperationReportedStatus.Failed,
					ModOperationDurability.VerifiedCommitted, "not persisted");
				journal.SaveOperation(CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, revision.Collection, target, revision, 4,
					CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending,
					new[] { new CollectionNativeChildOperation(1, member, CollectionNativeChildAction.ActivateOrReinstall,
						native, CollectionNativeChildCheckpoint.Reconciled, committedAfterFailure) }));

				CollectionNativeChildOperation loaded = journal.GetOperation(id).NativeChildren.Single();
				Assert.AreEqual(CollectionNativeChildCheckpoint.Reconciled, loaded.Checkpoint);
				Assert.AreEqual(ModOperationReportedStatus.Failed, loaded.NativeResult.ReportedStatus);
				Assert.AreEqual(ModOperationDurability.VerifiedCommitted, loaded.NativeResult.Durability);
				Assert.IsNull(loaded.NativeResult.Message);

				var rewrittenDurabilityResult = new ModOperationResult(native, ModOperationReportedStatus.Failed,
					ModOperationDurability.VerifiedRolledBack, null);
				CollectionNativeChildOperation rewrittenDurability = new CollectionNativeChildOperation(1, member,
					CollectionNativeChildAction.ActivateOrReinstall, native, CollectionNativeChildCheckpoint.Reconciled, rewrittenDurabilityResult);
				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(CreateOperation(id,
					CollectionOperationKind.ApplyResolvedPlan, revision.Collection, target, revision, 5,
					CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending, new[] { rewrittenDurability })));

				CollectionNativeChildOperation regressed = new CollectionNativeChildOperation(1, member,
					CollectionNativeChildAction.ActivateOrReinstall, native, CollectionNativeChildCheckpoint.NativeSubmitted, null);
				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(CreateOperation(id,
					CollectionOperationKind.ApplyResolvedPlan, revision.Collection, target, revision, 5,
					CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending, new[] { regressed })));

				ModOperationIdentity reboundAttempt = native.CreateNextAttempt();
				var reboundResult = new ModOperationResult(reboundAttempt, ModOperationReportedStatus.Failed,
					ModOperationDurability.VerifiedCommitted, null);
				CollectionNativeChildOperation rebound = new CollectionNativeChildOperation(1, member,
					CollectionNativeChildAction.ActivateOrReinstall, reboundAttempt, CollectionNativeChildCheckpoint.Reconciled, reboundResult);
				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(CreateOperation(id,
					CollectionOperationKind.ApplyResolvedPlan, revision.Collection, target, revision, 5,
					CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending, new[] { rebound })));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Child_ExistingIntentCannotDisappearAndLaterIntentsAppendOnly()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionRevisionIdentity revision = SeedNexusRevision(featureStore, "child-append", "rev-a", 1);
				var journal = new CollectionsOperationStore(featureStore);
				CollectionOperationIdentity id = CollectionOperationIdentity.CreateNew();
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-a");
				CollectionNativeChildOperation first = CreateIntentChild(1, revision, "member-1", target.Fingerprint);
				journal.SaveOperation(CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, revision.Collection, target, revision, 1,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new[] { first }));

				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(CreateOperation(id,
					CollectionOperationKind.ApplyResolvedPlan, revision.Collection, target, revision, 2,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0])));

				CollectionNativeChildOperation third = CreateIntentChild(3, revision, "member-3", target.Fingerprint);
				journal.SaveOperation(CreateOperation(id, CollectionOperationKind.ApplyResolvedPlan, revision.Collection, target, revision, 2,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new[] { first, third }));

				CollectionNativeChildOperation second = CreateIntentChild(2, revision, "member-2", target.Fingerprint);
				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(CreateOperation(id,
					CollectionOperationKind.ApplyResolvedPlan, revision.Collection, target, revision, 3,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new[] { first, second, third })));
				Assert.AreEqual(new[] { 1, 3 }, journal.GetOperation(id).NativeChildren.Select(x => x.Sequence).ToArray());
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Queries_ReturnIncompleteOperationsAndCorrelateExactNativeAttempt()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionRevisionIdentity revision = SeedNexusRevision(featureStore, "operation-query", "rev-a", 1);
				var journal = new CollectionsOperationStore(featureStore);
				CollectionTargetIdentity firstTarget = CollectionTargetIdentity.FromFingerprint("target-a");
				CollectionTargetIdentity secondTarget = CollectionTargetIdentity.FromFingerprint("target-b");
				ModOperationIdentity native = CreateNativeIdentity(firstTarget.Fingerprint, ModOperationOrigin.Collection,
					ModInstallMethod.Virtual, ModInstallRoot.Data, null);
				CollectionNativeChildOperation child = CreateChild(1, revision, "member-a", native,
					CollectionNativeChildAction.Deactivate, CollectionNativeChildCheckpoint.IntentPersisted, null);
				CollectionOperationIdentity pendingId = CollectionOperationIdentity.CreateNew();
				journal.SaveOperation(CreateOperation(pendingId, CollectionOperationKind.UninstallCollectionEffects,
					revision.Collection, firstTarget, revision, 1, CollectionOperationPhase.Preparing,
					CollectionOperationResultState.Pending, new[] { child }));

				CollectionOperationIdentity recoveryId = CollectionOperationIdentity.CreateNew();
				journal.SaveOperation(CreateOperation(recoveryId, CollectionOperationKind.ApplyResolvedPlan,
					revision.Collection, secondTarget, revision, 1, CollectionOperationPhase.RecoveryRequired,
					CollectionOperationResultState.RecoveryRequired, new CollectionNativeChildOperation[0]));

				CollectionOperationIdentity completedId = CollectionOperationIdentity.CreateNew();
				journal.SaveOperation(CreateOperation(completedId, CollectionOperationKind.ApplyResolvedPlan,
					revision.Collection, firstTarget, revision, 1, CollectionOperationPhase.Completed,
					CollectionOperationResultState.FailedBeforeApply, new CollectionNativeChildOperation[0]));

				CollectionAssert.AreEquivalent(new[] { pendingId.OperationId, recoveryId.OperationId },
					journal.GetIncompleteOperations().Select(x => x.Identity.OperationId).ToArray());
				CollectionAssert.AreEqual(new[] { pendingId.OperationId },
					journal.GetIncompleteOperations(firstTarget).Select(x => x.Identity.OperationId).ToArray());
				CollectionOperation correlated = journal.GetOperationForNativeAttempt(native.OperationId, native.AttemptId);
				Assert.IsNotNull(correlated);
				Assert.AreEqual(pendingId.OperationId, correlated.Identity.OperationId);
				Assert.IsNull(journal.GetOperationForNativeAttempt(native.OperationId, Guid.NewGuid()));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void OperationAndChild_RequirePersistedExactCollectionRevisions()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				var journal = new CollectionsOperationStore(featureStore);
				CollectionIdentity missingCollection = CollectionIdentity.FromNexus("missing-collection");
				CollectionRevisionIdentity missingRevision = CollectionRevisionIdentity.FromNexus(missingCollection, "rev-a", 1);
				CollectionOperation missingOperation = CreateOperation(CollectionOperationIdentity.CreateNew(),
					CollectionOperationKind.ApplyResolvedPlan, missingCollection, CollectionTargetIdentity.FromFingerprint("target-a"),
					missingRevision, 0, CollectionOperationPhase.Created, CollectionOperationResultState.Pending,
					new CollectionNativeChildOperation[0]);
				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(missingOperation));

				CollectionRevisionIdentity persistedRevision = SeedNexusRevision(featureStore, "persisted-operation", "rev-a", 1);
				CollectionIdentity missingMemberCollection = CollectionIdentity.FromNexus("missing-member");
				CollectionRevisionIdentity missingMemberRevision = CollectionRevisionIdentity.FromNexus(missingMemberCollection, "rev-x", 1);
				ModOperationIdentity native = CreateNativeIdentity("target-a", ModOperationOrigin.Collection,
					ModInstallMethod.Virtual, ModInstallRoot.Data, "recipe-v1");
				CollectionNativeChildOperation missingChild = CreateChild(1, missingMemberRevision, "member-a", native,
					CollectionNativeChildAction.ActivateOrReinstall, CollectionNativeChildCheckpoint.IntentPersisted, null);
				CollectionOperation operation = CreateOperation(CollectionOperationIdentity.CreateNew(),
					CollectionOperationKind.ApplyResolvedPlan, persistedRevision.Collection,
					CollectionTargetIdentity.FromFingerprint("target-a"), persistedRevision, 1,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new[] { missingChild });
				Assert.Throws<InvalidOperationException>(() => journal.SaveOperation(operation));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void LocalCollectionAndLocalMemberIdentity_RoundTripWithoutNexusAssumptions()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				var catalog = new CollectionsCatalogStore(featureStore);
				CollectionIdentity collection = CollectionIdentity.FromLocal(Guid.NewGuid());
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(collection, Guid.NewGuid());
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "Local", null, null),
					new CollectionRevision(revision, "Local revision", null, 1));
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("local-target");
				ModOperationIdentity native = CreateNativeIdentity(target.Fingerprint, ModOperationOrigin.LocalRestore,
					ModInstallMethod.Direct, ModInstallRoot.GameRoot, null);
				CollectionNativeChildOperation child = new CollectionNativeChildOperation(1,
					new CollectionOperationMemberReference(revision, CollectionMemberKey.FromLocal(Guid.NewGuid())),
					CollectionNativeChildAction.Deactivate, native, CollectionNativeChildCheckpoint.IntentPersisted, null);
				CollectionOperation operation = CreateOperation(CollectionOperationIdentity.CreateNew(),
					CollectionOperationKind.RestoreLocalCapture, collection, target, revision, 1,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new[] { child });

				var journal = new CollectionsOperationStore(featureStore);
				journal.SaveOperation(operation);
				CollectionOperation loaded = journal.GetOperation(operation.Identity);
				Assert.AreEqual(CollectionOrigin.Local, loaded.Collection.Origin);
				Assert.IsFalse(loaded.Revision.NexusRevisionNumber.HasValue);
				Assert.AreEqual(CollectionMemberKeyKind.Local, loaded.NativeChildren.Single().Member.MemberKey.Kind);
				Assert.IsNull(loaded.NativeChildren.Single().NativeOperation.Fingerprint.RecipeFingerprint);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionOperation CreateOperation(CollectionOperationIdentity identity, CollectionOperationKind kind,
			CollectionIdentity collection, CollectionTargetIdentity target, CollectionRevisionIdentity revision,
			long checkpointSequence, CollectionOperationPhase phase, CollectionOperationResultState resultState,
			IEnumerable<CollectionNativeChildOperation> children)
		{
			return new CollectionOperation(identity, kind, collection, target, revision, null, checkpointSequence,
				phase, resultState, children);
		}

		private static CollectionNativeChildOperation CreateIntentChild(int sequence, CollectionRevisionIdentity revision,
			string memberKey, string target)
		{
			ModOperationIdentity native = CreateNativeIdentity(target, ModOperationOrigin.Collection,
				ModInstallMethod.Virtual, ModInstallRoot.Data, "recipe-" + memberKey);
			return CreateChild(sequence, revision, memberKey, native, CollectionNativeChildAction.ActivateOrReinstall,
				CollectionNativeChildCheckpoint.IntentPersisted, null);
		}

		private static CollectionNativeChildOperation CreateChild(int sequence, CollectionRevisionIdentity revision,
			string memberKey, ModOperationIdentity native, CollectionNativeChildAction action,
			CollectionNativeChildCheckpoint checkpoint, ModOperationResult result)
		{
			return new CollectionNativeChildOperation(sequence,
				new CollectionOperationMemberReference(revision, CollectionMemberKey.FromProvider(memberKey)),
				action, native, checkpoint, result);
		}

		private static ModOperationIdentity CreateNativeIdentity(string target, ModOperationOrigin origin,
			ModInstallMethod method, ModInstallRoot root, string recipeFingerprint)
		{
			return ModOperationIdentity.CreateNew(origin,
				new ModOperationFingerprint(target, new ModInstallContext(method, root), recipeFingerprint));
		}

		private static CollectionsStore CreateFeatureStore(string root)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			return store;
		}

		private static CollectionRevisionIdentity SeedNexusRevision(CollectionsStore store, string collectionId,
			string revisionId, long revisionNumber)
		{
			var catalog = new CollectionsCatalogStore(store);
			CollectionIdentity collection = CollectionIdentity.FromNexus(collectionId);
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, revisionId, revisionNumber);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, collectionId, null, null),
				new CollectionRevision(revision, "Revision " + revisionNumber, null, null));
			return revision;
		}

		private static string CreateTemporaryDirectory()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-collections-operation-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			return directory;
		}
	}
}
