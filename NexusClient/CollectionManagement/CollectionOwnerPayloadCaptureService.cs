using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Retains exact current owner/fallback payload bytes for the file-ownership portion of a Local Collection capture.
	/// </summary>
	/// <remarks>
	/// The service writes only Collections-owned immutable retained content and lifetime references. It never changes game files,
	/// native deployment ownership, InstallLog state or Virtual activation. The caller remains responsible for a stable capture
	/// boundary and the later C7 sealing/completeness decision.
	/// </remarks>
	public sealed class CollectionOwnerPayloadCaptureService
	{
		private readonly NativeStateCaptureReader _nativeStateReader;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;

		/// <summary>Creates a C7.3 payload capture service over native read state and Collections retained storage.</summary>
		public CollectionOwnerPayloadCaptureService(NativeStateCaptureReader nativeStateReader,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore)
		{
			_nativeStateReader = nativeStateReader ?? throw new ArgumentNullException(nameof(nativeStateReader));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
		}

		/// <summary>Captures current file-owner topology and retains all currently resolvable owner/fallback payloads.</summary>
		public CollectionOwnerPayloadSnapshot Capture(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity)
		{
			return Capture(target, captureIdentity, CancellationToken.None);
		}

		/// <summary>Captures current file-owner topology and retains payloads with cooperative cancellation during byte copies.</summary>
		public CollectionOwnerPayloadSnapshot Capture(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity,
			CancellationToken cancellationToken)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (captureIdentity == null)
				throw new ArgumentNullException(nameof(captureIdentity));
			return Capture(target, captureIdentity, _nativeStateReader.Capture(), cancellationToken);
		}

		internal CollectionOwnerPayloadSnapshot Capture(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity,
			NativeStateCaptureSnapshot nativeState, CancellationToken cancellationToken)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (captureIdentity == null)
				throw new ArgumentNullException(nameof(captureIdentity));
			if (nativeState == null)
				throw new ArgumentNullException(nameof(nativeState));

			InstallLogReadSnapshot install = nativeState.InstallLog;
			var issues = new List<CollectionOwnerPayloadIssue>();
			NativeStateCaptureCoverage coverage = NativeStateCaptureCoverage.Complete;
			Dictionary<string, InstallLogReadMod> modsByKey = install.Mods.Where(x => !x.Hidden)
				.GroupBy(x => x.ModKey, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

			Dictionary<ModDeploymentTarget, InstallLogReadDeploymentTarget> promoted = ToUniqueDictionary(
				install.DeploymentTargets, x => x.Target, "committed promoted deployment target");
			Dictionary<ModDeploymentTarget, NativeStateCaptureDeploymentTarget> observedPromoted = ToUniqueDictionary(
				nativeState.DeploymentTargets, x => x.Target, "generic native deployment target");
			Dictionary<ModDeploymentTarget, InstallLogReadFile> legacy = ToUniqueDictionary(
				install.Files, x => x.Target, "legacy InstallLog file target");
			Dictionary<ModDeploymentTarget, List<VirtualModReadLink>> virtualGroups = nativeState.VirtualState.Links
				.GroupBy(x => x.Target).ToDictionary(x => x.Key, x => x.ToList());
			Dictionary<ModDeploymentTarget, List<NativeStateCaptureVirtualPayloadSource>> virtualPayloadSources =
				nativeState.ActiveVirtualPayloadSources.GroupBy(x => x.Target).ToDictionary(x => x.Key, x => x.ToList());

			var targets = new HashSet<ModDeploymentTarget>(promoted.Keys);
			targets.UnionWith(legacy.Keys);
			targets.UnionWith(virtualGroups.Keys);
			var capturedTargets = new List<CollectionOwnerPayloadTarget>();

			foreach (ModDeploymentTarget deploymentTarget in targets
				.OrderBy(x => (int)x.Root).ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase))
			{
				InstallLogReadDeploymentTarget promotedState;
				if (promoted.TryGetValue(deploymentTarget, out promotedState))
				{
					CollectionOwnerPayloadTarget captured = CapturePromotedTarget(captureIdentity, deploymentTarget,
						promotedState, observedPromoted, install, modsByKey, issues, ref coverage, cancellationToken);
					if (captured != null)
						capturedTargets.Add(captured);
					continue;
				}

				List<VirtualModReadLink> links;
				virtualGroups.TryGetValue(deploymentTarget, out links);
				List<VirtualModReadLink> activeOwners = BuildVirtualOwnerOrder(links == null
					? Enumerable.Empty<VirtualModReadLink>() : links.Where(x => x.Active));
				if (activeOwners.Count > 0)
				{
					List<NativeStateCaptureVirtualPayloadSource> payloadSources;
					virtualPayloadSources.TryGetValue(deploymentTarget, out payloadSources);
					capturedTargets.Add(CaptureVirtualTarget(captureIdentity, deploymentTarget, activeOwners,
						payloadSources, modsByKey, issues, ref coverage, cancellationToken));
					continue;
				}

				InstallLogReadFile legacyState;
				if (links == null && legacy.TryGetValue(deploymentTarget, out legacyState) && legacyState.OwnerKeys.Count > 0)
				{
					capturedTargets.Add(CaptureLegacyTarget(deploymentTarget, legacyState, install, modsByKey,
						issues, ref coverage));
				}
			}

			if (capturedTargets.Count == 0)
				coverage = NativeStateCaptureCoverage.NotApplicable;

			return new CollectionOwnerPayloadSnapshot(target, captureIdentity, install.DeploymentCommitSequence,
				capturedTargets, coverage, issues);
		}

		private CollectionOwnerPayloadTarget CapturePromotedTarget(LocalCaptureIdentity captureIdentity,
			ModDeploymentTarget target, InstallLogReadDeploymentTarget promotedState,
			IDictionary<ModDeploymentTarget, NativeStateCaptureDeploymentTarget> observedPromoted,
			InstallLogReadSnapshot install, IDictionary<string, InstallLogReadMod> modsByKey,
			List<CollectionOwnerPayloadIssue> issues, ref NativeStateCaptureCoverage coverage,
			CancellationToken cancellationToken)
		{
			if (promotedState.OwnerKeys.Count == 0)
				return null;

			NativeStateCaptureDeploymentTarget observed;
			bool exactObservedStack = observedPromoted.TryGetValue(target, out observed) &&
				observed.Owners.Count == promotedState.OwnerKeys.Count;
			if (exactObservedStack)
			{
				for (int index = 0; index < promotedState.OwnerKeys.Count; index++)
				{
					if (!String.Equals(promotedState.OwnerKeys[index], observed.Owners[index].OwnerKey,
						StringComparison.OrdinalIgnoreCase))
					{
						exactObservedStack = false;
						break;
					}
				}
			}
			if (!exactObservedStack)
			{
				coverage = NativeStateCaptureCoverage.Partial;
				issues.Add(new CollectionOwnerPayloadIssue(CollectionOwnerPayloadIssueKind.DeploymentTopologyUnavailable,
					target.ToString(), "The committed promoted owner stack has no matching generic deployment payload observation."));
			}

			var owners = new List<CollectionOwnerPayloadOwner>(promotedState.OwnerKeys.Count);
			for (int index = 0; index < promotedState.OwnerKeys.Count; index++)
			{
				string ownerKey = promotedState.OwnerKeys[index];
				NativeStateCaptureDeploymentOwnerKind kind = ResolveOwnerKind(install, modsByKey, ownerKey);
				string sourcePath = String.Empty;
				if (exactObservedStack)
				{
					kind = observed.Owners[index].Kind;
					sourcePath = observed.Owners[index].PayloadSourcePath;
				}
				if (kind == NativeStateCaptureDeploymentOwnerKind.Unresolved)
				{
					coverage = NativeStateCaptureCoverage.Partial;
					issues.Add(new CollectionOwnerPayloadIssue(CollectionOwnerPayloadIssueKind.OwnerUnresolved,
						target.ToString(), "A promoted owner cannot be mapped to the original value or an active native mod registration."));
				}

				CollectionOwnerPayloadRetention retained = RetainPayload(captureIdentity, target, index,
					sourcePath, issues, ref coverage, cancellationToken);
				owners.Add(new CollectionOwnerPayloadOwner(index, ownerKey, null, kind,
					index == promotedState.OwnerKeys.Count - 1, retained));
			}
			return new CollectionOwnerPayloadTarget(target, true, owners);
		}

		private CollectionOwnerPayloadTarget CaptureVirtualTarget(LocalCaptureIdentity captureIdentity,
			ModDeploymentTarget target, IList<VirtualModReadLink> activeOwners,
			IList<NativeStateCaptureVirtualPayloadSource> payloadSources,
			IDictionary<string, InstallLogReadMod> modsByKey, List<CollectionOwnerPayloadIssue> issues,
			ref NativeStateCaptureCoverage coverage, CancellationToken cancellationToken)
		{
			var owners = new List<CollectionOwnerPayloadOwner>(activeOwners.Count);
			for (int index = 0; index < activeOwners.Count; index++)
			{
				VirtualModReadLink link = activeOwners[index];
				NativeStateCaptureDeploymentOwnerKind kind = ResolveVirtualOwnerKind(modsByKey, link.OwnerKey);
				if (kind == NativeStateCaptureDeploymentOwnerKind.Unresolved)
				{
					coverage = NativeStateCaptureCoverage.Partial;
					issues.Add(new CollectionOwnerPayloadIssue(CollectionOwnerPayloadIssueKind.OwnerUnresolved,
						target.ToString(), "An active Virtual owner cannot be mapped to an active Virtual native mod registration."));
				}
				string sourcePath = ResolveVirtualPayloadSource(link, payloadSources);
				CollectionOwnerPayloadRetention retained = RetainPayload(captureIdentity, target, index,
					sourcePath, issues, ref coverage, cancellationToken);
				owners.Add(new CollectionOwnerPayloadOwner(index, link.OwnerKey, link.OwnerReference, kind,
					index == activeOwners.Count - 1, retained));
			}
			return new CollectionOwnerPayloadTarget(target, false, owners);
		}

		private static CollectionOwnerPayloadTarget CaptureLegacyTarget(ModDeploymentTarget target,
			InstallLogReadFile legacyState, InstallLogReadSnapshot install, IDictionary<string, InstallLogReadMod> modsByKey,
			List<CollectionOwnerPayloadIssue> issues, ref NativeStateCaptureCoverage coverage)
		{
			var owners = new List<CollectionOwnerPayloadOwner>(legacyState.OwnerKeys.Count);
			for (int index = 0; index < legacyState.OwnerKeys.Count; index++)
			{
				string ownerKey = legacyState.OwnerKeys[index];
				NativeStateCaptureDeploymentOwnerKind kind = ResolveOwnerKind(install, modsByKey, ownerKey);
				if (kind == NativeStateCaptureDeploymentOwnerKind.Unresolved)
				{
					issues.Add(new CollectionOwnerPayloadIssue(CollectionOwnerPayloadIssueKind.OwnerUnresolved,
						target.ToString(), "A legacy InstallLog owner cannot be mapped to the original value or an active native mod registration."));
				}
				issues.Add(new CollectionOwnerPayloadIssue(CollectionOwnerPayloadIssueKind.LegacyPayloadSourceUnavailable,
					target.ToString(), "Legacy InstallLog ownership has no authoritative retained/live payload source without a matching Virtual link or promoted deployment record."));
				owners.Add(new CollectionOwnerPayloadOwner(index, ownerKey, null, kind,
					index == legacyState.OwnerKeys.Count - 1, null));
			}
			coverage = NativeStateCaptureCoverage.Partial;
			return new CollectionOwnerPayloadTarget(target, false, owners);
		}

		private CollectionOwnerPayloadRetention RetainPayload(LocalCaptureIdentity captureIdentity,
			ModDeploymentTarget target, int stackIndex, string sourcePath,
			List<CollectionOwnerPayloadIssue> issues, ref NativeStateCaptureCoverage coverage,
			CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(sourcePath))
			{
				coverage = NativeStateCaptureCoverage.Partial;
				issues.Add(new CollectionOwnerPayloadIssue(CollectionOwnerPayloadIssueKind.PayloadSourceUnavailable,
					target.ToString(), "A current owner has no authoritative payload source path to retain."));
				return null;
			}

			try
			{
				if (!File.Exists(sourcePath))
				{
					coverage = NativeStateCaptureCoverage.Partial;
					issues.Add(new CollectionOwnerPayloadIssue(CollectionOwnerPayloadIssueKind.PayloadSourceMissing,
						target.ToString(), "A payload source referenced by current native ownership no longer exists."));
					return null;
				}

				CollectionsRetainedArtifact artifact = _artifactStore.PublishFile(sourcePath, cancellationToken);
				string role = CreateReferenceRole(target, stackIndex);
				_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString(), role);
				return new CollectionOwnerPayloadRetention(artifact.ArtifactId, role,
					artifact.ContentHash, artifact.ByteLength);
			}
			catch (FileNotFoundException)
			{
				coverage = NativeStateCaptureCoverage.Partial;
				issues.Add(new CollectionOwnerPayloadIssue(CollectionOwnerPayloadIssueKind.PayloadSourceMissing,
					target.ToString(), "A payload source disappeared while it was being retained."));
				return null;
			}
			catch (DirectoryNotFoundException)
			{
				coverage = NativeStateCaptureCoverage.Partial;
				issues.Add(new CollectionOwnerPayloadIssue(CollectionOwnerPayloadIssueKind.PayloadSourceMissing,
					target.ToString(), "A payload source directory disappeared while it was being retained."));
				return null;
			}
		}

		private static string ResolveVirtualPayloadSource(VirtualModReadLink link,
			IEnumerable<NativeStateCaptureVirtualPayloadSource> payloadSources)
		{
			if (link == null || payloadSources == null)
				return String.Empty;
			foreach (NativeStateCaptureVirtualPayloadSource source in payloadSources)
			{
				if (!String.IsNullOrWhiteSpace(link.OwnerKey) &&
					String.Equals(link.OwnerKey, source.OwnerKey, StringComparison.OrdinalIgnoreCase))
					return source.PayloadSourcePath;
				if (String.IsNullOrWhiteSpace(link.OwnerKey) && !String.IsNullOrWhiteSpace(link.OwnerReference) &&
					String.Equals(link.OwnerReference, source.OwnerReference, StringComparison.Ordinal))
					return source.PayloadSourcePath;
			}
			return String.Empty;
		}

		private static string CreateReferenceRole(ModDeploymentTarget target, int stackIndex)
		{
			return String.Format(CultureInfo.InvariantCulture, "owner-payload:{0}:{1}:{2}",
				(int)target.Root, stackIndex, target.RelativePath);
		}

		private static NativeStateCaptureDeploymentOwnerKind ResolveOwnerKind(InstallLogReadSnapshot install,
			IDictionary<string, InstallLogReadMod> modsByKey, string ownerKey)
		{
			if (String.Equals(ownerKey, install.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
				return NativeStateCaptureDeploymentOwnerKind.OriginalValue;
			InstallLogReadMod mod;
			if (String.IsNullOrWhiteSpace(ownerKey) || !modsByKey.TryGetValue(ownerKey, out mod))
				return NativeStateCaptureDeploymentOwnerKind.Unresolved;
			return mod.InstallMethod == ModInstallMethod.Direct
				? NativeStateCaptureDeploymentOwnerKind.Direct
				: NativeStateCaptureDeploymentOwnerKind.Virtual;
		}

		private static NativeStateCaptureDeploymentOwnerKind ResolveVirtualOwnerKind(
			IDictionary<string, InstallLogReadMod> modsByKey, string ownerKey)
		{
			InstallLogReadMod mod;
			if (String.IsNullOrWhiteSpace(ownerKey) || !modsByKey.TryGetValue(ownerKey, out mod) ||
				mod.InstallMethod != ModInstallMethod.Virtual)
				return NativeStateCaptureDeploymentOwnerKind.Unresolved;
			return NativeStateCaptureDeploymentOwnerKind.Virtual;
		}

		private static List<VirtualModReadLink> BuildVirtualOwnerOrder(IEnumerable<VirtualModReadLink> source)
		{
			var result = new List<VirtualModReadLink>();
			foreach (VirtualModReadLink link in (source ?? Enumerable.Empty<VirtualModReadLink>())
				.OrderByDescending(x => x.Priority))
			{
				string identity = !String.IsNullOrWhiteSpace(link.OwnerKey) ? link.OwnerKey : link.OwnerReference;
				if (!String.IsNullOrWhiteSpace(identity))
				{
					int existing = result.FindIndex(x => String.Equals(
						!String.IsNullOrWhiteSpace(x.OwnerKey) ? x.OwnerKey : x.OwnerReference,
						identity, StringComparison.OrdinalIgnoreCase));
					if (existing >= 0)
						result.RemoveAt(existing);
				}
				result.Add(link);
			}
			return result;
		}

		private static Dictionary<TKey, TValue> ToUniqueDictionary<TValue, TKey>(IEnumerable<TValue> source,
			Func<TValue, TKey> keySelector, string description)
		{
			var result = new Dictionary<TKey, TValue>();
			foreach (TValue value in source)
			{
				TKey key = keySelector(value);
				if (result.ContainsKey(key))
					throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture,
						"Multiple {0} records resolve to the same logical key '{1}'.", description, key));
				result.Add(key, value);
			}
			return result;
		}
	}
}
