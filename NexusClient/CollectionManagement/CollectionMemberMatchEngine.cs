using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Matches selected Collection members against one immutable C6.1 native-state index and already verified C4 archives.
	/// </summary>
	/// <remarks>
	/// This component is deliberately read-only. It does not acquire content, install/reinstall mods, write provenance,
	/// choose dependency order or resolve file/plugin conflicts. Those responsibilities remain in later C6 phases.
	/// </remarks>
	public sealed class CollectionMemberMatchEngine
	{
		/// <summary>Matches a resolved plan without any already verified archive inputs.</summary>
		public CollectionMemberMatchSet Match(ResolvedCollectionPlan plan, CollectionNativeStateIndex nativeState)
		{
			return Match(plan, nativeState, Enumerable.Empty<CollectionVerifiedArchive>());
		}

		/// <summary>
		/// Matches the complete selected closure against current native state and exact verified archive inputs.
		/// </summary>
		public CollectionMemberMatchSet Match(ResolvedCollectionPlan plan, CollectionNativeStateIndex nativeState,
			IEnumerable<CollectionVerifiedArchive> verifiedArchives)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));
			if (nativeState == null)
				throw new ArgumentNullException(nameof(nativeState));
			if (verifiedArchives == null)
				throw new ArgumentNullException(nameof(verifiedArchives));
			if (!plan.Target.Equals(nativeState.Target))
				throw new ArgumentException("The resolved plan and native-state index must describe the same target.", nameof(nativeState));

			Dictionary<CollectionMemberKey, CollectionVerifiedArchive> archivesByMember =
				ValidateVerifiedArchives(plan, verifiedArchives);

			if (plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				return BuildUniformBlockedSet(plan, nativeState, archivesByMember, CollectionMemberMatchReason.UnsupportedExecutionPolicy);

			if (!plan.CurrentStateFingerprint.Equals(nativeState.Fingerprint))
				return BuildUniformBlockedSet(plan, nativeState, archivesByMember, CollectionMemberMatchReason.CurrentStateChanged);

			if (nativeState.AssociationCoverage != CollectionNativeStateCoverage.Complete)
				return BuildUniformBlockedSet(plan, nativeState, archivesByMember, CollectionMemberMatchReason.AssociationStateUnavailable);

			MatchingContext context = MatchingContext.Create(plan, nativeState);
			var results = new List<CollectionMemberMatchResult>(plan.SelectedMembers.Count);
			foreach (ResolvedCollectionMemberPlan member in plan.SelectedMembers)
			{
				CollectionVerifiedArchive archive;
				archivesByMember.TryGetValue(member.MemberKey, out archive);
				results.Add(MatchMember(nativeState, context, member, archive));
			}
			return new CollectionMemberMatchSet(plan, nativeState, results);
		}

		private static CollectionMemberMatchResult MatchMember(CollectionNativeStateIndex nativeState, MatchingContext context,
			ResolvedCollectionMemberPlan member, CollectionVerifiedArchive verifiedArchive)
		{
			string gameDomain;
			long selectedModId;
			long selectedFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact,
				out gameDomain, out selectedModId, out selectedFileId) || String.IsNullOrWhiteSpace(gameDomain))
			{
				return Result(member, CollectionMemberMatchDisposition.Blocked,
					CollectionMemberMatchReason.UnsupportedArtifactIdentity, null, null, verifiedArchive);
			}

			List<CollectionMemberBinding> directBindings = context.GetDirectBindings(member.MemberKey);
			var candidates = new Dictionary<NativeModInstanceIdentity, CollectionNativeModState>();
			var exactArtifactCandidates = new HashSet<NativeModInstanceIdentity>();

			foreach (CollectionNativeModState mod in context.GetNativeMods(selectedModId))
			{
				candidates[mod.Identity] = mod;
				long nativeFileId;
				if (TryParsePositiveId(mod.NexusFileId, out nativeFileId) && nativeFileId == selectedFileId)
					exactArtifactCandidates.Add(mod.Identity);
			}

			foreach (CollectionMemberBinding binding in directBindings)
			{
				CollectionNativeModState bound;
				if (!nativeState.Mods.TryGetValue(binding.NativeMod, out bound))
				{
					return Result(member, CollectionMemberMatchDisposition.Blocked,
						CollectionMemberMatchReason.MissingBoundNativeMod, null, directBindings, verifiedArchive);
				}
				if (NativeMetadataContradicts(bound, selectedModId, selectedFileId))
				{
					return Result(member, CollectionMemberMatchDisposition.Blocked,
						CollectionMemberMatchReason.BoundNativeArtifactMismatch, new[] { bound }, directBindings, verifiedArchive);
				}
				candidates[bound.Identity] = bound;
			}

			if (candidates.Count > 1)
			{
				List<CollectionMemberBinding> candidateBindings = GetBindings(nativeState, candidates.Keys);
				return Result(member, CollectionMemberMatchDisposition.Blocked,
					CollectionMemberMatchReason.AmbiguousInstalledCandidates, candidates.Values, candidateBindings, verifiedArchive);
			}

			if (candidates.Count == 1)
			{
				CollectionNativeModState candidate = candidates.Values.Single();
				List<CollectionMemberBinding> bindings = GetBindings(nativeState, new[] { candidate.Identity });

				if (bindings.Any(x => !x.VerifiedRecipe.Equals(member.RecipeIdentity)))
				{
					return Result(member, CollectionMemberMatchDisposition.Blocked,
						CollectionMemberMatchReason.ConflictingVerifiedRecipe, new[] { candidate }, bindings, verifiedArchive);
				}

				List<CollectionMemberBinding> verifiedApplied = bindings
					.Where(x => x.VerifiedRecipe.Equals(member.RecipeIdentity) &&
						x.Association.State == CollectionAssociationState.Applied)
					.ToList();
				bool exactArtifact = exactArtifactCandidates.Contains(candidate.Identity);
				bool directRecipeBinding = directBindings.Any(x => x.NativeMod.Equals(candidate.Identity) &&
					x.VerifiedRecipe.Equals(member.RecipeIdentity) && x.Association.State == CollectionAssociationState.Applied);

				if ((exactArtifact || directRecipeBinding) && verifiedApplied.Count > 0 &&
					bindings.All(x => x.Association.State == CollectionAssociationState.Applied))
				{
					CollectionMemberMatchReason reason = directRecipeBinding
						? CollectionMemberMatchReason.ExistingVerifiedBinding
						: CollectionMemberMatchReason.CompatibleSharedVerifiedBinding;
					return Result(member, CollectionMemberMatchDisposition.InstalledCompatible,
						reason, new[] { candidate }, bindings, verifiedArchive);
				}

				if (bindings.Any(x => x.Association.State == CollectionAssociationState.Incomplete ||
					x.Association.State == CollectionAssociationState.Recovering))
				{
					return Result(member, CollectionMemberMatchDisposition.Blocked,
						CollectionMemberMatchReason.AssociationRequiresRecovery, new[] { candidate }, bindings, verifiedArchive);
				}

				if (bindings.Any(x => x.Association.State == CollectionAssociationState.Modified))
				{
					return Result(member, CollectionMemberMatchDisposition.ReinstallRequired,
						CollectionMemberMatchReason.AssociationNotApplied, new[] { candidate }, bindings, verifiedArchive);
				}

				if (exactArtifact || directBindings.Any(x => x.NativeMod.Equals(candidate.Identity)))
				{
					return Result(member, CollectionMemberMatchDisposition.ReinstallRequired,
						CollectionMemberMatchReason.ExactArtifactRecipeUnverified, new[] { candidate }, bindings, verifiedArchive);
				}

				return Result(member, CollectionMemberMatchDisposition.ReinstallRequired,
					CollectionMemberMatchReason.AlternateArtifactInstalled, new[] { candidate }, bindings, verifiedArchive);
			}

			if (verifiedArchive != null)
			{
				return Result(member, CollectionMemberMatchDisposition.ArchiveOnlyReuse,
					CollectionMemberMatchReason.VerifiedArchiveAvailable, null, null, verifiedArchive);
			}

			return Result(member, CollectionMemberMatchDisposition.AcquisitionRequired,
				CollectionMemberMatchReason.NoReusableInput, null, null, null);
		}

		private static CollectionMemberMatchSet BuildUniformBlockedSet(ResolvedCollectionPlan plan,
			CollectionNativeStateIndex nativeState, IDictionary<CollectionMemberKey, CollectionVerifiedArchive> archives,
			CollectionMemberMatchReason reason)
		{
			var results = new List<CollectionMemberMatchResult>(plan.SelectedMembers.Count);
			foreach (ResolvedCollectionMemberPlan member in plan.SelectedMembers)
			{
				CollectionVerifiedArchive archive;
				archives.TryGetValue(member.MemberKey, out archive);
				results.Add(Result(member, CollectionMemberMatchDisposition.Blocked, reason, null, null, archive));
			}
			return new CollectionMemberMatchSet(plan, nativeState, results);
		}

		private static Dictionary<CollectionMemberKey, CollectionVerifiedArchive> ValidateVerifiedArchives(
			ResolvedCollectionPlan plan, IEnumerable<CollectionVerifiedArchive> verifiedArchives)
		{
			var selectedByKey = plan.SelectedMembers.ToDictionary(x => x.MemberKey);
			var result = new Dictionary<CollectionMemberKey, CollectionVerifiedArchive>();
			foreach (CollectionVerifiedArchive archive in verifiedArchives)
			{
				if (archive == null)
					throw new ArgumentException("Verified archive inputs cannot contain null entries.", nameof(verifiedArchives));
				CollectionAcquisitionRequest request = archive.Request;
				ResolvedCollectionMemberPlan member;
				if (!request.PlanIdentity.Equals(plan.Identity) || !request.Revision.Equals(plan.Revision) ||
					!request.Target.Equals(plan.Target) || !selectedByKey.TryGetValue(request.MemberKey, out member) ||
					!request.SelectedArtifact.Equals(member.ArtifactChoice.SelectedArtifact) ||
					!request.RecipeIdentity.Equals(member.RecipeIdentity))
					throw new ArgumentException("A verified archive input does not belong to the exact resolved member plan being matched.", nameof(verifiedArchives));
				if (result.ContainsKey(request.MemberKey))
					throw new ArgumentException("Only one verified archive input may be supplied for the same selected member.", nameof(verifiedArchives));
				result.Add(request.MemberKey, archive);
			}
			return result;
		}

		private static List<CollectionMemberBinding> GetBindings(CollectionNativeStateIndex nativeState,
			IEnumerable<NativeModInstanceIdentity> nativeMods)
		{
			var result = new List<CollectionMemberBinding>();
			var seen = new HashSet<CollectionMemberBinding>();
			foreach (NativeModInstanceIdentity nativeMod in nativeMods)
			{
				ReadOnlyCollection<CollectionMemberBinding> bindings;
				if (!nativeState.BindingsByNativeMod.TryGetValue(nativeMod, out bindings))
					continue;
				foreach (CollectionMemberBinding binding in bindings)
					if (seen.Add(binding))
						result.Add(binding);
			}
			return result;
		}

		private sealed class MatchingContext
		{
			private readonly Dictionary<long, List<CollectionNativeModState>> _modsByNexusModId;
			private readonly Dictionary<CollectionMemberKey, List<CollectionMemberBinding>> _directBindingsByMember;

			private MatchingContext(Dictionary<long, List<CollectionNativeModState>> modsByNexusModId,
				Dictionary<CollectionMemberKey, List<CollectionMemberBinding>> directBindingsByMember)
			{
				_modsByNexusModId = modsByNexusModId;
				_directBindingsByMember = directBindingsByMember;
			}

			/// <summary>Builds the per-pass matching indexes once from the immutable C6.1 state.</summary>
			public static MatchingContext Create(ResolvedCollectionPlan plan, CollectionNativeStateIndex nativeState)
			{
				var modsByNexusModId = new Dictionary<long, List<CollectionNativeModState>>();
				foreach (CollectionNativeModState mod in nativeState.Mods.Values)
				{
					long modId;
					if (!TryParsePositiveId(mod.NexusModId, out modId))
						continue;
					List<CollectionNativeModState> mods;
					if (!modsByNexusModId.TryGetValue(modId, out mods))
					{
						mods = new List<CollectionNativeModState>();
						modsByNexusModId.Add(modId, mods);
					}
					mods.Add(mod);
				}

				var directBindingsByMember = new Dictionary<CollectionMemberKey, List<CollectionMemberBinding>>();
				foreach (KeyValuePair<Guid, ReadOnlyCollection<CollectionMemberBinding>> pair in nativeState.BindingsByAssociation)
				{
					CollectionTargetAssociation association;
					if (!nativeState.Associations.TryGetValue(pair.Key, out association) || !association.Revision.Equals(plan.Revision))
						continue;
					foreach (CollectionMemberBinding binding in pair.Value)
					{
						List<CollectionMemberBinding> bindings;
						if (!directBindingsByMember.TryGetValue(binding.MemberKey, out bindings))
						{
							bindings = new List<CollectionMemberBinding>();
							directBindingsByMember.Add(binding.MemberKey, bindings);
						}
						bindings.Add(binding);
					}
				}
				return new MatchingContext(modsByNexusModId, directBindingsByMember);
			}

			public IEnumerable<CollectionNativeModState> GetNativeMods(long nexusModId)
			{
				List<CollectionNativeModState> mods;
				return _modsByNexusModId.TryGetValue(nexusModId, out mods)
					? mods
					: Enumerable.Empty<CollectionNativeModState>();
			}

			public List<CollectionMemberBinding> GetDirectBindings(CollectionMemberKey memberKey)
			{
				List<CollectionMemberBinding> bindings;
				return _directBindingsByMember.TryGetValue(memberKey, out bindings)
					? new List<CollectionMemberBinding>(bindings)
					: new List<CollectionMemberBinding>();
			}
		}

		private static bool NativeMetadataContradicts(CollectionNativeModState nativeMod, long selectedModId, long selectedFileId)
		{
			long nativeModId;
			if (TryParsePositiveId(nativeMod.NexusModId, out nativeModId) && nativeModId != selectedModId)
				return true;
			long nativeFileId;
			return TryParsePositiveId(nativeMod.NexusFileId, out nativeFileId) && nativeFileId != selectedFileId;
		}

		private static bool TryParsePositiveId(string value, out long parsed)
		{
			return Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0;
		}

		private static CollectionMemberMatchResult Result(ResolvedCollectionMemberPlan member,
			CollectionMemberMatchDisposition disposition, CollectionMemberMatchReason reason,
			IEnumerable<CollectionNativeModState> nativeCandidates, IEnumerable<CollectionMemberBinding> bindings,
			CollectionVerifiedArchive verifiedArchive)
		{
			return new CollectionMemberMatchResult(member, disposition, reason,
				nativeCandidates ?? Enumerable.Empty<CollectionNativeModState>(),
				bindings ?? Enumerable.Empty<CollectionMemberBinding>(), verifiedArchive);
		}
	}
}
