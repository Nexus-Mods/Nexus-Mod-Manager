using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Transactions;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Captures one detached, read-only target-scoped index of native NMM and Collection association state.
	/// </summary>
	/// <remarks>
	/// The caller is responsible for establishing a stable native read boundary before capture. This reader never performs
	/// recovery, publishes target authority, starts installers, writes native stores or changes Collection associations.
	/// </remarks>
	public sealed class CollectionNativeStateReader
	{
		private readonly IInstallLog _installLog;
		private readonly IVirtualModActivator _virtualModActivator;
		private readonly IPluginManager _pluginManager;
		private readonly IGameMode _gameMode;
		private readonly CollectionsAssociationStore _associationStore;

		/// <summary>Creates a read-only native-state reader over already-established NMM services.</summary>
		public CollectionNativeStateReader(IInstallLog installLog, IVirtualModActivator virtualModActivator,
			IPluginManager pluginManager, IGameMode gameMode, CollectionsAssociationStore associationStore)
		{
			_installLog = installLog ?? throw new ArgumentNullException(nameof(installLog));
			_virtualModActivator = virtualModActivator ?? throw new ArgumentNullException(nameof(virtualModActivator));
			_gameMode = gameMode ?? throw new ArgumentNullException(nameof(gameMode));
			_pluginManager = pluginManager;
			_associationStore = associationStore;
		}

		/// <summary>
		/// Captures one deterministic index from already-established native services.
		/// </summary>
		public CollectionNativeStateIndex Capture(CollectionTargetIdentity target)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (Transaction.Current != null)
				throw new InvalidOperationException("Collection native state cannot be captured from inside an ambient native transaction.");

			var issues = new List<CollectionNativeStateIssue>();
			InstallLogReadSnapshot install = _installLog.GetCommittedStateSnapshot();
			VirtualModReadSnapshot virtualState = _virtualModActivator.GetReadSnapshot();

			var nativeMods = new List<CollectionNativeModState>();
			var modsByKey = new Dictionary<string, CollectionNativeModState>(StringComparer.OrdinalIgnoreCase);
			foreach (InstallLogReadMod record in install.Mods.Where(x => !x.Hidden))
			{
				NativeModInstanceIdentity identity = new NativeModInstanceIdentity(target, record.ModKey);
				CollectionNativeModState state = new CollectionNativeModState(identity, record.ArchivePath,
					record.FileName, record.NexusModId, record.NexusFileId, record.HumanReadableVersion,
					record.MachineVersion, record.HasInstallScript, record.InstallRoot, record.InstallMethod);
				nativeMods.Add(state);
				modsByKey[record.ModKey] = state;
			}

			List<CollectionNativeRootState> roots = CaptureRoots(issues);
			List<CollectionNativeFileState> files = CaptureFiles(install, virtualState, modsByKey, issues);
			List<CollectionNativeIniState> iniEdits = CaptureIniEdits(install, issues);
			List<CollectionNativeGameValueState> gameValues = CaptureGameValues(install);

			CollectionNativeStateCoverage pluginCoverage;
			List<CollectionNativePluginState> plugins = CapturePlugins(issues, out pluginCoverage);

			CollectionNativeStateCoverage associationCoverage;
			IReadOnlyList<CollectionTargetAssociation> associations;
			IReadOnlyList<CollectionMemberBinding> bindings;
			IReadOnlyList<UserOverride> overrides;
			CaptureAssociations(target, issues, out associationCoverage, out associations, out bindings, out overrides);

			return new CollectionNativeStateIndex(target, roots, nativeMods, files, iniEdits, gameValues,
				plugins, pluginCoverage, associations, bindings, overrides, associationCoverage, issues,
				install.DeploymentCommitSequence);
		}

		private List<CollectionNativeRootState> CaptureRoots(List<CollectionNativeStateIssue> issues)
		{
			var result = new List<CollectionNativeRootState>();
			foreach (ModDeploymentRoot root in Enum.GetValues(typeof(ModDeploymentRoot)))
			{
				if (root == ModDeploymentRoot.Secondary && !_gameMode.HasSecondaryInstallPath)
					continue;
				try
				{
					result.Add(new CollectionNativeRootState(root,
						Path.GetFullPath(ModDeploymentTargetResolver.GetPhysicalRootPath(_gameMode, root))));
				}
				catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
					exception is IOException || exception is NotSupportedException)
				{
					issues.Add(new CollectionNativeStateIssue(CollectionNativeStateIssueKind.PhysicalRootUnavailable,
						root.ToString(), exception.Message));
				}
			}
			return result;
		}

		private List<CollectionNativeFileState> CaptureFiles(InstallLogReadSnapshot install, VirtualModReadSnapshot virtualState,
			IDictionary<string, CollectionNativeModState> modsByKey, List<CollectionNativeStateIssue> issues)
		{
			var promoted = install.DeploymentTargets.ToDictionary(x => x.Target);
			var virtualGroups = virtualState.Links.GroupBy(x => x.Target).ToDictionary(x => x.Key, x => x.ToList());
			var legacyByTarget = new Dictionary<ModDeploymentTarget, InstallLogReadFile>();

			foreach (InstallLogReadFile file in install.Files)
			{
				InstallLogReadFile existing;
				if (legacyByTarget.TryGetValue(file.Target, out existing))
					throw new InvalidOperationException(String.Format(
						"Multiple committed InstallLog file records resolve to deployment target '{0}'.", file.Target));
				legacyByTarget.Add(file.Target, file);
			}

			var targets = new HashSet<ModDeploymentTarget>(promoted.Keys);
			targets.UnionWith(virtualGroups.Keys);
			targets.UnionWith(legacyByTarget.Keys);
			var result = new List<CollectionNativeFileState>(targets.Count);

			foreach (ModDeploymentTarget deploymentTarget in targets
				.OrderBy(x => (int)x.Root).ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase))
			{
				InstallLogReadDeploymentTarget promotedState;
				InstallLogReadFile legacyState;
				List<VirtualModReadLink> targetLinks;
				promoted.TryGetValue(deploymentTarget, out promotedState);
				legacyByTarget.TryGetValue(deploymentTarget, out legacyState);
				virtualGroups.TryGetValue(deploymentTarget, out targetLinks);

				bool isPromoted = promotedState != null;
				string effectiveOwnerKey = isPromoted
					? promotedState.OwnerKeys.LastOrDefault()
					: targetLinks != null
						? BuildVirtualOwnerOrder(targetLinks.Where(x => x.Active)).LastOrDefault()
						: legacyState == null ? null : legacyState.OwnerKeys.LastOrDefault();

				List<CollectionNativeOwnerState> installLogOwners = legacyState == null
					? new List<CollectionNativeOwnerState>()
					: legacyState.OwnerKeys.Select(ownerKey => CreateOwnerState(ownerKey, null, install.OriginalValuesKey,
						modsByKey, null, issues, deploymentTarget.ToString())).ToList();
				List<CollectionNativeOwnerState> deploymentOwners = promotedState == null
					? new List<CollectionNativeOwnerState>()
					: promotedState.OwnerKeys.Select(ownerKey => CreateOwnerState(ownerKey, null, install.OriginalValuesKey,
						modsByKey, null, issues, deploymentTarget.ToString())).ToList();
				List<CollectionNativeOwnerState> virtualOwners = targetLinks == null
					? new List<CollectionNativeOwnerState>()
					: targetLinks.OrderBy(x => x.Active ? 1 : 0).ThenByDescending(x => x.Priority)
						.Select(link => CreateOwnerState(link.OwnerKey, link.OwnerReference, install.OriginalValuesKey,
							modsByKey, link, issues, deploymentTarget.ToString())).ToList();

				string physicalPath = String.Empty;
				try
				{
					physicalPath = ModDeploymentTargetResolver.GetPhysicalPath(_gameMode, deploymentTarget);
				}
				catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
					exception is IOException || exception is NotSupportedException)
				{
					issues.Add(new CollectionNativeStateIssue(CollectionNativeStateIssueKind.PhysicalRootUnavailable,
						deploymentTarget.ToString(), exception.Message));
				}

				result.Add(new CollectionNativeFileState(deploymentTarget, physicalPath, isPromoted,
					legacyState != null || isPromoted, targetLinks != null, effectiveOwnerKey,
					installLogOwners, deploymentOwners, virtualOwners));
			}
			return result;
		}

		private static List<string> BuildVirtualOwnerOrder(IEnumerable<VirtualModReadLink> source)
		{
			var ownerKeys = new List<string>();
			foreach (VirtualModReadLink link in (source ?? Enumerable.Empty<VirtualModReadLink>())
				.Where(x => !String.IsNullOrWhiteSpace(x.OwnerKey))
				.OrderBy(x => x.Active ? 1 : 0).ThenByDescending(x => x.Priority))
			{
				int existing = ownerKeys.FindIndex(x => x.Equals(link.OwnerKey, StringComparison.OrdinalIgnoreCase));
				if (existing >= 0)
					ownerKeys.RemoveAt(existing);
				ownerKeys.Add(link.OwnerKey);
			}
			return ownerKeys;
		}

		private static CollectionNativeOwnerState CreateOwnerState(string ownerKey, string unresolvedReference,
			string originalValuesKey, IDictionary<string, CollectionNativeModState> modsByKey, VirtualModReadLink virtualLink,
			List<CollectionNativeStateIssue> issues, string resourceKey)
		{
			CollectionNativeOwnerKind kind;
			if (String.IsNullOrWhiteSpace(ownerKey))
			{
				kind = CollectionNativeOwnerKind.Unresolved;
				issues.Add(new CollectionNativeStateIssue(CollectionNativeStateIssueKind.UnresolvedOwner,
					resourceKey, "A Virtual owner could not be mapped to an active native mod registration."));
			}
			else if (ownerKey.Equals(originalValuesKey, StringComparison.OrdinalIgnoreCase))
				kind = CollectionNativeOwnerKind.OriginalValue;
			else if (modsByKey.ContainsKey(ownerKey))
				kind = CollectionNativeOwnerKind.NativeMod;
			else
			{
				kind = CollectionNativeOwnerKind.Unresolved;
				unresolvedReference = ownerKey;
				issues.Add(new CollectionNativeStateIssue(CollectionNativeStateIssueKind.UnresolvedOwner,
					resourceKey, "A recorded owner key does not identify a current native mod registration."));
			}

			return new CollectionNativeOwnerState(ownerKey, unresolvedReference, kind,
				virtualLink == null ? (bool?)null : virtualLink.Active,
				virtualLink == null ? (int?)null : virtualLink.Priority,
				virtualLink == null ? null : virtualLink.StagedSourcePath);
		}

		private static List<CollectionNativeIniState> CaptureIniEdits(InstallLogReadSnapshot install,
			List<CollectionNativeStateIssue> issues)
		{
			var grouped = new Dictionary<CollectionNativeIniKey, List<InstallLogReadIniEdit>>();
			foreach (InstallLogReadIniEdit ini in install.IniEdits)
			{
				var key = new CollectionNativeIniKey(ini.File, ini.Section, ini.Key);
				List<InstallLogReadIniEdit> group;
				if (!grouped.TryGetValue(key, out group))
				{
					group = new List<InstallLogReadIniEdit>();
					grouped.Add(key, group);
				}
				group.Add(ini);
			}

			var result = new List<CollectionNativeIniState>(grouped.Count);
			foreach (KeyValuePair<CollectionNativeIniKey, List<InstallLogReadIniEdit>> group in grouped)
			{
				if (group.Value.Count > 1)
				{
					issues.Add(new CollectionNativeStateIssue(CollectionNativeStateIssueKind.AmbiguousIniIdentity,
						group.Key.ToString(), "InstallLog contains case-alias INI entries that compare as one logical setting."));
				}
				result.Add(new CollectionNativeIniState(group.Key, group.Value.SelectMany(x => x.Values)
					.Select(x => new CollectionNativeTextOwnerValue(x.OwnerKey, x.Value))));
			}
			return result;
		}

		private static List<CollectionNativeGameValueState> CaptureGameValues(InstallLogReadSnapshot install)
		{
			return install.GameValues.Select(value => new CollectionNativeGameValueState(value.Key,
				value.Values.Select(x => new CollectionNativeBinaryOwnerValue(x.OwnerKey, x.Value)))).ToList();
		}

		private List<CollectionNativePluginState> CapturePlugins(List<CollectionNativeStateIssue> issues,
			out CollectionNativeStateCoverage coverage)
		{
			if (!_gameMode.UsesPlugins)
			{
				coverage = CollectionNativeStateCoverage.NotApplicable;
				return new List<CollectionNativePluginState>();
			}
			PluginSnapshot snapshot = _pluginManager == null ? null : _pluginManager.CurrentSnapshot;
			if (snapshot == null)
			{
				coverage = CollectionNativeStateCoverage.Unavailable;
				issues.Add(new CollectionNativeStateIssue(CollectionNativeStateIssueKind.PluginStateUnavailable,
					"plugins", "The native plugin snapshot is unavailable for a plugin-enabled game."));
				return new List<CollectionNativePluginState>();
			}

			coverage = CollectionNativeStateCoverage.Complete;
			return snapshot.Entries.Where(x => x != null && x.Plugin != null).Select(entry =>
			{
				Plugin plugin = entry.Plugin;
				PluginMetadata metadata = plugin.Metadata ?? PluginMetadata.Unknown(plugin.Filename);
				return new CollectionNativePluginState(plugin.Filename, entry.Active, entry.Priority,
					entry.AllocatedIndex, entry.ModIndex, metadata.ParseStatus, metadata.AddressClass,
					metadata.HeaderFlags, metadata.SpecialFlags, metadata.EffectiveMaster, metadata.FormVersion,
					plugin.Masters == null ? Enumerable.Empty<string>() : plugin.Masters.ToArray(),
					entry.Diagnostics.Select(x => new CollectionNativePluginDiagnostic(x.Kind, x.Severity)));
			}).ToList();
		}

		private void CaptureAssociations(CollectionTargetIdentity target, List<CollectionNativeStateIssue> issues,
			out CollectionNativeStateCoverage coverage, out IReadOnlyList<CollectionTargetAssociation> associations,
			out IReadOnlyList<CollectionMemberBinding> bindings, out IReadOnlyList<UserOverride> overrides)
		{
			associations = new CollectionTargetAssociation[0];
			bindings = new CollectionMemberBinding[0];
			overrides = new UserOverride[0];
			if (_associationStore == null)
			{
				coverage = CollectionNativeStateCoverage.Unavailable;
				issues.Add(new CollectionNativeStateIssue(CollectionNativeStateIssueKind.AssociationStateUnavailable,
					"collections", "The Collections association store is unavailable."));
				return;
			}

			try
			{
				CollectionsAssociationTargetSnapshot snapshot = _associationStore.GetTargetSnapshot(target);
				associations = snapshot.Associations;
				bindings = snapshot.Bindings;
				overrides = snapshot.Overrides;
				coverage = CollectionNativeStateCoverage.Complete;
			}
			catch (Exception exception) when (exception is FileNotFoundException ||
				exception is CollectionsStoreAccessException || exception is CollectionsStoreSchemaException)
			{
				coverage = CollectionNativeStateCoverage.Unavailable;
				issues.Add(new CollectionNativeStateIssue(CollectionNativeStateIssueKind.AssociationStateUnavailable,
					"collections", exception.Message));
			}
		}
	}
}
