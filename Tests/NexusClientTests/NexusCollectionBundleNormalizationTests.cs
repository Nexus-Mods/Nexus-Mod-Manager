using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies bundle/collection.json normalization without any native install or game mutation.
	/// </summary>
	[Category("CollectionsC12Compatibility")]
	public class NexusCollectionBundleNormalizationTests
	{
		[Test]
		public void Normalize_Module12LovingPiperRulesPreserveLiteralLeadingSpaces()
		{
			string members = "{\"name\":\"Wasteland Heroines Replacer All in One\",\"version\":\"2.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":29205,\"fileId\":188514,\"md5\":\"cd49ea932b8a1d95b052b126f1dacdbb\",\"fileSize\":386415,\"logicalFilename\":\"Wasteland Heroines Replacer All in One\",\"updatePolicy\":\"exact\",\"tag\":\"ziIMB9vmMf\"}},{\"name\":\"  LovingPiper\",\"version\":\"2.0\",\"optional\":true,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":9811,\"fileId\":62262,\"md5\":\"8fc14fe26a7d995bb132ec9002fec7fb\",\"fileSize\":2402,\"logicalFilename\":\"  LovingPiper\",\"updatePolicy\":\"exact\",\"tag\":\"oV55-bxhHh\"}},{\"name\":\"goodlooking piper replacer version\",\"version\":\"1.0\",\"optional\":true,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":25959,\"fileId\":106060,\"md5\":\"b1e091bc9f4f9424671134d40acfe601\",\"fileSize\":2528,\"logicalFilename\":\"goodlooking piper replacer version\",\"updatePolicy\":\"exact\",\"tag\":\"nwYIZWShKPl\"}}";
			string rules = "[{\"type\":\"before\",\"reference\":{\"fileExpression\":\"LovingPiper-9811-2-0\",\"fileMD5\":\"8fc14fe26a7d995bb132ec9002fec7fb\",\"versionMatch\":\"2.0.0\",\"logicalFileName\":\"  LovingPiper\"},\"source\":{\"fileExpression\":\"Wasteland Heroines Replacer All in One-29205-2-0-1596688655\",\"fileMD5\":\"cd49ea932b8a1d95b052b126f1dacdbb\",\"versionMatch\":\"2.0.0\",\"logicalFileName\":\"Wasteland Heroines Replacer All in One\"}},{\"type\":\"conflicts\",\"reference\":{\"logicalFileName\":\"goodlooking piper replacer version\",\"versionMatch\":\"*\",\"idHint\":\"goodlooking piper replacer version-25959-1-0\"},\"source\":{\"fileExpression\":\"LovingPiper-9811-2-0\",\"fileMD5\":\"8fc14fe26a7d995bb132ec9002fec7fb\",\"versionMatch\":\"2.0.0\",\"logicalFileName\":\"  LovingPiper\"}},{\"type\":\"conflicts\",\"reference\":{\"fileExpression\":\"goodlooking piper replacer version-25959-1-0\",\"fileMD5\":\"b1e091bc9f4f9424671134d40acfe601\",\"versionMatch\":\"1.0.0\",\"logicalFileName\":\"goodlooking piper replacer version\"},\"source\":{\"fileExpression\":\"LovingPiper-9811-2-0\",\"fileMD5\":\"8fc14fe26a7d995bb132ec9002fec7fb\",\"versionMatch\":\"2.0.0\",\"logicalFileName\":\"  LovingPiper\"}},{\"type\":\"conflicts\",\"reference\":{\"logicalFileName\":\"  LovingPiper\",\"versionMatch\":\"*\",\"idHint\":\"LovingPiper-9811-2-0\"},\"source\":{\"fileExpression\":\"goodlooking piper replacer version-25959-1-0\",\"fileMD5\":\"b1e091bc9f4f9424671134d40acfe601\",\"versionMatch\":\"1.0.0\",\"logicalFileName\":\"goodlooking piper replacer version\"}}]";
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 3);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.FilePriorityRules.Count);
			Assert.AreEqual(3, result.Manifest.ConflictConstraints.Count);
			Assert.IsTrue(result.Manifest.ConflictConstraints.Any(x => x.Reference.LogicalFileName == "  LovingPiper"));
		}

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
		public void Normalize_NexusArchiveSizeIsMetadataWithoutChangingLegacyIdentity()
		{
			string json = BuildManifest(
				"{\"name\":\"Requested Release\",\"version\":\"1.17.1\",\"optional\":false,\"domainName\":\"skyrimspecialedition\",\"source\":{\"type\":\"nexus\",\"modId\":51073,\"fileId\":794717,\"fileSize\":8555518,\"md5\":\"04A2999F76205E07242DAFD2C88C505B\",\"updatePolicy\":\"exact\"}}", "[]");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			CollectionArtifactReference artifact = result.Manifest.Members[0].Artifact;
			var legacy = new CollectionArtifactReference(artifact.Scheme, artifact.StableId, artifact.ExpectedContentHash);

			Assert.That(artifact.ExpectedByteLength, Is.EqualTo(8555518));
			Assert.That(artifact.ExpectedMd5, Is.EqualTo("04a2999f76205e07242dafd2c88c505b"));
			Assert.That(artifact, Is.EqualTo(legacy));
			Assert.That(artifact.GetHashCode(), Is.EqualTo(legacy.GetHashCode()));
			Assert.That(CollectionExternalArtifactIdentity.MatchesByteLength(artifact, 6398382), Is.False);
			Assert.That(CollectionExternalArtifactIdentity.MatchesByteLength(artifact, 8555518), Is.True);
			Assert.That(CollectionExternalArtifactIdentity.MatchesByteLength(legacy, 6398382), Is.True);
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

		/// <summary>Accepts an empty extension load order without changing installation recipes or retained source bytes.</summary>
		[TestCase("cyberpunk2077")]
		[TestCase("skyrim")]
		public void Normalize_EmptyLoadOrderHasNoInstallationBehavior(string domain)
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"" + domain + "\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]");
			JObject root = JObject.Parse(json);
			root["info"]["domainName"] = domain;
			NexusCollectionManifestNormalizationResult withoutOrder = Normalize(root.ToString(), 1);
			root["loadOrder"] = new JArray();
			string withOrderJson = root.ToString();
			NexusCollectionManifestNormalizationResult withOrder = Normalize(withOrderJson, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, withOrder.CapabilityReport.Status);
			Assert.AreEqual(withoutOrder.Manifest.Members[0].RecipeIdentity, withOrder.Manifest.Members[0].RecipeIdentity);
			CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(withOrderJson), withOrder.GetRawManifestBytes());
		}

		/// <summary>Keeps unimplemented ordering instructions and malformed load orders blocked outside game adapters.</summary>
		[TestCase("[{\"id\":\"example\",\"enabled\":true}]")]
		[TestCase("[null]")]
		[TestCase("{}")]
		[TestCase("null")]
		[TestCase("false")]
		[TestCase("\"\"")]
		public void Normalize_NonEmptyOrMalformedCyberpunkLoadOrderRemainsUnsupported(string loadOrder)
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"cyberpunk2077\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]", "\"loadOrder\":" + loadOrder);
			JObject root = JObject.Parse(json);
			root["info"]["domainName"] = "cyberpunk2077";
			NexusCollectionManifestNormalizationResult result = Normalize(root.ToString(), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(issue => issue.Code == "manifest.unknown-field" && issue.FieldPath == "$.loadOrder"));
		}

		[Test]
		public void Normalize_SetupGuidanceIsPreservedWithoutChangingCapability()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"collectionConfig\":{\"recommendNewProfile\":true,\"referenceTagScheme\":\"v1\"}",
				"Read this before installing.\nKeep the curator setup notes.",
				new[] { "1.10.980.0", "1.10.984.0" });

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.IsTrue(result.Manifest.SetupGuidance.RecommendNewProfile);
			Assert.AreEqual("Read this before installing.\nKeep the curator setup notes.", result.Manifest.SetupGuidance.InstallInstructions);
			CollectionAssert.AreEqual(new[] { "1.10.980.0", "1.10.984.0" }, result.Manifest.SetupGuidance.GameVersions);
			Assert.IsTrue(result.Manifest.SetupGuidance.HasGuidance);
		}

		[Test]
		public void Normalize_AbsentSetupGuidanceProducesEmptyGuidance()
		{
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}", "[]"), 1);

			Assert.IsNotNull(result.Manifest.SetupGuidance);
			Assert.IsFalse(result.Manifest.SetupGuidance.HasGuidance);
		}

		[Test]
		public void Normalize_GameRelativeCollectionToolIsPreservedAsExplicitLauncherMetadata()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"tools\":[{\"name\":\"Example tool\",\"exe\":\"Tools\\\\Runner.exe\",\"args\":[\"--profile\",\"A B\"]," +
				"\"env\":{\"NMM_TEST\":\"1\"},\"cwd\":\"Tools\",\"shell\":false,\"detach\":false}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.LaunchTools.Count);
			CollectionLaunchTool tool = result.Manifest.LaunchTools[0];
			Assert.AreEqual("Example tool", tool.Name);
			Assert.AreEqual("Tools\\Runner.exe", tool.RelativeExecutablePath);
			CollectionAssert.AreEqual(new[] { "--profile", "A B" }, tool.Arguments);
			Assert.AreEqual("Tools", tool.RelativeWorkingDirectory);
			Assert.AreEqual("1", tool.Environment["NMM_TEST"]);
			CollectionEffectiveSelection selection = new CollectionEffectiveSelectionBuilder().Build(result.CapabilityReport,
				new CollectionOptionalMemberSelection[0]);
			CollectionAssert.AreEqual(result.Manifest.LaunchTools, selection.Manifest.LaunchTools);
		}

		[Test]
		public void Normalize_CollectionToolShellLifecycleDisablesLauncherWithoutBlockingMods()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"tools\":[{\"name\":\"Unsafe tool\",\"exe\":\"Tools\\\\Runner.exe\",\"shell\":true}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(0, result.Manifest.LaunchTools.Count);
			Assert.IsTrue(result.Manifest.LaunchToolIssues.Any(x => x.Code == "manifest.tool-lifecycle-unsupported"));
		}

		[Test]
		public void Normalize_AbsoluteCollectionToolExecutableDisablesLauncherWithoutBlockingMods()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"tools\":[{\"name\":\"Unsafe tool\",\"exe\":\"C:\\\\Tools\\\\Runner.exe\"}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(0, result.Manifest.LaunchTools.Count);
			Assert.IsTrue(result.Manifest.LaunchToolIssues.Any(x => x.Code == "manifest.tool-path-unsupported"));
		}

		[Test]
		public void Normalize_ReservedDeviceCollectionToolPathDisablesLauncherWithoutBlockingMods()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"tools\":[{\"name\":\"Unsafe tool\",\"exe\":\"CON.exe\"}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(0, result.Manifest.LaunchTools.Count);
			Assert.IsTrue(result.Manifest.LaunchToolIssues.Any(x => x.Code == "manifest.tool-path-unsupported"));
		}

		[Test]
		public void Normalize_CaseCollidingCollectionToolEnvironmentDisablesLauncherWithoutBlockingMods()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"tools\":[{\"name\":\"Unsafe tool\",\"exe\":\"Tools\\\\Runner.exe\",\"env\":{\"Path\":\"A\",\"PATH\":\"B\"}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(0, result.Manifest.LaunchTools.Count);
			Assert.IsTrue(result.Manifest.LaunchToolIssues.Any(x => x.Code == "manifest.tool-env-invalid"));
		}

		[Test]
		public void Normalize_MalformedToolsDisableLaunchersWithoutBlockingMods()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"tools\":{}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(0, result.Manifest.LaunchTools.Count);
			Assert.IsTrue(result.Manifest.LaunchToolIssues.Any(x => x.Code == "manifest.tools-invalid"));
		}

		/// <summary>Preserves supported launchers while excluding entries with untranslated behavior.</summary>
		[Test]
		public void Normalize_UnknownToolBehaviorDoesNotExposeAPartiallyTranslatedLauncher()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"tools\":[{\"name\":\"Unknown behavior\",\"exe\":\"Runner.exe\",\"unknownLaunchMode\":true}," +
				"{\"name\":\"Supported launcher\",\"exe\":\"Safe.exe\"}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.LaunchToolIssues.Count);
			Assert.AreEqual("$.tools[0].unknownLaunchMode", result.Manifest.LaunchToolIssues[0].FieldPath);
			Assert.AreEqual("Safe.exe", result.Manifest.LaunchTools.Single().RelativeExecutablePath);
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
		public void Normalize_IdenticalDuplicatePluginStateIsIdempotentCaseInsensitively()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]", "\"plugins\":[{\"name\":\"A.esp\",\"enabled\":true},{\"name\":\"a.ESP\",\"enabled\":true}]");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.PluginStates.Count);
			Assert.IsTrue(result.Manifest.PluginStates[0].Enabled);
			Assert.AreEqual("A.esp", result.Manifest.PluginStates[0].PluginName);
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
		public void Normalize_ClosedPluginGroupsTranslateToRelativeOrderRules()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"A.esp\",\"group\":\"Late\"},{\"name\":\"B.esm\",\"group\":\"Early\"}]," +
				"\"groups\":[{\"name\":\"Late\",\"after\":[\"Early\"]},{\"name\":\"Early\",\"after\":[]}]}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			CollectionPluginRelativeOrderRule rule = result.Manifest.PluginRelativeOrderRules.Single();
			Assert.AreEqual("A.esp", rule.PluginName);
			Assert.AreEqual("B.esm", rule.AfterPluginName);
		}

		[Test]
		public void Normalize_ClosedPluginGroupChainIncludesTransitiveRelativeOrder()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"A.esp\",\"group\":\"Late\"},{\"name\":\"B.esp\",\"group\":\"Middle\"},{\"name\":\"C.esm\",\"group\":\"Early\"}]," +
				"\"groups\":[{\"name\":\"Late\",\"after\":[\"Middle\"]},{\"name\":\"Middle\",\"after\":[\"Early\"]},{\"name\":\"Early\",\"after\":[]}]}");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(3, result.Manifest.PluginRelativeOrderRules.Count);
			Assert.IsTrue(result.Manifest.PluginRelativeOrderRules.Contains(new CollectionPluginRelativeOrderRule("A.esp", "B.esp")));
			Assert.IsTrue(result.Manifest.PluginRelativeOrderRules.Contains(new CollectionPluginRelativeOrderRule("B.esp", "C.esm")));
			Assert.IsTrue(result.Manifest.PluginRelativeOrderRules.Contains(new CollectionPluginRelativeOrderRule("A.esp", "C.esm")));
		}

		[Test]
		public void Normalize_PluginGroupAssignmentToExternalGroupRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"A.esp\",\"group\":\"Masterlist Group\"}],\"groups\":[]}");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.plugin-group-assignment-external"));
		}

		/// <summary>FNV masterlist groups retain transitive order even when intermediate groups have no assigned plugins.</summary>
		[Test]
		public void Normalize_NewVegasDynamicPatchesGroupRetainsMasterlistOrdering()
		{
			string json = BuildManifest(
				"{\"name\":\"Weather\",\"version\":\"1\",\"optional\":false,\"domainName\":\"newvegas\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}", "[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"DNWeathers.esp\",\"group\":\"Dynamic Patches\"},{\"name\":\"Base.esp\",\"group\":\"default\"},{\"name\":\"Fixes.esm\",\"group\":\"Fixes & Resources\"}]}")
				.Replace("\"domainName\":\"skyrim\"", "\"domainName\":\"newvegas\"");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.IsTrue(result.Manifest.PluginRelativeOrderRules.Contains(new CollectionPluginRelativeOrderRule("DNWeathers.esp", "Base.esp")));
			Assert.IsTrue(result.Manifest.PluginRelativeOrderRules.Contains(new CollectionPluginRelativeOrderRule("DNWeathers.esp", "Fixes.esm")));
		}

		/// <summary>Vortex skips an empty CRC declaration and continues applying other valid diffs for that mod.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void Normalize_EmptyPatchCrcIsSkippedWhileValidPatchesRemain(bool includeValidPatch)
		{
			JObject member = JObject.Parse("{\"name\":\"Weather\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}");
			JObject patches = new JObject { ["DNWeathers.override"] = "" };
			if (includeValidPatch) patches["config/HDRWeatherAndColor.ini"] = "C0307BCB";
			member["patches"] = patches;
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member.ToString(), "[]"), 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			NormalizedCollectionMember normalized = result.Manifest.Members.Single();
			Assert.AreEqual(includeValidPatch, normalized.HasVortexBinaryPatches);
			if (includeValidPatch)
			{
				Assert.AreEqual(1, normalized.VortexBinaryPatches.Items.Count);
				Assert.AreEqual("C0307BCB", normalized.VortexBinaryPatches.Items.Single().SourceCrc32);
			}
		}

		/// <summary>Stale external tags can use portable filename identities, while tag-only references remain unsupported.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void Normalize_ExternalPriorityTagPreservesPortableFallback(bool includeFilename)
		{
			string member = "{\"name\":\"Base\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"tag\":\"base\"}}";
			JObject reference = new JObject { ["tag"] = "stale-tag", ["fileMD5"] = "d41cad0f7ac181ffdd4c9ee0d606ca3a", ["versionMatch"] = "1.1.0" };
			if (includeFilename) reference["fileExpression"] = "NVAO - Throwverhaul-91374-1-1-1744380118";
			JArray rules = new JArray(new JObject { ["type"] = "before", ["source"] = new JObject { ["tag"] = "base" }, ["reference"] = reference });
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rules.ToString()), 1);
			Assert.AreEqual(includeFilename ? CollectionCompatibilityStatus.Supported : CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			if (includeFilename) Assert.AreEqual("stale-tag", result.Manifest.ExternalFilePriorityRules.Single().ExternalReference.Tag);
		}

		[Test]
		public void Normalize_SkyrimMasterlistGroupsMergeCustomRulesAndPreserveTransitiveOrder()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrimspecialedition\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}", "[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"A.esp\",\"group\":\"Late Loaders\"},{\"name\":\"B.esp\",\"group\":\"default\"},{\"name\":\"C.esp\",\"group\":\"Floral Fields\"}]," +
				"\"groups\":[{\"name\":\"default\",\"after\":[\"Floral Fields\"]},{\"name\":\"Floral Fields\",\"after\":[\"Early Loaders\"]},{\"name\":\"Low Priority Overrides\",\"after\":[]}]}")
				.Replace("\"domainName\":\"skyrim\"", "\"domainName\":\"skyrimspecialedition\"");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(3, result.Manifest.PluginRelativeOrderRules.Count);
			Assert.IsTrue(result.Manifest.PluginRelativeOrderRules.Contains(new CollectionPluginRelativeOrderRule("A.esp", "B.esp")));
			Assert.IsTrue(result.Manifest.PluginRelativeOrderRules.Contains(new CollectionPluginRelativeOrderRule("B.esp", "C.esp")));
			Assert.IsTrue(result.Manifest.PluginRelativeOrderRules.Contains(new CollectionPluginRelativeOrderRule("A.esp", "C.esp")));
		}

		[TestCase("Early Loaders", "Late Loaders", "manifest.plugin-group-cycle")]
		[TestCase("Custom", "Unknown Group", "manifest.plugin-group-reference-external")]
		public void Normalize_SkyrimGroupDefaultsDoNotHideInvalidCustomRules(string group, string after, string issue)
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrimspecialedition\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}", "[]",
				"\"pluginRules\":{\"plugins\":[],\"groups\":[{\"name\":\"" + group + "\",\"after\":[\"" + after + "\"]}]}")
				.Replace("\"domainName\":\"skyrim\"", "\"domainName\":\"skyrimspecialedition\"");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == issue));
		}

		[Test]
		public void Normalize_SkyrimGroupDefaultsWorkWithoutCollectionGroupDefinitions()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrimspecialedition\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}", "[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"A.esp\",\"group\":\"Dynamic LOD\"},{\"name\":\"B.esp\",\"group\":\"Late Fixes & Changes\"},{\"name\":\"C.esp\",\"group\":\"Skills & Perks\"}]}")
				.Replace("\"domainName\":\"skyrim\"", "\"domainName\":\"skyrimspecialedition\"");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(3, result.Manifest.PluginRelativeOrderRules.Count);
		}

		[Test]
		public void Normalize_PluginGroupAfterExternalGroupRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"A.esp\",\"group\":\"Late\"}]," +
				"\"groups\":[{\"name\":\"Late\",\"after\":[\"Default\"]}]}");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.plugin-group-reference-external"));
		}

		[Test]
		public void Normalize_PluginGroupCycleRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"A.esp\",\"group\":\"Late\"},{\"name\":\"B.esp\",\"group\":\"Early\"}]," +
				"\"groups\":[{\"name\":\"Late\",\"after\":[\"Early\"]},{\"name\":\"Early\",\"after\":[\"Late\"]}]}");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.plugin-group-cycle"));
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

		[Test]
		public void Normalize_PluginGroupAndDirectRuleCycleRemainsFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}",
				"[]",
				"\"pluginRules\":{\"plugins\":[{\"name\":\"A.esp\",\"group\":\"Late\"},{\"name\":\"B.esp\",\"group\":\"Early\",\"after\":[\"A.esp\"]}]," +
				"\"groups\":[{\"name\":\"Late\",\"after\":[\"Early\"]},{\"name\":\"Early\",\"after\":[]}]}");
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

		[TestCase("manual", null)]
		[TestCase("browse", "https://mods.example.invalid/curated-file")]
		public void Normalize_ExternalPreferCanUseExactCuratorArchiveThroughManualVerification(string type, string url)
		{
			string source = "{\"type\":\"" + type + "\",\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"," +
				"\"fileSize\":456,\"updatePolicy\":\"prefer\"" +
				(url == null ? String.Empty : ",\"url\":\"" + url + "\"") + "}";
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(
				"{\"name\":\"Preferred external archive\",\"version\":\"\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":" + source + "}", "[]"), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/456", result.Manifest.Members.Single().Artifact.StableId);
			Assert.IsFalse(result.CapabilityReport.AllIssues.Any(x => x.Code == "member.external-update-policy-unsupported"));
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

		[TestCase(null)]
		[TestCase("Classic Holstered Weapons System (CHW) Configuration.zip")]
		[TestCase("Bundled - NeverNude BodySlide Output (v64).7z")]
		public void Normalize_ExactBundleSourceProducesRevisionScopedStableArtifactIdentity(string logicalFilename)
		{
			string json = BuildManifest(
				"{\"name\":\"Bundled\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"bundle\",\"fileSize\":8192,\"updatePolicy\":\"exact\",\"fileExpression\":\"Bundled - Local Tool (v1)\",\"tag\":\"bundleTag_1\"}}",
				"[]");

			if (logicalFilename != null)
			{
				Newtonsoft.Json.Linq.JObject manifest = Newtonsoft.Json.Linq.JObject.Parse(json);
				manifest["mods"][0]["source"]["logicalFilename"] = logicalFilename;
				json = manifest.ToString();
			}

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			NormalizedCollectionMember member = result.Manifest.Members.Single();
			Assert.IsNotNull(member.Artifact);
			Assert.AreEqual("collection-bundle", member.Artifact.Scheme);
			StringAssert.EndsWith("/bundleTag_1", member.Artifact.StableId);
			Assert.AreEqual("collection-bundle:" + member.Artifact.StableId, member.IdentityResolution.Key.Value);
			Assert.IsNull(member.Artifact.ExpectedContentHash, "Vortex recompresses embedded bundle directories, so logical identity must not invent a stable source archive hash.");
		}

		/// <summary>Retains original bundle archive names for conflict-rule matching.</summary>
		[Test]
		public void Normalize_BundleLogicalFilenameCanIdentifyFilePriorityRuleSource()
		{
			string members =
				"{\"name\":\"Settings\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"bundle\",\"fileSize\":10,\"fileExpression\":\"Bundled - Settings (v65)\",\"logicalFilename\":\"Settings.zip\",\"tag\":\"settingsTag\"}}," +
				"{\"name\":\"Base\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"bundle\",\"fileSize\":10,\"fileExpression\":\"Bundled - Base\",\"tag\":\"baseTag\"}}";
			string rules = "[{\"type\":\"after\",\"source\":{\"logicalFileName\":\"Settings.zip\",\"versionMatch\":\"*\"},\"reference\":{\"tag\":\"baseTag\",\"versionMatch\":\"*\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 2);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.FilePriorityRules.Count);
			Assert.AreEqual(0, result.Manifest.ExternalFilePriorityRules.Count);
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

		[TestCase("\"name\":null", null)]
		[TestCase("\"name\":\"\"", "")]
		[TestCase("", null)]
		public void Normalize_UnnamedVortexFomodStepsAreRetainedForNativeShapeValidation(string nameField, string expected)
		{
			string stepName = String.IsNullOrEmpty(nameField) ? String.Empty : nameField + ",";
			string json = BuildManifest(
				"{\"name\":\"Legacy\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"choices\":{\"type\":\"fomod\",\"options\":[{" + stepName + "\"groups\":[{\"name\":\"Group\",\"choices\":[{\"name\":\"Selected\",\"idx\":0}]}]}]}}",
				"[]");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			CollectionVortexFomodStepSelection step = result.Manifest.Members.Single().VortexFomodSelection.Steps.Single();
			Assert.AreEqual(expected, step.Name);
			Assert.DoesNotThrow(() => step.GetHashCode());
		}

		/// <summary>Preserves literal installer group labels, including the spaces used by Meridia's recorded choices.</summary>
		[TestCase("\"\"", true)]
		[TestCase("\" \"", true)]
		[TestCase("\"  \"", true)]
		[TestCase("\"\\t\"", true)]
		[TestCase("null", false)]
		[TestCase("1", false)]
		public void Normalize_UnnamedVortexFomodGroupRetainsExactNativeIdentity(string nameValue, bool supported)
		{
			string json = BuildManifest(
				"{\"name\":\"Legacy\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"choices\":{\"type\":\"fomod\",\"options\":[{\"name\":\"\",\"groups\":[{\"name\":" + nameValue + ",\"choices\":[{\"name\":\"Selected\",\"idx\":1}]}]}]}}", "[]");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(supported ? CollectionCompatibilityStatus.Supported : CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			if (supported)
			{
				CollectionVortexFomodGroupSelection group = result.Manifest.Members.Single().VortexFomodSelection.Steps.Single().Groups.Single();
				Assert.AreEqual(JToken.Parse(nameValue).Value<string>(), group.Name);
				Assert.AreEqual(1, group.Choices.Single().Index);
				Assert.DoesNotThrow(() => group.GetHashCode());
			}
			else Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "member.installer-choices-invalid"));
		}

		[Test]
		public void Normalize_WhitespaceOnlyVortexFomodStepPreservesExactLabel()
		{
			string json = BuildManifest(
				"{\"name\":\"Legacy\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"choices\":{\"type\":\"fomod\",\"options\":[{\"name\":\"  \",\"groups\":[]}]}}",
				"[]");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual("  ", result.Manifest.Members.Single().VortexFomodSelection.Steps.Single().Name);
		}

		[Test]
		public void Normalize_EmptyVortexFomodOptionsBecomeTypedSupportedDefaultOnlySelection()
		{
			string json = BuildManifest(
				"{\"name\":\"Default only\",\"version\":\"1.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"choices\":{\"type\":\"fomod\",\"options\":[]}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			NormalizedCollectionMember member = result.Manifest.Members.Single();
			Assert.IsTrue(member.HasVortexFomodSelection);
			Assert.AreEqual(0, member.VortexFomodSelection.Steps.Count);
		}

		/// <summary>A type-only marker must defer to native archive inspection instead of inventing saved installer choices.</summary>
		[Test]
		public void Normalize_TypeOnlyFomodMarkerHasNoRecordedSelections()
		{
			string json = BuildManifest(
				"{\"name\":\"Compact UI\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20},\"choices\":{\"type\":\"fomod\"}}", "[]");
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.IsFalse(result.Manifest.Members.Single().HasVortexFomodSelection);
		}

		/// <summary>Omitted options are distinct from explicitly malformed records; unknown marker fields remain unsupported.</summary>
		[TestCase("{\"type\":\"fomod\",\"options\":null}")]
		[TestCase("{\"type\":\"fomod\",\"options\":{}}")]
		[TestCase("{\"type\":\"fomod\",\"options\":\"missing\"}")]
		[TestCase("{\"type\":\"fomod\",\"future\":true}")]
		public void Normalize_MalformedFomodOptionsOrUnknownMarkerFieldsRemainBlocked(string choices)
		{
			string json = BuildManifest(
				"{\"name\":\"Compact UI\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20},\"choices\":" + choices + "}", "[]");
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, Normalize(json, 1).CapabilityReport.Status);
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
		public void Normalize_VortexFomodChoicesCombinedWithDinputGameRootBecomeSupportedComposition()
		{
			string json = BuildManifest(
				"{\"name\":\"Injector\",\"version\":\"2.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"details\":{\"type\":\"dinput\"},\"choices\":{\"type\":\"fomod\",\"options\":[{\"name\":\"Step\",\"groups\":[{\"name\":\"Group\",\"choices\":[]}]}]}}",
				"[]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(CollectionMemberInstallRootBehavior.VortexDInputGameRoot, result.Manifest.Members.Single().InstallRootBehavior);
			Assert.IsTrue(result.Manifest.Members.Single().HasVortexFomodSelection);
		}

		[Test]
		public void Normalize_VortexFomodChoicesCombinedWithEnbGameRootRemainFailClosed()
		{
			string json = BuildManifest(
				"{\"name\":\"ENB\",\"version\":\"2.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}," +
				"\"details\":{\"type\":\"enb\"},\"choices\":{\"type\":\"fomod\",\"options\":[{\"name\":\"Step\",\"groups\":[{\"name\":\"Group\",\"choices\":[]}]}]}}",
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
		public void Normalize_InternalRequiresModRuleBecomesMemberPrerequisite()
		{
			string json = BuildManifest(
				"{\"name\":\"Framework\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}," +
				"{\"name\":\"Dependent\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21}}",
				"[{\"source\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrim\",\"modId\":\"11\",\"fileId\":\"21\"}},\"type\":\"requires\",\"reference\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrim\",\"modId\":\"10\",\"fileId\":\"20\"}}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 2);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.Dependencies.Count);
			CollectionMemberDependency dependency = result.Manifest.Dependencies.Single();
			Assert.AreEqual(CollectionMemberDependencyKind.InstallerPrerequisite, dependency.Kind);
			Assert.AreEqual(result.Manifest.Members[0].IdentityResolution.Key, dependency.PrerequisiteMemberKey);
			Assert.AreEqual(result.Manifest.Members[1].IdentityResolution.Key, dependency.DependentMemberKey);
		}

		[Test]
		public void Normalize_InternalRequiresOptionalPrerequisiteUsesExistingSelectionCapabilityCheck()
		{
			string json = BuildManifest(
				"{\"name\":\"Optional Framework\",\"version\":\"1\",\"optional\":true,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}," +
				"{\"name\":\"Dependent\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21}}",
				"[{\"source\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrim\",\"modId\":\"11\",\"fileId\":\"21\"}},\"type\":\"requires\",\"reference\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrim\",\"modId\":\"10\",\"fileId\":\"20\"}}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 2);

			Assert.AreEqual(CollectionCompatibilityStatus.ActionRequired, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "member.prerequisite-unselected" && x.SourceOrdinal == 1));
		}

		[Test]
		public void Normalize_RequiresExternalReferenceRemainsUnsupported()
		{
			string json = BuildManifest(
				"{\"name\":\"Dependent\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21}}",
				"[{\"source\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrim\",\"modId\":\"11\",\"fileId\":\"21\"}},\"type\":\"requires\",\"reference\":{\"logicalFileName\":\"External Framework\",\"versionMatch\":\">=2.0\"}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.requires-rule-reference-external"));
		}

		[TestCase("recommends")]
		[TestCase("provides")]
		public void Normalize_SoftOrProviderModRulesRemainUnsupported(string ruleType)
		{
			string json = BuildManifest(
				"{\"name\":\"A\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}," +
				"{\"name\":\"B\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21}}",
				"[{\"source\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrim\",\"modId\":\"10\",\"fileId\":\"20\"}},\"type\":\"" + ruleType + "\",\"reference\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrim\",\"modId\":\"11\",\"fileId\":\"21\"}}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 2);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-type-unsupported"));
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
		public void Normalize_WhitespaceDecoratedPrioritySourceBindsOnlyByUniqueExactMd5()
		{
			string members =
				"{\"name\":\"A\",\"version\":\"1.0.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"hash-a\",\"logicalFilename\":\"A File\"}}," +
				"{\"name\":\"B\",\"version\":\"2.0.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21,\"md5\":\"hash-b\",\"logicalFilename\":\"B File\"}}";
			string rules = "[{\"type\":\"before\",\"source\":{\"fileMD5\":\"hash-a\",\"logicalFileName\":\"  A File \"," +
				"\"fileExpression\":\" Exported Archive Name  \",\"versionMatch\":\" 1.0.0 \"}," +
				"\"reference\":{\"fileMD5\":\"hash-b\",\"logicalFileName\":\"B File\",\"versionMatch\":\"2.0.0\"}}]";
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 2);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.FilePriorityRules.Count);
			Assert.AreEqual(result.Manifest.Members[0].IdentityResolution.Key, result.Manifest.FilePriorityRules[0].LowerPriorityMemberKey);
			Assert.AreEqual(result.Manifest.Members[1].IdentityResolution.Key, result.Manifest.FilePriorityRules[0].HigherPriorityMemberKey);
		}

		[Test]
		public void Normalize_BlankVersionPrioritySourceBindsUnversionedExternalMemberByExactMd5()
		{
			const string externalMd5 = "c78bc245ec508649f4773c252cfa9505";
			const string otherMd5 = "dc0ffb6d9a8ca71207a95dfff6613c60";
			string members =
				"{\"name\":\"FusionGirl-0223.7z\",\"version\":\"\",\"optional\":false,\"domainName\":\"fallout4\"," +
				"\"source\":{\"type\":\"browse\",\"url\":\"https://mods.example.invalid/fusion-girl\",\"md5\":\"" + externalMd5 +
				"\",\"fileSize\":742109851,\"updatePolicy\":\"exact\"}}," +
				"{\"name\":\"TBOS-BodyTalk4\",\"version\":\"4.0.1\",\"optional\":false,\"domainName\":\"fallout4\"," +
				"\"source\":{\"type\":\"nexus\",\"modId\":72310,\"fileId\":383018,\"md5\":\"" + otherMd5 + "\",\"logicalFilename\":\"TBOS-BodyTalk4\"}}";
			string rules = "[{\"type\":\"after\",\"reference\":{\"fileExpression\":\"TBOS-BodyTalk4-72310-4-0-1-1769451493\"," +
				"\"fileMD5\":\"" + otherMd5 + "\",\"versionMatch\":\"*\",\"logicalFileName\":\"TBOS-BodyTalk4\"}," +
				"\"source\":{\"fileExpression\":\"FusionGirl-0223\",\"fileMD5\":\"" + externalMd5 + "\",\"versionMatch\":\"\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 2);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.FilePriorityRules.Count);
			Assert.AreEqual(result.Manifest.Members[0].IdentityResolution.Key, result.Manifest.FilePriorityRules[0].HigherPriorityMemberKey);
			Assert.AreEqual(result.Manifest.Members[1].IdentityResolution.Key, result.Manifest.FilePriorityRules[0].LowerPriorityMemberKey);
		}

		[TestCase("", "", false)]
		[TestCase(" ", "c78bc245ec508649f4773c252cfa9505", true)]
		[TestCase("", "c78bc245ec508649f4773c252cfa9505", true)]
		public void Normalize_BlankVersionPriorityNeedsOneExactMd5AndNormalizesWhitespace(
			string version, string md5, bool expectedSupported)
		{
			string members = "{\"name\":\"A\",\"version\":\"\",\"optional\":false,\"domainName\":\"fallout4\"," +
				"\"source\":{\"type\":\"browse\",\"url\":\"https://mods.example.invalid/file\"," +
				"\"md5\":\"c78bc245ec508649f4773c252cfa9505\",\"fileSize\":10}}," +
				"{\"name\":\"B\",\"version\":\"1.0\",\"optional\":false,\"domainName\":\"fallout4\"," +
				"\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":22,\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}}";
			string rules = "[{\"type\":\"before\",\"source\":{\"fileExpression\":\"A-archive\"," +
				"\"fileMD5\":\"" + md5 + "\",\"versionMatch\":\"" + version + "\"}," +
				"\"reference\":{\"fileMD5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"versionMatch\":\"*\"}}]";
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 2);
			Assert.AreEqual(expectedSupported ? CollectionCompatibilityStatus.Supported : CollectionCompatibilityStatus.Unsupported,
				result.CapabilityReport.Status);
			if (!expectedSupported)
				Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-source-unresolved"));
		}

		[TestCase("wrong-md5", "  A File  ", "1.0.0")]
		[TestCase("hash-a", "  Different File  ", "1.0.0")]
		[TestCase("hash-a", "  A File  ", "1.1.0")]
		[TestCase("hash-a", "  A File  ", ">=1.0.0+prefer")]
		[TestCase("hash-a", "  ", "1.0.0")]
		public void Normalize_WhitespacePrioritySourceStillRejectsMismatchedOrUnsupportedConstraints(
			string md5, string logicalFileName, string version)
		{
			string members =
				"{\"name\":\"A\",\"version\":\"1.0.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"hash-a\",\"logicalFilename\":\"A File\"}}," +
				"{\"name\":\"B\",\"version\":\"2.0.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21,\"md5\":\"hash-b\"}}";
			string rules = "[{\"type\":\"before\",\"source\":{\"fileMD5\":\"" + md5 + "\",\"logicalFileName\":\"" + logicalFileName +
				"\",\"versionMatch\":\"" + version + "\"},\"reference\":{\"fileMD5\":\"hash-b\",\"versionMatch\":\"2.0.0\"}}]";
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 2);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-source-unresolved"));
		}

		[Test]
		public void Normalize_WhitespacePrioritySourceWithAmbiguousExactMd5IsUnsupported()
		{
			string members =
				"{\"name\":\"A One\",\"version\":\"1.0.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"hash-a\",\"logicalFilename\":\"A File\"}}," +
				"{\"name\":\"A Two\",\"version\":\"1.0.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":12,\"fileId\":22,\"md5\":\"hash-a\",\"logicalFilename\":\"A File\"}}," +
				"{\"name\":\"B\",\"version\":\"2.0.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21,\"md5\":\"hash-b\"}}";
			string rules = "[{\"type\":\"before\",\"source\":{\"fileMD5\":\"hash-a\",\"logicalFileName\":\"  A File  \",\"versionMatch\":\" 1.0.0 \"}," +
				"\"reference\":{\"fileMD5\":\"hash-b\",\"versionMatch\":\"2.0.0\"}}]";
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 3);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-source-unresolved"));
		}

		[Test]
		public void Normalize_WhitespacePrioritySourceWithoutExactMd5NeverFallsBackToFilenameGuess()
		{
			string members =
				"{\"name\":\"A\",\"version\":\"1.0.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"hash-a\"}}," +
				"{\"name\":\"B\",\"version\":\"2.0.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21,\"md5\":\"hash-b\"}}";
			string rules = "[{\"type\":\"before\",\"source\":{\"logicalFileName\":\"  A  \",\"versionMatch\":\"1.0.0\"}," +
				"\"reference\":{\"fileMD5\":\"hash-b\",\"versionMatch\":\"2.0.0\"}}]";
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 2);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-source-unresolved"));
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
		public void Normalize_ConflictMd5FallbackAcceptsUniqueMatchingExactSourceVersion()
		{
			string member = "{\"name\":\"Base\",\"version\":\"1.2.0\",\"optional\":false,\"domainName\":\"fallout4\"," +
				"\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"hash-a\",\"logicalFilename\":\"Base\"}}";
			string rule = "[{\"type\":\"conflicts\",\"source\":{\"fileMD5\":\"hash-a\",\"fileExpression\":\"Unrecorded Archive Name\"," +
				"\"versionMatch\":\"1.2\"},\"reference\":{\"logicalFileName\":\"Other Mod\",\"versionMatch\":\"*\"}}]";
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rule), 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.ConflictConstraints.Count);
		}

		[TestCase("1.3.0")]
		[TestCase("1.2+prefer")]
		[TestCase(">=1.0")]
		public void Normalize_ConflictMd5FallbackRejectsDifferentOrFuzzySourceVersions(string version)
		{
			string member = "{\"name\":\"Base\",\"version\":\"1.2.0\",\"optional\":false,\"domainName\":\"fallout4\"," +
				"\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"hash-a\"}}";
			string rule = "[{\"type\":\"conflicts\",\"source\":{\"fileMD5\":\"hash-a\",\"fileExpression\":\"Different Archive Name\"," +
				"\"versionMatch\":\"" + version + "\"},\"reference\":{\"logicalFileName\":\"Other Mod\",\"versionMatch\":\"*\"}}]";
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rule), 1);
			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.conflict-rule-source-unresolved"));
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
		public void Normalize_ConflictPreferVersionUsesCharacterizedVortexFuzzyVersionSemantics()
		{
			string json = BuildManifest(
				"{\"name\":\"A\",\"version\":\"1.2.0\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"hash-a\",\"logicalFilename\":\"A\"}}",
				"[{\"source\":{\"fileMD5\":\"hash-a\",\"logicalFileName\":\"A\",\"versionMatch\":\"*\"},\"type\":\"conflicts\",\"reference\":{\"logicalFileName\":\"Other\",\"versionMatch\":\">=1.2.0+prefer\"}}]");

			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			CollectionVortexVersionMatch match = result.Manifest.ConflictConstraints.Single().Reference.VersionMatch;
			Assert.IsTrue(match.IsPrefer);
			Assert.IsTrue(match.IsFuzzy);
			Assert.IsTrue(match.IsRange);
			Assert.AreEqual(CollectionVortexVersionMatchResult.Match, match.Evaluate("1.2.0"));
			Assert.AreEqual(CollectionVortexVersionMatchResult.Match, match.Evaluate("1.9.0"));
			Assert.AreEqual(CollectionVortexVersionMatchResult.NoMatch, match.Evaluate("1.1.9"));
		}

		[Test]
		public void CharacterizedVortexPreferExactVersionStripsMarkerForComparisonButRemainsFuzzy()
		{
			CollectionVortexVersionMatch match;
			string failure;
			Assert.IsTrue(CollectionVortexVersionMatch.TryCreate("1.5.3+prefer", out match, out failure), failure);

			Assert.IsTrue(match.IsPrefer);
			Assert.IsTrue(match.IsFuzzy);
			Assert.IsFalse(match.IsRange);
			Assert.AreEqual("1.5.3+prefer", match.Expression);
			Assert.AreEqual(CollectionVortexVersionMatchResult.Match, match.Evaluate("1.5.3"));
			Assert.AreEqual(CollectionVortexVersionMatchResult.NoMatch, match.Evaluate("1.5.4"));
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
		public void Normalize_ExternalAfterEndpointWithSimpleWildcardFileExpressionIsCharacterized()
		{
			string member = "{\"name\":\"FallUI - HUD\",\"version\":\"1.7.1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":51813,\"fileId\":257220}}";
			string rules = "[{\"type\":\"after\",\"source\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"fallout4\",\"modId\":51813,\"fileId\":257220}},\"reference\":{\"fileExpression\":\"HUDFramework*-20309-1-0?\",\"versionMatch\":\"1.0f\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rules), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.ExternalFilePriorityRules.Count);
			Assert.AreEqual("HUDFramework*-20309-1-0?", result.Manifest.ExternalFilePriorityRules.Single().ExternalReference.FileExpression);
		}

		[Test]
		public void Normalize_ConflictEndpointWithSimpleWildcardFileExpressionIsCharacterized()
		{
			string member = "{\"name\":\"A\",\"version\":\"1.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}";
			string rules = "[{\"type\":\"conflicts\",\"source\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"fallout4\",\"modId\":10,\"fileId\":20}},\"reference\":{\"fileExpression\":\"Other Mod-?-*\",\"versionMatch\":\"*\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rules), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(1, result.Manifest.ConflictConstraints.Count);
			Assert.AreEqual("Other Mod-?-*", result.Manifest.ConflictConstraints.Single().Reference.FileExpression);
		}

		[Test]
		public void Normalize_ExternalWildcardEndpointWithCharacterClassRemainsUnsupported()
		{
			string member = "{\"name\":\"FallUI - HUD\",\"version\":\"1.7.1\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":51813,\"fileId\":257220}}";
			string rules = "[{\"type\":\"after\",\"source\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"fallout4\",\"modId\":51813,\"fileId\":257220}},\"reference\":{\"fileExpression\":\"HUDFramework[0-9]*\",\"versionMatch\":\"*\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(member, rules), 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.IsTrue(result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-reference-unresolved"));
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

		[TestCase(true)]
		[TestCase(false)]
		public void Normalize_DifferentVersionMetadataRequiresMatchingExactArchiveMd5(bool sameArchive)
		{
			string members =
				"{\"name\":\"Text Version\",\"version\":\"0.5b\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"md5\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"logicalFilename\":\"Text Version\"}}," +
				"{\"name\":\"Other\",\"version\":\"1.0\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":30,\"fileId\":40,\"md5\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"logicalFilename\":\"Other\"}}";
			string endpointMd5 = sameArchive ? "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" : "cccccccccccccccccccccccccccccccc";
			string rules = "[{\"type\":\"before\",\"source\":{\"fileExpression\":\"Text Version-10-0-5b\",\"fileMD5\":\"" + endpointMd5 + "\",\"versionMatch\":\"0.5.0\",\"logicalFileName\":\"Text Version\"},\"reference\":{\"fileMD5\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"versionMatch\":\"1.0\",\"logicalFileName\":\"Other\"}}]";

			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(members, rules), 2);

			Assert.AreEqual(sameArchive ? CollectionCompatibilityStatus.Supported : CollectionCompatibilityStatus.Unsupported, result.CapabilityReport.Status);
			Assert.AreEqual(!sameArchive, result.CapabilityReport.ManifestIssues.Any(x => x.Code == "manifest.mod-rule-source-unresolved"));
		}

		/// <summary>Exact archive identity takes precedence over exported version formatting and display-name aliases.</summary>
		[TestCase("3.0.2.10", "3.0.2-10", "Animation Selector", "Animation Selector", "before")]
		[TestCase("1.4.8.1", "1.4.8-1", "Origins Of Forest", "Origins Of Forest", "after")]
		[TestCase("2.8.6b", "2.8.6-b", "Follower Framework", "Follower Framework", "before")]
		[TestCase(".60", "60.0.0", "Laci Living Doll ESL", "Laci Living Doll ESL", "after")]
		[TestCase("2.2.6", "2.2.6", "Skse64 2 02 06", "Skyrim Script Extender (SKSE64)", "conflicts")]
		public void Normalize_ExactArchiveHashResolvesExportedMetadataAliases(string memberVersion, string ruleVersion,
			string memberLogicalName, string ruleLogicalName, string type)
		{
			var members = new JArray
			{
				new JObject
				{
					["name"] = "Source mod", ["version"] = memberVersion, ["optional"] = false, ["domainName"] = "skyrim",
					["source"] = new JObject { ["type"] = "nexus", ["modId"] = 10, ["fileId"] = 20,
						["md5"] = "89da1342816cf3593c60a775b9aaeba3", ["logicalFilename"] = memberLogicalName }
				},
				new JObject
				{
					["name"] = "Other mod", ["version"] = "1.0", ["optional"] = false, ["domainName"] = "skyrim",
					["source"] = new JObject { ["type"] = "nexus", ["modId"] = 11, ["fileId"] = 21,
						["md5"] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", ["logicalFilename"] = "Other mod" }
				}
			};
			var rules = new JArray { new JObject
			{
				["type"] = type,
				["source"] = new JObject { ["fileMD5"] = "89da1342816cf3593c60a775b9aaeba3",
					["logicalFileName"] = ruleLogicalName, ["versionMatch"] = ruleVersion, ["fileExpression"] = "Exported archive filename" },
				["reference"] = new JObject { ["fileMD5"] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
					["logicalFileName"] = "Other mod", ["versionMatch"] = "*" }
			}};
			NexusCollectionManifestNormalizationResult result = Normalize(BuildManifest(
				String.Join(",", members.Select(x => x.ToString(Newtonsoft.Json.Formatting.None))), rules.ToString(Newtonsoft.Json.Formatting.None)), 2);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			if (type == "conflicts")
				Assert.AreEqual(result.Manifest.Members[0].IdentityResolution.Key, result.Manifest.ConflictConstraints.Single().SourceMemberKey);
			else
				Assert.AreEqual(1, result.Manifest.FilePriorityRules.Count);
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

		/// <summary>Characterizes the Odyssey Starfield extension fields without relying on a user's retained files.</summary>
		[Test]
		public void Normalize_StarfieldDataFolderAndSparsePluginOrderUseNativeBehavior()
		{
			JObject root = BuildStarfieldManifest();
			root["loadOrder"] = JArray.Parse("[{\"id\":\"Starfield.esm\",\"name\":\"Starfield.esm\",\"enabled\":true,\"locked\":true,\"modId\":\"unrelated-curator-local-id\"}," +
				"{\"id\":\"Economy.esm\",\"name\":\"Economy.esm\",\"enabled\":true,\"locked\":false,\"data\":{\"isInvalid\":false}}," +
				"{\"id\":\"Optional.esm\",\"name\":\"Optional.esm\",\"enabled\":false}," +
				"{\"id\":\"Patch.esm\",\"name\":\"Patch.esm\",\"enabled\":true}]");
			string json = root.ToString();
			NexusCollectionManifestNormalizationResult result = Normalize(json, 1);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.AreEqual(CollectionMemberInstallRootBehavior.Default, result.Manifest.Members[0].InstallRootBehavior);
			Assert.IsFalse(result.Manifest.Members[0].RequiresGameRootInstall);
			Assert.IsTrue(result.Manifest.PluginStatesArePartial);
			Assert.AreEqual(3, result.Manifest.PluginStates.Count);
			Assert.IsFalse(result.Manifest.PluginStates.Single(x => x.PluginName == "Optional.esm").Enabled);
			Assert.IsFalse(result.Manifest.PluginStates.Any(x => x.PluginName == "Starfield.esm"));
			Assert.AreEqual(3, result.Manifest.PluginRelativeOrderRules.Count);
			Assert.IsTrue(result.Manifest.PluginRelativeOrderRules.Contains(new CollectionPluginRelativeOrderRule("Patch.esm", "Economy.esm")),
				"Skipping an optional intermediate plugin must preserve the selected subsequence.");
			CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(json), result.GetRawManifestBytes());
		}

		/// <summary>Keeps unknown semantics, malformed states and unrecognized locks blocked.</summary>
		[TestCase("{\"id\":\"A.esm\",\"name\":\"B.esm\",\"enabled\":true}")]
		[TestCase("{\"id\":\"../A.esm\",\"name\":\"../A.esm\",\"enabled\":true}")]
		[TestCase("{\"id\":\"A:bad.esm\",\"name\":\"A:bad.esm\",\"enabled\":true}")]
		[TestCase("{\"id\":\"A.esm\",\"name\":\"A.esm\",\"enabled\":\"true\"}")]
		[TestCase("{\"id\":\"A.esm\",\"name\":\"A.esm\",\"enabled\":true,\"locked\":true}")]
		[TestCase("{\"id\":\"A.esm\",\"name\":\"A.esm\",\"enabled\":true,\"data\":{\"isInvalid\":true}}")]
		[TestCase("{\"id\":\"A.esm\",\"name\":\"A.esm\",\"enabled\":true,\"data\":{\"future\":false}}")]
		[TestCase("{\"id\":\"A.esm\",\"name\":\"A.esm\",\"enabled\":true,\"position\":42}")]
		[TestCase("{\"id\":\"Starfield.esm\",\"name\":\"Starfield.esm\",\"enabled\":false}")]
		[TestCase("null")]
		public void Normalize_StarfieldMalformedLoadOrderEntryRemainsUnsupported(string entry)
		{
			JObject root = BuildStarfieldManifest();
			root["loadOrder"] = JArray.Parse("[" + entry + "]");
			Assert.IsTrue(Normalize(root.ToString(), 1).CapabilityReport.AllIssues.Any(x => x.Code == "manifest.starfield-load-order-invalid"));
		}

		/// <summary>Rejects malformed Starfield order containers rather than accepting them as extension metadata.</summary>
		[TestCase("{}")]
		[TestCase("null")]
		[TestCase("false")]
		public void Normalize_StarfieldMalformedLoadOrderRemainsUnsupported(string loadOrder)
		{
			JObject root = BuildStarfieldManifest();
			root["loadOrder"] = JToken.Parse(loadOrder);
			Assert.IsTrue(Normalize(root.ToString(), 1).CapabilityReport.AllIssues.Any(x => x.Code == "manifest.starfield-load-order-invalid"));
		}

		/// <summary>Never resolves contradictory declarations by silently choosing one source.</summary>
		[Test]
		public void Normalize_StarfieldConflictingPluginStatesAndOrderRemainBlocked()
		{
			JObject root = BuildStarfieldManifest();
			root["loadOrder"] = JArray.Parse("[{\"id\":\"A.esm\",\"name\":\"A.esm\",\"enabled\":true},{\"id\":\"B.esm\",\"name\":\"B.esm\",\"enabled\":true}]");
			root["plugins"] = JArray.Parse("[{\"name\":\"A.esm\",\"enabled\":false}]");
			root["pluginRules"] = JObject.Parse("{\"plugins\":[{\"name\":\"A.esm\",\"after\":[\"B.esm\"]}],\"groups\":[]}");
			NexusCollectionManifestNormalizationResult result = Normalize(root.ToString(), 1);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "manifest.starfield-plugin-state-conflict"));
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "manifest.plugin-rule-cycle"));
			Assert.IsFalse(result.Manifest.PluginStatesArePartial, "An explicit non-empty plugins section retains its exhaustive semantics.");
		}

		/// <summary>Rejects duplicate names case-insensitively without constructing duplicate native model entries.</summary>
		[Test]
		public void Normalize_StarfieldDuplicatePluginAndInvalidExtensionVersionRemainBlocked()
		{
			JObject root = BuildStarfieldManifest();
			root["loadOrder"] = JArray.Parse("[{\"id\":\"A.esm\",\"name\":\"A.esm\",\"enabled\":true},{\"id\":\"a.esm\",\"name\":\"a.esm\",\"enabled\":true}]");
			root["extensionVersion"] = 11010;
			NexusCollectionManifestNormalizationResult result = Normalize(root.ToString(), 1);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "manifest.starfield-load-order-invalid"));
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "manifest.starfield-extension-version-invalid"));
		}

		/// <summary>Does not broaden another game's accepted extension or install-root semantics.</summary>
		[Test]
		public void Normalize_StarfieldFieldsRemainUnsupportedForOtherGames()
		{
			JObject root = BuildStarfieldManifest();
			root["info"]["domainName"] = "skyrim";
			root["mods"][0]["domainName"] = "skyrim";
			root["loadOrder"] = JArray.Parse("[{\"id\":\"A.esm\",\"name\":\"A.esm\",\"enabled\":true}]");
			NexusCollectionManifestNormalizationResult result = Normalize(root.ToString(), 1);
			Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == "member.mod-type-unsupported"));
			Assert.AreEqual(2, result.CapabilityReport.AllIssues.Count(x => x.Code == "manifest.unknown-field"));
		}

		/// <summary>Creates the exact minimal Starfield manifest envelope used by the extension adapter checks.</summary>
		private static JObject BuildStarfieldManifest()
		{
			JObject root = JObject.Parse(BuildManifest("{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"starfield\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20},\"details\":{\"type\":\"starfield-data-folder\"}}", "[]"));
			root["info"]["domainName"] = "starfield";
			root["extensionVersion"] = "1.10.10";
			root["plugins"] = new JArray();
			return root;
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

		private static string BuildManifest(string members, string modRules, string additionalRootFields = null,
			string installInstructions = null, IEnumerable<string> gameVersions = null)
		{
			string instructionsJson = installInstructions == null ? String.Empty :
				",\"installInstructions\":\"" + EscapeJsonString(installInstructions) + "\"";
			string versionsJson = gameVersions == null ? String.Empty :
				",\"gameVersions\":[" + String.Join(",", gameVersions.Select(x => "\"" + EscapeJsonString(x) + "\"")) + "]";
			return "{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"Example\",\"description\":\"Example collection\",\"domainName\":\"skyrim\"" + instructionsJson + versionsJson + "}," +
				"\"mods\":[" + members + "]," +
				"\"modRules\":" + modRules +
				(String.IsNullOrEmpty(additionalRootFields) ? String.Empty : "," + additionalRootFields) +
				"}";
		}

		private static string EscapeJsonString(string value)
		{
			return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
		}
	}
}
