using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Materializes exact C7 owner payloads from either their retained standalone blob or the exact retained source archive.
	/// </summary>
	internal sealed class CollectionOwnerPayloadMaterializer : IDisposable
	{
		private const int CopyBufferSize = 1024 * 1024;

		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly Func<string, IMod> _transientModFactory;
		private readonly bool _archiveBackedReconstructionSupported;
		private readonly Dictionary<string, CollectionCapturedArchiveArtifact> _archivesByNativeKey;
		private readonly Dictionary<string, CollectionInstalledModIdentity> _installedByNativeKey;
		private readonly Dictionary<string, ArchiveContext> _archiveContexts =
			new Dictionary<string, ArchiveContext>(StringComparer.OrdinalIgnoreCase);

		internal CollectionOwnerPayloadMaterializer(CollectionsRetainedArtifactStore artifactStore, ModManager modManager,
			CollectionSealedCaptureSnapshot sealedCapture)
			: this(artifactStore, sealedCapture, CreateTransientModFactory(modManager), SupportsArchiveBackedReconstruction(modManager))
		{
		}

		internal CollectionOwnerPayloadMaterializer(CollectionsRetainedArtifactStore artifactStore,
			CollectionSealedCaptureSnapshot sealedCapture, Func<string, IMod> transientModFactory)
			: this(artifactStore, sealedCapture, transientModFactory, true)
		{
		}

		private CollectionOwnerPayloadMaterializer(CollectionsRetainedArtifactStore artifactStore,
			CollectionSealedCaptureSnapshot sealedCapture, Func<string, IMod> transientModFactory,
			bool archiveBackedReconstructionSupported)
		{
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			_transientModFactory = transientModFactory ?? throw new ArgumentNullException(nameof(transientModFactory));
			_archiveBackedReconstructionSupported = archiveBackedReconstructionSupported;
			_archivesByNativeKey = BuildUniqueArchiveIndex(sealedCapture.Archives);
			_installedByNativeKey = BuildUniqueInstalledIndex(sealedCapture.InstalledIdentities.Mods);
		}

		/// <summary>Materializes one exact payload and verifies its SHA-256/length before it is exposed to native restore.</summary>
		internal void Materialize(CollectionOwnerPayloadSource source, string destination, CancellationToken cancellationToken)
		{
			if (source == null)
				throw new ArgumentNullException(nameof(source));
			if (String.IsNullOrWhiteSpace(destination))
				throw new ArgumentException("A materialized owner-payload destination is required.", nameof(destination));
			if (File.Exists(destination))
				throw new IOException("The owner-payload materialization destination already exists.");

			Stream payload = null;
			try
			{
				switch (source.Kind)
				{
					case CollectionOwnerPayloadSourceKind.CapturedArtifact:
						payload = OpenCapturedArtifact(source.CapturedArtifact, cancellationToken);
						break;
					case CollectionOwnerPayloadSourceKind.ArchiveBacked:
						payload = OpenArchiveBackedEntry(source, cancellationToken);
						break;
					default:
						throw new InvalidDataException("The sealed owner payload uses an unsupported source kind.");
				}

				CopyAndVerify(payload, destination, source.ExpectedContentHash, source.ExpectedByteLength, cancellationToken);
			}
			catch
			{
				TryDeleteFile(destination);
				throw;
			}
			finally
			{
				if (payload != null)
					payload.Dispose();
			}
		}

		private Stream OpenCapturedArtifact(CollectionOwnerPayloadRetention retained, CancellationToken cancellationToken)
		{
			if (retained == null)
				throw new InvalidDataException("A captured-artifact owner payload is missing its retained payload descriptor.");
			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(retained.StableArtifactId);
			if (artifact == null || artifact.ByteLength != retained.ByteLength || !artifact.ContentHash.Equals(retained.ContentHash) ||
				!_artifactStore.VerifyArtifact(retained.StableArtifactId, cancellationToken))
			{
				throw new InvalidDataException("A retained owner payload no longer matches its sealed capture identity.");
			}
			return _artifactStore.OpenRead(retained.StableArtifactId);
		}

		private Stream OpenArchiveBackedEntry(CollectionOwnerPayloadSource source, CancellationToken cancellationToken)
		{
			if (!_archiveBackedReconstructionSupported)
				throw new InvalidDataException("Archive-backed owner-payload reconstruction is not characterized for the current game mode.");
			CollectionOwnerPayloadArchiveBackedDescriptor descriptor = source.ArchiveBacked;
			if (descriptor == null || descriptor.ReconstructionKind != CollectionOwnerPayloadArchiveBackedKind.ExactArchiveEntry)
				throw new InvalidDataException("The sealed owner payload does not contain a supported archive-backed reconstruction descriptor.");

			CollectionCapturedArchiveArtifact capturedArchive;
			CollectionInstalledModIdentity installed;
			if (!_archivesByNativeKey.TryGetValue(descriptor.NativeSnapshotKey, out capturedArchive) ||
				!_installedByNativeKey.TryGetValue(descriptor.NativeSnapshotKey, out installed))
			{
				throw new InvalidDataException("An archive-backed owner payload is not bound to one retained archive and captured native member.");
			}
			if (installed.HasInstallScript || installed.InstallContext.InstallRoot != ModInstallRoot.Default)
				throw new InvalidDataException("The captured native member is outside the characterized archive-backed reconstruction subset.");

			string expectedProof = CollectionOwnerPayloadArchiveReconstruction.CreateProofIdentity(descriptor.NativeSnapshotKey,
				installed.InstallContext, descriptor.ArchiveEntryPath, source.ExpectedContentHash, source.ExpectedByteLength);
			if (!StringComparer.Ordinal.Equals(expectedProof, descriptor.ReconstructionIdentity))
				throw new InvalidDataException("The archive-backed owner-payload reconstruction proof no longer matches the sealed capture identity.");

			ArchiveContext context = GetArchiveContext(descriptor.NativeSnapshotKey, capturedArchive, cancellationToken);
			List<string> matches;
			if (!context.Entries.TryGetValue(descriptor.ArchiveEntryPath, out matches) || matches.Count != 1)
				throw new InvalidDataException("The retained archive no longer contains one unambiguous entry for the archive-backed owner payload.");

			try
			{
				return context.Mod.GetFileStream(matches[0]);
			}
			catch (Exception exception) when (!(exception is OperationCanceledException))
			{
				throw new InvalidDataException("The retained archive entry for an archive-backed owner payload could not be opened.", exception);
			}
		}

		private ArchiveContext GetArchiveContext(string nativeSnapshotKey, CollectionCapturedArchiveArtifact capturedArchive,
			CancellationToken cancellationToken)
		{
			ArchiveContext cached;
			if (_archiveContexts.TryGetValue(nativeSnapshotKey, out cached))
				return cached;

			RetainedArtifactReference retained = capturedArchive.RetainedArtifact;
			CollectionsRetainedArtifact persisted = _artifactStore.GetArtifact(retained.StableArtifactId);
			if (persisted == null || persisted.ByteLength != retained.ByteLength || !persisted.ContentHash.Equals(retained.ContentHash))
				throw new InvalidDataException("The retained source archive metadata no longer matches the sealed Local Collection capture.");

			string archivePath = _artifactStore.GetVerifiedReadOnlyPath(retained.StableArtifactId, cancellationToken);
			IMod mod = _transientModFactory(archivePath);
			if (mod == null)
				throw new InvalidDataException("The retained source archive could not be opened as a transient mod.");
			Dictionary<string, List<string>> entries = BuildCanonicalArchiveEntries(mod);
			cached = new ArchiveContext(mod, entries);
			_archiveContexts.Add(nativeSnapshotKey, cached);
			return cached;
		}

		private static bool SupportsArchiveBackedReconstruction(ModManager modManager)
		{
			return modManager != null && modManager.GameMode != null &&
				!modManager.GameMode.RequiresSpecialFileInstallation && !modManager.GameMode.RequiresModFileMerge;
		}

		private static Func<string, IMod> CreateTransientModFactory(ModManager modManager)
		{
			if (modManager == null)
				throw new ArgumentNullException(nameof(modManager));
			return archivePath =>
			{
				var candidates = new List<KeyValuePair<FormatConfidence, IModFormat>>();
				foreach (IModFormat format in modManager.ModFormats)
				{
					FormatConfidence confidence;
					try
					{
						confidence = format.CheckFormatCompliance(archivePath);
					}
					catch
					{
						confidence = FormatConfidence.Incompatible;
					}
					candidates.Add(new KeyValuePair<FormatConfidence, IModFormat>(confidence, format));
				}
				if (candidates.Count == 0)
					throw new InvalidDataException("No installed mod format can read the retained source archive.");
				candidates.Sort((left, right) => -left.Key.CompareTo(right.Key));
				if (candidates[0].Key <= FormatConfidence.Convertible)
					throw new InvalidDataException("The retained source archive is no longer recognized by a supported mod format.");
				try
				{
					IMod mod = candidates[0].Value.CreateMod(archivePath, modManager.GameMode, false);
					if (mod == null)
						throw new InvalidDataException("The retained source archive could not be opened as a transient mod.");
					return mod;
				}
				catch (InvalidDataException)
				{
					throw;
				}
				catch (Exception exception)
				{
					throw new InvalidDataException("The retained source archive could not be opened by its recognized mod format.", exception);
				}
			};
		}

		private static Dictionary<string, List<string>> BuildCanonicalArchiveEntries(IMod mod)
		{
			var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			IEnumerable<string> files;
			try
			{
				files = mod.GetFileList() ?? new List<string>();
			}
			catch (Exception exception)
			{
				throw new InvalidDataException("The retained source archive file list could not be read.", exception);
			}
			foreach (string file in files)
			{
				string canonical;
				try
				{
					canonical = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, file).Path;
				}
				catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException)
				{
					continue;
				}
				List<string> list;
				if (!result.TryGetValue(canonical, out list))
				{
					list = new List<string>();
					result.Add(canonical, list);
				}
				list.Add(file);
			}
			return result;
		}

		private static void CopyAndVerify(Stream source, string destination, CollectionContentHash expectedHash,
			long expectedByteLength, CancellationToken cancellationToken)
		{
			if (source == null || !source.CanRead)
				throw new InvalidDataException("The exact owner-payload source stream is unavailable.");
			using (SHA256 sha256 = SHA256.Create())
			using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
				CopyBufferSize, FileOptions.SequentialScan))
			{
				byte[] buffer = new byte[CopyBufferSize];
				long length = 0;
				int read;
				while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					output.Write(buffer, 0, read);
					sha256.TransformBlock(buffer, 0, read, null, 0);
					length += read;
				}
				sha256.TransformFinalBlock(new byte[0], 0, 0);
				output.Flush(true);
				string hash = ToHex(sha256.Hash);
				if (length != expectedByteLength || !StringComparer.Ordinal.Equals(hash, expectedHash.Value))
					throw new InvalidDataException("The materialized owner payload does not match its sealed SHA-256 and byte length.");
			}
		}

		private static Dictionary<string, CollectionCapturedArchiveArtifact> BuildUniqueArchiveIndex(
			IEnumerable<CollectionCapturedArchiveArtifact> archives)
		{
			var result = new Dictionary<string, CollectionCapturedArchiveArtifact>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionCapturedArchiveArtifact archive in archives)
			{
				if (result.ContainsKey(archive.NativeSnapshotKey))
					throw new InvalidDataException("The sealed Local Collection contains duplicate retained archives for one captured native member.");
				result.Add(archive.NativeSnapshotKey, archive);
			}
			return result;
		}

		private static Dictionary<string, CollectionInstalledModIdentity> BuildUniqueInstalledIndex(
			IEnumerable<CollectionInstalledModIdentity> installed)
		{
			var result = new Dictionary<string, CollectionInstalledModIdentity>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionInstalledModIdentity mod in installed)
			{
				if (result.ContainsKey(mod.NativeSnapshotKey))
					throw new InvalidDataException("The sealed Local Collection contains duplicate captured native identities.");
				result.Add(mod.NativeSnapshotKey, mod);
			}
			return result;
		}

		private static string ToHex(byte[] value)
		{
			var builder = new System.Text.StringBuilder(value.Length * 2);
			foreach (byte item in value)
				builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
			return builder.ToString();
		}

		private static void TryDeleteFile(string path)
		{
			try
			{
				if (!String.IsNullOrWhiteSpace(path) && File.Exists(path))
					File.Delete(path);
			}
			catch { }
		}

		public void Dispose()
		{
			_archiveContexts.Clear();
		}

		private sealed class ArchiveContext
		{
			internal ArchiveContext(IMod mod, Dictionary<string, List<string>> entries)
			{
				Mod = mod;
				Entries = entries;
			}
			internal IMod Mod { get; }
			internal Dictionary<string, List<string>> Entries { get; }
		}
	}
}
