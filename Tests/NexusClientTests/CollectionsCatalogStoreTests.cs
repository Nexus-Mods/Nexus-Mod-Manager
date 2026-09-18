using System;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C4.6 durable Collection definition/revision catalog persistence coverage.
	/// </summary>
	public class CollectionsCatalogStoreTests
	{
		[Test]
		public void SaveDefinitionAndRevision_RoundTripsExactNexusIdentityAndMetadata()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var catalog = new CollectionsCatalogStore(featureStore);
				CollectionIdentity collectionIdentity = CollectionIdentity.FromNexus("ab12cd");
				var definition = new CollectionDefinition(collectionIdentity, "Curated Setup", "Curator", "Summary text");
				CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromNexus(collectionIdentity, "revision-node-42", 42);
				var revision = new CollectionRevision(revisionIdentity, "Revision 42", "Pinned baseline", 123);

				catalog.SaveDefinitionAndRevision(definition, revision);

				CollectionDefinition loadedDefinition = catalog.GetDefinition(collectionIdentity);
				CollectionRevision loadedRevision = catalog.GetRevision(revisionIdentity);
				Assert.IsNotNull(loadedDefinition);
				Assert.AreEqual(collectionIdentity, loadedDefinition.Identity);
				Assert.AreEqual("Curated Setup", loadedDefinition.DisplayName);
				Assert.AreEqual("Curator", loadedDefinition.AuthorDisplayName);
				Assert.AreEqual("Summary text", loadedDefinition.Summary);
				Assert.IsNotNull(loadedRevision);
				Assert.AreEqual(revisionIdentity, loadedRevision.Identity);
				Assert.AreEqual(42, loadedRevision.Identity.NexusRevisionNumber);
				Assert.AreEqual("Revision 42", loadedRevision.RevisionLabel);
				Assert.AreEqual("Pinned baseline", loadedRevision.Notes);
				Assert.AreEqual(123, loadedRevision.DeclaredMemberCount);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void SaveDefinition_RefreshesDisplayMetadataWithoutDeletingExistingRevisions()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var catalog = new CollectionsCatalogStore(featureStore);
				CollectionIdentity collectionIdentity = CollectionIdentity.FromNexus("refresh-me");
				CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromNexus(collectionIdentity, "rev-one", 1);
				var revision = new CollectionRevision(revisionIdentity, "One", null, 4);
				catalog.SaveDefinitionAndRevision(
					new CollectionDefinition(collectionIdentity, "Old name", "Old author", "Old summary"),
					revision);

				catalog.SaveDefinition(new CollectionDefinition(collectionIdentity, "New name", "New author", "New summary"));

				CollectionDefinition refreshed = catalog.GetDefinition(collectionIdentity);
				Assert.AreEqual("New name", refreshed.DisplayName);
				Assert.AreEqual("New author", refreshed.AuthorDisplayName);
				Assert.AreEqual("New summary", refreshed.Summary);
				Assert.AreEqual(revision, catalog.GetRevision(revisionIdentity));
				Assert.AreEqual(1, catalog.GetRevisions(collectionIdentity).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void SaveRevision_IsIdempotentForSameMetadataAndRejectsMutation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var catalog = new CollectionsCatalogStore(featureStore);
				CollectionIdentity collectionIdentity = CollectionIdentity.FromNexus("immutable-revision");
				CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromNexus(collectionIdentity, "stable-revision", 7);
				var definition = new CollectionDefinition(collectionIdentity, "Collection", null, null);
				var original = new CollectionRevision(revisionIdentity, "Seven", "Original notes", 10);
				catalog.SaveDefinitionAndRevision(definition, original);

				Assert.DoesNotThrow(() => catalog.SaveRevision(new CollectionRevision(revisionIdentity, "Seven", "Original notes", 10)));
				Assert.Throws<InvalidOperationException>(() =>
					catalog.SaveRevision(new CollectionRevision(revisionIdentity, "Seven changed", "Original notes", 10)));

				CollectionRevision loaded = catalog.GetRevision(revisionIdentity);
				Assert.AreEqual("Seven", loaded.RevisionLabel);
				Assert.AreEqual("Original notes", loaded.Notes);
				Assert.AreEqual(10, loaded.DeclaredMemberCount);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void SaveDefinitionAndRevision_RollsBackDefinitionRefreshWhenRevisionMutationIsRejected()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var catalog = new CollectionsCatalogStore(featureStore);
				CollectionIdentity collectionIdentity = CollectionIdentity.FromNexus("atomic-catalog");
				CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromNexus(collectionIdentity, "stable-revision", 3);
				catalog.SaveDefinitionAndRevision(
					new CollectionDefinition(collectionIdentity, "Original definition", null, null),
					new CollectionRevision(revisionIdentity, "Original revision", null, 5));

				Assert.Throws<InvalidOperationException>(() =>
					catalog.SaveDefinitionAndRevision(
						new CollectionDefinition(collectionIdentity, "Should roll back", null, null),
						new CollectionRevision(revisionIdentity, "Changed revision", null, 5)));

				Assert.AreEqual("Original definition", catalog.GetDefinition(collectionIdentity).DisplayName);
				Assert.AreEqual("Original revision", catalog.GetRevision(revisionIdentity).RevisionLabel);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void SaveRevision_RequiresPersistedOwningDefinition()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var catalog = new CollectionsCatalogStore(featureStore);
				CollectionIdentity collectionIdentity = CollectionIdentity.FromNexus("missing-parent");
				var revision = new CollectionRevision(
					CollectionRevisionIdentity.FromNexus(collectionIdentity, "rev", 1), null, null, null);

				Assert.Throws<InvalidOperationException>(() => catalog.SaveRevision(revision));
				Assert.IsNull(catalog.GetDefinition(collectionIdentity));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void NexusRevisionNumber_CannotBeReboundToDifferentStableRevisionIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var catalog = new CollectionsCatalogStore(featureStore);
				CollectionIdentity collectionIdentity = CollectionIdentity.FromNexus("revision-number-conflict");
				catalog.SaveDefinition(new CollectionDefinition(collectionIdentity, "Collection", null, null));
				catalog.SaveRevision(new CollectionRevision(
					CollectionRevisionIdentity.FromNexus(collectionIdentity, "provider-revision-a", 9), "A", null, null));

				Assert.Throws<InvalidOperationException>(() =>
					catalog.SaveRevision(new CollectionRevision(
						CollectionRevisionIdentity.FromNexus(collectionIdentity, "provider-revision-b", 9), "B", null, null)));
				Assert.AreEqual(1, catalog.GetRevisions(collectionIdentity).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void LocalCollectionAndRevisions_RoundTripWithoutInventingNexusRevisionNumbers()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var catalog = new CollectionsCatalogStore(featureStore);
				CollectionIdentity collectionIdentity = CollectionIdentity.FromLocal(Guid.NewGuid());
				CollectionRevisionIdentity firstIdentity = CollectionRevisionIdentity.FromLocal(collectionIdentity, Guid.NewGuid());
				CollectionRevisionIdentity secondIdentity = CollectionRevisionIdentity.FromLocal(collectionIdentity, Guid.NewGuid());
				catalog.SaveDefinitionAndRevision(
					new CollectionDefinition(collectionIdentity, "Local collection", "Local user", null),
					new CollectionRevision(firstIdentity, "First sealed capture", null, 6));
				catalog.SaveRevision(new CollectionRevision(secondIdentity, "Second sealed capture", null, 7));

				CollectionRevision[] revisions = catalog.GetRevisions(collectionIdentity).ToArray();
				Assert.AreEqual(2, revisions.Length);
				Assert.IsTrue(revisions.All(x => x.IsSealedLocalRevision));
				Assert.IsTrue(revisions.All(x => !x.Identity.NexusRevisionNumber.HasValue));
				Assert.AreEqual(firstIdentity, catalog.GetRevision(firstIdentity).Identity);
				Assert.AreEqual(secondIdentity, catalog.GetRevision(secondIdentity).Identity);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Definitions_AreListedInStableOriginAndIdentityOrder()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var catalog = new CollectionsCatalogStore(featureStore);
				catalog.SaveDefinition(new CollectionDefinition(CollectionIdentity.FromLocal(Guid.Parse("11111111-1111-1111-1111-111111111111")), "Local", null, null));
				catalog.SaveDefinition(new CollectionDefinition(CollectionIdentity.FromNexus("zeta"), "Zeta", null, null));
				catalog.SaveDefinition(new CollectionDefinition(CollectionIdentity.FromNexus("alpha"), "Alpha", null, null));

				CollectionDefinition[] definitions = catalog.GetDefinitions().ToArray();
				Assert.AreEqual(3, definitions.Length);
				Assert.AreEqual("alpha", definitions[0].Identity.StableId);
				Assert.AreEqual("zeta", definitions[1].Identity.StableId);
				Assert.AreEqual(CollectionOrigin.Local, definitions[2].Origin);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static string CreateTemporaryDirectory()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-collections-catalog-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			return directory;
		}
	}
}
