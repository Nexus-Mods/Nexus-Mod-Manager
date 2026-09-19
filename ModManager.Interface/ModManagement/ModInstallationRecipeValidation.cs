using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Identifies how a path participates in a validated native installation recipe.
	/// </summary>
	public enum ModInstallationRecipePathKind
	{
		/// <summary>
		/// No supported recipe-path role has been selected.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// A source path relative to the immutable input archive or retained content.
		/// </summary>
		ArchiveSource = 1,

		/// <summary>
		/// A destination path relative to the validated native install root.
		/// </summary>
		Destination = 2
	}

	/// <summary>
	/// Represents one canonical relative path admitted by the native installation-recipe boundary.
	/// </summary>
	public sealed class ModInstallationRecipePath
	{
		/// <summary>
		/// Initializes a validated archive-source or destination path.
		/// </summary>
		/// <param name="kind">The role of the relative path.</param>
		/// <param name="path">The relative path to validate and canonicalize.</param>
		public ModInstallationRecipePath(ModInstallationRecipePathKind kind, string path)
		{
			if (!Enum.IsDefined(typeof(ModInstallationRecipePathKind), kind) || kind == ModInstallationRecipePathKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));

			Kind = kind;
			Path = NormalizeRelativePath(path, nameof(path));
		}

		/// <summary>
		/// Gets the role of this path in the recipe.
		/// </summary>
		public ModInstallationRecipePathKind Kind { get; }

		/// <summary>
		/// Gets the canonical backslash-separated relative path.
		/// </summary>
		public string Path { get; }

		/// <summary>
		/// Validates and canonicalizes one untrusted relative recipe path without resolving it against a live game directory.
		/// </summary>
		/// <param name="path">The relative path to validate.</param>
		/// <param name="parameterName">The parameter name used when validation fails.</param>
		/// <returns>The canonical backslash-separated relative path.</returns>
		internal static string NormalizeRelativePath(string path, string parameterName)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A recipe path is required.", parameterName);
			if (!StringComparer.Ordinal.Equals(path, path.Trim()))
				throw new ArgumentException("Recipe paths must not contain leading or trailing whitespace.", parameterName);

			string normalized = path.Replace('/', '\\');
			if (normalized[0] == '\\' || System.IO.Path.IsPathRooted(path) ||
				(normalized.Length > 1 && normalized[1] == ':'))
			{
				throw new InvalidDataException(string.Format("Recipe path '{0}' must be relative to its declared scope.", path));
			}

			string[] parts = normalized.Split('\\');
			var canonicalParts = new List<string>(parts.Length);
			foreach (string part in parts)
			{
				if (string.IsNullOrEmpty(part) || part == "." || part == "..")
					throw new InvalidDataException(string.Format("Recipe path '{0}' is not canonical and relative.", path));
				if (!StringComparer.Ordinal.Equals(part, part.TrimEnd(' ', '.')))
					throw new InvalidDataException(string.Format("Recipe path '{0}' contains a Windows-normalized trailing space or dot.", path));
				if (part.IndexOf(':') >= 0)
					throw new InvalidDataException(string.Format("Recipe path '{0}' contains an invalid root or alternate-data-stream qualifier.", path));
				if (part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
					throw new InvalidDataException(string.Format("Recipe path '{0}' contains characters that are invalid in a file name.", path));
				if (IsReservedDeviceName(part))
					throw new InvalidDataException(string.Format("Recipe path '{0}' contains a reserved Windows device name.", path));

				canonicalParts.Add(part);
			}

			return string.Join("\\", canonicalParts.ToArray());
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

	/// <summary>
	/// Identifies the exact immutable source bytes expected by a native installation recipe.
	/// </summary>
	public sealed class ModInstallationRecipeExpectedContent
	{
		/// <summary>
		/// Initializes an exact SHA-256 and byte-length expectation.
		/// </summary>
		/// <param name="sha256">The canonical lowercase SHA-256 digest.</param>
		/// <param name="byteLength">The exact expected byte length.</param>
		public ModInstallationRecipeExpectedContent(string sha256, long byteLength)
		{
			if (string.IsNullOrWhiteSpace(sha256))
				throw new ArgumentException("An expected SHA-256 digest is required.", nameof(sha256));
			if (!StringComparer.Ordinal.Equals(sha256, sha256.Trim()) || sha256.Length != 64)
				throw new ArgumentException("Expected content SHA-256 must be a canonical 64-character hexadecimal digest.", nameof(sha256));
			for (int index = 0; index < sha256.Length; index++)
			{
				char character = sha256[index];
				bool isCanonicalHex = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
				if (!isCanonicalHex)
					throw new ArgumentException("Expected content SHA-256 must contain only lowercase hexadecimal characters.", nameof(sha256));
			}
			if (byteLength < 0)
				throw new ArgumentOutOfRangeException(nameof(byteLength), "Expected content length cannot be negative.");

			Sha256 = sha256;
			ByteLength = byteLength;
		}

		/// <summary>
		/// Gets the canonical lowercase SHA-256 digest of the expected source bytes.
		/// </summary>
		public string Sha256 { get; }

		/// <summary>
		/// Gets the exact expected source byte length.
		/// </summary>
		public long ByteLength { get; }
	}

	/// <summary>
	/// Identifies one explicitly versioned native-recipe capability understood by an adapter.
	/// </summary>
	public sealed class ModInstallationRecipeCapability
	{
		/// <summary>
		/// Initializes a named capability and its positive contract version.
		/// </summary>
		/// <param name="capabilityId">The canonical adapter capability identifier.</param>
		/// <param name="version">The positive capability contract version.</param>
		public ModInstallationRecipeCapability(string capabilityId, int version)
		{
			CapabilityId = ModInstallationRecipeValidation.RequireCanonicalToken(capabilityId, nameof(capabilityId));
			if (version <= 0)
				throw new ArgumentOutOfRangeException(nameof(version), "Recipe capability versions must be positive.");
			Version = version;
		}

		/// <summary>
		/// Gets the canonical capability identifier.
		/// </summary>
		public string CapabilityId { get; }

		/// <summary>
		/// Gets the positive capability contract version.
		/// </summary>
		public int Version { get; }
	}

	/// <summary>
	/// Stores the immutable, non-executable validation metadata required before a recipe may reach native translation or execution.
	/// </summary>
	/// <remarks>
	/// This type deliberately contains no raw Collection/Vortex objects and no executable installer operations. Adapters in
	/// later C5 steps translate supported recipes only after these common trust-boundary invariants have been established.
	/// </remarks>
	public sealed class ModInstallationRecipeValidation
	{
		private readonly ReadOnlyCollection<ModInstallationRecipeCapability> m_rocCapabilities;
		private readonly ReadOnlyCollection<ModInstallationRecipePath> m_rocPaths;

		/// <summary>
		/// Initializes validated metadata for one native installation recipe.
		/// </summary>
		/// <param name="adapterId">The canonical identifier of the adapter contract which produced the recipe input.</param>
		/// <param name="adapterVersion">The positive adapter contract version.</param>
		/// <param name="installContext">The native method/root against which paths and effects were validated.</param>
		/// <param name="expectedContent">The exact immutable source content expected by the recipe.</param>
		/// <param name="capabilities">The explicitly versioned capabilities required by the recipe.</param>
		/// <param name="paths">The relative source and destination paths admitted by the recipe.</param>
		public ModInstallationRecipeValidation(string adapterId, int adapterVersion, ModInstallContext installContext,
			ModInstallationRecipeExpectedContent expectedContent, IEnumerable<ModInstallationRecipeCapability> capabilities,
			IEnumerable<ModInstallationRecipePath> paths)
		{
			AdapterId = RequireCanonicalToken(adapterId, nameof(adapterId));
			if (adapterVersion <= 0)
				throw new ArgumentOutOfRangeException(nameof(adapterVersion), "Recipe adapter versions must be positive.");
			if (installContext == null)
				throw new ArgumentNullException(nameof(installContext));
			if (expectedContent == null)
				throw new ArgumentNullException(nameof(expectedContent));
			if (capabilities == null)
				throw new ArgumentNullException(nameof(capabilities));
			if (paths == null)
				throw new ArgumentNullException(nameof(paths));

			AdapterVersion = adapterVersion;
			InstallContext = new ModInstallContext(installContext.Method, installContext.InstallRoot);
			ExpectedContent = expectedContent;

			var copiedCapabilities = new List<ModInstallationRecipeCapability>();
			var capabilityIds = new HashSet<string>(StringComparer.Ordinal);
			foreach (ModInstallationRecipeCapability capability in capabilities)
			{
				if (capability == null)
					throw new ArgumentException("Recipe capabilities cannot contain null values.", nameof(capabilities));
				if (!capabilityIds.Add(capability.CapabilityId))
					throw new ArgumentException("Recipe capabilities cannot contain duplicate capability identifiers.", nameof(capabilities));
				copiedCapabilities.Add(capability);
			}
			if (copiedCapabilities.Count == 0)
				throw new ArgumentException("At least one versioned recipe capability is required.", nameof(capabilities));
			m_rocCapabilities = new ReadOnlyCollection<ModInstallationRecipeCapability>(copiedCapabilities);

			var copiedPaths = new List<ModInstallationRecipePath>();
			foreach (ModInstallationRecipePath path in paths)
			{
				if (path == null)
					throw new ArgumentException("Recipe paths cannot contain null values.", nameof(paths));
				copiedPaths.Add(path);
			}
			m_rocPaths = new ReadOnlyCollection<ModInstallationRecipePath>(copiedPaths);
		}

		/// <summary>
		/// Gets the adapter contract identifier that produced the trusted native recipe representation.
		/// </summary>
		public string AdapterId { get; }

		/// <summary>
		/// Gets the positive adapter contract version.
		/// </summary>
		public int AdapterVersion { get; }

		/// <summary>
		/// Gets the immutable install method/root for which the recipe was validated.
		/// </summary>
		public ModInstallContext InstallContext { get; }

		/// <summary>
		/// Gets the exact immutable input bytes expected by the recipe.
		/// </summary>
		public ModInstallationRecipeExpectedContent ExpectedContent { get; }

		/// <summary>
		/// Gets the explicitly versioned native capabilities required by the recipe.
		/// </summary>
		public IReadOnlyList<ModInstallationRecipeCapability> Capabilities
		{
			get { return m_rocCapabilities; }
		}

		/// <summary>
		/// Gets the validated relative source/destination paths declared by the recipe adapter.
		/// </summary>
		public IReadOnlyList<ModInstallationRecipePath> Paths
		{
			get { return m_rocPaths; }
		}

		/// <summary>
		/// Validates a canonical opaque token used at the native recipe boundary.
		/// </summary>
		internal static string RequireCanonicalToken(string value, string parameterName)
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentException("A canonical recipe token is required.", parameterName);
			if (!StringComparer.Ordinal.Equals(value, value.Trim()))
				throw new ArgumentException("Recipe tokens must not contain leading or trailing whitespace.", parameterName);
			if (value.Length > 256)
				throw new ArgumentException("Recipe tokens cannot exceed 256 characters.", parameterName);
			for (int index = 0; index < value.Length; index++)
			{
				if (char.IsControl(value[index]))
					throw new ArgumentException("Recipe tokens cannot contain control characters.", parameterName);
			}
			return value;
		}
	}
}
