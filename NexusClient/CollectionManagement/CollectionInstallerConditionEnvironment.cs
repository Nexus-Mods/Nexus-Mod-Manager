using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Shows a fresh Collection wizard the live baseline plus exact outputs already prepared in install order.</summary>
	internal sealed class CollectionInstallerConditionEnvironment : IModInstallationConditionEnvironment
	{
		private readonly Dictionary<string, bool> _plugins;
		private readonly HashSet<ModDeploymentTarget> _files;
		private readonly IGameMode _gameMode;
		private readonly CollectionNativeStateCoverage _coverage;

		/// <summary>Projects only proven file outputs; manifest plugin names alone never invent installed dependencies.</summary>
		internal CollectionInstallerConditionEnvironment(CollectionNativeStateIndex state, IGameMode gameMode,
			NormalizedCollectionManifest manifest, IEnumerable<PreparedCollectionNativeRecipe> previousRecipes, bool enableUnspecifiedPlugins = false)
		{
			_gameMode = gameMode;
			_coverage = state.PluginCoverage;
			_plugins = state.Plugins.Values.ToDictionary(x => x.FileName, x => x.Active, StringComparer.OrdinalIgnoreCase);
			_files = new HashSet<ModDeploymentTarget>();
			foreach (PreparedCollectionNativeRecipe recipe in previousRecipes)
				foreach (CollectionPlannedFileEffect file in recipe.EffectPreview.Files)
				{
					_files.Add(file.Target);
					string relative = file.Target.RelativePath;
					if (!gameMode.UsesPlugins || gameMode.PluginExtensions == null || String.IsNullOrWhiteSpace(gameMode.PluginDirectory) ||
						!gameMode.PluginExtensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase)) continue;
					string physical = ModDeploymentTargetResolver.GetPhysicalPath(gameMode, file.Target);
					if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(physical).TrimEnd('\\', '/'),
						Path.GetFullPath(gameMode.PluginDirectory).TrimEnd('\\', '/'))) continue;
					string plugin = Path.GetFileName(relative);
					CollectionDesiredPluginState desired = manifest.PluginStates.FirstOrDefault(x =>
						StringComparer.OrdinalIgnoreCase.Equals(x.PluginName, plugin));
					_plugins[plugin] = desired != null ? desired.Enabled : enableUnspecifiedPlugins || !manifest.HasPluginStateSection || manifest.PluginStatesArePartial;
				}
		}

		public IReadOnlyList<string> RegisteredPlugins { get { return _plugins.Keys.ToList().AsReadOnly(); } }
		public IReadOnlyList<string> ActivePlugins { get { return _plugins.Where(x => x.Value).Select(x => x.Key).ToList().AsReadOnly(); } }

		/// <summary>Resolves a plugin against proven earlier outputs and the captured native plugin inventory.</summary>
		public bool TryGetPluginRegistered(string pluginPath, out bool registered)
		{
			registered = _plugins.ContainsKey(Path.GetFileName((pluginPath ?? String.Empty).Replace('/', '\\')));
			return registered || _coverage == CollectionNativeStateCoverage.Complete || _coverage == CollectionNativeStateCoverage.NotApplicable;
		}

		/// <summary>Resolves the effective activation state without changing the actual plugin manager.</summary>
		public bool TryGetPluginActive(string pluginPath, out bool active)
		{
			if (_plugins.TryGetValue(Path.GetFileName((pluginPath ?? String.Empty).Replace('/', '\\')), out active)) return true;
			return _coverage == CollectionNativeStateCoverage.Complete || _coverage == CollectionNativeStateCoverage.NotApplicable;
		}

		/// <summary>Reads canonical file visibility while making prepared files visible before deployment.</summary>
		public bool TryGetDataFileExists(ModDeploymentRoot root, string relativePath, out bool exists)
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(root, relativePath);
			exists = _files.Contains(target) || File.Exists(ModDeploymentTargetResolver.GetPhysicalPath(_gameMode, target));
			return true;
		}

		/// <summary>Leaves INI facts unproven because XML option capture consumes plugin dependencies only.</summary>
		public bool TryGetIniString(string settingsFileName, string section, string key, out string value)
		{
			value = null;
			return false;
		}
	}
}
