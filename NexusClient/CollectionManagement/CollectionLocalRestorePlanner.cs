using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// C7.9 read-only planner that maps one sealed Local Collection capture onto the current native NMM state.
	/// </summary>
	/// <remarks>
	/// Planning revalidates retained bytes and current archive identity, but performs no native writes. Captured native owner keys
	/// are never assumed to survive restoration: existing registrations are mapped explicitly and recreated members remain symbolic
	/// until C7.10 receives the new native key from the native install boundary.
	/// </remarks>
	public sealed class CollectionLocalRestorePlanner
	{
		private readonly CollectionsRetainedArtifactStore _artifactStore;

		public CollectionLocalRestorePlanner(CollectionsRetainedArtifactStore artifactStore)
		{
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
		}

		/// <summary>Builds one deterministic restore plan against an already captured current native-state boundary.</summary>
		public CollectionLocalRestorePlan Plan(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionTargetIdentity currentTarget, CollectionCurrentStateFingerprint currentStateFingerprint,
			NativeStateCaptureSnapshot currentNativeState)
		{
			return Plan(sealedCapture, currentTarget, currentStateFingerprint, currentNativeState, CancellationToken.None);
		}

		/// <summary>Builds one deterministic restore plan with cooperative cancellation for hashing/retained-byte verification.</summary>
		public CollectionLocalRestorePlan Plan(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionTargetIdentity currentTarget, CollectionCurrentStateFingerprint currentStateFingerprint,
			NativeStateCaptureSnapshot currentNativeState, CancellationToken cancellationToken)
		{
			if (sealedCapture == null)
				throw new ArgumentNullException(nameof(sealedCapture));
			if (currentTarget == null)
				throw new ArgumentNullException(nameof(currentTarget));
			if (currentStateFingerprint == null)
				throw new ArgumentNullException(nameof(currentStateFingerprint));
			if (currentNativeState == null)
				throw new ArgumentNullException(nameof(currentNativeState));
			cancellationToken.ThrowIfCancellationRequested();

			LocalCapture capture = sealedCapture.Capture;
			var issues = new List<CollectionLocalRestorePlanIssue>();
			ValidateCaptureEnvelope(sealedCapture, currentTarget, issues);
			ValidateSnapshotRetainedReferences(sealedCapture, issues);
			ValidateRetainedArtifacts(capture, issues, cancellationToken);

			List<InstallLogReadMod> currentMods = currentNativeState.InstallLog.Mods.Where(x => !x.Hidden)
				.OrderBy(x => x.ModKey, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ModKey, StringComparer.Ordinal).ToList();
			if (currentMods.GroupBy(x => x.ModKey, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1))
			{
				issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.CurrentNativeStateAmbiguous,
					"native-mods", "Current committed native state contains duplicate active mod keys and cannot be remapped deterministically."));
			}

			Dictionary<string, CollectionCapturedArchiveArtifact> archivesByNativeKey = BuildArchiveIndex(sealedCapture, issues);
			Dictionary<string, LocalCaptureNativeRecordMapping> mappingsByNativeKey = BuildMappingIndex(capture, issues);
			var usedCurrentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var fileIdentityCache = new Dictionary<string, FileIdentity>(StringComparer.OrdinalIgnoreCase);
			var members = new List<CollectionLocalRestoreMemberPlan>();

			foreach (CollectionInstalledModIdentity capturedMod in sealedCapture.InstalledIdentities.Mods
				.OrderBy(x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.NativeSnapshotKey, StringComparer.Ordinal))
			{
				cancellationToken.ThrowIfCancellationRequested();
				LocalCaptureNativeRecordMapping mapping;
				if (!mappingsByNativeKey.TryGetValue(capturedMod.NativeSnapshotKey, out mapping))
				{
					issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.NativeMappingMissing,
						capturedMod.NativeSnapshotKey, "The sealed capture has no stable Local Collection member mapping for this captured native registration."));
					continue;
				}

				CollectionCapturedArchiveArtifact retainedArchive;
				archivesByNativeKey.TryGetValue(capturedMod.NativeSnapshotKey, out retainedArchive);
				if (capture.Scope.Contains(LocalCaptureScopeArea.ModArchives) && retainedArchive == null)
				{
					issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.RetainedArchiveMissing,
						capturedMod.NativeSnapshotKey, "The captured member has no retained archive available for deterministic local recreation."));
				}

				InstallLogReadMod reusable = FindReusableNative(capturedMod, retainedArchive, currentMods,
					usedCurrentKeys, fileIdentityCache, issues, cancellationToken);
				CollectionLocalRestoreMemberAction action;
				string currentNativeKey;
				if (reusable != null)
				{
					action = CollectionLocalRestoreMemberAction.ReuseExistingNative;
					currentNativeKey = reusable.ModKey;
					usedCurrentKeys.Add(reusable.ModKey);
				}
				else if (retainedArchive != null)
				{
					action = CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive;
					currentNativeKey = String.Empty;
				}
				else
				{
					action = CollectionLocalRestoreMemberAction.ManualReviewRequired;
					currentNativeKey = String.Empty;
				}

				members.Add(new CollectionLocalRestoreMemberPlan(mapping.SnapshotMemberKey, capturedMod.NativeSnapshotKey,
					action, currentNativeKey, capturedMod.InstallContext, retainedArchive));
			}

			Dictionary<string, CollectionLocalRestoreMemberPlan> memberByCapturedKey = BuildMemberPlanIndex(members, issues);
			ValidateSnapshotNativeReferences(sealedCapture, currentNativeState, memberByCapturedKey, issues);
			List<CollectionLocalRestoreDeploymentPlan> deploymentPlans = BuildDeploymentPlans(sealedCapture, currentNativeState,
				memberByCapturedKey, issues);

			List<string> removeKeys = currentMods.Select(x => x.ModKey)
				.Where(x => !usedCurrentKeys.Contains(x))
				.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal).ToList();

			members = members.OrderBy(x => x.SnapshotMemberKey.Kind).ThenBy(x => x.SnapshotMemberKey.Value, StringComparer.Ordinal).ToList();
			deploymentPlans = deploymentPlans.OrderBy(x => (int)x.Target.Root)
				.ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase)
				.ThenBy(x => x.Target.RelativePath, StringComparer.Ordinal).ToList();
			issues = issues.OrderBy(x => (int)x.Kind).ThenBy(x => x.ResourceKey, StringComparer.Ordinal).ThenBy(x => x.Message, StringComparer.Ordinal).ToList();

			string fingerprint = CreatePlanFingerprint(capture, currentTarget, currentStateFingerprint,
				currentNativeState.InstallLog.DeploymentCommitSequence, currentNativeState.InstallLog.OriginalValuesKey,
				members, deploymentPlans, removeKeys, issues);
			return new CollectionLocalRestorePlan(capture.Identity, currentTarget, currentStateFingerprint,
				currentNativeState.InstallLog.DeploymentCommitSequence, currentNativeState.InstallLog.OriginalValuesKey,
				fingerprint, members, deploymentPlans, removeKeys, issues);
		}

		private static void ValidateCaptureEnvelope(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionTargetIdentity currentTarget, List<CollectionLocalRestorePlanIssue> issues)
		{
			LocalCapture capture = sealedCapture.Capture;
			if (!capture.IsLocallyRestorableWithinScope)
				issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.CaptureNotLocallyRestorable,
					capture.Identity.ToString(), "Only a capture sealed as locally restorable within its declared scope can enter automatic restore planning."));
			if (capture.SchemaVersion != LocalCapture.CurrentSchemaVersion || capture.CapabilityVersion != LocalCapture.CurrentCapabilityVersion ||
				capture.Scope.Version != LocalCaptureScope.CurrentVersion)
				issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.UnsupportedCaptureVersion,
					capture.Identity.ToString(), "The sealed capture uses a schema, capability or scope version this restore planner does not support."));
			if (!capture.SourceTarget.Equals(currentTarget))
				issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.TargetMismatch,
					currentTarget.Fingerprint, "The current target authority does not match the target recorded by the Local Collection capture."));

			long checkpoint = sealedCapture.InstalledIdentities.DeploymentCommitSequence;
			if (!capture.SourceTarget.Equals(sealedCapture.InstalledIdentities.Target) ||
				!capture.SourceTarget.Equals(sealedCapture.OwnerPayloads.Target) ||
				!capture.SourceTarget.Equals(sealedCapture.ScriptedReplay.Target) ||
				!capture.SourceTarget.Equals(sealedCapture.NativeEffects.Target) ||
				!capture.SourceTarget.Equals(sealedCapture.UserMetadata.Target) ||
				!capture.Identity.Equals(sealedCapture.OwnerPayloads.CaptureIdentity) ||
				!capture.Identity.Equals(sealedCapture.ScriptedReplay.CaptureIdentity) ||
				!capture.Identity.Equals(sealedCapture.UserMetadata.CaptureIdentity) ||
				checkpoint != sealedCapture.OwnerPayloads.DeploymentCommitSequence ||
				checkpoint != sealedCapture.ScriptedReplay.DeploymentCommitSequence ||
				checkpoint != sealedCapture.NativeEffects.DeploymentCommitSequence ||
				checkpoint != sealedCapture.UserMetadata.DeploymentCommitSequence)
			{
				issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.CaptureSnapshotMismatch,
					capture.Identity.ToString(), "The packaged C7 capture components do not describe one coherent target/capture/checkpoint."));
			}
		}

		private static void ValidateSnapshotRetainedReferences(CollectionSealedCaptureSnapshot sealedCapture,
			List<CollectionLocalRestorePlanIssue> issues)
		{
			Dictionary<string, RetainedArtifactReference> byRole = sealedCapture.Capture.RetainedArtifacts
				.ToDictionary(x => x.Role, x => x, StringComparer.Ordinal);
			foreach (CollectionCapturedArchiveArtifact archive in sealedCapture.Archives)
				ValidateSnapshotReference(archive.RetainedArtifact, byRole, issues);
			foreach (CollectionOwnerPayloadOwner owner in sealedCapture.OwnerPayloads.Targets.SelectMany(x => x.Owners))
			{
				if (owner.RetainedPayload != null)
					ValidateSnapshotReference(new RetainedArtifactReference(owner.RetainedPayload.StableArtifactId,
						owner.RetainedPayload.ReferenceRole, owner.RetainedPayload.ContentHash, owner.RetainedPayload.ByteLength),
						byRole, issues);
			}
			foreach (CollectionScriptedReplayArtifactSet replay in sealedCapture.ScriptedReplay.ArtifactSets)
			{
				ValidateSnapshotReference(replay.ReplayXml, byRole, issues);
				foreach (CollectionScriptedGeneratedPayload generated in replay.GeneratedPayloads)
					ValidateSnapshotReference(generated.RetainedArtifact, byRole, issues);
			}
			foreach (CollectionCapturedScreenshotOverride screenshot in sealedCapture.UserMetadata.ScreenshotOverrides)
				ValidateSnapshotReference(screenshot.RetainedArtifact, byRole, issues);
		}

		private static void ValidateSnapshotReference(RetainedArtifactReference reference,
			IDictionary<string, RetainedArtifactReference> byRole, List<CollectionLocalRestorePlanIssue> issues)
		{
			RetainedArtifactReference sealedReference;
			if (!byRole.TryGetValue(reference.Role, out sealedReference) ||
				!StringComparer.Ordinal.Equals(sealedReference.StableArtifactId, reference.StableArtifactId) ||
				!sealedReference.ContentHash.Equals(reference.ContentHash) || sealedReference.ByteLength != reference.ByteLength)
			{
				issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.RetainedArtifactMetadataMismatch,
					reference.Role, "A packaged snapshot artifact reference does not match the retained reference sealed into the LocalCapture contract."));
			}
		}

		private void ValidateRetainedArtifacts(LocalCapture capture, List<CollectionLocalRestorePlanIssue> issues,
			CancellationToken cancellationToken)
		{
			var verified = new HashSet<string>(StringComparer.Ordinal);
			foreach (RetainedArtifactReference reference in capture.RetainedArtifacts.OrderBy(x => x.Role, StringComparer.Ordinal))
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionsRetainedArtifact persisted = _artifactStore.GetArtifact(reference.StableArtifactId);
				if (persisted == null)
				{
					issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.RetainedArtifactMissing,
						reference.Role, "A retained artifact required by the sealed capture is no longer recorded in Collections storage."));
					continue;
				}
				if (!persisted.ContentHash.Equals(reference.ContentHash) || persisted.ByteLength != reference.ByteLength)
				{
					issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.RetainedArtifactMetadataMismatch,
						reference.Role, "Retained-artifact metadata no longer matches the exact identity sealed into the capture."));
					continue;
				}
				if (verified.Add(reference.StableArtifactId) && !_artifactStore.VerifyArtifact(reference.StableArtifactId, cancellationToken))
					issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.RetainedArtifactCorrupt,
						reference.Role, "Retained artifact bytes no longer match their sealed SHA-256 identity."));
			}
		}

		private static Dictionary<string, CollectionCapturedArchiveArtifact> BuildArchiveIndex(
			CollectionSealedCaptureSnapshot sealedCapture, List<CollectionLocalRestorePlanIssue> issues)
		{
			var result = new Dictionary<string, CollectionCapturedArchiveArtifact>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionCapturedArchiveArtifact archive in sealedCapture.Archives)
			{
				if (!result.ContainsKey(archive.NativeSnapshotKey))
					result.Add(archive.NativeSnapshotKey, archive);
				else
					issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.NativeMappingAmbiguous,
						archive.NativeSnapshotKey, "The sealed capture contains more than one retained archive for the same native snapshot member."));
			}
			return result;
		}

		private static Dictionary<string, LocalCaptureNativeRecordMapping> BuildMappingIndex(LocalCapture capture,
			List<CollectionLocalRestorePlanIssue> issues)
		{
			var result = new Dictionary<string, LocalCaptureNativeRecordMapping>(StringComparer.OrdinalIgnoreCase);
			foreach (LocalCaptureNativeRecordMapping mapping in capture.NativeRecordMappings)
			{
				string key = mapping.SourceNativeInstance.NativeModKey;
				if (!result.ContainsKey(key))
					result.Add(key, mapping);
				else
					issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.NativeMappingAmbiguous,
						key, "More than one Local Collection member maps to the same captured native registration."));
			}
			return result;
		}

		private static InstallLogReadMod FindReusableNative(CollectionInstalledModIdentity capturedMod,
			CollectionCapturedArchiveArtifact retainedArchive, IList<InstallLogReadMod> currentMods,
			HashSet<string> usedCurrentKeys, IDictionary<string, FileIdentity> fileIdentityCache,
			List<CollectionLocalRestorePlanIssue> issues, CancellationToken cancellationToken)
		{
			List<InstallLogReadMod> candidates = new List<InstallLogReadMod>();
			foreach (InstallLogReadMod current in currentMods)
			{
				if (!InstallContextMatches(capturedMod, current) || !RecordedIdentityMatches(capturedMod, current))
					continue;
				if (retainedArchive != null && !CurrentArchiveMatches(current.ArchivePath, retainedArchive.RetainedArtifact,
					fileIdentityCache, cancellationToken))
					continue;
				if (retainedArchive == null && !StringComparer.OrdinalIgnoreCase.Equals(capturedMod.NativeSnapshotKey, current.ModKey))
					continue;
				candidates.Add(current);
			}

			InstallLogReadMod exact = candidates.FirstOrDefault(x =>
				StringComparer.OrdinalIgnoreCase.Equals(x.ModKey, capturedMod.NativeSnapshotKey));
			if (exact != null)
			{
				if (usedCurrentKeys.Contains(exact.ModKey))
				{
					issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.CurrentNativeMatchAmbiguous,
						capturedMod.NativeSnapshotKey, "The exact current native key is already required by another captured Local Collection member."));
					return null;
				}
				return exact;
			}

			candidates = candidates.Where(x => !usedCurrentKeys.Contains(x.ModKey)).ToList();
			if (candidates.Count == 1)
				return candidates[0];
			if (candidates.Count > 1)
				issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.CurrentNativeMatchAmbiguous,
					capturedMod.NativeSnapshotKey, "More than one current native registration has the captured install context and exact archive identity; automatic remapping is ambiguous."));
			return null;
		}

		private static bool InstallContextMatches(CollectionInstalledModIdentity captured, InstallLogReadMod current)
		{
			return captured.InstallContext.Method == current.InstallMethod && captured.InstallContext.InstallRoot == current.InstallRoot;
		}

		private static bool RecordedIdentityMatches(CollectionInstalledModIdentity captured, InstallLogReadMod current)
		{
			bool hasNexusIdentity = IsPositiveInteger(captured.NexusModId) && IsPositiveInteger(captured.NexusFileId);
			if (hasNexusIdentity && (!StringComparer.Ordinal.Equals(captured.NexusModId, current.NexusModId) ||
				!StringComparer.Ordinal.Equals(captured.NexusFileId, current.NexusFileId)))
				return false;
			if (!String.IsNullOrWhiteSpace(captured.MachineVersion) && !String.IsNullOrWhiteSpace(current.MachineVersion) &&
				!StringComparer.Ordinal.Equals(captured.MachineVersion, current.MachineVersion))
				return false;
			return true;
		}

		private static bool IsPositiveInteger(string value)
		{
			long parsed;
			return Int64.TryParse(value, out parsed) && parsed > 0;
		}

		private static bool CurrentArchiveMatches(string path, RetainedArtifactReference retained,
			IDictionary<string, FileIdentity> cache, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
				return false;
			try
			{
				FileIdentity identity;
				if (!cache.TryGetValue(path, out identity))
				{
					identity = ComputeFileIdentity(path, cancellationToken);
					cache[path] = identity;
				}
				return identity.ByteLength == retained.ByteLength && identity.ContentHash.Equals(retained.ContentHash);
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				return false;
			}
		}

		private static FileIdentity ComputeFileIdentity(string path, CancellationToken cancellationToken)
		{
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan))
			using (SHA256 sha256 = SHA256.Create())
			{
				byte[] buffer = new byte[128 * 1024];
				long length = 0;
				int read;
				while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha256.TransformBlock(buffer, 0, read, null, 0);
					length += read;
				}
				sha256.TransformFinalBlock(new byte[0], 0, 0);
				return new FileIdentity(CollectionContentHash.FromSha256(ToHex(sha256.Hash)), length);
			}
		}

		private static Dictionary<string, CollectionLocalRestoreMemberPlan> BuildMemberPlanIndex(
			IEnumerable<CollectionLocalRestoreMemberPlan> members, List<CollectionLocalRestorePlanIssue> issues)
		{
			var result = new Dictionary<string, CollectionLocalRestoreMemberPlan>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionLocalRestoreMemberPlan member in members)
			{
				if (!result.ContainsKey(member.CapturedNativeKey))
					result.Add(member.CapturedNativeKey, member);
				else
					issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.NativeMappingAmbiguous,
						member.CapturedNativeKey, "The packaged installed-identity snapshot contains the same native snapshot key more than once."));
			}
			return result;
		}

		private static void ValidateSnapshotNativeReferences(CollectionSealedCaptureSnapshot sealedCapture,
			NativeStateCaptureSnapshot currentNativeState, IDictionary<string, CollectionLocalRestoreMemberPlan> members,
			List<CollectionLocalRestorePlanIssue> issues)
		{
			foreach (CollectionCapturedArchiveArtifact archive in sealedCapture.Archives)
				RequireCapturedMemberReference(archive.NativeSnapshotKey, "archive:" + archive.NativeSnapshotKey, members, issues);
			foreach (CollectionScriptedReplayArtifactSet replay in sealedCapture.ScriptedReplay.ArtifactSets)
				RequireCapturedMemberReference(replay.NativeSnapshotKey, "replay:" + replay.NativeSnapshotKey, members, issues);
			foreach (CollectionCapturedSortAssignment sort in sealedCapture.UserMetadata.SortAssignments)
				RequireCapturedMemberReference(sort.NativeSnapshotKey, "sort:" + sort.NativeSnapshotKey, members, issues);
			foreach (CollectionCapturedScreenshotOverride screenshot in sealedCapture.UserMetadata.ScreenshotOverrides)
				RequireCapturedMemberReference(screenshot.NativeSnapshotKey, "screenshot:" + screenshot.NativeSnapshotKey, members, issues);

			foreach (CollectionCapturedIniEffect effect in sealedCapture.NativeEffects.IniEdits)
			{
				foreach (CollectionCapturedIniOwnerValue owner in effect.Values)
					ValidateEffectOwner(owner.OwnerKey, owner.OwnerKind, "ini:" + effect.ResourceKey, currentNativeState, members, issues);
			}
			foreach (CollectionCapturedGameValueEffect effect in sealedCapture.NativeEffects.GameValues)
			{
				foreach (CollectionCapturedGameValueOwnerValue owner in effect.Values)
					ValidateEffectOwner(owner.OwnerKey, owner.OwnerKind, "game-value:" + effect.Key, currentNativeState, members, issues);
			}
		}

		private static void ValidateEffectOwner(string ownerKey, CollectionNativeEffectOwnerKind ownerKind, string resourceKey,
			NativeStateCaptureSnapshot currentNativeState, IDictionary<string, CollectionLocalRestoreMemberPlan> members,
			List<CollectionLocalRestorePlanIssue> issues)
		{
			if (ownerKind == CollectionNativeEffectOwnerKind.OriginalValue)
			{
				if (String.IsNullOrWhiteSpace(currentNativeState.InstallLog.OriginalValuesKey))
					issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.CurrentOriginalOwnerUnavailable,
						resourceKey, "Current native state does not expose the original-values owner required by a captured configuration effect."));
				return;
			}
			if (ownerKind == CollectionNativeEffectOwnerKind.NativeMod)
			{
				RequireCapturedMemberReference(ownerKey, resourceKey, members, issues);
				return;
			}
			issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.CapturedOwnerUnresolved,
				resourceKey, "A captured configuration-effect owner is unresolved and cannot be remapped automatically."));
		}

		private static void RequireCapturedMemberReference(string capturedNativeKey, string resourceKey,
			IDictionary<string, CollectionLocalRestoreMemberPlan> members, List<CollectionLocalRestorePlanIssue> issues)
		{
			if (String.IsNullOrWhiteSpace(capturedNativeKey) || !members.ContainsKey(capturedNativeKey))
				issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.NativeMappingMissing,
					resourceKey, "A packaged capture record refers to a native snapshot key that has no Local Collection member remap."));
		}

		private static List<CollectionLocalRestoreDeploymentPlan> BuildDeploymentPlans(
			CollectionSealedCaptureSnapshot sealedCapture, NativeStateCaptureSnapshot currentNativeState,
			IDictionary<string, CollectionLocalRestoreMemberPlan> memberByCapturedKey,
			List<CollectionLocalRestorePlanIssue> issues)
		{
			var result = new List<CollectionLocalRestoreDeploymentPlan>();
			foreach (CollectionOwnerPayloadTarget target in sealedCapture.OwnerPayloads.Targets)
			{
				var owners = new List<CollectionLocalRestoreOwnerBinding>();
				foreach (CollectionOwnerPayloadOwner owner in target.Owners.OrderBy(x => x.StackIndex))
				{
					if (owner.Kind == NativeStateCaptureDeploymentOwnerKind.OriginalValue)
					{
						string originalKey = currentNativeState.InstallLog.OriginalValuesKey;
						if (String.IsNullOrWhiteSpace(originalKey))
						{
							issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.CurrentOriginalOwnerUnavailable,
								target.Target.ToString(), "Current native state does not expose the original-values owner key required by the captured owner stack."));
							owners.Add(new CollectionLocalRestoreOwnerBinding(owner.StackIndex, owner.OwnerKey, owner.Kind,
								owner.CurrentWinner, CollectionLocalRestoreOwnerBindingKind.Unresolved, null, null));
						}
						else
							owners.Add(new CollectionLocalRestoreOwnerBinding(owner.StackIndex, owner.OwnerKey, owner.Kind,
								owner.CurrentWinner, CollectionLocalRestoreOwnerBindingKind.OriginalValue, originalKey, null));
						continue;
					}

					CollectionLocalRestoreMemberPlan member;
					if (owner.Kind == NativeStateCaptureDeploymentOwnerKind.Unresolved ||
						!memberByCapturedKey.TryGetValue(owner.OwnerKey, out member))
					{
						issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.CapturedOwnerUnresolved,
							target.Target + "#" + owner.StackIndex, "A captured deployment owner cannot be mapped to a Local Collection snapshot member."));
						owners.Add(new CollectionLocalRestoreOwnerBinding(owner.StackIndex, owner.OwnerKey, owner.Kind,
							owner.CurrentWinner, CollectionLocalRestoreOwnerBindingKind.Unresolved, null, null));
						continue;
					}

					ModInstallMethod expectedMethod = owner.Kind == NativeStateCaptureDeploymentOwnerKind.Direct
						? ModInstallMethod.Direct : ModInstallMethod.Virtual;
					if (member.InstallContext.Method != expectedMethod)
						issues.Add(new CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind.CapturedOwnerMethodMismatch,
							target.Target + "#" + owner.StackIndex, "Captured owner topology conflicts with the member's recorded native install method."));

					if (member.Action == CollectionLocalRestoreMemberAction.ReuseExistingNative)
						owners.Add(new CollectionLocalRestoreOwnerBinding(owner.StackIndex, owner.OwnerKey, owner.Kind,
							owner.CurrentWinner, CollectionLocalRestoreOwnerBindingKind.ExistingNative, member.CurrentNativeKey, null));
					else if (member.Action == CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive)
						owners.Add(new CollectionLocalRestoreOwnerBinding(owner.StackIndex, owner.OwnerKey, owner.Kind,
							owner.CurrentWinner, CollectionLocalRestoreOwnerBindingKind.RecreatedSnapshotMember, null, member.SnapshotMemberKey));
					else
						owners.Add(new CollectionLocalRestoreOwnerBinding(owner.StackIndex, owner.OwnerKey, owner.Kind,
							owner.CurrentWinner, CollectionLocalRestoreOwnerBindingKind.Unresolved, null, member.SnapshotMemberKey));
				}
				result.Add(new CollectionLocalRestoreDeploymentPlan(target.Target, target.Promoted, owners));
			}
			return result;
		}

		private static string CreatePlanFingerprint(LocalCapture capture, CollectionTargetIdentity currentTarget,
			CollectionCurrentStateFingerprint currentStateFingerprint, long deploymentCommitSequence, string currentOriginalValuesKey,
			IEnumerable<CollectionLocalRestoreMemberPlan> members, IEnumerable<CollectionLocalRestoreDeploymentPlan> deployments,
			IEnumerable<string> removeKeys, IEnumerable<CollectionLocalRestorePlanIssue> issues)
		{
			var canonical = new StringBuilder();
			canonical.Append("nmmce-local-restore-plan-v1\n");
			canonical.Append(capture.Identity).Append('\n');
			canonical.Append(currentTarget.Fingerprint).Append('\n');
			canonical.Append(currentStateFingerprint.FormatVersion).Append(':').Append(currentStateFingerprint.Value).Append('\n');
			canonical.Append(deploymentCommitSequence).Append('\n');
			canonical.Append(currentOriginalValuesKey ?? String.Empty).Append('\n');
			foreach (CollectionLocalRestoreMemberPlan member in members)
			{
				canonical.Append("M|").Append((int)member.SnapshotMemberKey.Kind).Append('|').Append(member.SnapshotMemberKey.Value)
					.Append('|').Append(member.CapturedNativeKey).Append('|').Append((int)member.Action).Append('|')
					.Append(member.CurrentNativeKey).Append('|').Append((int)member.InstallContext.Method).Append('|')
					.Append((int)member.InstallContext.InstallRoot).Append('|')
					.Append(member.RetainedArchive == null ? String.Empty : member.RetainedArchive.RetainedArtifact.StableArtifactId).Append('\n');
			}
			foreach (CollectionLocalRestoreDeploymentPlan deployment in deployments)
			{
				canonical.Append("D|").Append((int)deployment.Target.Root).Append('|').Append(deployment.Target.RelativePath)
					.Append('|').Append(deployment.Promoted ? '1' : '0').Append('\n');
				foreach (CollectionLocalRestoreOwnerBinding owner in deployment.Owners)
					canonical.Append("O|").Append(owner.StackIndex).Append('|').Append(owner.CapturedOwnerKey).Append('|')
						.Append((int)owner.CapturedOwnerKind).Append('|').Append((int)owner.BindingKind).Append('|')
						.Append(owner.CurrentNativeKey).Append('|')
						.Append(owner.RecreatedSnapshotMember == null ? String.Empty : owner.RecreatedSnapshotMember.ToString()).Append('\n');
			}
			foreach (string key in removeKeys)
				canonical.Append("R|").Append(key).Append('\n');
			foreach (CollectionLocalRestorePlanIssue issue in issues)
				canonical.Append("I|").Append((int)issue.Kind).Append('|').Append(issue.ResourceKey).Append('|').Append(issue.Message).Append('\n');

			using (SHA256 sha256 = SHA256.Create())
				return "restore-plan-sha256:" + ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())));
		}

		private static string ToHex(byte[] bytes)
		{
			var builder = new StringBuilder(bytes.Length * 2);
			foreach (byte value in bytes)
				builder.Append(value.ToString("x2"));
			return builder.ToString();
		}

		private sealed class FileIdentity
		{
			public FileIdentity(CollectionContentHash contentHash, long byteLength)
			{
				ContentHash = contentHash;
				ByteLength = byteLength;
			}

			public CollectionContentHash ContentHash { get; }
			public long ByteLength { get; }
		}
	}
}
