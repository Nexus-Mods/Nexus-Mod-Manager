using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes whether the normalizer observed the complete collection-member set.
	/// </summary>
	public enum CollectionManifestMemberSetCompleteness
	{
		Unknown = 0,
		Complete = 1,
		Incomplete = 2
	}

	/// <summary>One normalized Vortex Collection plugin-state declaration.</summary>
	/// <remarks>
	/// Vortex applies these declarations only to plugin files contributed by the effective installed Collection member closure.
	/// A contributed plugin absent from the enabled declarations is desired inactive. This record preserves the exact source name/state;
	/// it does not independently claim ownership of a native plugin.
	/// </remarks>
	public sealed class CollectionDesiredPluginState : IEquatable<CollectionDesiredPluginState>
	{
		public CollectionDesiredPluginState(string pluginName, bool enabled)
		{
			PluginName = CollectionDomainValidation.RequireDisplayValue(pluginName, nameof(pluginName));
			Enabled = enabled;
		}

		public string PluginName { get; }
		public bool Enabled { get; }

		public bool Equals(CollectionDesiredPluginState other)
		{
			return other != null && StringComparer.OrdinalIgnoreCase.Equals(PluginName, other.PluginName) && Enabled == other.Enabled;
		}

		public override bool Equals(object obj) { return Equals(obj as CollectionDesiredPluginState); }
		public override int GetHashCode()
		{
			unchecked { return (StringComparer.OrdinalIgnoreCase.GetHashCode(PluginName) * 397) ^ Enabled.GetHashCode(); }
		}
	}

	/// <summary>Curator-authored setup guidance retained from the exact Collection manifest.</summary>
	/// <remarks>
	/// These values are advisory. They never create/switch an NMM profile or silently gate native mutation, but remain visible
	/// during preview/review so Vortex setup intent is not discarded while translating the Collection into NMM-native effects.
	/// </remarks>
	public sealed class CollectionSetupGuidance
	{
		private readonly ReadOnlyCollection<string> _gameVersions;

		/// <summary>Creates immutable setup guidance exactly as represented by the characterized manifest fields.</summary>
		public CollectionSetupGuidance(bool recommendNewProfile, string installInstructions, IEnumerable<string> gameVersions)
		{
			RecommendNewProfile = recommendNewProfile;
			InstallInstructions = installInstructions;
			List<string> versions = (gameVersions ?? Enumerable.Empty<string>()).ToList();
			if (versions.Any(x => x == null))
				throw new ArgumentException("Collection setup game-version guidance cannot contain null values.", nameof(gameVersions));
			_gameVersions = new ReadOnlyCollection<string>(versions);
		}

		/// <summary>Gets whether the curator recommends a fresh Vortex profile/setup context.</summary>
		public bool RecommendNewProfile { get; }
		/// <summary>Gets the exact curator installation instructions, when supplied.</summary>
		public string InstallInstructions { get; }
		/// <summary>Gets the exact game-version strings retained from the manifest.</summary>
		public ReadOnlyCollection<string> GameVersions { get { return _gameVersions; } }
		/// <summary>Gets whether any user-visible setup guidance is present.</summary>
		public bool HasGuidance { get { return RecommendNewProfile || !String.IsNullOrWhiteSpace(InstallInstructions) || _gameVersions.Count > 0; } }
	}


	/// <summary>One safe, explicitly user-launched tool retained from a Vortex Collection manifest.</summary>
	/// <remarks>
	/// Collection tools are launcher metadata, not native installation effects. NMM never executes them automatically. The
	/// characterized subset is restricted to game-relative executables and working directories without shell/detach/UI-lifecycle
	/// behavior so an applied association can expose the tool without introducing an arbitrary command-execution phase.
	/// </remarks>
	public sealed class CollectionLaunchTool : IEquatable<CollectionLaunchTool>
	{
		private readonly ReadOnlyCollection<string> _arguments;
		private readonly ReadOnlyDictionary<string, string> _environment;

		public CollectionLaunchTool(string name, string relativeExecutablePath, IEnumerable<string> arguments,
			string relativeWorkingDirectory, IDictionary<string, string> environment)
		{
			Name = CollectionDomainValidation.RequireDisplayValue(name, nameof(name));
			RelativeExecutablePath = RequireRelativePath(relativeExecutablePath, nameof(relativeExecutablePath));
			if (!StringComparer.OrdinalIgnoreCase.Equals(System.IO.Path.GetExtension(RelativeExecutablePath), ".exe"))
				throw new ArgumentException("A characterized Collection tool executable must use the .exe extension.", nameof(relativeExecutablePath));
			RelativeWorkingDirectory = String.IsNullOrEmpty(relativeWorkingDirectory)
				? null
				: RequireRelativePath(relativeWorkingDirectory, nameof(relativeWorkingDirectory));

			List<string> copiedArguments = (arguments ?? Enumerable.Empty<string>()).ToList();
			if (copiedArguments.Any(x => x == null))
				throw new ArgumentException("Collection tool arguments cannot contain null values.", nameof(arguments));
			_arguments = new ReadOnlyCollection<string>(copiedArguments);

			var copiedEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, string> pair in environment ?? new Dictionary<string, string>())
			{
				if (String.IsNullOrWhiteSpace(pair.Key) || pair.Key.IndexOf('=') >= 0 || pair.Key.IndexOf('\0') >= 0 || pair.Value == null)
					throw new ArgumentException("Collection tool environment entries must use non-empty Windows variable names and string values.", nameof(environment));
				copiedEnvironment.Add(pair.Key, pair.Value);
			}
			_environment = new ReadOnlyDictionary<string, string>(copiedEnvironment);
		}

		public string Name { get; }
		public string RelativeExecutablePath { get; }
		public ReadOnlyCollection<string> Arguments { get { return _arguments; } }
		public string RelativeWorkingDirectory { get; }
		public IReadOnlyDictionary<string, string> Environment { get { return _environment; } }

		public bool Equals(CollectionLaunchTool other)
		{
			if (other == null || !StringComparer.Ordinal.Equals(Name, other.Name) ||
				!StringComparer.OrdinalIgnoreCase.Equals(RelativeExecutablePath, other.RelativeExecutablePath) ||
				!StringComparer.OrdinalIgnoreCase.Equals(RelativeWorkingDirectory ?? String.Empty, other.RelativeWorkingDirectory ?? String.Empty) ||
				_arguments.Count != other._arguments.Count || _environment.Count != other._environment.Count)
				return false;
			for (int index = 0; index < _arguments.Count; index++)
				if (!StringComparer.Ordinal.Equals(_arguments[index], other._arguments[index])) return false;
			foreach (KeyValuePair<string, string> pair in _environment)
			{
				string otherValue;
				if (!other._environment.TryGetValue(pair.Key, out otherValue) || !StringComparer.Ordinal.Equals(pair.Value, otherValue)) return false;
			}
			return true;
		}

		public override bool Equals(object obj) { return Equals(obj as CollectionLaunchTool); }
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = StringComparer.Ordinal.GetHashCode(Name);
				hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(RelativeExecutablePath);
				return hash;
			}
		}

		private static string RequireRelativePath(string value, string parameterName)
		{
			if (String.IsNullOrWhiteSpace(value) || !StringComparer.Ordinal.Equals(value, value.Trim()))
				throw new ArgumentException("A canonical relative Collection tool path is required.", parameterName);
			string normalized = value.Replace('/', '\\');
			if (normalized[0] == '\\' || System.IO.Path.IsPathRooted(value) || (normalized.Length > 1 && normalized[1] == ':'))
				throw new ArgumentException("Collection tool paths must be relative to the game root.", parameterName);
			foreach (string part in normalized.Split('\\'))
			{
				if (String.IsNullOrEmpty(part) || part == "." || part == ".." || part.IndexOf(':') >= 0 ||
					!StringComparer.Ordinal.Equals(part, part.TrimEnd(' ', '.')) || part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
					IsReservedDeviceName(part))
					throw new ArgumentException("Collection tool paths must be canonical safe Windows-relative paths.", parameterName);
			}
			return normalized;
		}

		private static bool IsReservedDeviceName(string part)
		{
			int extensionSeparator = part.IndexOf('.');
			string name = (extensionSeparator < 0 ? part : part.Substring(0, extensionSeparator)).ToUpperInvariant();
			if (name == "CON" || name == "PRN" || name == "AUX" || name == "NUL")
				return true;
			if (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)))
				return name[3] >= '1' && name[3] <= '9';
			return false;
		}
	}

	/// <summary>One characterized Vortex plugin rule requiring one plugin to load after another.</summary>
	public sealed class CollectionPluginRelativeOrderRule : IEquatable<CollectionPluginRelativeOrderRule>
	{
		public CollectionPluginRelativeOrderRule(string pluginName, string afterPluginName)
		{
			PluginName = RequirePluginFileName(pluginName, nameof(pluginName));
			AfterPluginName = RequirePluginFileName(afterPluginName, nameof(afterPluginName));
			if (StringComparer.OrdinalIgnoreCase.Equals(PluginName, AfterPluginName))
				throw new ArgumentException("A relative plugin-order rule cannot reference the same plugin on both sides.", nameof(afterPluginName));
		}

		/// <summary>Gets the plugin which must load later.</summary>
		public string PluginName { get; }

		/// <summary>Gets the plugin which must load before <see cref="PluginName"/>.</summary>
		public string AfterPluginName { get; }

		public bool Equals(CollectionPluginRelativeOrderRule other)
		{
			return other != null && StringComparer.OrdinalIgnoreCase.Equals(PluginName, other.PluginName) &&
				StringComparer.OrdinalIgnoreCase.Equals(AfterPluginName, other.AfterPluginName);
		}

		public override bool Equals(object obj) { return Equals(obj as CollectionPluginRelativeOrderRule); }
		public override int GetHashCode()
		{
			unchecked
			{
				return (StringComparer.OrdinalIgnoreCase.GetHashCode(PluginName) * 397) ^
					StringComparer.OrdinalIgnoreCase.GetHashCode(AfterPluginName);
			}
		}

		private static string RequirePluginFileName(string value, string parameterName)
		{
			if (String.IsNullOrWhiteSpace(value) || !StringComparer.Ordinal.Equals(value, value.Trim()) ||
				value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0)
				throw new ArgumentException("A plugin rule must use one non-empty plugin file name without path separators.", parameterName);
			return value;
		}
	}

	/// <summary>
	/// Immutable normalized view of one exact collection revision manifest.
	/// </summary>
	/// <remarks>
	/// This model is intentionally not a native installation plan. It preserves incomplete or ambiguous source data for
	/// preview/reporting while preventing list position, display name or archive filename from becoming implicit identity.
	/// </remarks>
	public sealed class NormalizedCollectionManifest
	{
		private readonly ReadOnlyCollection<NormalizedCollectionMember> _members;
		private readonly ReadOnlyCollection<CollectionMemberDependency> _dependencies;
		private readonly ReadOnlyCollection<CollectionFilePriorityRule> _filePriorityRules;
		private readonly ReadOnlyCollection<CollectionExternalFilePriorityRule> _externalFilePriorityRules;
		private readonly ReadOnlyCollection<CollectionConflictConstraint> _conflictConstraints;
		private readonly ReadOnlyCollection<CollectionDesiredPluginState> _pluginStates;
		private readonly ReadOnlyCollection<CollectionPluginRelativeOrderRule> _pluginRelativeOrderRules;
		private readonly ReadOnlyCollection<CollectionLaunchTool> _launchTools;

		/// <summary>
		/// Creates an immutable normalized collection manifest snapshot.
		/// </summary>
		public NormalizedCollectionManifest(
			CollectionRevisionIdentity revision,
			CollectionManifestSourceSnapshot source,
			CollectionManifestMemberSetCompleteness memberSetCompleteness,
			string incompletenessReason,
			IEnumerable<NormalizedCollectionMember> members)
			: this(revision, source, memberSetCompleteness, incompletenessReason, members, null, null, null, null, null)
		{
		}

		/// <summary>
		/// Creates an immutable normalized collection manifest snapshot with characterized member dependencies.
		/// </summary>
		public NormalizedCollectionManifest(
			CollectionRevisionIdentity revision,
			CollectionManifestSourceSnapshot source,
			CollectionManifestMemberSetCompleteness memberSetCompleteness,
			string incompletenessReason,
			IEnumerable<NormalizedCollectionMember> members,
			IEnumerable<CollectionMemberDependency> dependencies)
			: this(revision, source, memberSetCompleteness, incompletenessReason, members, dependencies, null, null, null, null)
		{
		}

		/// <summary>
		/// Creates an immutable normalized collection manifest with characterized prerequisite and file-priority relationships.
		/// </summary>
		public NormalizedCollectionManifest(
			CollectionRevisionIdentity revision,
			CollectionManifestSourceSnapshot source,
			CollectionManifestMemberSetCompleteness memberSetCompleteness,
			string incompletenessReason,
			IEnumerable<NormalizedCollectionMember> members,
			IEnumerable<CollectionMemberDependency> dependencies,
			IEnumerable<CollectionFilePriorityRule> filePriorityRules)
			: this(revision, source, memberSetCompleteness, incompletenessReason, members, dependencies, filePriorityRules, null, null, null)
		{
		}

		/// <summary>
		/// Creates an immutable normalized collection manifest with characterized member relationships and Collection plugin-state declarations.
		/// </summary>
		public NormalizedCollectionManifest(
			CollectionRevisionIdentity revision,
			CollectionManifestSourceSnapshot source,
			CollectionManifestMemberSetCompleteness memberSetCompleteness,
			string incompletenessReason,
			IEnumerable<NormalizedCollectionMember> members,
			IEnumerable<CollectionMemberDependency> dependencies,
			IEnumerable<CollectionFilePriorityRule> filePriorityRules,
			IEnumerable<CollectionDesiredPluginState> pluginStates)
			: this(revision, source, memberSetCompleteness, incompletenessReason, members, dependencies, filePriorityRules, pluginStates, null, null)
		{
		}

		/// <summary>
		/// Creates an immutable normalized collection manifest with characterized relationships, plugin state and conflict constraints.
		/// </summary>
		public NormalizedCollectionManifest(
			CollectionRevisionIdentity revision,
			CollectionManifestSourceSnapshot source,
			CollectionManifestMemberSetCompleteness memberSetCompleteness,
			string incompletenessReason,
			IEnumerable<NormalizedCollectionMember> members,
			IEnumerable<CollectionMemberDependency> dependencies,
			IEnumerable<CollectionFilePriorityRule> filePriorityRules,
			IEnumerable<CollectionDesiredPluginState> pluginStates,
			IEnumerable<CollectionConflictConstraint> conflictConstraints)
			: this(revision, source, memberSetCompleteness, incompletenessReason, members, dependencies, filePriorityRules, pluginStates, conflictConstraints, null)
		{
		}

		/// <summary>
		/// Creates an immutable normalized collection manifest including characterized external before/after endpoints.
		/// </summary>
		public NormalizedCollectionManifest(
			CollectionRevisionIdentity revision,
			CollectionManifestSourceSnapshot source,
			CollectionManifestMemberSetCompleteness memberSetCompleteness,
			string incompletenessReason,
			IEnumerable<NormalizedCollectionMember> members,
			IEnumerable<CollectionMemberDependency> dependencies,
			IEnumerable<CollectionFilePriorityRule> filePriorityRules,
			IEnumerable<CollectionDesiredPluginState> pluginStates,
			IEnumerable<CollectionConflictConstraint> conflictConstraints,
			IEnumerable<CollectionExternalFilePriorityRule> externalFilePriorityRules,
			IEnumerable<CollectionPluginRelativeOrderRule> pluginRelativeOrderRules = null,
			CollectionSetupGuidance setupGuidance = null,
			IEnumerable<CollectionLaunchTool> launchTools = null)
		{
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (source == null)
				throw new ArgumentNullException(nameof(source));
			if (!Enum.IsDefined(typeof(CollectionManifestMemberSetCompleteness), memberSetCompleteness) ||
				memberSetCompleteness == CollectionManifestMemberSetCompleteness.Unknown)
				throw new ArgumentOutOfRangeException(nameof(memberSetCompleteness));
			if (members == null)
				throw new ArgumentNullException(nameof(members));

			if (memberSetCompleteness == CollectionManifestMemberSetCompleteness.Complete)
			{
				if (incompletenessReason != null)
					throw new ArgumentException("A complete manifest cannot carry an incompleteness reason.", nameof(incompletenessReason));
			}
			else
			{
				incompletenessReason = CollectionDomainValidation.RequireDisplayValue(incompletenessReason, nameof(incompletenessReason));
			}

			List<NormalizedCollectionMember> copiedMembers = new List<NormalizedCollectionMember>();
			HashSet<CollectionMemberKey> resolvedKeys = new HashSet<CollectionMemberKey>();
			foreach (NormalizedCollectionMember member in members)
			{
				if (member == null)
					throw new ArgumentException("A normalized manifest cannot contain a null member.", nameof(members));

				if (member.IdentityResolution.IsResolved && !resolvedKeys.Add(member.IdentityResolution.Key))
					throw new ArgumentException("A normalized manifest cannot contain duplicate resolved member keys.", nameof(members));

				copiedMembers.Add(member);
			}

			List<CollectionMemberDependency> copiedDependencies = new List<CollectionMemberDependency>();
			HashSet<CollectionMemberDependency> uniqueDependencies = new HashSet<CollectionMemberDependency>();
			if (dependencies != null)
			{
				foreach (CollectionMemberDependency dependency in dependencies)
				{
					if (dependency == null)
						throw new ArgumentException("A normalized manifest cannot contain a null dependency.", nameof(dependencies));
					if (!resolvedKeys.Contains(dependency.PrerequisiteMemberKey) || !resolvedKeys.Contains(dependency.DependentMemberKey))
						throw new ArgumentException("A normalized dependency must reference resolved members in the same manifest.", nameof(dependencies));
					if (!uniqueDependencies.Add(dependency))
						throw new ArgumentException("A normalized manifest cannot contain duplicate dependency edges.", nameof(dependencies));
					copiedDependencies.Add(dependency);
				}
			}

			List<CollectionFilePriorityRule> copiedFilePriorityRules = new List<CollectionFilePriorityRule>();
			HashSet<CollectionFilePriorityRule> uniqueFilePriorityRules = new HashSet<CollectionFilePriorityRule>();
			if (filePriorityRules != null)
			{
				foreach (CollectionFilePriorityRule rule in filePriorityRules)
				{
					if (rule == null)
						throw new ArgumentException("A normalized manifest cannot contain a null file-priority rule.", nameof(filePriorityRules));
					if (!resolvedKeys.Contains(rule.LowerPriorityMemberKey) || !resolvedKeys.Contains(rule.HigherPriorityMemberKey))
						throw new ArgumentException("A normalized file-priority rule must reference resolved members in the same manifest.", nameof(filePriorityRules));
					if (!uniqueFilePriorityRules.Add(rule))
						throw new ArgumentException("A normalized manifest cannot contain duplicate file-priority rules.", nameof(filePriorityRules));
					copiedFilePriorityRules.Add(rule);
				}
			}

			Revision = revision;
			Source = source;
			MemberSetCompleteness = memberSetCompleteness;
			IncompletenessReason = incompletenessReason;
			_members = new ReadOnlyCollection<NormalizedCollectionMember>(copiedMembers);
			_dependencies = new ReadOnlyCollection<CollectionMemberDependency>(copiedDependencies);
			_filePriorityRules = new ReadOnlyCollection<CollectionFilePriorityRule>(copiedFilePriorityRules);

			List<CollectionExternalFilePriorityRule> copiedExternalFilePriorityRules = new List<CollectionExternalFilePriorityRule>();
			HashSet<CollectionExternalFilePriorityRule> uniqueExternalFilePriorityRules = new HashSet<CollectionExternalFilePriorityRule>();
			if (externalFilePriorityRules != null)
			{
				foreach (CollectionExternalFilePriorityRule rule in externalFilePriorityRules)
				{
					if (rule == null) throw new ArgumentException("A normalized manifest cannot contain a null external file-priority rule.", nameof(externalFilePriorityRules));
					if (!resolvedKeys.Contains(rule.MemberKey)) throw new ArgumentException("An external file-priority rule must reference a resolved member in the same manifest.", nameof(externalFilePriorityRules));
					if (!uniqueExternalFilePriorityRules.Add(rule)) throw new ArgumentException("A normalized manifest cannot contain duplicate external file-priority rules.", nameof(externalFilePriorityRules));
					copiedExternalFilePriorityRules.Add(rule);
				}
			}
			_externalFilePriorityRules = new ReadOnlyCollection<CollectionExternalFilePriorityRule>(copiedExternalFilePriorityRules);

			List<CollectionConflictConstraint> copiedConflictConstraints = new List<CollectionConflictConstraint>();
			HashSet<CollectionConflictConstraint> uniqueConflictConstraints = new HashSet<CollectionConflictConstraint>();
			if (conflictConstraints != null)
			{
				foreach (CollectionConflictConstraint constraint in conflictConstraints)
				{
					if (constraint == null)
						throw new ArgumentException("A normalized manifest cannot contain a null conflict constraint.", nameof(conflictConstraints));
					if (!resolvedKeys.Contains(constraint.SourceMemberKey) || constraint.MatchingMemberKeys.Any(x => !resolvedKeys.Contains(x)))
						throw new ArgumentException("A normalized conflict constraint must reference only resolved members in the same manifest.", nameof(conflictConstraints));
					if (!uniqueConflictConstraints.Add(constraint))
						throw new ArgumentException("A normalized manifest cannot contain duplicate conflict constraints.", nameof(conflictConstraints));
					copiedConflictConstraints.Add(constraint);
				}
			}
			_conflictConstraints = new ReadOnlyCollection<CollectionConflictConstraint>(copiedConflictConstraints);

			HasPluginStateSection = pluginStates != null;
			List<CollectionDesiredPluginState> copiedPluginStates = new List<CollectionDesiredPluginState>();
			HashSet<string> pluginNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (pluginStates != null)
			{
				foreach (CollectionDesiredPluginState pluginState in pluginStates)
				{
					if (pluginState == null)
						throw new ArgumentException("A normalized manifest cannot contain a null plugin-state declaration.", nameof(pluginStates));
					if (!pluginNames.Add(pluginState.PluginName))
						throw new ArgumentException("A normalized manifest cannot contain duplicate plugin-state declarations.", nameof(pluginStates));
					copiedPluginStates.Add(pluginState);
				}
			}
			_pluginStates = new ReadOnlyCollection<CollectionDesiredPluginState>(copiedPluginStates);

			List<CollectionPluginRelativeOrderRule> copiedPluginRelativeOrderRules = new List<CollectionPluginRelativeOrderRule>();
			HashSet<CollectionPluginRelativeOrderRule> uniquePluginRelativeOrderRules = new HashSet<CollectionPluginRelativeOrderRule>();
			if (pluginRelativeOrderRules != null)
			{
				foreach (CollectionPluginRelativeOrderRule rule in pluginRelativeOrderRules)
				{
					if (rule == null)
						throw new ArgumentException("A normalized manifest cannot contain a null plugin relative-order rule.", nameof(pluginRelativeOrderRules));
					if (!uniquePluginRelativeOrderRules.Add(rule))
						throw new ArgumentException("A normalized manifest cannot contain duplicate plugin relative-order rules.", nameof(pluginRelativeOrderRules));
					copiedPluginRelativeOrderRules.Add(rule);
				}
			}
			_pluginRelativeOrderRules = new ReadOnlyCollection<CollectionPluginRelativeOrderRule>(copiedPluginRelativeOrderRules);
			SetupGuidance = setupGuidance ?? new CollectionSetupGuidance(false, null, null);

			List<CollectionLaunchTool> copiedLaunchTools = new List<CollectionLaunchTool>();
			HashSet<CollectionLaunchTool> uniqueLaunchTools = new HashSet<CollectionLaunchTool>();
			foreach (CollectionLaunchTool tool in launchTools ?? Enumerable.Empty<CollectionLaunchTool>())
			{
				if (tool == null) throw new ArgumentException("A normalized manifest cannot contain a null Collection tool.", nameof(launchTools));
				if (!uniqueLaunchTools.Add(tool)) throw new ArgumentException("A normalized manifest cannot contain duplicate Collection tools.", nameof(launchTools));
				copiedLaunchTools.Add(tool);
			}
			_launchTools = new ReadOnlyCollection<CollectionLaunchTool>(copiedLaunchTools);
		}

		/// <summary>
		/// Gets the exact revision represented by this normalized source snapshot.
		/// </summary>
		public CollectionRevisionIdentity Revision { get; }

		/// <summary>
		/// Gets the exact raw-content/schema/normalizer provenance for this normalized model.
		/// </summary>
		public CollectionManifestSourceSnapshot Source { get; }

		/// <summary>
		/// Gets whether the normalizer received the complete revision member set.
		/// </summary>
		public CollectionManifestMemberSetCompleteness MemberSetCompleteness { get; }

		/// <summary>
		/// Gets the precise reason the member set is incomplete, or null for a complete set.
		/// </summary>
		public string IncompletenessReason { get; }

		/// <summary>
		/// Gets the immutable normalized members in source presentation order.
		/// </summary>
		/// <remarks>
		/// Presentation order is preserved only for diagnostics/UI. Member identity comes from <see cref="CollectionMemberKey"/>.
		/// </remarks>
		public ReadOnlyCollection<NormalizedCollectionMember> Members
		{
			get { return _members; }
		}

		/// <summary>
		/// Gets characterized member-prerequisite edges. Uncharacterized raw rule systems are not silently translated here.
		/// </summary>
		public ReadOnlyCollection<CollectionMemberDependency> Dependencies
		{
			get { return _dependencies; }
		}

		/// <summary>
		/// Gets exact characterized lower-to-higher file-priority relationships between members.
		/// </summary>
		public ReadOnlyCollection<CollectionFilePriorityRule> FilePriorityRules
		{
			get { return _filePriorityRules; }
		}

		/// <summary>Gets characterized before/after rules whose other endpoint is outside the retained Collection member set.</summary>
		public ReadOnlyCollection<CollectionExternalFilePriorityRule> ExternalFilePriorityRules
		{
			get { return _externalFilePriorityRules; }
		}

		/// <summary>Gets characterized Vortex conflict constraints. These are compatibility checks, never file-priority rules.</summary>
		public ReadOnlyCollection<CollectionConflictConstraint> ConflictConstraints
		{
			get { return _conflictConstraints; }
		}

		/// <summary>
		/// Gets Collection plugin-state declarations retained from the manifest. They are interpreted only against plugin files
		/// contributed by the effective selected member closure.
		/// </summary>
		public ReadOnlyCollection<CollectionDesiredPluginState> PluginStates
		{
			get { return _pluginStates; }
		}

		/// <summary>Gets characterized Vortex plugin `after` constraints as stable plugin-name relationships.</summary>
		public ReadOnlyCollection<CollectionPluginRelativeOrderRule> PluginRelativeOrderRules
		{
			get { return _pluginRelativeOrderRules; }
		}

		/// <summary>Gets whether collection.json explicitly supplied the Vortex plugins array, including an intentionally empty array.</summary>
		public bool HasPluginStateSection { get; }

		/// <summary>Gets curator-authored, non-mutating setup guidance retained from collection.json.</summary>
		public CollectionSetupGuidance SetupGuidance { get; }

		/// <summary>Gets safe, explicitly user-launched Collection tools retained from collection.json.</summary>
		public ReadOnlyCollection<CollectionLaunchTool> LaunchTools { get { return _launchTools; } }

		/// <summary>
		/// Gets whether the source member set is known to be complete.
		/// </summary>
		public bool IsMemberSetComplete
		{
			get { return MemberSetCompleteness == CollectionManifestMemberSetCompleteness.Complete; }
		}
	}
}
