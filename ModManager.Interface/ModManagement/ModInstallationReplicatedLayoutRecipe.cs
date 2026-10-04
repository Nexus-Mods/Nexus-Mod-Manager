using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.ModManagement
{
	/// <summary>Identifies the content-matching digest used by one replicated-layout output.</summary>
	public enum ModInstallationReplicatedContentHashAlgorithm
	{
		Md5 = 0,
		Xxh64 = 1
	}

	/// <summary>
	/// Describes one desired destination in a byte-preserving replicated-layout recipe.
	/// </summary>
	/// <remarks>
	/// <see cref="ContentHash"/> is a content-matching key used to locate bytes inside the already SHA-256-verified
	/// archive from C5.3. It is not an independent integrity guarantee. An optional source hint may disambiguate two
	/// archive entries whose bytes produce the same matching digest.
	/// </remarks>
	public sealed class ModInstallationReplicatedFile
	{
		/// <summary>
		/// Initializes one MD5 replicated output requirement.
		/// </summary>
		/// <param name="destinationPath">The logical destination path relative to the validated native install root.</param>
		/// <param name="contentMd5">The canonical lowercase MD5 digest used to match desired bytes inside the verified archive.</param>
		/// <param name="sourcePathHint">An optional exact archive-relative source path used to resolve an otherwise ambiguous content match.</param>
		public ModInstallationReplicatedFile(string destinationPath, string contentMd5, string sourcePathHint = null)
			: this(destinationPath, ModInstallationReplicatedContentHashAlgorithm.Md5, contentMd5, sourcePathHint)
		{
		}

		/// <summary>
		/// Initializes one replicated output requirement using the explicitly characterized matching digest.
		/// </summary>
		/// <param name="destinationPath">The logical destination path relative to the validated native install root.</param>
		/// <param name="hashAlgorithm">The matching digest algorithm.</param>
		/// <param name="contentHash">The canonical digest text for the selected algorithm.</param>
		/// <param name="sourcePathHint">An optional exact archive-relative source path used to resolve an otherwise ambiguous content match.</param>
		public ModInstallationReplicatedFile(string destinationPath, ModInstallationReplicatedContentHashAlgorithm hashAlgorithm,
			string contentHash, string sourcePathHint = null)
		{
			DestinationPath = new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, destinationPath).Path;
			HashAlgorithm = hashAlgorithm;
			ContentHash = RequireCanonicalHash(hashAlgorithm, contentHash, nameof(contentHash));
			SourcePathHint = sourcePathHint == null
				? null
				: new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, sourcePathHint).Path;
		}

		/// <summary>Gets the canonical destination path relative to the native install root.</summary>
		public string DestinationPath { get; }

		/// <summary>Gets the matching digest algorithm.</summary>
		public ModInstallationReplicatedContentHashAlgorithm HashAlgorithm { get; }

		/// <summary>Gets the canonical matching digest.</summary>
		public string ContentHash { get; }

		/// <summary>Gets the canonical lowercase MD5 digest when this output uses MD5; otherwise null.</summary>
		public string ContentMd5 { get { return HashAlgorithm == ModInstallationReplicatedContentHashAlgorithm.Md5 ? ContentHash : null; } }

		/// <summary>Gets the canonical Vortex base64 XXH64 digest when this output uses XXH64; otherwise null.</summary>
		public string ContentXxh64 { get { return HashAlgorithm == ModInstallationReplicatedContentHashAlgorithm.Xxh64 ? ContentHash : null; } }

		/// <summary>Gets the optional canonical archive-relative source path used to disambiguate a matching digest.</summary>
		public string SourcePathHint { get; }

		private static string RequireCanonicalHash(ModInstallationReplicatedContentHashAlgorithm algorithm, string value, string parameterName)
		{
			switch (algorithm)
			{
				case ModInstallationReplicatedContentHashAlgorithm.Md5:
					return RequireCanonicalMd5(value, parameterName);
				case ModInstallationReplicatedContentHashAlgorithm.Xxh64:
					return RequireCanonicalXxh64(value, parameterName);
				default:
					throw new ArgumentOutOfRangeException(nameof(algorithm));
			}
		}

		private static string RequireCanonicalMd5(string value, string parameterName)
		{
			if (string.IsNullOrWhiteSpace(value) || !StringComparer.Ordinal.Equals(value, value.Trim()) || value.Length != 32)
				throw new ArgumentException("Replicated-layout content MD5 must be a canonical 32-character hexadecimal digest.", parameterName);
			for (int index = 0; index < value.Length; index++)
			{
				char character = value[index];
				bool isCanonicalHex = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
				if (!isCanonicalHex)
					throw new ArgumentException("Replicated-layout content MD5 must contain only lowercase hexadecimal characters.", parameterName);
			}
			return value;
		}

		private static string RequireCanonicalXxh64(string value, string parameterName)
		{
			if (String.IsNullOrWhiteSpace(value) || !StringComparer.Ordinal.Equals(value, value.Trim()))
				throw new ArgumentException("Replicated-layout content XXH64 must be a canonical base64-encoded 64-bit digest.", parameterName);
			byte[] decoded;
			try
			{
				decoded = Convert.FromBase64String(value);
			}
			catch (FormatException ex)
			{
				throw new ArgumentException("Replicated-layout content XXH64 must be a canonical base64-encoded 64-bit digest.", parameterName, ex);
			}
			if (decoded.Length != 8 || !StringComparer.Ordinal.Equals(Convert.ToBase64String(decoded), value))
				throw new ArgumentException("Replicated-layout content XXH64 must be a canonical base64-encoded 64-bit digest.", parameterName);
			return value;
		}
	}

	/// <summary>
	/// Stores the immutable desired output tree for a byte-preserving replicated-layout recipe.
	/// </summary>
	/// <remarks>
	/// Archive files not represented by <see cref="Files"/> are intentionally excluded. Several outputs may resolve to the
	/// same verified archive source, allowing replication, and each output may rename/remap that source to a different native
	/// destination. Byte-changing patches and other customized-file transformations are intentionally outside this contract.
	/// </remarks>
	public sealed class ModInstallationReplicatedLayoutRecipe
	{
		private readonly ReadOnlyCollection<ModInstallationReplicatedFile> m_rocFiles;

		/// <summary>
		/// Initializes a replicated layout from the complete ordered set of desired output files.
		/// </summary>
		/// <param name="files">The desired output files in deterministic native operation order.</param>
		public ModInstallationReplicatedLayoutRecipe(IEnumerable<ModInstallationReplicatedFile> files)
		{
			if (files == null)
				throw new ArgumentNullException(nameof(files));
			var copiedFiles = new List<ModInstallationReplicatedFile>();
			var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ModInstallationReplicatedFile file in files)
			{
				if (file == null)
					throw new ArgumentException("Replicated-layout files cannot contain null values.", nameof(files));
				if (!destinations.Add(file.DestinationPath))
					throw new ArgumentException("A replicated-layout recipe cannot produce several files at the same native destination.", nameof(files));
				copiedFiles.Add(file);
			}
			if (copiedFiles.Count == 0)
				throw new ArgumentException("A replicated-layout recipe requires at least one desired output file.", nameof(files));
			m_rocFiles = new ReadOnlyCollection<ModInstallationReplicatedFile>(copiedFiles);
		}

		/// <summary>
		/// Gets the complete desired output files in deterministic native operation order.
		/// </summary>
		public IReadOnlyList<ModInstallationReplicatedFile> Files
		{
			get { return m_rocFiles; }
		}
	}
}
