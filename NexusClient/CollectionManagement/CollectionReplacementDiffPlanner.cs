using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// C8.1 deterministic read-only planner comparing one replacement selection with one immutable current-setup snapshot.
	/// </summary>
	/// <remarks>
	/// This planner never persists consent, mutates native state, projects installer conditions or authorizes execution.
	/// It deliberately separates reuse/matching facts from later permission to remove managed effects.
	/// </remarks>
	public sealed class CollectionReplacementDiffPlanner
	{
		private readonly CollectionMemberMatchEngine _memberMatchEngine;

		/// <summary>Creates the pure replacement-difference planner.</summary>
		public CollectionReplacementDiffPlanner()
			: this(new CollectionMemberMatchEngine())
		{
		}

		internal CollectionReplacementDiffPlanner(CollectionMemberMatchEngine memberMatchEngine)
		{
			_memberMatchEngine = memberMatchEngine ?? throw new ArgumentNullException(nameof(memberMatchEngine));
		}

		/// <summary>Builds a stable C8.1 difference without persistence or native writes.</summary>
		public CollectionReplacementDiffPlan Plan(ResolvedCollectionPlan incomingPlan,
			CollectionReplacementCurrentSetupSnapshot currentSetup)
		{
			if (incomingPlan == null)
				throw new ArgumentNullException(nameof(incomingPlan));
			if (currentSetup == null)
				throw new ArgumentNullException(nameof(currentSetup));
			if (incomingPlan.Policy.Kind != CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup)
				throw new ArgumentException("The C8.1 replacement diff planner requires the explicit Replace current managed setup policy.", nameof(incomingPlan));
			if (!incomingPlan.Target.Equals(currentSetup.Target))
				throw new ArgumentException("The incoming replacement plan and current-setup snapshot must describe the same target.", nameof(currentSetup));

			CollectionMemberMatchSet matches = _memberMatchEngine.MatchForReplacementPlanning(incomingPlan, currentSetup.NativeState);
			List<CollectionReplacementIncomingMemberImpact> incomingImpacts = matches.Members
				.Select(BuildIncomingImpact).ToList();
			Dictionary<NativeModInstanceIdentity, List<CollectionReplacementIncomingMemberImpact>> incomingByNative =
				BuildIncomingByNative(incomingImpacts);

			List<CollectionReplacementAssociationImpact> associationImpacts = BuildAssociationImpacts(currentSetup, incomingByNative);
			var associationById = associationImpacts.ToDictionary(x => x.Association.AssociationId);
			HashSet<Guid> survivingAssociations = new HashSet<Guid>(associationImpacts
				.Where(x => x.Disposition == CollectionReplacementAssociationDisposition.SurvivesCompatible)
				.Select(x => x.Association.AssociationId));

			List<CollectionReplacementNativeModImpact> nativeImpacts = BuildNativeModImpacts(incomingPlan, currentSetup,
				incomingByNative, associationById, survivingAssociations);
			Dictionary<string, CollectionReplacementNativeModImpact> nativeImpactByKey = nativeImpacts
				.ToDictionary(x => x.NativeMod.Identity.NativeModKey, StringComparer.OrdinalIgnoreCase);

			List<CollectionReplacementEffectImpact> effects = BuildEffectImpacts(currentSetup.NativeState, nativeImpactByKey);
			return new CollectionReplacementDiffPlan(incomingPlan, currentSetup, incomingImpacts, nativeImpacts,
				associationImpacts, effects);
		}

		private static CollectionReplacementIncomingMemberImpact BuildIncomingImpact(CollectionMemberMatchResult match)
		{
			CollectionReplacementDiffDisposition disposition;
			string reason;
			switch (match.Disposition)
			{
				case CollectionMemberMatchDisposition.InstalledCompatible:
					disposition = CollectionReplacementDiffDisposition.RetainedReused;
					reason = "The selected incoming member is already satisfied by one verified compatible native instance.";
					break;
				case CollectionMemberMatchDisposition.ReinstallRequired:
					disposition = CollectionReplacementDiffDisposition.ReinstallOrChange;
					reason = "A current native candidate exists but its artifact, recipe, association state or install context requires change.";
					break;
				case CollectionMemberMatchDisposition.ArchiveOnlyReuse:
				case CollectionMemberMatchDisposition.AcquisitionRequired:
					disposition = CollectionReplacementDiffDisposition.IncomingOnly;
					reason = "No installed native instance currently satisfies the selected incoming member.";
					break;
				default:
					disposition = CollectionReplacementDiffDisposition.BlockedAmbiguous;
					reason = "Incoming member matching is ambiguous or blocked and cannot define destructive replacement scope.";
					break;
			}
			return new CollectionReplacementIncomingMemberImpact(match.Member, disposition, match.Disposition,
				match.Reason, match.NativeCandidates, reason);
		}

		private static Dictionary<NativeModInstanceIdentity, List<CollectionReplacementIncomingMemberImpact>> BuildIncomingByNative(
			IEnumerable<CollectionReplacementIncomingMemberImpact> incomingImpacts)
		{
			var result = new Dictionary<NativeModInstanceIdentity, List<CollectionReplacementIncomingMemberImpact>>();
			foreach (CollectionReplacementIncomingMemberImpact incoming in incomingImpacts)
			{
				foreach (CollectionNativeModState candidate in incoming.NativeCandidates)
				{
					List<CollectionReplacementIncomingMemberImpact> values;
					if (!result.TryGetValue(candidate.Identity, out values))
					{
						values = new List<CollectionReplacementIncomingMemberImpact>();
						result.Add(candidate.Identity, values);
					}
					values.Add(incoming);
				}
			}
			return result;
		}

		private static List<CollectionReplacementAssociationImpact> BuildAssociationImpacts(
			CollectionReplacementCurrentSetupSnapshot currentSetup,
			IDictionary<NativeModInstanceIdentity, List<CollectionReplacementIncomingMemberImpact>> incomingByNative)
		{
			var result = new List<CollectionReplacementAssociationImpact>();
			foreach (CollectionTargetAssociation association in currentSetup.NativeState.Associations.Values
				.OrderBy(x => x.AssociationId))
			{
				CollectionAssociationCustomization customization;
				currentSetup.CustomizationsByAssociation.TryGetValue(association.AssociationId, out customization);
				bool hasCustomization = association.State == CollectionAssociationState.Modified ||
					(customization != null && customization.RequiresReview);
				CollectionReplacementAssociationDisposition disposition;
				string reason;

				if (association.State == CollectionAssociationState.Incomplete || association.State == CollectionAssociationState.Recovering)
				{
					disposition = CollectionReplacementAssociationDisposition.Blocked;
					reason = "The association is incomplete/recovering and must be reconciled before replacement can decide its transition.";
				}
				else if (hasCustomization)
				{
					disposition = CollectionReplacementAssociationDisposition.RequiresExplicitReview;
					reason = "The association contains deliberate customization, detected drift, or an unexplained Modified state that replacement must review explicitly.";
				}
				else
				{
					ReadOnlyCollection<CollectionMemberBinding> bindings;
					currentSetup.NativeState.BindingsByAssociation.TryGetValue(association.AssociationId, out bindings);
					bool survives = bindings != null && bindings.Count > 0 && bindings.All(binding =>
					{
						List<CollectionReplacementIncomingMemberImpact> incoming;
						return incomingByNative.TryGetValue(binding.NativeMod, out incoming) && incoming.Count == 1 &&
							incoming[0].Disposition == CollectionReplacementDiffDisposition.RetainedReused;
					});
					if (survives)
					{
						disposition = CollectionReplacementAssociationDisposition.SurvivesCompatible;
						reason = "Every bound native member remains a verified compatible instance in the incoming selected setup.";
					}
					else
					{
						disposition = CollectionReplacementAssociationDisposition.OutgoingTransition;
						reason = "At least one association requirement is not retained unchanged by the incoming selected setup.";
					}
				}
				result.Add(new CollectionReplacementAssociationImpact(association, disposition, hasCustomization, reason));
			}
			return result;
		}

		private static List<CollectionReplacementNativeModImpact> BuildNativeModImpacts(ResolvedCollectionPlan incomingPlan,
			CollectionReplacementCurrentSetupSnapshot currentSetup,
			IDictionary<NativeModInstanceIdentity, List<CollectionReplacementIncomingMemberImpact>> incomingByNative,
			IDictionary<Guid, CollectionReplacementAssociationImpact> associationById, ISet<Guid> survivingAssociations)
		{
			var result = new List<CollectionReplacementNativeModImpact>();
			bool stale = !incomingPlan.CurrentStateFingerprint.Equals(currentSetup.NativeState.Fingerprint);
			foreach (CollectionNativeModState mod in currentSetup.NativeState.Mods.Values
				.OrderBy(x => x.Identity.NativeModKey, StringComparer.Ordinal))
			{
				ReadOnlyCollection<CollectionMemberBinding> bindings;
				currentSetup.NativeState.BindingsByNativeMod.TryGetValue(mod.Identity, out bindings);
				IEnumerable<CollectionMemberBinding> bindingValues = bindings == null ? Enumerable.Empty<CollectionMemberBinding>() : bindings;
				Guid[] surviving = bindingValues.Select(x => x.Association.AssociationId)
					.Where(survivingAssociations.Contains).Distinct().OrderBy(x => x).ToArray();
				NativeModProvenance provenance;
				if (!currentSetup.ProvenanceByNativeMod.TryGetValue(mod.Identity, out provenance))
					provenance = new NativeModProvenance(mod.Identity, StandaloneModUse.Unknown);
				bool customized = bindingValues.Any(binding =>
				{
					CollectionAssociationCustomization customization;
					CollectionReplacementAssociationImpact associationImpact;
					return (currentSetup.CustomizationsByAssociation.TryGetValue(binding.Association.AssociationId, out customization) && customization.RequiresReview) ||
						(associationById.TryGetValue(binding.Association.AssociationId, out associationImpact) && associationImpact.HasCustomization);
				});

				CollectionReplacementDiffDisposition disposition;
				CollectionReplacementRemovalDecision removalDecision;
				string reason;
				List<CollectionReplacementIncomingMemberImpact> incoming;
				incomingByNative.TryGetValue(mod.Identity, out incoming);

				if (stale || currentSetup.NativeState.AssociationCoverage != CollectionNativeStateCoverage.Complete)
				{
					disposition = CollectionReplacementDiffDisposition.BlockedAmbiguous;
					removalDecision = CollectionReplacementRemovalDecision.Blocked;
					reason = stale
						? "The incoming plan was built from a different native-state fingerprint; no destructive classification is current."
						: "Collection association coverage is unavailable, so replacement cannot prove current ownership/provenance scope.";
				}
				else if (incoming != null && incoming.Count > 1)
				{
					disposition = CollectionReplacementDiffDisposition.BlockedAmbiguous;
					removalDecision = CollectionReplacementRemovalDecision.Blocked;
					reason = "The same native mod is a candidate for more than one selected incoming member; replacement cannot infer one exact reuse/change decision.";
				}
				else if (incoming != null && incoming.Count == 1 && incoming[0].Disposition == CollectionReplacementDiffDisposition.BlockedAmbiguous)
				{
					disposition = CollectionReplacementDiffDisposition.BlockedAmbiguous;
					removalDecision = CollectionReplacementRemovalDecision.Blocked;
					reason = "The native mod participates in a blocked incoming member match and must be preserved until that ambiguity is resolved.";
				}
				else if (surviving.Length > 0)
				{
					disposition = CollectionReplacementDiffDisposition.SharedProtected;
					removalDecision = CollectionReplacementRemovalDecision.Protected;
					reason = incoming != null && incoming.Count == 1 && incoming[0].Disposition == CollectionReplacementDiffDisposition.RetainedReused
						? "The incoming setup reuses this native instance and at least one compatible surviving Collection association also requires it."
						: "At least one compatible surviving Collection association still requires this native instance.";
				}
				else if (incoming != null && incoming.Count == 1 && incoming[0].Disposition == CollectionReplacementDiffDisposition.RetainedReused)
				{
					disposition = CollectionReplacementDiffDisposition.RetainedReused;
					removalDecision = CollectionReplacementRemovalDecision.Protected;
					reason = "The incoming selected recipe reuses this exact verified native instance.";
				}
				else if (incoming != null && incoming.Count == 1 && incoming[0].Disposition == CollectionReplacementDiffDisposition.ReinstallOrChange)
				{
					disposition = CollectionReplacementDiffDisposition.ReinstallOrChange;
					removalDecision = CollectionReplacementRemovalDecision.NotApplicable;
					reason = "The incoming selected recipe requires this native instance to be reinstalled or changed rather than treated as unrelated outgoing state.";
				}
				else
				{
					string coverageFailure = GetAuthoritativeRemovalCoverageFailure(currentSetup.NativeState, mod.Identity);
					bool associationBlocked = bindingValues.Any(binding =>
					{
						CollectionReplacementAssociationImpact associationImpact;
						return associationById.TryGetValue(binding.Association.AssociationId, out associationImpact) && associationImpact.BlocksPlanning;
					});
					if (coverageFailure != null || associationBlocked)
					{
						disposition = CollectionReplacementDiffDisposition.BlockedAmbiguous;
						removalDecision = CollectionReplacementRemovalDecision.Blocked;
						reason = coverageFailure ?? "A bound Collection association requires recovery/reconciliation before replacement can decide removal.";
					}
					else
					{
						disposition = CollectionReplacementDiffDisposition.OutgoingOnly;
						if (customized)
						{
							removalDecision = CollectionReplacementRemovalDecision.RequiresExplicitReview;
							reason = "The native mod is outgoing-only, but deliberate customization or drift must be reviewed explicitly before removal.";
						}
						else if (provenance.StandaloneUse == StandaloneModUse.ExplicitStandaloneUse)
						{
							removalDecision = CollectionReplacementRemovalDecision.RequiresExplicitReview;
							reason = "The native mod is outgoing-only relative to the incoming recipe, but explicit standalone use must be acknowledged in replacement review.";
						}
						else if (provenance.StandaloneUse == StandaloneModUse.Unknown)
						{
							removalDecision = CollectionReplacementRemovalDecision.RequiresExplicitReview;
							reason = "The native mod is outgoing-only, but standalone provenance is unresolved and cannot be silently treated as removable.";
						}
						else
						{
							removalDecision = CollectionReplacementRemovalDecision.EligibleForReviewedRemoval;
							reason = "No incoming reuse, surviving Collection association, customization, or standalone provenance protects this outgoing native mod; later reviewed consent is still required.";
						}
					}
				}

				result.Add(new CollectionReplacementNativeModImpact(mod, disposition, removalDecision,
					provenance.StandaloneUse, customized, bindingValues, surviving, reason));
			}
			return result;
		}

		private static List<CollectionReplacementEffectImpact> BuildEffectImpacts(CollectionNativeStateIndex state,
			IDictionary<string, CollectionReplacementNativeModImpact> nativeImpactByKey)
		{
			var result = new List<CollectionReplacementEffectImpact>();
			var fileImpacts = new Dictionary<CollectionNativeFileState, CollectionReplacementEffectImpact>();
			foreach (CollectionNativeFileState file in state.Files.Values)
			{
				IList<CollectionNativeOwnerState> owners = GetAuthoritativeFileOwners(file);
				CollectionReplacementEffectImpact impact = ClassifyFileEffect(file, owners, nativeImpactByKey);
				result.Add(impact);
				fileImpacts.Add(file, impact);
			}

			foreach (CollectionNativeIniState ini in state.IniEdits.Values)
				result.Add(ClassifyOwnerKeyEffect(CollectionReplacementEffectKind.Ini, ini.Key.ToString(),
					ini.Values.Select(x => x.OwnerKey).ToList(), nativeImpactByKey));
			foreach (CollectionNativeGameValueState value in state.GameValues.Values)
				result.Add(ClassifyOwnerKeyEffect(CollectionReplacementEffectKind.GameValue, value.Key,
					value.Values.Select(x => x.OwnerKey).ToList(), nativeImpactByKey));

			foreach (CollectionNativePluginState plugin in state.Plugins.Values)
			{
				List<KeyValuePair<CollectionNativeFileState, CollectionReplacementEffectImpact>> candidates = fileImpacts
					.Where(x => StringComparer.OrdinalIgnoreCase.Equals(GetFileName(x.Key.Target.RelativePath), plugin.FileName)).ToList();
				if (candidates.Count == 1)
				{
					CollectionReplacementEffectImpact fileImpact = candidates[0].Value;
					result.Add(new CollectionReplacementEffectImpact(CollectionReplacementEffectKind.Plugin, plugin.FileName,
						fileImpact.Disposition, fileImpact.CurrentOwnerKey, fileImpact.ProjectedOwnerKey,
						fileImpact.OwnerKeys, "Plugin file ownership follows the corresponding managed file difference; activation/environment projection remains C8.2 work."));
				}
				else if (candidates.Count > 1)
				{
					result.Add(new CollectionReplacementEffectImpact(CollectionReplacementEffectKind.Plugin, plugin.FileName,
						CollectionReplacementEffectDisposition.BlockedAmbiguous, null, null, new string[0],
						"Multiple managed file targets map to the same plugin identity; replacement cannot infer one owner transition."));
				}
				else
				{
					result.Add(new CollectionReplacementEffectImpact(CollectionReplacementEffectKind.Plugin, plugin.FileName,
						CollectionReplacementEffectDisposition.PreservedUnknownOrUnmanaged, null, null, new string[0],
						"No managed file-owner record establishes this observed plugin's ownership; C8.1 preserves it rather than treating it as absent."));
				}
			}
			return result;
		}

		private static CollectionReplacementEffectImpact ClassifyFileEffect(CollectionNativeFileState file,
			IList<CollectionNativeOwnerState> owners, IDictionary<string, CollectionReplacementNativeModImpact> nativeImpactByKey)
		{
			if (owners.Any(x => x.Kind == CollectionNativeOwnerKind.Unresolved))
			{
				return new CollectionReplacementEffectImpact(CollectionReplacementEffectKind.File, file.Target.ToString(),
					CollectionReplacementEffectDisposition.BlockedAmbiguous, file.EffectiveOwnerKey, null,
					owners.Select(x => x.OwnerKey), "The native owner stack contains an unresolved owner reference.");
			}

			string currentOwner = file.EffectiveOwnerKey;
			if (String.IsNullOrWhiteSpace(currentOwner) && owners.Count > 0)
				currentOwner = owners[owners.Count - 1].OwnerKey;
			return ClassifyOwnerKeyEffect(CollectionReplacementEffectKind.File, file.Target.ToString(),
				owners.Select(x => x.OwnerKey).ToList(), nativeImpactByKey, currentOwner,
				owners.Where(x => !String.IsNullOrWhiteSpace(x.OwnerKey))
					.GroupBy(x => x.OwnerKey, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(x => x.Key, x => x.Last().Kind, StringComparer.OrdinalIgnoreCase));
		}

		private static CollectionReplacementEffectImpact ClassifyOwnerKeyEffect(CollectionReplacementEffectKind kind,
			string resourceKey, IList<string> ownerKeys, IDictionary<string, CollectionReplacementNativeModImpact> nativeImpactByKey,
			string explicitCurrentOwner = null, IDictionary<string, CollectionNativeOwnerKind> ownerKinds = null)
		{
			string currentOwner = explicitCurrentOwner;
			if (String.IsNullOrWhiteSpace(currentOwner) && ownerKeys.Count > 0)
				currentOwner = ownerKeys[ownerKeys.Count - 1];
			CollectionReplacementNativeModImpact currentImpact;
			if (String.IsNullOrWhiteSpace(currentOwner) || !nativeImpactByKey.TryGetValue(currentOwner, out currentImpact))
			{
				return new CollectionReplacementEffectImpact(kind, resourceKey,
					CollectionReplacementEffectDisposition.PreservedUnknownOrUnmanaged, currentOwner, currentOwner,
					ownerKeys, "The current effect is not proven to be owned by a current managed native mod; preserve it within the observed scope.");
			}

			if (currentImpact.Disposition == CollectionReplacementDiffDisposition.BlockedAmbiguous || currentImpact.BlocksPlanning)
			{
				return new CollectionReplacementEffectImpact(kind, resourceKey,
					CollectionReplacementEffectDisposition.BlockedAmbiguous, currentOwner, currentOwner,
					ownerKeys, "The current managed owner is blocked/ambiguous, so its effect transition cannot be predicted safely.");
			}
			if (currentImpact.Disposition == CollectionReplacementDiffDisposition.ReinstallOrChange)
			{
				return new CollectionReplacementEffectImpact(kind, resourceKey,
					CollectionReplacementEffectDisposition.ReinstallOrChange, currentOwner, null,
					ownerKeys, "The current owner is scheduled for recipe/context change; exact resulting effect belongs to later prepared-recipe/environment planning.");
			}
			if (currentImpact.Disposition == CollectionReplacementDiffDisposition.RetainedReused ||
				currentImpact.Disposition == CollectionReplacementDiffDisposition.SharedProtected)
			{
				return new CollectionReplacementEffectImpact(kind, resourceKey,
					CollectionReplacementEffectDisposition.Retained, currentOwner, currentOwner,
					ownerKeys, "The current managed owner remains protected in the replacement difference.");
			}

			string projectedOwner = null;
			for (int index = ownerKeys.Count - 1; index >= 0; index--)
			{
				string ownerKey = ownerKeys[index];
				if (String.IsNullOrWhiteSpace(ownerKey) || StringComparer.OrdinalIgnoreCase.Equals(ownerKey, currentOwner))
					continue;
				CollectionReplacementNativeModImpact impact;
				if (nativeImpactByKey.TryGetValue(ownerKey, out impact) && impact.Disposition == CollectionReplacementDiffDisposition.OutgoingOnly)
					continue;
				projectedOwner = ownerKey;
				break;
			}

			if (projectedOwner == null)
			{
				return new CollectionReplacementEffectImpact(kind, resourceKey,
					CollectionReplacementEffectDisposition.PreservedUnknownOrUnmanaged, currentOwner, null,
					ownerKeys, "Removing the outgoing managed owner exposes no provable surviving owner in the captured stack; C8.1 does not model that as file/config absence.");
			}

			CollectionReplacementNativeModImpact projectedImpact;
			if (nativeImpactByKey.TryGetValue(projectedOwner, out projectedImpact))
			{
				if (projectedImpact.Disposition == CollectionReplacementDiffDisposition.BlockedAmbiguous || projectedImpact.BlocksPlanning)
				{
					return new CollectionReplacementEffectImpact(kind, resourceKey,
						CollectionReplacementEffectDisposition.BlockedAmbiguous, currentOwner, projectedOwner,
						ownerKeys, "The next surviving managed owner is itself blocked/ambiguous.");
				}
				return new CollectionReplacementEffectImpact(kind, resourceKey,
					CollectionReplacementEffectDisposition.WinnerChange, currentOwner, projectedOwner,
					ownerKeys, "Removing the outgoing current owner exposes another surviving managed owner; replacement must review the winner change.");
			}

			CollectionNativeOwnerKind kindValue;
			bool explicitOriginal = ownerKinds != null && ownerKinds.TryGetValue(projectedOwner, out kindValue) &&
				kindValue == CollectionNativeOwnerKind.OriginalValue;
			return new CollectionReplacementEffectImpact(kind, resourceKey,
				explicitOriginal ? CollectionReplacementEffectDisposition.OutgoingOnly : CollectionReplacementEffectDisposition.PreservedUnknownOrUnmanaged,
				currentOwner, projectedOwner, ownerKeys, explicitOriginal
					? "The outgoing managed effect has an explicit recorded original fallback that remains outside managed ownership."
					: "The next visible owner is not a current managed native mod; preserve it as original/unmanaged rather than treating it as replacement-owned content.");
		}

		private static IList<CollectionNativeOwnerState> GetAuthoritativeFileOwners(CollectionNativeFileState file)
		{
			if (file.Promoted && file.DeploymentOwners.Count > 0)
				return file.DeploymentOwners.ToList();
			if (file.RecordedByVirtualState && file.VirtualOwners.Count > 0)
			{
				List<CollectionNativeOwnerState> active = file.VirtualOwners
					.Where(x => !x.VirtualLinkActive.HasValue || x.VirtualLinkActive.Value).ToList();
				if (active.Count > 0)
					return active;
			}
			return file.InstallLogOwners.ToList();
		}

		private static string GetAuthoritativeRemovalCoverageFailure(CollectionNativeStateIndex state,
			NativeModInstanceIdentity nativeMod)
		{
			if (state.AssociationCoverage != CollectionNativeStateCoverage.Complete)
				return "Collection association coverage is unavailable.";
			if (state.PluginCoverage == CollectionNativeStateCoverage.Unavailable)
				return "Native plugin-state coverage is unavailable.";

			ReadOnlyCollection<CollectionNativeFileState> ownedFiles;
			ReadOnlyCollection<CollectionNativeIniState> ownedIni;
			state.FilesByOwnerKey.TryGetValue(nativeMod.NativeModKey, out ownedFiles);
			state.IniEditsByOwnerKey.TryGetValue(nativeMod.NativeModKey, out ownedIni);
			IEnumerable<CollectionNativeFileState> relevantFiles = ownedFiles == null ? Enumerable.Empty<CollectionNativeFileState>() : ownedFiles;
			IEnumerable<CollectionNativeIniState> relevantIni = ownedIni == null ? Enumerable.Empty<CollectionNativeIniState>() : ownedIni;
			var fileResources = new HashSet<string>(relevantFiles.Select(x => x.Target.ToString()), StringComparer.OrdinalIgnoreCase);
			var fileRoots = new HashSet<string>(relevantFiles.Select(x => x.Target.Root.ToString()), StringComparer.OrdinalIgnoreCase);
			var iniResources = new HashSet<string>(relevantIni.Select(x => x.Key.ToString()), StringComparer.OrdinalIgnoreCase);

			foreach (CollectionNativeStateIssue issue in state.Issues)
			{
				bool relevant;
				switch (issue.Kind)
				{
					case CollectionNativeStateIssueKind.AssociationStateUnavailable:
					case CollectionNativeStateIssueKind.PluginStateUnavailable:
						relevant = true;
						break;
					case CollectionNativeStateIssueKind.UnresolvedOwner:
						relevant = fileResources.Contains(issue.ResourceKey);
						break;
					case CollectionNativeStateIssueKind.PhysicalRootUnavailable:
						relevant = fileResources.Contains(issue.ResourceKey) || fileRoots.Contains(issue.ResourceKey);
						break;
					case CollectionNativeStateIssueKind.AmbiguousIniIdentity:
						relevant = iniResources.Contains(issue.ResourceKey);
						break;
					default:
						relevant = true;
						break;
				}
				if (relevant)
					return String.Format("Native-state coverage is incomplete at '{0}': {1}", issue.ResourceKey, issue.Message);
			}
			return null;
		}

		private static string GetFileName(string relativePath)
		{
			if (String.IsNullOrEmpty(relativePath))
				return String.Empty;
			int slash = Math.Max(relativePath.LastIndexOf('/'), relativePath.LastIndexOf('\\'));
			return slash < 0 ? relativePath : relativePath.Substring(slash + 1);
		}
	}
}
