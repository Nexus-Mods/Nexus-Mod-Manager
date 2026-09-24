using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Prepares one additive Collection native child by durably recording intent and retaining required recovery inputs.</summary>
	/// <remarks>C6.6 never submits native work. Native deployment recovery remains authoritative for deployed-file ownership.</remarks>
	public sealed class CollectionNativeChildPreparationCoordinator
	{
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
		private readonly Action<CollectionOperation, CollectionNativeChildOperation> _afterIntentPersisted;

		/// <summary>Creates a durable child-preparation coordinator over existing C4-C6 stores.</summary>
		public CollectionNativeChildPreparationCoordinator(CollectionsOperationStore operationStore,
			CollectionsResolvedPlanStore planStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, CollectionsNativeChildRecoveryManifestStore manifestStore)
			: this(operationStore, planStore, artifactStore, referenceStore, manifestStore, null)
		{
		}

		/// <summary>Creates a coordinator with one deterministic post-intent boundary callback for focused failure-injection tests.</summary>
		internal CollectionNativeChildPreparationCoordinator(CollectionsOperationStore operationStore,
			CollectionsResolvedPlanStore planStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, CollectionsNativeChildRecoveryManifestStore manifestStore,
			Action<CollectionOperation, CollectionNativeChildOperation> afterIntentPersisted)
		{
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			_manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
			_afterIntentPersisted = afterIntentPersisted;
		}

		/// <summary>Prepares the next mutating member in deterministic C6.3 order, or returns <c>null</c> when no native child remains.</summary>
		/// <param name="installInfoDirectory">The current game mode InstallInfo directory used to locate live scripted replay data.</param>
		public CollectionNativeChildPreparationResult PrepareNext(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, CollectionMemberMatchSet matches, CollectionDependencyPhasePlan dependencyPlan,
			CollectionConflictImpactPlan impactPlan, CollectionNativeStateIndex currentState,
			IEnumerable<CollectionMemberEffectPreview> effectPreviews,
			IEnumerable<CollectionVerifiedArchive> verifiedArchives, string installInfoDirectory)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (matches == null) throw new ArgumentNullException(nameof(matches));
			if (dependencyPlan == null) throw new ArgumentNullException(nameof(dependencyPlan));
			if (impactPlan == null) throw new ArgumentNullException(nameof(impactPlan));
			if (currentState == null) throw new ArgumentNullException(nameof(currentState));
			if (effectPreviews == null) throw new ArgumentNullException(nameof(effectPreviews));
			if (verifiedArchives == null) throw new ArgumentNullException(nameof(verifiedArchives));
			if (String.IsNullOrWhiteSpace(installInfoDirectory)) throw new ArgumentException("The native InstallInfo directory is required for child recovery preparation.", nameof(installInfoDirectory));

			CollectionOperation operation = RequireOperation(operationIdentity);
			ValidatePlanningInputs(operation, plan, matches, dependencyPlan, impactPlan, currentState);
			Dictionary<CollectionMemberKey, CollectionMemberEffectPreview> previews = IndexPreviews(plan, effectPreviews);
			Dictionary<CollectionMemberKey, CollectionVerifiedArchive> archives = IndexVerifiedArchives(plan, matches, verifiedArchives);

			CollectionNativeChildOperation existingPending = GetSinglePendingChild(operation);
			CollectionMemberMatchResult match = existingPending == null
				? FindNextActionableMatch(dependencyPlan, operation)
				: GetMatch(matches, existingPending.Member.MemberKey);
			if (match == null) return null;
			if (match.Disposition != CollectionMemberMatchDisposition.ArchiveOnlyReuse && match.Disposition != CollectionMemberMatchDisposition.ReinstallRequired)
				throw new InvalidOperationException("Only archive-only activation or reviewed reinstall members create C6.6 native child intent.");

			CollectionMemberEffectPreview preview;
			if (!previews.TryGetValue(match.Member.MemberKey, out preview) || !preview.IsComplete)
				throw new InvalidOperationException("The next mutating Collection member does not have a complete reviewed native-effect preview.");
			ValidatePreview(match, preview);

			CollectionVerifiedArchive archive;
			if (!archives.TryGetValue(match.Member.MemberKey, out archive))
				throw new InvalidOperationException("A mutating Collection child requires exact verified immutable archive bytes before native submission.");
			ValidateVerifiedArchive(plan, match.Member, archive);

			CollectionNativeModState previousNativeMod = null;
			if (match.Disposition == CollectionMemberMatchDisposition.ReinstallRequired)
			{
				previousNativeMod = match.MatchedNativeMod;
				if (previousNativeMod == null) throw new InvalidOperationException("A reinstall-required member must identify exactly one native instance.");
				CollectionNativeModState currentNativeMod;
				if (!currentState.Mods.TryGetValue(previousNativeMod.Identity, out currentNativeMod) || !SameNativeMod(previousNativeMod, currentNativeMod))
					throw new InvalidOperationException("The native instance selected for reinstall changed before durable child preparation.");
				previousNativeMod = currentNativeMod;
				if (previousNativeMod.InstallMethod != preview.InstallMethod || previousNativeMod.InstallRoot != preview.InstallRoot)
					throw new InvalidOperationException("C6 additive reinstall preparation cannot silently convert the installed member's native method or install root.");
				RequireReinstallEffectsReviewed(previousNativeMod, preview, currentState);
			}

			CollectionNativeChildOperation child = existingPending;
			if (child == null)
			{
				int sequence = NextSequence(operation);
				var nativeOperation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
					new ModOperationFingerprint(plan.Target.Fingerprint,
						new ModInstallContext(preview.InstallMethod, preview.InstallRoot), match.Member.RecipeIdentity.Fingerprint));
				child = new CollectionNativeChildOperation(sequence,
					new CollectionOperationMemberReference(plan.Revision, match.Member.MemberKey),
					CollectionNativeChildAction.ActivateOrReinstall, nativeOperation, CollectionNativeChildCheckpoint.IntentPersisted, null);
				operation = SaveChild(operation, child);
				_afterIntentPersisted?.Invoke(operation, child);
			}
			else
			{
				ValidateExistingChild(child, plan, match, preview);
				if (child.Checkpoint == CollectionNativeChildCheckpoint.RecoveryInputsReady)
				{
					CollectionNativeChildRecoveryManifest readyManifest = _manifestStore.GetManifest(operation, child);
					if (readyManifest == null) throw new InvalidDataException("A RecoveryInputsReady Collection child is missing its retained recovery manifest.");
					if (!readyManifest.PreparationStateFingerprint.Equals(currentState.Fingerprint) ||
						!readyManifest.PreparationStateFingerprint.Equals(ResolveExpectedPreparationStateFingerprint(operation, plan)))
						throw new InvalidOperationException("The native-state observation changed after this child was prepared; revalidation is required before submission.");
					return new CollectionNativeChildPreparationResult(operation, child, readyManifest);
				}
				if (child.HasCrossedNativeBoundary) throw new InvalidOperationException("C6.6 cannot re-prepare a Collection child after native submission.");
			}

			// Durable intent precedes retained recovery/content roles. No native mutation has occurred at this point.
			CollectionRecoveryArtifact incomingArchive = RetainIncomingArchive(operation, child, archive);
			CollectionRecoveryArtifact previousArchive = null;
			CollectionScriptedReplayRecoverySnapshot replay = new CollectionScriptedReplayRecoverySnapshot(false, null, false, new CollectionReplayRecoveryPayload[0]);
			if (previousNativeMod != null)
			{
				previousArchive = RetainPreviousArchive(operation, child, previousNativeMod);
				replay = RetainScriptedReplay(operation, child, previousNativeMod, installInfoDirectory);
			}

			var manifest = new CollectionNativeChildRecoveryManifest(operation.Identity, child.Sequence, plan.Identity,
				child.Member, child.Action, child.NativeOperation, currentState.Fingerprint, incomingArchive,
				previousNativeMod, previousArchive, replay);
			_manifestStore.SaveManifest(manifest);

			CollectionNativeChildOperation prepared = new CollectionNativeChildOperation(child.Sequence, child.Member,
				child.Action, child.NativeOperation, CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
			operation = SaveChild(operation, prepared);
			CollectionNativeChildRecoveryManifest persisted = _manifestStore.GetManifest(operation, prepared);
			if (persisted == null) throw new InvalidDataException("The Collection child recovery manifest disappeared before the preparation checkpoint was committed.");
			return new CollectionNativeChildPreparationResult(operation, prepared, persisted);
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null) throw new InvalidOperationException("The Collection operation is not present in the durable operation journal.");
			if (operation.Kind != CollectionOperationKind.ApplyResolvedPlan || operation.Phase != CollectionOperationPhase.ApplyingNativeChildren ||
				operation.ResultState != CollectionOperationResultState.Pending)
				throw new InvalidOperationException("C6.6 child preparation requires an active additive operation in ApplyingNativeChildren.");
			return operation;
		}

		private void ValidatePlanningInputs(CollectionOperation operation, ResolvedCollectionPlan plan,
			CollectionMemberMatchSet matches, CollectionDependencyPhasePlan dependencyPlan,
			CollectionConflictImpactPlan impactPlan, CollectionNativeStateIndex currentState)
		{
			if (operation.PlanIdentity == null || !operation.PlanIdentity.Equals(plan.Identity) || operation.Revision == null ||
				!operation.Revision.Equals(plan.Revision) || !operation.Target.Equals(plan.Target))
				throw new ArgumentException("Child preparation inputs must belong to the exact approved Collection operation plan.", nameof(plan));
			if (plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new ArgumentException("C6.6 only prepares additive Collection operations.", nameof(plan));
			if (!matches.PlanIdentity.Equals(plan.Identity) || !matches.Target.Equals(plan.Target) ||
				!dependencyPlan.PlanIdentity.Equals(plan.Identity) || !dependencyPlan.Target.Equals(plan.Target) ||
				!impactPlan.PlanIdentity.Equals(plan.Identity) || !impactPlan.Target.Equals(plan.Target))
				throw new ArgumentException("C6.2-C6.4 preparation inputs do not belong to the exact approved Collection plan.");
			if (!dependencyPlan.IsReady || !impactPlan.IsReady || matches.HasBlockedMembers || matches.HasAcquisitionRequired)
				throw new InvalidOperationException("A Collection operation cannot prepare native children until C6.2-C6.4 are fully ready.");
			if (!currentState.Target.Equals(plan.Target))
				throw new ArgumentException("The preparation native-state observation belongs to a different target.", nameof(currentState));
			if (!matches.StateFingerprint.Equals(plan.CurrentStateFingerprint) ||
				!dependencyPlan.StateFingerprint.Equals(plan.CurrentStateFingerprint) ||
				!impactPlan.StateFingerprint.Equals(plan.CurrentStateFingerprint))
				throw new InvalidOperationException("The C6.2-C6.4 inputs no longer represent the exact originally reviewed Collection state.");

			CollectionCurrentStateFingerprint expectedPreparationState = ResolveExpectedPreparationStateFingerprint(operation, plan);
			if (!currentState.Fingerprint.Equals(expectedPreparationState))
				throw new InvalidOperationException("Authoritative native state differs from the latest verified Collection safe boundary; revalidation is required before preparing another child.");

			CollectionResolvedPlanRecord persisted = _planStore.GetPlan(plan.Identity);
			if (persisted == null || !persisted.Revision.Equals(plan.Revision) || !persisted.Target.Equals(plan.Target) ||
				persisted.PolicyKind != plan.Policy.Kind || !persisted.CurrentStateFingerprint.Equals(plan.CurrentStateFingerprint))
				throw new InvalidOperationException("The exact approved Collection plan is not durably persisted for child preparation.");
		}

		private CollectionCurrentStateFingerprint ResolveExpectedPreparationStateFingerprint(CollectionOperation operation, ResolvedCollectionPlan plan)
		{
			CollectionCurrentStateFingerprint expected = plan.CurrentStateFingerprint;
			foreach (CollectionNativeChildOperation previous in operation.NativeChildren.OrderBy(x => x.Sequence))
			{
				if (!previous.IsReconciled)
					continue;
				if (previous.NativeResult == null || previous.NativeResult.Durability != ModOperationDurability.VerifiedCommitted)
					throw new InvalidOperationException("A non-committed reconciled Collection child prevents later reviewed children from continuing without re-preparation.");
				CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, previous);
				if (manifest == null || manifest.SafeBoundaryStateFingerprint == null)
					throw new InvalidDataException("A verified committed Collection child is missing its durable C6.10 safe-boundary fingerprint.");
				expected = manifest.SafeBoundaryStateFingerprint;
			}
			return expected;
		}

		private static Dictionary<CollectionMemberKey, CollectionMemberEffectPreview> IndexPreviews(ResolvedCollectionPlan plan,
			IEnumerable<CollectionMemberEffectPreview> effectPreviews)
		{
			var selected = new HashSet<CollectionMemberKey>(plan.SelectedMembers.Select(x => x.MemberKey));
			var result = new Dictionary<CollectionMemberKey, CollectionMemberEffectPreview>();
			foreach (CollectionMemberEffectPreview preview in effectPreviews)
			{
				if (preview == null || !selected.Contains(preview.MemberKey)) throw new ArgumentException("Effect previews must belong to selected members of the exact Collection plan.", nameof(effectPreviews));
				if (result.ContainsKey(preview.MemberKey)) throw new ArgumentException("Only one effect preview may be supplied for each selected member.", nameof(effectPreviews));
				result.Add(preview.MemberKey, preview);
			}
			return result;
		}

		private static Dictionary<CollectionMemberKey, CollectionVerifiedArchive> IndexVerifiedArchives(ResolvedCollectionPlan plan,
			CollectionMemberMatchSet matches, IEnumerable<CollectionVerifiedArchive> supplied)
		{
			var result = new Dictionary<CollectionMemberKey, CollectionVerifiedArchive>();
			foreach (CollectionMemberMatchResult match in matches.Members) if (match.VerifiedArchive != null) result[match.Member.MemberKey] = match.VerifiedArchive;
			foreach (CollectionVerifiedArchive archive in supplied)
			{
				if (archive == null) throw new ArgumentException("Verified archive inputs cannot contain null entries.", nameof(supplied));
				ResolvedCollectionMemberPlan member = plan.SelectedMembers.SingleOrDefault(x => x.MemberKey.Equals(archive.Request.MemberKey));
				if (member == null) throw new ArgumentException("A verified archive input does not belong to the selected Collection plan.", nameof(supplied));
				ValidateVerifiedArchive(plan, member, archive);
				CollectionVerifiedArchive existing;
				if (result.TryGetValue(member.MemberKey, out existing) && !StringComparer.Ordinal.Equals(existing.Artifact.ArtifactId, archive.Artifact.ArtifactId))
					throw new ArgumentException("Conflicting verified archives were supplied for one Collection member.", nameof(supplied));
				result[member.MemberKey] = archive;
			}
			return result;
		}

		private static CollectionNativeChildOperation GetSinglePendingChild(CollectionOperation operation)
		{
			List<CollectionNativeChildOperation> pending = operation.NativeChildren.Where(x => !x.IsReconciled).ToList();
			if (pending.Count > 1) throw new InvalidOperationException("C6.6 requires a single serialized native-child preparation/submission lane.");
			return pending.Count == 0 ? null : pending[0];
		}

		private static CollectionMemberMatchResult GetMatch(CollectionMemberMatchSet matches, CollectionMemberKey key)
		{
			CollectionMemberMatchResult result;
			if (!matches.MembersByKey.TryGetValue(key, out result)) throw new InvalidOperationException("A durable child refers to a member absent from the exact C6.2 match set.");
			return result;
		}

		private static CollectionMemberMatchResult FindNextActionableMatch(CollectionDependencyPhasePlan dependencyPlan, CollectionOperation operation)
		{
			if (operation.NativeChildren.Any(x => x.IsReconciled && !x.HasVerifiedCommittedNativeState))
				throw new InvalidOperationException("C6.6 will not advance past a reconciled native child that did not verify as committed; recovery/retry policy must decide the next action explicitly.");
			var completed = new HashSet<CollectionMemberKey>(operation.NativeChildren.Where(x => x.IsReconciled && x.HasVerifiedCommittedNativeState).Select(x => x.Member.MemberKey));
			foreach (CollectionExecutionPhase phase in dependencyPlan.Phases)
				foreach (CollectionPlannedPhaseMember planned in phase.Members)
				{
					if (completed.Contains(planned.MemberKey) || planned.MatchDisposition == CollectionMemberMatchDisposition.InstalledCompatible) continue;
					return planned.Match;
				}
			return null;
		}

		private static void ValidatePreview(CollectionMemberMatchResult match, CollectionMemberEffectPreview preview)
		{
			if (!preview.MemberKey.Equals(match.Member.MemberKey) || !preview.RecipeIdentity.Equals(match.Member.RecipeIdentity))
				throw new ArgumentException("The native-effect preview does not describe the exact matched Collection member.", nameof(preview));
		}

		private static void ValidateVerifiedArchive(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member, CollectionVerifiedArchive archive)
		{
			CollectionAcquisitionRequest request = archive.Request;
			if (!request.PlanIdentity.Equals(plan.Identity) || !request.Revision.Equals(plan.Revision) || !request.Target.Equals(plan.Target) ||
				!request.MemberKey.Equals(member.MemberKey) || !request.SelectedArtifact.Equals(member.ArtifactChoice.SelectedArtifact) ||
				!request.RecipeIdentity.Equals(member.RecipeIdentity))
				throw new ArgumentException("The verified archive does not belong to the exact approved Collection member plan.", nameof(archive));
		}

		private CollectionRecoveryArtifact RetainIncomingArchive(CollectionOperation operation, CollectionNativeChildOperation child, CollectionVerifiedArchive archive)
		{
			CollectionsRetainedArtifact persisted = _artifactStore.GetArtifact(archive.Artifact.ArtifactId);
			if (persisted == null || !persisted.Equals(archive.Artifact) || !_artifactStore.VerifyArtifact(persisted.ArtifactId))
				throw new InvalidDataException("The verified incoming Collection archive is missing or failed retained-content integrity verification.");
			_referenceStore.AcquireExclusiveRoleReference(persisted.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				operation.Identity.ToString(), CollectionsNativeChildRecoveryManifestStore.GetIncomingArchiveRole(child.Sequence));
			return CollectionRecoveryArtifact.FromRetainedArtifact(persisted);
		}

		private CollectionRecoveryArtifact RetainPreviousArchive(CollectionOperation operation, CollectionNativeChildOperation child, CollectionNativeModState previousNativeMod)
		{
			if (String.IsNullOrWhiteSpace(previousNativeMod.ArchivePath) || !File.Exists(previousNativeMod.ArchivePath))
				throw new FileNotFoundException("The previous native archive required for reinstall recovery is unavailable.", previousNativeMod.ArchivePath);
			CollectionsRetainedArtifact retained = _artifactStore.PublishFile(previousNativeMod.ArchivePath);
			_referenceStore.AcquireExclusiveRoleReference(retained.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				operation.Identity.ToString(), CollectionsNativeChildRecoveryManifestStore.GetPreviousArchiveRole(child.Sequence));
			return CollectionRecoveryArtifact.FromRetainedArtifact(retained);
		}

		private CollectionScriptedReplayRecoverySnapshot RetainScriptedReplay(CollectionOperation operation,
			CollectionNativeChildOperation child, CollectionNativeModState previousNativeMod, string installInfoDirectory)
		{
			string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(previousNativeMod.FileName, installInfoDirectory);
			bool replayExists = File.Exists(replayPath);
			if (previousNativeMod.HasInstallScript)
			{
				var liveReplay = new ScriptedFileSelectionCache(replayPath);
				if (!liveReplay.HasCompleteReplay)
					throw new InvalidOperationException("A scripted previous installation requires a complete native replay before Collection reinstall can be prepared safely.");
				// This also validates any generated replay payload lengths and hashes before they are retained.
				liveReplay.LoadReplayOperations();
			}
			CollectionRecoveryArtifact replayArtifact = null;
			if (replayExists)
			{
				CollectionsRetainedArtifact retained = _artifactStore.PublishFile(replayPath);
				_referenceStore.AcquireExclusiveRoleReference(retained.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
					operation.Identity.ToString(), CollectionsNativeChildRecoveryManifestStore.GetReplayXmlRole(child.Sequence));
				replayArtifact = CollectionRecoveryArtifact.FromRetainedArtifact(retained);
			}
			else if (_referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation,
				operation.Identity.ToString(), CollectionsNativeChildRecoveryManifestStore.GetReplayXmlRole(child.Sequence)) != null)
				throw new InvalidOperationException("The live scripted replay changed after partial Collection child preparation.");

			string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
			bool payloadDirectoryExists = Directory.Exists(payloadDirectory);
			var payloads = new List<CollectionReplayRecoveryPayload>();
			var currentPayloadRoles = new HashSet<string>(StringComparer.Ordinal);
			if (payloadDirectoryExists)
			{
				string root = Path.GetFullPath(payloadDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
				foreach (string file in Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories)
					.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal))
				{
					string full = Path.GetFullPath(file);
					if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A scripted replay payload escaped its expected sidecar directory.");
					string relative = full.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
					var payload = new CollectionReplayRecoveryPayload(relative, CollectionRecoveryArtifact.FromRetainedArtifact(_artifactStore.PublishFile(full)));
					string role = CollectionsNativeChildRecoveryManifestStore.GetReplayPayloadRole(child.Sequence, payload.RelativePath);
					currentPayloadRoles.Add(role);
					_referenceStore.AcquireExclusiveRoleReference(payload.Artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
						operation.Identity.ToString(), role);
					payloads.Add(payload);
				}
			}

			string payloadPrefix = String.Format("child-{0:D8}-replay-payload-", child.Sequence);
			foreach (CollectionsRetainedArtifactReferenceRecord reference in _referenceStore.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Operation, operation.Identity.ToString()))
				if (reference.Role.StartsWith(payloadPrefix, StringComparison.Ordinal) && !currentPayloadRoles.Contains(reference.Role))
					throw new InvalidOperationException("The live scripted replay payload set changed after partial Collection child preparation.");
			return new CollectionScriptedReplayRecoverySnapshot(replayExists, replayArtifact, payloadDirectoryExists, payloads);
		}

		private static void RequireReinstallEffectsReviewed(CollectionNativeModState previousNativeMod, CollectionMemberEffectPreview preview, CollectionNativeStateIndex currentState)
		{
			string ownerKey = previousNativeMod.Identity.NativeModKey;
			ReadOnlyCollection<CollectionNativeFileState> oldFiles;
			if (currentState.FilesByOwnerKey.TryGetValue(ownerKey, out oldFiles))
			{
				var planned = new HashSet<ModDeploymentTarget>(preview.Files.Select(x => x.Target));
				if (oldFiles.Any(x => !planned.Contains(x.Target))) throw new InvalidOperationException("The reviewed reinstall preview does not cover every existing file effect that native upgrade may remove or replace.");
			}
			ReadOnlyCollection<CollectionNativeIniState> oldIni;
			if (currentState.IniEditsByOwnerKey.TryGetValue(ownerKey, out oldIni))
			{
				var planned = new HashSet<CollectionNativeIniKey>(preview.IniEdits.Select(x => x.Key));
				if (oldIni.Any(x => !planned.Contains(x.Key))) throw new InvalidOperationException("The reviewed reinstall preview does not cover every existing INI effect that native upgrade may remove or replace.");
			}
			ReadOnlyCollection<CollectionNativeGameValueState> oldValues;
			if (currentState.GameValuesByOwnerKey.TryGetValue(ownerKey, out oldValues))
			{
				var planned = new HashSet<string>(preview.GameValues.Select(x => x.Key), StringComparer.Ordinal);
				if (oldValues.Any(x => !planned.Contains(x.Key))) throw new InvalidOperationException("The reviewed reinstall preview does not cover every existing game-specific effect that native upgrade may remove or replace.");
			}
		}

		private static bool SameNativeMod(CollectionNativeModState expected, CollectionNativeModState actual)
		{
			return expected.Identity.Equals(actual.Identity) && StringComparer.OrdinalIgnoreCase.Equals(expected.ArchivePath, actual.ArchivePath) &&
				StringComparer.OrdinalIgnoreCase.Equals(expected.FileName, actual.FileName) && StringComparer.Ordinal.Equals(expected.NexusModId, actual.NexusModId) &&
				StringComparer.Ordinal.Equals(expected.NexusFileId, actual.NexusFileId) && StringComparer.Ordinal.Equals(expected.HumanReadableVersion, actual.HumanReadableVersion) &&
				StringComparer.Ordinal.Equals(expected.MachineVersion, actual.MachineVersion) && expected.HasInstallScript == actual.HasInstallScript &&
				expected.InstallRoot == actual.InstallRoot && expected.InstallMethod == actual.InstallMethod;
		}

		private static void ValidateExistingChild(CollectionNativeChildOperation child, ResolvedCollectionPlan plan,
			CollectionMemberMatchResult match, CollectionMemberEffectPreview preview)
		{
			var expectedFingerprint = new ModOperationFingerprint(plan.Target.Fingerprint,
				new ModInstallContext(preview.InstallMethod, preview.InstallRoot), match.Member.RecipeIdentity.Fingerprint);
			if (!child.Member.Revision.Equals(plan.Revision) || !child.Member.MemberKey.Equals(match.Member.MemberKey) ||
				child.Action != CollectionNativeChildAction.ActivateOrReinstall || child.NativeOperation.Origin != ModOperationOrigin.Collection ||
				!child.NativeOperation.Fingerprint.Equals(expectedFingerprint))
				throw new InvalidOperationException("The existing durable native-child intent does not match the next approved Collection member action.");
		}

		private CollectionOperation SaveChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			if (operation.CheckpointSequence == Int64.MaxValue) throw new InvalidOperationException("The Collection operation checkpoint sequence cannot be incremented further.");
			List<CollectionNativeChildOperation> children = operation.NativeChildren.Where(x => x.Sequence != child.Sequence).ToList();
			children.Add(child);
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1, operation.Phase, operation.ResultState, children);
			_operationStore.SaveOperation(updated);
			return _operationStore.GetOperation(operation.Identity);
		}

		private static int NextSequence(CollectionOperation operation)
		{
			int maximum = operation.NativeChildren.Count == 0 ? 0 : operation.NativeChildren.Max(x => x.Sequence);
			if (maximum == Int32.MaxValue) throw new InvalidOperationException("The Collection operation native-child sequence cannot be incremented further.");
			return maximum + 1;
		}

	}
}
