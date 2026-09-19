using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Describes one exact one-to-one archive-source to native-destination mapping in a simple installation recipe.
	/// </summary>
	public sealed class ModInstallationSimpleFileMapping
	{
		/// <summary>
		/// Initializes one canonical exact-file mapping using the common C5 recipe-path validation rules.
		/// </summary>
		/// <param name="sourcePath">The path of the source file relative to the immutable mod archive.</param>
		/// <param name="destinationPath">The logical destination path relative to the validated native install root.</param>
		public ModInstallationSimpleFileMapping(string sourcePath, string destinationPath)
		{
			SourcePath = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, sourcePath).Path;
			DestinationPath = new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, destinationPath).Path;
		}

		/// <summary>
		/// Gets the canonical archive-relative source path.
		/// </summary>
		public string SourcePath { get; }

		/// <summary>
		/// Gets the canonical destination path relative to the native install root.
		/// </summary>
		public string DestinationPath { get; }
	}

	/// <summary>
	/// Stores an immutable simple exact-file recipe containing only one-to-one archive file mappings.
	/// </summary>
	/// <remarks>
	/// Replication, transformations, generated files, installer choices and non-file effects intentionally remain outside
	/// this contract. Later C5 adapters add those independently after their own validation and ownership semantics exist.
	/// </remarks>
	public sealed class ModInstallationSimpleFileRecipe
	{
		private readonly ReadOnlyCollection<ModInstallationSimpleFileMapping> m_rocMappings;

		/// <summary>
		/// Initializes a simple recipe from an ordered sequence of exact one-to-one file mappings.
		/// </summary>
		/// <param name="mappings">The exact file mappings in deterministic native operation order.</param>
		public ModInstallationSimpleFileRecipe(IEnumerable<ModInstallationSimpleFileMapping> mappings)
		{
			if (mappings == null)
				throw new ArgumentNullException(nameof(mappings));

			var copiedMappings = new List<ModInstallationSimpleFileMapping>();
			var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var destinationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ModInstallationSimpleFileMapping mapping in mappings)
			{
				if (mapping == null)
					throw new ArgumentException("Simple file mappings cannot contain null values.", nameof(mappings));
				if (!sourcePaths.Add(mapping.SourcePath))
					throw new ArgumentException("A simple exact-file recipe cannot replicate one archive source to several destinations.", nameof(mappings));
				if (!destinationPaths.Add(mapping.DestinationPath))
					throw new ArgumentException("A simple exact-file recipe cannot map several archive sources to the same destination.", nameof(mappings));
				copiedMappings.Add(mapping);
			}

			if (copiedMappings.Count == 0)
				throw new ArgumentException("A simple exact-file recipe requires at least one file mapping.", nameof(mappings));

			m_rocMappings = new ReadOnlyCollection<ModInstallationSimpleFileMapping>(copiedMappings);
		}

		/// <summary>
		/// Gets the exact mappings in deterministic native operation order.
		/// </summary>
		public IReadOnlyList<ModInstallationSimpleFileMapping> Mappings
		{
			get { return m_rocMappings; }
		}
	}
}
