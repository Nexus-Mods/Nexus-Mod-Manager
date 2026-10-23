using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Proves shared Cyberpunk installation behavior from retained sources without rewriting legacy recipe identities.</summary>
	internal sealed class CollectionSharedRecipeCompatibility
	{
		private readonly CollectionsCatalogStore _catalog;
		private readonly CollectionsRevisionSourceStore _sources;
		private readonly Dictionary<CollectionRevisionIdentity, NexusCollectionManifestNormalizationResult> _manifests =
			new Dictionary<CollectionRevisionIdentity, NexusCollectionManifestNormalizationResult>();

		/// <summary>Creates a read-only compatibility boundary over exact retained Collection manifests.</summary>
		internal CollectionSharedRecipeCompatibility(CollectionsCatalogStore catalog, CollectionsRevisionSourceStore sources)
		{
			_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
			_sources = sources ?? throw new ArgumentNullException(nameof(sources));
		}

		/// <summary>Accepts only the same installed Nexus file with matching installation instructions and verified legacy provenance.</summary>
		internal bool AreCompatible(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			CollectionNativeModState native, CollectionMemberBinding binding)
		{
			if (binding.Association.State != CollectionAssociationState.Applied ||
				binding.Association.Revision.Collection.Equals(plan.Revision.Collection) ||
				!binding.NativeMod.Equals(native.Identity)) return false;
			try
			{
				NexusCollectionManifestNormalizationResult incoming = Load(plan.Revision);
				NexusCollectionManifestNormalizationResult installed = Load(binding.Association.Revision);
				if (incoming == null || installed == null || !incoming.Manifest.Source.Equals(plan.ManifestSource)) return false;
				NormalizedCollectionMember requested = incoming.Manifest.Members.SingleOrDefault(x =>
					x.IdentityResolution.IsResolved && x.IdentityResolution.Key.Equals(member.MemberKey));
				NormalizedCollectionMember baseline = installed.Manifest.Members.SingleOrDefault(x =>
					x.IdentityResolution.IsResolved && x.IdentityResolution.Key.Equals(binding.MemberKey));
				if (requested == null || baseline == null || !requested.RecipeIdentity.Equals(member.RecipeIdentity) ||
					!baseline.RecipeIdentity.Equals(binding.VerifiedRecipe) || !requested.Artifact.Equals(member.ArtifactChoice.RequestedArtifact)) return false;
				if (baseline.Artifact.StableId == member.ArtifactChoice.SelectedArtifact.StableId)
				{
					CollectionArtifactReference selected = member.ArtifactChoice.SelectedArtifact;
					if ((selected.ExpectedContentHash != null && baseline.Artifact.ExpectedContentHash != null &&
						!selected.ExpectedContentHash.Equals(baseline.Artifact.ExpectedContentHash)) ||
						(selected.ExpectedByteLength.HasValue && baseline.Artifact.ExpectedByteLength.HasValue &&
						 selected.ExpectedByteLength != baseline.Artifact.ExpectedByteLength)) return false;
				}

				CollectionMemberCapabilityReport sourceReport = incoming.CapabilityReport.MemberReports.Single(x => x.Member.SourceOrdinal == requested.SourceOrdinal);
				CollectionMemberCapabilityReport reviewedReport = plan.CapabilityReport.MemberReports.Single(x => x.Member.SourceOrdinal == member.SourceOrdinal);
				if (sourceReport.Status != CollectionCompatibilityStatus.Supported &&
					!CollectionNexusSourcePolicyResolver.IsReviewedResolution(member, sourceReport, reviewedReport)) return false;
				CollectionMemberCapabilityReport baselineReport = installed.CapabilityReport.MemberReports.Single(x => x.Member.SourceOrdinal == baseline.SourceOrdinal);
				if (baselineReport.Issues.Any(x => x.Code != CollectionNexusSourcePolicyResolver.LatestIssueCode &&
					x.Code != CollectionNexusSourcePolicyResolver.PreferIssueCode)) return false;

				JObject incomingRaw = ReadMember(incoming, requested.SourceOrdinal);
				JObject installedRaw = ReadMember(installed, baseline.SourceOrdinal);
				return UsesInstalledArtifact(member.ArtifactChoice.SelectedArtifact, incomingRaw, native) &&
					UsesInstalledArtifact(member.ArtifactChoice.SelectedArtifact, installedRaw, native) &&
					NexusCollectionManifestNormalizer.HaveSameInstalledBehavior(incomingRaw, installedRaw);
			}
			catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is JsonException)
			{
				Trace.WriteLine("Collection shared recipe compatibility unavailable: " + ex.Message);
				return false;
			}
		}

		/// <summary>Retains only verified immutable manifests; missing or unsupported sources preserve the existing conflict gate.</summary>
		private NexusCollectionManifestNormalizationResult Load(CollectionRevisionIdentity revision)
		{
			lock (_manifests)
			{
				NexusCollectionManifestNormalizationResult result;
				if (_manifests.TryGetValue(revision, out result))
				{
					_sources.RequireVerifiedManifestSource(revision, result.Manifest.Source);
					return result;
				}
				CollectionRevision retainedRevision = _catalog.GetRevision(revision);
				if (retainedRevision == null) return null;
				byte[] bytes = _sources.LoadManifest(revision, NexusCollectionManifestNormalizer.SchemaIdentity, NexusCollectionManifestNormalizer.NormalizerVersion);
				result = new NexusCollectionManifestNormalizer().Normalize(bytes, retainedRevision);
				if (!result.Manifest.IsMemberSetComplete || result.CapabilityReport.ManifestIssues.Count != 0) return null;
				if (_manifests.Count >= 32) _manifests.Clear();
				_manifests.Add(revision, result);
				return result;
			}
		}

		/// <summary>Reads the raw instructions only after the normalizer has verified their exact persisted recipe identity.</summary>
		private static JObject ReadMember(NexusCollectionManifestNormalizationResult manifest, int ordinal)
		{
			JObject root = JObject.Parse(Encoding.UTF8.GetString(manifest.GetRawManifestBytes()).TrimStart('\uFEFF'));
			return root["mods"][ordinal] as JObject;
		}

		/// <summary>Requires the exact installed file; an older requested file is eligible only through an existing latest/prefer policy.</summary>
		private static bool UsesInstalledArtifact(CollectionArtifactReference selected, JObject raw, CollectionNativeModState native)
		{
			string domain; long modId; long fileId;
			if (raw == null || !NexusCollectionModFileArtifactIdentity.TryParse(selected, out domain, out modId, out fileId) ||
				!StringComparer.OrdinalIgnoreCase.Equals(domain, "cyberpunk2077") ||
				!StringComparer.OrdinalIgnoreCase.Equals((string)raw["domainName"], domain) ||
				!StringComparer.Ordinal.Equals(native.NexusModId, modId.ToString(System.Globalization.CultureInfo.InvariantCulture)) ||
				!StringComparer.Ordinal.Equals(native.NexusFileId, fileId.ToString(System.Globalization.CultureInfo.InvariantCulture))) return false;
			JObject source = raw["source"] as JObject;
			if (source == null || (string)source["type"] != "nexus" || (long?)source["modId"] != modId) return false;
			string policy = (string)source["updatePolicy"];
			return (long?)source["fileId"] == fileId || policy == "latest" || policy == "prefer";
		}
	}
}
