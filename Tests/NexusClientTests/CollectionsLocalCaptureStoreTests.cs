using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C7.8 durable Local Collection capture catalog/package binding coverage.</summary>
	[TestFixture]
	public class CollectionsLocalCaptureStoreTests
	{
		[Test]
		public void Save_RoundTripsSealedCaptureAndDurablePackageBinding()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				var captures = new CollectionsLocalCaptureStore(store);

				CollectionIdentity collectionIdentity = CollectionIdentity.FromLocal(Guid.Parse("11111111-1111-1111-1111-111111111111"));
				CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromLocal(collectionIdentity,
					Guid.Parse("22222222-2222-2222-2222-222222222222"));
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(Guid.Parse("33333333-3333-3333-3333-333333333333"));
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c78-store");
				var definition = new CollectionDefinition(collectionIdentity, "Saved setup", null, "Captured from NMM.");
				var revision = new CollectionRevision(revisionIdentity, "Captured setup", null, 1);

				CollectionsRetainedArtifact contentArtifact = Publish(artifacts, "captured owner bytes");
				const string contentRole = "owner-payload:test";
				references.AcquireExclusiveRoleReference(contentArtifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString(), contentRole);
				var retained = new RetainedArtifactReference(contentArtifact.ArtifactId, contentRole,
					contentArtifact.ContentHash, contentArtifact.ByteLength);

				CollectionsRetainedArtifact packageArtifact = Publish(artifacts, "versioned capture package");
				references.AcquireExclusiveRoleReference(packageArtifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString(),
					CollectionsLocalCaptureStore.PackageReferenceRole);

				var scope = new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[]
				{
					LocalCaptureScopeArea.ManagedModState,
					LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads
				});
				var exclusion = new LocalCaptureExclusion(LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads,
					"fixture-exclusion", "Fixture exclusion retained for round-trip coverage.");
				var mapping = new LocalCaptureNativeRecordMapping(
					CollectionMemberKey.FromLocal(Guid.Parse("44444444-4444-4444-4444-444444444444")),
					new NativeModInstanceIdentity(target, "native-a"));
				var capture = new LocalCapture(captureIdentity, revisionIdentity, target,
					new CollectionCurrentStateFingerprint("state-v1", "fingerprint-c78"), scope,
					LocalCaptureCapability.RecipeOnly, new[] { retained }, new[] { exclusion }, new[] { mapping });

				captures.Save(definition, revision, capture, CollectionLocalCapturePackageCodec.CurrentFormatVersion,
					packageArtifact.ArtifactId);

				LocalCapture loaded = captures.GetCapture(captureIdentity);
				Assert.IsNotNull(loaded);
				Assert.AreEqual(capture.Identity, loaded.Identity);
				Assert.AreEqual(capture.Revision, loaded.Revision);
				Assert.AreEqual(capture.SourceTarget, loaded.SourceTarget);
				Assert.AreEqual(capture.CapturedStateFingerprint, loaded.CapturedStateFingerprint);
				Assert.AreEqual(capture.Capability, loaded.Capability);
				Assert.AreEqual(capture.SchemaVersion, loaded.SchemaVersion);
				Assert.AreEqual(capture.CapabilityVersion, loaded.CapabilityVersion);
				Assert.AreEqual(1, loaded.RetainedArtifacts.Count);
				Assert.AreEqual(retained, loaded.RetainedArtifacts.Single());
				Assert.AreEqual(exclusion, loaded.Exclusions.Single());
				Assert.AreEqual(mapping, loaded.NativeRecordMappings.Single());
				Assert.AreEqual(packageArtifact.ArtifactId, captures.GetPackageArtifactId(captureIdentity));

				var catalog = new CollectionsCatalogStore(store);
				Assert.AreEqual("Saved setup", catalog.GetDefinition(collectionIdentity).DisplayName);
				Assert.AreEqual(1, catalog.GetRevision(revisionIdentity).DeclaredMemberCount);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Save_RequiresCaptureOwnedPackageReferenceBeforePublishingCatalogRows()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var captures = new CollectionsLocalCaptureStore(store);
				CollectionIdentity collectionIdentity = CollectionIdentity.FromLocal(Guid.NewGuid());
				CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromLocal(collectionIdentity, Guid.NewGuid());
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(Guid.NewGuid());
				var definition = new CollectionDefinition(collectionIdentity, "Not published", null, null);
				var revision = new CollectionRevision(revisionIdentity, "Capture", null, 0);
				var target = CollectionTargetIdentity.FromFingerprint("target-c78-missing-package-reference");
				var capture = new LocalCapture(captureIdentity, revisionIdentity, target,
					new CollectionCurrentStateFingerprint("state-v1", "fingerprint"),
					new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[] { LocalCaptureScopeArea.ManagedModState }),
					LocalCaptureCapability.RecipeOnly, new RetainedArtifactReference[0],
					new LocalCaptureExclusion[0], new LocalCaptureNativeRecordMapping[0]);
				CollectionsRetainedArtifact packageArtifact = Publish(artifacts, "unreferenced package");

				Assert.Throws<InvalidOperationException>(() => captures.Save(definition, revision, capture,
					CollectionLocalCapturePackageCodec.CurrentFormatVersion, packageArtifact.ArtifactId));

				var catalog = new CollectionsCatalogStore(store);
				Assert.IsNull(catalog.GetDefinition(collectionIdentity));
				Assert.IsNull(captures.GetCapture(captureIdentity));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionsRetainedArtifact Publish(CollectionsRetainedArtifactStore store, string text)
		{
			using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text), false))
				return store.Publish(stream);
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c78-local-capture-store-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}
	}
}
