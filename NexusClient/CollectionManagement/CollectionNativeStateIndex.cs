using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes whether one state domain was authoritatively observed for a native-state index.
	/// </summary>
	public enum CollectionNativeStateCoverage
	{
		Unavailable = 0,
		Complete = 1,
		NotApplicable = 2
	}

	/// <summary>
	/// Identifies the kind of a native ownership entry without treating original or unresolved entries as real mods.
	/// </summary>
	public enum CollectionNativeOwnerKind
	{
		NativeMod = 1,
		OriginalValue = 2,
		Unresolved = 3
	}

	/// <summary>
	/// Classifies a non-fatal limitation observed while building the read-only native-state index.
	/// </summary>
	public enum CollectionNativeStateIssueKind
	{
		Unknown = 0,
		UnresolvedOwner = 1,
		PhysicalRootUnavailable = 2,
		PluginStateUnavailable = 3,
		AssociationStateUnavailable = 4,
		AmbiguousIniIdentity = 5
	}

	/// <summary>
	/// One structured limitation of a read-only state capture.
	/// </summary>
	public sealed class CollectionNativeStateIssue
	{
		/// <summary>Creates one structured capture issue.</summary>
		public CollectionNativeStateIssue(CollectionNativeStateIssueKind kind, string resourceKey, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionNativeStateIssueKind), kind) || kind == CollectionNativeStateIssueKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			ResourceKey = resourceKey ?? String.Empty;
			Message = message ?? String.Empty;
		}

		public CollectionNativeStateIssueKind Kind { get; }
		public string ResourceKey { get; }
		public string Message { get; }
	}

	/// <summary>
	/// Detached native mod registration suitable for collection matching/planning without retaining a live IMod object.
	/// </summary>
	public sealed class CollectionNativeModState
	{
		/// <summary>Creates one detached native mod state.</summary>
		public CollectionNativeModState(NativeModInstanceIdentity identity, string archivePath, string fileName,
			string nexusModId, string nexusFileId, string humanReadableVersion, string machineVersion,
			ModInstallRoot installRoot, ModInstallMethod installMethod)
			: this(identity, archivePath, fileName, nexusModId, nexusFileId, humanReadableVersion, machineVersion, false, installRoot, installMethod)
		{
		}

		/// <summary>Creates one detached native mod state including scripted-installer capability.</summary>
		public CollectionNativeModState(NativeModInstanceIdentity identity, string archivePath, string fileName,
			string nexusModId, string nexusFileId, string humanReadableVersion, string machineVersion, bool hasInstallScript,
			ModInstallRoot installRoot, ModInstallMethod installMethod)
		{
			Identity = identity ?? throw new ArgumentNullException(nameof(identity));
			ArchivePath = archivePath ?? String.Empty;
			FileName = fileName ?? String.Empty;
			NexusModId = nexusModId ?? String.Empty;
			NexusFileId = nexusFileId ?? String.Empty;
			HumanReadableVersion = humanReadableVersion ?? String.Empty;
			MachineVersion = machineVersion ?? String.Empty;
			HasInstallScript = hasInstallScript;
			InstallRoot = installRoot;
			InstallMethod = installMethod;
		}

		public NativeModInstanceIdentity Identity { get; }
		public string ArchivePath { get; }
		public string FileName { get; }
		public string NexusModId { get; }
		public string NexusFileId { get; }
		public string HumanReadableVersion { get; }
		public string MachineVersion { get; }
		public bool HasInstallScript { get; }
		public ModInstallRoot InstallRoot { get; }
		public ModInstallMethod InstallMethod { get; }
	}

	/// <summary>
	/// Detached physical mapping for one native deployment root.
	/// </summary>
	public sealed class CollectionNativeRootState
	{
		/// <summary>Creates one deployment-root mapping observation.</summary>
		public CollectionNativeRootState(ModDeploymentRoot root, string physicalPath)
		{
			Root = root;
			PhysicalPath = physicalPath ?? String.Empty;
		}

		public ModDeploymentRoot Root { get; }
		public string PhysicalPath { get; }
	}

	/// <summary>
	/// One native owner recorded for a deployment target.
	/// </summary>
	public sealed class CollectionNativeOwnerState
	{
		/// <summary>Creates one native owner observation.</summary>
		public CollectionNativeOwnerState(string ownerKey, string unresolvedReference, CollectionNativeOwnerKind kind,
			bool? virtualLinkActive, int? virtualPriority, string virtualStagedSourcePath)
		{
			if (!Enum.IsDefined(typeof(CollectionNativeOwnerKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (kind != CollectionNativeOwnerKind.Unresolved && String.IsNullOrWhiteSpace(ownerKey))
				throw new ArgumentException("A native owner key is required for resolved ownership entries.", nameof(ownerKey));
			if (kind == CollectionNativeOwnerKind.Unresolved && String.IsNullOrWhiteSpace(unresolvedReference))
				throw new ArgumentException("An unresolved ownership reference is required.", nameof(unresolvedReference));
			OwnerKey = ownerKey;
			UnresolvedReference = unresolvedReference ?? String.Empty;
			Kind = kind;
			VirtualLinkActive = virtualLinkActive;
			VirtualPriority = virtualPriority;
			VirtualStagedSourcePath = virtualStagedSourcePath ?? String.Empty;
		}

		public string OwnerKey { get; }
		public string UnresolvedReference { get; }
		public CollectionNativeOwnerKind Kind { get; }
		public bool? VirtualLinkActive { get; }
		public int? VirtualPriority { get; }
		public string VirtualStagedSourcePath { get; }
	}

	/// <summary>
	/// Root-aware native ownership observation for one deployment target.
	/// </summary>
	public sealed class CollectionNativeFileState
	{
		private readonly ReadOnlyCollection<CollectionNativeOwnerState> _installLogOwners;
		private readonly ReadOnlyCollection<CollectionNativeOwnerState> _deploymentOwners;
		private readonly ReadOnlyCollection<CollectionNativeOwnerState> _virtualOwners;

		/// <summary>Creates one root-aware file ownership observation without collapsing distinct native ownership sources.</summary>
		public CollectionNativeFileState(ModDeploymentTarget target, string physicalPath, bool promoted,
			bool recordedByInstallLog, bool recordedByVirtualState, string effectiveOwnerKey,
			IEnumerable<CollectionNativeOwnerState> installLogOwners, IEnumerable<CollectionNativeOwnerState> deploymentOwners,
			IEnumerable<CollectionNativeOwnerState> virtualOwners)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			PhysicalPath = physicalPath ?? String.Empty;
			Promoted = promoted;
			RecordedByInstallLog = recordedByInstallLog;
			RecordedByVirtualState = recordedByVirtualState;
			EffectiveOwnerKey = effectiveOwnerKey;
			_installLogOwners = CopyOwners(installLogOwners, nameof(installLogOwners));
			_deploymentOwners = CopyOwners(deploymentOwners, nameof(deploymentOwners));
			_virtualOwners = CopyOwners(virtualOwners, nameof(virtualOwners));
		}

		public ModDeploymentTarget Target { get; }
		public string PhysicalPath { get; }
		public bool Promoted { get; }
		public bool RecordedByInstallLog { get; }
		public bool RecordedByVirtualState { get; }
		/// <summary>
		/// Gets the native intended/effective owner when native state establishes one; this is not proof of current file bytes.
		/// </summary>
		public string EffectiveOwnerKey { get; }
		public ReadOnlyCollection<CollectionNativeOwnerState> InstallLogOwners { get { return _installLogOwners; } }
		public ReadOnlyCollection<CollectionNativeOwnerState> DeploymentOwners { get { return _deploymentOwners; } }
		public ReadOnlyCollection<CollectionNativeOwnerState> VirtualOwners { get { return _virtualOwners; } }

		private static ReadOnlyCollection<CollectionNativeOwnerState> CopyOwners(IEnumerable<CollectionNativeOwnerState> owners, string parameterName)
		{
			if (owners == null)
				throw new ArgumentNullException(parameterName);
			List<CollectionNativeOwnerState> copied = owners.ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("A file ownership stack cannot contain a null owner record.", parameterName);
			return new ReadOnlyCollection<CollectionNativeOwnerState>(copied);
		}
	}

	/// <summary>
	/// Case-insensitive identity of one INI setting, with hashing consistent with equality.
	/// </summary>
	public sealed class CollectionNativeIniKey : IEquatable<CollectionNativeIniKey>
	{
		/// <summary>Creates one normalized case-insensitive INI identity.</summary>
		public CollectionNativeIniKey(string file, string section, string key)
		{
			File = file ?? String.Empty;
			Section = section ?? String.Empty;
			Key = key ?? String.Empty;
		}

		public string File { get; }
		public string Section { get; }
		public string Key { get; }

		/// <inheritdoc />
		public bool Equals(CollectionNativeIniKey other)
		{
			return !ReferenceEquals(other, null) &&
				StringComparer.OrdinalIgnoreCase.Equals(File, other.File) &&
				StringComparer.OrdinalIgnoreCase.Equals(Section, other.Section) &&
				StringComparer.OrdinalIgnoreCase.Equals(Key, other.Key);
		}

		/// <inheritdoc />
		public override bool Equals(object obj) { return Equals(obj as CollectionNativeIniKey); }
		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(File ?? String.Empty);
				hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Section ?? String.Empty);
				hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Key ?? String.Empty);
				return hash;
			}
		}
		/// <inheritdoc />
		public override string ToString() { return File + "|" + Section + "|" + Key; }
	}

	/// <summary>
	/// One recorded native text-valued owner history entry.
	/// </summary>
	public sealed class CollectionNativeTextOwnerValue
	{
		/// <summary>Creates one text-valued native owner history entry.</summary>
		public CollectionNativeTextOwnerValue(string ownerKey, string value)
		{
			OwnerKey = ownerKey ?? throw new ArgumentNullException(nameof(ownerKey));
			Value = value;
		}
		public string OwnerKey { get; }
		public string Value { get; }
	}

	/// <summary>
	/// One recorded INI edit and its native owner/value history.
	/// </summary>
	public sealed class CollectionNativeIniState
	{
		/// <summary>Creates one INI setting ownership observation.</summary>
		public CollectionNativeIniState(CollectionNativeIniKey key, IEnumerable<CollectionNativeTextOwnerValue> values)
		{
			Key = key ?? throw new ArgumentNullException(nameof(key));
			Values = new ReadOnlyCollection<CollectionNativeTextOwnerValue>((values ?? throw new ArgumentNullException(nameof(values))).ToList());
		}
		public CollectionNativeIniKey Key { get; }
		public ReadOnlyCollection<CollectionNativeTextOwnerValue> Values { get; }
	}

	/// <summary>
	/// One recorded native binary-valued owner history entry.
	/// </summary>
	public sealed class CollectionNativeBinaryOwnerValue
	{
		private readonly byte[] _value;
		/// <summary>Creates one defensively copied binary owner history entry.</summary>
		public CollectionNativeBinaryOwnerValue(string ownerKey, byte[] value)
		{
			OwnerKey = ownerKey ?? throw new ArgumentNullException(nameof(ownerKey));
			_value = value == null ? null : (byte[])value.Clone();
		}
		public string OwnerKey { get; }
		public byte[] Value { get { return _value == null ? null : (byte[])_value.Clone(); } }
		internal byte[] UnsafeValue { get { return _value; } }
	}

	/// <summary>
	/// One recorded game-specific value and its native owner/value history.
	/// </summary>
	public sealed class CollectionNativeGameValueState
	{
		/// <summary>Creates one game-specific value ownership observation.</summary>
		public CollectionNativeGameValueState(string key, IEnumerable<CollectionNativeBinaryOwnerValue> values)
		{
			Key = key ?? String.Empty;
			Values = new ReadOnlyCollection<CollectionNativeBinaryOwnerValue>((values ?? throw new ArgumentNullException(nameof(values))).ToList());
		}
		public string Key { get; }
		public ReadOnlyCollection<CollectionNativeBinaryOwnerValue> Values { get; }
	}

	/// <summary>
	/// Detached plugin diagnostic used by read-only collection planning.
	/// </summary>
	public sealed class CollectionNativePluginDiagnostic
	{
		/// <summary>Creates one detached plugin diagnostic.</summary>
		public CollectionNativePluginDiagnostic(PluginValidationIssueKind kind, PluginValidationSeverity severity)
		{
			Kind = kind;
			Severity = severity;
		}
		public PluginValidationIssueKind Kind { get; }
		public PluginValidationSeverity Severity { get; }
	}

	/// <summary>
	/// Detached plugin state copied from the native plugin manager snapshot.
	/// </summary>
	public sealed class CollectionNativePluginState
	{
		/// <summary>Creates one deeply detached plugin-state observation.</summary>
		public CollectionNativePluginState(string fileName, bool active, int priority, int? allocatedIndex, string modIndex,
			PluginParseStatus parseStatus, PluginAddressClass addressClass, PluginHeaderFlags headerFlags,
			PluginSpecialFlags specialFlags, bool effectiveMaster, int formVersion, IEnumerable<string> masters,
			IEnumerable<CollectionNativePluginDiagnostic> diagnostics)
		{
			FileName = fileName ?? String.Empty;
			Active = active;
			Priority = priority;
			AllocatedIndex = allocatedIndex;
			ModIndex = modIndex ?? String.Empty;
			ParseStatus = parseStatus;
			AddressClass = addressClass;
			HeaderFlags = headerFlags;
			SpecialFlags = specialFlags;
			EffectiveMaster = effectiveMaster;
			FormVersion = formVersion;
			Masters = new ReadOnlyCollection<string>((masters ?? Enumerable.Empty<string>()).Select(x => x ?? String.Empty).ToList());
			Diagnostics = new ReadOnlyCollection<CollectionNativePluginDiagnostic>((diagnostics ?? Enumerable.Empty<CollectionNativePluginDiagnostic>()).ToList());
		}

		public string FileName { get; }
		public bool Active { get; }
		public int Priority { get; }
		public int? AllocatedIndex { get; }
		public string ModIndex { get; }
		public PluginParseStatus ParseStatus { get; }
		public PluginAddressClass AddressClass { get; }
		public PluginHeaderFlags HeaderFlags { get; }
		public PluginSpecialFlags SpecialFlags { get; }
		public bool EffectiveMaster { get; }
		public int FormVersion { get; }
		public ReadOnlyCollection<string> Masters { get; }
		public ReadOnlyCollection<CollectionNativePluginDiagnostic> Diagnostics { get; }
	}

	/// <summary>
	/// Immutable target-scoped native-state index used as the read-only foundation for later C6 planning.
	/// </summary>
	public sealed class CollectionNativeStateIndex
	{
		private readonly ReadOnlyDictionary<NativeModInstanceIdentity, CollectionNativeModState> _mods;
		private readonly ReadOnlyDictionary<string, CollectionNativeModState> _modsByNativeKey;
		private readonly ReadOnlyDictionary<ModDeploymentTarget, CollectionNativeFileState> _files;
		private readonly ReadOnlyDictionary<string, ReadOnlyCollection<CollectionNativeFileState>> _filesByOwnerKey;
		private readonly ReadOnlyDictionary<CollectionNativeIniKey, CollectionNativeIniState> _iniEdits;
		private readonly ReadOnlyDictionary<string, ReadOnlyCollection<CollectionNativeIniState>> _iniEditsByOwnerKey;
		private readonly ReadOnlyDictionary<string, CollectionNativeGameValueState> _gameValues;
		private readonly ReadOnlyDictionary<string, ReadOnlyCollection<CollectionNativeGameValueState>> _gameValuesByOwnerKey;
		private readonly ReadOnlyDictionary<string, CollectionNativePluginState> _plugins;
		private readonly ReadOnlyDictionary<Guid, CollectionTargetAssociation> _associations;
		private readonly ReadOnlyDictionary<NativeModInstanceIdentity, ReadOnlyCollection<CollectionMemberBinding>> _bindingsByNativeMod;
		private readonly ReadOnlyDictionary<Guid, ReadOnlyCollection<CollectionMemberBinding>> _bindingsByAssociation;
		private readonly ReadOnlyDictionary<Guid, ReadOnlyCollection<UserOverride>> _overridesByAssociation;

		/// <summary>Creates and indexes one immutable target-scoped native-state observation.</summary>
		public CollectionNativeStateIndex(CollectionTargetIdentity target, IEnumerable<CollectionNativeRootState> roots,
			IEnumerable<CollectionNativeModState> mods, IEnumerable<CollectionNativeFileState> files,
			IEnumerable<CollectionNativeIniState> iniEdits, IEnumerable<CollectionNativeGameValueState> gameValues,
			IEnumerable<CollectionNativePluginState> plugins, CollectionNativeStateCoverage pluginCoverage,
			IEnumerable<CollectionTargetAssociation> associations, IEnumerable<CollectionMemberBinding> bindings,
			IEnumerable<UserOverride> overrides, CollectionNativeStateCoverage associationCoverage,
			IEnumerable<CollectionNativeStateIssue> issues, long deploymentCommitSequence)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			Roots = new ReadOnlyCollection<CollectionNativeRootState>((roots ?? throw new ArgumentNullException(nameof(roots))).ToList());
			if (!Enum.IsDefined(typeof(CollectionNativeStateCoverage), pluginCoverage))
				throw new ArgumentOutOfRangeException(nameof(pluginCoverage));
			if (!Enum.IsDefined(typeof(CollectionNativeStateCoverage), associationCoverage))
				throw new ArgumentOutOfRangeException(nameof(associationCoverage));
			PluginCoverage = pluginCoverage;
			AssociationCoverage = associationCoverage;
			DeploymentCommitSequence = deploymentCommitSequence;
			Issues = new ReadOnlyCollection<CollectionNativeStateIssue>((issues ?? throw new ArgumentNullException(nameof(issues))).ToList());

			List<CollectionNativeModState> modList = (mods ?? throw new ArgumentNullException(nameof(mods))).ToList();
			_mods = new ReadOnlyDictionary<NativeModInstanceIdentity, CollectionNativeModState>(
				modList.ToDictionary(x => x.Identity));
			_modsByNativeKey = new ReadOnlyDictionary<string, CollectionNativeModState>(
				modList.ToDictionary(x => x.Identity.NativeModKey, StringComparer.OrdinalIgnoreCase));

			List<CollectionNativeFileState> fileList = (files ?? throw new ArgumentNullException(nameof(files))).ToList();
			_files = new ReadOnlyDictionary<ModDeploymentTarget, CollectionNativeFileState>(fileList.ToDictionary(x => x.Target));
			_filesByOwnerKey = BuildOwnerIndex(fileList, file => file.InstallLogOwners.Select(x => x.OwnerKey)
				.Concat(file.DeploymentOwners.Select(x => x.OwnerKey))
				.Concat(file.VirtualOwners.Select(x => x.OwnerKey)));

			List<CollectionNativeIniState> iniList = (iniEdits ?? throw new ArgumentNullException(nameof(iniEdits))).ToList();
			_iniEdits = new ReadOnlyDictionary<CollectionNativeIniKey, CollectionNativeIniState>(iniList.ToDictionary(x => x.Key));
			_iniEditsByOwnerKey = BuildOwnerIndex(iniList, ini => ini.Values.Select(x => x.OwnerKey));

			List<CollectionNativeGameValueState> gameValueList = (gameValues ?? throw new ArgumentNullException(nameof(gameValues))).ToList();
			_gameValues = new ReadOnlyDictionary<string, CollectionNativeGameValueState>(
				gameValueList.ToDictionary(x => x.Key, StringComparer.Ordinal));
			_gameValuesByOwnerKey = BuildOwnerIndex(gameValueList, value => value.Values.Select(x => x.OwnerKey));
			_plugins = new ReadOnlyDictionary<string, CollectionNativePluginState>(
				(plugins ?? throw new ArgumentNullException(nameof(plugins))).ToDictionary(x => x.FileName, StringComparer.OrdinalIgnoreCase));
			_associations = new ReadOnlyDictionary<Guid, CollectionTargetAssociation>(
				(associations ?? throw new ArgumentNullException(nameof(associations))).ToDictionary(x => x.AssociationId));

			List<CollectionMemberBinding> bindingList = (bindings ?? throw new ArgumentNullException(nameof(bindings))).ToList();
			Dictionary<NativeModInstanceIdentity, ReadOnlyCollection<CollectionMemberBinding>> bindingsIndex = bindingList
				.GroupBy(x => x.NativeMod)
				.ToDictionary(x => x.Key, x => new ReadOnlyCollection<CollectionMemberBinding>(x.ToList()));
			_bindingsByNativeMod = new ReadOnlyDictionary<NativeModInstanceIdentity, ReadOnlyCollection<CollectionMemberBinding>>(bindingsIndex);
			Dictionary<Guid, ReadOnlyCollection<CollectionMemberBinding>> associationBindings = bindingList
				.GroupBy(x => x.Association.AssociationId)
				.ToDictionary(x => x.Key, x => new ReadOnlyCollection<CollectionMemberBinding>(x.ToList()));
			_bindingsByAssociation = new ReadOnlyDictionary<Guid, ReadOnlyCollection<CollectionMemberBinding>>(associationBindings);

			Dictionary<Guid, ReadOnlyCollection<UserOverride>> overrideIndex =
				(overrides ?? throw new ArgumentNullException(nameof(overrides)))
				.GroupBy(x => x.Requirement.AssociationId)
				.ToDictionary(x => x.Key, x => new ReadOnlyCollection<UserOverride>(x.ToList()));
			_overridesByAssociation = new ReadOnlyDictionary<Guid, ReadOnlyCollection<UserOverride>>(overrideIndex);

			Fingerprint = CollectionNativeStateFingerprintBuilder.Build(this);
		}

		public CollectionTargetIdentity Target { get; }
		public ReadOnlyCollection<CollectionNativeRootState> Roots { get; }
		public IReadOnlyDictionary<NativeModInstanceIdentity, CollectionNativeModState> Mods { get { return _mods; } }
		public IReadOnlyDictionary<string, CollectionNativeModState> ModsByNativeKey { get { return _modsByNativeKey; } }
		public IReadOnlyDictionary<ModDeploymentTarget, CollectionNativeFileState> Files { get { return _files; } }
		public IReadOnlyDictionary<string, ReadOnlyCollection<CollectionNativeFileState>> FilesByOwnerKey { get { return _filesByOwnerKey; } }
		public IReadOnlyDictionary<CollectionNativeIniKey, CollectionNativeIniState> IniEdits { get { return _iniEdits; } }
		public IReadOnlyDictionary<string, ReadOnlyCollection<CollectionNativeIniState>> IniEditsByOwnerKey { get { return _iniEditsByOwnerKey; } }
		public IReadOnlyDictionary<string, CollectionNativeGameValueState> GameValues { get { return _gameValues; } }
		public IReadOnlyDictionary<string, ReadOnlyCollection<CollectionNativeGameValueState>> GameValuesByOwnerKey { get { return _gameValuesByOwnerKey; } }
		public IReadOnlyDictionary<string, CollectionNativePluginState> Plugins { get { return _plugins; } }
		public CollectionNativeStateCoverage PluginCoverage { get; }
		public IReadOnlyDictionary<Guid, CollectionTargetAssociation> Associations { get { return _associations; } }
		public IReadOnlyDictionary<NativeModInstanceIdentity, ReadOnlyCollection<CollectionMemberBinding>> BindingsByNativeMod { get { return _bindingsByNativeMod; } }
		public IReadOnlyDictionary<Guid, ReadOnlyCollection<CollectionMemberBinding>> BindingsByAssociation { get { return _bindingsByAssociation; } }
		public IReadOnlyDictionary<Guid, ReadOnlyCollection<UserOverride>> OverridesByAssociation { get { return _overridesByAssociation; } }
		public CollectionNativeStateCoverage AssociationCoverage { get; }
		public ReadOnlyCollection<CollectionNativeStateIssue> Issues { get; }
		public long DeploymentCommitSequence { get; }
		public CollectionCurrentStateFingerprint Fingerprint { get; }

		private static ReadOnlyDictionary<string, ReadOnlyCollection<T>> BuildOwnerIndex<T>(IEnumerable<T> records,
			Func<T, IEnumerable<string>> ownerSelector)
		{
			Dictionary<string, ReadOnlyCollection<T>> index = records
				.SelectMany(record => ownerSelector(record)
					.Where(ownerKey => !String.IsNullOrWhiteSpace(ownerKey))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.Select(ownerKey => new { OwnerKey = ownerKey, Record = record }))
				.GroupBy(x => x.OwnerKey, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(x => x.Key, x => new ReadOnlyCollection<T>(x.Select(y => y.Record).ToList()),
					StringComparer.OrdinalIgnoreCase);
			return new ReadOnlyDictionary<string, ReadOnlyCollection<T>>(index);
		}
	}
}
