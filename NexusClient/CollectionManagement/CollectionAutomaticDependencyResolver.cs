using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.ModRepositories;
using Nexus.Client.Plugins;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util.Localization;
using Nexus.Client.Util;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Finds included plugin prerequisites from exact recipe outputs and archive inventories without scanning texture contents.</summary>
	internal sealed class CollectionAutomaticDependencyResolver
	{
		private readonly object _cacheLock = new object();
		private readonly Dictionary<string, IList<string>> _masterCache = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, HashSet<string>> _inventoryCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Reads plugin dependencies only for files actually deployed by the curator's selected recipe.</summary>
		internal void AddRecipeDependencies(PreparedCollectionNativeRecipe recipe, IMod mod, IGameMode gameMode,
			IDictionary<string, IList<string>> dependencies, CancellationToken cancellationToken)
		{
			IPluginFactory factory = gameMode.GetPluginFactory();
			if (factory == null) return;
			var pluginNames = new HashSet<string>(recipe.EffectPreview.PluginEffects
				.Where(x => x.Kind == CollectionPlannedPluginEffectKind.Activation)
				.Select(x => Path.GetFileName(x.PluginPaths[0])), StringComparer.OrdinalIgnoreCase);
			if (pluginNames.Count == 0) return;
			string stamp = ArchiveStamp(mod);
			var pending = new List<InstallModFileOperation>();
			foreach (InstallModFileOperation file in recipe.RecipeInput.NativeOperations.OfType<InstallModFileOperation>())
			{
				string adjustedPath = gameMode.GetModFormatAdjustedPath(mod.Format, file.DestinationPath, false);
				string name = Path.GetFileName(adjustedPath);
				if (!pluginNames.Contains(name)) continue;
				cancellationToken.ThrowIfCancellationRequested();
				string cacheKey = stamp + "|" + file.SourcePath;
				IList<string> masters;
				lock (_cacheLock) _masterCache.TryGetValue(cacheKey, out masters);
				if (masters == null) pending.Add(file);
				else dependencies[name] = masters;
			}
			if (pending.Count == 0) return;

			string temporaryDirectory = Path.Combine(Path.GetTempPath(), "NMMp-" + FileUtil.CreateTempToken());
			try
			{
				Directory.CreateDirectory(temporaryDirectory);
				var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (InstallModFileOperation file in pending)
				{
					if (destinations.ContainsKey(file.SourcePath)) continue;
					string directory = Path.Combine(temporaryDirectory, destinations.Count.ToString(CultureInfo.InvariantCulture));
					Directory.CreateDirectory(directory);
					string name = Path.GetFileName(gameMode.GetModFormatAdjustedPath(mod.Format, file.DestinationPath, false));
					destinations.Add(file.SourcePath, Path.Combine(directory, name));
				}
				IModFileBatchSource batchSource = mod as IModFileBatchSource;
				if (batchSource != null)
				{
					CollectionPreparationProgress.Report(LanguageManager.Format("Collections.Workflow.ExtractingPluginDependencies",
						"Reading selected plugins together: {0} ({1:P0})", recipe.Member.DisplayName, 0f), true);
					Action<string> report = CollectionPreparationProgress.CaptureStatusReporter();
					batchSource.CopyFilesTo(destinations, cancellationToken, percent => report?.Invoke(LanguageManager.Format(
						"Collections.Workflow.ExtractingPluginDependencies", "Reading selected plugins together: {0} ({1:P0})", recipe.Member.DisplayName, percent)));
				}
				else
				{
					foreach (KeyValuePair<string, string> file in destinations)
					{
						cancellationToken.ThrowIfCancellationRequested();
						CollectionPreparationProgress.Report(LanguageManager.Format("Collections.Workflow.ReadingPluginDependencies",
							"Reading plugin requirements: {0} — {1}", recipe.Member.DisplayName, Path.GetFileName(file.Value)), true);
						using (FileStream source = mod.GetFileStream(file.Key))
						using (FileStream destination = File.Create(file.Value)) source.CopyTo(destination);
					}
				}
				if (!StringComparer.Ordinal.Equals(stamp, ArchiveStamp(mod)))
					throw new InvalidDataException("The mod archive changed while reading plugin dependencies.");
				foreach (InstallModFileOperation file in pending)
				{
					cancellationToken.ThrowIfCancellationRequested();
					string name = Path.GetFileName(gameMode.GetModFormatAdjustedPath(mod.Format, file.DestinationPath, false));
					string cacheKey = stamp + "|" + file.SourcePath;
					IList<string> masters;
					lock (_cacheLock) _masterCache.TryGetValue(cacheKey, out masters);
					if (masters == null)
					{
						CollectionPreparationProgress.Report(LanguageManager.Format("Collections.Workflow.ReadingPluginDependencies",
							"Reading plugin requirements: {0} — {1}", recipe.Member.DisplayName, name), true);
						Plugin plugin = factory.CreatePlugin(destinations[file.SourcePath]);
						masters = plugin == null || plugin.Masters == null ? new List<string>() : plugin.Masters.ToList();
						lock (_cacheLock)
						{
							if (_masterCache.Count >= 2048) _masterCache.Clear();
							_masterCache[cacheKey] = masters;
						}
					}
					dependencies[name] = masters;
				}
			}
			finally
			{
				try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true); }
				catch (IOException ex) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
				catch (UnauthorizedAccessException ex) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
			}
		}

		/// <summary>Walks the transitive master closure of selected active plugins, including already installed collection members.</summary>
		internal HashSet<string> GetRequiredPlugins(ResolvedCollectionPlan plan, CollectionMemberMatchSet matches,
			CollectionNativeStateIndex state, IDictionary<string, IList<string>> incomingDependencies,
			IEnumerable<string> activeIncomingPlugins, out HashSet<string> provided)
		{
			var dependencies = state.Plugins.Values.ToDictionary(x => Path.GetFileName(x.FileName), x => (IList<string>)x.Masters.ToList(), StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, IList<string>> incoming in incomingDependencies) dependencies[incoming.Key] = incoming.Value;
			provided = new HashSet<string>(incomingDependencies.Keys, StringComparer.OrdinalIgnoreCase);
			foreach (CollectionMemberMatchResult match in matches.Members.Where(x => x.MatchedNativeMod != null))
			{
				System.Collections.ObjectModel.ReadOnlyCollection<CollectionNativeFileState> files;
				if (state.FilesByOwnerKey.TryGetValue(match.MatchedNativeMod.Identity.NativeModKey, out files))
					foreach (CollectionNativeFileState file in files)
						if (dependencies.ContainsKey(Path.GetFileName(file.Target.RelativePath))) provided.Add(Path.GetFileName(file.Target.RelativePath));
			}
			HashSet<string> providedNames = provided;
			NormalizedCollectionManifest manifest = plan.CapabilityReport.Manifest;
			var active = manifest.HasPluginStateSection
				? new HashSet<string>(manifest.PluginStates.Where(x => x.Enabled && providedNames.Contains(x.PluginName)).Select(x => x.PluginName), StringComparer.OrdinalIgnoreCase)
				: new HashSet<string>(activeIncomingPlugins.Concat(state.Plugins.Values.Where(x => x.Active && providedNames.Contains(Path.GetFileName(x.FileName))).Select(x => Path.GetFileName(x.FileName))), StringComparer.OrdinalIgnoreCase);
			if (plan.Policy.EnableUnspecifiedPlugins)
			{
				var explicitlyDisabled = new HashSet<string>(manifest.PluginStates.Where(x => !x.Enabled).Select(x => x.PluginName), StringComparer.OrdinalIgnoreCase);
				active.UnionWith(providedNames.Where(x => !explicitlyDisabled.Contains(x)));
			}
			var pending = new Queue<string>(active);
			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			while (pending.Count > 0)
			{
				string name = pending.Dequeue();
				IList<string> masters;
				if (!visited.Add(name) || !dependencies.TryGetValue(name, out masters)) continue;
				foreach (string master in masters)
					if (!String.IsNullOrWhiteSpace(master) && required.Add(master)) pending.Enqueue(master);
			}
			return required;
		}

		/// <summary>Matches required plugin names to optional collection members using curator file lists or exact Nexus archive identities.</summary>
		internal CollectionEffectiveSelection IncludePluginProviders(CollectionCapabilityReport capability, ISet<string> requiredPlugins,
			ModManager modManager, CancellationToken cancellationToken, bool useRemoteInventory = false)
		{
			var selections = new Dictionary<CollectionMemberKey, CollectionMemberSelection>();
			List<IMod> managedMods = modManager.ManagedMods.ToList();
			var remaining = new HashSet<string>(requiredPlugins, StringComparer.OrdinalIgnoreCase);
			foreach (NormalizedCollectionMember member in capability.Manifest.Members.OrderByDescending(x => x.SourceOrdinal))
			{
				if (remaining.Count == 0) break;
				if (member.IsSelected || member.Requirement != CollectionMemberRequirement.Optional || !member.IdentityResolution.IsResolved) continue;
				cancellationToken.ThrowIfCancellationRequested();
				HashSet<string> names;
				if (member.HasVortexFileList)
					names = new HashSet<string>(member.VortexFileList.Items.Select(x => Path.GetFileName(x.DestinationPath)), StringComparer.OrdinalIgnoreCase);
				else
				{
					string domain; long modId; long fileId;
					if (!NexusCollectionModFileArtifactIdentity.TryParse(member.Artifact, out domain, out modId, out fileId) ||
						modId > Int32.MaxValue || fileId > Int32.MaxValue || modManager.ModRepository == null ||
						!StringComparer.OrdinalIgnoreCase.Equals(domain, modManager.ModRepository.GameDomainName)) continue;
					IMod mod = managedMods.FirstOrDefault(x => x.Id == modId.ToString(CultureInfo.InvariantCulture) && x.DownloadId == fileId.ToString(CultureInfo.InvariantCulture) && File.Exists(CollectionArchiveContentMatcher.GetManagedArchivePath(x)));
					if (mod == null && !useRemoteInventory) continue;
					string stamp = mod == null ? "nexus-preview:" + member.Artifact.StableId : ArchiveStamp(mod);
					lock (_cacheLock) _inventoryCache.TryGetValue(stamp, out names);
					if (names == null)
					{
						IList<string> paths = mod == null
							? (modManager.ModRepository as NexusModsApiRepository)?.GetCollectionFileContentPreview(checked((int)modId), checked((int)fileId), cancellationToken)
							: mod.GetFileList();
						if (paths == null) continue;
						names = new HashSet<string>(paths.Select(x => Path.GetFileName(x.Replace('/', '\\'))), StringComparer.OrdinalIgnoreCase);
						lock (_cacheLock)
						{
							if (_inventoryCache.Count >= 256) _inventoryCache.Clear();
							_inventoryCache[stamp] = names;
						}
					}
				}
				if (!names.Overlaps(remaining)) continue;
				selections[member.IdentityResolution.Key] = CollectionMemberSelection.Selected;
				remaining.ExceptWith(names);
			}
			return new CollectionEffectiveSelectionBuilder().Build(capability,
				selections.Select(x => new CollectionOptionalMemberSelection(x.Key, x.Value)), true);
		}

		/// <summary>Invalidates read-only dependency caches when the source archive changes.</summary>
		private static string ArchiveStamp(IMod mod)
		{
			var file = new FileInfo(CollectionArchiveContentMatcher.GetManagedArchivePath(mod));
			return file.FullName + "|" + file.Length.ToString(CultureInfo.InvariantCulture) + "|" + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
		}
	}
}
