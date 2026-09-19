using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Describes one desired destination in a byte-preserving replicated-layout recipe.
	/// </summary>
	/// <remarks>
	/// <see cref="ContentMd5"/> is a content-matching key used to locate bytes inside the already SHA-256-verified
	/// archive from C5.3. It is not an independent integrity guarantee. An optional source hint may disambiguate two
	/// archive entries whose bytes produce the same matching digest.
	/// </remarks>
	public sealed class ModInstallationReplicatedFile
	{
		/// <summary>
		/// Initializes one replicated output requirement.
		/// </summary>
		/// <param name="destinationPath">The logical destination path relative to the validated native install root.</param>
		/// <param name="contentMd5">The canonical lowercase MD5 digest used to match the desired bytes inside the verified archive.</param>
		/// <param name="sourcePathHint">An optional exact archive-relative source path used to resolve an otherwise ambiguous content match.</param>
		public ModInstallationReplicatedFile(string destinationPath, string contentMd5, string sourcePathHint = null)
		{
			DestinationPath = new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, destinationPath).Path;
			ContentMd5 = RequireCanonicalMd5(contentMd5, nameof(contentMd5));
			SourcePathHint = sourcePathHint == null
				? null
				: new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, sourcePathHint).Path;
		}

		/// <summary>
		/// Gets the canonical destination path relative to the native install root.
		/// </summary>
		public string DestinationPath { get; }

		/// <summary>
		/// Gets the canonical lowercase MD5 digest used only for content matching within the verified archive.
		/// </summary>
		public string ContentMd5 { get; }

		/// <summary>
		/// Gets the optional canonical archive-relative source path used to disambiguate a matching digest.
		/// </summary>
		public string SourcePathHint { get; }

		/// <summary>
		/// Validates a canonical lowercase MD5 content-matching digest.
		/// </summary>
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
