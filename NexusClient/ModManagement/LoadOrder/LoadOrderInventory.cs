using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Stable inventory diagnostic identifiers for localization and later repair planning.</summary>
	public enum LoadOrderDiagnosticKind
	{
		RootUnavailable,
		EnumerationFailed,
		UnexpectedFile,
		UnexpectedDirectory,
		ScanLimitReached,
		InspectorUnavailable,
		PackageInspectionFailed,
		PackageInspectionIncomplete,
		PackageChanged,
		DuplicateModuleIdentity
	}

	/// <summary>Severity is independent from whether an inventory is complete.</summary>
	public enum LoadOrderDiagnosticSeverity { Information, Warning, Error }

	/// <summary>Immutable evidence with a localizable identifier; never a cleanup instruction or ownership claim.</summary>
	public sealed class LoadOrderDiagnostic
	{
		/// <summary>Captures a finding and optional technical details without producing user-facing prose.</summary>
		public LoadOrderDiagnostic(LoadOrderDiagnosticKind kind, LoadOrderDiagnosticSeverity severity, string path,
			string moduleIdentity = null, string technicalDetails = null)
		{
			if (!Enum.IsDefined(typeof(LoadOrderDiagnosticKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
			if (!Enum.IsDefined(typeof(LoadOrderDiagnosticSeverity), severity)) throw new ArgumentOutOfRangeException(nameof(severity));
			Kind = kind; Severity = severity; Path = path; ModuleIdentity = moduleIdentity; TechnicalDetails = technicalDetails;
		}

		public LoadOrderDiagnosticKind Kind { get; }
		public LoadOrderDiagnosticSeverity Severity { get; }
		public string LocalizationKey => "LoadOrder.Diagnostics." + Kind;
		public string Path { get; }
		public string ModuleIdentity { get; }
		public string TechnicalDetails { get; }
	}

	/// <summary>Detached filesystem metadata, useful for cache invalidation but not sufficient authority for later writes.</summary>
	public sealed class LoadOrderPackageFile
	{
		/// <summary>Records one absolute package path and the metadata observed before inspection.</summary>
		public LoadOrderPackageFile(string path, long length, long lastWriteTimeUtcTicks)
		{
			if (String.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path)) throw new ArgumentException("An absolute package path is required.", nameof(path));
			if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
			if (lastWriteTimeUtcTicks < DateTime.MinValue.Ticks || lastWriteTimeUtcTicks > DateTime.MaxValue.Ticks)
				throw new ArgumentOutOfRangeException(nameof(lastWriteTimeUtcTicks));
			Path = System.IO.Path.GetFullPath(path); Length = length; LastWriteTimeUtcTicks = lastWriteTimeUtcTicks;
		}

		public string Path { get; }
		public string FileName => System.IO.Path.GetFileName(Path);
		public long Length { get; }
		public long LastWriteTimeUtcTicks { get; }
	}

	/// <summary>Positive content observations; absent flags are not proof of absence in a partial inspection.</summary>
	[Flags]
	public enum LoadOrderContentFeatures
	{
		None = 0,
		LuaScripts = 1,
		OsirisScripts = 2,
		ScriptExtender = 4,
		EmbeddedModFixer = 8,
		GameFileOverrides = 16
	}

	/// <summary>Package inspection completeness, independent of whether the contents are orderable.</summary>
	public enum LoadOrderInspectionStatus { Complete, Partial, Failed, Unavailable }

	/// <summary>Distinguishes orderable modules, overrides, continuation files and unclassified packages.</summary>
	public enum LoadOrderPackageKind { Unknown, Modules, Override, Continuation }

	/// <summary>Campaigns are identifiable without treating their fixed position as an editable add-on.</summary>
	public enum LoadOrderModuleKind { AddOn, Campaign }

	/// <summary>Declared dependency or conflict metadata, retaining the provider's raw version representation.</summary>
	public sealed class LoadOrderModuleReference
	{
		/// <summary>Captures an identity without inferring version compatibility or a download location.</summary>
		public LoadOrderModuleReference(string identity, string name, string version)
		{
			Identity = LoadOrderInventoryValues.Required(identity, nameof(identity), 256);
			Name = LoadOrderInventoryValues.Optional(name, nameof(name), 1024);
			Version = LoadOrderInventoryValues.Optional(version, nameof(version), 128);
		}
		public string Identity { get; }
		public string Name { get; }
		public string Version { get; }
	}

	/// <summary>Shared module metadata; integer versions and publishing handles remain strings to avoid numeric loss.</summary>
	public sealed class LoadOrderModuleMetadata
	{
		/// <summary>Freezes package-provided metadata without inventing missing author, checksum or publishing values.</summary>
		public LoadOrderModuleMetadata(string identity, string name, string folder, string version,
			string author = null, string description = null, string md5 = null, string publishHandle = null, IEnumerable<string> tags = null)
		{
			Identity = LoadOrderInventoryValues.Required(identity, nameof(identity), 256);
			Name = LoadOrderInventoryValues.Required(name, nameof(name), 1024);
			Folder = LoadOrderInventoryValues.Required(folder, nameof(folder), 1024);
			Version = LoadOrderInventoryValues.Required(version, nameof(version), 128);
			Author = LoadOrderInventoryValues.Optional(author, nameof(author), 1024);
			Description = LoadOrderInventoryValues.Optional(description, nameof(description), 65536);
			Md5 = LoadOrderInventoryValues.Optional(md5, nameof(md5), 128);
			PublishHandle = LoadOrderInventoryValues.Optional(publishHandle, nameof(publishHandle), 128);
			Tags = LoadOrderInventoryValues.Copy(tags, 128, nameof(tags));
			foreach (string tag in Tags) LoadOrderInventoryValues.Required(tag, nameof(tags), 128);
		}
		public string Identity { get; }
		public string Name { get; }
		public string Folder { get; }
		public string Version { get; }
		public string Author { get; }
		public string Description { get; }
		public string Md5 { get; }
		public string PublishHandle { get; }
		public IReadOnlyList<string> Tags { get; }
	}

	/// <summary>One module within a package, with independently scoped dependencies, conflicts and script observations.</summary>
	public sealed class LoadOrderModuleInfo
	{
		/// <summary>Freezes metadata and positive feature observations; no enabled or ownership state is inferred.</summary>
		public LoadOrderModuleInfo(LoadOrderModuleMetadata metadata, LoadOrderModuleKind kind, LoadOrderContentFeatures features,
			int? requiredScriptExtenderVersion, IEnumerable<LoadOrderModuleReference> dependencies, IEnumerable<LoadOrderModuleReference> conflicts)
		{
			Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
			if (!Enum.IsDefined(typeof(LoadOrderModuleKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
			LoadOrderInventoryValues.ValidateFeatures(features);
			if (requiredScriptExtenderVersion < 0) throw new ArgumentOutOfRangeException(nameof(requiredScriptExtenderVersion));
			Kind = kind; Features = features; RequiredScriptExtenderVersion = requiredScriptExtenderVersion;
			Dependencies = LoadOrderInventoryValues.Copy(dependencies, 4096, nameof(dependencies));
			Conflicts = LoadOrderInventoryValues.Copy(conflicts, 4096, nameof(conflicts));
		}
		public LoadOrderModuleMetadata Metadata { get; }
		public LoadOrderModuleKind Kind { get; }
		public LoadOrderContentFeatures Features { get; }
		public int? RequiredScriptExtenderVersion { get; }
		public IReadOnlyList<LoadOrderModuleReference> Dependencies { get; }
		public IReadOnlyList<LoadOrderModuleReference> Conflicts { get; }
	}

	/// <summary>One package-reader result, preserving partial evidence and continuation-file relationships explicitly.</summary>
	public sealed class LoadOrderPackageInspection
	{
		/// <summary>Freezes an inspection; complete results must classify the package and identify all files read.</summary>
		public LoadOrderPackageInspection(LoadOrderInspectionStatus status, LoadOrderPackageKind kind,
			IEnumerable<LoadOrderModuleInfo> modules, LoadOrderContentFeatures features, IEnumerable<string> readFileNames,
			string primaryFileName = null, string technicalDetails = null)
		{
			if (!Enum.IsDefined(typeof(LoadOrderInspectionStatus), status) || !Enum.IsDefined(typeof(LoadOrderPackageKind), kind))
				throw new ArgumentException("Unsupported package inspection classification.");
			LoadOrderInventoryValues.ValidateFeatures(features);
			Modules = LoadOrderInventoryValues.Copy(modules, 256, nameof(modules));
			ReadFileNames = LoadOrderInventoryValues.Copy(readFileNames, 1024, nameof(readFileNames));
			foreach (string file in ReadFileNames) LoadOrderInventoryValues.FileName(file);
			if (primaryFileName != null) LoadOrderInventoryValues.FileName(primaryFileName);
			if ((status == LoadOrderInspectionStatus.Complete && (kind == LoadOrderPackageKind.Unknown || ReadFileNames.Count == 0)) ||
				(kind == LoadOrderPackageKind.Modules && Modules.Count == 0) ||
				(kind != LoadOrderPackageKind.Modules && Modules.Count != 0) ||
				(kind == LoadOrderPackageKind.Continuation) != (primaryFileName != null))
				throw new ArgumentException("Inconsistent package inspection contents.");
			Status = status; Kind = kind; Features = features; PrimaryFileName = primaryFileName;
			TechnicalDetails = LoadOrderInventoryValues.Optional(technicalDetails, nameof(technicalDetails), 8192);
		}
		public LoadOrderInspectionStatus Status { get; }
		public LoadOrderPackageKind Kind { get; }
		public IReadOnlyList<LoadOrderModuleInfo> Modules { get; }
		public LoadOrderContentFeatures Features { get; }
		public IReadOnlyList<string> ReadFileNames { get; }
		public string PrimaryFileName { get; }
		public string TechnicalDetails { get; }
	}

	/// <summary>Associates filesystem evidence with a package result without claiming NMM ownership.</summary>
	public sealed class LoadOrderPackageObservation
	{
		/// <summary>Captures one inspected package from the observed root.</summary>
		public LoadOrderPackageObservation(LoadOrderPackageFile file, LoadOrderPackageInspection inspection)
		{
			File = file ?? throw new ArgumentNullException(nameof(file));
			Inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
		}
		public LoadOrderPackageFile File { get; }
		public LoadOrderPackageInspection Inspection { get; }
	}

	/// <summary>Read-only progress for one package scan; completed observations can enrich a provisional display.</summary>
	public sealed class LoadOrderInspectionProgress
	{
		/// <summary>Reports the current filename before inspection and its observation after inspection completes.</summary>
		internal LoadOrderInspectionProgress(int completed, int total, string fileName, LoadOrderPackageObservation package = null, int reusedPackages = 0)
		{
			Completed = completed; Total = total; FileName = fileName; Package = package; ReusedPackages = reusedPackages;
		}
		public int Completed { get; }
		public int Total { get; }
		public string FileName { get; }
		public LoadOrderPackageObservation Package { get; }
		public int ReusedPackages { get; }
	}

	/// <summary>Detached read-only inventory; completeness is not game compatibility or permission to publish an order.</summary>
	public sealed class LoadOrderInventory
	{
		/// <summary>Freezes partial successes and diagnostics from one explicitly requested scan.</summary>
		internal LoadOrderInventory(string packageRoot, IEnumerable<LoadOrderPackageObservation> packages, bool isComplete,
			IEnumerable<LoadOrderDiagnostic> diagnostics)
		{
			PackageRoot = packageRoot;
			Packages = Array.AsReadOnly(packages.ToArray());
			Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
			IsComplete = isComplete;
		}
		public string PackageRoot { get; }
		public IReadOnlyList<LoadOrderPackageObservation> Packages { get; }
		public IReadOnlyList<LoadOrderDiagnostic> Diagnostics { get; }
		public bool IsComplete { get; }
	}

	/// <summary>Shared bounds for immutable models and the untrusted package-reader protocol.</summary>
	internal static class LoadOrderInventoryValues
	{
		/// <summary>Validates bounded required text while preserving the original value.</summary>
		internal static string Required(string value, string parameter, int maximum)
		{
			if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("A nonempty value is required.", parameter);
			return Optional(value, parameter, maximum);
		}
		/// <summary>Retains absent optional metadata without synthesizing values.</summary>
		internal static string Optional(string value, string parameter, int maximum)
		{
			if (value != null && (value.Length > maximum || value.IndexOf('\0') >= 0)) throw new ArgumentException("Invalid or oversized metadata.", parameter);
			return value;
		}
		/// <summary>Copies bounded sequences and rejects null members.</summary>
		internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, int maximum, string parameter) where T : class
		{
			T[] result = (values ?? Enumerable.Empty<T>()).Take(maximum + 1).ToArray();
			if (result.Length > maximum || result.Any(value => value == null)) throw new ArgumentException("Invalid or oversized collection.", parameter);
			return Array.AsReadOnly(result);
		}
		/// <summary>Rejects undefined feature bits instead of silently presenting them as recognized content.</summary>
		internal static void ValidateFeatures(LoadOrderContentFeatures features)
		{
			if (((int)features & ~31) != 0) throw new ArgumentOutOfRangeException(nameof(features));
		}
		/// <summary>Accepts one ordinary filename, excluding paths and reserved Windows segments.</summary>
		internal static void FileName(string value)
		{
			Nexus.Client.ModManagement.ManagedContent.StructuredXmlRecordRule.Segment(value);
		}
	}
}
