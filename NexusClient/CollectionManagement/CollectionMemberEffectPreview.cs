using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Classifies a planning limitation while translating native recipe operations into C6.4 effects.</summary>
	public enum CollectionEffectPreviewIssueKind
	{
		Unknown = 0,
		UnboundedBasicInstall = 1,
		LegacyPluginIndexOrdering = 2,
		UnsupportedNativeOperation = 3
	}

	/// <summary>One deterministic limitation of an otherwise read-only member-effect preview.</summary>
	public sealed class CollectionEffectPreviewIssue
	{
		/// <summary>Creates one effect-preview limitation.</summary>
		public CollectionEffectPreviewIssue(CollectionEffectPreviewIssueKind kind, string operationType, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionEffectPreviewIssueKind), kind) || kind == CollectionEffectPreviewIssueKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			OperationType = operationType ?? String.Empty;
			Message = message ?? String.Empty;
		}

		public CollectionEffectPreviewIssueKind Kind { get; }
		public string OperationType { get; }
		public string Message { get; }
	}

	/// <summary>One root-aware file effect produced by a translated native recipe.</summary>
	public sealed class CollectionPlannedFileEffect
	{
		/// <summary>Creates one planned managed-file destination.</summary>
		public CollectionPlannedFileEffect(ModDeploymentTarget target)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
		}
		public ModDeploymentTarget Target { get; }
	}

	/// <summary>One planned INI edit produced by a translated native recipe.</summary>
	public sealed class CollectionPlannedIniEffect
	{
		/// <summary>Creates one exact INI key/value effect.</summary>
		public CollectionPlannedIniEffect(CollectionNativeIniKey key, string value)
		{
			Key = key ?? throw new ArgumentNullException(nameof(key));
			Value = value;
		}
		public CollectionNativeIniKey Key { get; }
		public string Value { get; }
	}

	/// <summary>One planned game-specific binary value edit produced by a translated native recipe.</summary>
	public sealed class CollectionPlannedGameValueEffect
	{
		private readonly byte[] _value;

		/// <summary>Creates one exact game-specific key/value effect.</summary>
		public CollectionPlannedGameValueEffect(string key, byte[] value)
		{
			if (String.IsNullOrWhiteSpace(key))
				throw new ArgumentException("A game-specific value key is required.", nameof(key));
			Key = key;
			_value = value == null ? null : (byte[])value.Clone();
		}
		public string Key { get; }
		public byte[] Value { get { return _value == null ? null : (byte[])_value.Clone(); } }
		internal byte[] UnsafeValue { get { return _value; } }
	}

	/// <summary>Identifies the native plugin-state effect represented by one translated operation.</summary>
	public enum CollectionPlannedPluginEffectKind
	{
		Unknown = 0,
		Activation = 1,
		AbsoluteOrderIndex = 2,
		RelativeOrder = 3
	}

	/// <summary>One stable plugin-state effect produced by translated native operations.</summary>
	public sealed class CollectionPlannedPluginEffect
	{
		private readonly ReadOnlyCollection<string> _pluginPaths;

		private CollectionPlannedPluginEffect(CollectionPlannedPluginEffectKind kind, IEnumerable<string> pluginPaths,
			bool? active, int? absoluteIndex)
		{
			if (!Enum.IsDefined(typeof(CollectionPlannedPluginEffectKind), kind) || kind == CollectionPlannedPluginEffectKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (pluginPaths == null)
				throw new ArgumentNullException(nameof(pluginPaths));
			List<string> copied = pluginPaths.Select(RequirePluginPath).ToList();
			if (copied.Count == 0)
				throw new ArgumentException("A plugin effect must identify at least one plugin path.", nameof(pluginPaths));
			Kind = kind;
			_pluginPaths = new ReadOnlyCollection<string>(copied);
			Active = active;
			AbsoluteIndex = absoluteIndex;
		}

		/// <summary>Creates one plugin activation/deactivation effect.</summary>
		public static CollectionPlannedPluginEffect Activation(string pluginPath, bool active)
		{
			return new CollectionPlannedPluginEffect(CollectionPlannedPluginEffectKind.Activation,
				new[] { pluginPath }, active, null);
		}

		/// <summary>Creates one absolute plugin-order request using a stable plugin path.</summary>
		public static CollectionPlannedPluginEffect AbsoluteOrder(string pluginPath, int index)
		{
			return new CollectionPlannedPluginEffect(CollectionPlannedPluginEffectKind.AbsoluteOrderIndex,
				new[] { pluginPath }, null, index);
		}

		/// <summary>Creates one relative-order request over stable plugin paths.</summary>
		public static CollectionPlannedPluginEffect RelativeOrder(IEnumerable<string> pluginPaths)
		{
			return new CollectionPlannedPluginEffect(CollectionPlannedPluginEffectKind.RelativeOrder,
				pluginPaths, null, null);
		}

		public CollectionPlannedPluginEffectKind Kind { get; }
		public ReadOnlyCollection<string> PluginPaths { get { return _pluginPaths; } }
		public bool? Active { get; }
		public int? AbsoluteIndex { get; }

		private static string RequirePluginPath(string path)
		{
			if (String.IsNullOrWhiteSpace(path))
				throw new ArgumentException("Plugin paths must not be empty.", nameof(path));
			return path.Replace('/', '\\');
		}
	}

	/// <summary>
	/// Immutable C6.4 preview of exact native effects for one selected member after C5 translation.
	/// </summary>
	/// <remarks>
	/// This object carries no executable handles and does not authorize mutation. Incomplete previews remain explicit so
	/// C6.4 cannot accidentally interpret an unbounded/legacy native operation as having no impact.
	/// </remarks>
	public sealed class CollectionMemberEffectPreview
	{
		private readonly ReadOnlyCollection<CollectionPlannedFileEffect> _files;
		private readonly ReadOnlyCollection<CollectionPlannedIniEffect> _iniEdits;
		private readonly ReadOnlyCollection<CollectionPlannedGameValueEffect> _gameValues;
		private readonly ReadOnlyCollection<CollectionPlannedPluginEffect> _pluginEffects;
		private readonly ReadOnlyCollection<CollectionEffectPreviewIssue> _issues;

		/// <summary>Creates one immutable member-effect preview.</summary>
		public CollectionMemberEffectPreview(CollectionMemberKey memberKey, CollectionRecipeIdentity recipeIdentity,
			ModInstallMethod installMethod, ModInstallRoot installRoot,
			IEnumerable<CollectionPlannedFileEffect> files, IEnumerable<CollectionPlannedIniEffect> iniEdits,
			IEnumerable<CollectionPlannedGameValueEffect> gameValues, IEnumerable<CollectionPlannedPluginEffect> pluginEffects,
			IEnumerable<CollectionEffectPreviewIssue> issues)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			RecipeIdentity = recipeIdentity ?? throw new ArgumentNullException(nameof(recipeIdentity));
			InstallMethod = installMethod;
			InstallRoot = installRoot;
			_files = Copy(files, nameof(files));
			_iniEdits = Copy(iniEdits, nameof(iniEdits));
			_gameValues = Copy(gameValues, nameof(gameValues));
			_pluginEffects = Copy(pluginEffects, nameof(pluginEffects));
			_issues = Copy(issues, nameof(issues));
		}

		public CollectionMemberKey MemberKey { get; }
		public CollectionRecipeIdentity RecipeIdentity { get; }
		public ModInstallMethod InstallMethod { get; }
		public ModInstallRoot InstallRoot { get; }
		public ReadOnlyCollection<CollectionPlannedFileEffect> Files { get { return _files; } }
		public ReadOnlyCollection<CollectionPlannedIniEffect> IniEdits { get { return _iniEdits; } }
		public ReadOnlyCollection<CollectionPlannedGameValueEffect> GameValues { get { return _gameValues; } }
		public ReadOnlyCollection<CollectionPlannedPluginEffect> PluginEffects { get { return _pluginEffects; } }
		public ReadOnlyCollection<CollectionEffectPreviewIssue> Issues { get { return _issues; } }
		public bool IsComplete { get { return _issues.Count == 0; } }

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName) where T : class
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("An effect preview cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}
}
