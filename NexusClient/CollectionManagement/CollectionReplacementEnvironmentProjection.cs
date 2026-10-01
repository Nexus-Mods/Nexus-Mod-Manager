using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Describes whether C8.2 can prove one projected environment fact.</summary>
	public enum CollectionReplacementEnvironmentKnowledge
	{
		Unknown = 0,
		Known = 1,
		Unprovable = 2
	}

	/// <summary>Projected visibility of one managed data/game-root file after reviewed outgoing removal.</summary>
	public sealed class CollectionReplacementFileEnvironmentState
	{
		internal CollectionReplacementFileEnvironmentState(ModDeploymentTarget target, CollectionReplacementEnvironmentKnowledge knowledge,
			bool visible, string ownerKey, string reason)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			if (!Enum.IsDefined(typeof(CollectionReplacementEnvironmentKnowledge), knowledge) || knowledge == CollectionReplacementEnvironmentKnowledge.Unknown)
				throw new ArgumentOutOfRangeException(nameof(knowledge));
			Knowledge = knowledge;
			Visible = visible;
			OwnerKey = ownerKey;
			Reason = reason ?? String.Empty;
		}

		public ModDeploymentTarget Target { get; }
		public CollectionReplacementEnvironmentKnowledge Knowledge { get; }
		public bool Visible { get; }
		public string OwnerKey { get; }
		public string Reason { get; }
	}

	/// <summary>Projected value of one native INI setting.</summary>
	public sealed class CollectionReplacementIniEnvironmentState
	{
		internal CollectionReplacementIniEnvironmentState(CollectionNativeIniKey key, CollectionReplacementEnvironmentKnowledge knowledge,
			string value, string ownerKey, string reason)
		{
			Key = key ?? throw new ArgumentNullException(nameof(key));
			if (!Enum.IsDefined(typeof(CollectionReplacementEnvironmentKnowledge), knowledge) || knowledge == CollectionReplacementEnvironmentKnowledge.Unknown)
				throw new ArgumentOutOfRangeException(nameof(knowledge));
			Knowledge = knowledge;
			Value = value;
			OwnerKey = ownerKey;
			Reason = reason ?? String.Empty;
		}

		public CollectionNativeIniKey Key { get; }
		public CollectionReplacementEnvironmentKnowledge Knowledge { get; }
		public string Value { get; }
		public string OwnerKey { get; }
		public string Reason { get; }
	}

	/// <summary>Projected registration/activation state of one plugin.</summary>
	public sealed class CollectionReplacementPluginEnvironmentState
	{
		internal CollectionReplacementPluginEnvironmentState(string fileName, bool? registered, bool? active, string reason)
		{
			if (String.IsNullOrWhiteSpace(fileName))
				throw new ArgumentException("A projected plugin requires a file name.", nameof(fileName));
			FileName = NormalizePlugin(fileName);
			Registered = registered;
			Active = active;
			Reason = reason ?? String.Empty;
		}

		public string FileName { get; }
		public bool? Registered { get; }
		public bool? Active { get; }
		public string Reason { get; }

		internal static string NormalizePlugin(string path)
		{
			string normalized = (path ?? String.Empty).Replace('/', '\\').Trim();
			return Path.GetFileName(normalized);
		}
	}

	/// <summary>
	/// Immutable C8.2 effective environment used to evaluate supported installer conditions after outgoing removal and
	/// optional already-reviewed incoming effects. It performs no native reads or writes after construction.
	/// </summary>
	public sealed class CollectionReplacementEnvironmentProjection : IModInstallationConditionEnvironment
	{
		private readonly ReadOnlyDictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState> _files;
		private readonly ReadOnlyDictionary<CollectionNativeIniKey, CollectionReplacementIniEnvironmentState> _ini;
		private readonly ReadOnlyDictionary<string, CollectionReplacementPluginEnvironmentState> _plugins;
		private readonly ReadOnlyCollection<string> _issues;
		private readonly CollectionNativeStateCoverage _pluginCoverage;
		private readonly ReadOnlyCollection<string> _registeredPlugins;
		private readonly ReadOnlyCollection<string> _activePlugins;

		internal CollectionReplacementEnvironmentProjection(CollectionReplacementDiffPlan diff,
			IDictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState> files,
			IDictionary<CollectionNativeIniKey, CollectionReplacementIniEnvironmentState> ini,
			IDictionary<string, CollectionReplacementPluginEnvironmentState> plugins, CollectionNativeStateCoverage pluginCoverage,
			IEnumerable<string> issues)
		{
			Diff = diff ?? throw new ArgumentNullException(nameof(diff));
			_files = new ReadOnlyDictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState>(
				new Dictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState>(files ?? throw new ArgumentNullException(nameof(files))));
			_ini = new ReadOnlyDictionary<CollectionNativeIniKey, CollectionReplacementIniEnvironmentState>(
				new Dictionary<CollectionNativeIniKey, CollectionReplacementIniEnvironmentState>(ini ?? throw new ArgumentNullException(nameof(ini))));
			_plugins = new ReadOnlyDictionary<string, CollectionReplacementPluginEnvironmentState>(
				new Dictionary<string, CollectionReplacementPluginEnvironmentState>(plugins ?? throw new ArgumentNullException(nameof(plugins)), StringComparer.OrdinalIgnoreCase));
			if (!Enum.IsDefined(typeof(CollectionNativeStateCoverage), pluginCoverage))
				throw new ArgumentOutOfRangeException(nameof(pluginCoverage));
			_pluginCoverage = pluginCoverage;
			_issues = new ReadOnlyCollection<string>((issues ?? Enumerable.Empty<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct().ToList());
			_registeredPlugins = new ReadOnlyCollection<string>(_plugins.Values.Where(x => x.Registered == true)
				.Select(x => x.FileName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
			_activePlugins = new ReadOnlyCollection<string>(_plugins.Values.Where(x => x.Active == true)
				.Select(x => x.FileName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
		}

		public CollectionReplacementDiffPlan Diff { get; }
		public IReadOnlyDictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState> Files { get { return _files; } }
		public IReadOnlyDictionary<CollectionNativeIniKey, CollectionReplacementIniEnvironmentState> IniValues { get { return _ini; } }
		public IReadOnlyDictionary<string, CollectionReplacementPluginEnvironmentState> Plugins { get { return _plugins; } }
		public CollectionNativeStateCoverage PluginCoverage { get { return _pluginCoverage; } }
		public ReadOnlyCollection<string> Issues { get { return _issues; } }
		public bool IsReadyForSupportedConditions { get { return _issues.Count == 0; } }
		public IReadOnlyList<string> RegisteredPlugins { get { return _registeredPlugins; } }
		public IReadOnlyList<string> ActivePlugins { get { return _activePlugins; } }

		public bool TryGetDataFileExists(ModDeploymentRoot root, string relativePath, out bool exists)
		{
			exists = false;
			if (String.IsNullOrWhiteSpace(relativePath)) return false;
			ModDeploymentTarget target;
			try { target = ModDeploymentTargetResolver.FromCanonical(root, relativePath); }
			catch (ArgumentException) { return false; }
			CollectionReplacementFileEnvironmentState state;
			if (!_files.TryGetValue(target, out state) || state.Knowledge != CollectionReplacementEnvironmentKnowledge.Known)
				return false;
			exists = state.Visible;
			return true;
		}

		public bool TryGetIniString(string settingsFileName, string section, string key, out string value)
		{
			value = null;
			var iniKey = new CollectionNativeIniKey(settingsFileName, section, key);
			CollectionReplacementIniEnvironmentState state;
			if (!_ini.TryGetValue(iniKey, out state) || state.Knowledge != CollectionReplacementEnvironmentKnowledge.Known)
				return false;
			value = state.Value;
			return true;
		}

		public bool TryGetPluginRegistered(string pluginPath, out bool registered)
		{
			registered = false;
			CollectionReplacementPluginEnvironmentState state;
			string key = CollectionReplacementPluginEnvironmentState.NormalizePlugin(pluginPath);
			if (_plugins.TryGetValue(key, out state))
			{
				if (!state.Registered.HasValue) return false;
				registered = state.Registered.Value;
				return true;
			}
			if (PluginCoverage == CollectionNativeStateCoverage.Complete ||
				PluginCoverage == CollectionNativeStateCoverage.NotApplicable)
				return true;
			return false;
		}

		public bool TryGetPluginActive(string pluginPath, out bool active)
		{
			active = false;
			CollectionReplacementPluginEnvironmentState state;
			string key = CollectionReplacementPluginEnvironmentState.NormalizePlugin(pluginPath);
			if (_plugins.TryGetValue(key, out state))
			{
				if (!state.Active.HasValue) return false;
				active = state.Active.Value;
				return true;
			}
			if (PluginCoverage == CollectionNativeStateCoverage.Complete ||
				PluginCoverage == CollectionNativeStateCoverage.NotApplicable)
				return true;
			return false;
		}
	}
}
