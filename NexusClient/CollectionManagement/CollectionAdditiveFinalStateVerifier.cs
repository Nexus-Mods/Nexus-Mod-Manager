using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Verifies the complete supported additive Collection state from one fresh authoritative native observation before Applied publication.
	/// </summary>
	/// <remarks>
	/// Per-child verification remains responsible for exact archive/replay/content durability at the child boundary. This verifier closes
	/// the aggregate publication gap: every selected member must still exist, every reviewed managed file owner/winner must still be present,
	/// and all supported plugin/configuration effects must still match in the same fresh target observation.
	/// </remarks>
	public sealed class CollectionAdditiveFinalStateVerifier
	{
		/// <summary>Throws unless the fresh native state still satisfies the complete supported reviewed additive plan.</summary>
		public void Verify(ResolvedCollectionPlan plan, CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan,
			IEnumerable<PreparedCollectionNativeRecipe> preparedRecipes, CollectionNativeStateIndex state)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (matches == null) throw new ArgumentNullException(nameof(matches));
			if (impactPlan == null) throw new ArgumentNullException(nameof(impactPlan));
			if (preparedRecipes == null) throw new ArgumentNullException(nameof(preparedRecipes));
			if (state == null) throw new ArgumentNullException(nameof(state));
			if (!state.Target.Equals(plan.Target) || !matches.Target.Equals(plan.Target) || !impactPlan.Target.Equals(plan.Target))
				throw new InvalidOperationException("The aggregate Collection verification inputs do not belong to the reviewed target.");
			if (!matches.PlanIdentity.Equals(plan.Identity) || !impactPlan.PlanIdentity.Equals(plan.Identity))
				throw new InvalidOperationException("The aggregate Collection verification inputs do not belong to the reviewed plan.");
			if (state.AssociationCoverage != CollectionNativeStateCoverage.Complete)
				throw new InvalidOperationException("Aggregate Collection verification requires complete Collection association state.");

			Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> recipes = preparedRecipes.ToDictionary(x => x.Member.MemberKey);
			Dictionary<CollectionMemberKey, CollectionNativeModState> nativeByMember = ResolveSelectedNativeMembers(plan, matches, recipes, state);
			VerifyManagedFiles(impactPlan, recipes, nativeByMember, state);
			VerifyNonFileEffects(impactPlan, recipes, nativeByMember, state);
		}

		private static Dictionary<CollectionMemberKey, CollectionNativeModState> ResolveSelectedNativeMembers(ResolvedCollectionPlan plan,
			CollectionMemberMatchSet matches, IDictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> recipes,
			CollectionNativeStateIndex state)
		{
			CollectionTargetAssociation operationAssociation = state.Associations.Values.SingleOrDefault(x =>
				x.Target.Equals(plan.Target) && x.Revision.Equals(plan.Revision));
			ReadOnlyCollection<CollectionMemberBinding> operationBindings;
			if (operationAssociation == null ||
				!state.BindingsByAssociation.TryGetValue(operationAssociation.AssociationId, out operationBindings))
				operationBindings = new ReadOnlyCollection<CollectionMemberBinding>(new CollectionMemberBinding[0]);

			var result = new Dictionary<CollectionMemberKey, CollectionNativeModState>();
			foreach (ResolvedCollectionMemberPlan member in plan.SelectedMembers)
			{
				CollectionMemberMatchResult match;
				if (!matches.MembersByKey.TryGetValue(member.MemberKey, out match))
					throw new InvalidOperationException("Aggregate verification is missing one reviewed selected member.");

				NativeModInstanceIdentity identity;
				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
				{
					CollectionNativeModState reviewed = match.MatchedNativeMod;
					if (reviewed == null)
						throw new InvalidOperationException("An adopted member no longer has one exact reviewed native identity.");
					identity = reviewed.Identity;
					ReadOnlyCollection<CollectionMemberBinding> currentBindings;
					if (!state.BindingsByNativeMod.TryGetValue(identity, out currentBindings) ||
						!currentBindings.Any(x => x.VerifiedRecipe.Equals(member.RecipeIdentity) &&
							x.Association.State == CollectionAssociationState.Applied))
						throw new InvalidOperationException("An adopted selected member lost the applied verified binding evidence used by the reviewed match.");
				}
				else if (match.Disposition == CollectionMemberMatchDisposition.ArchiveOnlyReuse ||
					match.Disposition == CollectionMemberMatchDisposition.ReinstallRequired)
				{
					CollectionMemberBinding binding = operationBindings.SingleOrDefault(x => x.MemberKey.Equals(member.MemberKey));
					if (binding == null || binding.BindingKind != CollectionMemberBindingKind.InstalledForCollection ||
						!binding.VerifiedRecipe.Equals(member.RecipeIdentity))
						throw new InvalidOperationException("A mutating selected member is missing its verified Collection binding at final verification.");
					identity = binding.NativeMod;
				}
				else
					throw new InvalidOperationException("An unresolved member disposition reached aggregate final verification.");

				CollectionNativeModState live;
				if (!state.Mods.TryGetValue(identity, out live))
					throw new InvalidOperationException("A selected Collection member is no longer present in authoritative native state.");

				PreparedCollectionNativeRecipe recipe;
				if (recipes.TryGetValue(member.MemberKey, out recipe))
				{
					if (live.InstallMethod != recipe.InstallContext.Method || live.InstallRoot != recipe.InstallContext.InstallRoot)
						throw new InvalidOperationException("A mutating selected member no longer has its reviewed native install context.");
				}
				else if (match.MatchedNativeMod != null &&
					(live.InstallMethod != match.MatchedNativeMod.InstallMethod || live.InstallRoot != match.MatchedNativeMod.InstallRoot))
					throw new InvalidOperationException("An adopted selected member changed native install context after review.");

				result.Add(member.MemberKey, live);
			}
			return result;
		}

		private static void VerifyManagedFiles(CollectionConflictImpactPlan impactPlan,
			IDictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> recipes,
			IDictionary<CollectionMemberKey, CollectionNativeModState> nativeByMember, CollectionNativeStateIndex state)
		{
			var writersByTarget = new Dictionary<Nexus.Client.ModManagement.ModDeploymentTarget, List<CollectionMemberKey>>();
			foreach (PreparedCollectionNativeRecipe recipe in recipes.Values)
				foreach (CollectionPlannedFileEffect effect in recipe.EffectPreview.Files)
				{
					List<CollectionMemberKey> writers;
					if (!writersByTarget.TryGetValue(effect.Target, out writers))
					{
						writers = new List<CollectionMemberKey>();
						writersByTarget.Add(effect.Target, writers);
					}
					if (!writers.Contains(recipe.Member.MemberKey)) writers.Add(recipe.Member.MemberKey);
				}

			foreach (KeyValuePair<Nexus.Client.ModManagement.ModDeploymentTarget, List<CollectionMemberKey>> entry in writersByTarget)
			{
				CollectionNativeFileState file;
				if (!state.Files.TryGetValue(entry.Key, out file) || String.IsNullOrWhiteSpace(file.PhysicalPath) || !File.Exists(file.PhysicalPath))
					throw new InvalidOperationException("A reviewed Collection file target is missing from final native state.");
				foreach (CollectionMemberKey writer in entry.Value)
				{
					CollectionNativeModState native = nativeByMember[writer];
					if (!ContainsOwner(file, native.Identity.NativeModKey))
						throw new InvalidOperationException("A reviewed Collection file writer is missing from the final native owner stack.");
				}

				CollectionFileImpact impact = impactPlan.FileImpacts.SingleOrDefault(x => x.Target.Equals(entry.Key));
				if (impact == null)
					throw new InvalidOperationException("A reviewed Collection file target is missing its durable impact decision.");
				CollectionMemberKey winner = impact.PlannedWinner ?? (entry.Value.Count == 1 ? entry.Value[0] : null);
				if (winner == null || !nativeByMember.ContainsKey(winner) ||
					!StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, nativeByMember[winner].Identity.NativeModKey))
					throw new InvalidOperationException("The final native file winner no longer matches the reviewed Collection decision.");
			}
		}

		private static bool ContainsOwner(CollectionNativeFileState file, string ownerKey)
		{
			return file.InstallLogOwners.Concat(file.DeploymentOwners).Concat(file.VirtualOwners)
				.Any(x => x.Kind == CollectionNativeOwnerKind.NativeMod && StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey));
		}

		private static void VerifyNonFileEffects(CollectionConflictImpactPlan impactPlan,
			IDictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> recipes,
			IDictionary<CollectionMemberKey, CollectionNativeModState> nativeByMember, CollectionNativeStateIndex state)
		{
			foreach (PreparedCollectionNativeRecipe recipe in recipes.Values)
			{
				CollectionNativeModState native = nativeByMember[recipe.Member.MemberKey];
				string ownerKey = native.Identity.NativeModKey;
				foreach (CollectionPlannedIniEffect effect in recipe.EffectPreview.IniEdits)
				{
					CollectionNativeIniState ini;
					if (!state.IniEdits.TryGetValue(effect.Key, out ini) || ini.Values.Count == 0)
						throw new InvalidOperationException("A reviewed Collection INI effect is missing from final native state.");
					CollectionNativeTextOwnerValue current = ini.Values[ini.Values.Count - 1];
					if (!StringComparer.OrdinalIgnoreCase.Equals(current.OwnerKey, ownerKey) || !StringComparer.Ordinal.Equals(current.Value, effect.Value))
						throw new InvalidOperationException("A reviewed Collection INI effect no longer matches final native state.");
				}

				foreach (CollectionPlannedGameValueEffect effect in recipe.EffectPreview.GameValues)
				{
					CollectionNativeGameValueState value;
					if (!state.GameValues.TryGetValue(effect.Key, out value) || value.Values.Count == 0)
						throw new InvalidOperationException("A reviewed Collection game-specific effect is missing from final native state.");
					CollectionNativeBinaryOwnerValue current = value.Values[value.Values.Count - 1];
					if (!StringComparer.OrdinalIgnoreCase.Equals(current.OwnerKey, ownerKey) || !ByteArraysEqual(current.UnsafeValue, effect.UnsafeValue))
						throw new InvalidOperationException("A reviewed Collection game-specific effect no longer matches final native state.");
				}

			}

			if (impactPlan.PluginImpacts.Count > 0 && state.PluginCoverage != CollectionNativeStateCoverage.Complete)
				throw new InvalidOperationException("Final Collection verification cannot prove reviewed plugin effects because plugin state is unavailable.");
			foreach (CollectionPluginImpact impact in impactPlan.PluginImpacts)
				if (!CollectionNativeChildVerificationCoordinator.VerifyPluginEffect(state, impact.Effect))
					throw new InvalidOperationException("A reviewed Collection plugin effect no longer matches final native state.");
		}

		private static bool ByteArraysEqual(byte[] left, byte[] right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Length != right.Length) return false;
			for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
			return true;
		}
	}
}
