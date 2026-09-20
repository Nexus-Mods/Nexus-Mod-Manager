using System;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.5 immutable resolved-plan persistence coverage.
	/// </summary>
	[TestFixture]
	public class CollectionsResolvedPlanStoreTests
	{
		private const string Sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void SavePlan_RoundTripsExactMetadataAndDefensivePayload()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionRevisionIdentity revision = SeedRevision(featureStore, "plan-roundtrip", "rev-a", 1);
				ResolvedCollectionPlan plan = CreatePlan(revision, CollectionTargetIdentity.FromFingerprint("target-plan"),
					CollectionPlanIdentity.From(Guid.NewGuid(), 1));
				var store = new CollectionsResolvedPlanStore(featureStore);
				byte[] payload = { 1, 2, 3, 4 };

				store.SavePlan(plan, "test-plan/1", payload);
				payload[0] = 99;
				CollectionResolvedPlanRecord loaded = store.GetPlan(plan.Identity);

				Assert.IsNotNull(loaded);
				Assert.AreEqual(plan.Identity, loaded.Identity);
				Assert.AreEqual(plan.Revision, loaded.Revision);
				Assert.AreEqual(plan.Target, loaded.Target);
				Assert.AreEqual(CollectionExecutionPolicyKind.InstallIntoCurrentSetup, loaded.PolicyKind);
				Assert.AreEqual(plan.CurrentStateFingerprint, loaded.CurrentStateFingerprint);
				Assert.AreEqual("test-plan/1", loaded.PayloadFormat);
				CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, loaded.Payload);
				byte[] exposed = loaded.Payload;
				exposed[1] = 77;
				CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, loaded.Payload);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void SavePlan_IsIdempotentButRejectsRebindingSamePlanVersion()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionRevisionIdentity revision = SeedRevision(featureStore, "plan-immutable", "rev-a", 1);
				ResolvedCollectionPlan plan = CreatePlan(revision, CollectionTargetIdentity.FromFingerprint("target-plan"),
					CollectionPlanIdentity.From(Guid.NewGuid(), 1));
				var store = new CollectionsResolvedPlanStore(featureStore);
				byte[] payload = { 10, 20, 30 };

				store.SavePlan(plan, "test-plan/1", payload);
				store.SavePlan(plan, "test-plan/1", new byte[] { 10, 20, 30 });
				Assert.Throws<InvalidOperationException>(() => store.SavePlan(plan, "test-plan/1", new byte[] { 10, 20, 31 }));
				Assert.Throws<InvalidOperationException>(() => store.SavePlan(plan, "test-plan/2", payload));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void SavePlan_RequiresExactPersistedRevision()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionIdentity collection = CollectionIdentity.FromNexus("missing-plan-revision");
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "rev-a", 1);
				ResolvedCollectionPlan plan = CreatePlan(revision, CollectionTargetIdentity.FromFingerprint("target-plan"),
					CollectionPlanIdentity.From(Guid.NewGuid(), 1));
				var store = new CollectionsResolvedPlanStore(featureStore);

				Assert.Throws<InvalidOperationException>(() => store.SavePlan(plan, "test-plan/1", new byte[] { 1 }));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static ResolvedCollectionPlan CreatePlan(CollectionRevisionIdentity revision, CollectionTargetIdentity target,
			CollectionPlanIdentity identity)
		{
			NormalizedCollectionMember member = new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "game/100/200", null),
				CollectionRecipeIdentity.FromFingerprint("recipe-member"), "Member", 0);
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(revision,
				new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(Sha256), 10, "schema", "normalizer-v3"),
				CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			return new ResolvedCollectionPlan(identity, target, CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				new CollectionCurrentStateFingerprint("test-state/1", "state-a"), report,
				new[] { new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact)) });
		}

		private static CollectionsStore CreateFeatureStore(string root)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			return store;
		}

		private static CollectionRevisionIdentity SeedRevision(CollectionsStore store, string collectionId,
			string revisionId, long revisionNumber)
		{
			var catalog = new CollectionsCatalogStore(store);
			CollectionIdentity collection = CollectionIdentity.FromNexus(collectionId);
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, revisionId, revisionNumber);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, collectionId, null, null),
				new CollectionRevision(revision, "Revision", null, 1));
			return revision;
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c6-5-plan-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}
	}
}
