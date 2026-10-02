using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsGateA")]
	public class CollectionsLocalWorkingCopyStoreTests
	{
		[Test]
		public void CreateClone_RoundTripsExactBaseAndProtectsItAfterSourceRetentionRelease()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				CollectionIdentity sourceCollection = CollectionIdentity.FromNexus("working-copy-source");
				var sourceDefinition = new CollectionDefinition(sourceCollection, "Source Collection", "Curator", "Summary");
				var sourceRevision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(sourceCollection, "revision-one", 1),
					"Revision 1", null, 0);
				catalog.SaveDefinitionAndRevision(sourceDefinition, sourceRevision);

				byte[] bytes = Encoding.UTF8.GetBytes("{\"info\":{\"name\":\"Working copy base\"},\"mods\":[],\"modRules\":[]}");
				CollectionContentHash hash = ComputeHash(bytes);
				var sourceSnapshot = new CollectionManifestSourceSnapshot(hash, bytes.LongLength,
					NexusCollectionManifestNormalizer.SchemaIdentity, NexusCollectionManifestNormalizer.NormalizerVersion);
				var manifest = new NormalizedCollectionManifest(sourceRevision.Identity, sourceSnapshot,
					CollectionManifestMemberSetCompleteness.Complete, null, new NormalizedCollectionMember[0]);
				var revisionSources = new CollectionsRevisionSourceStore(store);
				CollectionRevisionSourceRecord retained = revisionSources.RetainManifest(manifest,
					CollectionRevisionSourceInputKind.RawManifest, hash, bytes.LongLength,
					NexusCollectionBundleImporter.ManifestFileName, bytes);

				CollectionIdentity localCollection = CollectionIdentity.FromLocal(Guid.NewGuid());
				var localDefinition = new CollectionDefinition(localCollection, "Editable copy", "Curator", "Summary");
				var workingCopies = new CollectionsLocalWorkingCopyStore(store);
				CollectionLocalWorkingCopyRecord created = workingCopies.CreateClone(localDefinition, sourceRevision, retained);

				Assert.AreEqual(localCollection, created.Collection);
				Assert.AreEqual(sourceRevision.Identity, created.SourceRevision);
				Assert.AreEqual(retained.RawManifestArtifactId, created.BaseSource.RawManifestArtifactId);
				Assert.AreEqual(retained.RawManifestArtifactId, created.DraftManifestArtifactId);
				Assert.AreEqual(1, workingCopies.GetAll().Count);
				CollectionAssert.AreEqual(bytes, workingCopies.LoadBaseManifest(created));
				CollectionAssert.AreEqual(bytes, workingCopies.LoadDraftManifest(created));
				Assert.AreEqual(0, catalog.GetRevisions(localCollection).Count,
					"Creating a mutable working copy must not invent an immutable Local revision.");

				var references = new CollectionsRetainedArtifactReferenceStore(store);
				references.ReleaseOwnerReferences(CollectionsRetainedArtifactOwnerKind.Revision,
					CollectionsRevisionSourceStore.GetRevisionOwnerId(sourceRevision.Identity));
				store.ExecuteWrite((connection, transaction) =>
				{
					using (var command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = @"UPDATE revision_sources
SET raw_manifest_artifact_id=NULL
WHERE origin=@origin AND collection_id=@collection_id AND revision_id=@revision_id;";
						command.Parameters.AddWithValue("@origin", (int)sourceRevision.Collection.Origin);
						command.Parameters.AddWithValue("@collection_id", sourceRevision.Collection.StableId);
						command.Parameters.AddWithValue("@revision_id", sourceRevision.Identity.StableRevisionId);
						Assert.AreEqual(1, command.ExecuteNonQuery());
					}
				});

				var cleanup = new CollectionsRetainedArtifactCleanupStore(store);
				Assert.IsFalse(cleanup.GetCleanupCandidates(128).Contains(retained.RawManifestArtifactId),
					"The Local working copy must keep its exact base manifest alive independently of source-revision retention.");
				CollectionAssert.AreEqual(bytes, workingCopies.LoadBaseManifest(created));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void CreateClone_RequiresRetainedExactManifestBytes()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				CollectionIdentity sourceCollection = CollectionIdentity.FromNexus("working-copy-missing-source");
				var sourceDefinition = new CollectionDefinition(sourceCollection, "Source", null, null);
				var sourceRevision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(sourceCollection, "revision-one", 1), null, null, 0);
				catalog.SaveDefinitionAndRevision(sourceDefinition, sourceRevision);
				byte[] bytes = Encoding.UTF8.GetBytes("{}");
				CollectionContentHash hash = ComputeHash(bytes);
				var sourceSnapshot = new CollectionManifestSourceSnapshot(hash, bytes.LongLength,
					NexusCollectionManifestNormalizer.SchemaIdentity, NexusCollectionManifestNormalizer.NormalizerVersion);
				var manifest = new NormalizedCollectionManifest(sourceRevision.Identity, sourceSnapshot,
					CollectionManifestMemberSetCompleteness.Complete, null, new NormalizedCollectionMember[0]);
				var revisionSources = new CollectionsRevisionSourceStore(store);
				revisionSources.RetainManifest(manifest, CollectionRevisionSourceInputKind.RawManifest, hash, bytes.LongLength,
					NexusCollectionBundleImporter.ManifestFileName, bytes);
				store.ExecuteWrite((connection, transaction) =>
				{
					using (var command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = @"UPDATE revision_sources
SET raw_manifest_artifact_id=NULL
WHERE origin=@origin AND collection_id=@collection_id AND revision_id=@revision_id;";
						command.Parameters.AddWithValue("@origin", (int)sourceRevision.Collection.Origin);
						command.Parameters.AddWithValue("@collection_id", sourceRevision.Collection.StableId);
						command.Parameters.AddWithValue("@revision_id", sourceRevision.Identity.StableRevisionId);
						Assert.AreEqual(1, command.ExecuteNonQuery());
					}
				});
				CollectionRevisionSourceRecord source = revisionSources.GetSource(sourceRevision.Identity);
				Assert.IsNotNull(source);
				Assert.IsNull(source.RawManifestArtifactId);
				var localDefinition = new CollectionDefinition(CollectionIdentity.FromLocal(Guid.NewGuid()), "Local", null, null);

				Assert.Throws<InvalidOperationException>(() => new CollectionsLocalWorkingCopyStore(store)
					.CreateClone(localDefinition, sourceRevision, source));
				Assert.IsNull(catalog.GetDefinition(localDefinition.Identity));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}


		[Test]
		public void Editor_SaveDraft_ChangesLocalRecipeWithoutMutatingImmutableCloneBase()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				CollectionIdentity sourceCollection = CollectionIdentity.FromNexus("working-copy-editor-source");
				var sourceDefinition = new CollectionDefinition(sourceCollection, "Source Collection", "Curator", "Source summary");
				var sourceRevision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(sourceCollection, "revision-one", 1),
					"Revision 1", null, 2);
				catalog.SaveDefinitionAndRevision(sourceDefinition, sourceRevision);

				byte[] baseBytes = BuildEditableManifestBytes();
				var normalizer = new NexusCollectionManifestNormalizer();
				NexusCollectionManifestNormalizationResult normalized = normalizer.Normalize(baseBytes, sourceRevision);
				Assert.AreEqual(CollectionCompatibilityStatus.Supported, normalized.CapabilityReport.Status);
				var revisionSources = new CollectionsRevisionSourceStore(store);
				CollectionRevisionSourceRecord retained = revisionSources.RetainManifest(normalized.Manifest,
					CollectionRevisionSourceInputKind.RawManifest, normalized.Manifest.Source.ContentHash,
					normalized.Manifest.Source.ByteLength, NexusCollectionBundleImporter.ManifestFileName, baseBytes);
				CollectionIdentity localCollection = CollectionIdentity.FromLocal(Guid.NewGuid());
				var workingCopies = new CollectionsLocalWorkingCopyStore(store);
				CollectionLocalWorkingCopyRecord created = workingCopies.CreateClone(
					new CollectionDefinition(localCollection, "Editable copy", "Curator", "Source summary"), sourceRevision, retained);
				var editor = new CollectionLocalWorkingCopyEditor(store);
				CollectionLocalWorkingCopyEditSnapshot snapshot = editor.GetSnapshot(localCollection);

				CollectionLocalWorkingCopyMemberEditState required = snapshot.Members.Single(x => x.DisplayName == "Required");
				CollectionLocalWorkingCopyMemberEditState optional = snapshot.Members.Single(x => x.DisplayName == "Optional");
				CollectionLocalWorkingCopyEditSnapshot edited = editor.SaveDraft(snapshot, "Edited copy", "Edited summary",
					new[]
					{
						new CollectionLocalWorkingCopyMemberDecision(required.MemberKey, true, CollectionMemberRequirement.Optional),
						new CollectionLocalWorkingCopyMemberDecision(optional.MemberKey, false, CollectionMemberRequirement.Optional)
					});

				CollectionAssert.AreEqual(baseBytes, workingCopies.LoadBaseManifest(edited.Record),
					"Editing a working copy must never rewrite the immutable clone base recipe.");
				Assert.AreNotEqual(created.DraftManifestArtifactId, edited.Record.DraftManifestArtifactId);
				CollectionDefinition editedDefinition = catalog.GetDefinition(localCollection);
				Assert.AreEqual("Edited copy", editedDefinition.DisplayName);
				Assert.AreEqual("Edited summary", editedDefinition.Summary);
				byte[] draftBytes = workingCopies.LoadDraftManifest(edited.Record);
				NexusCollectionManifestNormalizationResult draft = normalizer.Normalize(draftBytes, sourceRevision);
				Assert.AreEqual(CollectionCompatibilityStatus.Supported, draft.CapabilityReport.Status);
				Assert.AreEqual(1, draft.Manifest.Members.Count);
				Assert.AreEqual("Required", draft.Manifest.Members.Single().DisplayName);
				Assert.AreEqual(CollectionMemberRequirement.Optional, draft.Manifest.Members.Single().Requirement);
				Assert.IsFalse(new CollectionsRetainedArtifactCleanupStore(store).GetCleanupCandidates(128)
					.Contains(edited.Record.DraftManifestArtifactId),
					"The mutable draft must remain retained while its Local working copy points at it.");
				Assert.Throws<InvalidOperationException>(() => editor.SaveDraft(snapshot, "Stale edit", null,
					new[] { new CollectionLocalWorkingCopyMemberDecision(required.MemberKey, true, required.Requirement) }));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Editor_SealRevision_RetainsEditedManifestAndInheritedBundleAsImmutableLocalSource()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				CollectionIdentity sourceCollection = CollectionIdentity.FromNexus("working-copy-bundle-source");
				var sourceRevision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(sourceCollection, "revision-one", 1),
					"Revision 1", null, 1);
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(sourceCollection, "Bundled source", "Curator", null), sourceRevision);
				byte[] manifestBytes = Encoding.UTF8.GetBytes("{" +
					"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"Bundled source\",\"description\":\"Source\",\"domainName\":\"skyrim\"}," +
					"\"mods\":[{\"name\":\"Bundled Tool\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"bundle\",\"fileSize\":32,\"updatePolicy\":\"exact\",\"fileExpression\":\"Bundled - Tool\",\"tag\":\"toolTag\"}}],\"modRules\":[]}");
				byte[] bundleBytes = Encoding.UTF8.GetBytes("immutable inherited outer collection bundle");
				var normalizer = new NexusCollectionManifestNormalizer();
				NexusCollectionManifestNormalizationResult normalized = normalizer.Normalize(manifestBytes, sourceRevision);
				Assert.AreEqual(CollectionCompatibilityStatus.Supported, normalized.CapabilityReport.Status);
				var revisionSources = new CollectionsRevisionSourceStore(store);
				CollectionRevisionSourceRecord retained = revisionSources.RetainManifest(normalized.Manifest,
					CollectionRevisionSourceInputKind.Archive, ComputeHash(bundleBytes), bundleBytes.LongLength,
					NexusCollectionBundleImporter.ManifestFileName, manifestBytes);
				using (var bundle = new MemoryStream(bundleBytes, false))
					retained = revisionSources.RetainBundle(sourceRevision.Identity, bundle);

				CollectionIdentity localCollection = CollectionIdentity.FromLocal(Guid.NewGuid());
				new CollectionsLocalWorkingCopyStore(store).CreateClone(
					new CollectionDefinition(localCollection, "Editable bundle", "Curator", null), sourceRevision, retained);
				var editor = new CollectionLocalWorkingCopyEditor(store);
				CollectionLocalWorkingCopyEditSnapshot snapshot = editor.GetSnapshot(localCollection);
				CollectionLocalWorkingCopyMemberEditState member = snapshot.Members.Single();
				CollectionLocalWorkingCopyEditSnapshot edited = editor.SaveDraft(snapshot, "Edited bundle", "Local edit",
					new[] { new CollectionLocalWorkingCopyMemberDecision(member.MemberKey, true, CollectionMemberRequirement.Optional) });
				byte[] editedBytes = new CollectionsLocalWorkingCopyStore(store).LoadDraftManifest(edited.Record);

				CollectionRevision sealedRevision = editor.SealRevision(localCollection, "Local revision 1", "First local edit");

				Assert.AreEqual(localCollection, sealedRevision.Collection);
				Assert.AreEqual(CollectionOrigin.Local, sealedRevision.Collection.Origin);
				Assert.AreEqual(1, catalog.GetRevisions(localCollection).Count);
				CollectionRevisionSourceRecord sealedSource = revisionSources.GetSource(sealedRevision.Identity);
				Assert.IsNotNull(sealedSource);
				Assert.AreEqual(CollectionRevisionSourceInputKind.LocalWorkingCopy, sealedSource.InputKind);
				Assert.IsNotNull(sealedSource.RawBundleArtifactId);
				CollectionAssert.AreEqual(editedBytes, revisionSources.LoadManifest(sealedRevision.Identity, sealedSource.ManifestSource));
				using (Stream openedBundle = revisionSources.OpenBundle(sealedRevision.Identity))
				using (var copiedBundle = new MemoryStream())
				{
					openedBundle.CopyTo(copiedBundle);
					CollectionAssert.AreEqual(bundleBytes, copiedBundle.ToArray());
				}
				NexusCollectionManifestNormalizationResult sealedNormalized = normalizer.Normalize(editedBytes, sealedRevision);
				Assert.AreEqual(CollectionCompatibilityStatus.Supported, sealedNormalized.CapabilityReport.Status);
				Assert.AreEqual(CollectionMemberRequirement.Optional, sealedNormalized.Manifest.Members.Single().Requirement);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static byte[] BuildEditableManifestBytes()
		{
			return Encoding.UTF8.GetBytes("{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"Editable source\",\"description\":\"Source\",\"domainName\":\"skyrim\"}," +
				"\"mods\":[" +
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}," +
				"{\"name\":\"Optional\",\"version\":\"1\",\"optional\":true,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21}}]," +
				"\"modRules\":[]}");
		}

		private static CollectionContentHash ComputeHash(byte[] bytes)
		{
			using (SHA256 sha = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", String.Empty).ToLowerInvariant());
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "NMM-Collections-WorkingCopyTests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}
	}
}
