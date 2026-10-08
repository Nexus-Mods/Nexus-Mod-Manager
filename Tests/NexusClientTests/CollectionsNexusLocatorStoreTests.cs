using System;
using System.IO;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	public class CollectionsNexusLocatorStoreTests
	{
		[Test]
		public void Save_RoundTripsSlugAndDomainWithoutChangingStableIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var locators = new CollectionsNexusLocatorStore(featureStore);
				CollectionIdentity collection = CollectionIdentity.FromNexus("229114");

				locators.Save(collection, "Fallout4", "is2qjo");

				CollectionNexusLocatorRecord loaded = locators.Get(collection);
				Assert.IsNotNull(loaded);
				Assert.AreEqual(collection, loaded.Collection);
				Assert.AreEqual("fallout4", loaded.GameDomain);
				Assert.AreEqual("is2qjo", loaded.CollectionSlug);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Save_RefreshesPublicLocatorForSameStableCollection()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var locators = new CollectionsNexusLocatorStore(featureStore);
				CollectionIdentity collection = CollectionIdentity.FromNexus("229114");
				locators.Save(collection, "fallout4", "oldslug");

				locators.Save(collection, "fallout4", "newslug");

				Assert.AreEqual("newslug", locators.Get(collection).CollectionSlug);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "NmmCollectionsLocatorTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}
	}
}
