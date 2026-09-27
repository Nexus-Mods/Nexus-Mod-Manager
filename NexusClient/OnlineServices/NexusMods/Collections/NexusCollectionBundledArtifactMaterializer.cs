using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Newtonsoft.Json.Linq;
using SevenZip;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Materializes one characterized Vortex <c>source.type == bundle</c> directory from an immutable retained Collection archive.
	/// </summary>
	/// <remarks>
	/// Vortex exports bundled mods as unpacked directories under <c>bundled/&lt;fileExpression&gt;/</c>; there is no nested source ZIP
	/// to copy. NMM therefore creates a deterministic ZIP from exactly that directory, seals the bytes into the retained artifact
	/// store, and later imports those same bytes through the normal AddMod path.
	/// </remarks>
	public sealed class NexusCollectionBundledArtifactMaterializer
	{
		private const int MaxArchiveEntries = 100000;
		private const long MaxMemberPayloadBytes = NexusCollectionBundleAcquirer.DefaultMaximumBundleBytes;
		private static readonly DateTimeOffset DeterministicZipTimestamp = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
		private readonly CollectionsStore _store;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;

		public NexusCollectionBundledArtifactMaterializer(CollectionsStore store, CollectionsRevisionSourceStore revisionSourceStore)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
			_revisionSourceStore = revisionSourceStore ?? throw new ArgumentNullException(nameof(revisionSourceStore));
			_artifactStore = new CollectionsRetainedArtifactStore(store);
		}

		public NexusCollectionBundledArtifactMaterialization Materialize(CollectionAcquisitionRequest request,
			CancellationToken cancellationToken)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			if (!CollectionBundledArtifactIdentity.IsBundle(request.SelectedArtifact))
				throw new ArgumentException("The acquisition request is not a characterized Collection bundle artifact.", nameof(request));
			if (request.SelectedArtifact.ExpectedContentHash != null)
				throw new InvalidDataException("Collection bundle logical identity must not pretend that Vortex recompressed bytes have a provider-stable content hash.");

			BundleManifestEntry source = ResolveManifestEntry(request, cancellationToken);
			string stagingDirectory = Path.Combine(_store.StoreDirectory, ".bundle-member-staging", request.RequestId.ToString("N"));
			Directory.CreateDirectory(stagingDirectory);
			string stagingPath = Path.Combine(stagingDirectory, CreateSafeStageFileName(source.FileExpression));
			string temporaryPath = stagingPath + ".tmp-" + Guid.NewGuid().ToString("N");

			try
			{
				using (Stream retainedBundle = _revisionSourceStore.OpenBundle(request.Revision, cancellationToken))
				using (SevenZipExtractor extractor = new SevenZipExtractor(retainedBundle))
				{
					IReadOnlyList<BundleArchiveEntry> selected = SelectEntries(extractor.ArchiveFileData, source, cancellationToken);
					using (FileStream output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
					using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
					{
						foreach (BundleArchiveEntry selectedEntry in selected)
						{
							cancellationToken.ThrowIfCancellationRequested();
							ZipArchiveEntry destination = zip.CreateEntry(selectedEntry.RelativePath, System.IO.Compression.CompressionLevel.NoCompression);
							destination.LastWriteTime = DeterministicZipTimestamp;
							using (Stream destinationStream = destination.Open())
								extractor.ExtractFile(selectedEntry.ArchiveIndex, destinationStream);
						}
					}
				}

				if (File.Exists(stagingPath)) File.Delete(stagingPath);
				File.Move(temporaryPath, stagingPath);
				CollectionsRetainedArtifact retained = _artifactStore.PublishFile(stagingPath, cancellationToken);
				return new NexusCollectionBundledArtifactMaterialization(retained, stagingPath, source.FileExpression, source.ReferenceTag);
			}
			catch
			{
				TryDelete(temporaryPath);
				throw;
			}
		}

		private BundleManifestEntry ResolveManifestEntry(CollectionAcquisitionRequest request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			byte[] manifestBytes = _revisionSourceStore.LoadManifest(request.Revision,
				NexusCollectionManifestNormalizer.SchemaIdentity, NexusCollectionManifestNormalizer.NormalizerVersion);
			JObject root;
			try { root = JObject.Parse(new System.Text.UTF8Encoding(false, true).GetString(manifestBytes)); }
			catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is System.Text.DecoderFallbackException)
			{
				throw new InvalidDataException("The retained Collection manifest could not be reparsed for embedded-member materialization.", ex);
			}

			var matches = new List<BundleManifestEntry>();
			JArray mods = root["mods"] as JArray;
			if (mods == null) throw new InvalidDataException("The retained Collection manifest no longer exposes its characterized mods array.");
			foreach (JObject member in mods.OfType<JObject>())
			{
				JObject source = member["source"] as JObject;
				if (source == null || !StringComparer.Ordinal.Equals((string)source["type"], "bundle")) continue;
				string tag = source["tag"] != null && source["tag"].Type == JTokenType.String ? (string)source["tag"] : null;
				if (!CollectionBundledArtifactIdentity.IsValidReferenceTag(tag)) continue;
				string stableId = CollectionBundledArtifactIdentity.Format(request.Revision, tag);
				if (!StringComparer.Ordinal.Equals(stableId, request.SelectedArtifact.StableId)) continue;

				string expression = source["fileExpression"] != null && source["fileExpression"].Type == JTokenType.String
					? (string)source["fileExpression"] : null;
				long fileSize;
				if (!IsSafeLeafName(expression) || !TryReadPositiveInt64(source["fileSize"], out fileSize) || fileSize > MaxMemberPayloadBytes)
					throw new InvalidDataException("The exact retained bundle member no longer satisfies the characterized bundle path/size contract.");
				matches.Add(new BundleManifestEntry(tag, expression, fileSize));
			}
			if (matches.Count != 1)
				throw new InvalidDataException(matches.Count == 0
					? "The retained Collection manifest does not contain the exact embedded member selected by the plan."
					: "The retained Collection manifest contains more than one embedded member with the selected stable bundle identity.");
			return matches[0];
		}

		private static IReadOnlyList<BundleArchiveEntry> SelectEntries(ReadOnlyCollection<ArchiveFileInfo> entries,
			BundleManifestEntry source, CancellationToken cancellationToken)
		{
			if (entries.Count > MaxArchiveEntries)
				throw new InvalidDataException("The retained Collection archive contains too many entries for bounded embedded-member materialization.");
			string prefix = "bundled/" + source.FileExpression + "/";
			var result = new List<BundleArchiveEntry>();
			var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			long payloadBytes = 0;
			foreach (ArchiveFileInfo entry in entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (entry.IsDirectory) continue;
				string name = (entry.FileName ?? String.Empty).Replace('\\', '/');
				if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
				string relative = name.Substring(prefix.Length);
				if (!IsSafeRelativeArchivePath(relative))
					throw new InvalidDataException("A bundled Collection member contains an unsafe or ambiguous archive-relative path.");
				if (!unique.Add(relative))
					throw new InvalidDataException("A bundled Collection member contains archive paths that collide on a Windows target.");
				if (entry.Size > Int64.MaxValue)
					throw new InvalidDataException("A bundled Collection member entry exceeds the supported bounded size range.");
				checked { payloadBytes += (long)entry.Size; }
				if (payloadBytes > source.FileSize || payloadBytes > MaxMemberPayloadBytes)
					throw new InvalidDataException("A bundled Collection member exceeds its manifest-declared or NMM bounded payload size.");
				result.Add(new BundleArchiveEntry(entry.Index, relative));
			}
			if (result.Count == 0)
				throw new InvalidDataException("The retained Collection archive does not contain files beneath the exact bundled member directory.");
			return result.OrderBy(x => x.RelativePath, StringComparer.Ordinal).ToList();
		}

		internal static bool IsSafeLeafName(string value)
		{
			if (String.IsNullOrWhiteSpace(value) || !StringComparer.Ordinal.Equals(value, value.Trim()) || value == "." || value == "..")
				return false;
			if (Path.IsPathRooted(value) || value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.IndexOf(':') >= 0)
				return false;
			return value.All(ch => !Char.IsControl(ch));
		}

		private static bool IsSafeRelativeArchivePath(string value)
		{
			if (String.IsNullOrWhiteSpace(value) || value.StartsWith("/", StringComparison.Ordinal) || value.IndexOf('\\') >= 0 || value.IndexOf(':') >= 0)
				return false;
			string[] parts = value.Split('/');
			return parts.All(part => !String.IsNullOrWhiteSpace(part) && part != "." && part != ".." && part.All(ch => !Char.IsControl(ch)));
		}

		private static bool TryReadPositiveInt64(JToken token, out long value)
		{
			value = 0;
			if (token == null || token.Type != JTokenType.Integer) return false;
			try { value = token.Value<long>(); return value > 0; }
			catch (Exception ex) when (ex is OverflowException || ex is InvalidCastException || ex is FormatException) { value = 0; return false; }
		}

		/// <summary>Best-effort cleanup for the request-scoped local archive used only as AddMod input.</summary>
		internal void CleanupStaging(CollectionAcquisitionRequest request)
		{
			if (request == null || !CollectionBundledArtifactIdentity.IsBundle(request.SelectedArtifact))
				return;
			string directory = Path.Combine(_store.StoreDirectory, ".bundle-member-staging", request.RequestId.ToString("N"));
			try
			{
				if (Directory.Exists(directory))
					Directory.Delete(directory, true);
			}
			catch
			{
				// Retained immutable bytes and the native managed archive remain authoritative; stale staging is non-authoritative.
			}
		}

		private static string CreateSafeStageFileName(string expression)
		{
			string stem = Path.GetFileNameWithoutExtension(expression);
			if (String.IsNullOrWhiteSpace(stem)) stem = "collection-bundle";
			foreach (char invalid in Path.GetInvalidFileNameChars()) stem = stem.Replace(invalid, '_');
			if (stem.Length > 120) stem = stem.Substring(0, 120);
			return stem + ".zip";
		}

		private static void TryDelete(string path)
		{
			try { if (!String.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
		}

		private sealed class BundleManifestEntry
		{
			public BundleManifestEntry(string referenceTag, string fileExpression, long fileSize) { ReferenceTag = referenceTag; FileExpression = fileExpression; FileSize = fileSize; }
			public string ReferenceTag { get; }
			public string FileExpression { get; }
			public long FileSize { get; }
		}

		private sealed class BundleArchiveEntry
		{
			public BundleArchiveEntry(int archiveIndex, string relativePath) { ArchiveIndex = archiveIndex; RelativePath = relativePath; }
			public int ArchiveIndex { get; }
			public string RelativePath { get; }
		}
	}

	/// <summary>One deterministic embedded-member archive sealed into retained storage.</summary>
	public sealed class NexusCollectionBundledArtifactMaterialization
	{
		internal NexusCollectionBundledArtifactMaterialization(CollectionsRetainedArtifact artifact, string stagingPath,
			string fileExpression, string referenceTag)
		{
			Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
			StagingPath = stagingPath ?? throw new ArgumentNullException(nameof(stagingPath));
			FileExpression = fileExpression;
			ReferenceTag = referenceTag;
		}
		public CollectionsRetainedArtifact Artifact { get; }
		public string StagingPath { get; }
		public string FileExpression { get; }
		public string ReferenceTag { get; }
	}
}
