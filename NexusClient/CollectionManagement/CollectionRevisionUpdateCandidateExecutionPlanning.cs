using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>One deterministic C10.6 execution view over the latest verified candidate-update safe boundary.</summary>
	internal sealed class CollectionRevisionUpdateCandidateExecutionPlanning
	{
		internal CollectionRevisionUpdateCandidateExecutionPlanning(ResolvedCollectionPlan executionPlan,
			CollectionMemberMatchSet matches, CollectionDependencyPhasePlan dependencyPlan,
			CollectionConflictImpactPlan impactPlan, IEnumerable<PreparedCollectionNativeRecipe> recipes)
		{
			ExecutionPlan = executionPlan ?? throw new ArgumentNullException(nameof(executionPlan));
			Matches = matches ?? throw new ArgumentNullException(nameof(matches));
			DependencyPlan = dependencyPlan ?? throw new ArgumentNullException(nameof(dependencyPlan));
			ImpactPlan = impactPlan ?? throw new ArgumentNullException(nameof(impactPlan));
			Recipes = new ReadOnlyCollection<PreparedCollectionNativeRecipe>((recipes ?? throw new ArgumentNullException(nameof(recipes))).ToList());
		}

		internal ResolvedCollectionPlan ExecutionPlan { get; }
		internal CollectionMemberMatchSet Matches { get; }
		internal CollectionDependencyPhasePlan DependencyPlan { get; }
		internal CollectionConflictImpactPlan ImpactPlan { get; }
		internal ReadOnlyCollection<PreparedCollectionNativeRecipe> Recipes { get; }
		internal bool IsReady
		{
			get { return DependencyPlan.IsReady && ImpactPlan.IsReady && !Matches.HasBlockedMembers && !Matches.HasAcquisitionRequired; }
		}
	}

	/// <summary>
	/// Builds the C10.6 execution-time candidate match/dependency/impact view without reusing additive association authorization.
	/// Old-revision bindings are treated as reinstall inputs rather than as conflicting candidate recipe bindings.
	/// </summary>
	internal sealed class CollectionRevisionUpdateCandidateExecutionPlanner
	{
		private readonly CollectionDependencyPhasePlanner _dependencyPlanner = new CollectionDependencyPhasePlanner();
		private readonly CollectionConflictImpactPlanner _impactPlanner = new CollectionConflictImpactPlanner();

		internal CollectionRevisionUpdateCandidateExecutionPlanning Build(CollectionRevisionUpdatePlan updatePlan,
			CollectionNativeStateIndex currentState, CollectionRevisionUpdateOverridePreservationPlan preservationPlan,
			IEnumerable<CollectionRevisionUpdatePreparationMemberState> preparationMembers,
			IEnumerable<PreparedCollectionNativeRecipe> effectiveRecipes, IEnumerable<CollectionMemberKey> committedMembers, CancellationToken cancellationToken)
		{
			if (updatePlan == null) throw new ArgumentNullException(nameof(updatePlan));
			if (currentState == null) throw new ArgumentNullException(nameof(currentState));
			if (preservationPlan == null) throw new ArgumentNullException(nameof(preservationPlan));
			if (preparationMembers == null) throw new ArgumentNullException(nameof(preparationMembers));
			if (effectiveRecipes == null) throw new ArgumentNullException(nameof(effectiveRecipes));
			if (committedMembers == null) throw new ArgumentNullException(nameof(committedMembers));
			if (!updatePlan.NewPlan.Target.Equals(currentState.Target))
				throw new ArgumentException("The C10.6 execution state belongs to a different Collection target.", nameof(currentState));
			if (!preservationPlan.UpdatePlan.NewPlan.Identity.Equals(updatePlan.NewPlan.Identity))
				throw new ArgumentException("The C10.5 preservation plan belongs to a different candidate plan.", nameof(preservationPlan));

			ResolvedCollectionPlan executionPlan = RebindState(updatePlan.NewPlan, currentState.Fingerprint);
			Dictionary<CollectionMemberKey, CollectionRevisionUpdateMemberPlan> allUpdateMembers = updatePlan.Members
				.ToDictionary(x => x.MemberKey);
			Dictionary<CollectionMemberKey, CollectionRevisionUpdateMemberPlan> updateMembers = updatePlan.Members
				.Where(x => x.NewMember != null).ToDictionary(x => x.MemberKey);
			CollectionRevisionUpdateMemberCorrelationMap correlations = updatePlan.MemberCorrelations;
			Dictionary<CollectionMemberKey, CollectionRevisionUpdatePreparationMemberState> preparation = preparationMembers
				.ToDictionary(x => x.UpdateMember.MemberKey);
			Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> recipes = effectiveRecipes
				.ToDictionary(x => x.Member.MemberKey);
			var suppressed = new HashSet<CollectionMemberKey>(preservationPlan.MembersWhoseCandidateMutationIsSuppressed);
			var committed = new HashSet<CollectionMemberKey>(committedMembers);
			// PreserveStandalone protects a member that is truly absent from the candidate revision. A validated Nexus
			// file-id change is different: the old key is correlated to a candidate key for the same logical Nexus mod, so
			// its native instance is the reviewed reinstall target rather than an unrelated independently protected mod.
			var preservedNative = new HashSet<NativeModInstanceIdentity>(updatePlan.Members
				.Where(x => x.Disposition == CollectionRevisionUpdateDisposition.PreserveStandalone && x.Binding != null &&
					!correlations.NewByOld.ContainsKey(x.MemberKey))
				.Select(x => x.Binding.NativeMod));
			// The approved three-way update review already owns surviving old-revision native instances. A real Nexus
			// revision can change the validated member key when fileId changes, so resolve the unique old-side binding
			// through the revision-only correlation map instead of requiring identical CollectionMemberKey values.
			var reviewedExistingOwnerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionRevisionUpdateMemberPlan candidateMember in updateMembers.Values)
			{
				if (suppressed.Contains(candidateMember.MemberKey)) continue;
				bool reviewedBindingIsCorrelated;
				CollectionMemberBinding reviewedBinding = ResolveCandidateBinding(candidateMember, allUpdateMembers, correlations, out reviewedBindingIsCorrelated);
				if (reviewedBinding == null || preservedNative.Contains(reviewedBinding.NativeMod) || !currentState.Mods.ContainsKey(reviewedBinding.NativeMod))
					continue;
				reviewedExistingOwnerKeys.Add(reviewedBinding.NativeMod.NativeModKey);
			}
			var results = new List<CollectionMemberMatchResult>();

			foreach (ResolvedCollectionMemberPlan member in executionPlan.SelectedMembers)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionRevisionUpdateMemberPlan updateMember;
				if (!updateMembers.TryGetValue(member.MemberKey, out updateMember))
				{
					results.Add(Block(member, CollectionMemberMatchReason.AssociationStateUnavailable));
					continue;
				}

				bool effectiveBindingIsCorrelated;
				CollectionMemberBinding effectiveBinding = ResolveCandidateBinding(updateMember, allUpdateMembers, correlations, out effectiveBindingIsCorrelated);
				CollectionNativeModState bound = ResolveBoundNative(effectiveBinding, currentState);
				IEnumerable<CollectionMemberBinding> existingBindings = bound == null
					? Enumerable.Empty<CollectionMemberBinding>()
					: GetBindings(currentState, bound.Identity);

				if (committed.Contains(member.MemberKey))
				{
					CollectionRevisionUpdatePreparationMemberState committedPreparation;
					if (!preparation.TryGetValue(member.MemberKey, out committedPreparation) || committedPreparation.VerifiedArchive == null)
					{
						results.Add(Block(member, CollectionMemberMatchReason.NoReusableInput));
						continue;
					}
					List<CollectionNativeModState> committedCandidates = FindExactCurrentCandidates(member, currentState,
						committedPreparation.VerifiedArchive, cancellationToken);
					if (committedCandidates.Count != 1)
					{
						results.Add(new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.Blocked,
							committedCandidates.Count == 0 ? CollectionMemberMatchReason.MissingBoundNativeMod : CollectionMemberMatchReason.AmbiguousInstalledCandidates,
							committedCandidates, committedCandidates.SelectMany(x => GetBindings(currentState, x.Identity)), committedPreparation.VerifiedArchive));
						continue;
					}
					CollectionNativeModState committedNative = committedCandidates[0];
					results.Add(new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.InstalledCompatible,
						CollectionMemberMatchReason.ExactArtifactRecipeUnverified, committedCandidates, GetBindings(currentState, committedNative.Identity), committedPreparation.VerifiedArchive));
					continue;
				}

				if (suppressed.Contains(member.MemberKey))
				{
					if (bound == null)
						results.Add(Block(member, CollectionMemberMatchReason.MissingBoundNativeMod));
					else
						results.Add(new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.InstalledCompatible,
							CollectionMemberMatchReason.ExistingVerifiedBinding, new[] { bound }, existingBindings, null));
					continue;
				}

				CollectionRevisionUpdatePreparationMemberState preparedState;
				PreparedCollectionNativeRecipe recipe;
				bool requiresMutation = preparation.TryGetValue(member.MemberKey, out preparedState);
				if (!requiresMutation)
				{
					if (bound == null)
						results.Add(Block(member, CollectionMemberMatchReason.MissingBoundNativeMod));
					else
						results.Add(new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.InstalledCompatible,
							CollectionMemberMatchReason.ExistingVerifiedBinding, new[] { bound }, existingBindings, null));
					continue;
				}

				if (preparedState.VerifiedArchive == null || !recipes.TryGetValue(member.MemberKey, out recipe))
				{
					results.Add(new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.AcquisitionRequired,
						CollectionMemberMatchReason.NoReusableInput, Enumerable.Empty<CollectionNativeModState>(), existingBindings,
						preparedState.VerifiedArchive));
					continue;
				}
				if (!recipe.Member.MemberKey.Equals(member.MemberKey) || !recipe.Member.RecipeIdentity.Equals(member.RecipeIdentity))
					throw new InvalidOperationException("An effective C10.6 prepared recipe no longer belongs to the exact candidate member.");

				if (bound != null)
				{
					if (preservedNative.Contains(bound.Identity))
					{
						results.Add(Block(member, CollectionMemberMatchReason.ConflictingVerifiedRecipe));
						continue;
					}
					if (existingBindings.Any(x => x.Association.AssociationId != updatePlan.Association.AssociationId))
					{
						results.Add(new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.Blocked,
							CollectionMemberMatchReason.ConflictingVerifiedRecipe, new[] { bound }, existingBindings, preparedState.VerifiedArchive));
						continue;
					}
					results.Add(new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.ReinstallRequired,
						CollectionMemberMatchReason.ExactArtifactRecipeUnverified, new[] { bound }, existingBindings, preparedState.VerifiedArchive));
					continue;
				}
				if (effectiveBinding != null && !effectiveBindingIsCorrelated)
				{
					results.Add(Block(member, CollectionMemberMatchReason.MissingBoundNativeMod));
					continue;
				}
				// A cross-key old binding may legitimately be absent here: C10.6 runs only after C10.4 has verified
				// obsolete effects. In that case the candidate is a normal verified archive install, not a missing-binding error.

				List<CollectionNativeModState> candidates = FindExactCurrentCandidates(member, currentState,
					preparedState.VerifiedArchive, cancellationToken);
				if (candidates.Count > 1)
				{
					results.Add(new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.Blocked,
						CollectionMemberMatchReason.AmbiguousInstalledCandidates, candidates,
						candidates.SelectMany(x => GetBindings(currentState, x.Identity)), preparedState.VerifiedArchive));
					continue;
				}
				if (candidates.Count == 1)
				{
					if (preservedNative.Contains(candidates[0].Identity))
					{
						results.Add(Block(member, CollectionMemberMatchReason.ConflictingVerifiedRecipe));
						continue;
					}
					results.Add(new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.ReinstallRequired,
						CollectionMemberMatchReason.ExactArtifactRecipeUnverified, candidates,
						GetBindings(currentState, candidates[0].Identity), preparedState.VerifiedArchive));
					continue;
				}
				results.Add(new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.ArchiveOnlyReuse,
					CollectionMemberMatchReason.VerifiedArchiveAvailable, Enumerable.Empty<CollectionNativeModState>(),
					Enumerable.Empty<CollectionMemberBinding>(), preparedState.VerifiedArchive));
			}

			var matches = new CollectionMemberMatchSet(executionPlan, currentState, results);
			CollectionDependencyPhasePlan dependency = _dependencyPlanner.Plan(executionPlan, matches);
			CollectionConflictImpactPlan impact = _impactPlanner.PlanForRevisionUpdateExecution(executionPlan, matches,
				dependency, currentState, recipes.Values.Select(x => x.EffectPreview), new CollectionConflictResolutionDecision[0],
				updatePlan.Association.AssociationId, reviewedExistingOwnerKeys);
			return new CollectionRevisionUpdateCandidateExecutionPlanning(executionPlan, matches, dependency, impact, recipes.Values);
		}

		private static CollectionMemberBinding ResolveCandidateBinding(CollectionRevisionUpdateMemberPlan candidateMember,
			IDictionary<CollectionMemberKey, CollectionRevisionUpdateMemberPlan> allUpdateMembers,
			CollectionRevisionUpdateMemberCorrelationMap correlations, out bool correlatedOldBinding)
		{
			correlatedOldBinding = false;
			if (candidateMember.Binding != null) return candidateMember.Binding;

			CollectionMemberKey oldKey;
			if (!correlations.TryGetOldMemberKey(candidateMember.MemberKey, out oldKey)) return null;
			CollectionRevisionUpdateMemberPlan correlatedOldMember;
			if (!allUpdateMembers.TryGetValue(oldKey, out correlatedOldMember) || correlatedOldMember == null ||
				correlatedOldMember.OldMember == null || correlatedOldMember.NewMember != null)
				throw new InvalidOperationException("The reviewed cross-revision Nexus member correlation no longer resolves to one old-side member.");
			correlatedOldBinding = correlatedOldMember.Binding != null;
			return correlatedOldMember.Binding;
		}

		private static CollectionNativeModState ResolveBoundNative(CollectionMemberBinding binding, CollectionNativeStateIndex state)
		{
			if (binding == null) return null;
			CollectionNativeModState native;
			return state.Mods.TryGetValue(binding.NativeMod, out native) ? native : null;
		}

		private static IEnumerable<CollectionMemberBinding> GetBindings(CollectionNativeStateIndex state,
			NativeModInstanceIdentity identity)
		{
			ReadOnlyCollection<CollectionMemberBinding> bindings;
			return state.BindingsByNativeMod.TryGetValue(identity, out bindings)
				? bindings : Enumerable.Empty<CollectionMemberBinding>();
		}

		private static List<CollectionNativeModState> FindExactCurrentCandidates(ResolvedCollectionMemberPlan member,
			CollectionNativeStateIndex state, CollectionVerifiedArchive archive, CancellationToken cancellationToken)
		{
			string gameDomain;
			long modId;
			long fileId;
			bool nexus = NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact,
				out gameDomain, out modId, out fileId);
			string expectedMod = nexus ? modId.ToString(CultureInfo.InvariantCulture) : null;
			string expectedFile = nexus ? fileId.ToString(CultureInfo.InvariantCulture) : null;
			var result = new List<CollectionNativeModState>();
			foreach (CollectionNativeModState native in state.Mods.Values)
			{
				if (nexus && (!StringComparer.Ordinal.Equals(native.NexusModId, expectedMod) ||
					!StringComparer.Ordinal.Equals(native.NexusFileId, expectedFile)))
					continue;
				if (!CollectionArchiveContentMatcher.MatchesFile(native.ArchivePath, archive.Artifact, cancellationToken))
					continue;
				result.Add(native);
			}
			return result;
		}

		private static CollectionMemberMatchResult Block(ResolvedCollectionMemberPlan member, CollectionMemberMatchReason reason)
		{
			return new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.Blocked, reason,
				Enumerable.Empty<CollectionNativeModState>(), Enumerable.Empty<CollectionMemberBinding>(), null);
		}

		internal static ResolvedCollectionPlan RebindState(ResolvedCollectionPlan plan, CollectionCurrentStateFingerprint fingerprint)
		{
			return new ResolvedCollectionPlan(plan.Identity, plan.Target, plan.Policy, fingerprint, plan.CapabilityReport, plan.SelectedMembers);
		}
	}
}
