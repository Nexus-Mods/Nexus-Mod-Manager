using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Builds the read-only C6.4 file/plugin/configuration impact view for one additive Collection plan.
	/// </summary>
	/// <remarks>
	/// The planner never changes native ownership, plugin state or Collection persistence. File winners are derived only from
	/// characterized file-priority rules; installation/phase order is deliberately not used as an implicit conflict rule.
	/// </remarks>
	public sealed class CollectionConflictImpactPlanner
	{
		/// <summary>Plans deterministic impacts against one exact native-state snapshot.</summary>
		public CollectionConflictImpactPlan Plan(ResolvedCollectionPlan plan, CollectionMemberMatchSet matches,
			CollectionDependencyPhasePlan dependencyPlan, CollectionNativeStateIndex nativeState,
			IEnumerable<CollectionMemberEffectPreview> effectPreviews)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (matches == null) throw new ArgumentNullException(nameof(matches));
			if (dependencyPlan == null) throw new ArgumentNullException(nameof(dependencyPlan));
			if (nativeState == null) throw new ArgumentNullException(nameof(nativeState));
			if (effectPreviews == null) throw new ArgumentNullException(nameof(effectPreviews));
			ValidateInputs(plan, matches, dependencyPlan, nativeState);

			var issues = new List<CollectionConflictImpactIssue>();
			if (dependencyPlan.IsBlocked)
			{
				issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.UpstreamPlanBlocked,
					CollectionConflictImpactStatus.Blocked, null, String.Empty,
					"C6.4 cannot plan impacts until the exact C6.3 dependency/phase plan is ready."));
				return Empty(plan, nativeState, issues);
			}

			Dictionary<CollectionMemberKey, CollectionMemberEffectPreview> previews = IndexPreviews(plan, effectPreviews, issues);
			Dictionary<CollectionMemberKey, CollectionMemberMatchResult> matchByKey = matches.MembersByKey.ToDictionary(x => x.Key, x => x.Value);

			EvaluateCompatibilityConstraints(plan, matches, nativeState, issues);
			EvaluateExternalPriorityRules(plan, matches, nativeState, previews, issues);

			foreach (ResolvedCollectionMemberPlan member in plan.SelectedMembers)
			{
				CollectionMemberMatchResult match = matchByKey[member.MemberKey];
				if (match.Disposition == CollectionMemberMatchDisposition.AcquisitionRequired)
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.AcquisitionOrTranslationRequired,
						CollectionConflictImpactStatus.PreparationRequired, member.MemberKey, member.ArtifactChoice.SelectedArtifact.ToString(),
						"The member still requires artifact acquisition and C5 translation before exact conflict/impact review."));
					continue;
				}
				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
					continue;
				if (match.IsBlocked)
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.UpstreamPlanBlocked,
						CollectionConflictImpactStatus.Blocked, member.MemberKey, String.Empty,
						"The member is blocked by C6.2 matching and cannot participate in an impact plan."));
					continue;
				}

				CollectionMemberEffectPreview preview;
				if (!previews.TryGetValue(member.MemberKey, out preview))
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.AcquisitionOrTranslationRequired,
						CollectionConflictImpactStatus.PreparationRequired, member.MemberKey, String.Empty,
						"A member which would mutate native state requires an exact translated C5 effect preview."));
					continue;
				}
				if (!preview.IsComplete)
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.EffectPreviewIncomplete,
						CollectionConflictImpactStatus.Blocked, member.MemberKey, String.Empty,
						"The translated native recipe contains effects which C6.4 cannot bound exactly."));
				}
			}

			Dictionary<CollectionMemberKey, CollectionMemberEffectPreview> mutationPreviews = previews
				.Where(x => matchByKey[x.Key].Disposition == CollectionMemberMatchDisposition.ArchiveOnlyReuse ||
					matchByKey[x.Key].Disposition == CollectionMemberMatchDisposition.ReinstallRequired)
				.ToDictionary(x => x.Key, x => x.Value);

			var associationKinds = new Dictionary<Guid, CollectionAssociationImpactKind>();
			AddSharedNativeInstanceImpacts(matches, nativeState, associationKinds);

			List<CollectionFileImpact> fileImpacts = BuildFileImpacts(plan, matches, nativeState, mutationPreviews, associationKinds, issues);
			List<CollectionPluginImpact> pluginImpacts = BuildPluginImpacts(plan, nativeState, matches, mutationPreviews, associationKinds, issues);
			List<CollectionConfigurationImpact> configImpacts = BuildConfigurationImpacts(nativeState, matches, mutationPreviews, associationKinds, issues);

			ReviewAffectedAssociations(nativeState, associationKinds, issues);
			List<CollectionAssociationImpact> associationImpacts = associationKinds.OrderBy(x => x.Key)
				.Where(x => nativeState.Associations.ContainsKey(x.Key))
				.Select(x => new CollectionAssociationImpact(nativeState.Associations[x.Key], x.Value)).ToList();

			issues.Sort(CompareIssues);
			return new CollectionConflictImpactPlan(plan, nativeState, fileImpacts, pluginImpacts, configImpacts, associationImpacts, issues);
		}


		private static void EvaluateCompatibilityConstraints(ResolvedCollectionPlan plan, CollectionMemberMatchSet matches,
			CollectionNativeStateIndex nativeState, IList<CollectionConflictImpactIssue> issues)
		{
			if (plan.CapabilityReport.Manifest.ConflictConstraints.Count == 0)
				return;

			var selectedByKey = plan.SelectedMembers.ToDictionary(x => x.MemberKey);
			var matchByKey = matches.MembersByKey;

			foreach (CollectionConflictConstraint constraint in plan.CapabilityReport.Manifest.ConflictConstraints)
			{
				ResolvedCollectionMemberPlan sourceMember;
				if (!selectedByKey.TryGetValue(constraint.SourceMemberKey, out sourceMember))
					continue; // A conflicts rule attached to an unselected optional member is inactive, matching Vortex enabled-mod behavior.

				var representedNativeIds = new HashSet<NativeModInstanceIdentity>();
				foreach (CollectionMemberKey targetKey in constraint.MatchingMemberKeys)
				{
					if (targetKey.Equals(constraint.SourceMemberKey) || !selectedByKey.ContainsKey(targetKey))
						continue; // Vortex excludes the rule's own source instance before testing the reference.
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.CompatibilityConflict,
						CollectionConflictImpactStatus.ActionRequired, constraint.SourceMemberKey, "member:" + targetKey,
						"The effective Collection selection contains another member matched by a Vortex conflicts rule; change the optional selection or resolve the incompatibility before applying."));
					CollectionMemberMatchResult targetMatch;
					if (matchByKey.TryGetValue(targetKey, out targetMatch) && targetMatch.MatchedNativeMod != null)
						representedNativeIds.Add(targetMatch.MatchedNativeMod.Identity);
				}

				CollectionMemberMatchResult sourceMatch;
				NativeModInstanceIdentity sourceNative = matchByKey.TryGetValue(constraint.SourceMemberKey, out sourceMatch) &&
					sourceMatch.MatchedNativeMod != null ? sourceMatch.MatchedNativeMod.Identity : null;
				if (sourceNative != null) representedNativeIds.Add(sourceNative);

				List<CollectionNativeModState> externalNativeMods = nativeState.Mods.Values
					.Where(x => !representedNativeIds.Contains(x.Identity))
					.OrderBy(x => x.Identity.NativeModKey, StringComparer.Ordinal).ToList();
				if (!HasNativeCandidateMarker(constraint.Reference) && externalNativeMods.Count > 0)
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ConflictReferenceEvaluationRequired,
						CollectionConflictImpactStatus.ActionRequired, constraint.SourceMemberKey, "constraint:" + constraint.SourceMemberKey,
						"NMM cannot identify external native candidates for this Vortex conflict reference from committed metadata alone; explicit review is required."));
					continue;
				}

				foreach (CollectionNativeModState nativeMod in externalNativeMods)
				{
					CollectionConflictNativeMatch nativeMatch = MatchConflictReference(constraint.Reference, nativeMod);
					if (nativeMatch == CollectionConflictNativeMatch.NoMatch)
						continue;
					if (nativeMatch == CollectionConflictNativeMatch.Match)
					{
						issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.CompatibilityConflict,
							CollectionConflictImpactStatus.ActionRequired, constraint.SourceMemberKey, "native:" + nativeMod.Identity.NativeModKey,
							"An existing NMM mod matches a Vortex conflicts rule for the selected Collection member; remove/replace the conflicting native mod or change the Collection selection before applying."));
					}
					else
					{
						issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ConflictReferenceEvaluationRequired,
							CollectionConflictImpactStatus.ActionRequired, constraint.SourceMemberKey, "native:" + nativeMod.Identity.NativeModKey,
							"NMM cannot prove whether an existing native mod satisfies every identifying/version field of this Vortex conflict reference from committed metadata alone; explicit review is required."));
					}
				}
			}
		}


		private static void EvaluateExternalPriorityRules(ResolvedCollectionPlan plan, CollectionMemberMatchSet matches,
			CollectionNativeStateIndex nativeState, IDictionary<CollectionMemberKey, CollectionMemberEffectPreview> previews,
			IList<CollectionConflictImpactIssue> issues)
		{
			if (plan.CapabilityReport.Manifest.ExternalFilePriorityRules.Count == 0) return;
			var selected = new HashSet<CollectionMemberKey>(plan.SelectedMembers.Select(x => x.MemberKey));
			var represented = new HashSet<NativeModInstanceIdentity>(matches.Members
				.Where(x => x.MatchedNativeMod != null).Select(x => x.MatchedNativeMod.Identity));

			foreach (CollectionExternalFilePriorityRule rule in plan.CapabilityReport.Manifest.ExternalFilePriorityRules)
			{
				if (!selected.Contains(rule.MemberKey)) continue;
				HashSet<ModDeploymentTarget> memberTargets = GetMemberFileTargets(rule.MemberKey, matches, nativeState, previews);
				if (memberTargets.Count == 0)
					continue; // A deployment-priority edge has no native effect when the Collection member owns no file target.

				var matchesFound = new List<CollectionNativeModState>();
				var unknown = new List<CollectionNativeModState>();
				foreach (CollectionNativeModState nativeMod in nativeState.Mods.Values
					.Where(x => !represented.Contains(x.Identity)).OrderBy(x => x.Identity.NativeModKey, StringComparer.Ordinal))
				{
					if (!NativeModOwnsAnyTarget(nativeState, nativeMod.Identity.NativeModKey, memberTargets))
						continue; // Vortex before/after only matters where the two mods actually compete for a deployed path.
					CollectionConflictNativeMatch result = MatchConflictReference(rule.ExternalReference, nativeMod);
					if (result == CollectionConflictNativeMatch.Match) matchesFound.Add(nativeMod);
					else if (result == CollectionConflictNativeMatch.Unknown) unknown.Add(nativeMod);
				}

				if (matchesFound.Count > 1)
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ExternalPriorityEndpointPresent,
						CollectionConflictImpactStatus.ActionRequired, rule.MemberKey, "external-priority:" + rule.MemberKey,
						"More than one installed external NMM mod matches a Vortex before/after endpoint on a file target shared with the Collection member; NMM cannot choose which native instance the rule refers to."));
					continue;
				}

				if (matchesFound.Count == 1 && rule.MemberIsLowerPriority)
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ExternalPriorityEndpointPresent,
						CollectionConflictImpactStatus.ActionRequired, rule.MemberKey, "native:" + matchesFound[0].Identity.NativeModKey,
						"An installed external NMM mod exactly matches a Vortex before/after endpoint and must remain the higher-priority owner on shared files; the current additive winner model cannot yet persist that external winner decision."));
					continue;
				}

				// One exact lower-priority external endpoint is fully characterized: the Collection rule itself authorizes the
				// incoming member to become the winner on shared targets. BuildFileImpacts consumes that authorization.
				if (matchesFound.Count == 1)
					continue;

				if (unknown.Count > 0)
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ExternalPriorityReferenceEvaluationRequired,
						CollectionConflictImpactStatus.ActionRequired, rule.MemberKey, "external-priority:" + rule.MemberKey,
						"NMM cannot prove whether an installed native mod sharing files with the Collection member satisfies the external Vortex before/after endpoint; explicit review is required."));
				}
				// No exact/unknown overlapping endpoint is the characterized Vortex absent/non-conflicting endpoint case.
			}
		}

		private static HashSet<ModDeploymentTarget> GetMemberFileTargets(CollectionMemberKey memberKey, CollectionMemberMatchSet matches,
			CollectionNativeStateIndex nativeState, IDictionary<CollectionMemberKey, CollectionMemberEffectPreview> previews)
		{
			var result = new HashSet<ModDeploymentTarget>();
			CollectionMemberEffectPreview preview;
			if (previews.TryGetValue(memberKey, out preview) && preview.IsComplete)
				foreach (CollectionPlannedFileEffect file in preview.Files) result.Add(file.Target);

			CollectionMemberMatchResult match;
			if (matches.MembersByKey.TryGetValue(memberKey, out match) && match.MatchedNativeMod != null)
			{
				ReadOnlyCollection<CollectionNativeFileState> existing;
				if (nativeState.FilesByOwnerKey.TryGetValue(match.MatchedNativeMod.Identity.NativeModKey, out existing))
					foreach (CollectionNativeFileState file in existing) result.Add(file.Target);
			}
			return result;
		}

		private static bool NativeModOwnsAnyTarget(CollectionNativeStateIndex nativeState, string ownerKey,
			HashSet<ModDeploymentTarget> targets)
		{
			ReadOnlyCollection<CollectionNativeFileState> files;
			return nativeState.FilesByOwnerKey.TryGetValue(ownerKey, out files) && files.Any(x => targets.Contains(x.Target));
		}


		private static bool HasNativeCandidateMarker(CollectionConflictReference reference)
		{
			return !String.IsNullOrEmpty(reference.Repository) || !String.IsNullOrEmpty(reference.LogicalFileName) ||
				!String.IsNullOrEmpty(reference.FileExpression);
		}

		private enum CollectionConflictNativeMatch
		{
			NoMatch = 0,
			Match = 1,
			Unknown = 2
		}

		private static CollectionConflictNativeMatch MatchConflictReference(CollectionConflictReference reference,
			CollectionNativeModState nativeMod)
		{
			bool fuzzy = reference.VersionMatch.IsAny || reference.VersionMatch.IsRange;
			bool hasObservableMarker = false;
			bool identityUnknown = false;

			if (!String.IsNullOrEmpty(reference.Repository))
			{
				hasObservableMarker = true;
				long nativeModId;
				if (!Int64.TryParse(nativeMod.NexusModId, out nativeModId) || !reference.RepositoryModId.HasValue ||
					nativeModId != reference.RepositoryModId.Value)
					return CollectionConflictNativeMatch.NoMatch;
				if (!fuzzy)
				{
					long nativeFileId;
					if (!reference.RepositoryFileId.HasValue || !Int64.TryParse(nativeMod.NexusFileId, out nativeFileId) ||
						nativeFileId != reference.RepositoryFileId.Value)
						return CollectionConflictNativeMatch.NoMatch;
				}
			}

			if (!String.IsNullOrEmpty(reference.LogicalFileName))
			{
				hasObservableMarker = true;
				if (String.IsNullOrWhiteSpace(nativeMod.ModName))
					identityUnknown = true;
				else if (!StringComparer.Ordinal.Equals(reference.LogicalFileName, nativeMod.ModName) &&
					String.IsNullOrEmpty(reference.FileExpression))
					return CollectionConflictNativeMatch.NoMatch;
			}

			if (!String.IsNullOrEmpty(reference.FileExpression))
			{
				hasObservableMarker = true;
				if (String.IsNullOrWhiteSpace(nativeMod.FileName))
					identityUnknown = true;
				else
				{
					string candidate = SanitizeVortexFileExpressionCandidate(nativeMod.FileName);
					if (!StringComparer.Ordinal.Equals(reference.FileExpression, candidate))
					{
						// Vortex checks exact equality before minimatch. A retained expression containing literal brackets
						// may therefore be an exact generated archive name, which is fully observable above. If exact
						// equality fails, however, '[' / ']' could be a minimatch character class. Do not silently
						// reinterpret that uncharacterized fallback as a definite non-match.
						if (ContainsVortexBracketPatternSyntax(reference.FileExpression))
							identityUnknown = true;
						else
							return CollectionConflictNativeMatch.NoMatch;
					}
				}
			}

			if (!String.IsNullOrEmpty(reference.FileMd5) && !fuzzy && !hasObservableMarker)
			{
				// Vortex treats fileMD5 as one positive identity path, not a mandatory conjunction with logical/file-expression/version
				// matching. InstallLog does not retain MD5, so it is unknown only when no other committed marker can identify the endpoint.
				identityUnknown = true;
			}
			if (!String.IsNullOrEmpty(reference.Tag) && !hasObservableMarker)
				identityUnknown = true; // Vortex reference tags are not native NMM metadata.

			if (!hasObservableMarker && !identityUnknown)
				return CollectionConflictNativeMatch.NoMatch;
			if (identityUnknown)
				return CollectionConflictNativeMatch.Unknown;

			string version = !String.IsNullOrWhiteSpace(nativeMod.HumanReadableVersion)
				? nativeMod.HumanReadableVersion : nativeMod.MachineVersion;
			CollectionVortexVersionMatchResult versionResult = reference.VersionMatch.Evaluate(version);
			if (versionResult == CollectionVortexVersionMatchResult.Unknown)
				return CollectionConflictNativeMatch.Unknown;
			return versionResult == CollectionVortexVersionMatchResult.Match
				? CollectionConflictNativeMatch.Match
				: CollectionConflictNativeMatch.NoMatch;
		}

		private static bool ContainsVortexBracketPatternSyntax(string expression)
		{
			return !String.IsNullOrEmpty(expression) && (expression.IndexOf('[') >= 0 || expression.IndexOf(']') >= 0);
		}

		private static string SanitizeVortexFileExpressionCandidate(string fileName)
		{
			string name = Path.GetFileNameWithoutExtension(fileName ?? String.Empty);
			if (String.IsNullOrEmpty(name)) return String.Empty;
			name = System.Text.RegularExpressions.Regex.Replace(name, @"\.\d+$", String.Empty);
			return System.Text.RegularExpressions.Regex.Replace(name, @" \(\d+\)$", String.Empty);
		}

		private static CollectionConflictImpactPlan Empty(ResolvedCollectionPlan plan, CollectionNativeStateIndex nativeState,
			IEnumerable<CollectionConflictImpactIssue> issues)
		{
			return new CollectionConflictImpactPlan(plan, nativeState, new CollectionFileImpact[0],
				new CollectionPluginImpact[0], new CollectionConfigurationImpact[0], new CollectionAssociationImpact[0], issues);
		}

		private static void ValidateInputs(ResolvedCollectionPlan plan, CollectionMemberMatchSet matches,
			CollectionDependencyPhasePlan dependencyPlan, CollectionNativeStateIndex nativeState)
		{
			if (!Equals(plan.Identity, matches.PlanIdentity) || !Equals(plan.Identity, dependencyPlan.PlanIdentity))
				throw new ArgumentException("C6.4 inputs must belong to the same resolved Collection plan.");
			if (!Equals(plan.Target, matches.Target) || !Equals(plan.Target, dependencyPlan.Target) || !Equals(plan.Target, nativeState.Target))
				throw new ArgumentException("C6.4 inputs must describe the same target.");
			if (!Equals(matches.StateFingerprint, dependencyPlan.StateFingerprint) || !Equals(matches.StateFingerprint, nativeState.Fingerprint) ||
				!Equals(plan.CurrentStateFingerprint, nativeState.Fingerprint))
				throw new ArgumentException("C6.4 inputs must use the exact same C6.1 native-state fingerprint.");
		}

		private static Dictionary<CollectionMemberKey, CollectionMemberEffectPreview> IndexPreviews(ResolvedCollectionPlan plan,
			IEnumerable<CollectionMemberEffectPreview> values, IList<CollectionConflictImpactIssue> issues)
		{
			var selected = plan.SelectedMembers.ToDictionary(x => x.MemberKey);
			var result = new Dictionary<CollectionMemberKey, CollectionMemberEffectPreview>();
			foreach (CollectionMemberEffectPreview preview in values)
			{
				if (preview == null) throw new ArgumentException("Effect previews cannot contain null records.", nameof(values));
				ResolvedCollectionMemberPlan member;
				if (!selected.TryGetValue(preview.MemberKey, out member))
					throw new ArgumentException("An effect preview references a member outside the selected plan.", nameof(values));
				if (result.ContainsKey(preview.MemberKey))
					throw new ArgumentException("Only one effect preview may be supplied per selected member.", nameof(values));
				if (!Equals(preview.RecipeIdentity, member.RecipeIdentity))
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.EffectPreviewMismatch,
						CollectionConflictImpactStatus.Blocked, member.MemberKey, String.Empty,
						"The native effect preview recipe does not match the resolved member recipe identity."));
					continue;
				}
				result.Add(preview.MemberKey, preview);
			}
			return result;
		}

		private static List<CollectionFileImpact> BuildFileImpacts(ResolvedCollectionPlan plan, CollectionMemberMatchSet matches,
			CollectionNativeStateIndex nativeState, IDictionary<CollectionMemberKey, CollectionMemberEffectPreview> previews,
			IDictionary<Guid, CollectionAssociationImpactKind> associationKinds, IList<CollectionConflictImpactIssue> issues)
		{
			var matchByKey = matches.MembersByKey;
			var writersByTarget = new Dictionary<ModDeploymentTarget, List<CollectionMemberKey>>();
			foreach (CollectionMemberEffectPreview preview in previews.Values.Where(x => x.IsComplete))
				foreach (CollectionPlannedFileEffect effect in preview.Files)
				{
					List<CollectionMemberKey> writers;
					if (!writersByTarget.TryGetValue(effect.Target, out writers))
					{
						writers = new List<CollectionMemberKey>();
						writersByTarget.Add(effect.Target, writers);
					}
					if (!writers.Contains(preview.MemberKey)) writers.Add(preview.MemberKey);
				}

			// Installed-compatible members do not need a C5 mutation preview, but their existing native files still
			// participate in winner rules on any target touched by an incoming mutation. Do not scan unrelated targets.
			foreach (CollectionMemberMatchResult match in matches.Members.Where(x =>
				x.Disposition == CollectionMemberMatchDisposition.InstalledCompatible && x.MatchedNativeMod != null))
			{
				ReadOnlyCollection<CollectionNativeFileState> ownedFiles;
				if (!nativeState.FilesByOwnerKey.TryGetValue(match.MatchedNativeMod.Identity.NativeModKey, out ownedFiles)) continue;
				foreach (CollectionNativeFileState ownedFile in ownedFiles)
				{
					List<CollectionMemberKey> writers;
					if (writersByTarget.TryGetValue(ownedFile.Target, out writers) && !writers.Contains(match.Member.MemberKey))
						writers.Add(match.Member.MemberKey);
				}
			}

			var result = new List<CollectionFileImpact>();
			foreach (KeyValuePair<ModDeploymentTarget, List<CollectionMemberKey>> entry in writersByTarget
				.OrderBy(x => x.Key.Root).ThenBy(x => x.Key.RelativePath, StringComparer.OrdinalIgnoreCase))
			{
				entry.Value.Sort(CompareMemberKeys);
				bool priorityCycle;
				CollectionMemberKey winner = ResolveFileWinner(entry.Value, plan.CapabilityReport.Manifest.FilePriorityRules, out priorityCycle);
				if (priorityCycle)
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.FilePriorityCycle,
						CollectionConflictImpactStatus.Blocked, null, entry.Key.ToString(),
						"The characterized Collection file-priority rules form a cycle for members writing the same native target."));
				}
				else if (winner == null)
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.FileWinnerDecisionRequired,
						CollectionConflictImpactStatus.ActionRequired, null, entry.Key.ToString(),
						"Several selected members write the same native target without a unique characterized file-priority winner."));
				}

				CollectionNativeFileState current;
				nativeState.Files.TryGetValue(entry.Key, out current);
				string currentOwner = current == null ? null : current.EffectiveOwnerKey;
				if (HasUnresolvedManagedOwnership(current))
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ExistingFileOwnershipUnresolved,
						CollectionConflictImpactStatus.Blocked, winner, entry.Key.ToString(),
						"The target has recorded managed ownership which C6.1 could not reduce to one effective native owner; exact additive winner planning must stop."));
				}
				HashSet<Guid> affected = AssociationIdsForOwner(nativeState, currentOwner);
				foreach (Guid associationId in affected) AddAssociationImpact(associationKinds, associationId, CollectionAssociationImpactKind.FileWinner);

				if (winner != null && !String.IsNullOrWhiteSpace(currentOwner) && !CurrentOwnerIsIncomingWriter(currentOwner, entry.Value, matchByKey) &&
					!ExternalPriorityAuthorizesIncomingWinner(plan, winner, currentOwner, nativeState))
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired,
						CollectionConflictImpactStatus.ActionRequired, winner, entry.Key.ToString(),
						"Applying the incoming file winner would replace an existing unrelated managed owner; additive mode requires explicit reviewed conflict handling."));
				}

				result.Add(new CollectionFileImpact(entry.Key, entry.Value, winner, currentOwner, affected));
			}
			return result;
		}

		private static bool ExternalPriorityAuthorizesIncomingWinner(ResolvedCollectionPlan plan, CollectionMemberKey winner,
			string currentOwnerKey, CollectionNativeStateIndex nativeState)
		{
			CollectionNativeModState currentOwner;
			if (!nativeState.ModsByNativeKey.TryGetValue(currentOwnerKey, out currentOwner)) return false;
			foreach (CollectionExternalFilePriorityRule rule in plan.CapabilityReport.Manifest.ExternalFilePriorityRules)
			{
				if (!rule.MemberKey.Equals(winner) || rule.MemberIsLowerPriority) continue;
				if (MatchConflictReference(rule.ExternalReference, currentOwner) == CollectionConflictNativeMatch.Match)
					return true;
			}
			return false;
		}

		private static CollectionMemberKey ResolveFileWinner(IList<CollectionMemberKey> writers,
			IEnumerable<CollectionFilePriorityRule> rules, out bool cycle)
		{
			cycle = false;
			if (writers.Count == 1) return writers[0];
			var writerSet = new HashSet<CollectionMemberKey>(writers);
			var outgoing = writers.ToDictionary(x => x, x => new HashSet<CollectionMemberKey>());
			var indegree = writers.ToDictionary(x => x, x => 0);
			foreach (CollectionFilePriorityRule rule in rules)
			{
				if (!writerSet.Contains(rule.LowerPriorityMemberKey) || !writerSet.Contains(rule.HigherPriorityMemberKey)) continue;
				if (outgoing[rule.LowerPriorityMemberKey].Add(rule.HigherPriorityMemberKey))
					indegree[rule.HigherPriorityMemberKey]++;
			}

			var queue = new SortedSet<CollectionMemberKey>(new MemberKeyComparer());
			foreach (CollectionMemberKey writer in writers) if (indegree[writer] == 0) queue.Add(writer);
			int visited = 0;
			while (queue.Count > 0)
			{
				CollectionMemberKey current = queue.Min;
				queue.Remove(current);
				visited++;
				foreach (CollectionMemberKey next in outgoing[current]) if (--indegree[next] == 0) queue.Add(next);
			}
			if (visited != writers.Count) { cycle = true; return null; }

			List<CollectionMemberKey> maxima = writers.Where(x => outgoing[x].Count == 0).ToList();
			return maxima.Count == 1 ? maxima[0] : null;
		}

		private static bool HasUnresolvedManagedOwnership(CollectionNativeFileState current)
		{
			if (current == null) return false;
			IEnumerable<CollectionNativeOwnerState> owners = current.InstallLogOwners
				.Concat(current.DeploymentOwners).Concat(current.VirtualOwners);
			if (owners.Any(x => x.Kind == CollectionNativeOwnerKind.Unresolved)) return true;
			return String.IsNullOrWhiteSpace(current.EffectiveOwnerKey) && owners.Any(x => x.Kind == CollectionNativeOwnerKind.NativeMod);
		}

		private static bool CurrentOwnerIsIncomingWriter(string ownerKey, IEnumerable<CollectionMemberKey> writers,
			IReadOnlyDictionary<CollectionMemberKey, CollectionMemberMatchResult> matches)
		{
			foreach (CollectionMemberKey writer in writers)
			{
				CollectionNativeModState mod = matches[writer].MatchedNativeMod;
				if (mod != null && StringComparer.OrdinalIgnoreCase.Equals(ownerKey, mod.Identity.NativeModKey)) return true;
			}
			return false;
		}

		private static List<CollectionPluginImpact> BuildPluginImpacts(ResolvedCollectionPlan plan, CollectionNativeStateIndex nativeState,
			CollectionMemberMatchSet matches, IDictionary<CollectionMemberKey, CollectionMemberEffectPreview> previews,
			IDictionary<Guid, CollectionAssociationImpactKind> associationKinds, IList<CollectionConflictImpactIssue> issues)
		{
			List<Tuple<CollectionMemberKey, CollectionPlannedPluginEffect>> original = previews.Values.Where(x => x.IsComplete)
				.SelectMany(x => x.PluginEffects.Select(effect => Tuple.Create(x.MemberKey, effect))).ToList();
			List<Tuple<CollectionMemberKey, CollectionPlannedPluginEffect>> all;

			if (plan.CapabilityReport.Manifest.HasPluginStateSection)
			{
				var candidates = new Dictionary<string, List<Tuple<CollectionMemberKey, string>>>(StringComparer.OrdinalIgnoreCase);
				foreach (Tuple<CollectionMemberKey, CollectionPlannedPluginEffect> item in original.Where(x => x.Item2.Kind == CollectionPlannedPluginEffectKind.Activation))
					AddPluginCandidate(candidates, item.Item1, item.Item2.PluginPaths[0]);

				foreach (CollectionMemberMatchResult match in matches.Members.Where(x => x.Disposition == CollectionMemberMatchDisposition.InstalledCompatible && x.MatchedNativeMod != null))
				{
					string ownerKey = match.MatchedNativeMod.Identity.NativeModKey;
					foreach (CollectionNativePluginState plugin in nativeState.Plugins.Values)
					{
						CollectionNativeFileState file = FindFileForPlugin(nativeState, plugin.FileName);
						if (file != null && FileHasNativeOwner(file, ownerKey))
							AddPluginCandidate(candidates, match.Member.MemberKey, String.IsNullOrWhiteSpace(file.PhysicalPath) ? plugin.FileName : file.PhysicalPath);
					}
				}

				var enabledNames = new HashSet<string>(plan.CapabilityReport.Manifest.PluginStates.Where(x => x.Enabled).Select(x => x.PluginName),
					StringComparer.OrdinalIgnoreCase);
				all = original.Where(x => x.Item2.Kind != CollectionPlannedPluginEffectKind.Activation).ToList();
				foreach (KeyValuePair<string, List<Tuple<CollectionMemberKey, string>>> candidate in candidates.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
				{
					CollectionMemberKey memberKey = candidate.Value.Select(x => x.Item1).Distinct().OrderBy(x => x, new MemberKeyComparer()).First();
					CollectionNativeFileState existingFile = FindFileForPlugin(nativeState, candidate.Key);
					string path = existingFile != null && !String.IsNullOrWhiteSpace(existingFile.PhysicalPath)
						? existingFile.PhysicalPath
						: candidate.Value.Select(x => x.Item2).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First();
					all.Add(Tuple.Create(memberKey, CollectionPlannedPluginEffect.Activation(path, enabledNames.Contains(candidate.Key))));
				}

			}
			else
				all = original;

			if (all.Count == 0) return new List<CollectionPluginImpact>();
			if (nativeState.PluginCoverage != CollectionNativeStateCoverage.Complete)
			{
				issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.PluginStateUnavailable,
					CollectionConflictImpactStatus.Blocked, null, String.Empty,
					"The incoming recipe changes plugin state but the C6.1 plugin snapshot is not complete for this target."));
			}

			DetectConflictingPluginIntent(all, issues);
			var result = new List<CollectionPluginImpact>();
			var selectedKeys = new HashSet<CollectionMemberKey>(plan.SelectedMembers.Select(x => x.MemberKey));
			foreach (Tuple<CollectionMemberKey, CollectionPlannedPluginEffect> item in all)
			{
				CollectionNativePluginState current = item.Item2.PluginPaths.Count == 1 ? FindPlugin(nativeState, item.Item2.PluginPaths[0]) : null;
				var affected = new HashSet<Guid>();
				bool changesExistingUnrelatedPlugin = false;
				foreach (string pluginPath in item.Item2.PluginPaths)
				{
					affected.UnionWith(AssociationIdsForPlugin(nativeState, pluginPath));
					CollectionNativeFileState pluginFile = FindFileForPlugin(nativeState, pluginPath);
					string ownerKey = pluginFile == null ? null : pluginFile.EffectiveOwnerKey;
					if (!String.IsNullOrWhiteSpace(ownerKey) && !CurrentOwnerIsIncomingWriter(ownerKey, selectedKeys, matches.MembersByKey))
						changesExistingUnrelatedPlugin = true;
					CollectionNativePluginState existingPlugin = FindPlugin(nativeState, pluginPath);
					CollectionMemberEffectPreview memberPreview;
					bool previewWrites = previews.TryGetValue(item.Item1, out memberPreview) && PreviewWritesPlugin(memberPreview, pluginPath);
					if (existingPlugin != null && PluginEffectChangesExistingState(item.Item2, existingPlugin) &&
						(String.IsNullOrWhiteSpace(ownerKey) || (!previewWrites && !CurrentOwnerIsIncomingWriter(ownerKey, selectedKeys, matches.MembersByKey))))
						changesExistingUnrelatedPlugin = true;
				}
				foreach (Guid associationId in affected) AddAssociationImpact(associationKinds, associationId, CollectionAssociationImpactKind.PluginState);
				if (changesExistingUnrelatedPlugin)
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ExistingPluginStateDecisionRequired,
						CollectionConflictImpactStatus.ActionRequired, item.Item1, String.Join("|", item.Item2.PluginPaths),
						"The incoming recipe would change plugin state/order belonging to the existing additive setup; explicit review is required."));
				}
				if (item.Item2.Kind == CollectionPlannedPluginEffectKind.AbsoluteOrderIndex)
				{
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.PluginOrderImpactRequiresReview,
						CollectionConflictImpactStatus.ActionRequired, item.Item1, item.Item2.PluginPaths[0],
						"An absolute plugin index can shift unrelated plugins in additive mode and therefore requires explicit impact review."));
				}
				result.Add(new CollectionPluginImpact(item.Item1, item.Item2, current, affected));
			}
			return result;
		}

		private static void AddPluginCandidate(IDictionary<string, List<Tuple<CollectionMemberKey, string>>> candidates,
			CollectionMemberKey memberKey, string pluginPath)
		{
			string name = Path.GetFileName((pluginPath ?? String.Empty).Replace('/', '\\'));
			if (String.IsNullOrWhiteSpace(name)) return;
			List<Tuple<CollectionMemberKey, string>> values;
			if (!candidates.TryGetValue(name, out values))
			{
				values = new List<Tuple<CollectionMemberKey, string>>();
				candidates.Add(name, values);
			}
			if (!values.Any(x => x.Item1.Equals(memberKey) && StringComparer.OrdinalIgnoreCase.Equals(x.Item2, pluginPath)))
				values.Add(Tuple.Create(memberKey, pluginPath));
		}

		private static bool FileHasNativeOwner(CollectionNativeFileState file, string ownerKey)
		{
			if (file == null || String.IsNullOrWhiteSpace(ownerKey)) return false;
			return file.InstallLogOwners.Concat(file.DeploymentOwners).Concat(file.VirtualOwners).Any(x =>
				x.Kind == CollectionNativeOwnerKind.NativeMod && StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey));
		}

		private static void DetectConflictingPluginIntent(IEnumerable<Tuple<CollectionMemberKey, CollectionPlannedPluginEffect>> effects,
			IList<CollectionConflictImpactIssue> issues)
		{
			var activations = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			var absoluteIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var relativeEdges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (Tuple<CollectionMemberKey, CollectionPlannedPluginEffect> item in effects)
			{
				CollectionPlannedPluginEffect effect = item.Item2;
				if (effect.Kind == CollectionPlannedPluginEffectKind.Activation)
				{
					string path = effect.PluginPaths[0];
					bool existing;
					if (activations.TryGetValue(path, out existing) && existing != effect.Active.Value)
					{
						issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ConflictingPluginIntent,
							CollectionConflictImpactStatus.ActionRequired, item.Item1, path,
							"Selected members request contradictory activation states for the same plugin."));
					}
					else activations[path] = effect.Active.Value;
				}
				else if (effect.Kind == CollectionPlannedPluginEffectKind.AbsoluteOrderIndex)
				{
					string path = effect.PluginPaths[0];
					int existing;
					if (absoluteIndices.TryGetValue(path, out existing) && existing != effect.AbsoluteIndex.Value)
					{
						issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ConflictingPluginIntent,
							CollectionConflictImpactStatus.ActionRequired, item.Item1, path,
							"Selected members request different absolute load-order indices for the same plugin."));
					}
					else absoluteIndices[path] = effect.AbsoluteIndex.Value;
				}
				else if (effect.Kind == CollectionPlannedPluginEffectKind.RelativeOrder)
				{
					for (int index = 0; index + 1 < effect.PluginPaths.Count; index++)
					{
						string lower = effect.PluginPaths[index];
						string higher = effect.PluginPaths[index + 1];
						HashSet<string> outgoing;
						if (!relativeEdges.TryGetValue(lower, out outgoing))
						{
							outgoing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
							relativeEdges.Add(lower, outgoing);
						}
						outgoing.Add(higher);
						if (!relativeEdges.ContainsKey(higher))
							relativeEdges.Add(higher, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
					}
				}
			}

			if (HasPluginOrderCycle(relativeEdges))
			{
				issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ConflictingPluginIntent,
					CollectionConflictImpactStatus.ActionRequired, null, "plugin-relative-order",
					"Selected members request contradictory relative plugin ordering which forms a cycle."));
			}
		}

		private static bool HasPluginOrderCycle(IDictionary<string, HashSet<string>> edges)
		{
			var indegree = edges.Keys.ToDictionary(x => x, x => 0, StringComparer.OrdinalIgnoreCase);
			foreach (HashSet<string> outgoing in edges.Values)
				foreach (string target in outgoing)
					indegree[target]++;
			var queue = new SortedSet<string>(indegree.Where(x => x.Value == 0).Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
			int visited = 0;
			while (queue.Count > 0)
			{
				string current = queue.Min;
				queue.Remove(current);
				visited++;
				foreach (string next in edges[current])
					if (--indegree[next] == 0) queue.Add(next);
			}
			return visited != edges.Count;
		}


		private static List<CollectionConfigurationImpact> BuildConfigurationImpacts(CollectionNativeStateIndex nativeState, CollectionMemberMatchSet matches,
			IDictionary<CollectionMemberKey, CollectionMemberEffectPreview> previews,
			IDictionary<Guid, CollectionAssociationImpactKind> associationKinds, IList<CollectionConflictImpactIssue> issues)
		{
			var result = new List<CollectionConfigurationImpact>();
			var iniDesired = new Dictionary<CollectionNativeIniKey, string>();
			var gameDesired = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			foreach (CollectionMemberEffectPreview preview in previews.Values.Where(x => x.IsComplete).OrderBy(x => x.MemberKey, new MemberKeyComparer()))
			{
				foreach (CollectionPlannedIniEffect effect in preview.IniEdits)
				{
					string prior;
					if (iniDesired.TryGetValue(effect.Key, out prior) && !StringComparer.Ordinal.Equals(prior, effect.Value))
						issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ConfigurationWinnerDecisionRequired,
							CollectionConflictImpactStatus.ActionRequired, preview.MemberKey, effect.Key.ToString(),
							"Selected members request different values for the same INI setting."));
					else iniDesired[effect.Key] = effect.Value;

					CollectionNativeIniState current;
					nativeState.IniEdits.TryGetValue(effect.Key, out current);
					CollectionNativeTextOwnerValue currentValue = current == null || current.Values.Count == 0 ? null : current.Values[current.Values.Count - 1];
					HashSet<Guid> affected = AssociationIdsForOwner(nativeState, currentValue == null ? null : currentValue.OwnerKey);
					foreach (Guid associationId in affected) AddAssociationImpact(associationKinds, associationId, CollectionAssociationImpactKind.ConfigurationState);
					bool changesValue = currentValue == null || !StringComparer.Ordinal.Equals(currentValue.Value, effect.Value);
					if (changesValue && currentValue != null && !CurrentOwnerIsIncomingWriter(currentValue.OwnerKey, previews.Keys, matches.MembersByKey))
					{
						issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ExistingConfigurationDecisionRequired,
							CollectionConflictImpactStatus.ActionRequired, preview.MemberKey, effect.Key.ToString(),
							"The incoming recipe would change an INI value currently owned by the existing additive setup; explicit review is required."));
					}
					result.Add(new CollectionConfigurationImpact(preview.MemberKey, CollectionConfigurationImpactKind.Ini,
						effect.Key.ToString(), currentValue == null ? null : currentValue.OwnerKey, changesValue, affected));
				}

				foreach (CollectionPlannedGameValueEffect effect in preview.GameValues)
				{
					byte[] prior;
					if (gameDesired.TryGetValue(effect.Key, out prior) && !ByteArraysEqual(prior, effect.UnsafeValue))
						issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ConfigurationWinnerDecisionRequired,
							CollectionConflictImpactStatus.ActionRequired, preview.MemberKey, effect.Key,
							"Selected members request different values for the same game-specific setting."));
					else gameDesired[effect.Key] = effect.UnsafeValue;

					CollectionNativeGameValueState current;
					nativeState.GameValues.TryGetValue(effect.Key, out current);
					CollectionNativeBinaryOwnerValue currentValue = current == null || current.Values.Count == 0 ? null : current.Values[current.Values.Count - 1];
					HashSet<Guid> affected = AssociationIdsForOwner(nativeState, currentValue == null ? null : currentValue.OwnerKey);
					foreach (Guid associationId in affected) AddAssociationImpact(associationKinds, associationId, CollectionAssociationImpactKind.ConfigurationState);
					bool changesValue = currentValue == null || !ByteArraysEqual(currentValue.UnsafeValue, effect.UnsafeValue);
					if (changesValue && currentValue != null && !CurrentOwnerIsIncomingWriter(currentValue.OwnerKey, previews.Keys, matches.MembersByKey))
					{
						issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ExistingConfigurationDecisionRequired,
							CollectionConflictImpactStatus.ActionRequired, preview.MemberKey, effect.Key,
							"The incoming recipe would change a game-specific value currently owned by the existing additive setup; explicit review is required."));
					}
					result.Add(new CollectionConfigurationImpact(preview.MemberKey, CollectionConfigurationImpactKind.GameSpecificValue,
						effect.Key, currentValue == null ? null : currentValue.OwnerKey, changesValue, affected));
				}
			}
			return result;
		}

		private static void AddSharedNativeInstanceImpacts(CollectionMemberMatchSet matches, CollectionNativeStateIndex nativeState,
			IDictionary<Guid, CollectionAssociationImpactKind> associationKinds)
		{
			foreach (CollectionMemberMatchResult match in matches.Members)
			{
				if (match.Disposition != CollectionMemberMatchDisposition.ReinstallRequired || match.MatchedNativeMod == null) continue;
				ReadOnlyCollection<CollectionMemberBinding> bindings;
				if (!nativeState.BindingsByNativeMod.TryGetValue(match.MatchedNativeMod.Identity, out bindings)) continue;
				foreach (CollectionMemberBinding binding in bindings)
					AddAssociationImpact(associationKinds, binding.Association.AssociationId, CollectionAssociationImpactKind.SharedNativeInstance);
			}
		}

		private static void ReviewAffectedAssociations(CollectionNativeStateIndex nativeState,
			IDictionary<Guid, CollectionAssociationImpactKind> associationKinds, IList<CollectionConflictImpactIssue> issues)
		{
			foreach (Guid associationId in associationKinds.Keys.ToList())
			{
				CollectionTargetAssociation association;
				if (!nativeState.Associations.TryGetValue(associationId, out association)) continue;
				if (association.State == CollectionAssociationState.Incomplete || association.State == CollectionAssociationState.Recovering)
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.AffectedAssociationRequiresRecovery,
						CollectionConflictImpactStatus.Blocked, null, associationId.ToString("D"),
						"An existing Collection association affected by this plan is incomplete/recovering and must be reconciled first."));
				else if (association.State == CollectionAssociationState.Modified)
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.AffectedAssociationModified,
						CollectionConflictImpactStatus.ActionRequired, null, associationId.ToString("D"),
						"An existing Collection association affected by this plan is modified and requires explicit impact review."));

				ReadOnlyCollection<UserOverride> overrides;
				if (!nativeState.OverridesByAssociation.TryGetValue(associationId, out overrides)) continue;
				if (overrides.Any(IsConflictRelevantOverride))
				{
					associationKinds[associationId] |= CollectionAssociationImpactKind.UserOverride;
					issues.Add(new CollectionConflictImpactIssue(CollectionConflictImpactIssueKind.ExistingUserOverride,
						CollectionConflictImpactStatus.ActionRequired, null, associationId.ToString("D"),
						"An affected Collection association contains an explicit user override; C6.4 will not silently replace that local decision."));
				}
			}
		}

		private static bool IsConflictRelevantOverride(UserOverride value)
		{
			switch (value.Requirement.Aspect)
			{
				case CollectionRequirementAspect.MemberEnabledState:
				case CollectionRequirementAspect.ArtifactSelection:
				case CollectionRequirementAspect.InstallerRecipe:
				case CollectionRequirementAspect.FileWinner:
				case CollectionRequirementAspect.PluginState:
				case CollectionRequirementAspect.ConfigurationState:
					return true;
				default:
					return false;
			}
		}

		private static bool PreviewWritesPlugin(CollectionMemberEffectPreview preview, string pluginPath)
		{
			string normalized = (pluginPath ?? String.Empty).Replace('/', '\\');
			string fileName = Path.GetFileName(normalized);
			return preview.Files.Any(x => x.Target.Root == ModDeploymentRoot.Data &&
				(StringComparer.OrdinalIgnoreCase.Equals(x.Target.RelativePath, normalized) ||
				(!String.IsNullOrWhiteSpace(fileName) &&
					StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(x.Target.RelativePath), fileName))));
		}

		private static bool PluginEffectChangesExistingState(CollectionPlannedPluginEffect effect, CollectionNativePluginState current)
		{
			if (effect.Kind == CollectionPlannedPluginEffectKind.Activation)
				return current.Active != effect.Active.Value;
			return effect.Kind == CollectionPlannedPluginEffectKind.AbsoluteOrderIndex ||
				effect.Kind == CollectionPlannedPluginEffectKind.RelativeOrder;
		}

		private static HashSet<Guid> AssociationIdsForPlugin(CollectionNativeStateIndex state, string pluginPath)
		{
			CollectionNativeFileState file = FindFileForPlugin(state, pluginPath);
			return AssociationIdsForOwner(state, file == null ? null : file.EffectiveOwnerKey);
		}

		private static CollectionNativeFileState FindFileForPlugin(CollectionNativeStateIndex state, string pluginPath)
		{
			string normalized = (pluginPath ?? String.Empty).Replace('/', '\\');
			List<CollectionNativeFileState> exact = state.Files.Values.Where(x =>
				StringComparer.OrdinalIgnoreCase.Equals(x.Target.RelativePath, normalized)).ToList();
			if (exact.Count == 1) return exact[0];
			string name = Path.GetFileName(normalized);
			List<CollectionNativeFileState> byName = state.Files.Values.Where(x =>
				StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(x.Target.RelativePath), name)).ToList();
			return byName.Count == 1 ? byName[0] : null;
		}

		private static CollectionNativePluginState FindPlugin(CollectionNativeStateIndex state, string pluginPath)
		{
			CollectionNativePluginState value;
			if (state.Plugins.TryGetValue(pluginPath, out value)) return value;
			string name = Path.GetFileName(pluginPath ?? String.Empty);
			if (state.Plugins.TryGetValue(name, out value)) return value;
			return null;
		}

		private static HashSet<Guid> AssociationIdsForOwner(CollectionNativeStateIndex state, string ownerKey)
		{
			var result = new HashSet<Guid>();
			if (String.IsNullOrWhiteSpace(ownerKey)) return result;
			CollectionNativeModState mod;
			if (!state.ModsByNativeKey.TryGetValue(ownerKey, out mod)) return result;
			ReadOnlyCollection<CollectionMemberBinding> bindings;
			if (!state.BindingsByNativeMod.TryGetValue(mod.Identity, out bindings)) return result;
			foreach (CollectionMemberBinding binding in bindings) result.Add(binding.Association.AssociationId);
			return result;
		}

		private static void AddAssociationImpact(IDictionary<Guid, CollectionAssociationImpactKind> impacts,
			Guid associationId, CollectionAssociationImpactKind kind)
		{
			CollectionAssociationImpactKind existing;
			impacts.TryGetValue(associationId, out existing);
			impacts[associationId] = existing | kind;
		}

		private static bool ByteArraysEqual(byte[] left, byte[] right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Length != right.Length) return false;
			for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
			return true;
		}

		private static int CompareIssues(CollectionConflictImpactIssue left, CollectionConflictImpactIssue right)
		{
			int status = ((int)right.Status).CompareTo((int)left.Status);
			if (status != 0) return status;
			int kind = left.Kind.CompareTo(right.Kind);
			if (kind != 0) return kind;
			int member = CompareNullableMemberKeys(left.MemberKey, right.MemberKey);
			if (member != 0) return member;
			return StringComparer.Ordinal.Compare(left.SubjectKey, right.SubjectKey);
		}

		private static int CompareNullableMemberKeys(CollectionMemberKey left, CollectionMemberKey right)
		{
			if (ReferenceEquals(left, right)) return 0;
			if (left == null) return -1;
			if (right == null) return 1;
			return CompareMemberKeys(left, right);
		}

		private static int CompareMemberKeys(CollectionMemberKey left, CollectionMemberKey right)
		{
			int kind = left.Kind.CompareTo(right.Kind);
			return kind != 0 ? kind : StringComparer.Ordinal.Compare(left.Value, right.Value);
		}

		private sealed class MemberKeyComparer : IComparer<CollectionMemberKey>
		{
			public int Compare(CollectionMemberKey x, CollectionMemberKey y) { return CompareMemberKeys(x, y); }
		}
	}
}
