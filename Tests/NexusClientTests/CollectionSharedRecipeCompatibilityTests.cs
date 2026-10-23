using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies cross-Collection sharing against exact retained legacy manifests without native installation.</summary>
	[TestFixture]
	public class CollectionSharedRecipeCompatibilityTests
	{
		/// <summary>Shares identical selected archives while preserving source-policy and legacy-fingerprint validation.</summary>
		[TestCase("exact", 200, "exact", 200, true, true)]
		[TestCase("latest", 199, "exact", 200, true, true)]
		[TestCase("exact", 200, "latest", 199, true, true)]
		[TestCase("exact", 199, "exact", 200, true, false)]
		[TestCase("exact", 200, "exact", 199, true, false)]
		[TestCase("exact", 200, "exact", 200, false, false)]
		public void Match_RetainedSourcesPermitOnlyVerifiedSameFileBehavior(string baselinePolicy, int baselineFile,
			string incomingPolicy, int incomingFile, bool verifiedBaseline, bool compatible)
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-shared-recipe-" + Guid.NewGuid().ToString("N"));
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				var sources = new CollectionsRevisionSourceStore(store);
				NexusCollectionManifestNormalizationResult baseline = Retain(catalog, sources, "100", baselinePolicy, baselineFile);
				NexusCollectionManifestNormalizationResult incoming = Retain(catalog, sources, "101", incomingPolicy, incomingFile);
				var target = CollectionTargetIdentity.FromFingerprint("shared-recipe-target");
				var native = new CollectionNativeModState(new NativeModInstanceIdentity(target, "native-1"),
					"C:\\Mods\\Shared.7z", "Shared.7z", "10", "200", "2.0", "2.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
				var association = new CollectionTargetAssociation(Guid.NewGuid(), baseline.Manifest.Revision, target, CollectionAssociationState.Applied);
				NormalizedCollectionMember oldMember = baseline.Manifest.Members.Single();
				var binding = new CollectionMemberBinding(association, oldMember.IdentityResolution.Key, native.Identity,
					verifiedBaseline ? oldMember.RecipeIdentity : CollectionRecipeIdentity.FromFingerprint("unverified-recipe"),
					CollectionMemberBindingKind.InstalledForCollection);
				var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new[] { native },
					new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
					new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable, new[] { association }, new[] { binding },
					new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
				CollectionEffectiveSelection selection = new CollectionEffectiveSelectionBuilder().Build(incoming.CapabilityReport,
					new CollectionOptionalMemberSelection[0]);
				CollectionNexusSourcePolicyResolution resolved = new CollectionNexusSourcePolicyResolver(sources,
					(domain, modId, fileId, policy, version) => CollectionNexusSourcePolicyFileResolution.Resolved(200)).Resolve(selection);
				NormalizedCollectionMember member = incoming.Manifest.Members.Single();
				CollectionResolvedArtifactChoice choice;
				if (!resolved.ArtifactChoices.TryGetValue(member.IdentityResolution.Key, out choice))
					choice = CollectionResolvedArtifactChoice.Exact(member.Artifact);
				var plan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
					CollectionExecutionPolicy.InstallIntoCurrentSetup(), state.Fingerprint, resolved.Selection.CapabilityReport,
					new[] { new ResolvedCollectionMemberPlan(member, choice) });
				var matcher = new CollectionMemberMatchEngine
				{
					SharedRecipeCompatibility = new CollectionSharedRecipeCompatibility(catalog, sources).AreCompatible
				};
				CollectionMemberMatchSet matches = matcher.Match(plan, state);
				CollectionMemberMatchResult result = matches.Members.Single();

				Assert.That(result.Disposition, Is.EqualTo(compatible ? CollectionMemberMatchDisposition.InstalledCompatible : CollectionMemberMatchDisposition.Blocked));
				Assert.That(result.Reason, Is.EqualTo(compatible ? CollectionMemberMatchReason.CompatibleSharedInstallBehavior : CollectionMemberMatchReason.ConflictingVerifiedRecipe));
				Assert.That(sources.LoadManifest(baseline.Manifest.Revision, baseline.Manifest.Source), Is.EqualTo(baseline.GetRawManifestBytes()));
				Assert.That(binding.VerifiedRecipe, Is.EqualTo(verifiedBaseline ? oldMember.RecipeIdentity : CollectionRecipeIdentity.FromFingerprint("unverified-recipe")));
				if (compatible)
				{
					Assert.That(binding.VerifiedRecipe, Is.Not.EqualTo(member.RecipeIdentity), "Different Collection tags retain their original persisted identities.");
					Assert.That(matches.HasReusableVerifiedBinding(binding, native.Identity, member.RecipeIdentity), Is.True);
				}
			}
			finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
		}

		/// <summary>Installer choices, file lists, exclusions, patches and mod types remain part of compatibility.</summary>
		[TestCase("choices")]
		[TestCase("hashes")]
		[TestCase("fileOverrides")]
		[TestCase("patches")]
		[TestCase("details")]
		public void InstalledBehavior_DoesNotIgnoreInstallationInstructions(string field)
		{
			JObject left = JObject.Parse("{\"domainName\":\"cyberpunk2077\",\"source\":{\"tag\":\"first\"}}");
			JObject right = (JObject)left.DeepClone();
			right["source"]["tag"] = "second";
			Assert.That(NexusCollectionManifestNormalizer.HaveSameInstalledBehavior(left, right), Is.True);
			right[field] = new JObject { ["type"] = "different" };
			Assert.That(NexusCollectionManifestNormalizer.HaveSameInstalledBehavior(left, right), Is.False);
		}

		/// <summary>Retains immutable manifests using the same catalog/source APIs as an imported Collection.</summary>
		private static NexusCollectionManifestNormalizationResult Retain(CollectionsCatalogStore catalog,
			CollectionsRevisionSourceStore sources, string collectionId, string policy, int fileId)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus(collectionId);
			var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "revision-1", 1), null, null, 1);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "Shared framework", "Curator", null), revision);
			string json = "{\"info\":{\"author\":\"Curator\",\"authorUrl\":\"\",\"description\":\"\",\"name\":\"Shared framework\",\"domainName\":\"cyberpunk2077\"}," +
				"\"mods\":[{\"name\":\"Shared\",\"version\":\"" + fileId + "\",\"optional\":false,\"domainName\":\"cyberpunk2077\"," +
				"\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":" + fileId + ",\"tag\":\"" + collectionId +
				"\",\"updatePolicy\":\"" + policy + "\"}}],\"modRules\":[],\"loadOrder\":[]}";
			byte[] bytes = Encoding.UTF8.GetBytes(json);
			NexusCollectionManifestNormalizationResult result = new NexusCollectionManifestNormalizer().Normalize(bytes, revision);
			sources.RetainManifest(result.Manifest, CollectionRevisionSourceInputKind.RawManifest,
				result.Manifest.Source.ContentHash, bytes.LongLength, "collection.json", bytes);
			return result;
		}
	}
}
