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

			bool needsMd5 = false;
			bool needsXxh64 = false;
			foreach (ModInstallationReplicatedFile file in recipe.Files)
			{
				needsMd5 |= file.HashAlgorithm == ModInstallationReplicatedContentHashAlgorithm.Md5;
				needsXxh64 |= file.HashAlgorithm == ModInstallationReplicatedContentHashAlgorithm.Xxh64;
			}
			ArchiveContentIndex archiveIndex = BuildArchiveContentIndex(mod, needsMd5, needsXxh64);
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
		private static ArchiveContentIndex BuildArchiveContentIndex(IMod mod, bool needsMd5, bool needsXxh64)
		{
			List<string> fileList = mod.GetFileList();
			if (fileList == null)
				throw new InvalidDataException("The verified mod archive did not expose a file list for replicated-layout translation.");

			var hashesByPath = new Dictionary<string, ArchiveContentDigests>(StringComparer.OrdinalIgnoreCase);
			var pathsByMd5 = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			var pathsByXxh64 = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			foreach (string archivePath in fileList)
			{
				string canonicalPath = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, archivePath).Path;
				if (hashesByPath.ContainsKey(canonicalPath))
					throw new InvalidDataException(string.Format("The verified archive contains ambiguous duplicate path '{0}'.", canonicalPath));

				byte[] fileBytes = mod.GetFile(archivePath);
				if (fileBytes == null)
					throw new InvalidDataException(string.Format("The verified archive could not read '{0}' for replicated-layout matching.", canonicalPath));

				string contentMd5 = null;
				if (needsMd5)
				{
					using (MD5 md5 = MD5.Create())
						contentMd5 = ToHex(md5.ComputeHash(fileBytes));
					AddHashPath(pathsByMd5, contentMd5, canonicalPath);
				}
				string contentXxh64 = null;
				if (needsXxh64)
				{
					contentXxh64 = ComputeXxh64CanonicalBase64(fileBytes);
					AddHashPath(pathsByXxh64, contentXxh64, canonicalPath);
				}
				hashesByPath.Add(canonicalPath, new ArchiveContentDigests(contentMd5, contentXxh64));
			}

			return new ArchiveContentIndex(hashesByPath, pathsByMd5, pathsByXxh64);
		}

		private static void AddHashPath(Dictionary<string, List<string>> pathsByHash, string digest, string path)
		{
			List<string> matchingPaths;
			if (!pathsByHash.TryGetValue(digest, out matchingPaths))
			{
				matchingPaths = new List<string>();
				pathsByHash.Add(digest, matchingPaths);
			}
			matchingPaths.Add(path);
		}

		/// <summary>
		/// Resolves one desired output to exactly one archive source, rejecting missing or ambiguous content matches.
		/// </summary>
		private static string ResolveSourcePath(ArchiveContentIndex archiveIndex, ModInstallationReplicatedFile file)
		{
			if (file.SourcePathHint != null)
			{
				ArchiveContentDigests hintedHashes;
				if (!archiveIndex.HashesByPath.TryGetValue(file.SourcePathHint, out hintedHashes))
					throw new InvalidDataException(string.Format("Replicated-layout source hint '{0}' was not found in the verified archive.", file.SourcePathHint));
				if (!StringComparer.Ordinal.Equals(hintedHashes.Get(file.HashAlgorithm), file.ContentHash))
					throw new InvalidDataException(string.Format("Replicated-layout source hint '{0}' does not match the requested content digest for '{1}'.", file.SourcePathHint, file.DestinationPath));
				return FindCanonicalPath(archiveIndex.HashesByPath, file.SourcePathHint);
			}

			Dictionary<string, List<string>> pathsByHash = archiveIndex.GetPathsByHash(file.HashAlgorithm);
			List<string> matches;
			if (!pathsByHash.TryGetValue(file.ContentHash, out matches) || matches.Count == 0)
				throw new InvalidDataException(string.Format("The requested replicated content for '{0}' was not found in the verified archive.", file.DestinationPath));
			if (matches.Count != 1)
				throw new InvalidDataException(string.Format("The requested replicated content for '{0}' matches several archive files; an explicit source path is required.", file.DestinationPath));

			return matches[0];
		}

		private static string FindCanonicalPath(Dictionary<string, ArchiveContentDigests> hashesByPath, string path)
		{
			foreach (string candidate in hashesByPath.Keys)
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

		/// <summary>Computes the zero-seed XXH64 digest and returns xxHash's canonical big-endian bytes as standard base64.</summary>
		private static string ComputeXxh64CanonicalBase64(byte[] data)
		{
			ulong hash = ComputeXxh64(data);
			byte[] canonical = new byte[8];
			for (int index = 0; index < canonical.Length; index++)
				canonical[index] = (byte)(hash >> (56 - (index * 8)));
			return Convert.ToBase64String(canonical);
		}

		private static ulong ComputeXxh64(byte[] data)
		{
			const ulong prime1 = 11400714785074694791UL;
			const ulong prime2 = 14029467366897019727UL;
			const ulong prime3 = 1609587929392839161UL;
			const ulong prime4 = 9650029242287828579UL;
			const ulong prime5 = 2870177450012600261UL;
			int offset = 0;
			ulong hash;
			unchecked
			{
				if (data.Length >= 32)
				{
					ulong v1 = prime1 + prime2;
					ulong v2 = prime2;
					ulong v3 = 0;
					ulong v4 = 0UL - prime1;
					int limit = data.Length - 32;
					do
					{
						v1 = Xxh64Round(v1, ReadUInt64LittleEndian(data, offset)); offset += 8;
						v2 = Xxh64Round(v2, ReadUInt64LittleEndian(data, offset)); offset += 8;
						v3 = Xxh64Round(v3, ReadUInt64LittleEndian(data, offset)); offset += 8;
						v4 = Xxh64Round(v4, ReadUInt64LittleEndian(data, offset)); offset += 8;
					} while (offset <= limit);
					hash = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
					hash = Xxh64MergeRound(hash, v1);
					hash = Xxh64MergeRound(hash, v2);
					hash = Xxh64MergeRound(hash, v3);
					hash = Xxh64MergeRound(hash, v4);
				}
				else
				{
					hash = prime5;
				}

				hash += (ulong)data.Length;
				while (offset <= data.Length - 8)
				{
					ulong lane = Xxh64Round(0, ReadUInt64LittleEndian(data, offset));
					hash ^= lane;
					hash = RotateLeft(hash, 27) * prime1 + prime4;
					offset += 8;
				}
				if (offset <= data.Length - 4)
				{
					hash ^= (ulong)ReadUInt32LittleEndian(data, offset) * prime1;
					hash = RotateLeft(hash, 23) * prime2 + prime3;
					offset += 4;
				}
				while (offset < data.Length)
				{
					hash ^= data[offset] * prime5;
					hash = RotateLeft(hash, 11) * prime1;
					offset++;
				}
				hash ^= hash >> 33;
				hash *= prime2;
				hash ^= hash >> 29;
				hash *= prime3;
				hash ^= hash >> 32;
			}
			return hash;
		}

		private static ulong Xxh64Round(ulong accumulator, ulong input)
		{
			const ulong prime1 = 11400714785074694791UL;
			const ulong prime2 = 14029467366897019727UL;
			unchecked
			{
				accumulator += input * prime2;
				accumulator = RotateLeft(accumulator, 31);
				return accumulator * prime1;
			}
		}

		private static ulong Xxh64MergeRound(ulong accumulator, ulong value)
		{
			const ulong prime1 = 11400714785074694791UL;
			const ulong prime4 = 9650029242287828579UL;
			unchecked
			{
				accumulator ^= Xxh64Round(0, value);
				return accumulator * prime1 + prime4;
			}
		}

		private static ulong RotateLeft(ulong value, int count)
		{
			return (value << count) | (value >> (64 - count));
		}

		private static ulong ReadUInt64LittleEndian(byte[] data, int offset)
		{
			return (ulong)data[offset] |
				((ulong)data[offset + 1] << 8) |
				((ulong)data[offset + 2] << 16) |
				((ulong)data[offset + 3] << 24) |
				((ulong)data[offset + 4] << 32) |
				((ulong)data[offset + 5] << 40) |
				((ulong)data[offset + 6] << 48) |
				((ulong)data[offset + 7] << 56);
		}

		private static uint ReadUInt32LittleEndian(byte[] data, int offset)
		{
			return (uint)(data[offset] |
				(data[offset + 1] << 8) |
				(data[offset + 2] << 16) |
				(data[offset + 3] << 24));
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
		private sealed class ArchiveContentDigests
		{
			public ArchiveContentDigests(string md5, string xxh64)
			{
				Md5 = md5;
				Xxh64 = xxh64;
			}
			public string Md5 { get; }
			public string Xxh64 { get; }
			public string Get(ModInstallationReplicatedContentHashAlgorithm algorithm)
			{
				switch (algorithm)
				{
					case ModInstallationReplicatedContentHashAlgorithm.Md5: return Md5;
					case ModInstallationReplicatedContentHashAlgorithm.Xxh64: return Xxh64;
					default: throw new ArgumentOutOfRangeException(nameof(algorithm));
				}
			}
		}

		private sealed class ArchiveContentIndex
		{
			public ArchiveContentIndex(Dictionary<string, ArchiveContentDigests> hashesByPath,
				Dictionary<string, List<string>> pathsByMd5, Dictionary<string, List<string>> pathsByXxh64)
			{
				HashesByPath = hashesByPath;
				PathsByMd5 = pathsByMd5;
				PathsByXxh64 = pathsByXxh64;
			}
			public Dictionary<string, ArchiveContentDigests> HashesByPath { get; }
			public Dictionary<string, List<string>> PathsByMd5 { get; }
			public Dictionary<string, List<string>> PathsByXxh64 { get; }
			public Dictionary<string, List<string>> GetPathsByHash(ModInstallationReplicatedContentHashAlgorithm algorithm)
			{
				switch (algorithm)
				{
					case ModInstallationReplicatedContentHashAlgorithm.Md5: return PathsByMd5;
					case ModInstallationReplicatedContentHashAlgorithm.Xxh64: return PathsByXxh64;
					default: throw new ArgumentOutOfRangeException(nameof(algorithm));
				}
			}
		}
	}
}
