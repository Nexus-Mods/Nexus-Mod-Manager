using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Translates a validated replicated output tree into existing native archive-file installation operations.
	/// </summary>
	/// <remarks>
	/// This C5.8 adapter supports the characterized byte-preserving replicate subset: content-hash resolution, exclusions,
	/// renames/remaps and one-source-to-many-destination replication. It deliberately does not implement binary patches,
	/// arbitrary transformations, root-layout writes, external tools or game-specific merges.
	/// </remarks>
	public sealed class ModInstallationReplicatedLayoutRecipeAdapter
	{
		/// <summary>
		/// Identifies the C5.8 replicated-layout adapter contract.
		/// </summary>
		public const string AdapterId = "nmm-ce.native.replicated-layout";

		/// <summary>
		/// Identifies the supported replicated-layout adapter contract version.
		/// </summary>
		public const int AdapterVersion = 1;

		/// <summary>
		/// Identifies the only native recipe capability consumed by this adapter.
		/// </summary>
		public const string CapabilityId = "replicated-layout";

		/// <summary>
		/// Identifies the supported replicated-layout capability contract version.
		/// </summary>
		public const int CapabilityVersion = 1;

		/// <summary>
		/// Resolves verified archive content for each replicated output and produces native archive-file operations.
		/// </summary>
		/// <param name="recipeInput">The validated recipe envelope established by C5.1-C5.3.</param>
		/// <param name="mod">The verified immutable mod archive whose files are available to native installation.</param>
		/// <param name="recipe">The complete byte-preserving replicated output tree.</param>
		/// <returns>A new immutable recipe input carrying the translated native operation plan.</returns>
		public ModInstallationRecipeInput Translate(ModInstallationRecipeInput recipeInput, IMod mod,
			ModInstallationReplicatedLayoutRecipe recipe)
		{
			if (recipeInput == null)
				throw new ArgumentNullException(nameof(recipeInput));
			if (mod == null)
				throw new ArgumentNullException(nameof(mod));
			if (recipe == null)
				throw new ArgumentNullException(nameof(recipe));

			ValidateAdapterContract(recipeInput.Validation);
			ValidateDeclaredPaths(recipeInput.Validation.Paths, recipe.Files);

			ArchiveContentIndex archiveIndex = BuildArchiveContentIndex(mod);
			var plan = new ScriptedInstallationPlan();
			foreach (ModInstallationReplicatedFile file in recipe.Files)
			{
				string sourcePath = ResolveSourcePath(archiveIndex, file);
				plan.Add(new InstallModFileOperation(sourcePath, file.DestinationPath));
			}

			return recipeInput.WithNativePlan(plan.Operations);
		}

		/// <summary>
		/// Verifies that the recipe was validated specifically for the exact adapter/capability contract implemented here.
		/// </summary>
		private static void ValidateAdapterContract(ModInstallationRecipeValidation validation)
		{
			if (!StringComparer.Ordinal.Equals(validation.AdapterId, AdapterId) || validation.AdapterVersion != AdapterVersion)
			{
				throw new NotSupportedException(string.Format(
					"The replicated-layout adapter cannot translate recipe adapter '{0}' version {1}.",
					validation.AdapterId, validation.AdapterVersion));
			}

			if (validation.Capabilities.Count != 1 ||
				!StringComparer.Ordinal.Equals(validation.Capabilities[0].CapabilityId, CapabilityId) ||
				validation.Capabilities[0].Version != CapabilityVersion)
			{
				throw new NotSupportedException("The replicated-layout adapter requires exactly replicated-layout capability version 1 and cannot ignore additional recipe capabilities.");
			}
		}

		/// <summary>
		/// Verifies that C5.3 admitted every destination and every explicit source disambiguator consumed by the recipe.
		/// </summary>
		/// <remarks>
		/// Hash-resolved archive sources are intentionally not predeclared: resolving their exact source paths from the
		/// already SHA-256-verified immutable archive is the responsibility of this adapter. Only explicit source hints are
		/// caller-supplied source paths and therefore must appear at the C5.3 trust boundary.
		/// </remarks>
		private static void ValidateDeclaredPaths(IReadOnlyList<ModInstallationRecipePath> declaredPaths,
			IReadOnlyList<ModInstallationReplicatedFile> files)
		{
			var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ModInstallationReplicatedFile file in files)
			{
				expected.Add(CreatePathKey(ModInstallationRecipePathKind.Destination, file.DestinationPath));
				if (file.SourcePathHint != null)
					expected.Add(CreatePathKey(ModInstallationRecipePathKind.ArchiveSource, file.SourcePathHint));
			}

			var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ModInstallationRecipePath path in declaredPaths)
			{
				string key = CreatePathKey(path.Kind, path.Path);
				if (!declared.Add(key))
					throw new InvalidDataException("Recipe validation contains duplicate path declarations.");
				if (!expected.Contains(key))
					throw new InvalidDataException("Recipe validation contains a path which is not consumed by the replicated-layout recipe.");
			}

			if (declared.Count != expected.Count)
				throw new InvalidDataException("The replicated-layout recipe contains a destination or source hint which was not admitted by recipe validation.");
		}

		/// <summary>
		/// Enumerates and hashes the actual files in the verified archive once so requested content can be resolved unambiguously.
		/// </summary>
		private static ArchiveContentIndex BuildArchiveContentIndex(IMod mod)
		{
			List<string> fileList = mod.GetFileList();
			if (fileList == null)
				throw new InvalidDataException("The verified mod archive did not expose a file list for replicated-layout translation.");

			var hashByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var pathsByHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			foreach (string archivePath in fileList)
			{
				string canonicalPath = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, archivePath).Path;
				if (hashByPath.ContainsKey(canonicalPath))
					throw new InvalidDataException(string.Format("The verified archive contains ambiguous duplicate path '{0}'.", canonicalPath));

				byte[] fileBytes = mod.GetFile(archivePath);
				if (fileBytes == null)
					throw new InvalidDataException(string.Format("The verified archive could not read '{0}' for replicated-layout matching.", canonicalPath));

				string contentMd5;
				using (MD5 md5 = MD5.Create())
					contentMd5 = ToHex(md5.ComputeHash(fileBytes));

				hashByPath.Add(canonicalPath, contentMd5);
				List<string> matchingPaths;
				if (!pathsByHash.TryGetValue(contentMd5, out matchingPaths))
				{
					matchingPaths = new List<string>();
					pathsByHash.Add(contentMd5, matchingPaths);
				}
				matchingPaths.Add(canonicalPath);
			}

			return new ArchiveContentIndex(hashByPath, pathsByHash);
		}

		/// <summary>
		/// Resolves one desired output to exactly one archive source, rejecting missing or ambiguous content matches.
		/// </summary>
		private static string ResolveSourcePath(ArchiveContentIndex archiveIndex, ModInstallationReplicatedFile file)
		{
			if (file.SourcePathHint != null)
			{
				string hintedHash;
				if (!archiveIndex.HashByPath.TryGetValue(file.SourcePathHint, out hintedHash))
					throw new InvalidDataException(string.Format("Replicated-layout source hint '{0}' was not found in the verified archive.", file.SourcePathHint));
				if (!StringComparer.Ordinal.Equals(hintedHash, file.ContentMd5))
					throw new InvalidDataException(string.Format("Replicated-layout source hint '{0}' does not match the requested content digest for '{1}'.", file.SourcePathHint, file.DestinationPath));
				return FindCanonicalPath(archiveIndex.HashByPath, file.SourcePathHint);
			}

			List<string> matches;
			if (!archiveIndex.PathsByHash.TryGetValue(file.ContentMd5, out matches) || matches.Count == 0)
				throw new InvalidDataException(string.Format("The requested replicated content for '{0}' was not found in the verified archive.", file.DestinationPath));
			if (matches.Count != 1)
				throw new InvalidDataException(string.Format("The requested replicated content for '{0}' matches several archive files; an explicit source path is required.", file.DestinationPath));

			return matches[0];
		}

		/// <summary>
		/// Returns the archive's canonical casing for a case-insensitive source-hint match.
		/// </summary>
		private static string FindCanonicalPath(Dictionary<string, string> hashByPath, string path)
		{
			foreach (string candidate in hashByPath.Keys)
			{
				if (StringComparer.OrdinalIgnoreCase.Equals(candidate, path))
					return candidate;
			}
			throw new InvalidDataException(string.Format("Replicated-layout source hint '{0}' was not found in the verified archive.", path));
		}

		/// <summary>
		/// Creates a comparison key that keeps archive-source and destination namespaces distinct while comparing Windows paths case-insensitively.
		/// </summary>
		private static string CreatePathKey(ModInstallationRecipePathKind kind, string path)
		{
			return ((int)kind).ToString(CultureInfo.InvariantCulture) + "\0" + path;
		}

		/// <summary>
		/// Encodes a digest as canonical lowercase hexadecimal text.
		/// </summary>
		private static string ToHex(byte[] bytes)
		{
			var builder = new StringBuilder(bytes.Length * 2);
			foreach (byte value in bytes)
				builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
			return builder.ToString();
		}

		/// <summary>
		/// Holds one immutable translation-time index of archive paths and matching content digests.
		/// </summary>
		private sealed class ArchiveContentIndex
		{
			/// <summary>
			/// Initializes the archive content index.
			/// </summary>
			public ArchiveContentIndex(Dictionary<string, string> hashByPath, Dictionary<string, List<string>> pathsByHash)
			{
				HashByPath = hashByPath;
				PathsByHash = pathsByHash;
			}

			/// <summary>
			/// Gets archive content digests keyed by canonical source path.
			/// </summary>
			public Dictionary<string, string> HashByPath { get; }

			/// <summary>
			/// Gets canonical source paths grouped by matching content digest.
			/// </summary>
			public Dictionary<string, List<string>> PathsByHash { get; }
		}
	}
}
