using System;
using System.Collections.ObjectModel;
using System.IO;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Util;
using SevenZip;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Inspects a local/acquired Nexus Collection bundle and extracts only the bounded root collection.json manifest.
	/// </summary>
	/// <remarks>
	/// The importer does not extract arbitrary archive paths, install mods, follow URLs or mutate game/native state. C4
	/// later owns durable retention of the outer bundle and manifest bytes after their hashes have been verified.
	/// </remarks>
	public sealed class NexusCollectionBundleImporter
	{
		public const string ManifestFileName = "collection.json";
		private const int MaxArchiveEntries = 100000;

		private readonly NexusCollectionManifestNormalizer _normalizer;

		public NexusCollectionBundleImporter()
			: this(new NexusCollectionManifestNormalizer())
		{
		}

		public NexusCollectionBundleImporter(NexusCollectionManifestNormalizer normalizer)
		{
			_normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
		}

		/// <summary>
		/// Imports collection.json itself or an archive containing exactly one root collection.json entry.
		/// </summary>
		public NexusCollectionBundleImportResult ImportFile(string sourcePath, CollectionRevision revision)
		{
			if (string.IsNullOrWhiteSpace(sourcePath))
				throw new ArgumentException("A collection bundle path is required.", nameof(sourcePath));
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (!File.Exists(sourcePath))
				throw new FileNotFoundException("The collection bundle/import file does not exist.", sourcePath);

			string sourceName = Path.GetFileName(sourcePath);
			using (FileStream sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				long bundleByteLength = sourceStream.Length;
				CollectionContentHash bundleHash = CollectionContentHash.FromSha256(
					NexusCollectionManifestNormalizer.ComputeSha256Hex(sourceStream));

				if (StringComparer.OrdinalIgnoreCase.Equals(sourceName, ManifestFileName))
				{
					sourceStream.Position = 0;
					byte[] rawManifest = ReadBoundedManifest(sourceStream, bundleByteLength);
					NexusCollectionManifestNormalizationResult normalization = _normalizer.Normalize(rawManifest, revision);
					return new NexusCollectionBundleImportResult(
						NexusCollectionBundleInputKind.RawManifest,
						bundleHash,
						bundleByteLength,
						ManifestFileName,
						normalization);
				}

				// Keep the read-only handle open while SevenZip re-opens the path. On Windows this prevents a writer
				// from changing the archive between the outer hash and collection.json extraction.
				byte[] archiveManifest = ExtractRootManifest(sourcePath);
				NexusCollectionManifestNormalizationResult archiveNormalization = _normalizer.Normalize(archiveManifest, revision);
				return new NexusCollectionBundleImportResult(
					NexusCollectionBundleInputKind.Archive,
					bundleHash,
					bundleByteLength,
					ManifestFileName,
					archiveNormalization);
			}
		}

		/// <summary>
		/// Imports already-extracted collection.json bytes while preserving the exact byte identity.
		/// </summary>
		public NexusCollectionManifestNormalizationResult ImportManifest(byte[] rawManifestBytes, CollectionRevision revision)
		{
			return _normalizer.Normalize(rawManifestBytes, revision);
		}

		private static byte[] ReadBoundedManifest(Stream input, long byteLength)
		{
			if (byteLength > NexusCollectionManifestNormalizer.MaxManifestBytes)
				throw new InvalidDataException("collection.json exceeds the C2.5 bounded manifest size limit.");
			if (byteLength > Int32.MaxValue)
				throw new InvalidDataException("collection.json is too large to normalize in memory.");

			using (MemoryStream output = new MemoryStream((int)byteLength))
			{
				CopyBounded(input, output, NexusCollectionManifestNormalizer.MaxManifestBytes);
				return output.ToArray();
			}
		}

		private static byte[] ExtractRootManifest(string archivePath)
		{
			try
			{
				using (SevenZipExtractor extractor = Archive.GetExtractor(archivePath))
				{
					ReadOnlyCollection<ArchiveFileInfo> entries = extractor.ArchiveFileData;
					if (entries.Count > MaxArchiveEntries)
						throw new InvalidDataException("The collection bundle contains too many archive entries for bounded preview inspection.");

					ArchiveFileInfo manifestEntry = default(ArchiveFileInfo);
					bool foundManifest = false;
					foreach (ArchiveFileInfo entry in entries)
					{
						if (entry.IsDirectory)
							continue;

						string normalizedName = (entry.FileName ?? string.Empty).Replace('\\', '/');
						if (!StringComparer.OrdinalIgnoreCase.Equals(normalizedName, ManifestFileName))
							continue;

						if (foundManifest)
							throw new InvalidDataException("The collection bundle contains more than one root collection.json entry.");

						manifestEntry = entry;
						foundManifest = true;
					}

					if (!foundManifest)
						throw new InvalidDataException("The collection bundle does not contain a root collection.json manifest.");
					if (manifestEntry.Size > (ulong)NexusCollectionManifestNormalizer.MaxManifestBytes)
						throw new InvalidDataException("The collection bundle's collection.json exceeds the bounded manifest size limit.");

					int capacity = manifestEntry.Size > Int32.MaxValue ? 0 : (int)manifestEntry.Size;
					using (MemoryStream output = capacity > 0 ? new MemoryStream(capacity) : new MemoryStream())
					{
						extractor.ExtractFile(manifestEntry.Index, output);
						if (output.Length > NexusCollectionManifestNormalizer.MaxManifestBytes)
							throw new InvalidDataException("The extracted collection.json exceeded its bounded preview size.");
						return output.ToArray();
					}
				}
			}
			catch (InvalidDataException)
			{
				throw;
			}
			catch (SevenZipArchiveException ex)
			{
				throw new InvalidDataException("The collection bundle is not a readable supported archive.", ex);
			}
		}

		private static void CopyBounded(Stream input, Stream output, int maximumBytes)
		{
			byte[] buffer = new byte[64 * 1024];
			int total = 0;
			while (true)
			{
				int read = input.Read(buffer, 0, buffer.Length);
				if (read <= 0)
					break;

				total += read;
				if (total > maximumBytes)
					throw new InvalidDataException("collection.json exceeds the bounded preview size.");
				output.Write(buffer, 0, read);
			}
		}
	}
}
