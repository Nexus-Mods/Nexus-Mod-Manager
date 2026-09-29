using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionNexusPreferExactPolicyResolverTests
	{
		[Test]
		public void Resolve_PreferExactAvailableBecomesSupportedWithoutChangingArtifactChoice()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "prefer");
				int calls = 0;
				var resolver = new CollectionNexusPreferExactPolicyResolver(fixture.SourceStore, (domain, modId, fileId) =>
				{
					calls++;
					Assert.AreEqual("fallout4", domain);
					Assert.AreEqual(66566, modId);
					Assert.AreEqual(259831, fileId);
					return CollectionPreferExactResolutionStatus.ExactAvailable;
				});

				CollectionEffectiveSelection result = resolver.Resolve(fixture.Selection);

				Assert.AreEqual(1, calls);
				Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
				Assert.IsFalse(result.CapabilityReport.AllIssues.Any(x => x.Code == CollectionNexusPreferExactPolicyResolver.PreferIssueCode));
				Assert.AreSame(fixture.Selection.Manifest, result.Manifest);
				Assert.AreEqual(fixture.Selection.SelectionFingerprint, result.SelectionFingerprint);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Resolve_PreferFallbackRequiredRemainsActionRequired()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "prefer");
				var resolver = new CollectionNexusPreferExactPolicyResolver(fixture.SourceStore,
					(domain, modId, fileId) => CollectionPreferExactResolutionStatus.FallbackRequired);

				CollectionEffectiveSelection result = resolver.Resolve(fixture.Selection);

				Assert.AreEqual(CollectionCompatibilityStatus.ActionRequired, result.CapabilityReport.Status);
				Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == CollectionNexusPreferExactPolicyResolver.PreferIssueCode));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Resolve_LatestIsNeverSilentlyHandledByPreferExactResolver()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "latest");
				var resolver = new CollectionNexusPreferExactPolicyResolver(fixture.SourceStore,
					(domain, modId, fileId) =>
					{
						Assert.Fail("Latest must not invoke the Prefer Exact resolver.");
						return CollectionPreferExactResolutionStatus.Unknown;
					});

				CollectionEffectiveSelection result = resolver.Resolve(fixture.Selection);

				Assert.AreEqual(CollectionCompatibilityStatus.ActionRequired, result.CapabilityReport.Status);
				Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "member.source-policy-needs-resolution"));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void ReviewedExactPreferChoiceRehydratesWithoutProviderCall()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "prefer");
				NormalizedCollectionMember member = fixture.Selection.Manifest.Members.Single();
				var reviewed = new CollectionReviewedMemberSnapshot(member.IdentityResolution.Key, member.SourceOrdinal,
					member.Requirement, member.InstallationPhase, member.RecipeIdentity.Fingerprint,
					CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact, member.Artifact, member.Artifact, null);

				CollectionEffectiveSelection result = CollectionNexusPreferExactPolicyResolver.ReapplyReviewedExactChoices(
					fixture.Selection, fixture.RawManifest, new[] { reviewed });

				Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
				Assert.IsFalse(result.CapabilityReport.AllIssues.Any(x => x.Code == CollectionNexusPreferExactPolicyResolver.PreferIssueCode));
			}
			finally { Directory.Delete(root, true); }
		}

		private static Fixture CreateFixture(string root, string updatePolicy)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			CollectionIdentity collection = CollectionIdentity.FromNexus("55233");
			var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "791455", 11),
				"Module 02 - HUD", null, 1);
			new CollectionsCatalogStore(store).SaveDefinitionAndRevision(
				new CollectionDefinition(collection, "Module 02 - HUD", "Sephrajin", null), revision);

			string json = "{" +
				"\"info\":{\"author\":\"Sephrajin\",\"authorUrl\":\"https://example.invalid\",\"name\":\"Module 02 - HUD\",\"description\":\"HUD\",\"domainName\":\"fallout4\"}," +
				"\"mods\":[{\"name\":\"JHUD - FallUI HUD Preset\",\"version\":\"1.2\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":66566,\"fileId\":259831,\"updatePolicy\":\"" + updatePolicy + "\"}}]," +
				"\"modRules\":[]}";
			byte[] bytes = Encoding.UTF8.GetBytes(json);
			NexusCollectionManifestNormalizationResult normalized = new NexusCollectionManifestNormalizer().Normalize(bytes, revision);
			CollectionEffectiveSelection selection = new CollectionEffectiveSelectionBuilder().Build(normalized.CapabilityReport,
				new CollectionOptionalMemberSelection[0]);
			var sourceStore = new CollectionsRevisionSourceStore(store);
			sourceStore.RetainManifest(normalized.Manifest, CollectionRevisionSourceInputKind.RawManifest,
				normalized.Manifest.Source.ContentHash, bytes.LongLength, "collection.json", bytes);
			return new Fixture(sourceStore, selection, bytes);
		}

		private static string NewRoot()
		{
			return Path.Combine(Path.GetTempPath(), "nmm-c615-prefer-exact-" + Guid.NewGuid().ToString("N"));
		}

		private sealed class Fixture
		{
			public Fixture(CollectionsRevisionSourceStore sourceStore, CollectionEffectiveSelection selection, byte[] rawManifest)
			{
				SourceStore = sourceStore;
				Selection = selection;
				RawManifest = rawManifest;
			}
			public CollectionsRevisionSourceStore SourceStore { get; }
			public CollectionEffectiveSelection Selection { get; }
			public byte[] RawManifest { get; }
		}
	}
}
