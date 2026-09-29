using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModRepositories;
using Newtonsoft.Json.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Bounded outcome for resolving the first Vortex <c>prefer</c> compatibility slice.</summary>
	internal enum CollectionPreferExactResolutionStatus
	{
		Unknown = 0,
		ExactAvailable = 1,
		FallbackRequired = 2
	}

	internal delegate CollectionPreferExactResolutionStatus CollectionPreferExactFileResolver(string domain, int modId, int fileId);

	/// <summary>
	/// Resolves Vortex Prefer Exact conservatively: use the manifest-requested Nexus file only when Nexus proves that
	/// exact file is still available. This slice never guesses or chooses a successor file.
	/// </summary>
	internal sealed class CollectionNexusPreferExactPolicyResolver
	{
		internal const string PreferIssueCode = "member.source-policy-prefer-needs-resolution";
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionPreferExactFileResolver _fileResolver;

		internal CollectionNexusPreferExactPolicyResolver(CollectionsRevisionSourceStore revisionSourceStore,
			NexusModsApiRepository repository)
			: this(revisionSourceStore, CreateRepositoryResolver(repository))
		{
		}

		internal CollectionNexusPreferExactPolicyResolver(CollectionsRevisionSourceStore revisionSourceStore,
			CollectionPreferExactFileResolver fileResolver)
		{
			_revisionSourceStore = revisionSourceStore ?? throw new ArgumentNullException(nameof(revisionSourceStore));
			_fileResolver = fileResolver ?? throw new ArgumentNullException(nameof(fileResolver));
		}

		/// <summary>
		/// Resolves selected Prefer Exact findings before any durable additive operation is created. Latest remains
		/// unresolved, and prefer remains action-required whenever the exact file is archived/deleted/not-found or the
		/// provider result cannot be established.
		/// </summary>
		internal CollectionEffectiveSelection Resolve(CollectionEffectiveSelection selection)
		{
			if (selection == null) throw new ArgumentNullException(nameof(selection));
			HashSet<int> pendingOrdinals = SelectedPreferIssueOrdinals(selection.CapabilityReport);
			if (pendingOrdinals.Count == 0) return selection;

			byte[] rawManifest = _revisionSourceStore.LoadManifest(selection.Manifest.Revision, selection.Manifest.Source);
			JArray mods = ParseMods(rawManifest);
			var resolvedOrdinals = new HashSet<int>();
			foreach (int ordinal in pendingOrdinals.OrderBy(x => x))
			{
				NormalizedCollectionMember member = selection.Manifest.Members.Single(x => x.SourceOrdinal == ordinal);
				JObject source;
				if (!TryGetPreferNexusSource(mods, ordinal, member, out source))
					continue;

				string domain = ((string)source["domainName"] ?? (string)((JObject)mods[ordinal])["domainName"] ?? String.Empty).Trim().ToLowerInvariant();
				int modId;
				int fileId;
				if (!Int32.TryParse(Convert.ToString(source["modId"]), out modId) || modId <= 0 ||
					!Int32.TryParse(Convert.ToString(source["fileId"]), out fileId) || fileId <= 0)
					continue;

				CollectionPreferExactResolutionStatus status = _fileResolver(domain, modId, fileId);
				Trace.WriteLine(String.Format("Collection Prefer Exact resolution: member={0}, nexus={1}/{2}/{3}, result={4}.",
					ordinal, domain, modId, fileId, status));
				if (status == CollectionPreferExactResolutionStatus.ExactAvailable)
					resolvedOrdinals.Add(ordinal);
			}

			return ApplyResolvedOrdinals(selection, resolvedOrdinals);
		}

		/// <summary>
		/// Re-applies an already reviewed Prefer Exact decision from durable v2 plan data. No network call occurs on
		/// restart: an exact choice could only have been reviewed after the provider resolver proved it before review.
		/// </summary>
		internal static CollectionEffectiveSelection ReapplyReviewedExactChoices(CollectionEffectiveSelection selection,
			byte[] rawManifest, IEnumerable<CollectionReviewedMemberSnapshot> reviewedMembers)
		{
			if (selection == null) throw new ArgumentNullException(nameof(selection));
			if (rawManifest == null) throw new ArgumentNullException(nameof(rawManifest));
			if (reviewedMembers == null) throw new ArgumentNullException(nameof(reviewedMembers));

			JArray mods = ParseMods(rawManifest);
			var resolvedOrdinals = new HashSet<int>();
			foreach (CollectionReviewedMemberSnapshot reviewed in reviewedMembers)
			{
				if (reviewed == null || reviewed.ArtifactChoiceKind != CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact ||
					!reviewed.RequestedArtifact.Equals(reviewed.SelectedArtifact))
					continue;
				NormalizedCollectionMember member = selection.Manifest.Members.SingleOrDefault(x => x.SourceOrdinal == reviewed.SourceOrdinal);
				JObject ignored;
				if (member != null && TryGetPreferNexusSource(mods, reviewed.SourceOrdinal, member, out ignored))
					resolvedOrdinals.Add(reviewed.SourceOrdinal);
			}
			return ApplyResolvedOrdinals(selection, resolvedOrdinals);
		}

		private static CollectionEffectiveSelection ApplyResolvedOrdinals(CollectionEffectiveSelection selection,
			HashSet<int> resolvedOrdinals)
		{
			if (resolvedOrdinals == null || resolvedOrdinals.Count == 0) return selection;
			CollectionCapabilityReport report = selection.CapabilityReport.FilterDeclaredIssues(issue =>
				!StringComparer.Ordinal.Equals(issue.Code, PreferIssueCode) || !issue.SourceOrdinal.HasValue ||
				!resolvedOrdinals.Contains(issue.SourceOrdinal.Value));
			return selection.WithCapabilityReport(report);
		}

		private static HashSet<int> SelectedPreferIssueOrdinals(CollectionCapabilityReport report)
		{
			var result = new HashSet<int>();
			foreach (CollectionMemberCapabilityReport memberReport in report.MemberReports)
			{
				if (!memberReport.Member.IsSelected) continue;
				foreach (CollectionCapabilityIssue issue in memberReport.Issues)
					if (StringComparer.Ordinal.Equals(issue.Code, PreferIssueCode) && issue.SourceOrdinal.HasValue)
						result.Add(issue.SourceOrdinal.Value);
			}
			return result;
		}

		private static bool TryGetPreferNexusSource(JArray mods, int ordinal, NormalizedCollectionMember member, out JObject source)
		{
			source = null;
			if (ordinal < 0 || ordinal >= mods.Count || member == null || member.Artifact == null ||
				!StringComparer.Ordinal.Equals(member.Artifact.Scheme, "nexus-mod-file"))
				return false;
			JObject rawMember = mods[ordinal] as JObject;
			source = rawMember?["source"] as JObject;
			return source != null && StringComparer.Ordinal.Equals((string)source["type"], "nexus") &&
				StringComparer.Ordinal.Equals((string)source["updatePolicy"], "prefer");
		}

		private static JArray ParseMods(byte[] rawManifest)
		{
			string json = Encoding.UTF8.GetString(rawManifest);
			JObject root = JObject.Parse(json);
			JArray mods = root["mods"] as JArray;
			if (mods == null)
				throw new InvalidOperationException("The retained Collection manifest no longer contains its normalized mods array.");
			return mods;
		}

		private static CollectionPreferExactFileResolver CreateRepositoryResolver(NexusModsApiRepository repository)
		{
			if (repository == null) throw new ArgumentNullException(nameof(repository));
			return (domain, modId, fileId) =>
			{
				if (!StringComparer.OrdinalIgnoreCase.Equals(domain, repository.GameDomainName))
					return CollectionPreferExactResolutionStatus.Unknown;
				bool exactAvailable;
				if (!repository.TryResolveCollectionPreferExactFile(modId, fileId, out exactAvailable))
					return CollectionPreferExactResolutionStatus.Unknown;
				return exactAvailable
					? CollectionPreferExactResolutionStatus.ExactAvailable
					: CollectionPreferExactResolutionStatus.FallbackRequired;
			};
		}
	}
}
