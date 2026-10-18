using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;

namespace Nexus.Client.Games
{
	/// <summary>Plans package-local generated files without reading other installed mods or writing native state.</summary>
	public interface IDeterministicPackageFilePlanProvider
	{
		/// <summary>Gets whether basic installation must execute an exact generated-file recipe.</summary>
		bool HasPackageFilePlan { get; }

		/// <summary>Consumes exact selected archive sources and returns their immutable generated replacements.</summary>
		PackageGeneratedFilePlan GetPackageFilePlan(IMod mod, ModInstallContext context,
			IReadOnlyList<KeyValuePair<string, string>> selectedFiles);
	}

	/// <summary>Separates consumed archive inputs from ordinary native-owned generated outputs.</summary>
	public sealed class PackageGeneratedFilePlan
	{
		/// <summary>Copies exact source provenance and generated payloads for one package.</summary>
		public PackageGeneratedFilePlan(IEnumerable<string> consumedSources, IEnumerable<BasicInstallGeneratedFile> generatedFiles)
		{
			if (consumedSources == null) throw new ArgumentNullException(nameof(consumedSources));
			if (generatedFiles == null) throw new ArgumentNullException(nameof(generatedFiles));
			List<string> sources = consumedSources.ToList();
			List<BasicInstallGeneratedFile> outputs = generatedFiles.ToList();
			if (sources.Any(String.IsNullOrWhiteSpace) || sources.Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count ||
				outputs.Any(file => file == null || !sources.Contains(file.SourcePath, StringComparer.OrdinalIgnoreCase)) ||
				(sources.Count == 0) != (outputs.Count == 0))
				throw new ArgumentException("Generated package files require unique consumed sources and exact source provenance.");
			ConsumedSources = new ReadOnlyCollection<string>(sources);
			GeneratedFiles = new ReadOnlyCollection<BasicInstallGeneratedFile>(outputs);
		}

		public IReadOnlyList<string> ConsumedSources { get; }
		public IReadOnlyList<BasicInstallGeneratedFile> GeneratedFiles { get; }
	}
}
