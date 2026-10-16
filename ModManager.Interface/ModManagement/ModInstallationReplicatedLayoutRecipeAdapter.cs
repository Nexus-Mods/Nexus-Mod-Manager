using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.Util;
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
			return Translate(recipeInput, mod, recipe, CancellationToken.None, null);
		}

		/// <summary>Resolves replicated sources with cancellable batch extraction and bounded progress reporting.</summary>
		public ModInstallationRecipeInput Translate(ModInstallationRecipeInput recipeInput, IMod mod,
			ModInstallationReplicatedLayoutRecipe recipe, CancellationToken cancellationToken, Action<float> progress)
		{
			return Translate(recipeInput, mod, recipe, cancellationToken, progress, false);
		}

		/// <summary>Optionally trusts unambiguous archive paths, retaining digest matching for renamed or alternative payloads.</summary>
		public ModInstallationRecipeInput Translate(ModInstallationRecipeInput recipeInput, IMod mod,
			ModInstallationReplicatedLayoutRecipe recipe, CancellationToken cancellationToken, Action<float> progress, bool skipContentChecks)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (recipeInput == null)
				throw new ArgumentNullException(nameof(recipeInput));
			if (mod == null)
				throw new ArgumentNullException(nameof(mod));
			if (recipe == null)
				throw new ArgumentNullException(nameof(recipe));

			ValidateAdapterContract(recipeInput.Validation);
			ValidateDeclaredPaths(recipeInput.Validation.Paths, recipe.Files);

			var inventory = mod as IModInstallerSourceInventory;
			List<string> fileList = inventory == null ? mod.GetFileList() : inventory.GetInstallerSourceFiles(null, true);
			if (fileList == null)
				throw new InvalidDataException("The verified mod archive did not expose a file list for replicated-layout translation.");
			var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string path in fileList)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string canonical = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, path).Path;
				if (sources.ContainsKey(canonical))
					throw new InvalidDataException(String.Format("The verified archive contains ambiguous duplicate path '{0}'.", canonical));
				sources.Add(canonical, path);
			}
			var resolvedPaths = new Dictionary<ModInstallationReplicatedFile, string>();
			if (skipContentChecks)
			{
				var destinations = new HashSet<string>(recipe.Files.Where(x => x.SourcePathHint == null)
					.Select(x => x.DestinationPath), StringComparer.OrdinalIgnoreCase);
				var uniqueSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (string path in sources.Keys)
				{
					cancellationToken.ThrowIfCancellationRequested();
					int start = 0;
					while (start < path.Length)
					{
						string suffix = path.Substring(start);
						if (destinations.Contains(suffix))
							uniqueSources[suffix] = uniqueSources.ContainsKey(suffix) ? null : path;
						int separator = path.IndexOf('\\', start);
						if (separator < 0) break;
						start = separator + 1;
					}
				}
				foreach (ModInstallationReplicatedFile file in recipe.Files)
				{
					string path;
					if (file.SourcePathHint != null)
					{
						if (!sources.ContainsKey(file.SourcePathHint))
							throw new InvalidDataException(String.Format("Replicated-layout source hint '{0}' was not found in the verified archive.", file.SourcePathHint));
						resolvedPaths.Add(file, file.SourcePathHint);
					}
					else if (uniqueSources.TryGetValue(file.DestinationPath, out path) && path != null)
						resolvedPaths.Add(file, path);
				}
			}
			List<ModInstallationReplicatedFile> unresolvedFiles = recipe.Files.Where(x => !resolvedPaths.ContainsKey(x)).ToList();
			bool needsMd5 = false;
			bool needsXxh64 = false;
			foreach (ModInstallationReplicatedFile file in unresolvedFiles)
			{
				needsMd5 |= file.HashAlgorithm == ModInstallationReplicatedContentHashAlgorithm.Md5;
				needsXxh64 |= file.HashAlgorithm == ModInstallationReplicatedContentHashAlgorithm.Xxh64;
			}
			ArchiveContentIndex archiveIndex = unresolvedFiles.Count == 0 ? null
				: BuildArchiveContentIndex(mod, sources, unresolvedFiles, needsMd5, needsXxh64, cancellationToken, progress);
			var plan = new ScriptedInstallationPlan();
			foreach (ModInstallationReplicatedFile file in recipe.Files)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string sourcePath;
				if (!resolvedPaths.TryGetValue(file, out sourcePath)) sourcePath = ResolveSourcePath(archiveIndex, file);
				plan.Add(new InstallModFileOperation(sourcePath, file.DestinationPath));
			}

			if (archiveIndex == null) progress?.Invoke(1f);
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
		/// Matches archive content using one batch extraction when available, retaining strong comparison for ambiguous sources.
		/// </summary>
		private static ArchiveContentIndex BuildArchiveContentIndex(IMod mod, Dictionary<string, string> sources,
			IReadOnlyList<ModInstallationReplicatedFile> files, bool needsMd5, bool needsXxh64,
			CancellationToken cancellationToken, Action<float> progress)
		{
			string temporaryDirectory = null;
			try
			{
				IModFileBatchSource batchSource = mod as IModFileBatchSource;
				var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				progress?.Invoke(0f);
				if (batchSource != null && sources.Count > 0)
				{
					// A per-entry GetFile call can decode the solid block again for every texture.
					// Use the existing batch source once, with flat short scratch names.
					temporaryDirectory = Path.Combine(Path.GetTempPath(), "NMMr-" + FileUtil.CreateTempToken());
					Directory.CreateDirectory(temporaryDirectory);
					var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					foreach (KeyValuePair<string, string> source in sources)
					{
						string destination = Path.Combine(temporaryDirectory, destinations.Count.ToString("x8", CultureInfo.InvariantCulture) + ".bin");
						destinations.Add(source.Value, destination);
						extracted.Add(source.Key, destination);
					}
					batchSource.CopyFilesTo(destinations, cancellationToken, value => progress?.Invoke(value * 0.5f));
				}
				Func<string, byte[]> read = path => extracted.Count > 0
					? File.ReadAllBytes(FileUtil.GetIoPath(extracted[path])) : mod.GetFile(sources[path]);
				var hashesByPath = new Dictionary<string, ArchiveContentDigests>(StringComparer.OrdinalIgnoreCase);
				var pathsByMd5 = new Dictionary<string, List<string>>(StringComparer.Ordinal);
				var pathsByXxh64 = new Dictionary<string, List<string>>(StringComparer.Ordinal);
				int completed = 0;
				foreach (string canonicalPath in sources.Keys)
				{
					cancellationToken.ThrowIfCancellationRequested();
					byte[] fileBytes = read(canonicalPath);
					if (fileBytes == null)
						throw new InvalidDataException(string.Format("The verified archive could not read '{0}' for replicated-layout matching.", canonicalPath));

					string contentMd5 = null;
					if (needsMd5)
					{
						using (MD5 md5 = MD5.Create()) contentMd5 = ToHex(md5.ComputeHash(fileBytes));
						AddHashPath(pathsByMd5, contentMd5, canonicalPath);
					}
					string contentXxh64 = null;
					if (needsXxh64)
					{
						contentXxh64 = ComputeXxh64CanonicalBase64(fileBytes);
						AddHashPath(pathsByXxh64, contentXxh64, canonicalPath);
					}
					hashesByPath.Add(canonicalPath, new ArchiveContentDigests(contentMd5, contentXxh64, fileBytes.LongLength, null));
					completed++;
					if (completed % 32 == 0 || completed == sources.Count)
						progress?.Invoke(0.5f + 0.4f * completed / sources.Count);
				}

				var index = new ArchiveContentIndex(hashesByPath, pathsByMd5, pathsByXxh64);
				var ambiguousPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (ModInstallationReplicatedFile file in files.Where(x => x.SourcePathHint == null))
				{
					List<string> matches;
					if (index.GetPathsByHash(file.HashAlgorithm).TryGetValue(file.ContentHash, out matches) && matches.Count > 1)
						ambiguousPaths.UnionWith(matches);
				}
				// Strong comparison is needed only when one requested digest identifies several sources.
				// Unique matches do not need a second digest of every texture in the archive.
				int compared = 0;
				foreach (string path in ambiguousPaths)
				{
					cancellationToken.ThrowIfCancellationRequested();
					ArchiveContentDigests digest = hashesByPath[path];
					using (SHA256 sha256 = SHA256.Create())
						hashesByPath[path] = new ArchiveContentDigests(digest.Md5, digest.Xxh64, digest.ByteLength, ToHex(sha256.ComputeHash(read(path))));
					compared++;
					if (compared % 32 == 0 || compared == ambiguousPaths.Count)
						progress?.Invoke(0.9f + 0.1f * compared / ambiguousPaths.Count);
				}
				progress?.Invoke(1f);
				return index;
			}
			finally
			{
				if (temporaryDirectory != null && Directory.Exists(temporaryDirectory))
					FileUtil.ForceDelete(temporaryDirectory);
			}
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
		/// Resolves one desired output to a stable content-equivalent archive source, rejecting missing or differing content matches.
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
			ArchiveContentDigests first = archiveIndex.HashesByPath[matches[0]];
			if (matches.Any(path => archiveIndex.HashesByPath[path].ByteLength != first.ByteLength ||
				!StringComparer.Ordinal.Equals(archiveIndex.HashesByPath[path].Sha256, first.Sha256)))
				throw new InvalidDataException(string.Format("The requested replicated digest for '{0}' matches different archive contents; an explicit source path is required.", file.DestinationPath));

			// Source location does not change the requested output bytes. Prefer the destination's original
			// path, then the shortest matching archive-root prefix, with stable ordering for other copies.
			return matches.OrderBy(path => StringComparer.OrdinalIgnoreCase.Equals(path, file.DestinationPath) ? 0 :
				path.EndsWith("\\" + file.DestinationPath, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
				.ThenBy(path => path.Length).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).First();
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
			public ArchiveContentDigests(string md5, string xxh64, long byteLength, string sha256)
			{
				Md5 = md5;
				Xxh64 = xxh64;
				ByteLength = byteLength;
				Sha256 = sha256;
			}
			public string Md5 { get; }
			public string Xxh64 { get; }
			public long ByteLength { get; }
			public string Sha256 { get; }
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
