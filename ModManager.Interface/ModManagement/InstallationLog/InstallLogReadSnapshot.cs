using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.ModManagement.InstallationLog
{
	/// <summary>
	/// Immutable committed InstallLog observation used by read-only planning code.
	/// </summary>
	/// <remarks>
	/// This snapshot deliberately excludes ambient transaction buffers. It is a detached view of the committed
	/// registration, ownership and configuration histories observed at capture time.
	/// </remarks>
	public sealed class InstallLogReadSnapshot
	{
		/// <summary>Creates one immutable committed InstallLog observation.</summary>
		public InstallLogReadSnapshot(string originalValuesKey, long deploymentCommitSequence,
			IEnumerable<InstallLogReadMod> mods, IEnumerable<InstallLogReadFile> files,
			IEnumerable<InstallLogReadIniEdit> iniEdits, IEnumerable<InstallLogReadGameValue> gameValues,
			IEnumerable<InstallLogReadDeploymentTarget> deploymentTargets)
		{
			OriginalValuesKey = originalValuesKey;
			DeploymentCommitSequence = deploymentCommitSequence;
			Mods = Copy(mods, nameof(mods));
			Files = Copy(files, nameof(files));
			IniEdits = Copy(iniEdits, nameof(iniEdits));
			GameValues = Copy(gameValues, nameof(gameValues));
			DeploymentTargets = Copy(deploymentTargets, nameof(deploymentTargets));
		}

		public string OriginalValuesKey { get; }
		public long DeploymentCommitSequence { get; }
		public ReadOnlyCollection<InstallLogReadMod> Mods { get; }
		public ReadOnlyCollection<InstallLogReadFile> Files { get; }
		public ReadOnlyCollection<InstallLogReadIniEdit> IniEdits { get; }
		public ReadOnlyCollection<InstallLogReadGameValue> GameValues { get; }
		public ReadOnlyCollection<InstallLogReadDeploymentTarget> DeploymentTargets { get; }

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => ReferenceEquals(x, null)))
				throw new ArgumentException("A native read snapshot cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}

	/// <summary>
	/// Captures one committed native mod registration and its recorded installation context.
	/// </summary>
	public sealed class InstallLogReadMod
	{
		/// <summary>Creates one detached native mod registration observation.</summary>
		public InstallLogReadMod(string modKey, string archivePath, string fileName, string nexusModId, string nexusFileId,
			string humanReadableVersion, string machineVersion, ModInstallRoot installRoot, ModInstallMethod installMethod, bool hidden)
			: this(modKey, archivePath, fileName, nexusModId, nexusFileId, humanReadableVersion, machineVersion, false, installRoot, installMethod, hidden)
		{
		}

		/// <summary>Creates one detached native mod registration observation including scripted-installer capability.</summary>
		public InstallLogReadMod(string modKey, string archivePath, string fileName, string nexusModId, string nexusFileId,
			string humanReadableVersion, string machineVersion, bool hasInstallScript, ModInstallRoot installRoot, ModInstallMethod installMethod, bool hidden)
		{
			if (String.IsNullOrWhiteSpace(modKey))
				throw new ArgumentException("A native mod key is required.", nameof(modKey));
			ModKey = modKey;
			ArchivePath = archivePath ?? String.Empty;
			FileName = fileName ?? String.Empty;
			NexusModId = nexusModId ?? String.Empty;
			NexusFileId = nexusFileId ?? String.Empty;
			HumanReadableVersion = humanReadableVersion ?? String.Empty;
			MachineVersion = machineVersion ?? String.Empty;
			HasInstallScript = hasInstallScript;
			InstallRoot = installRoot;
			InstallMethod = installMethod;
			Hidden = hidden;
		}

		public string ModKey { get; }
		public string ArchivePath { get; }
		public string FileName { get; }
		public string NexusModId { get; }
		public string NexusFileId { get; }
		public string HumanReadableVersion { get; }
		public string MachineVersion { get; }
		public bool HasInstallScript { get; }
		public ModInstallRoot InstallRoot { get; }
		public ModInstallMethod InstallMethod { get; }
		public bool Hidden { get; }
	}

	/// <summary>
	/// Captures the ordered committed owner history for one legacy InstallLog file entry.
	/// </summary>
	public sealed class InstallLogReadFile
	{
		/// <summary>Creates one root-aware legacy file ownership observation.</summary>
		public InstallLogReadFile(string path, ModDeploymentTarget target, IEnumerable<string> ownerKeys)
		{
			if (String.IsNullOrWhiteSpace(path))
				throw new ArgumentException("An installed file path is required.", nameof(path));
			Path = path;
			Target = target ?? throw new ArgumentNullException(nameof(target));
			OwnerKeys = CopyOwnerKeys(ownerKeys, nameof(ownerKeys));
		}

		public string Path { get; }
		public ModDeploymentTarget Target { get; }
		public ReadOnlyCollection<string> OwnerKeys { get; }

		internal static ReadOnlyCollection<string> CopyOwnerKeys(IEnumerable<string> ownerKeys, string parameterName)
		{
			if (ownerKeys == null)
				throw new ArgumentNullException(parameterName);
			List<string> copied = ownerKeys.ToList();
			if (copied.Any(String.IsNullOrWhiteSpace))
				throw new ArgumentException("An owner stack cannot contain an empty native owner key.", parameterName);
			return new ReadOnlyCollection<string>(copied);
		}
	}

	/// <summary>
	/// Captures one committed INI edit and the ordered values installed by its native owners.
	/// </summary>
	public sealed class InstallLogReadIniEdit
	{
		/// <summary>Creates one recorded INI ownership observation.</summary>
		public InstallLogReadIniEdit(string file, string section, string key, IEnumerable<InstallLogReadStringValue> values)
		{
			File = file ?? String.Empty;
			Section = section ?? String.Empty;
			Key = key ?? String.Empty;
			Values = new ReadOnlyCollection<InstallLogReadStringValue>((values ?? throw new ArgumentNullException(nameof(values))).ToList());
		}

		public string File { get; }
		public string Section { get; }
		public string Key { get; }
		public ReadOnlyCollection<InstallLogReadStringValue> Values { get; }
	}

	/// <summary>
	/// Captures one committed game-specific value and the ordered values installed by its native owners.
	/// </summary>
	public sealed class InstallLogReadGameValue
	{
		/// <summary>Creates one recorded game-specific value observation.</summary>
		public InstallLogReadGameValue(string key, IEnumerable<InstallLogReadBinaryValue> values)
		{
			if (String.IsNullOrWhiteSpace(key))
				throw new ArgumentException("A game-specific value key is required.", nameof(key));
			Key = key;
			Values = new ReadOnlyCollection<InstallLogReadBinaryValue>((values ?? throw new ArgumentNullException(nameof(values))).ToList());
		}

		public string Key { get; }
		public ReadOnlyCollection<InstallLogReadBinaryValue> Values { get; }
	}

	/// <summary>
	/// Captures one owner/value pair from an INI edit history.
	/// </summary>
	public sealed class InstallLogReadStringValue
	{
		/// <summary>Creates one recorded text value for a native owner.</summary>
		public InstallLogReadStringValue(string ownerKey, string value)
		{
			if (String.IsNullOrWhiteSpace(ownerKey))
				throw new ArgumentException("A native owner key is required.", nameof(ownerKey));
			OwnerKey = ownerKey;
			Value = value;
		}

		public string OwnerKey { get; }
		public string Value { get; }
	}

	/// <summary>
	/// Captures one owner/value pair from a game-specific value history.
	/// </summary>
	public sealed class InstallLogReadBinaryValue
	{
		private readonly byte[] _value;

		/// <summary>Creates one recorded binary value for a native owner.</summary>
		public InstallLogReadBinaryValue(string ownerKey, byte[] value)
		{
			if (String.IsNullOrWhiteSpace(ownerKey))
				throw new ArgumentException("A native owner key is required.", nameof(ownerKey));
			OwnerKey = ownerKey;
			_value = value == null ? null : (byte[])value.Clone();
		}

		public string OwnerKey { get; }
		public byte[] Value { get { return _value == null ? null : (byte[])_value.Clone(); } }
		internal byte[] UnsafeValue { get { return _value; } }
	}

	/// <summary>
	/// Captures one promoted method-neutral deployment target and its ordered native owner stack.
	/// </summary>
	public sealed class InstallLogReadDeploymentTarget
	{
		/// <summary>Creates one promoted deployment ownership observation.</summary>
		public InstallLogReadDeploymentTarget(ModDeploymentTarget target, IEnumerable<string> ownerKeys)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			OwnerKeys = InstallLogReadFile.CopyOwnerKeys(ownerKeys, nameof(ownerKeys));
		}

		public ModDeploymentTarget Target { get; }
		public ReadOnlyCollection<string> OwnerKeys { get; }
	}
}
