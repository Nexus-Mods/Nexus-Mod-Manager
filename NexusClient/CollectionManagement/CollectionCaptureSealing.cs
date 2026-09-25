using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Classifies the severity of one C7.7 sealing/completeness finding.</summary>
	public enum CollectionCaptureSealIssueSeverity
	{
		RestorableBlocker = 1,
		Fatal = 2
	}

	/// <summary>Classifies one deterministic C7.7 capture-sealing/completeness finding.</summary>
	public enum CollectionCaptureSealIssueKind
	{
		UnsupportedScopeVersion = 1,
		CaptureStateChanged = 2,
		SnapshotCheckpointMismatch = 3,
		NativeRecordMappingIncomplete = 4,
		ArchiveUnavailable = 5,
		ArchiveRetentionFailed = 6,
		OwnerPayloadIncomplete = 7,
		ScriptedReplayIncomplete = 8,
		NativeConfigurationIncomplete = 9,
		PluginStateIncomplete = 10,
		UserMetadataIncomplete = 11,
		RetainedArtifactMissing = 12,
		RetainedArtifactMetadataMismatch = 13,
		RetainedArtifactCorrupt = 14,
		RetainedArtifactReferenceConflict = 15,
		ArchiveIdentityMismatch = 16,
		InvalidExclusion = 17
	}

	/// <summary>Records one immutable C7.7 reason a capture cannot be sealed or cannot claim local restorability.</summary>
	public sealed class CollectionCaptureSealIssue
	{
		/// <summary>Creates one sealing/completeness issue.</summary>
		public CollectionCaptureSealIssue(CollectionCaptureSealIssueKind kind, CollectionCaptureSealIssueSeverity severity,
			LocalCaptureScopeArea area, string resourceKey, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionCaptureSealIssueKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (!Enum.IsDefined(typeof(CollectionCaptureSealIssueSeverity), severity))
				throw new ArgumentOutOfRangeException(nameof(severity));
			if (area != LocalCaptureScopeArea.Unknown && !Enum.IsDefined(typeof(LocalCaptureScopeArea), area))
				throw new ArgumentOutOfRangeException(nameof(area));
			Kind = kind;
			Severity = severity;
			Area = area;
			ResourceKey = resourceKey ?? String.Empty;
			Message = message ?? String.Empty;
		}

		public CollectionCaptureSealIssueKind Kind { get; }
		public CollectionCaptureSealIssueSeverity Severity { get; }
		public LocalCaptureScopeArea Area { get; }
		public string ResourceKey { get; }
		public string Message { get; }
		public bool BlocksSealing { get { return Severity == CollectionCaptureSealIssueSeverity.Fatal; } }
		public bool BlocksLocalRestorability { get { return true; } }
	}

	/// <summary>Captures one installed native member's immutable retained source archive for offline reconstruction.</summary>
	public sealed class CollectionCapturedArchiveArtifact
	{
		/// <summary>Creates one immutable retained archive descriptor.</summary>
		public CollectionCapturedArchiveArtifact(string nativeSnapshotKey, string fileName,
			CollectionInstalledSourceIdentity sourceIdentity, RetainedArtifactReference retainedArtifact)
		{
			if (String.IsNullOrWhiteSpace(nativeSnapshotKey))
				throw new ArgumentException("A native snapshot key is required.", nameof(nativeSnapshotKey));
			NativeSnapshotKey = nativeSnapshotKey;
			FileName = fileName ?? String.Empty;
			SourceIdentity = sourceIdentity;
			RetainedArtifact = retainedArtifact ?? throw new ArgumentNullException(nameof(retainedArtifact));
		}

		public string NativeSnapshotKey { get; }
		public string FileName { get; }
		public CollectionInstalledSourceIdentity SourceIdentity { get; }
		public RetainedArtifactReference RetainedArtifact { get; }
	}

	/// <summary>Immutable input contract consumed by the C7.7 sealing/completeness boundary.</summary>
	public sealed class CollectionCaptureSealRequest
	{
		private readonly ReadOnlyCollection<CollectionCapturedArchiveArtifact> _retainedArchives;
		private readonly ReadOnlyCollection<LocalCaptureExclusion> _exclusions;
		private readonly ReadOnlyCollection<LocalCaptureNativeRecordMapping> _nativeRecordMappings;

		/// <summary>Creates one sealing request over a completed C7.2-C7.6 capture attempt.</summary>
		public CollectionCaptureSealRequest(LocalCaptureIdentity identity, CollectionRevisionIdentity revision,
			CollectionTargetIdentity sourceTarget, CollectionCurrentStateFingerprint capturedStateFingerprint,
			CollectionCurrentStateFingerprint sealingStateFingerprint, LocalCaptureScope scope,
			LocalCaptureCapability requestedCapability, CollectionInstalledIdentitySnapshot installedIdentities,
			CollectionOwnerPayloadSnapshot ownerPayloads, CollectionScriptedReplaySnapshot scriptedReplay,
			CollectionNativeEffectSnapshot nativeEffects, CollectionUserMetadataSnapshot userMetadata,
			IEnumerable<CollectionCapturedArchiveArtifact> retainedArchives, IEnumerable<LocalCaptureExclusion> exclusions,
			IEnumerable<LocalCaptureNativeRecordMapping> nativeRecordMappings)
		{
			Identity = identity ?? throw new ArgumentNullException(nameof(identity));
			Revision = revision ?? throw new ArgumentNullException(nameof(revision));
			if (Revision.Collection.Origin != CollectionOrigin.Local)
				throw new ArgumentException("C7.7 can seal only a Local Collection revision.", nameof(revision));
			SourceTarget = sourceTarget ?? throw new ArgumentNullException(nameof(sourceTarget));
			CapturedStateFingerprint = capturedStateFingerprint ?? throw new ArgumentNullException(nameof(capturedStateFingerprint));
			SealingStateFingerprint = sealingStateFingerprint ?? throw new ArgumentNullException(nameof(sealingStateFingerprint));
			Scope = scope ?? throw new ArgumentNullException(nameof(scope));
			if (!Enum.IsDefined(typeof(LocalCaptureCapability), requestedCapability) || requestedCapability == LocalCaptureCapability.Unknown)
				throw new ArgumentOutOfRangeException(nameof(requestedCapability));
			RequestedCapability = requestedCapability;
			InstalledIdentities = installedIdentities ?? throw new ArgumentNullException(nameof(installedIdentities));
			OwnerPayloads = ownerPayloads ?? throw new ArgumentNullException(nameof(ownerPayloads));
			ScriptedReplay = scriptedReplay ?? throw new ArgumentNullException(nameof(scriptedReplay));
			NativeEffects = nativeEffects ?? throw new ArgumentNullException(nameof(nativeEffects));
			UserMetadata = userMetadata ?? throw new ArgumentNullException(nameof(userMetadata));
			_retainedArchives = Copy(retainedArchives, nameof(retainedArchives));
			_exclusions = Copy(exclusions, nameof(exclusions));
			_nativeRecordMappings = Copy(nativeRecordMappings, nameof(nativeRecordMappings));
		}

		public LocalCaptureIdentity Identity { get; }
		public CollectionRevisionIdentity Revision { get; }
		public CollectionTargetIdentity SourceTarget { get; }
		public CollectionCurrentStateFingerprint CapturedStateFingerprint { get; }
		public CollectionCurrentStateFingerprint SealingStateFingerprint { get; }
		public LocalCaptureScope Scope { get; }
		public LocalCaptureCapability RequestedCapability { get; }
		public CollectionInstalledIdentitySnapshot InstalledIdentities { get; }
		public CollectionOwnerPayloadSnapshot OwnerPayloads { get; }
		public CollectionScriptedReplaySnapshot ScriptedReplay { get; }
		public CollectionNativeEffectSnapshot NativeEffects { get; }
		public CollectionUserMetadataSnapshot UserMetadata { get; }
		public ReadOnlyCollection<CollectionCapturedArchiveArtifact> RetainedArchives { get { return _retainedArchives; } }
		public ReadOnlyCollection<LocalCaptureExclusion> Exclusions { get { return _exclusions; } }
		public ReadOnlyCollection<LocalCaptureNativeRecordMapping> NativeRecordMappings { get { return _nativeRecordMappings; } }

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => ReferenceEquals(x, null)))
				throw new ArgumentException("A capture sealing request cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}

	/// <summary>
	/// Immutable C7.7 package binding one sealed LocalCapture contract to the exact C7.2-C7.6 snapshots it validated.
	/// </summary>
	public sealed class CollectionSealedCaptureSnapshot
	{
		private readonly ReadOnlyCollection<CollectionCapturedArchiveArtifact> _archives;

		internal CollectionSealedCaptureSnapshot(LocalCapture capture, CollectionInstalledIdentitySnapshot installedIdentities,
			IEnumerable<CollectionCapturedArchiveArtifact> archives, CollectionOwnerPayloadSnapshot ownerPayloads,
			CollectionScriptedReplaySnapshot scriptedReplay, CollectionNativeEffectSnapshot nativeEffects,
			CollectionUserMetadataSnapshot userMetadata)
		{
			Capture = capture ?? throw new ArgumentNullException(nameof(capture));
			InstalledIdentities = installedIdentities ?? throw new ArgumentNullException(nameof(installedIdentities));
			_archives = new ReadOnlyCollection<CollectionCapturedArchiveArtifact>((archives ?? throw new ArgumentNullException(nameof(archives))).ToList());
			OwnerPayloads = ownerPayloads ?? throw new ArgumentNullException(nameof(ownerPayloads));
			ScriptedReplay = scriptedReplay ?? throw new ArgumentNullException(nameof(scriptedReplay));
			NativeEffects = nativeEffects ?? throw new ArgumentNullException(nameof(nativeEffects));
			UserMetadata = userMetadata ?? throw new ArgumentNullException(nameof(userMetadata));
		}

		public LocalCapture Capture { get; }
		public CollectionInstalledIdentitySnapshot InstalledIdentities { get; }
		public ReadOnlyCollection<CollectionCapturedArchiveArtifact> Archives { get { return _archives; } }
		public CollectionOwnerPayloadSnapshot OwnerPayloads { get; }
		public CollectionScriptedReplaySnapshot ScriptedReplay { get; }
		public CollectionNativeEffectSnapshot NativeEffects { get; }
		public CollectionUserMetadataSnapshot UserMetadata { get; }
	}

	/// <summary>Immutable result of one C7.7 sealing attempt.</summary>
	public sealed class CollectionCaptureSealResult
	{
		private readonly ReadOnlyCollection<CollectionCaptureSealIssue> _issues;

		internal CollectionCaptureSealResult(LocalCaptureCapability requestedCapability,
			CollectionSealedCaptureSnapshot sealedCapture, IEnumerable<CollectionCaptureSealIssue> issues)
		{
			RequestedCapability = requestedCapability;
			SealedCapture = sealedCapture;
			_issues = new ReadOnlyCollection<CollectionCaptureSealIssue>((issues ?? Enumerable.Empty<CollectionCaptureSealIssue>()).ToList());
		}

		public LocalCaptureCapability RequestedCapability { get; }
		public CollectionSealedCaptureSnapshot SealedCapture { get; }
		public ReadOnlyCollection<CollectionCaptureSealIssue> Issues { get { return _issues; } }
		public bool IsSealed { get { return SealedCapture != null; } }
		public bool HasFatalIssues { get { return _issues.Any(x => x.BlocksSealing); } }
		public bool HasRestorabilityBlockers { get { return _issues.Any(x => x.BlocksLocalRestorability); } }
	}

	/// <summary>
	/// C7.7 boundary which validates capture coherence/completeness, freezes archive bytes and publishes one immutable capture promise.
	/// </summary>
	/// <remarks>
	/// A requested restorable capture either seals as <see cref="LocalCaptureCapability.LocallyRestorableWithinScope"/> or does not
	/// seal. It is never silently downgraded to recipe-only. This service writes only Collections retained-content/reference state;
	/// it performs no native game/deployment mutation.
	/// </remarks>
	public sealed class CollectionCaptureSealer
	{
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;

		/// <summary>Creates a capture sealer over the existing retained-content infrastructure.</summary>
		public CollectionCaptureSealer(CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
		{
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
		}

		/// <summary>Seals one completed capture attempt without cancellation.</summary>
		public CollectionCaptureSealResult Seal(CollectionCaptureSealRequest request)
		{
			return Seal(request, CancellationToken.None);
		}

		/// <summary>Validates and seals one completed capture attempt with cooperative retained-content verification.</summary>
		public CollectionCaptureSealResult Seal(CollectionCaptureSealRequest request, CancellationToken cancellationToken)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));

			var issues = new List<CollectionCaptureSealIssue>();
			ValidateStructuralCoherence(request, issues);
			ValidateCompleteness(request, issues);

			List<CollectionCapturedArchiveArtifact> archives = new List<CollectionCapturedArchiveArtifact>();
			if (!issues.Any(x => x.BlocksSealing) && request.Scope.Contains(LocalCaptureScopeArea.ModArchives))
				archives = PrepareArchives(request, issues, cancellationToken);

			List<RetainedArtifactReference> retainedArtifacts = CollectRetainedArtifacts(request, archives);
			if (!issues.Any(x => x.BlocksSealing))
				ValidateRetainedArtifacts(request.Identity, retainedArtifacts, issues, cancellationToken);

			bool canSeal = !issues.Any(x => x.BlocksSealing);
			if (request.RequestedCapability == LocalCaptureCapability.LocallyRestorableWithinScope &&
				issues.Any(x => x.BlocksLocalRestorability))
				canSeal = false;
			if (!canSeal)
				return new CollectionCaptureSealResult(request.RequestedCapability, null, issues);

			LocalCapture capture = new LocalCapture(request.Identity, request.Revision, request.SourceTarget,
				request.CapturedStateFingerprint, request.Scope, request.RequestedCapability,
				LocalCapture.CurrentSchemaVersion, LocalCapture.CurrentCapabilityVersion,
				retainedArtifacts, request.Exclusions, request.NativeRecordMappings);
			var sealedCapture = new CollectionSealedCaptureSnapshot(capture, request.InstalledIdentities, archives,
				request.OwnerPayloads, request.ScriptedReplay, request.NativeEffects, request.UserMetadata);
			return new CollectionCaptureSealResult(request.RequestedCapability, sealedCapture, issues);
		}

		private static void ValidateStructuralCoherence(CollectionCaptureSealRequest request,
			List<CollectionCaptureSealIssue> issues)
		{
			if (request.Scope.Version != LocalCaptureScope.CurrentVersion)
			{
				AddFatal(issues, CollectionCaptureSealIssueKind.UnsupportedScopeVersion, LocalCaptureScopeArea.Unknown,
					"scope", "The requested Local Collection capture scope version is not supported by this C7.7 sealer.");
			}
			if (!request.CapturedStateFingerprint.Equals(request.SealingStateFingerprint))
			{
				AddFatal(issues, CollectionCaptureSealIssueKind.CaptureStateChanged, LocalCaptureScopeArea.ManagedModState,
					"native-state", "The managed-state fingerprint changed between capture and sealing.");
			}

			CollectionTargetIdentity target = request.SourceTarget;
			if (!target.Equals(request.InstalledIdentities.Target) || !target.Equals(request.OwnerPayloads.Target) ||
				!target.Equals(request.ScriptedReplay.Target) || !target.Equals(request.NativeEffects.Target) ||
				!target.Equals(request.UserMetadata.Target))
			{
				AddFatal(issues, CollectionCaptureSealIssueKind.SnapshotCheckpointMismatch, LocalCaptureScopeArea.ManagedModState,
					"target", "C7.2-C7.6 snapshots do not all belong to the requested capture target.");
			}
			if (!request.Identity.Equals(request.OwnerPayloads.CaptureIdentity) ||
				!request.Identity.Equals(request.ScriptedReplay.CaptureIdentity) ||
				!request.Identity.Equals(request.UserMetadata.CaptureIdentity))
			{
				AddFatal(issues, CollectionCaptureSealIssueKind.SnapshotCheckpointMismatch, LocalCaptureScopeArea.ManagedModState,
					"capture-identity", "Retained C7.3/C7.4/C7.6 snapshots do not belong to the requested capture identity.");
			}

			long checkpoint = request.InstalledIdentities.DeploymentCommitSequence;
			if (request.OwnerPayloads.DeploymentCommitSequence != checkpoint ||
				request.ScriptedReplay.DeploymentCommitSequence != checkpoint ||
				request.NativeEffects.DeploymentCommitSequence != checkpoint ||
				request.UserMetadata.DeploymentCommitSequence != checkpoint)
			{
				AddFatal(issues, CollectionCaptureSealIssueKind.SnapshotCheckpointMismatch, LocalCaptureScopeArea.ManagedModState,
					"deployment-checkpoint", "C7.2-C7.6 snapshots were not captured from the same native deployment checkpoint.");
			}

			ValidateNativeMappings(request, issues);
			ValidateExclusions(request, issues);
		}

		private static void ValidateExclusions(CollectionCaptureSealRequest request,
			List<CollectionCaptureSealIssue> issues)
		{
			var keys = new HashSet<string>(StringComparer.Ordinal);
			foreach (LocalCaptureExclusion exclusion in request.Exclusions)
			{
				string key = ((int)exclusion.Area).ToString(CultureInfo.InvariantCulture) + ":" + exclusion.Code;
				if (!request.Scope.Contains(exclusion.Area) || !keys.Add(key))
				{
					AddFatal(issues, CollectionCaptureSealIssueKind.InvalidExclusion, exclusion.Area, exclusion.Code,
						"Capture exclusions must be unique and belong to an explicitly declared capture-scope area.");
				}
			}
		}

		private static void ValidateNativeMappings(CollectionCaptureSealRequest request,
			List<CollectionCaptureSealIssue> issues)
		{
			var expected = new HashSet<string>(request.InstalledIdentities.Mods.Select(x => x.NativeSnapshotKey),
				StringComparer.OrdinalIgnoreCase);
			if (expected.Count != request.InstalledIdentities.Mods.Count)
			{
				AddFatal(issues, CollectionCaptureSealIssueKind.NativeRecordMappingIncomplete,
					LocalCaptureScopeArea.ManagedModState, "native-identities",
					"Installed-identity capture contains duplicate native snapshot keys.");
			}

			var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var mappedMembers = new HashSet<CollectionMemberKey>();
			foreach (LocalCaptureNativeRecordMapping mapping in request.NativeRecordMappings)
			{
				if (!request.SourceTarget.Equals(mapping.SourceNativeInstance.Target) ||
					!expected.Contains(mapping.SourceNativeInstance.NativeModKey) ||
					!mapped.Add(mapping.SourceNativeInstance.NativeModKey) ||
					!mappedMembers.Add(mapping.SnapshotMemberKey))
				{
					AddFatal(issues, CollectionCaptureSealIssueKind.NativeRecordMappingIncomplete,
						LocalCaptureScopeArea.ManagedModState, mapping.SourceNativeInstance.NativeModKey,
						"Local Collection member/native mappings are not a one-to-one projection of the captured active native registrations.");
				}
			}
			foreach (string nativeKey in expected)
			{
				if (!mapped.Contains(nativeKey))
				{
					AddFatal(issues, CollectionCaptureSealIssueKind.NativeRecordMappingIncomplete,
						LocalCaptureScopeArea.ManagedModState, nativeKey,
						"An active captured native registration has no Local Collection snapshot-member mapping.");
				}
			}
		}

		private static void ValidateCompleteness(CollectionCaptureSealRequest request,
			List<CollectionCaptureSealIssue> issues)
		{
			if (request.Scope.Contains(LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads))
			{
				RequireCompleteCoverage(request.OwnerPayloads.Coverage, issues,
					CollectionCaptureSealIssueKind.OwnerPayloadIncomplete,
					LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads, "file-ownership",
					"File ownership/fallback payload capture is incomplete.");
				foreach (CollectionOwnerPayloadTarget target in request.OwnerPayloads.Targets)
				{
					foreach (CollectionOwnerPayloadOwner owner in target.Owners)
					{
						if (owner.Kind == NativeStateCaptureDeploymentOwnerKind.Unresolved || owner.RetainedPayload == null)
						{
							AddRestorableBlocker(issues, CollectionCaptureSealIssueKind.OwnerPayloadIncomplete,
								LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads, target.Target.ToString(),
								"A captured file-owner stack contains an unresolved owner or missing retained fallback payload.");
						}
					}
				}
			}

			if (request.Scope.Contains(LocalCaptureScopeArea.InstallerReplayAndGeneratedPayloads))
			{
				RequireCompleteCoverage(request.ScriptedReplay.Coverage, issues,
					CollectionCaptureSealIssueKind.ScriptedReplayIncomplete,
					LocalCaptureScopeArea.InstallerReplayAndGeneratedPayloads, "scripted-replay",
					"Scripted installer replay/generated-payload capture is incomplete.");
				var replayKeys = new HashSet<string>(request.ScriptedReplay.ArtifactSets.Select(x => x.NativeSnapshotKey),
					StringComparer.OrdinalIgnoreCase);
				foreach (CollectionInstalledModIdentity mod in request.InstalledIdentities.Mods.Where(x => x.HasInstallScript))
				{
					CollectionScriptedReplayArtifactSet set = request.ScriptedReplay.ArtifactSets
						.FirstOrDefault(x => String.Equals(x.NativeSnapshotKey, mod.NativeSnapshotKey, StringComparison.OrdinalIgnoreCase));
					if (set == null || !set.CompleteReplayFormat || !set.ReplayPlanValidated)
					{
						AddRestorableBlocker(issues, CollectionCaptureSealIssueKind.ScriptedReplayIncomplete,
							LocalCaptureScopeArea.InstallerReplayAndGeneratedPayloads, mod.NativeSnapshotKey,
							"A scripted native member does not have one complete validated retained replay artifact set.");
					}
				}
				if (replayKeys.Count != request.ScriptedReplay.ArtifactSets.Count)
				{
					AddRestorableBlocker(issues, CollectionCaptureSealIssueKind.ScriptedReplayIncomplete,
						LocalCaptureScopeArea.InstallerReplayAndGeneratedPayloads, "scripted-replay",
						"Scripted replay capture contains duplicate native member keys.");
				}
			}

			if (request.Scope.Contains(LocalCaptureScopeArea.NativeConfigurationEffects))
			{
				RequireCompleteCoverage(request.NativeEffects.IniCoverage, issues,
					CollectionCaptureSealIssueKind.NativeConfigurationIncomplete,
					LocalCaptureScopeArea.NativeConfigurationEffects, "ini-effects",
					"Native INI effect capture is incomplete.");
				RequireCompleteCoverage(request.NativeEffects.GameValueCoverage, issues,
					CollectionCaptureSealIssueKind.NativeConfigurationIncomplete,
					LocalCaptureScopeArea.NativeConfigurationEffects, "game-values",
					"Native game-specific effect capture is incomplete.");
			}
			if (request.Scope.Contains(LocalCaptureScopeArea.PluginState))
			{
				RequireCompleteCoverage(request.NativeEffects.PluginCoverage, issues,
					CollectionCaptureSealIssueKind.PluginStateIncomplete, LocalCaptureScopeArea.PluginState,
					"plugins", "Managed plugin final-state capture is incomplete.");
			}
			if (request.Scope.Contains(LocalCaptureScopeArea.UserMetadata))
			{
				RequireCompleteCoverage(request.UserMetadata.SortCoverage, issues,
					CollectionCaptureSealIssueKind.UserMetadataIncomplete, LocalCaptureScopeArea.UserMetadata,
					"sort", "Logical Sort assignment capture is incomplete.");
				RequireCompleteCoverage(request.UserMetadata.ScreenshotCoverage, issues,
					CollectionCaptureSealIssueKind.UserMetadataIncomplete, LocalCaptureScopeArea.UserMetadata,
					"screenshot-overrides", "Logical screenshot-override capture is incomplete.");
			}
		}

		private List<CollectionCapturedArchiveArtifact> PrepareArchives(CollectionCaptureSealRequest request,
			List<CollectionCaptureSealIssue> issues, CancellationToken cancellationToken)
		{
			var installedByKey = request.InstalledIdentities.Mods.ToDictionary(x => x.NativeSnapshotKey,
				x => x, StringComparer.OrdinalIgnoreCase);
			var supplied = new Dictionary<string, CollectionCapturedArchiveArtifact>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionCapturedArchiveArtifact archive in request.RetainedArchives)
			{
				CollectionInstalledModIdentity installed;
				if (!installedByKey.TryGetValue(archive.NativeSnapshotKey, out installed))
				{
					AddFatal(issues, CollectionCaptureSealIssueKind.ArchiveIdentityMismatch,
						LocalCaptureScopeArea.ModArchives, archive.NativeSnapshotKey,
						"A supplied retained archive does not belong to any captured native snapshot member.");
					continue;
				}
				if (!SourceIdentityMatches(installed.Archive.SourceIdentity, archive.SourceIdentity))
				{
					AddFatal(issues, CollectionCaptureSealIssueKind.ArchiveIdentityMismatch,
						LocalCaptureScopeArea.ModArchives, archive.NativeSnapshotKey,
						"A supplied retained archive declares source provenance that conflicts with the captured native archive identity.");
					continue;
				}
				if (!supplied.ContainsKey(archive.NativeSnapshotKey))
					supplied.Add(archive.NativeSnapshotKey, archive);
				else
					AddFatal(issues, CollectionCaptureSealIssueKind.ArchiveIdentityMismatch,
						LocalCaptureScopeArea.ModArchives, archive.NativeSnapshotKey,
						"More than one retained archive was supplied for the same native snapshot member.");
			}

			if (issues.Any(x => x.BlocksSealing))
				return new List<CollectionCapturedArchiveArtifact>();

			var result = new List<CollectionCapturedArchiveArtifact>();
			foreach (CollectionInstalledModIdentity mod in request.InstalledIdentities.Mods
				.OrderBy(x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase))
			{
				string livePath = mod.Archive.LiveArchivePath;
				if (String.IsNullOrWhiteSpace(livePath) || !File.Exists(livePath))
				{
					AddRestorableBlocker(issues, CollectionCaptureSealIssueKind.ArchiveUnavailable,
						LocalCaptureScopeArea.ModArchives, mod.NativeSnapshotKey,
						"The installed native member has no available archive whose exact bytes can be frozen and proven for this capture.");
					continue;
				}

				try
				{
					CollectionsRetainedArtifact artifact = _artifactStore.PublishFile(livePath, cancellationToken);
					CollectionCapturedArchiveArtifact existing;
					if (supplied.TryGetValue(mod.NativeSnapshotKey, out existing) &&
						(!StringComparer.Ordinal.Equals(existing.RetainedArtifact.StableArtifactId, artifact.ArtifactId) ||
						 !existing.RetainedArtifact.ContentHash.Equals(artifact.ContentHash) ||
						 existing.RetainedArtifact.ByteLength != artifact.ByteLength))
					{
						AddFatal(issues, CollectionCaptureSealIssueKind.ArchiveIdentityMismatch,
							LocalCaptureScopeArea.ModArchives, mod.NativeSnapshotKey,
							"A supplied retained archive does not match the exact live archive bytes captured for the native member.");
						continue;
					}

					string role = CreateArchiveRole(mod.NativeSnapshotKey);
					_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId,
						CollectionsRetainedArtifactOwnerKind.Capture, request.Identity.ToString(), role);
					var retained = new RetainedArtifactReference(artifact.ArtifactId, role, artifact.ContentHash, artifact.ByteLength);
					result.Add(new CollectionCapturedArchiveArtifact(mod.NativeSnapshotKey, mod.Archive.FileName,
						mod.Archive.SourceIdentity, retained));
				}
				catch (InvalidDataException exception)
				{
					AddFatal(issues, CollectionCaptureSealIssueKind.RetainedArtifactCorrupt,
						LocalCaptureScopeArea.ModArchives, mod.NativeSnapshotKey, exception.Message);
				}
				catch (InvalidOperationException exception)
				{
					AddFatal(issues, CollectionCaptureSealIssueKind.RetainedArtifactReferenceConflict,
						LocalCaptureScopeArea.ModArchives, mod.NativeSnapshotKey, exception.Message);
				}
				catch (Exception exception) when (exception is FileNotFoundException || exception is DirectoryNotFoundException ||
					exception is IOException || exception is UnauthorizedAccessException)
				{
					AddRestorableBlocker(issues, CollectionCaptureSealIssueKind.ArchiveRetentionFailed,
						LocalCaptureScopeArea.ModArchives, mod.NativeSnapshotKey, exception.Message);
				}
			}
			return result;
		}

		private static List<RetainedArtifactReference> CollectRetainedArtifacts(CollectionCaptureSealRequest request,
			IEnumerable<CollectionCapturedArchiveArtifact> archives)
		{
			var result = new List<RetainedArtifactReference>();
			if (request.Scope.Contains(LocalCaptureScopeArea.ModArchives))
				result.AddRange(archives.Select(x => x.RetainedArtifact));
			if (request.Scope.Contains(LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads))
			{
				result.AddRange(request.OwnerPayloads.Targets.SelectMany(x => x.Owners)
					.Where(x => x.RetainedPayload != null)
					.Select(x => new RetainedArtifactReference(x.RetainedPayload.StableArtifactId,
						x.RetainedPayload.ReferenceRole, x.RetainedPayload.ContentHash, x.RetainedPayload.ByteLength)));
			}
			if (request.Scope.Contains(LocalCaptureScopeArea.InstallerReplayAndGeneratedPayloads))
			{
				foreach (CollectionScriptedReplayArtifactSet set in request.ScriptedReplay.ArtifactSets)
				{
					result.Add(set.ReplayXml);
					result.AddRange(set.GeneratedPayloads.Select(x => x.RetainedArtifact));
				}
			}
			if (request.Scope.Contains(LocalCaptureScopeArea.UserMetadata))
				result.AddRange(request.UserMetadata.ScreenshotOverrides.Select(x => x.RetainedArtifact));

			return result.OrderBy(x => x.Role, StringComparer.Ordinal).ToList();
		}

		private void ValidateRetainedArtifacts(LocalCaptureIdentity captureIdentity,
			IEnumerable<RetainedArtifactReference> retainedArtifacts, List<CollectionCaptureSealIssue> issues,
			CancellationToken cancellationToken)
		{
			var roles = new HashSet<string>(StringComparer.Ordinal);
			var validatedArtifacts = new Dictionary<string, RetainedArtifactReference>(StringComparer.Ordinal);
			foreach (RetainedArtifactReference reference in retainedArtifacts)
			{
				if (!roles.Add(reference.Role))
				{
					AddFatal(issues, CollectionCaptureSealIssueKind.RetainedArtifactReferenceConflict,
						LocalCaptureScopeArea.Unknown, reference.Role,
						"The sealed capture contains more than one retained artifact bound to the same semantic role.");
					continue;
				}

				RetainedArtifactReference alreadyValidated;
				bool needsContentVerification = !validatedArtifacts.TryGetValue(reference.StableArtifactId, out alreadyValidated);
				if (!needsContentVerification &&
					(!alreadyValidated.ContentHash.Equals(reference.ContentHash) || alreadyValidated.ByteLength != reference.ByteLength))
				{
					AddFatal(issues, CollectionCaptureSealIssueKind.RetainedArtifactMetadataMismatch,
						LocalCaptureScopeArea.Unknown, reference.Role,
						"Two semantic roles bind the same retained artifact identity with conflicting SHA-256/length metadata.");
					continue;
				}

				if (needsContentVerification)
				{
					CollectionsRetainedArtifact artifact;
					try
					{
						artifact = _artifactStore.GetArtifact(reference.StableArtifactId);
					}
					catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
					{
						AddFatal(issues, CollectionCaptureSealIssueKind.RetainedArtifactMissing,
							LocalCaptureScopeArea.Unknown, reference.Role, exception.Message);
						continue;
					}
					if (artifact == null)
					{
						AddFatal(issues, CollectionCaptureSealIssueKind.RetainedArtifactMissing,
							LocalCaptureScopeArea.Unknown, reference.Role,
							"A retained artifact referenced by the capture is not recorded in Collections storage.");
						continue;
					}
					if (!reference.ContentHash.Equals(artifact.ContentHash) || reference.ByteLength != artifact.ByteLength)
					{
						AddFatal(issues, CollectionCaptureSealIssueKind.RetainedArtifactMetadataMismatch,
							LocalCaptureScopeArea.Unknown, reference.Role,
							"A retained artifact reference no longer matches its sealed SHA-256/length metadata.");
						continue;
					}
					try
					{
						if (!_artifactStore.VerifyArtifact(reference.StableArtifactId, cancellationToken))
						{
							AddFatal(issues, CollectionCaptureSealIssueKind.RetainedArtifactCorrupt,
								LocalCaptureScopeArea.Unknown, reference.Role,
								"A retained artifact failed seal-time SHA-256/length verification.");
							continue;
						}
					}
					catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
					{
						AddFatal(issues, CollectionCaptureSealIssueKind.RetainedArtifactCorrupt,
							LocalCaptureScopeArea.Unknown, reference.Role, exception.Message);
						continue;
					}
					validatedArtifacts.Add(reference.StableArtifactId, reference);
				}

				try
				{
					_referenceStore.AcquireExclusiveRoleReference(reference.StableArtifactId,
						CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString(), reference.Role);
				}
				catch (Exception exception) when (exception is InvalidOperationException || exception is IOException ||
					exception is UnauthorizedAccessException)
				{
					AddFatal(issues, CollectionCaptureSealIssueKind.RetainedArtifactReferenceConflict,
						LocalCaptureScopeArea.Unknown, reference.Role, exception.Message);
				}
			}
		}

		private static void RequireCompleteCoverage(NativeStateCaptureCoverage coverage,
			List<CollectionCaptureSealIssue> issues, CollectionCaptureSealIssueKind kind,
			LocalCaptureScopeArea area, string resourceKey, string message)
		{
			if (coverage != NativeStateCaptureCoverage.Complete && coverage != NativeStateCaptureCoverage.NotApplicable)
				AddRestorableBlocker(issues, kind, area, resourceKey, message);
		}

		private static bool SourceIdentityMatches(CollectionInstalledSourceIdentity captured,
			CollectionInstalledSourceIdentity supplied)
		{
			if (captured == null || supplied == null)
				return captured == null && supplied == null;
			return StringComparer.Ordinal.Equals(captured.Scheme, supplied.Scheme) &&
				StringComparer.Ordinal.Equals(captured.StableId, supplied.StableId);
		}

		private static string CreateArchiveRole(string nativeSnapshotKey)
		{
			using (SHA256 sha256 = SHA256.Create())
			{
				byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes((nativeSnapshotKey ?? String.Empty).ToUpperInvariant()));
				var builder = new StringBuilder("mod-archive:".Length + hash.Length * 2);
				builder.Append("mod-archive:");
				foreach (byte value in hash)
					builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
				return builder.ToString();
			}
		}

		private static void AddRestorableBlocker(List<CollectionCaptureSealIssue> issues,
			CollectionCaptureSealIssueKind kind, LocalCaptureScopeArea area, string resourceKey, string message)
		{
			issues.Add(new CollectionCaptureSealIssue(kind, CollectionCaptureSealIssueSeverity.RestorableBlocker,
				area, resourceKey, message));
		}

		private static void AddFatal(List<CollectionCaptureSealIssue> issues, CollectionCaptureSealIssueKind kind,
			LocalCaptureScopeArea area, string resourceKey, string message)
		{
			issues.Add(new CollectionCaptureSealIssue(kind, CollectionCaptureSealIssueSeverity.Fatal,
				area, resourceKey, message));
		}
	}
}
