using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Translates a validated simple exact-file recipe into existing native scripted installation operations.
	/// </summary>
	/// <remarks>
	/// This adapter performs translation only. It does not execute operations, resolve deployment/overwrite decisions,
	/// register mods, write replay artifacts or mutate native ownership state.
	/// </remarks>
	public sealed class ModInstallationSimpleFileRecipeAdapter
	{
		/// <summary>
		/// Identifies the C5.4 simple exact-file adapter contract.
		/// </summary>
		public const string AdapterId = "nmm-ce.native.simple-file";

		/// <summary>
		/// Identifies the supported simple exact-file adapter contract version.
		/// </summary>
		public const int AdapterVersion = 1;

		/// <summary>
		/// Identifies the only native recipe capability consumed by this adapter.
		/// </summary>
		public const string CapabilityId = "simple-file";

		/// <summary>
		/// Identifies the supported simple-file capability contract version.
		/// </summary>
		public const int CapabilityVersion = 1;

		/// <summary>
		/// Translates one validated exact-file recipe into an ordered native scripted installation plan.
		/// </summary>
		/// <param name="recipeInput">The validated native recipe envelope established by C5.1-C5.3.</param>
		/// <param name="recipe">The immutable simple exact-file mappings to translate.</param>
		/// <returns>A new immutable recipe input carrying one <see cref="InstallModFileOperation"/> per mapping.</returns>
		public ModInstallationRecipeInput Translate(ModInstallationRecipeInput recipeInput, ModInstallationSimpleFileRecipe recipe)
		{
			if (recipeInput == null)
				throw new ArgumentNullException(nameof(recipeInput));
			if (recipe == null)
				throw new ArgumentNullException(nameof(recipe));

			ValidateAdapterContract(recipeInput.Validation);
			ValidateDeclaredPaths(recipeInput.Validation.Paths, recipe.Mappings);

			var plan = new ScriptedInstallationPlan();
			foreach (ModInstallationSimpleFileMapping mapping in recipe.Mappings)
				plan.Add(new InstallModFileOperation(mapping.SourcePath, mapping.DestinationPath));

			return recipeInput.WithNativePlan(plan.Operations);
		}

		/// <summary>Attaches a bounded prepared file/generated/value plan, including configuration-only Collection members.</summary>
		/// <remarks>Generated content and structured values are already represented by the durable reviewed-workflow contract.
		/// This entry point consumes exactly the declared path set and never accepts executable installer operations.</remarks>
		public ModInstallationRecipeInput TranslatePreparedPlan(ModInstallationRecipeInput input, IEnumerable<ScriptedInstallOperation> operations)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (operations == null) throw new ArgumentNullException(nameof(operations));
			ValidateAdapterContract(input.Validation);
			var copied = operations.ToList();
			if (copied.Count == 0 || copied.Count > 1000000) throw new InvalidDataException("Prepared native recipe has an invalid operation count.");
			var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var values = new HashSet<string>(StringComparer.Ordinal);
			foreach (ScriptedInstallOperation operation in copied)
			{
				InstallModFileOperation file = operation as InstallModFileOperation;
				GenerateDataFileOperation generated = operation as GenerateDataFileOperation;
				EditGameSpecificValueOperation value = operation as EditGameSpecificValueOperation;
				if (value != null && value.HasResolvedOverwriteDecision && value.Value != null && !String.IsNullOrWhiteSpace(value.Key))
				{
					if (!values.Add(value.Key)) throw new InvalidDataException("Prepared native recipe contains duplicate value edits.");
					continue;
				}
				string source = file != null && file.DeploymentDecision == null ? file.SourcePath :
					generated != null && generated.DeploymentDecision == null && generated.Data != null ? generated.PreparationSourcePath : null;
				string destination = file != null ? file.DestinationPath : generated?.DestinationPath;
				if (String.IsNullOrWhiteSpace(source) || String.IsNullOrWhiteSpace(destination)) throw new InvalidDataException("Prepared recipe contains an unsupported or unresolved operation.");
				var sourcePath = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, source);
				var targetPath = new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, destination);
				paths.Add(CreatePathKey(sourcePath.Kind, sourcePath.Path)); paths.Add(CreatePathKey(targetPath.Kind, targetPath.Path));
				if (!destinations.Add(targetPath.Path)) throw new InvalidDataException("Prepared native recipe contains duplicate file destinations.");
			}
			var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ModInstallationRecipePath path in input.Validation.Paths)
				if (!declared.Add(CreatePathKey(path.Kind, path.Path))) throw new InvalidDataException("Prepared native recipe contains duplicate path declarations.");
			if (!paths.SetEquals(declared)) throw new InvalidDataException("Prepared native effects do not consume the exact declared recipe paths.");
			return input.WithNativePlan(copied);
		}

		/// <summary>
		/// Verifies that the recipe was validated specifically for the exact adapter/capability contract implemented here.
		/// </summary>
		private static void ValidateAdapterContract(ModInstallationRecipeValidation validation)
		{
			if (!StringComparer.Ordinal.Equals(validation.AdapterId, AdapterId) || validation.AdapterVersion != AdapterVersion)
			{
				throw new NotSupportedException(string.Format(
					"The simple-file adapter cannot translate recipe adapter '{0}' version {1}.",
					validation.AdapterId, validation.AdapterVersion));
			}

			if (validation.Capabilities.Count != 1 ||
				!StringComparer.Ordinal.Equals(validation.Capabilities[0].CapabilityId, CapabilityId) ||
				validation.Capabilities[0].Version != CapabilityVersion)
			{
				throw new NotSupportedException("The simple-file adapter requires exactly simple-file capability version 1 and cannot ignore additional recipe capabilities.");
			}
		}

		/// <summary>
		/// Verifies that the adapter consumes exactly the source/destination path set admitted by the C5.3 validation record.
		/// </summary>
		private static void ValidateDeclaredPaths(IReadOnlyList<ModInstallationRecipePath> declaredPaths,
			IReadOnlyList<ModInstallationSimpleFileMapping> mappings)
		{
			var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ModInstallationSimpleFileMapping mapping in mappings)
			{
				expected.Add(CreatePathKey(ModInstallationRecipePathKind.ArchiveSource, mapping.SourcePath));
				expected.Add(CreatePathKey(ModInstallationRecipePathKind.Destination, mapping.DestinationPath));
			}

			var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ModInstallationRecipePath path in declaredPaths)
			{
				string key = CreatePathKey(path.Kind, path.Path);
				if (!declared.Add(key))
					throw new InvalidDataException("Recipe validation contains duplicate path declarations.");
				if (!expected.Contains(key))
					throw new InvalidDataException("Recipe validation contains a path which is not consumed by the simple exact-file recipe.");
			}

			if (declared.Count != expected.Count)
				throw new InvalidDataException("The simple exact-file recipe contains a source or destination path which was not admitted by recipe validation.");
		}

		/// <summary>
		/// Creates a comparison key that keeps archive-source and destination namespaces distinct while comparing Windows paths case-insensitively.
		/// </summary>
		private static string CreatePathKey(ModInstallationRecipePathKind kind, string path)
		{
			return ((int)kind).ToString() + "\0" + path;
		}
	}
}
