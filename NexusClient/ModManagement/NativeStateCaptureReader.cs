using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Transactions;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Creates one detached read-only observation of native NMM state without applying backup, profile or Collection policy.
	/// </summary>
	/// <remarks>
	/// The caller is responsible for establishing a stable read boundary. This reader never performs recovery, writes native
	/// stores, changes deployment, copies retained content or restores anything.
	/// </remarks>
	public sealed class NativeStateCaptureReader
	{
		private readonly IInstallLog _installLog;
		private readonly IVirtualModActivator _virtualModActivator;
		private readonly IModDeploymentManager _deploymentManager;
		private readonly IPluginManager _pluginManager;
		private readonly IGameMode _gameMode;

		/// <summary>Creates a generic native-state capture reader over already-established NMM services.</summary>
		public NativeStateCaptureReader(IInstallLog installLog, IVirtualModActivator virtualModActivator,
			IModDeploymentManager deploymentManager, IPluginManager pluginManager, IGameMode gameMode)
		{
			_installLog = installLog ?? throw new ArgumentNullException(nameof(installLog));
			_virtualModActivator = virtualModActivator ?? throw new ArgumentNullException(nameof(virtualModActivator));
			_gameMode = gameMode ?? throw new ArgumentNullException(nameof(gameMode));
			_deploymentManager = deploymentManager;
			_pluginManager = pluginManager;
		}

		/// <summary>
		/// Captures one deterministic immutable observation from the current committed native services.
		/// </summary>
		public NativeStateCaptureSnapshot Capture()
		{
			if (Transaction.Current != null)
				throw new InvalidOperationException("Native state cannot be captured from inside an ambient native transaction.");

			InstallLogReadSnapshot install = RequireInstallLogSnapshot(_installLog.GetCommittedStateSnapshot());
			VirtualModReadSnapshot virtualState = RequireVirtualSnapshot(_virtualModActivator.GetReadSnapshot());
			var issues = new List<NativeStateCaptureIssue>();
			List<NativeStateCaptureVirtualPayloadSource> activeVirtualPayloadSources = CaptureActiveVirtualPayloadSources(virtualState, issues);
			List<NativeStateCaptureRoot> roots = CaptureRoots(issues);

			NativeStateCaptureCoverage deploymentCoverage;
			List<NativeStateCaptureDeploymentTarget> deploymentTargets = CaptureDeploymentTargets(install, issues, out deploymentCoverage);
			List<NativeStateCaptureReplayReference> replayReferences = CaptureReplayReferences(install, issues);

			NativeStateCaptureCoverage pluginCoverage;
			List<NativeStateCapturePlugin> plugins = CapturePlugins(issues, out pluginCoverage);

			return new NativeStateCaptureSnapshot(install, virtualState, activeVirtualPayloadSources, roots, deploymentTargets, deploymentCoverage,
				replayReferences, plugins, pluginCoverage, issues);
		}

		private static InstallLogReadSnapshot RequireInstallLogSnapshot(InstallLogReadSnapshot source)
		{
			if (source == null)
				throw new InvalidOperationException("The committed InstallLog read snapshot is unavailable.");
			return source;
		}

		private static VirtualModReadSnapshot RequireVirtualSnapshot(VirtualModReadSnapshot source)
		{
			if (source == null)
				throw new InvalidOperationException("The Virtual Mod Activator read snapshot is unavailable.");
			return source;
		}

		private static List<NativeStateCaptureVirtualPayloadSource> CaptureActiveVirtualPayloadSources(VirtualModReadSnapshot virtualState,
			List<NativeStateCaptureIssue> issues)
		{
			var result = new List<NativeStateCaptureVirtualPayloadSource>();
			foreach (VirtualModReadLink link in virtualState.Links.Where(x => x.Active)
				.OrderBy(x => (int)x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase)
				.ThenByDescending(x => x.Priority).ThenBy(x => x.OwnerKey ?? x.OwnerReference, StringComparer.OrdinalIgnoreCase))
			{
				string sourcePath = link.ResolvedPayloadSourcePath ?? String.Empty;
				if (String.IsNullOrWhiteSpace(link.OwnerKey))
				{
					issues.Add(new NativeStateCaptureIssue(NativeStateCaptureIssueKind.VirtualPayloadSourceUnavailable,
						link.Target.ToString(), "An active Virtual link has no native owner key, so its staged payload source cannot be identified authoritatively."));
				}
				else if (String.IsNullOrWhiteSpace(sourcePath))
				{
					issues.Add(new NativeStateCaptureIssue(NativeStateCaptureIssueKind.VirtualPayloadSourceUnavailable,
						link.Target.ToString(), "An active Virtual owner has no VMA-resolved live payload source."));
				}

				result.Add(new NativeStateCaptureVirtualPayloadSource(link.Target, link.OwnerKey, link.OwnerReference, sourcePath));
			}
			return result;
		}

		private List<NativeStateCaptureRoot> CaptureRoots(List<NativeStateCaptureIssue> issues)
		{
			var result = new List<NativeStateCaptureRoot>();
			foreach (ModDeploymentRoot root in Enum.GetValues(typeof(ModDeploymentRoot)).Cast<ModDeploymentRoot>().OrderBy(x => (int)x))
			{
				if (root == ModDeploymentRoot.Secondary && !_gameMode.HasSecondaryInstallPath)
					continue;
				try
				{
					result.Add(new NativeStateCaptureRoot(root,
						Path.GetFullPath(ModDeploymentTargetResolver.GetPhysicalRootPath(_gameMode, root))));
				}
				catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
					exception is IOException || exception is NotSupportedException)
				{
					issues.Add(new NativeStateCaptureIssue(NativeStateCaptureIssueKind.PhysicalRootUnavailable,
						root.ToString(), exception.Message));
				}
			}
			return result;
		}

		private List<NativeStateCaptureDeploymentTarget> CaptureDeploymentTargets(InstallLogReadSnapshot install,
			List<NativeStateCaptureIssue> issues, out NativeStateCaptureCoverage coverage)
		{
			if (install.DeploymentTargets.Count == 0)
			{
				coverage = NativeStateCaptureCoverage.NotApplicable;
				return new List<NativeStateCaptureDeploymentTarget>();
			}

			if (_deploymentManager == null)
			{
				coverage = NativeStateCaptureCoverage.Unavailable;
				issues.Add(new NativeStateCaptureIssue(NativeStateCaptureIssueKind.DeploymentStateUnavailable,
					"deployment", "The method-neutral deployment manager is unavailable; promoted owner topology remains available in the committed InstallLog snapshot."));
				return new List<NativeStateCaptureDeploymentTarget>();
			}

			coverage = NativeStateCaptureCoverage.Complete;
			Dictionary<string, InstallLogReadMod> modsByKey = install.Mods
				.GroupBy(x => x.ModKey, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
			var result = new List<NativeStateCaptureDeploymentTarget>(install.DeploymentTargets.Count);

			foreach (InstallLogReadDeploymentTarget record in install.DeploymentTargets
				.OrderBy(x => (int)x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase))
			{
				string deploymentPath = String.Empty;
				try
				{
					deploymentPath = _deploymentManager.GetDeploymentPath(record.Target) ?? String.Empty;
				}
				catch (Exception exception) when (IsPathObservationException(exception))
				{
					coverage = NativeStateCaptureCoverage.Partial;
					issues.Add(new NativeStateCaptureIssue(NativeStateCaptureIssueKind.DeploymentPathUnavailable,
						record.Target.ToString(), exception.Message));
				}

				string winnerKey = record.OwnerKeys.LastOrDefault();
				var owners = new List<NativeStateCaptureDeploymentOwner>(record.OwnerKeys.Count);
				foreach (string ownerKey in record.OwnerKeys)
				{
					bool currentWinner = String.Equals(ownerKey, winnerKey, StringComparison.OrdinalIgnoreCase);
					NativeStateCaptureDeploymentOwnerKind kind = ResolveOwnerKind(install, modsByKey, ownerKey);
					if (kind == NativeStateCaptureDeploymentOwnerKind.Unresolved)
					{
						coverage = NativeStateCaptureCoverage.Partial;
						issues.Add(new NativeStateCaptureIssue(NativeStateCaptureIssueKind.DeploymentOwnerUnresolved,
							record.Target.ToString(), "A promoted deployment owner does not identify the original value or a current native mod registration."));
					}

					string sourcePath = ResolveDeploymentPayloadSource(record.Target, ownerKey, kind, currentWinner,
						deploymentPath, issues, ref coverage);
					owners.Add(new NativeStateCaptureDeploymentOwner(ownerKey, kind, currentWinner, sourcePath));
				}

				result.Add(new NativeStateCaptureDeploymentTarget(record.Target, deploymentPath, owners));
			}

			return result;
		}

		private static NativeStateCaptureDeploymentOwnerKind ResolveOwnerKind(InstallLogReadSnapshot install,
			IDictionary<string, InstallLogReadMod> modsByKey, string ownerKey)
		{
			if (String.Equals(ownerKey, install.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
				return NativeStateCaptureDeploymentOwnerKind.OriginalValue;

			InstallLogReadMod mod;
			if (!modsByKey.TryGetValue(ownerKey, out mod))
				return NativeStateCaptureDeploymentOwnerKind.Unresolved;
			return mod.InstallMethod == ModInstallMethod.Direct
				? NativeStateCaptureDeploymentOwnerKind.Direct
				: NativeStateCaptureDeploymentOwnerKind.Virtual;
		}

		private string ResolveDeploymentPayloadSource(ModDeploymentTarget target, string ownerKey,
			NativeStateCaptureDeploymentOwnerKind kind, bool currentWinner, string deploymentPath,
			List<NativeStateCaptureIssue> issues, ref NativeStateCaptureCoverage coverage)
		{
			if (kind == NativeStateCaptureDeploymentOwnerKind.Unresolved)
				return String.Empty;

			try
			{
				string sourcePath;
				switch (kind)
				{
					case NativeStateCaptureDeploymentOwnerKind.OriginalValue:
						sourcePath = _deploymentManager.GetOwnerBackupPath(target, ownerKey);
						break;
					case NativeStateCaptureDeploymentOwnerKind.Direct:
						sourcePath = currentWinner ? deploymentPath : _deploymentManager.GetOwnerBackupPath(target, ownerKey);
						break;
					case NativeStateCaptureDeploymentOwnerKind.Virtual:
						sourcePath = _deploymentManager.GetOwnerSourcePath(target, ownerKey);
						break;
					default:
						return String.Empty;
				}

				if (!String.IsNullOrWhiteSpace(sourcePath))
					return sourcePath;

				coverage = NativeStateCaptureCoverage.Partial;
				issues.Add(new NativeStateCaptureIssue(NativeStateCaptureIssueKind.DeploymentPayloadSourceUnavailable,
					target.ToString(), "A promoted deployment owner has no resolvable live payload source reference."));
				return String.Empty;
			}
			catch (Exception exception) when (IsPathObservationException(exception))
			{
				coverage = NativeStateCaptureCoverage.Partial;
				issues.Add(new NativeStateCaptureIssue(NativeStateCaptureIssueKind.DeploymentPayloadSourceUnavailable,
					target.ToString(), exception.Message));
				return String.Empty;
			}
		}

		private List<NativeStateCaptureReplayReference> CaptureReplayReferences(InstallLogReadSnapshot install,
			List<NativeStateCaptureIssue> issues)
		{
			var scriptedMods = install.Mods.Where(x => !x.Hidden && x.HasInstallScript)
				.OrderBy(x => x.ModKey, StringComparer.OrdinalIgnoreCase).ToList();
			if (scriptedMods.Count == 0)
				return new List<NativeStateCaptureReplayReference>();

			string installInfoDirectory = _gameMode.GameModeEnvironmentInfo == null
				? null
				: _gameMode.GameModeEnvironmentInfo.InstallInfoDirectory;
			var result = new List<NativeStateCaptureReplayReference>(scriptedMods.Count);
			foreach (InstallLogReadMod mod in scriptedMods)
			{
				try
				{
					string cachePath = ScriptedFileSelectionCache.GetDefaultFilePath(mod.FileName, installInfoDirectory);
					result.Add(new NativeStateCaptureReplayReference(mod.ModKey, cachePath,
						ScriptedFileSelectionCache.GetPayloadDirectoryPath(cachePath)));
				}
				catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
					exception is IOException || exception is NotSupportedException)
				{
					issues.Add(new NativeStateCaptureIssue(NativeStateCaptureIssueKind.ReplayReferenceUnavailable,
						mod.ModKey, exception.Message));
				}
			}
			return result;
		}

		private List<NativeStateCapturePlugin> CapturePlugins(List<NativeStateCaptureIssue> issues,
			out NativeStateCaptureCoverage coverage)
		{
			if (!_gameMode.UsesPlugins)
			{
				coverage = NativeStateCaptureCoverage.NotApplicable;
				return new List<NativeStateCapturePlugin>();
			}

			PluginSnapshot snapshot = _pluginManager == null ? null : _pluginManager.CurrentSnapshot;
			if (snapshot == null)
			{
				coverage = NativeStateCaptureCoverage.Unavailable;
				issues.Add(new NativeStateCaptureIssue(NativeStateCaptureIssueKind.PluginStateUnavailable,
					"plugins", "The native plugin snapshot is unavailable for a plugin-enabled game."));
				return new List<NativeStateCapturePlugin>();
			}

			coverage = NativeStateCaptureCoverage.Complete;
			return snapshot.Entries.Where(x => x != null && x.Plugin != null).Select(entry =>
			{
				Plugin plugin = entry.Plugin;
				PluginMetadata metadata = plugin.Metadata ?? PluginMetadata.Unknown(plugin.Filename);
				return new NativeStateCapturePlugin(plugin.Filename, entry.Active, entry.Priority,
					entry.AllocatedIndex, entry.ModIndex, metadata.ParseStatus, metadata.AddressClass,
					metadata.HeaderFlags, metadata.SpecialFlags, metadata.EffectiveMaster, metadata.FormVersion,
					plugin.Masters == null ? Enumerable.Empty<string>() : plugin.Masters.ToArray(),
					entry.Diagnostics.Select(x => new NativeStateCapturePluginDiagnostic(x.Kind, x.Severity)));
			}).OrderBy(x => x.Priority).ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToList();
		}

		private static bool IsPathObservationException(Exception exception)
		{
			return exception is ArgumentException || exception is InvalidOperationException || exception is IOException ||
				exception is NotSupportedException;
		}
	}
}
