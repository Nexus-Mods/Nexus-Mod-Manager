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

		[TestCase("{\"plugins\":[{\"name\":\"A.esp\"}],\"groups\":[]}", "$.pluginRules.plugins")]
		[TestCase("{\"plugins\":[],\"groups\":[{\"name\":\"Late\"}]}", "$.pluginRules.groups")]
		public void Normalize_NonEmptyPluginRulesRemainFailClosed(string pluginRules, string expectedPath)
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":" + pluginRules);

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			CollectionCapabilityIssue issue = result.CapabilityReport.ManifestIssues.Single(x => x.Code == "manifest.plugin-rules-unsupported");
			Assert.AreEqual(expectedPath, issue.FieldPath);
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
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.installer-choices-invalid"));
		}

		[Test]
		public void Normalize_UnsupportedRequiredBehaviorBlocksCurrentSelection()
		{
			string json = BuildManifest(
				"{\"name\":\"Required FOMOD\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":3,\"fileId\":4},\"choices\":{\"step\":\"option\"}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(issue => issue.Code == "member.installer-choices-invalid"));
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
