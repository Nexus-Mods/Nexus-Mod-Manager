using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// C10.10a read-only verify/repair assessment. It distinguishes deliberate local decisions from repairable member-level
	/// differences and fail-closes on opaque/uncharacterized differences. No native or Collections state is written.
	/// </summary>
	public sealed class CollectionVerifyRepairPlanner
	{
		public CollectionVerifyRepairPlan Plan(CollectionTargetAssociation association, NormalizedCollectionManifest manifest,
			CollectionNativeStateIndex state, IEnumerable<CollectionMemberBinding> bindings,
			IEnumerable<UserOverride> overrides, IEnumerable<CollectionDriftObservation> drift)
		{
			return Plan(association, manifest, state, bindings, overrides, drift, null, null);
		}

		public CollectionVerifyRepairPlan Plan(CollectionTargetAssociation association, NormalizedCollectionManifest manifest,
			CollectionNativeStateIndex state, IEnumerable<CollectionMemberBinding> bindings,
			IEnumerable<UserOverride> overrides, IEnumerable<CollectionDriftObservation> drift,
			IEnumerable<CollectionMemberEffectPreview> exactEffectPreviews)
		{
			return Plan(association, manifest, state, bindings, overrides, drift, exactEffectPreviews, null);
		}

		public CollectionVerifyRepairPlan Plan(CollectionTargetAssociation association, NormalizedCollectionManifest manifest,
			CollectionNativeStateIndex state, IEnumerable<CollectionMemberBinding> bindings,
			IEnumerable<UserOverride> overrides, IEnumerable<CollectionDriftObservation> drift,
			IEnumerable<CollectionMemberEffectPreview> exactEffectPreviews, CollectionVerifyRepairPreparationResult preparation,
			IReadOnlyDictionary<CollectionRequirementReference, CollectionRequirementState> currentMemberStates = null,
			IEnumerable<CollectionMemberBinding> bindingUpdates = null)
		{
			if (association == null) throw new ArgumentNullException(nameof(association));
			if (manifest == null) throw new ArgumentNullException(nameof(manifest));
			if (state == null) throw new ArgumentNullException(nameof(state));
			if (bindings == null) throw new ArgumentNullException(nameof(bindings));
			if (overrides == null) throw new ArgumentNullException(nameof(overrides));
			if (drift == null) throw new ArgumentNullException(nameof(drift));
			if (!association.Revision.Equals(manifest.Revision) || !association.Target.Equals(state.Target))
				throw new ArgumentException("The verify/repair inputs must describe one exact association revision and target.");

			List<CollectionMemberBinding> bindingList = bindings.Where(x => x.Association.AssociationId == association.AssociationId).ToList();
			List<UserOverride> overrideList = overrides.Where(x => x.Requirement.AssociationId == association.AssociationId).ToList();
			List<CollectionDriftObservation> driftList = drift.Where(x => x.Requirement.AssociationId == association.AssociationId).ToList();
			IReadOnlyDictionary<CollectionRequirementReference, CollectionRequirementState> observedMemberStates = currentMemberStates ??
				new Dictionary<CollectionRequirementReference, CollectionRequirementState>();
			var findings = new List<CollectionVerifyRepairFinding>();

			if (association.State == CollectionAssociationState.Recovering)
				findings.Add(AssociationFinding(association, CollectionVerifyRepairFindingKind.AssociationRecovering,
					"The Collection association is recovering and must be reconciled before verify/repair can make a repair decision."));
			if (state.AssociationCoverage != CollectionNativeStateCoverage.Complete)
				findings.Add(AssociationFinding(association, CollectionVerifyRepairFindingKind.StateCoverageUnavailable,
					"Collection association/native-state coverage is unavailable."));
			if (!manifest.IsMemberSetComplete)
				findings.Add(AssociationFinding(association, CollectionVerifyRepairFindingKind.ManifestIncomplete,
					"The retained Collection manifest does not prove a complete member set."));

			var manifestByKey = manifest.Members.Where(x => x.IdentityResolution.IsResolved)
				.ToDictionary(x => x.IdentityResolution.Key);
			foreach (CollectionMemberBinding binding in bindingList)
			{
				NormalizedCollectionMember member;
				if (!manifestByKey.TryGetValue(binding.MemberKey, out member))
				{
					findings.Add(new CollectionVerifyRepairFinding(binding.MemberKey, null,
						CollectionVerifyRepairFindingKind.BindingMismatch, CollectionVerifyRepairDisposition.ActionRequired,
						null, null, "A persisted member binding is not present in the retained revision manifest."));
					continue;
				}
				if (member.RecipeIdentity == null || !binding.VerifiedRecipe.Equals(member.RecipeIdentity))
					findings.Add(new CollectionVerifyRepairFinding(binding.MemberKey, null,
						CollectionVerifyRepairFindingKind.BindingMismatch, CollectionVerifyRepairDisposition.ActionRequired,
						null, null, "The persisted member binding does not match the retained revision recipe identity."));
			}

			foreach (NormalizedCollectionMember member in manifest.Members)
			{
				if (!member.IdentityResolution.IsResolved)
				{
					if (member.IsRequired || member.IsSelected)
						findings.Add(new CollectionVerifyRepairFinding(null, null,
							CollectionVerifyRepairFindingKind.BindingMismatch, CollectionVerifyRepairDisposition.ActionRequired,
							null, null, "A selected/required retained member has no stable identity and cannot be verified safely."));
					continue;
				}

				CollectionMemberKey key = member.IdentityResolution.Key;
				CollectionMemberBinding binding = bindingList.SingleOrDefault(x => x.MemberKey.Equals(key));
				UserOverride participationOverride = FindOverride(overrideList, key, CollectionRequirementAspect.MemberParticipation);
				CollectionRequirementReference participation = new CollectionRequirementReference(association, key,
					CollectionRequirementAspect.MemberParticipation, null);
				CollectionRequirementState baselineParticipation = member.IsRequired || member.IsSelected || binding != null
					? CollectionMemberRequirementStates.Included() : CollectionRequirementState.Absent();
				CollectionRequirementState expectedParticipation = participationOverride == null
					? baselineParticipation : participationOverride.UserChosenState;
				bool nativePresent = binding != null && state.Mods.ContainsKey(binding.NativeMod);
				CollectionRequirementState observedParticipation = nativePresent
					? CollectionMemberRequirementStates.Included() : CollectionRequirementState.Absent();
				CollectionDriftObservation participationDrift = driftList.SingleOrDefault(x =>
					x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(key) &&
					x.Requirement.Aspect == CollectionRequirementAspect.MemberParticipation);

				if (!expectedParticipation.Equals(observedParticipation))
				{
					findings.Add(new CollectionVerifyRepairFinding(key, participation,
						CollectionVerifyRepairFindingKind.MemberParticipationMismatch, CollectionVerifyRepairDisposition.RestoreExpectedState,
						expectedParticipation, observedParticipation,
						participationOverride == null
							? "The installed member participation does not match the retained Collection baseline."
							: "The installed member participation does not match the explicit local Collection decision."));
				}
				else
				{
					if (participationDrift != null)
						findings.Add(new CollectionVerifyRepairFinding(key, participation,
							CollectionVerifyRepairFindingKind.Satisfied, CollectionVerifyRepairDisposition.None,
							expectedParticipation, observedParticipation,
							"A previously observed member-participation difference is no longer present in authoritative native state."));

					if (participationOverride != null)
					{
						findings.Add(new CollectionVerifyRepairFinding(key, participation,
							CollectionVerifyRepairFindingKind.PreservedExplicitOverride, CollectionVerifyRepairDisposition.PreserveLocalDecision,
							expectedParticipation, observedParticipation, "The explicit member participation override is currently satisfied and must not be silently repaired away."));
					}
					else if (participationDrift == null && baselineParticipation.Kind == CollectionRequirementStateKind.Present)
					{
						findings.Add(new CollectionVerifyRepairFinding(key, participation,
							CollectionVerifyRepairFindingKind.Satisfied, CollectionVerifyRepairDisposition.None,
							expectedParticipation, observedParticipation, "The Collection member is present in native state."));
					}
				}

				foreach (CollectionDriftObservation observation in driftList.Where(x => x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(key) &&
					x.Requirement.Aspect != CollectionRequirementAspect.MemberParticipation))
				{
					CollectionRequirementState currentObserved;
					if (observation.Requirement.Aspect == CollectionRequirementAspect.MemberEnabledState &&
						observedMemberStates.TryGetValue(observation.Requirement, out currentObserved))
					{
						if (observation.ExpectedState.Equals(currentObserved))
							findings.Add(new CollectionVerifyRepairFinding(key, observation.Requirement,
								CollectionVerifyRepairFindingKind.Satisfied, CollectionVerifyRepairDisposition.None,
								observation.ExpectedState, currentObserved,
								"A previously observed member enabled-state difference is no longer present in authoritative native state."));
						else
							AppendDriftFinding(findings, observation, overrideList, currentObserved);
					}
					else
					{
						AppendDriftFinding(findings, observation, overrideList);
					}
				}
				foreach (UserOverride localOverride in overrideList.Where(x => x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(key) &&
					x.Requirement.Aspect != CollectionRequirementAspect.MemberParticipation))
				{
					if (!driftList.Any(x => x.Requirement.Equals(localOverride.Requirement)))
						findings.Add(new CollectionVerifyRepairFinding(key, localOverride.Requirement,
							CollectionVerifyRepairFindingKind.PreservedExplicitOverride, CollectionVerifyRepairDisposition.PreserveLocalDecision,
							localOverride.UserChosenState, localOverride.UserChosenState,
							"The explicit local Collection decision is preserved; this verify pass has no evidence that it is currently violated."));
				}
			}

			foreach (CollectionDriftObservation observation in driftList.Where(x => x.Requirement.MemberKey == null))
				AppendDriftFinding(findings, observation, overrideList);
			foreach (UserOverride localOverride in overrideList.Where(x => x.Requirement.MemberKey == null))
				if (!driftList.Any(x => x.Requirement.Equals(localOverride.Requirement)))
					findings.Add(new CollectionVerifyRepairFinding(null, localOverride.Requirement,
						CollectionVerifyRepairFindingKind.PreservedExplicitOverride, CollectionVerifyRepairDisposition.PreserveLocalDecision,
						localOverride.UserChosenState, localOverride.UserChosenState,
						"The explicit local Collection decision is preserved; this verify pass does not reinterpret its opaque state."));

			AppendManifestPluginRelativeOrderFindings(association, manifest, state, overrideList, findings);

			bool hasSpecificDifference = findings.Any(x => x.Kind != CollectionVerifyRepairFindingKind.Satisfied &&
				x.Kind != CollectionVerifyRepairFindingKind.PreservedExplicitOverride);
			if ((association.State == CollectionAssociationState.Modified || association.State == CollectionAssociationState.Incomplete) &&
				!hasSpecificDifference && overrideList.Count == 0 && driftList.Count == 0)
				findings.Add(AssociationFinding(association, CollectionVerifyRepairFindingKind.UncharacterizedModification,
					"The association reports a changed/incomplete state but no exact current requirement difference is available; repair must not guess what changed."));

			bool exactAvailable = exactEffectPreviews != null;
			if (exactAvailable)
			{
				List<CollectionMemberEffectPreview> previews = exactEffectPreviews.ToList();
				if (previews.Any(x => x == null || !x.IsComplete))
					throw new ArgumentException("Exact verify/repair previews must be complete.", nameof(exactEffectPreviews));
				if (previews.Select(x => x.MemberKey).Distinct().Count() != previews.Count ||
					!new HashSet<CollectionMemberKey>(previews.Select(x => x.MemberKey)).SetEquals(bindingList.Select(x => x.MemberKey)))
					throw new ArgumentException("Exact verify/repair coverage must contain one complete effect preview for every current member binding.", nameof(exactEffectPreviews));
				findings.AddRange(new CollectionVerifyRepairExactEffectVerifier().Verify(association, state, bindingList, previews, overrideList));
			}
			if (preparation != null && !preparation.IsComplete)
				findings.Add(AssociationFinding(association, CollectionVerifyRepairFindingKind.ExactRecipePreparationUnavailable, preparation.Issue));
			return new CollectionVerifyRepairPlan(association, state.Fingerprint, findings, exactAvailable,
				preparation == null ? null : preparation.Plan, preparation == null ? null : preparation.PreparedRecipes, bindingUpdates);
		}

		private static void AppendManifestPluginRelativeOrderFindings(CollectionTargetAssociation association,
			NormalizedCollectionManifest manifest, CollectionNativeStateIndex state, IList<UserOverride> overrides,
			ICollection<CollectionVerifyRepairFinding> findings)
		{
			foreach (CollectionPluginRelativeOrderRule rule in manifest.PluginRelativeOrderRules)
			{
				string subject = CollectionPluginRelativeOrderApplicator.RequirementSubject(rule);
				var requirement = new CollectionRequirementReference(association, null, CollectionRequirementAspect.PluginState, subject);
				UserOverride localOverride = overrides.SingleOrDefault(x => x.Requirement.Equals(requirement));
				if (localOverride != null)
				{
					findings.Add(new CollectionVerifyRepairFinding(null, requirement,
						CollectionVerifyRepairFindingKind.PreservedExplicitOverride, CollectionVerifyRepairDisposition.PreserveLocalDecision,
						localOverride.UserChosenState, localOverride.UserChosenState,
						"The explicit local plugin-order decision is preserved; verify/repair will not silently restore the curator relative-order rule."));
					continue;
				}

				if (state.PluginCoverage != CollectionNativeStateCoverage.Complete)
				{
					findings.Add(new CollectionVerifyRepairFinding(null, requirement,
						CollectionVerifyRepairFindingKind.PluginEffectMismatch, CollectionVerifyRepairDisposition.ActionRequired,
						null, null, "Plugin state is unavailable for verification of a retained Collection plugin-after rule."));
					continue;
				}

				CollectionNativePluginState later = FindPlugin(state, rule.PluginName);
				CollectionNativePluginState earlier = FindPlugin(state, rule.AfterPluginName);
				if (later == null || earlier == null)
				{
					findings.Add(new CollectionVerifyRepairFinding(null, requirement,
						CollectionVerifyRepairFindingKind.PluginEffectMismatch, CollectionVerifyRepairDisposition.ActionRequired,
						null, null, "A retained Collection plugin-after endpoint is no longer present in authoritative native plugin state."));
					continue;
				}

				if (later.Priority <= earlier.Priority)
					findings.Add(new CollectionVerifyRepairFinding(null, requirement,
						CollectionVerifyRepairFindingKind.PluginEffectMismatch, CollectionVerifyRepairDisposition.RestoreExpectedState,
						null, null, "The native plugin order no longer satisfies the retained Collection plugin-after rule."));
			}
		}

		private static CollectionNativePluginState FindPlugin(CollectionNativeStateIndex state, string pluginName)
		{
			if (state == null || String.IsNullOrWhiteSpace(pluginName)) return null;
			CollectionNativePluginState plugin;
			if (state.Plugins.TryGetValue(pluginName, out plugin)) return plugin;
			return state.Plugins.Values.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(
				System.IO.Path.GetFileName(x.FileName), System.IO.Path.GetFileName(pluginName)));
		}

		private static void AppendDriftFinding(List<CollectionVerifyRepairFinding> findings,
			CollectionDriftObservation observation, IList<UserOverride> overrides, CollectionRequirementState observedState = null)
		{
			UserOverride localOverride = overrides.SingleOrDefault(x => x.Requirement.Equals(observation.Requirement));
			bool memberRepairable = observation.Requirement.Aspect == CollectionRequirementAspect.MemberParticipation ||
				observation.Requirement.Aspect == CollectionRequirementAspect.MemberEnabledState;
			findings.Add(new CollectionVerifyRepairFinding(observation.Requirement.MemberKey, observation.Requirement,
				CollectionVerifyRepairFindingKind.DetectedDrift,
				memberRepairable ? CollectionVerifyRepairDisposition.RestoreExpectedState : CollectionVerifyRepairDisposition.ActionRequired,
				observation.ExpectedState, observedState ?? observation.ObservedState,
				localOverride == null
					? (memberRepairable ? "Native reality differs from the Collection baseline and can be offered as an explicit qualified repair." : "Native reality differs from an opaque Collection effect; repair requires an explicit typed adapter/decision.")
					: (memberRepairable ? "Native reality differs from the user's explicit local decision and can be offered as an explicit qualified repair." : "Native reality differs from an explicit opaque local decision; repair must not reinterpret that fingerprint.")));
		}

		private static UserOverride FindOverride(IEnumerable<UserOverride> overrides, CollectionMemberKey memberKey,
			CollectionRequirementAspect aspect)
		{
			return overrides.SingleOrDefault(x => x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(memberKey) &&
				x.Requirement.Aspect == aspect);
		}

		private static CollectionVerifyRepairFinding AssociationFinding(CollectionTargetAssociation association,
			CollectionVerifyRepairFindingKind kind, string detail)
		{
			return new CollectionVerifyRepairFinding(null, null, kind, CollectionVerifyRepairDisposition.ActionRequired,
				null, null, detail);
		}
	}
}
