using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement.Witcher3;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Composes canonical target resolution and one C6.1 native-state capture into the production additive resolved-plan builder.
	/// </summary>
	/// <remarks>
	/// This service performs read-only target/native-state observation plus Collections metadata journaling only. It does not acquire
	/// member archives, translate Collection recipes, create native installer tasks or mutate the game/native ownership stores.
	/// </remarks>
	public sealed class CollectionAdditivePlanPreparationService
	{
		private readonly CollectionTargetIdentityResolver _targetIdentityResolver;
		private readonly CollectionNativeStateReader _nativeStateReader;
		private readonly CollectionResolvedPlanBuilder _resolvedPlanBuilder;
		private readonly CollectionNexusSourcePolicyResolver _sourcePolicyResolver;

		/// <summary>
		/// Creates the C6.15.4 preparation bridge over the existing C4 target resolver, C6.1 state reader and resolved-plan builder.
		/// </summary>
		public CollectionAdditivePlanPreparationService(CollectionTargetIdentityResolver targetIdentityResolver,
			CollectionNativeStateReader nativeStateReader, CollectionResolvedPlanBuilder resolvedPlanBuilder)
			: this(targetIdentityResolver, nativeStateReader, resolvedPlanBuilder, null)
		{
		}

		internal CollectionAdditivePlanPreparationService(CollectionTargetIdentityResolver targetIdentityResolver,
			CollectionNativeStateReader nativeStateReader, CollectionResolvedPlanBuilder resolvedPlanBuilder,
			CollectionNexusSourcePolicyResolver sourcePolicyResolver)
		{
			_targetIdentityResolver = targetIdentityResolver ?? throw new ArgumentNullException(nameof(targetIdentityResolver));
			_nativeStateReader = nativeStateReader ?? throw new ArgumentNullException(nameof(nativeStateReader));
			_resolvedPlanBuilder = resolvedPlanBuilder ?? throw new ArgumentNullException(nameof(resolvedPlanBuilder));
			_sourcePolicyResolver = sourcePolicyResolver;
		}

		/// <summary>
		/// Resolves the canonical target/storage authority, captures exactly one C6.1 state index and builds a new additive plan.
		/// </summary>
		public CollectionAdditivePlanBuildResult Prepare(CollectionEffectiveSelection effectiveSelection, GameStoragePathSet targetPaths)
		{
			return Prepare(effectiveSelection, targetPaths, null);
		}

		/// <summary>Refreshes a reopened plan without resolving source policies or selecting new artifacts.</summary>
		internal CollectionAdditivePlanBuildResult Refresh(CollectionOperation operation, ResolvedCollectionPlan previous,
			GameStoragePathSet targetPaths)
		{
			Witcher3NativeMutationReview.RequireCollectionSupport(targetPaths?.GameId);
			CollectionTargetAuthority authority = _targetIdentityResolver.Resolve(targetPaths);
			if (!authority.Target.Equals(previous.Target))
				throw new InvalidOperationException("The Collection target changed. Open the Collection for the intended game again.");
			return _resolvedPlanBuilder.Rebuild(operation, previous, _nativeStateReader.Capture(authority.Target));
		}

		/// <summary>Resolves only newly selected prerequisite policies and preserves the original target capture and artifact choices.</summary>
		internal CollectionAdditivePlanBuildResult ExpandPrerequisites(CollectionAdditivePlanBuildResult previous,
			CollectionEffectiveSelection selection)
		{
			CollectionNexusSourcePolicyResolution resolution = _sourcePolicyResolver == null ? null : _sourcePolicyResolver.Resolve(selection);
			return _resolvedPlanBuilder.ExpandPrerequisites(previous, resolution == null ? selection : resolution.Selection,
				resolution == null ? new Dictionary<CollectionMemberKey, CollectionResolvedArtifactChoice>() : resolution.ArtifactChoices);
		}
		/// <summary>Starts a new immutable reviewed plan after an explicit alternate external-archive decision.</summary>
		internal CollectionAdditivePlanBuildResult Prepare(CollectionEffectiveSelection effectiveSelection,
			GameStoragePathSet targetPaths, CollectionAlternateArchiveCandidate alternate, CollectionExecutionPolicy policy = null)
		{
			if (effectiveSelection == null)
				throw new ArgumentNullException(nameof(effectiveSelection));
			if (targetPaths == null)
				throw new ArgumentNullException(nameof(targetPaths));
			Witcher3NativeMutationReview.RequireCollectionSupport(targetPaths.GameId);

			// Selecting a different EXTERNAL archive creates a new immutable review, but the user's
			// unchanged manifest has already had its Nexus prefer files resolved. Replaying those
			// exact decisions avoids another serial Nexus API lookup per mod during every manual pause.
			// Revalidate each member against the current normalized policy first; all other policies still resolve live.
			var carriedNexusChoices = new Dictionary<CollectionMemberKey, CollectionResolvedArtifactChoice>();
			if (alternate != null && alternate.PreviousNexusManifestSource != null)
			{
				if (!alternate.PreviousNexusManifestSource.Equals(effectiveSelection.Manifest.Source) ||
					!alternate.Request.Revision.Equals(effectiveSelection.Manifest.Revision))
					throw new InvalidOperationException("A previous Nexus policy decision belongs to a different Collection revision or manifest source.");
				foreach (CollectionResolvedArtifactChoice previous in alternate.PreviousNexusChoices)
				{
					NormalizedCollectionMember member = effectiveSelection.Manifest.Members.SingleOrDefault(x =>
						x.IsSelected && x.IdentityResolution.IsResolved && x.Artifact.Equals(previous.RequestedArtifact));
					if (member == null || carriedNexusChoices.ContainsKey(member.IdentityResolution.Key))
						throw new InvalidOperationException("A retained Nexus policy decision does not match one unique selected Collection member.");
					CollectionMemberCapabilityReport report = effectiveSelection.CapabilityReport.MemberReports.Single(x =>
						x.Member.SourceOrdinal == member.SourceOrdinal);
					if (report.Issues.Count == 0 || !report.Issues.All(x => StringComparer.Ordinal.Equals(x.Code, CollectionNexusSourcePolicyResolver.PreferIssueCode)))
						throw new InvalidOperationException("A retained Nexus policy decision no longer refers exclusively to a resolvable provider policy.");
					carriedNexusChoices.Add(member.IdentityResolution.Key, previous);
				}
				effectiveSelection = CollectionNexusSourcePolicyResolver.ReapplyReviewedChoices(effectiveSelection, carriedNexusChoices);
				foreach (CollectionMemberKey key in carriedNexusChoices.Keys)
				{
					CollectionMemberCapabilityReport resolved = effectiveSelection.CapabilityReport.MemberReports.Single(x =>
						x.Member.IdentityResolution.IsResolved && x.Member.IdentityResolution.Key.Equals(key));
					if (resolved.Status != CollectionCompatibilityStatus.Supported)
						throw new InvalidOperationException("A retained Nexus policy decision cannot be safely replayed against the current manifest.");
				}
				CollectionPerformanceMetrics.RecordNexusSourcePolicyReusedChoices(carriedNexusChoices.Count);
			}
			CollectionNexusSourcePolicyResolution sourcePolicyResolution = null;
			if (_sourcePolicyResolver != null)
			{
				sourcePolicyResolution = _sourcePolicyResolver.Resolve(effectiveSelection);
				effectiveSelection = sourcePolicyResolution.Selection;
			}

			var choices = sourcePolicyResolution == null
				? new Dictionary<CollectionMemberKey, CollectionResolvedArtifactChoice>()
				: sourcePolicyResolution.ArtifactChoices.ToDictionary(x => x.Key, x => x.Value);
			foreach (KeyValuePair<CollectionMemberKey, CollectionResolvedArtifactChoice> carried in carriedNexusChoices)
			{
				if (choices.ContainsKey(carried.Key))
					throw new InvalidOperationException("A retained Nexus policy decision conflicts with a newly resolved policy decision.");
				choices.Add(carried.Key, carried.Value);
			}
			if (alternate != null)
			{
				foreach (CollectionResolvedArtifactChoice prior in alternate.PreviousAlternatives)
				{
					if (!CollectionResolvedArtifactChoice.IsValidUserSelectedExternalAlternative(prior))
						throw new InvalidOperationException("A previous alternate archive no longer has the approved exact external-content identity.");
					NormalizedCollectionMember priorMember = effectiveSelection.Manifest.Members.SingleOrDefault(x =>
						x.IsSelected && x.IdentityResolution.IsResolved && x.Artifact.Equals(prior.RequestedArtifact));
					if (priorMember == null || choices.ContainsKey(priorMember.IdentityResolution.Key) ||
						priorMember.IdentityResolution.Key.Equals(alternate.Request.MemberKey))
						throw new InvalidOperationException("A previous alternate archive decision does not match one unique selected member.");
					choices.Add(priorMember.IdentityResolution.Key, prior);
				}
				NormalizedCollectionMember member = effectiveSelection.Manifest.Members.SingleOrDefault(x =>
					x.IsSelected && x.IdentityResolution.IsResolved && x.IdentityResolution.Key.Equals(alternate.Request.MemberKey));
				string originalMd5; long originalLength; string selectedMd5; long selectedLength;
				if (member == null || !alternate.Request.Revision.Equals(effectiveSelection.Manifest.Revision) ||
					!member.Artifact.Equals(alternate.Request.SelectedArtifact) ||
					!CollectionExternalArtifactIdentity.TryParse(member.Artifact, out originalMd5, out originalLength) ||
					!CollectionExternalArtifactIdentity.TryParse(alternate.SelectedArtifact, out selectedMd5, out selectedLength) ||
					(selectedLength == originalLength && StringComparer.Ordinal.Equals(selectedMd5, originalMd5)) ||
					alternate.SelectedArtifact.ExpectedContentHash == null ||
					!alternate.SelectedArtifact.ExpectedContentHash.Equals(alternate.Artifact.ContentHash) ||
					selectedLength != alternate.Artifact.ByteLength ||
					choices.ContainsKey(alternate.Request.MemberKey))
					throw new InvalidOperationException("The user-selected external replacement does not match the selected retained Collection member or its sealed content identity.");
				CollectionResolvedArtifactChoice proposed = CollectionResolvedArtifactChoice.SupportedSubstitution(
					member.Artifact, alternate.SelectedArtifact, CollectionResolvedArtifactChoice.UserSelectedExternalAlternativeRuleId);
				if (!CollectionResolvedArtifactChoice.IsValidUserSelectedExternalAlternative(proposed))
					throw new InvalidOperationException("An alternate archive requires verified SHA-256 and an original exact external MD5/size.");
				choices.Add(alternate.Request.MemberKey, proposed);
			}

			CollectionTargetAuthority authority = _targetIdentityResolver.Resolve(targetPaths);
			if (alternate != null && !alternate.Request.Target.Equals(authority.Target))
				throw new InvalidOperationException("The alternate archive was chosen for a different Collection target.");
			CollectionNativeStateIndex nativeState = _nativeStateReader.Capture(authority.Target);
			return _resolvedPlanBuilder.Build(effectiveSelection, authority.Target, nativeState, choices, policy);
		}
	}
}
