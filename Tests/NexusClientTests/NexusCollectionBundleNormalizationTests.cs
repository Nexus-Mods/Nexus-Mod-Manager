using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies C2.5 bundle/collection.json normalization without any native install or game mutation.
	/// </summary>
	public class NexusCollectionBundleNormalizationTests
	{
		[Test]
		public void Normalize_SimpleExactNexusMembersProduceSupportedSelectedClosure()
		{
			string json = BuildManifest(
				"{\"name\":\"Required Mod\",\"version\":\"1.0\",\"optional\":false,\"domainName\":\"SkyrimSpecialEdition\",\"source\":{\"type\":\"nexus\",\"modId\":100,\"fileId\":200,\"updatePolicy\":\"exact\"}}," +
				"{\"name\":\"Optional Mod\",\"version\":\"2.0\",\"optional\":true,\"domainName\":\"SkyrimSpecialEdition\",\"source\":{\"type\":\"nexus\",\"modId\":101,\"fileId\":201}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 2);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(CollectionManifestMemberSetCompleteness.Complete, result.Manifest.MemberSetCompleteness);
			Assert.AreEqual(2, result.Manifest.Members.Count);

			NormalizedCollectionMember required = result.Manifest.Members[0];
			Assert.AreEqual(CollectionMemberRequirement.Required, required.Requirement);
			Assert.IsTrue(required.IsSelected);
			Assert.IsTrue(required.IdentityResolution.IsResolved);
			Assert.AreEqual(CollectionMemberKeyKind.ValidatedMatch, required.IdentityResolution.Key.Kind);
			Assert.AreEqual("nexus-mod-file:skyrimspecialedition/100/200", required.IdentityResolution.Key.Value);
			Assert.AreEqual("nexus-mod-file", required.Artifact.Scheme);
			Assert.AreEqual("skyrimspecialedition/100/200", required.Artifact.StableId);
			Assert.IsNotNull(required.RecipeIdentity);

			NormalizedCollectionMember optional = result.Manifest.Members[1];
			Assert.AreEqual(CollectionMemberRequirement.Optional, optional.Requirement);
			Assert.IsFalse(optional.IsSelected, "Remote optional members are visible but not silently selected by normalization.");
			Assert.IsFalse(optional.IsRequiredOmission);
		}

		[Test]
		public void Normalize_UnsupportedOptionalRemainsVisibleWithoutBlockingUntilSelectedLater()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":1,\"fileId\":2}}," +
				"{\"name\":\"Optional FOMOD\",\"version\":\"1\",\"optional\":true,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":3,\"fileId\":4},\"choices\":{\"step\":\"option\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 2);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.HasUnselectedUnsupportedOptionals);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.MemberReports[1].Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.installer-choices-unsupported"));
		}

		[Test]
		public void Normalize_UnsupportedRequiredBehaviorBlocksCurrentSelection()
		{
			string json = BuildManifest(
				"{\"name\":\"Required FOMOD\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":3,\"fileId\":4},\"choices\":{\"step\":\"option\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.installer-choices-unsupported"));
		}

		[Test]
		public void Normalize_DuplicateExactNexusFileIdentitiesRemainAmbiguousInsteadOfUsingListIndex()
		{
			string json = BuildManifest(
				"{\"name\":\"First\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}," +
				"{\"name\":\"Second\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 2);

			Assert.AreEqual(CollectionCompatibilityStatus.ActionRequired, result.CapabilityReport.Status);
			Assert.AreEqual(CollectionMemberIdentityResolutionStatus.Ambiguous, result.Manifest.Members[0].IdentityResolution.Status);
			Assert.AreEqual(CollectionMemberIdentityResolutionStatus.Ambiguous, result.Manifest.Members[1].IdentityResolution.Status);
			Assert.IsNull(result.Manifest.Members[0].IdentityResolution.Key);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.identity-ambiguous"));
		}

		[Test]
		public void Normalize_LatestPolicyKeepsBaselineArtifactButRequiresConcreteResolution()
		{
			string json = BuildManifest(
				"{\"name\":\"Latest\",\"version\":\"1.0\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"updatePolicy\":\"latest\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.ActionRequired, result.CapabilityReport.Status);
			Assert.AreEqual("skyrim/10/20", result.Manifest.Members[0].Artifact.StableId);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.source-policy-needs-resolution"));
		}

		[Test]
		public void Normalize_ProviderDeclaredMemberCountMismatchRequiresReviewWithoutInventingMissingMembers()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 2);

			Assert.AreEqual(CollectionManifestMemberSetCompleteness.Complete, result.Manifest.MemberSetCompleteness,
				"The descriptive provider count must not manufacture an unknown member or redefine manifest completeness.");
			Assert.AreEqual(1, result.Manifest.Members.Count);
			Assert.AreEqual(CollectionCompatibilityStatus.ActionRequired, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(issue => issue.Code == "manifest.declared-member-count-mismatch"));
		}

		[Test]
		public void Normalize_ModRulesAreWholeManifestUnsupportedUntilTypedDependencyPlannerExists()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[{\"source\":{},\"type\":\"after\",\"reference\":{}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			CollectionCapabilityIssue issue = result.CapabilityReport.ManifestIssues.Single(x => x.Code == "manifest.mod-rules-unsupported");
			Assert.AreEqual("$.modRules", issue.FieldPath);
		}

		[Test]
		public void Normalize_UnknownBehavioralFieldIsNotSilentlyAssumedCosmetic()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20},\"futureBehavior\":{\"enabled\":true}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			CollectionCapabilityIssue issue = result.CapabilityReport.AllIssues.Single(x => x.Code == "member.unknown-field");
			Assert.AreEqual("$.mods[0].futureBehavior", issue.FieldPath);
		}

		[Test]
		public void Normalize_MalformedNexusIdentityIsUnsupportedRatherThanGuessed()
		{
			string json = BuildManifest(
				"{\"name\":\"Bad\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim/escape\",\"source\":{\"type\":\"nexus\",\"modId\":10.5,\"fileId\":20}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsNull(result.Manifest.Members[0].Artifact);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.nexus-source-invalid"));
		}

		[Test]
		public void Normalize_TrailingJsonContentIsRejectedAsOneMalformedManifest()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]") + " {}";

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsFalse(result.Manifest.IsMemberSetComplete);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(issue => issue.Code == "manifest.json-invalid"));
		}

		[Test]
		public void Normalize_InvalidJsonProducesPreviewableUnsupportedIncompleteManifest()
		{
			byte[] bytes = Encoding.UTF8.GetBytes("{not-json");
			NexusCollectionManifestNormalizationResult result = new NexusCollectionManifestNormalizer().Normalize(bytes, CreateRevision(0));

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsFalse(result.Manifest.IsMemberSetComplete);
			Assert.AreEqual(0, result.Manifest.Members.Count);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(issue => issue.Code == "manifest.json-invalid"));
			Assert.AreEqual(NexusCollectionManifestNormalizer.SchemaIdentity, result.Manifest.Source.SchemaIdentity);
		}

		[Test]
		public void Normalize_RecipeFingerprintIsIndependentFromJsonObjectPropertyOrder()
		{
			string first = BuildManifest(
				"{\"name\":\"A\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]");
			string second = BuildManifest(
				"{\"source\":{\"fileId\":20,\"modId\":10,\"type\":\"nexus\"},\"domainName\":\"skyrim\",\"optional\":false,\"version\":\"1\",\"name\":\"A\"}",
				"[]");

			NexusCollectionManifestNormalizationResult firstResult = Normalize(first, 1);
			NexusCollectionManifestNormalizationResult secondResult = Normalize(second, 1);

			Assert.AreEqual(firstResult.Manifest.Members[0].RecipeIdentity, secondResult.Manifest.Members[0].RecipeIdentity);
			Assert.AreNotEqual(firstResult.Manifest.Source.ContentHash, secondResult.Manifest.Source.ContentHash,
				"Raw source provenance must still distinguish byte-different manifests.");
		}

		[Test]
		public void Normalize_RetainsExactRawManifestBytesByDefensiveCopy()
		{
			byte[] source = Encoding.UTF8.GetBytes(BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]"));
			byte expectedFirst = source[0];
			NexusCollectionManifestNormalizationResult result = new NexusCollectionManifestNormalizer().Normalize(source, CreateRevision(1));

			source[0] = (byte)'X';
			byte[] retained = result.GetRawManifestBytes();
			Assert.AreEqual(expectedFirst, retained[0]);
			retained[0] = (byte)'Y';
			Assert.AreEqual(expectedFirst, result.GetRawManifestBytes()[0]);
			Assert.AreEqual(source.LongLength, result.Manifest.Source.ByteLength);
		}

		[Test]
		public void ImportFile_RawCollectionJsonRecordsOuterAndManifestIntegrityWithoutSourcePathIdentity()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-c25-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			string path = Path.Combine(directory, "collection.json");
			try
			{
				File.WriteAllText(path, BuildManifest(
					"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
					"[]"), new UTF8Encoding(false));

				NexusCollectionBundleImportResult result = new NexusCollectionBundleImporter().ImportFile(path, CreateRevision(1));

				Assert.AreEqual(NexusCollectionBundleInputKind.RawManifest, result.InputKind);
				Assert.AreEqual("collection.json", result.ManifestEntryName);
				Assert.AreEqual(result.BundleContentHash, result.Manifest.Source.ContentHash);
				Assert.AreEqual(result.BundleByteLength, result.Manifest.Source.ByteLength);
				Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
				Assert.IsNull(typeof(NexusCollectionBundleImportResult).GetProperty("SourcePath"));
			}
			finally
			{
				if (Directory.Exists(directory))
					Directory.Delete(directory, true);
			}
		}

		private static NexusCollectionManifestNormalizationResult Normalize(string json, int declaredMemberCount)
		{
			return new NexusCollectionManifestNormalizer().Normalize(Encoding.UTF8.GetBytes(json), CreateRevision(declaredMemberCount));
		}

		private static CollectionRevision CreateRevision(int declaredMemberCount)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("2210");
			return new CollectionRevision(
				CollectionRevisionIdentity.FromNexus(collection, "772530", 100),
				null,
				null,
				declaredMemberCount);
		}

		private static string BuildManifest(string members, string modRules)
		{
			return "{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"Example\",\"description\":\"Example collection\",\"domainName\":\"skyrim\"}," +
				"\"mods\":[" + members + "]," +
				"\"modRules\":" + modRules +
				"}";
		}
	}
}
