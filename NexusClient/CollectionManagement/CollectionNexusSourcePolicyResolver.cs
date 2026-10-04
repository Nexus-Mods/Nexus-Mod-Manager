using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModRepositories;
using Newtonsoft.Json.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>One provider-backed immutable Nexus source-policy decision.</summary>
	internal sealed class CollectionNexusSourcePolicyFileResolution
	{
		private CollectionNexusSourcePolicyFileResolution(int selectedFileId)
		{
			SelectedFileId = selectedFileId;
		}

		internal int SelectedFileId { get; }
		internal bool IsResolved { get { return SelectedFileId > 0; } }

		internal static CollectionNexusSourcePolicyFileResolution Unresolved()
		{
			return new CollectionNexusSourcePolicyFileResolution(0);
		}

		internal static CollectionNexusSourcePolicyFileResolution Resolved(int selectedFileId)
		{
			if (selectedFileId <= 0) throw new ArgumentOutOfRangeException(nameof(selectedFileId));
			return new CollectionNexusSourcePolicyFileResolution(selectedFileId);
		}
	}

	internal delegate CollectionNexusSourcePolicyFileResolution CollectionNexusSourcePolicyFileResolver(
		string domain, int modId, int requestedFileId, string updatePolicy, string requestedVersion);

	/// <summary>
	/// Refines one effective selection with the exact Nexus artifact choices produced while resolving characterized
	/// Vortex <c>latest</c> and <c>prefer</c> source policies.
	/// </summary>
	internal sealed class CollectionNexusSourcePolicyResolution
	{
		private readonly ReadOnlyDictionary<CollectionMemberKey, CollectionResolvedArtifactChoice> _artifactChoices;

		internal CollectionNexusSourcePolicyResolution(CollectionEffectiveSelection selection,
			IDictionary<CollectionMemberKey, CollectionResolvedArtifactChoice> artifactChoices)
		{
			Selection = selection ?? throw new ArgumentNullException(nameof(selection));
			_artifactChoices = new ReadOnlyDictionary<CollectionMemberKey, CollectionResolvedArtifactChoice>(
				new Dictionary<CollectionMemberKey, CollectionResolvedArtifactChoice>(artifactChoices ??
					throw new ArgumentNullException(nameof(artifactChoices))));
		}

		internal CollectionEffectiveSelection Selection { get; }
		internal IReadOnlyDictionary<CollectionMemberKey, CollectionResolvedArtifactChoice> ArtifactChoices { get { return _artifactChoices; } }
	}

	/// <summary>
	/// Resolves the characterized Vortex Nexus source-policy subset before review so every executable member is bound to one
	/// immutable Nexus file. <c>prefer</c> keeps the curator file when available and otherwise accepts a characterized newer
	/// match; <c>latest</c> resolves through the characterized Nexus update-chain rule. Provider uncertainty remains fail-closed.
	/// </summary>
	internal sealed class CollectionNexusSourcePolicyResolver
	{
		internal const string LatestIssueCode = "member.source-policy-needs-resolution";
		internal const string PreferIssueCode = "member.source-policy-prefer-needs-resolution";
		internal const string LatestSubstitutionRuleId = "nexus-source-policy-latest-vortex-2.6.3";
		internal const string PreferFallbackSubstitutionRuleId = "nexus-source-policy-prefer-fallback-vortex-2.6.3";

		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionNexusSourcePolicyFileResolver _fileResolver;

		internal CollectionNexusSourcePolicyResolver(CollectionsRevisionSourceStore revisionSourceStore,
			NexusModsApiRepository repository)
			: this(revisionSourceStore, CreateRepositoryResolver(repository))
		{
		}

		internal CollectionNexusSourcePolicyResolver(CollectionsRevisionSourceStore revisionSourceStore,
			CollectionNexusSourcePolicyFileResolver fileResolver)
		{
			_revisionSourceStore = revisionSourceStore ?? throw new ArgumentNullException(nameof(revisionSourceStore));
			_fileResolver = fileResolver ?? throw new ArgumentNullException(nameof(fileResolver));
		}

		/// <summary>
		/// Resolves selected Nexus policy findings before a durable additive operation is created. A successful substitution
		/// is returned separately from provider-normalized member data so the immutable requested artifact remains provenance.
		/// </summary>
		internal CollectionNexusSourcePolicyResolution Resolve(CollectionEffectiveSelection selection)
		{
			if (selection == null) throw new ArgumentNullException(nameof(selection));
			HashSet<int> pendingOrdinals = SelectedPolicyIssueOrdinals(selection.CapabilityReport);
			var choices = new Dictionary<CollectionMemberKey, CollectionResolvedArtifactChoice>();
			if (pendingOrdinals.Count == 0)
				return new CollectionNexusSourcePolicyResolution(selection, choices);

			byte[] rawManifest = _revisionSourceStore.LoadManifest(selection.Manifest.Revision, selection.Manifest.Source);
			JArray mods = ParseMods(rawManifest);
			var resolvedOrdinals = new HashSet<int>();
			foreach (int ordinal in pendingOrdinals.OrderBy(x => x))
			{
				NormalizedCollectionMember member = selection.Manifest.Members.Single(x => x.SourceOrdinal == ordinal);
				if (!member.IdentityResolution.IsResolved)
					continue;
				JObject source;
				string policy;
				string requestedVersion;
				if (!TryGetPolicyNexusSource(mods, ordinal, member, out source, out policy, out requestedVersion))
					continue;

				string domain = ((string)source["domainName"] ?? (string)((JObject)mods[ordinal])["domainName"] ?? String.Empty).Trim().ToLowerInvariant();
				int modId;
				int fileId;
				if (!Int32.TryParse(Convert.ToString(source["modId"]), out modId) || modId <= 0 ||
					!Int32.TryParse(Convert.ToString(source["fileId"]), out fileId) || fileId <= 0)
					continue;

				CollectionNexusSourcePolicyFileResolution resolution = _fileResolver(domain, modId, fileId, policy, requestedVersion);
				Trace.WriteLine(String.Format("Collection Nexus source-policy resolution: member={0}, policy={1}, nexus={2}/{3}/{4}, selected={5}.",
					ordinal, policy, domain, modId, fileId, resolution == null ? 0 : resolution.SelectedFileId));
				if (resolution == null || !resolution.IsResolved)
					continue;

				CollectionResolvedArtifactChoice choice;
				if (resolution.SelectedFileId == fileId)
				{
					choice = CollectionResolvedArtifactChoice.Exact(member.Artifact);
				}
				else
				{
					var selectedArtifact = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
						NexusCollectionModFileArtifactIdentity.Format(domain, modId, resolution.SelectedFileId), null);
					choice = CollectionResolvedArtifactChoice.SupportedSubstitution(member.Artifact, selectedArtifact,
						StringComparer.Ordinal.Equals(policy, "prefer") ? PreferFallbackSubstitutionRuleId : LatestSubstitutionRuleId);
				}

				choices.Add(member.IdentityResolution.Key, choice);
				resolvedOrdinals.Add(ordinal);
			}

			CollectionCapabilityReport report = ApplyResolvedOrdinals(selection.CapabilityReport, resolvedOrdinals);
			return new CollectionNexusSourcePolicyResolution(selection.WithCapabilityReport(report), choices);
		}

		/// <summary>
		/// Reconstructs the exact artifact currently satisfying an already-installed source-policy member without consulting
		/// Nexus again. The native binding/state pair is the only safe authority for the old side of a revision update.
		/// </summary>
		internal CollectionNexusSourcePolicyResolution ResolveInstalled(CollectionEffectiveSelection selection,
			IEnumerable<CollectionMemberBinding> bindings, CollectionNativeStateIndex currentState)
		{
			if (selection == null) throw new ArgumentNullException(nameof(selection));
			if (bindings == null) throw new ArgumentNullException(nameof(bindings));
			if (currentState == null) throw new ArgumentNullException(nameof(currentState));

			HashSet<int> pendingOrdinals = SelectedPolicyIssueOrdinals(selection.CapabilityReport);
			var choices = new Dictionary<CollectionMemberKey, CollectionResolvedArtifactChoice>();
			if (pendingOrdinals.Count == 0)
				return new CollectionNexusSourcePolicyResolution(selection, choices);

			var bindingsByMember = bindings.Where(x => x != null).GroupBy(x => x.MemberKey).ToDictionary(x => x.Key, x => x.Single());
			byte[] rawManifest = _revisionSourceStore.LoadManifest(selection.Manifest.Revision, selection.Manifest.Source);
			JArray mods = ParseMods(rawManifest);
			var resolvedOrdinals = new HashSet<int>();
			foreach (int ordinal in pendingOrdinals.OrderBy(x => x))
			{
				NormalizedCollectionMember member = selection.Manifest.Members.Single(x => x.SourceOrdinal == ordinal);
				if (!member.IdentityResolution.IsResolved) continue;
				CollectionMemberBinding binding;
				CollectionNativeModState native;
				if (!bindingsByMember.TryGetValue(member.IdentityResolution.Key, out binding) ||
					!currentState.Mods.TryGetValue(binding.NativeMod, out native))
					continue;

				JObject ignoredSource; string policy; string ignoredVersion;
				if (!TryGetPolicyNexusSource(mods, ordinal, member, out ignoredSource, out policy, out ignoredVersion))
					continue;
				string domain; long requestedModId; long requestedFileId; long nativeModId; long nativeFileId;
				if (!NexusCollectionModFileArtifactIdentity.TryParse(member.Artifact, out domain, out requestedModId, out requestedFileId) ||
					!Int64.TryParse(native.NexusModId, out nativeModId) || !Int64.TryParse(native.NexusFileId, out nativeFileId) ||
					nativeModId <= 0 || nativeFileId <= 0 || nativeModId != requestedModId)
					continue;

				CollectionResolvedArtifactChoice choice;
				if (nativeFileId == requestedFileId)
					choice = CollectionResolvedArtifactChoice.Exact(member.Artifact);
				else
				{
					var selected = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
						NexusCollectionModFileArtifactIdentity.Format(domain, requestedModId, nativeFileId), null);
					choice = CollectionResolvedArtifactChoice.SupportedSubstitution(member.Artifact, selected,
						StringComparer.Ordinal.Equals(policy, "prefer") ? PreferFallbackSubstitutionRuleId : LatestSubstitutionRuleId);
				}
				choices.Add(member.IdentityResolution.Key, choice);
				resolvedOrdinals.Add(ordinal);
			}

			CollectionCapabilityReport report = ApplyResolvedOrdinals(selection.CapabilityReport, resolvedOrdinals);
			return new CollectionNexusSourcePolicyResolution(selection.WithCapabilityReport(report), choices);
		}

		/// <summary>
		/// Re-applies exact artifact choices already frozen by another durable review contract. This variant is used by
		/// revision-update restart and deliberately performs no provider lookup.
		/// </summary>
		internal static CollectionEffectiveSelection ReapplyReviewedChoices(CollectionEffectiveSelection selection,
			IReadOnlyDictionary<CollectionMemberKey, CollectionResolvedArtifactChoice> reviewedChoices)
		{
			if (selection == null) throw new ArgumentNullException(nameof(selection));
			if (reviewedChoices == null) throw new ArgumentNullException(nameof(reviewedChoices));
			var resolvedOrdinals = new HashSet<int>();
			foreach (CollectionMemberCapabilityReport memberReport in selection.CapabilityReport.MemberReports)
			{
				NormalizedCollectionMember member = memberReport.Member;
				if (!member.IsSelected || !member.IdentityResolution.IsResolved || memberReport.Issues.Count == 0) continue;
				CollectionResolvedArtifactChoice choice;
				if (!reviewedChoices.TryGetValue(member.IdentityResolution.Key, out choice) || choice == null) continue;
				string policy = null; bool onlyPolicyIssues = true;
				foreach (CollectionCapabilityIssue issue in memberReport.Issues)
				{
					if (StringComparer.Ordinal.Equals(issue.Code, PreferIssueCode)) policy = MergePolicy(policy, "prefer");
					else if (StringComparer.Ordinal.Equals(issue.Code, LatestIssueCode)) policy = MergePolicy(policy, "latest");
					else { onlyPolicyIssues = false; break; }
					if (policy == null) { onlyPolicyIssues = false; break; }
				}
				if (onlyPolicyIssues && ReviewedChoiceMatchesPolicy(member.Artifact, choice.Kind, choice.RequestedArtifact,
					choice.SelectedArtifact, choice.SubstitutionRuleId, policy))
					resolvedOrdinals.Add(member.SourceOrdinal);
			}
			return selection.WithCapabilityReport(ApplyResolvedOrdinals(selection.CapabilityReport, resolvedOrdinals));
		}

		/// <summary>
		/// Re-applies already reviewed Nexus source-policy decisions from durable workflow data. No provider lookup occurs on
		/// restart: requested/selected artifact identity and the substitution rule were frozen by the exact reviewed snapshot.
		/// </summary>
		internal static CollectionEffectiveSelection ReapplyReviewedChoices(CollectionEffectiveSelection selection,
			byte[] rawManifest, IEnumerable<CollectionReviewedMemberSnapshot> reviewedMembers)
		{
			if (selection == null) throw new ArgumentNullException(nameof(selection));
			if (rawManifest == null) throw new ArgumentNullException(nameof(rawManifest));
			if (reviewedMembers == null) throw new ArgumentNullException(nameof(reviewedMembers));

			JArray mods = ParseMods(rawManifest);
			var resolvedOrdinals = new HashSet<int>();
			foreach (CollectionReviewedMemberSnapshot reviewed in reviewedMembers)
			{
				if (reviewed == null)
					continue;
				NormalizedCollectionMember member = selection.Manifest.Members.SingleOrDefault(x => x.SourceOrdinal == reviewed.SourceOrdinal);
				JObject ignored;
				string policy;
				string ignoredVersion;
				if (member == null || !TryGetPolicyNexusSource(mods, reviewed.SourceOrdinal, member, out ignored, out policy, out ignoredVersion))
					continue;
				if (ReviewedChoiceMatchesPolicy(member.Artifact, reviewed.ArtifactChoiceKind, reviewed.RequestedArtifact,
					reviewed.SelectedArtifact, reviewed.SubstitutionRuleId, policy))
					resolvedOrdinals.Add(reviewed.SourceOrdinal);
			}
			return selection.WithCapabilityReport(ApplyResolvedOrdinals(selection.CapabilityReport, resolvedOrdinals));
		}

		/// <summary>Returns whether one normalized ActionRequired source-policy finding is exactly covered by the reviewed plan.</summary>
		internal static bool IsReviewedResolution(ResolvedCollectionMemberPlan member,
			CollectionMemberCapabilityReport sourceReport, CollectionMemberCapabilityReport reviewedReport)
		{
			if (member == null || sourceReport == null || reviewedReport == null ||
				reviewedReport.Status != CollectionCompatibilityStatus.Supported ||
				sourceReport.Status != CollectionCompatibilityStatus.ActionRequired || sourceReport.Issues.Count == 0)
				return false;

			string policy = null;
			foreach (CollectionCapabilityIssue issue in sourceReport.Issues)
			{
				if (StringComparer.Ordinal.Equals(issue.Code, PreferIssueCode))
					policy = MergePolicy(policy, "prefer");
				else if (StringComparer.Ordinal.Equals(issue.Code, LatestIssueCode))
					policy = MergePolicy(policy, "latest");
				else
					return false;
				if (policy == null)
					return false;
			}

			return ReviewedChoiceMatchesPolicy(member.ArtifactChoice.RequestedArtifact, member.ArtifactChoice.Kind, member.ArtifactChoice.RequestedArtifact,
				member.ArtifactChoice.SelectedArtifact, member.ArtifactChoice.SubstitutionRuleId, policy);
		}

		internal static bool IsResolvableIssue(CollectionCapabilityIssue issue)
		{
			return issue != null && (StringComparer.Ordinal.Equals(issue.Code, PreferIssueCode) ||
				StringComparer.Ordinal.Equals(issue.Code, LatestIssueCode));
		}

		private static string MergePolicy(string current, string next)
		{
			return current == null || StringComparer.Ordinal.Equals(current, next) ? next : null;
		}

		private static bool ReviewedChoiceMatchesPolicy(CollectionArtifactReference normalizedArtifact,
			CollectionResolvedArtifactChoiceKind choiceKind, CollectionArtifactReference requestedArtifact,
			CollectionArtifactReference selectedArtifact, string substitutionRuleId, string policy)
		{
			if (normalizedArtifact == null || requestedArtifact == null || selectedArtifact == null ||
				!normalizedArtifact.Equals(requestedArtifact))
				return false;
			if (choiceKind == CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact)
				return requestedArtifact.Equals(selectedArtifact) && substitutionRuleId == null;
			if (choiceKind != CollectionResolvedArtifactChoiceKind.SupportedSubstitution || requestedArtifact.Equals(selectedArtifact))
				return false;

			string requestedDomain;
			long requestedModId;
			long requestedFileId;
			string selectedDomain;
			long selectedModId;
			long selectedFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(requestedArtifact, out requestedDomain, out requestedModId, out requestedFileId) ||
				!NexusCollectionModFileArtifactIdentity.TryParse(selectedArtifact, out selectedDomain, out selectedModId, out selectedFileId) ||
				!StringComparer.Ordinal.Equals(requestedDomain, selectedDomain) || requestedModId != selectedModId || requestedFileId == selectedFileId)
				return false;

			string expectedRule = StringComparer.Ordinal.Equals(policy, "prefer")
				? PreferFallbackSubstitutionRuleId
				: LatestSubstitutionRuleId;
			return StringComparer.Ordinal.Equals(substitutionRuleId, expectedRule);
		}

		private static CollectionCapabilityReport ApplyResolvedOrdinals(CollectionCapabilityReport report, HashSet<int> resolvedOrdinals)
		{
			if (resolvedOrdinals == null || resolvedOrdinals.Count == 0) return report;
			return report.FilterDeclaredIssues(issue =>
				!IsResolvableIssue(issue) || !issue.SourceOrdinal.HasValue || !resolvedOrdinals.Contains(issue.SourceOrdinal.Value));
		}

		private static HashSet<int> SelectedPolicyIssueOrdinals(CollectionCapabilityReport report)
		{
			var result = new HashSet<int>();
			foreach (CollectionMemberCapabilityReport memberReport in report.MemberReports)
			{
				if (!memberReport.Member.IsSelected) continue;
				foreach (CollectionCapabilityIssue issue in memberReport.Issues)
					if (IsResolvableIssue(issue) && issue.SourceOrdinal.HasValue)
						result.Add(issue.SourceOrdinal.Value);
			}
			return result;
		}

		private static bool TryGetPolicyNexusSource(JArray mods, int ordinal, NormalizedCollectionMember member,
			out JObject source, out string policy, out string requestedVersion)
		{
			source = null;
			policy = null;
			requestedVersion = null;
			if (ordinal < 0 || ordinal >= mods.Count || member == null || member.Artifact == null ||
				!StringComparer.Ordinal.Equals(member.Artifact.Scheme, NexusCollectionModFileArtifactIdentity.Scheme))
				return false;
			JObject rawMember = mods[ordinal] as JObject;
			if (rawMember == null) return false;
			source = rawMember["source"] as JObject;
			if (source == null || !StringComparer.Ordinal.Equals((string)source["type"], "nexus"))
				return false;
			policy = ((string)source["updatePolicy"] ?? String.Empty).Trim();
			if (!StringComparer.Ordinal.Equals(policy, "latest") && !StringComparer.Ordinal.Equals(policy, "prefer"))
				return false;
			requestedVersion = (string)rawMember["version"];
			return true;
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

		private static CollectionNexusSourcePolicyFileResolver CreateRepositoryResolver(NexusModsApiRepository repository)
		{
			if (repository == null) throw new ArgumentNullException(nameof(repository));
			return (domain, modId, fileId, updatePolicy, requestedVersion) =>
			{
				if (!StringComparer.OrdinalIgnoreCase.Equals(domain, repository.GameDomainName))
					return CollectionNexusSourcePolicyFileResolution.Unresolved();
				int selectedFileId;
				if (!repository.TryResolveCollectionSourcePolicyFile(modId, fileId, updatePolicy, requestedVersion, out selectedFileId))
					return CollectionNexusSourcePolicyFileResolution.Unresolved();
				return CollectionNexusSourcePolicyFileResolution.Resolved(selectedFileId);
			};
		}
	}
}
