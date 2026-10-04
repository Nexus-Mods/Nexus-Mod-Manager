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
	/// Verifies bundle/collection.json normalization without any native install or game mutation.
	/// </summary>
	public class NexusCollectionBundleNormalizationTests
	{
		[Test]
		public void Normalize_SimpleExactNexusMembersProduceSupportedSelectedClosure()
		{
			string json = BuildManifest(
				"{\"name\":\"Required Mod\",\"version\":\"1.0\",\"optional\":false,\"domainName\":\"SkyrimSpecialEdition\",\"source\":{\"type\":\"nexus\",\"modId\":100,\"fileId\":200,\"updatePolicy\":\"exact\"}}," +
				"{\"name\":\"Optional Mod\",\"version\":\"2.0\",\"optional\":true,\"domainName\":\"SkyrimSpecialEdition\",\"phase\":3,\"source\":{\"type\":\"nexus\",\"modId\":101,\"fileId\":201}}",
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
			Assert.IsFalse(required.RequiresGameRootInstall);
			Assert.AreEqual(CollectionMemberInstallRootBehavior.Default, required.InstallRootBehavior);

			NormalizedCollectionMember optional = result.Manifest.Members[1];
			Assert.AreEqual(CollectionMemberRequirement.Optional, optional.Requirement);
			Assert.AreEqual(666, optional.InstallationPhase, "Vortex optionals use the dedicated trailing install phase.");
			Assert.IsFalse(optional.IsSelected, "Remote optional members are visible but not silently selected by normalization.");
			Assert.IsFalse(optional.IsRequiredOmission);
		}

		[Test]
		public void Normalize_EmptyToolsPluginRulesAndKnownCollectionConfigAreSupported()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"tools\":[],\"pluginRules\":{\"plugins\":[],\"groups\":[]}," +
				"\"collectionConfig\":{\"recommendNewProfile\":true,\"referenceTagScheme\":\"v1\"}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.IsFalse(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "manifest.unknown-field"));
			Assert.IsFalse(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "manifest.collection-config-unsupported"));
			Assert.IsFalse(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "manifest.plugin-rules-unsupported"));
		}

		[Test]
		public void Normalize_NonEmptyToolsRemainFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"tools\":[{\"name\":\"Example tool\"}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			CollectionCapabilityIssue issue = result.CapabilityReport.ManifestIssues.Single(x => x.Code == "manifest.tools-unsupported");
			Assert.AreEqual("$.tools", issue.FieldPath);
		}

		[Test]
		public void Normalize_MalformedToolsRemainFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"tools\":{}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.tools-invalid"));
		}

		[Test]
		public void Normalize_PluginStateDeclarationsAreCharacterizedWithoutPluginRules()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"plugins\":[{\"name\":\"A.esp\",\"enabled\":true},{\"name\":\"B.esl\",\"enabled\":false}]," +
				"\"pluginRules\":{\"plugins\":[],\"groups\":[]}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.IsTrue(result.Manifest.HasPluginStateSection);
			Assert.AreEqual(2, result.Manifest.PluginStates.Count);
			Assert.AreEqual("A.esp", result.Manifest.PluginStates[0].PluginName);
			Assert.IsTrue(result.Manifest.PluginStates[0].Enabled);
			Assert.IsFalse(result.Manifest.PluginStates[1].Enabled);
		}

		[Test]
		public void Normalize_AbsentPluginStateSectionDoesNotCreateImplicitDisableOverlay()
		{
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}", "[]"), 1);

			Assert.IsFalse(result.Manifest.HasPluginStateSection);
			Assert.AreEqual(0, result.Manifest.PluginStates.Count);
		}

		[Test]
		public void Normalize_ExplicitNullPluginStateSectionRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]", "\"plugins\":null");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.plugins-invalid"));
		}

		[TestCase("{\"name\":\"A.esp\",\"enabled\":\"yes\"}", "manifest.plugins-invalid")]
		[TestCase("{\"name\":\"A.esp\",\"enabled\":null}", "manifest.plugins-invalid")]
		[TestCase("{\"name\":\" A.esp \",\"enabled\":true}", "manifest.plugins-invalid")]
		[TestCase("{\"name\":\"folder/A.esp\",\"enabled\":true}", "manifest.plugins-invalid")]
		[TestCase("{\"name\":\"A.esp\",\"enabled\":true,\"future\":1}", "manifest.plugins-field-unsupported")]
		public void Normalize_MalformedOrExtendedPluginStateRemainsFailClosed(string plugin, string expectedCode)
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]", "\"plugins\":[" + plugin + "]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == expectedCode));
		}

		[Test]
		public void Normalize_DuplicatePluginNamesRemainFailClosedCaseInsensitively()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]", "\"plugins\":[{\"name\":\"A.esp\",\"enabled\":true},{\"name\":\"a.ESP\",\"enabled\":false}]");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.plugins-duplicate"));
		}

		[Test]
		public void Normalize_PluginAfterRuleIsCharacterized()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"A.esp\",\"after\":[\"B.esm\"]}],\"groups\":[]}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.PluginRelativeOrderRules.Count);
			Assert.AreEqual("A.esp", result.Manifest.PluginRelativeOrderRules[0].PluginName);
			Assert.AreEqual("B.esm", result.Manifest.PluginRelativeOrderRules[0].AfterPluginName);
		}

		[Test]
		public void Normalize_PluginGroupAssignmentRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"A.esp\",\"group\":\"Late Loaders\"}],\"groups\":[]}");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.plugin-group-assignment-unsupported"));
		}

		[Test]
		public void Normalize_NonEmptyPluginGroupsRemainFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":{\"plugins\":[],\"groups\":[{\"name\":\"Late\",\"after\":[\"Default\"]}]}");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.plugin-groups-unsupported"));
		}

		[Test]
		public void Normalize_PluginAfterRuleCycleRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"A.esp\",\"after\":[\"B.esp\"]},{\"name\":\"B.esp\",\"after\":[\"A.esp\"]}],\"groups\":[]}");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.plugin-rule-cycle"));
		}

		[TestCase("{\"name\":\"A.esp\",\"after\":\"B.esp\"}", "manifest.plugin-rules-invalid")]
		[TestCase("{\"name\":\"A.esp\",\"after\":[{\"name\":\"B.esp\"}]}", "manifest.plugin-rules-reference-unsupported")]
		[TestCase("{\"name\":\"A.esp\",\"after\":[\"A.esp\"]}", "manifest.plugin-rules-self-reference")]
		[TestCase("{\"name\":\"A.esp\",\"future\":true}", "manifest.plugin-rules-field-unsupported")]
		public void Normalize_MalformedOrExtendedPluginAfterRuleRemainsFailClosed(string pluginRule, string expectedCode)
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]", "\"pluginRules\":{\"plugins\":[" + pluginRule + "],\"groups\":[]}");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == expectedCode));
		}

		[Test]
		public void Normalize_MalformedPluginRulesRemainFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			CollectionCapabilityIssue issue = result.CapabilityReport.ManifestIssues.Single(x => x.Code == "manifest.plugin-rules-invalid");
			Assert.AreEqual("$.pluginRules", issue.FieldPath);
		}

		[Test]
		public void Normalize_UncharacterizedPluginRuleFieldRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":{\"plugins\":[],\"groups\":[],\"futureRules\":[]}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			CollectionCapabilityIssue issue = result.CapabilityReport.ManifestIssues.Single(x => x.Code == "manifest.plugin-rules-field-unsupported");
			Assert.AreEqual("$.pluginRules.futureRules", issue.FieldPath);
		}

		[Test]
		public void Normalize_UncharacterizedCollectionConfigFieldRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"collectionConfig\":{\"recommendNewProfile\":true,\"referenceTagScheme\":\"v1\",\"futureBehavior\":true}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			CollectionCapabilityIssue issue = result.CapabilityReport.ManifestIssues.Single(x => x.Code == "manifest.collection-config-field-unsupported");
			Assert.AreEqual("$.collectionConfig.futureBehavior", issue.FieldPath);
		}

		[Test]
		public void Normalize_ExcludePluginRulesIsValidatedAsExportTimeConfiguration()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"collectionConfig\":{\"recommendNewProfile\":false,\"excludePluginRules\":false,\"referenceTagScheme\":\"v1\"}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.IsFalse(result.CapabilityReport.ManifestIssues.Any(x => x.FieldPath == "$.collectionConfig.excludePluginRules"));
		}

		[Test]
		public void Normalize_InvalidExcludePluginRulesRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"collectionConfig\":{\"excludePluginRules\":\"no\"}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x =>
				x.Code == "manifest.collection-config-invalid" && x.FieldPath == "$.collectionConfig.excludePluginRules"));
		}

		[Test]
		public void Normalize_UnknownReferenceTagSchemeRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"collectionConfig\":{\"referenceTagScheme\":\"v2\"}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.reference-tag-scheme-unsupported"));
		}

		[Test]
		public void Normalize_InvalidKnownCollectionConfigValueRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"collectionConfig\":{\"recommendNewProfile\":\"yes\",\"referenceTagScheme\":\"v1\"}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			CollectionCapabilityIssue issue = result.CapabilityReport.ManifestIssues.Single(x =>
				x.Code == "manifest.collection-config-invalid" && x.FieldPath == "$.collectionConfig.recommendNewProfile");
			Assert.IsNotNull(issue);
		}

		[Test]
		public void Normalize_ManualExactSourceWithMd5AndSizeProducesSupportedExternalArtifact()
		{
			string json = BuildManifest(
				"{\"name\":\"Manual\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"manual\",\"md5\":\"0123456789ABCDEF0123456789ABCDEF\",\"fileSize\":123,\"instructions\":\"Choose the curator archive\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			NormalizedCollectionMember member = result.Manifest.Members.Single();
			Assert.AreEqual("vortex-external-file", member.Artifact.Scheme);
			Assert.AreEqual("0123456789abcdef0123456789abcdef/123", member.Artifact.StableId);
			Assert.AreEqual("vortex-external-file:0123456789abcdef0123456789abcdef/123", member.IdentityResolution.Key.Value);
		}

		[Test]
		public void Normalize_BrowseExactSourceRequiresSafePageAndKeepsUrlOutOfArtifactIdentity()
		{
			string json = BuildManifest(
				"{\"name\":\"Browse\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"browse\",\"url\":\"https://mods.example.invalid/download-page\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"fileSize\":456,\"updatePolicy\":\"exact\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/456", result.Manifest.Members.Single().Artifact.StableId);
			Assert.That(result.Manifest.Members.Single().Artifact.StableId, Does.Not.Contain("example.invalid"));
		}

		[TestCase("{\"type\":\"manual\",\"fileSize\":10}", "member.external-source-identity-required")]
		[TestCase("{\"type\":\"browse\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"fileSize\":10}", "member.browse-source-url-required")]
		[TestCase("{\"type\":\"browse\",\"url\":\"file:///tmp/mod.zip\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"fileSize\":10}", "member.browse-source-url-required")]
		[TestCase("{\"type\":\"direct\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"fileSize\":10}", "member.direct-source-url-required")]
		[TestCase("{\"type\":\"direct\",\"url\":\"http://example.invalid/mod.zip\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"fileSize\":10}", "member.direct-source-url-insecure")]
		[TestCase("{\"type\":\"direct\",\"url\":\"https://user:pass@example.invalid/mod.zip\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"fileSize\":10}", "member.direct-source-url-insecure")]
		[TestCase("{\"type\":\"direct\",\"url\":\"https://example.invalid/mod.zip#fragment\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"fileSize\":10}", "member.direct-source-url-insecure")]
		[TestCase("{\"type\":\"manual\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"fileSize\":10,\"updatePolicy\":\"latest\"}", "member.external-update-policy-unsupported")]
		[TestCase("{\"type\":\"direct\",\"url\":\"https://example.invalid/mod.zip\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"fileSize\":10,\"updatePolicy\":\"prefer\"}", "member.external-update-policy-unsupported")]
		public void Normalize_UncharacterizedExternalSourceSemanticsRemainFailClosed(string source, string expectedCode)
		{
			string json = BuildManifest(
				"{\"name\":\"External\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":" + source + "}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.That(result.CapabilityReport.AllIssues.Any(x => x.Code == expectedCode), Is.True);
		}

		[Test]
		public void Normalize_DirectHttpsExactSourceProducesSupportedExternalArtifact()
		{
			string json = BuildManifest(
				"{\"name\":\"Direct\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"direct\",\"url\":\"https://example.invalid/mod.zip?token=temporary\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"fileSize\":10,\"updatePolicy\":\"exact\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(CollectionExternalArtifactIdentity.Scheme, result.Manifest.Members.Single().Artifact.Scheme);
			Assert.AreEqual("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/10", result.Manifest.Members.Single().Artifact.StableId);
			Assert.That(result.Manifest.Members.Single().Artifact.StableId, Does.Not.Contain("token"));
		}

		[Test]
		public void Normalize_ExactBundleSourceProducesRevisionScopedStableArtifactIdentity()
		{
			string json = BuildManifest(
				"{\"name\":\"Bundled\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"bundle\",\"fileSize\":8192,\"updatePolicy\":\"exact\",\"fileExpression\":\"Bundled - Local Tool (v1)\",\"tag\":\"bundleTag_1\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			NormalizedCollectionMember member = result.Manifest.Members.Single();
			Assert.IsNotNull(member.Artifact);
			Assert.AreEqual("collection-bundle", member.Artifact.Scheme);
			StringAssert.EndsWith("/bundleTag_1", member.Artifact.StableId);
			Assert.AreEqual("collection-bundle:" + member.Artifact.StableId, member.IdentityResolution.Key.Value);
			Assert.IsNull(member.Artifact.ExpectedContentHash, "Vortex recompresses embedded bundle directories, so logical identity must not invent a stable source archive hash.");
		}

		[Test]
		public void Normalize_SameBundleTagInDifferentRevisionGetsDifferentStableIdentity()
		{
			string json = BuildManifest(
				"{\"name\":\"Bundled\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"bundle\",\"fileSize\":10,\"fileExpression\":\"Bundled - A\",\"tag\":\"sameTag\"}}",
				"[]");
			CollectionRevision first = CreateRevision(1);
			CollectionIdentity collection = first.Identity.Collection;
			CollectionRevision second = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "different-revision", 101), null, null, 1);

			NormalizedCollectionMember firstMember = new NexusCollectionManifestNormalizer().Normalize(Encoding.UTF8.GetBytes(json), first).Manifest.Members.Single();
			NormalizedCollectionMember secondMember = new NexusCollectionManifestNormalizer().Normalize(Encoding.UTF8.GetBytes(json), second).Manifest.Members.Single();

			Assert.AreNotEqual(firstMember.Artifact.StableId, secondMember.Artifact.StableId);
		}

		[TestCase("\"tag\":\"bad/tag\",\"fileExpression\":\"Bundled - A\",\"fileSize\":10", "member.bundle-tag-invalid")]
		[TestCase("\"tag\":\"tagA\",\"fileExpression\":\"../escape\",\"fileSize\":10", "member.bundle-path-invalid")]
		[TestCase("\"tag\":\"tagA\",\"fileExpression\":\"Bundled - A\",\"fileSize\":0", "member.bundle-size-invalid")]
		[TestCase("\"tag\":\"tagA\",\"fileExpression\":\"Bundled - A\",\"fileSize\":10.5", "member.bundle-size-invalid")]
		public void Normalize_MalformedBundleIdentityOrBoundsRemainFailClosed(string sourceFields, string expectedCode)
		{
			string json = BuildManifest(
				"{\"name\":\"Bundled\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"bundle\"," + sourceFields + "}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == expectedCode));
		}

		[Test]
		public void Normalize_BundleLatestPolicyAndNexusNarrowingFieldsRemainFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Bundled\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"bundle\",\"fileSize\":10,\"fileExpression\":\"Bundled - A\",\"tag\":\"tagA\",\"updatePolicy\":\"latest\",\"modId\":123}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "member.bundle-update-policy-unsupported"));
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "member.bundle-source-semantics-unsupported"));
		}

		[Test]
		public void Normalize_BundleWithDinputUsesNativeGameRootInstallRequirement()
		{
			string json = BuildManifest(
				"{\"name\":\"Bundled DInput\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"bundle\",\"fileSize\":8192,\"fileExpression\":\"Bundled - DInput\",\"tag\":\"dinputTag\"},\"details\":{\"type\":\"dinput\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			NormalizedCollectionMember member = result.Manifest.Members.Single();

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.IsNotNull(member.Artifact);
			Assert.IsTrue(member.IdentityResolution.IsResolved);
			Assert.IsTrue(member.RequiresGameRootInstall);
			Assert.AreEqual(CollectionMemberInstallRootBehavior.VortexDInputGameRoot, member.InstallRootBehavior);
			Assert.IsFalse(result.CapabilityReport.AllIssues.Any(x => x.Code == "member.mod-type-unsupported"));
			Assert.IsFalse(result.CapabilityReport.AllIssues.Any(x => x.Code == "member.source-type-unsupported"));
		}

		[Test]
		public void Normalize_EnbUsesNativeGameRootInstallRequirement()
		{
			string json = BuildManifest(
				"{\"name\":\"ENB preset\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20},\"details\":{\"type\":\"enb\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.IsTrue(result.Manifest.Members.Single().RequiresGameRootInstall);
			Assert.AreEqual(CollectionMemberInstallRootBehavior.VortexEnbGameRoot, result.Manifest.Members.Single().InstallRootBehavior);
		}

		[Test]
		public void Normalize_UnknownVortexModTypeRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Future type\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20},\"details\":{\"type\":\"future-root-type\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsFalse(result.Manifest.Members.Single().RequiresGameRootInstall);
			Assert.AreEqual(CollectionMemberInstallRootBehavior.Default, result.Manifest.Members.Single().InstallRootBehavior);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "member.mod-type-unsupported"));
		}

		[Test]
		public void Normalize_VortexMd5FileListBecomesTypedSupportedRecipe()
		{
			string json = BuildManifest(
				"{\"name\":\"Replicated\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"hashes\":[{\"path\":\"textures/body.dds\",\"md5\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}]}", "[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			NormalizedCollectionMember member = result.Manifest.Members.Single();
			Assert.IsTrue(member.HasVortexFileList);
			Assert.AreEqual(@"textures\body.dds", member.VortexFileList.Items[0].DestinationPath);
			Assert.AreEqual("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", member.VortexFileList.Items[0].ContentMd5);
		}

		[Test]
		public void Normalize_VortexXxh64FileListBecomesTypedSupportedRecipe()
		{
			string json = BuildManifest(
				"{\"name\":\"Replicated\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"hashes\":[{\"path\":\"textures/body.dds\",\"xxh64\":\"RLws9a13CZk=\"}]}", "[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			CollectionVortexFileList list = result.Manifest.Members.Single().VortexFileList;
			Assert.AreEqual(CollectionVortexFileListHashAlgorithm.Xxh64, list.HashAlgorithm);
			Assert.AreEqual(@"textures\body.dds", list.Items[0].DestinationPath);
			Assert.AreEqual("RLws9a13CZk=", list.Items[0].ContentXxh64);
			Assert.IsNull(list.Items[0].ContentMd5);
		}

		[Test]
		public void Normalize_VortexFileListWithAnyMd5UsesMd5ModeForWholeList()
		{
			string json = BuildManifest(
				"{\"name\":\"Replicated\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"hashes\":[{\"path\":\"one.bin\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"xxh64\":\"RLws9a13CZk=\"}," +
				"{\"path\":\"two.bin\",\"md5\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"}]}", "[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(CollectionVortexFileListHashAlgorithm.Md5, result.Manifest.Members.Single().VortexFileList.HashAlgorithm);
		}

		[Test]
		public void Normalize_VortexMixedMd5AndXxh64OnlyEntriesFailsClosedLikeVortexMd5Mode()
		{
			string json = BuildManifest(
				"{\"name\":\"Replicated\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"hashes\":[{\"path\":\"one.bin\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}," +
				"{\"path\":\"two.bin\",\"xxh64\":\"RLws9a13CZk=\"}]}", "[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.file-list-invalid"));
		}

		[Test]
		public void Normalize_VortexFileListWithUnsafeOrDuplicateDestinationRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Replicated\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"hashes\":[{\"path\":\"../escape.dds\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"},{\"path\":\"same.dds\",\"md5\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"},{\"path\":\"SAME.dds\",\"md5\":\"cccccccccccccccccccccccccccccccc\"}]}", "[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.file-list-invalid"));
		}

		[Test]
		public void Normalize_EmptyVortexFileListPreservesOrdinaryBasicInstallPath()
		{
			string json = BuildManifest(
				"{\"name\":\"Basic\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20},\"hashes\":[]}", "[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.IsFalse(result.Manifest.Members.Single().HasVortexFileList);
		}

		[Test]
		public void Normalize_VortexFileOverridesBecomeTypedSupportedDeploymentBlacklist()
		{
			string json = BuildManifest(
				"{\"name\":\"Override\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"fileOverrides\":[\"C:/Curator/Skyrim/Data/textures/body.dds\",\"meshes/body.nif\"]}", "[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			NormalizedCollectionMember member = result.Manifest.Members.Single();
			Assert.IsTrue(member.HasVortexFileOverrides);
			CollectionAssert.AreEquivalent(new[] { @"C:\Curator\Skyrim\Data\textures\body.dds", @"meshes\body.nif" },
				member.VortexFileOverrides.Paths);
			Assert.IsFalse(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.file-overrides-unsupported"));
		}

		[Test]
		public void Normalize_InvalidOrDuplicateVortexFileOverridesRemainFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Override\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"fileOverrides\":[\"Data/same.dds\",\"data/same.dds\"]}", "[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.file-overrides-invalid"));
		}

		[Test]
		public void Normalize_ExactVortexFomodChoicesBecomeTypedSupportedSelection()
		{
			string json = BuildManifest(
				"{\"name\":\"Map\",\"version\":\"2.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"choices\":{\"type\":\"fomod\",\"options\":[{\"name\":\"Install Map with Locations\",\"groups\":[{\"name\":\"Map with All Locations\",\"choices\":[{\"name\":\"4k With All Locations\",\"idx\":0}]}]}]}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			NormalizedCollectionMember member = result.Manifest.Members.Single();
			Assert.IsTrue(member.HasVortexFomodSelection);
			Assert.AreEqual("Install Map with Locations", member.VortexFomodSelection.Steps[0].Name);
			Assert.AreEqual("Map with All Locations", member.VortexFomodSelection.Steps[0].Groups[0].Name);
			Assert.AreEqual(0, member.VortexFomodSelection.Steps[0].Groups[0].Choices[0].Index);
			Assert.AreEqual("4k With All Locations", member.VortexFomodSelection.Steps[0].Groups[0].Choices[0].Name);
		}

		[Test]
		public void Normalize_EmptyVortexFomodGroupChoiceArrayIsRetainedAsExplicitNoSelection()
		{
			string json = BuildManifest(
				"{\"name\":\"Map\",\"version\":\"2.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"choices\":{\"type\":\"fomod\",\"options\":[{\"name\":\"Step\",\"groups\":[{\"name\":\"Optional\",\"choices\":[]}]}]}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(0, result.Manifest.Members.Single().VortexFomodSelection.Steps[0].Groups[0].Choices.Count);
		}

		[TestCase("future", "member.installer-choices-type-unsupported")]
		[TestCase("fomod", "member.installer-choices-invalid")]
		public void Normalize_UncharacterizedOrMalformedVortexFomodChoicesRemainFailClosed(string type, string expectedCode)
		{
			string choices = type == "future"
				? "{\"type\":\"future\",\"options\":[]}"
				: "{\"type\":\"fomod\",\"options\":[{\"name\":\"Step\",\"groups\":[{\"name\":\"Group\",\"choices\":[{\"name\":\"A\",\"idx\":0},{\"name\":\"B\",\"idx\":0}]}]}]}";
			string json = BuildManifest(
				"{\"name\":\"Map\",\"version\":\"2.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20},\"choices\":" + choices + "}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == expectedCode));
		}

		[Test]
		public void Normalize_UnknownVortexFomodChoiceFieldRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Map\",\"version\":\"2.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"choices\":{\"type\":\"fomod\",\"options\":[{\"name\":\"Step\",\"groups\":[{\"name\":\"Group\",\"choices\":[{\"name\":\"A\",\"idx\":0,\"future\":true}]}]}]}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.unknown-field" &&
				issue.FieldPath.EndsWith(".choices[0].future", StringComparison.Ordinal)));
		}

		[Test]
		public void Normalize_VortexFomodChoicesCombinedWithGameRootModTypeRemainFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Map\",\"version\":\"2.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"details\":{\"type\":\"dinput\"},\"choices\":{\"type\":\"fomod\",\"options\":[{\"name\":\"Step\",\"groups\":[{\"name\":\"Group\",\"choices\":[]}]}]}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue =>
				issue.Code == "member.fomod-mod-type-combination-unsupported"));
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
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.installer-choices-type-unsupported"));
		}

		[Test]
		public void Normalize_UnsupportedRequiredBehaviorBlocksCurrentSelection()
		{
			string json = BuildManifest(
				"{\"name\":\"Required FOMOD\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":3,\"fileId\":4},\"choices\":{\"step\":\"option\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.installer-choices-type-unsupported"));
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
		public void Normalize_BeforeAfterModRulesBecomeExactFilePriorityEdges()
		{
			string json = BuildManifest(
				"{\"name\":\"A\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}," +
				"{\"name\":\"B\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21}}",
				"[{\"source\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrim\",\"modId\":\"10\",\"fileId\":\"20\"}},\"type\":\"before\",\"reference\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrim\",\"modId\":\"11\",\"fileId\":\"21\"}}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 2);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.FilePriorityRules.Count);
			Assert.AreEqual(result.Manifest.Members[0].IdentityResolution.Key, result.Manifest.FilePriorityRules[0].LowerPriorityMemberKey);
			Assert.AreEqual(result.Manifest.Members[1].IdentityResolution.Key, result.Manifest.FilePriorityRules[0].HigherPriorityMemberKey);
		}

		[Test]
		public void Normalize_VortexStyleExactReferenceFieldsResolveToMembers()
		{
			string json = BuildManifest(
				"{\"name\":\"A\",\"version\":\"1.0.0\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"hash-a\",\"logicalFilename\":\"A File\"}}," +
				"{\"name\":\"B\",\"version\":\"2.0.0\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21,\"md5\":\"hash-b\",\"logicalFilename\":\"B File\"}}",
				"[{\"source\":{\"fileMD5\":\"hash-a\",\"logicalFileName\":\"A File\",\"versionMatch\":\"1.0.0\"},\"type\":\"before\",\"reference\":{\"fileMD5\":\"hash-b\",\"logicalFileName\":\"B File\",\"versionMatch\":\"2.0.0\"}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 2);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(result.Manifest.Members[0].IdentityResolution.Key, result.Manifest.FilePriorityRules.Single().LowerPriorityMemberKey);
			Assert.AreEqual(result.Manifest.Members[1].IdentityResolution.Key, result.Manifest.FilePriorityRules.Single().HigherPriorityMemberKey);
		}

		[Test]
		public void Normalize_FuzzyRangeModRuleReferenceFailsClosedInsteadOfGuessingVortexSemver()
		{
			string json = BuildManifest(
				"{\"name\":\"A\",\"version\":\"1.2.0\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"hash-a\"}}," +
				"{\"name\":\"B\",\"version\":\"2.0.0\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21,\"md5\":\"hash-b\"}}",
				"[{\"source\":{\"fileMD5\":\"hash-a\",\"versionMatch\":\">=1.0.0+prefer\"},\"type\":\"before\",\"reference\":{\"fileMD5\":\"hash-b\",\"versionMatch\":\"2.0.0\"}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 2);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-source-unresolved"));
		}

		[Test]
		public void Normalize_FixtureStyleF4seConflictRulesBecomeCompatibilityConstraints()
		{
			const string md5 = "a1d63c8f9d75c4d4e5633a24fc5e9b79";
			const string expression = "Fallout 4 Script Extender 42147 0.7.9 2026-08-18T14-42Z 2hTc9ppIs";
			string member = "{\"name\":\"Fallout 4 Script Extender\",\"version\":\"0.7.9\",\"optional\":false,\"domainName\":\"fallout4\"," +
				"\"source\":{\"type\":\"nexus\",\"modId\":42147,\"fileId\":407709,\"md5\":\"" + md5 + "\",\"logicalFilename\":\"Fallout 4 Script Extender\",\"updatePolicy\":\"exact\"}}";
			string source = "{\"fileExpression\":\"" + expression + "\",\"fileMD5\":\"" + md5 + "\",\"versionMatch\":\"*\",\"logicalFileName\":\"Fallout 4 Script Extender\"}";
			string rules = "[" +
				"{\"type\":\"conflicts\",\"reference\":{\"logicalFileName\":\"Fallout 4 Script Extender (F4SE)\",\"versionMatch\":\"<0.7.9 || >0.7.9\"},\"source\":" + source + "}," +
				"{\"type\":\"conflicts\",\"reference\":{\"logicalFileName\":\"Fallout 4 Script Extender (F4SE)\",\"versionMatch\":\"<0.7.7 || >0.7.7\"},\"source\":" + source + "}," +
				"{\"type\":\"conflicts\",\"reference\":" + source + ",\"source\":" + source + "}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rules), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(0, result.Manifest.FilePriorityRules.Count);
			Assert.AreEqual(3, result.Manifest.ConflictConstraints.Count);
			CollectionMemberKey memberKey = result.Manifest.Members.Single().IdentityResolution.Key;
			Assert.IsTrue(result.Manifest.ConflictConstraints.All(x => x.SourceMemberKey.Equals(memberKey)));
			CollectionAssert.AreEqual(new[] { "<0.7.9 || >0.7.9", "<0.7.7 || >0.7.7", "*" },
				result.Manifest.ConflictConstraints.Select(x => x.Reference.VersionMatch.Expression).ToArray());
		}

		[Test]
		public void CharacterizedVortexVersionRange_MatchesFixtureComparatorOrSemantics()
		{
			CollectionVortexVersionMatch range;
			string failure;
			Assert.IsTrue(CollectionVortexVersionMatch.TryCreate("<0.7.9 || >0.7.9", out range, out failure), failure);

			Assert.AreEqual(CollectionVortexVersionMatchResult.Match, range.Evaluate("0.7.8"));
			Assert.AreEqual(CollectionVortexVersionMatchResult.NoMatch, range.Evaluate("0.7.9"));
			Assert.AreEqual(CollectionVortexVersionMatchResult.Match, range.Evaluate("0.8.0"));
		}

		[Test]
		public void Normalize_ConflictRangeOutsideCharacterizedOrSubsetFailsClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"A\",\"version\":\"1.2.0\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"hash-a\",\"logicalFilename\":\"A\"}}",
				"[{\"source\":{\"fileMD5\":\"hash-a\",\"logicalFileName\":\"A\",\"versionMatch\":\"*\"},\"type\":\"conflicts\",\"reference\":{\"logicalFileName\":\"Other\",\"versionMatch\":\">=1.0.0 <2.0.0\"}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.conflict-rule-reference-unsupported"));
		}

		[Test]
		public void Normalize_FuzzyConflictReferenceCannotUseMd5AsItsOnlyIdentityMarker()
		{
			string json = BuildManifest(
				"{\"name\":\"A\",\"version\":\"1.2.0\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"hash-a\",\"logicalFilename\":\"A\"}}",
				"[{\"source\":{\"fileMD5\":\"hash-a\",\"logicalFileName\":\"A\",\"versionMatch\":\"*\"},\"type\":\"conflicts\",\"reference\":{\"fileMD5\":\"other-md5\",\"versionMatch\":\"*\"}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.conflict-rule-reference-unsupported"));
		}

		[Test]
		public void Normalize_UncharacterizedModRuleTypeStillFailsClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[{\"source\":{\"tag\":\"a\"},\"type\":\"future-rule\",\"reference\":{\"tag\":\"b\"}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-type-unsupported"));
		}

		[Test]
		public void Normalize_NonZeroFinitePhaseIsSupportedAndRetainedAsSchedulingMetadata()
		{
			string phased = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"phase\":10.5,\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]");
			string defaultPhase = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]");
			string earlierPhase = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"phase\":-5.25,\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]");

			NexusCollectionManifestNormalizationResult phasedResult = Normalize(phased, 1);
			NexusCollectionManifestNormalizationResult defaultResult = Normalize(defaultPhase, 1);
			NexusCollectionManifestNormalizationResult earlierResult = Normalize(earlierPhase, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, phasedResult.CapabilityReport.Status);
			Assert.AreEqual(10.5d, phasedResult.Manifest.Members[0].InstallationPhase);
			Assert.AreEqual(0d, defaultResult.Manifest.Members[0].InstallationPhase);
			Assert.AreEqual(-5.25d, earlierResult.Manifest.Members[0].InstallationPhase);
			Assert.AreEqual(defaultResult.Manifest.Members[0].RecipeIdentity, phasedResult.Manifest.Members[0].RecipeIdentity,
				"Installation phase must not change installed-recipe identity.");
			Assert.AreEqual("nmm-ce.collections.normalizer/3", phasedResult.Manifest.Source.NormalizerVersion);
		}

		[TestCase("\"not-a-number\"")]
		[TestCase("true")]
		public void Normalize_InvalidPhaseFailsClosed(string phase)
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"phase\":" + phase + ",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "member.phase-invalid"));
		}


		[Test]
		public void Normalize_ExternalBeforeEndpointWithWildcardLogicalNameIsCharacterized()
		{
			string member = "{\"name\":\"ENB Helper for Fallout 4\",\"version\":\"1.0.2\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":56566,\"fileId\":123}}";
			string rules = "[{\"type\":\"before\",\"source\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrim\",\"modId\":56566,\"fileId\":123}},\"reference\":{\"logicalFileName\":\"NAC X Legacy edition\",\"versionMatch\":\"*\",\"idHint\":\"NAC X Legacy edition-46722-1-0-0-1596620706\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rules), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(0, result.Manifest.FilePriorityRules.Count);
			Assert.AreEqual(1, result.Manifest.ExternalFilePriorityRules.Count);
			CollectionExternalFilePriorityRule rule = result.Manifest.ExternalFilePriorityRules.Single();
			Assert.AreEqual(result.Manifest.Members.Single().IdentityResolution.Key, rule.MemberKey);
			Assert.IsTrue(rule.MemberIsLowerPriority);
			Assert.AreEqual("NAC X Legacy edition", rule.ExternalReference.LogicalFileName);
		}

		[Test]
		public void Normalize_FixtureStyleExternalBeforeSourceUsesExactMd5WhenGeneratedExpressionIsNotRetained()
		{
			const string md5 = "bbf65ccae4a6e350a8eb80c361ae702e";
			string member = "{\"name\":\"enbhelperf4\",\"version\":\"1.0.2\",\"optional\":true,\"domainName\":\"fallout4\"," +
				"\"source\":{\"type\":\"nexus\",\"modId\":56566,\"fileId\":268795,\"md5\":\"" + md5 + "\",\"logicalFilename\":\"ENB Helper for Fallout 4\",\"tag\":\"7qp1yhzkdXL\"}}";
			string source = "{\"fileExpression\":\"ENB Helper for Fallout 4-56566-1-0-2-1677522163\",\"fileMD5\":\"" + md5 + "\",\"versionMatch\":\"1.0.2\",\"logicalFileName\":\"ENB Helper for Fallout 4\"}";
			string rules = "[{\"type\":\"before\",\"source\":" + source + ",\"reference\":{\"logicalFileName\":\"NAC X Legacy edition\",\"versionMatch\":\"*\",\"idHint\":\"NAC X Legacy edition-46722-1-0-0-1596620706\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rules), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(0, result.Manifest.FilePriorityRules.Count);
			Assert.AreEqual(1, result.Manifest.ExternalFilePriorityRules.Count);
			Assert.AreEqual(result.Manifest.Members.Single().IdentityResolution.Key, result.Manifest.ExternalFilePriorityRules.Single().MemberKey);
			Assert.IsFalse(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-source-unresolved"));
		}

		[Test]
		public void Normalize_ExternalBeforeSourceGeneratedExpressionDoesNotBypassExactMd5Mismatch()
		{
			string member = "{\"name\":\"enbhelperf4\",\"version\":\"1.0.2\",\"optional\":true,\"domainName\":\"fallout4\"," +
				"\"source\":{\"type\":\"nexus\",\"modId\":56566,\"fileId\":268795,\"md5\":\"bbf65ccae4a6e350a8eb80c361ae702e\",\"logicalFilename\":\"ENB Helper for Fallout 4\"}}";
			string source = "{\"fileExpression\":\"ENB Helper for Fallout 4-56566-1-0-2-1677522163\",\"fileMD5\":\"00000000000000000000000000000000\",\"versionMatch\":\"1.0.2\",\"logicalFileName\":\"ENB Helper for Fallout 4\"}";
			string rules = "[{\"type\":\"before\",\"source\":" + source + ",\"reference\":{\"logicalFileName\":\"NAC X Legacy edition\",\"versionMatch\":\"*\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rules), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-source-unresolved"));
		}

		[Test]
		public void Normalize_ExternalBeforeEndpointWithExactTextVersionIsCharacterized()
		{
			string member = "{\"name\":\"FallUI - HUD\",\"version\":\"1.7.1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":51813,\"fileId\":257220,\"md5\":\"3871060641dc09aa27fe80442fe51d73\",\"logicalFilename\":\"FallUI - HUD\"}}";
			string rules = "[{\"type\":\"after\",\"source\":{\"fileExpression\":\"FallUI - HUD-51813-1-7-1-1668637124\",\"fileMD5\":\"3871060641dc09aa27fe80442fe51d73\",\"versionMatch\":\"1.7.1\",\"logicalFileName\":\"FallUI - HUD\"},\"reference\":{\"fileExpression\":\"HUDFramework 1.0f-20309-1-0f\",\"fileMD5\":\"058abd525c9651cbc9277d2ec54529c1\",\"versionMatch\":\"1.0f\",\"logicalFileName\":\"HUDFramework 1.0f\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rules), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.ExternalFilePriorityRules.Count);
			Assert.AreEqual("1.0f", result.Manifest.ExternalFilePriorityRules.Single().ExternalReference.VersionMatch.Expression);
		}

		[Test]
		public void Normalize_ExternalEndpointCanUseExactFileExpressionWithoutLogicalName()
		{
			string member = "{\"name\":\"Bundled settings\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"bundle\",\"fileSize\":10,\"fileExpression\":\"Module 02 settings\",\"tag\":\"settingsTag\"}}";
			string rules = "[{\"type\":\"after\",\"source\":{\"fileExpression\":\"Module 02 settings\",\"versionMatch\":\"*\",\"tag\":\"settingsTag\"},\"reference\":{\"fileExpression\":\"Bundled - Module 05 settings (v1)\",\"fileMD5\":\"eca4836dadca1fe0bebd622d8b1910d9\",\"versionMatch\":\"1.0.0\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rules), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.ExternalFilePriorityRules.Count);
			Assert.AreEqual("Bundled - Module 05 settings (v1)", result.Manifest.ExternalFilePriorityRules.Single().ExternalReference.FileExpression);
		}

		[Test]
		public void Normalize_GeneratedReferenceExpressionCanBindAnotherCollectionMemberByExactMd5()
		{
			const string hudMd5 = "3871060641dc09aa27fe80442fe51d73";
			string members =
				"{\"name\":\"FallUI - HUD\",\"version\":\"1.7.1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":51813,\"fileId\":257220,\"md5\":\"" + hudMd5 + "\",\"logicalFilename\":\"FallUI - HUD\"}}," +
				"{\"name\":\"Bundled settings\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"bundle\",\"fileSize\":10,\"fileExpression\":\"Bundled settings\",\"tag\":\"settingsTag\"}}";
			string rules = "[{\"type\":\"after\",\"source\":{\"fileExpression\":\"Bundled settings\",\"versionMatch\":\"*\",\"tag\":\"settingsTag\"},\"reference\":{\"fileExpression\":\"FallUI - HUD-51813-1-7-1-1668637124\",\"fileMD5\":\"" + hudMd5 + "\",\"versionMatch\":\"1.7.1\",\"logicalFileName\":\"FallUI - HUD\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 2);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.FilePriorityRules.Count);
			Assert.AreEqual(0, result.Manifest.ExternalFilePriorityRules.Count);
		}

		[Test]
		public void Normalize_GeneratedReferenceExpressionBindsEquivalentZeroPaddedNumericVersions()
		{
			string members =
				"{\"name\":\"Natural Landscapes 2K\",\"version\":\"0.5\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":38841,\"fileId\":231741,\"md5\":\"76e4ecfbd9308179311a69df08a93657\",\"logicalFilename\":\"Natural Landscapes 2K\"}}," +
				"{\"name\":\"A Forest 0.8\",\"version\":\"0.8\",\"optional\":true,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":45330,\"fileId\":211049,\"md5\":\"1831fe84b026ee2b6b75203574d400bb\",\"logicalFilename\":\"A Forest 0.8\"}}";
			string rules = "[{\"type\":\"before\",\"source\":{\"fileExpression\":\"Natural Landscapes 2K-38841-0-5-1646962452\",\"fileMD5\":\"76e4ecfbd9308179311a69df08a93657\",\"versionMatch\":\"0.5.0\",\"logicalFileName\":\"Natural Landscapes 2K\"},\"reference\":{\"fileExpression\":\"A Forest 0.8-45330-0-8-1624579585\",\"fileMD5\":\"1831fe84b026ee2b6b75203574d400bb\",\"versionMatch\":\"0.8.0\",\"logicalFileName\":\"A Forest 0.8\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 2);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.FilePriorityRules.Count);
			Assert.IsFalse(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-source-unresolved"));
			Assert.IsFalse(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-reference-unresolved"));
		}

		[Test]
		public void Normalize_EquivalentNumericVersionBindingDoesNotBroadenTextVersions()
		{
			string members =
				"{\"name\":\"Text Version\",\"version\":\"0.5b\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"logicalFilename\":\"Text Version\"}}," +
				"{\"name\":\"Other\",\"version\":\"1.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":30,\"fileId\":40,\"md5\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"logicalFilename\":\"Other\"}}";
			string rules = "[{\"type\":\"before\",\"source\":{\"fileExpression\":\"Text Version-10-0-5b\",\"fileMD5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"versionMatch\":\"0.5.0\",\"logicalFileName\":\"Text Version\"},\"reference\":{\"fileMD5\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"versionMatch\":\"1.0\",\"logicalFileName\":\"Other\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 2);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-source-unresolved"));
		}

		[Test]
		public void Normalize_Module02HudFixtureLeavesOnlyPreferExactPoliciesForProviderResolution()
		{
			string members =
				"{\"name\":\"FallUI - HUD\",\"version\":\"1.7.1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":51813,\"fileId\":257220,\"md5\":\"3871060641dc09aa27fe80442fe51d73\",\"logicalFilename\":\"FallUI - HUD\",\"updatePolicy\":\"exact\"}}," +
				"{\"name\":\"FallUI - Inventory\",\"version\":\"2.2.1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":48758,\"fileId\":254844,\"md5\":\"f51979f417a2ef1f38f21417d792cc3b\",\"logicalFilename\":\"FallUI - Inventory\",\"updatePolicy\":\"exact\"}}," +
				"{\"name\":\"JHUD - FallUI HUD Preset\",\"version\":\"1.2\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":66566,\"fileId\":259831,\"md5\":\"8a417efec9f24b843ef58b334c4dd914\",\"logicalFilename\":\"JHUD - FallUI HUD Preset\",\"updatePolicy\":\"prefer\"}}," +
				"{\"name\":\"FallUI - Sleep And Wait\",\"version\":\"1.4\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":49070,\"fileId\":256778,\"md5\":\"96f6441906b8174742789ee2be8593d4\",\"logicalFilename\":\"FallUI - Sleep And Wait\",\"updatePolicy\":\"prefer\"}}," +
				"{\"name\":\"Neko FallUI_HUD Preset\",\"version\":\"1.1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":71969,\"fileId\":279784,\"md5\":\"519fa511e7156bb81958abeed1c66143\",\"logicalFilename\":\"Neko FallUI_HUD Preset\",\"updatePolicy\":\"prefer\"}}," +
				"{\"name\":\"[Module 02 - Settings] FallUI - Minimal.zip\",\"version\":\"1.0.0\",\"optional\":true,\"domainName\":\"fallout4\",\"source\":{\"type\":\"bundle\",\"fileSize\":24576,\"updatePolicy\":\"exact\",\"fileExpression\":\"Bundled - [Module 02 - Settings] FallUI - Minimal.zip (v_63_)\",\"tag\":\"N_ykpPtEGz\"}}";
			string rules = "[" +
				"{\"type\":\"after\",\"source\":{\"fileExpression\":\"FallUI - HUD-51813-1-7-1-1668637124\",\"fileMD5\":\"3871060641dc09aa27fe80442fe51d73\",\"versionMatch\":\"1.7.1\",\"logicalFileName\":\"FallUI - HUD\"},\"reference\":{\"fileExpression\":\"HUDFramework 1.0f-20309-1-0f\",\"fileMD5\":\"058abd525c9651cbc9277d2ec54529c1\",\"versionMatch\":\"1.0f\",\"logicalFileName\":\"HUDFramework 1.0f\"}}," +
				"{\"type\":\"after\",\"source\":{\"fileExpression\":\"FallUI - Inventory-48758-2-2-1-1666954336\",\"fileMD5\":\"f51979f417a2ef1f38f21417d792cc3b\",\"versionMatch\":\"2.2.1\",\"logicalFileName\":\"FallUI - Inventory\"},\"reference\":{\"fileExpression\":\"Vault Girl Interface - Neo's FOMOD Version-38220-1-0-3-1563051101\",\"fileMD5\":\"6dcef05b6133cf3b834371aaf17621fd\",\"versionMatch\":\"1.0.3\",\"logicalFileName\":\"Vault Girl Interface - Neo's FOMOD Version\"}}," +
				"{\"type\":\"after\",\"source\":{\"fileExpression\":\"[Module 02 - Settings] FallUI - Minimal\",\"versionMatch\":\"*\",\"tag\":\"N_ykpPtEGz\"},\"reference\":{\"fileExpression\":\"Bundled - [Module 05 - Settings] FallUI - Minimal.7z (v_63_)\",\"fileMD5\":\"eca4836dadca1fe0bebd622d8b1910d9\",\"versionMatch\":\"1.0.0\"}}," +
				"{\"type\":\"after\",\"source\":{\"fileExpression\":\"[Module 02 - Settings] FallUI - Minimal\",\"versionMatch\":\"*\",\"tag\":\"N_ykpPtEGz\"},\"reference\":{\"fileExpression\":\"FallUI - HUD-51813-1-7-1-1668637124\",\"fileMD5\":\"3871060641dc09aa27fe80442fe51d73\",\"versionMatch\":\"1.7.1\",\"logicalFileName\":\"FallUI - HUD\"}}]";
			string json = BuildManifest(members, rules,
				"\"collectionConfig\":{\"recommendNewProfile\":false,\"excludePluginRules\":false,\"referenceTagScheme\":\"v1\"}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 6);

			Assert.AreEqual(CollectionCompatibilityStatus.ActionRequired, result.CapabilityReport.Status);
			Assert.IsFalse(result.CapabilityReport.AllIssues.Any(x => x.Status == CollectionCompatibilityStatus.Unsupported));
			CollectionAssert.AreEquivalent(new[] { 2, 3, 4 }, result.CapabilityReport.AllIssues
				.Where(x => x.Code == "member.source-policy-prefer-needs-resolution")
				.Select(x => x.SourceOrdinal.Value).ToArray());
			Assert.AreEqual(3, result.Manifest.ExternalFilePriorityRules.Count);
			Assert.AreEqual(1, result.Manifest.FilePriorityRules.Count,
				"The Module 02 settings -> FallUI HUD rule must bind back to the selected HUD member by exact MD5.");
		}

		[Test]
		public void Normalize_ExternalBeforeEndpointWithUncharacterizedVersionRangeRemainsUnsupported()
		{
			string member = "{\"name\":\"ENB Helper for Fallout 4\",\"version\":\"1.0.2\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":56566,\"fileId\":123}}";
			string rules = "[{\"type\":\"before\",\"source\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrim\",\"modId\":56566,\"fileId\":123}},\"reference\":{\"logicalFileName\":\"NAC X Legacy edition\",\"versionMatch\":\">=1.0\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rules), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "manifest.mod-rule-reference-unresolved"));
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

		private static string BuildManifest(string members, string modRules, string additionalRootFields = null)
		{
			return "{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"Example\",\"description\":\"Example collection\",\"domainName\":\"skyrim\"}," +
				"\"mods\":[" + members + "]," +
				"\"modRules\":" + modRules +
				(String.IsNullOrEmpty(additionalRootFields) ? String.Empty : "," + additionalRootFields) +
				"}";
		}
	}
}
