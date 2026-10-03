using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// C10.1 pure three-way revision update planner. It never persists state or mutates native NMM state.
	/// </summary>
	public sealed class CollectionRevisionUpdatePlanner
	{
		public CollectionRevisionUpdatePlan Plan(CollectionTargetAssociation association, ResolvedCollectionPlan oldPlan,
			ResolvedCollectionPlan newPlan, CollectionNativeStateIndex currentState,
			IEnumerable<UserOverride> overrides, IEnumerable<CollectionDriftObservation> drift,
			IEnumerable<NativeModProvenance> provenance,
			IReadOnlyDictionary<CollectionMemberKey, PreparedCollectionNativeRecipeIdentity> oldPreparedIdentities = null,
			IReadOnlyDictionary<CollectionMemberKey, PreparedCollectionNativeRecipeIdentity> newPreparedIdentities = null,
			IReadOnlyDictionary<CollectionMemberKey, CollectionMemberEffectPreview> oldEffectPreviews = null,
			IReadOnlyDictionary<CollectionMemberKey, CollectionMemberEffectPreview> newEffectPreviews = null)
		{
			ValidateInputs(association, oldPlan, newPlan, currentState);

			List<UserOverride> overrideList = CopyAndValidateOverrides(association, overrides);
			List<CollectionDriftObservation> driftList = CopyAndValidateDrift(association, drift);
			Dictionary<NativeModInstanceIdentity, NativeModProvenance> provenanceByNative = CopyAndValidateProvenance(currentState, provenance);
			Dictionary<CollectionMemberKey, CollectionMemberBinding> bindings = GetBindings(currentState, association);
			Dictionary<CollectionMemberKey, ResolvedCollectionMemberPlan> oldMembers = oldPlan.SelectedMembers.ToDictionary(x => x.MemberKey);
			Dictionary<CollectionMemberKey, ResolvedCollectionMemberPlan> newMembers = newPlan.SelectedMembers.ToDictionary(x => x.MemberKey);

			var keys = oldMembers.Keys.Concat(newMembers.Keys).Distinct().OrderBy(x => x.Kind).ThenBy(x => x.Value, StringComparer.Ordinal).ToList();
			var results = new List<CollectionRevisionUpdateMemberPlan>();
			foreach (CollectionMemberKey key in keys)
			{
				ResolvedCollectionMemberPlan oldMember;
				ResolvedCollectionMemberPlan newMember;
				oldMembers.TryGetValue(key, out oldMember);
				newMembers.TryGetValue(key, out newMember);
				CollectionMemberBinding binding;
				bindings.TryGetValue(key, out binding);

				List<UserOverride> memberOverrides = overrideList.Where(x => key.Equals(x.Requirement.MemberKey)).ToList();
				List<CollectionDriftObservation> memberDrift = driftList.Where(x => key.Equals(x.Requirement.MemberKey)).ToList();
				CollectionRevisionUpdateChangeKind change = ClassifyChange(oldMember, newMember);
				CollectionRevisionUpdateCurrentStateKind current = ClassifyCurrentState(oldMember, binding, currentState, memberOverrides, memberDrift);
				PreparedCollectionNativeRecipeIdentity oldPrepared = GetPrepared(oldPreparedIdentities, key);
				PreparedCollectionNativeRecipeIdentity newPrepared = GetPrepared(newPreparedIdentities, key);
				CollectionRevisionUpdatePreparationKind preparation = ClassifyPreparation(change, oldPrepared, newPrepared);
				bool standaloneProtected = binding != null && IsStandaloneProtected(provenanceByNative, binding.NativeMod);
				CollectionRevisionUpdateDisposition disposition = ClassifyDisposition(change, current, standaloneProtected);
				string detail = Describe(change, current, preparation, standaloneProtected);

				results.Add(new CollectionRevisionUpdateMemberPlan(key, oldMember, newMember, binding, change, current,
					disposition, preparation, oldPrepared, newPrepared, memberOverrides, memberDrift, standaloneProtected, detail));
			}

			List<CollectionRevisionUpdateEffectPlan> effects = CompareEffects(oldEffectPreviews, newEffectPreviews);
			List<UserOverride> unscopedOverrides = overrideList.Where(x => x.Requirement.MemberKey == null).ToList();
			List<CollectionDriftObservation> unscopedDrift = driftList.Where(x => x.Requirement.MemberKey == null).ToList();
			return new CollectionRevisionUpdatePlan(association, oldPlan, newPlan, currentState.Fingerprint, results, effects,
				unscopedOverrides, unscopedDrift);
		}

		private static void ValidateInputs(CollectionTargetAssociation association, ResolvedCollectionPlan oldPlan,
			ResolvedCollectionPlan newPlan, CollectionNativeStateIndex currentState)
		{
			if (association == null) throw new ArgumentNullException(nameof(association));
			if (oldPlan == null) throw new ArgumentNullException(nameof(oldPlan));
			if (newPlan == null) throw new ArgumentNullException(nameof(newPlan));
			if (currentState == null) throw new ArgumentNullException(nameof(currentState));
			if (!association.Revision.Equals(oldPlan.Revision))
				throw new ArgumentException("The old resolved plan must be the exact revision currently associated with the target.", nameof(oldPlan));
			if (!association.Target.Equals(oldPlan.Target) || !association.Target.Equals(newPlan.Target) || !association.Target.Equals(currentState.Target))
				throw new ArgumentException("Old plan, candidate plan, association and current native state must describe the same target.");
			if (!oldPlan.Revision.Collection.Equals(newPlan.Revision.Collection))
				throw new ArgumentException("A revision update must remain within one Collection lineage.", nameof(newPlan));
			if (oldPlan.Revision.Equals(newPlan.Revision))
				throw new ArgumentException("The candidate revision must differ from the currently associated revision.", nameof(newPlan));
			if (currentState.AssociationCoverage != CollectionNativeStateCoverage.Complete)
				throw new InvalidOperationException("C10.1 requires complete current Collection association coverage.");
		}

		private static List<UserOverride> CopyAndValidateOverrides(CollectionTargetAssociation association, IEnumerable<UserOverride> overrides)
		{
			List<UserOverride> copied = (overrides ?? throw new ArgumentNullException(nameof(overrides))).ToList();
			if (copied.Any(x => x == null || x.Requirement.AssociationId != association.AssociationId ||
				!x.Requirement.BaselineRevision.Equals(association.Revision) || !x.Requirement.Target.Equals(association.Target)))
				throw new ArgumentException("Every override must belong to the exact old association baseline.", nameof(overrides));
			return copied;
		}

		private static List<CollectionDriftObservation> CopyAndValidateDrift(CollectionTargetAssociation association,
			IEnumerable<CollectionDriftObservation> drift)
		{
			List<CollectionDriftObservation> copied = (drift ?? throw new ArgumentNullException(nameof(drift))).ToList();
			if (copied.Any(x => x == null || x.Requirement.AssociationId != association.AssociationId ||
				!x.Requirement.BaselineRevision.Equals(association.Revision) || !x.Requirement.Target.Equals(association.Target)))
				throw new ArgumentException("Every drift observation must belong to the exact old association baseline.", nameof(drift));
			return copied;
		}

		private static Dictionary<NativeModInstanceIdentity, NativeModProvenance> CopyAndValidateProvenance(CollectionNativeStateIndex state,
			IEnumerable<NativeModProvenance> provenance)
		{
			var result = new Dictionary<NativeModInstanceIdentity, NativeModProvenance>();
			foreach (NativeModProvenance item in provenance ?? throw new ArgumentNullException(nameof(provenance)))
			{
				if (item == null || !item.NativeMod.Target.Equals(state.Target))
					throw new ArgumentException("Every provenance entry must belong to the current target.", nameof(provenance));
				result.Add(item.NativeMod, item);
			}
			return result;
		}

		private static Dictionary<CollectionMemberKey, CollectionMemberBinding> GetBindings(CollectionNativeStateIndex state,
			CollectionTargetAssociation association)
		{
			ReadOnlyCollection<CollectionMemberBinding> source;
			if (!state.BindingsByAssociation.TryGetValue(association.AssociationId, out source))
				return new Dictionary<CollectionMemberKey, CollectionMemberBinding>();
			return source.ToDictionary(x => x.MemberKey);
		}

		private static CollectionRevisionUpdateChangeKind ClassifyChange(ResolvedCollectionMemberPlan oldMember, ResolvedCollectionMemberPlan newMember)
		{
			if (oldMember == null) return CollectionRevisionUpdateChangeKind.Added;
			if (newMember == null) return CollectionRevisionUpdateChangeKind.Removed;
			bool artifactChanged = !oldMember.ArtifactChoice.Equals(newMember.ArtifactChoice);
			bool recipeChanged = !oldMember.RecipeIdentity.Equals(newMember.RecipeIdentity);
			if (artifactChanged && recipeChanged) return CollectionRevisionUpdateChangeKind.ArtifactAndRecipeChanged;
			if (artifactChanged) return CollectionRevisionUpdateChangeKind.ArtifactChanged;
			if (recipeChanged) return CollectionRevisionUpdateChangeKind.RecipeChanged;
			return CollectionRevisionUpdateChangeKind.Unchanged;
		}

		private static CollectionRevisionUpdateCurrentStateKind ClassifyCurrentState(ResolvedCollectionMemberPlan oldMember,
			CollectionMemberBinding binding, CollectionNativeStateIndex state, IList<UserOverride> overrides,
			IList<CollectionDriftObservation> drift)
		{
			if (oldMember == null) return CollectionRevisionUpdateCurrentStateKind.NotApplicable;
			if (drift.Count > 0) return CollectionRevisionUpdateCurrentStateKind.UnacceptedDrift;
			if (overrides.Count > 0) return CollectionRevisionUpdateCurrentStateKind.IntentionalOverride;
			if (binding == null || !binding.VerifiedRecipe.Equals(oldMember.RecipeIdentity) || !state.Mods.ContainsKey(binding.NativeMod))
				return CollectionRevisionUpdateCurrentStateKind.MissingNativeBinding;
			return CollectionRevisionUpdateCurrentStateKind.MatchesOldBaseline;
		}

		private static CollectionRevisionUpdatePreparationKind ClassifyPreparation(CollectionRevisionUpdateChangeKind change,
			PreparedCollectionNativeRecipeIdentity oldPrepared, PreparedCollectionNativeRecipeIdentity newPrepared)
		{
			if (newPrepared != null && oldPrepared == null)
				return CollectionRevisionUpdatePreparationKind.PreparedForCandidate;
			if (oldPrepared == null && newPrepared == null)
				return change == CollectionRevisionUpdateChangeKind.Unchanged || change == CollectionRevisionUpdateChangeKind.Removed
					? CollectionRevisionUpdatePreparationKind.NotAvailable : CollectionRevisionUpdatePreparationKind.ReprepareRequired;
			if (newPrepared == null)
				return change == CollectionRevisionUpdateChangeKind.Unchanged || change == CollectionRevisionUpdateChangeKind.Removed
					? CollectionRevisionUpdatePreparationKind.NotAvailable : CollectionRevisionUpdatePreparationKind.ReprepareRequired;
			if (oldPrepared.Equals(newPrepared))
				return CollectionRevisionUpdatePreparationKind.Unchanged;
			return CollectionRevisionUpdatePreparationKind.CandidateChanged;
		}

		private static CollectionRevisionUpdateDisposition ClassifyDisposition(CollectionRevisionUpdateChangeKind change,
			CollectionRevisionUpdateCurrentStateKind current, bool standaloneProtected)
		{
			if (current == CollectionRevisionUpdateCurrentStateKind.UnacceptedDrift)
				return CollectionRevisionUpdateDisposition.DriftRequiresReview;
			if (current == CollectionRevisionUpdateCurrentStateKind.IntentionalOverride)
				return CollectionRevisionUpdateDisposition.PreserveOverrideForReview;
			if (current == CollectionRevisionUpdateCurrentStateKind.MissingNativeBinding || current == CollectionRevisionUpdateCurrentStateKind.Ambiguous)
				return CollectionRevisionUpdateDisposition.ActionRequired;
			if (change == CollectionRevisionUpdateChangeKind.Added)
				return CollectionRevisionUpdateDisposition.AddFromNewRevision;
			if (change == CollectionRevisionUpdateChangeKind.Removed)
				return standaloneProtected ? CollectionRevisionUpdateDisposition.ActionRequired : CollectionRevisionUpdateDisposition.RemoveFromOldRevision;
			if (change == CollectionRevisionUpdateChangeKind.Unchanged)
				return CollectionRevisionUpdateDisposition.NoChange;
			return CollectionRevisionUpdateDisposition.FollowNewRevision;
		}

		private static PreparedCollectionNativeRecipeIdentity GetPrepared(
			IReadOnlyDictionary<CollectionMemberKey, PreparedCollectionNativeRecipeIdentity> source, CollectionMemberKey key)
		{
			if (source == null) return null;
			PreparedCollectionNativeRecipeIdentity value;
			return source.TryGetValue(key, out value) ? value : null;
		}

		private static bool IsStandaloneProtected(IDictionary<NativeModInstanceIdentity, NativeModProvenance> provenance,
			NativeModInstanceIdentity nativeMod)
		{
			NativeModProvenance value;
			return !provenance.TryGetValue(nativeMod, out value) || value.StandaloneUseProtectsFromAutomaticRemoval;
		}

		private static List<CollectionRevisionUpdateEffectPlan> CompareEffects(
			IReadOnlyDictionary<CollectionMemberKey, CollectionMemberEffectPreview> oldPreviews,
			IReadOnlyDictionary<CollectionMemberKey, CollectionMemberEffectPreview> newPreviews)
		{
			var result = new List<CollectionRevisionUpdateEffectPlan>();
			var keys = Enumerable.Empty<CollectionMemberKey>();
			if (oldPreviews != null) keys = keys.Concat(oldPreviews.Keys);
			if (newPreviews != null) keys = keys.Concat(newPreviews.Keys);
			foreach (CollectionMemberKey memberKey in keys.Distinct().OrderBy(x => x.Kind).ThenBy(x => x.Value, StringComparer.Ordinal))
			{
				CollectionMemberEffectPreview oldPreview = GetEffectPreview(oldPreviews, memberKey);
				CollectionMemberEffectPreview newPreview = GetEffectPreview(newPreviews, memberKey);
				if (oldPreview != null && (!oldPreview.IsComplete || !oldPreview.MemberKey.Equals(memberKey)))
					throw new ArgumentException("Old effect previews must be complete and keyed by their exact member identity.", nameof(oldPreviews));
				if (newPreview != null && (!newPreview.IsComplete || !newPreview.MemberKey.Equals(memberKey)))
					throw new ArgumentException("New effect previews must be complete and keyed by their exact member identity.", nameof(newPreviews));

				Dictionary<string, EffectState> oldStates = BuildEffectStates(oldPreview);
				Dictionary<string, EffectState> newStates = BuildEffectStates(newPreview);
				foreach (string identity in oldStates.Keys.Concat(newStates.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
				{
					EffectState oldState;
					EffectState newState;
					bool hasOld = oldStates.TryGetValue(identity, out oldState);
					bool hasNew = newStates.TryGetValue(identity, out newState);
					CollectionRevisionUpdateEffectChangeKind change = !hasOld
						? CollectionRevisionUpdateEffectChangeKind.Added
						: !hasNew ? CollectionRevisionUpdateEffectChangeKind.Removed
						: StringComparer.Ordinal.Equals(oldState.ValueFingerprint, newState.ValueFingerprint)
							? CollectionRevisionUpdateEffectChangeKind.Unchanged : CollectionRevisionUpdateEffectChangeKind.Changed;
					EffectState state = hasNew ? newState : oldState;
					result.Add(new CollectionRevisionUpdateEffectPlan(memberKey, state.Kind, identity, change));
				}
			}
			return result;
		}

		private static CollectionMemberEffectPreview GetEffectPreview(
			IReadOnlyDictionary<CollectionMemberKey, CollectionMemberEffectPreview> source, CollectionMemberKey key)
		{
			if (source == null) return null;
			CollectionMemberEffectPreview value;
			return source.TryGetValue(key, out value) ? value : null;
		}

		private static Dictionary<string, EffectState> BuildEffectStates(CollectionMemberEffectPreview preview)
		{
			var result = new Dictionary<string, EffectState>(StringComparer.Ordinal);
			if (preview == null) return result;
			foreach (CollectionPlannedFileEffect file in preview.Files)
				AddEffect(result, CollectionRevisionUpdateEffectKind.File, "file:" + (int)file.Target.Root + ":" + file.Target.RelativePath.ToLowerInvariant(), "present");
			foreach (CollectionPlannedIniEffect ini in preview.IniEdits)
				AddEffect(result, CollectionRevisionUpdateEffectKind.Ini, "ini:" + ini.Key.File.ToLowerInvariant() + ":" + ini.Key.Section.ToLowerInvariant() + ":" + ini.Key.Key.ToLowerInvariant(), ini.Value ?? "<null>");
			foreach (CollectionPlannedGameValueEffect value in preview.GameValues)
				AddEffect(result, CollectionRevisionUpdateEffectKind.GameValue, "game:" + value.Key, value.Value == null ? "<null>" : Convert.ToBase64String(value.Value));
			foreach (CollectionPlannedPluginEffect plugin in preview.PluginEffects)
			{
				string paths = String.Join("|", plugin.PluginPaths.Select(x => x.ToLowerInvariant()));
				string identity = "plugin:" + (int)plugin.Kind + ":" + paths;
				string value = plugin.Active.HasValue ? (plugin.Active.Value ? "active" : "inactive") :
					plugin.AbsoluteIndex.HasValue ? plugin.AbsoluteIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : paths;
				AddEffect(result, CollectionRevisionUpdateEffectKind.Plugin, identity, value);
			}
			return result;
		}

		private static void AddEffect(IDictionary<string, EffectState> target, CollectionRevisionUpdateEffectKind kind,
			string identity, string valueFingerprint)
		{
			if (target.ContainsKey(identity))
				throw new InvalidOperationException("An exact member effect preview contains duplicate effect identity: " + identity);
			target.Add(identity, new EffectState(kind, valueFingerprint));
		}

		private sealed class EffectState
		{
			public EffectState(CollectionRevisionUpdateEffectKind kind, string valueFingerprint)
			{
				Kind = kind;
				ValueFingerprint = valueFingerprint ?? String.Empty;
			}
			public CollectionRevisionUpdateEffectKind Kind { get; }
			public string ValueFingerprint { get; }
		}

		private static string Describe(CollectionRevisionUpdateChangeKind change, CollectionRevisionUpdateCurrentStateKind current,
			CollectionRevisionUpdatePreparationKind preparation, bool standaloneProtected)
		{
			if (current == CollectionRevisionUpdateCurrentStateKind.IntentionalOverride)
				return "A deliberate C9 local override exists and must be preserved or explicitly reconciled against the candidate revision.";
			if (current == CollectionRevisionUpdateCurrentStateKind.UnacceptedDrift)
				return "Observed native state differs from the accepted old baseline and has not been adopted as an override.";
			if (current == CollectionRevisionUpdateCurrentStateKind.MissingNativeBinding)
				return "The exact old member binding/verified native instance can no longer be proven from current authoritative state.";
			if (change == CollectionRevisionUpdateChangeKind.Removed && standaloneProtected)
				return "The candidate revision removes this member, but standalone provenance does not authorize automatic native removal.";
			if (preparation == CollectionRevisionUpdatePreparationKind.CandidateChanged)
				return "The candidate prepared-native output differs from the old preparation, including possible adapter/environment-sensitive changes.";
			if (preparation == CollectionRevisionUpdatePreparationKind.ReprepareRequired)
				return "The provider recipe changed and no candidate prepared-native identity is available; native output must be prepared before execution.";
			return change.ToString();
		}
	}
}
