using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.Mods.Formats.FOMod;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Finds existing NMM-managed Nexus archives by repository mod/file identity rather than filename.
	/// </summary>
	public sealed class ModManagerCollectionManagedArchiveSource : ICollectionManagedArchiveSource
	{
		private readonly ModManager _modManager;

		/// <summary>
		/// Creates an archive source over the live native managed-mod registry.
		/// </summary>
		public ModManagerCollectionManagedArchiveSource(ModManager modManager)
		{
			_modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
		}

		/// <inheritdoc />
		public IReadOnlyList<CollectionManagedArchiveCandidate> FindCandidates(CollectionArtifactReference requestedArtifact)
		{
			string expectedMd5;
			long expectedByteLength;
			if (CollectionExternalArtifactIdentity.TryParse(requestedArtifact, out expectedMd5, out expectedByteLength))
			{
				var externalCandidates = new List<CollectionManagedArchiveCandidate>();
				foreach (IMod mod in _modManager.ManagedMods)
				{
					string archivePath = CollectionArchiveContentMatcher.GetManagedArchivePath(mod);
					if (String.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
						continue;
					try
					{
						if (new FileInfo(archivePath).Length != expectedByteLength)
							continue;
					}
					catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
					{
						continue;
					}
					externalCandidates.Add(new CollectionManagedArchiveCandidate(
						CollectionExternalArtifactIdentity.Scheme, requestedArtifact.StableId, archivePath));
				}
				return externalCandidates;
			}

			string expectedDomain;
			long expectedModId;
			long expectedFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(requestedArtifact, out expectedDomain, out expectedModId, out expectedFileId))
				return new CollectionManagedArchiveCandidate[0];

			string currentDomain = _modManager.ModRepository == null ? null : _modManager.ModRepository.GameDomainName;
			if (!StringComparer.OrdinalIgnoreCase.Equals(currentDomain, expectedDomain))
				return new CollectionManagedArchiveCandidate[0];

			string expectedModIdText = expectedModId.ToString(CultureInfo.InvariantCulture);
			string expectedFileIdText = expectedFileId.ToString(CultureInfo.InvariantCulture);
			var candidates = new List<CollectionManagedArchiveCandidate>();
			var staleCandidates = new List<CollectionManagedArchiveCandidate>();
			foreach (IMod mod in _modManager.ManagedMods)
			{
				bool repositoryCandidate = IsRepositoryFileCandidate(mod, _modManager.SortOrderService, expectedModIdText, expectedFileIdText);
				if (!repositoryCandidate && !IsStaleRepositoryFileCandidate(mod, expectedModIdText, expectedFileIdText))
					continue;

				// A stale file ID supplies only a discovery hint. The adopter must prove the requested file's immutable bytes.
				(repositoryCandidate ? candidates : staleCandidates).Add(new CollectionManagedArchiveCandidate(
					NexusCollectionModFileArtifactIdentity.Scheme,
					NexusCollectionModFileArtifactIdentity.Format(expectedDomain, expectedModId, expectedFileId),
					CollectionArchiveContentMatcher.GetManagedArchivePath(mod)));
			}

			candidates.AddRange(staleCandidates);
			return candidates;
		}

		/// <summary>
		/// Finds an inactive archive with a matching mod ID whose stale file ID may be repaired after exact content verification.
		/// </summary>
		private bool IsStaleRepositoryFileCandidate(IMod mod, string expectedModId, string expectedFileId)
		{
			string archivePath = CollectionArchiveContentMatcher.GetManagedArchivePath(mod);
			if (mod == null || String.IsNullOrWhiteSpace(archivePath) || String.IsNullOrWhiteSpace(_modManager.CurrentGameModeModDirectory) ||
				!ModFileIdentity.IsUsableRepositoryId(expectedModId) || !ModFileIdentity.IsUsableRepositoryId(expectedFileId) ||
				!StringComparer.OrdinalIgnoreCase.Equals(mod.Id, expectedModId) || !ModFileIdentity.IsUsableRepositoryId(mod.DownloadId) ||
				StringComparer.OrdinalIgnoreCase.Equals(mod.DownloadId, expectedFileId))
				return false;

			string fullPath = Path.GetFullPath(archivePath);
			string libraryRoot = Path.GetFullPath(_modManager.CurrentGameModeModDirectory)
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (!fullPath.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase))
				return false;

			InstallLogReadSnapshot installed = _modManager.InstallationLog == null ? null : _modManager.InstallationLog.GetCommittedStateSnapshot();
			return installed != null && !installed.Mods.Any(x => IsInstalledArchiveReference(x, fullPath));
		}

		/// <summary>
		/// Checks an InstallLog archive reference while preserving synthetic owner entries and failing closed on invalid real paths.
		/// </summary>
		private static bool IsInstalledArchiveReference(InstallLogReadMod record, string fullPath)
		{
			// These built-in owners use labels containing a colon, not archive paths. Hidden real mods still require comparison.
			if (StringComparer.OrdinalIgnoreCase.Equals(record.ArchivePath, InstallLog.OriginalValueMod.ModArchivePath) ||
				StringComparer.OrdinalIgnoreCase.Equals(record.ArchivePath, InstallLog.ModManagerValueMod.ModArchivePath))
				return false;

			try
			{
				return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(record.FileName), Path.GetFileName(fullPath)) ||
					(!String.IsNullOrWhiteSpace(record.ArchivePath) && StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(record.ArchivePath), fullPath));
			}
			catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
			{
				// An unresolvable real registration cannot establish that metadata repair would leave installed identity unchanged.
				return true;
			}
		}

		/// <summary>
		/// Matches one managed archive against an exact requested repository file identity.
		/// </summary>
		/// <remarks>
		/// Live metadata wins when present. For older metadata-disabled downloads, durable Sort history may supply the missing
		/// provenance, but callers that reuse bytes still perform independent immutable content verification.
		/// </remarks>
		internal static bool MatchesRepositoryFileIdentity(IMod mod, ModSortOrderService sortOrderService,
			string expectedModId, string expectedFileId)
		{
			string archivePath = CollectionArchiveContentMatcher.GetManagedArchivePath(mod);
			if (mod == null || String.IsNullOrWhiteSpace(archivePath) ||
				!ModFileIdentity.IsUsableRepositoryId(expectedModId) ||
				!ModFileIdentity.IsUsableRepositoryId(expectedFileId))
				return false;

			bool hasLiveModId = ModFileIdentity.IsUsableRepositoryId(mod.Id);
			bool hasLiveFileId = ModFileIdentity.IsUsableRepositoryId(mod.DownloadId);
			if ((hasLiveModId && !StringComparer.OrdinalIgnoreCase.Equals(mod.Id, expectedModId)) ||
				(hasLiveFileId && !StringComparer.OrdinalIgnoreCase.Equals(mod.DownloadId, expectedFileId)))
				return false;

			if (hasLiveModId && hasLiveFileId)
				return true;

			return sortOrderService != null && sortOrderService.HasDurableRepositoryFileIdentity(
				archivePath, expectedModId, expectedFileId);
		}

		/// <summary>
		/// Determines whether one managed archive is a bounded candidate for immutable-byte verification.
		/// </summary>
		/// <remarks>
		/// Exact repository identity remains preferred. The legacy fallback deliberately accepts only a matching live ModId with
		/// a missing FileId; known contradictory metadata still rejects the archive. This keeps discovery bounded to one Nexus mod
		/// while the adopter performs the provider-backed exact-file verification before any identity is trusted.
		/// </remarks>
		internal static bool IsRepositoryFileCandidate(IMod mod, ModSortOrderService sortOrderService,
			string expectedModId, string expectedFileId)
		{
			if (MatchesRepositoryFileIdentity(mod, sortOrderService, expectedModId, expectedFileId))
				return true;
			string archivePath = CollectionArchiveContentMatcher.GetManagedArchivePath(mod);
			if (mod == null || String.IsNullOrWhiteSpace(archivePath) ||
				!ModFileIdentity.IsUsableRepositoryId(expectedModId) ||
				!ModFileIdentity.IsUsableRepositoryId(expectedFileId))
				return false;

			bool hasLiveModId = ModFileIdentity.IsUsableRepositoryId(mod.Id);
			bool hasLiveFileId = ModFileIdentity.IsUsableRepositoryId(mod.DownloadId);
			if ((hasLiveModId && !StringComparer.OrdinalIgnoreCase.Equals(mod.Id, expectedModId)) ||
				(hasLiveFileId && !StringComparer.OrdinalIgnoreCase.Equals(mod.DownloadId, expectedFileId)))
				return false;

			return hasLiveModId && !hasLiveFileId;
		}

		/// <summary>
		/// Returns whether the repository metadata currently known for a managed archive does not contradict an exact
		/// Nexus mod/file identity. Callers must still independently verify immutable archive bytes before trusting it.
		/// </summary>
		internal static bool IsMetadataCompatibleForVerifiedContent(IMod mod, string expectedModId, string expectedFileId)
		{
			string archivePath = CollectionArchiveContentMatcher.GetManagedArchivePath(mod);
			if (mod == null || String.IsNullOrWhiteSpace(archivePath) ||
				!ModFileIdentity.IsUsableRepositoryId(expectedModId) ||
				!ModFileIdentity.IsUsableRepositoryId(expectedFileId))
				return false;

			bool hasLiveModId = ModFileIdentity.IsUsableRepositoryId(mod.Id);
			bool hasLiveFileId = ModFileIdentity.IsUsableRepositoryId(mod.DownloadId);
			if ((hasLiveModId && !StringComparer.OrdinalIgnoreCase.Equals(mod.Id, expectedModId)) ||
				(hasLiveFileId && !StringComparer.OrdinalIgnoreCase.Equals(mod.DownloadId, expectedFileId)))
				return false;

			return true;
		}

		/// <summary>
		/// Persists exact repository identity after immutable-byte verification without mutating live IMod metadata.
		/// </summary>
		internal static void ConfirmVerifiedRepositoryFileIdentity(ModManager modManager, IMod mod,
			string expectedModId, string expectedFileId)
		{
			if (modManager == null) throw new ArgumentNullException(nameof(modManager));
			if (!IsMetadataCompatibleForVerifiedContent(mod, expectedModId, expectedFileId))
				throw new InvalidDataException("The verified managed archive has contradictory repository metadata.");
			if (modManager.SortOrderService == null)
				throw new InvalidOperationException("Verified Collection archive reuse requires the native durable repository-identity store.");

			modManager.SortOrderService.ConfirmVerifiedRepositoryFileIdentity(mod, expectedModId, expectedFileId);
		}

		/// <summary>
		/// Completes missing native repository metadata, or repairs an inactive archive's stale file ID, using exact retained bytes.
		/// </summary>
		internal static void ConfirmVerifiedArchiveRepositoryFileIdentity(ModManager modManager, IMod mod,
			string expectedModId, string expectedFileId, long byteLength, CollectionContentHash contentHash,
			CancellationToken cancellationToken)
		{
			if (modManager == null) throw new ArgumentNullException(nameof(modManager));
			if (mod == null) throw new ArgumentNullException(nameof(mod));
			if (modManager.SortOrderService == null)
				throw new InvalidOperationException("Verified Collection archive reuse requires the native durable repository-identity store.");

			if (!IsMetadataCompatibleForVerifiedContent(mod, expectedModId, expectedFileId))
			{
				string archivePath = CollectionArchiveContentMatcher.GetManagedArchivePath(mod);
				if (!ModFileIdentity.IsUsableRepositoryId(expectedModId) || !ModFileIdentity.IsUsableRepositoryId(expectedFileId) ||
					!StringComparer.OrdinalIgnoreCase.Equals(mod.Id, expectedModId) || String.IsNullOrWhiteSpace(archivePath))
					throw new InvalidDataException("The verified managed archive has contradictory repository mod metadata; its identity cannot be repaired.");

				string fullPath = Path.GetFullPath(archivePath);
				string libraryRoot = Path.GetFullPath(modManager.CurrentGameModeModDirectory)
					.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (!fullPath.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException("Only an inactive archive in the current game's mod library can have its verified file identity repaired.");

				InstallLogReadSnapshot installed = modManager.InstallationLog == null ? null : modManager.InstallationLog.GetCommittedStateSnapshot();
				if (installed == null || installed.Mods.Any(x => IsInstalledArchiveReference(x, fullPath)))
					throw new InvalidDataException("The archive's file identity cannot be repaired during preparation while it is referenced by the native InstallLog.");

				if (!CollectionArchiveContentMatcher.MatchesFile(fullPath, byteLength, contentHash, cancellationToken))
					throw new InvalidDataException("The archive failed exact retained-content verification before repository file identity repair.");

				// Cached file metadata can outlive the downloaded bytes. Keep the same mod identity and all user metadata;
				// the native UpdateInfo path durably saves the cache without changing archive bytes or installed state.
				mod.UpdateInfo(new ModInfo(mod) { DownloadId = expectedFileId }, true);
			}

			if (!ModFileIdentity.IsUsableRepositoryId(mod.Id) || !ModFileIdentity.IsUsableRepositoryId(mod.DownloadId))
			{
				string archivePath = CollectionArchiveContentMatcher.GetManagedArchivePath(mod);
				InstallLogReadSnapshot installed = modManager.InstallationLog == null ? null : modManager.InstallationLog.GetCommittedStateSnapshot();
				// Preparation must not change the identity of an installed instance after its native fingerprint was captured.
				// A submitted child's missing identity is restored separately under its held target reservation.
				if (installed != null && !installed.Mods.Any(x => IsInstalledArchiveReference(x, Path.GetFullPath(archivePath))))
					CompleteMissingRepositoryMetadata(mod, expectedModId, expectedFileId, byteLength, contentHash, cancellationToken);
			}
			ConfirmVerifiedRepositoryFileIdentity(modManager, mod, expectedModId, expectedFileId);
		}

		/// <summary>Completes absent native IDs only after verifying the exact archive bytes, preserving all other mod metadata.</summary>
		private static void CompleteMissingRepositoryMetadata(IMod mod, string modId, string fileId,
			long byteLength, CollectionContentHash contentHash, CancellationToken cancellationToken)
		{
			if (!CollectionArchiveContentMatcher.MatchesFile(CollectionArchiveContentMatcher.GetManagedArchivePath(mod), byteLength, contentHash, cancellationToken))
				throw new InvalidDataException("The archive failed exact retained-content verification before completing native repository metadata.");
			// FOMod.UpdateInfo persists info.xml in the native metadata cache; archive editing is disabled.
			mod.UpdateInfo(new ModInfo(mod) { Id = modId, DownloadId = fileId }, true);
		}

		/// <summary>Restores missing native IDs for one submitted child from its retained exact identity and archive proof.</summary>
		/// <remarks>Call only after native authority reload while holding the child's target mutation reservation.</remarks>
		internal static void RestoreMissingNativeRepositoryIdentity(ModManager modManager,
			CollectionNativeChildRecoveryManifest recovery, CancellationToken cancellationToken)
		{
			CollectionNativeChildExecutionEvidence evidence = recovery.ExecutionEvidence;
			if (evidence == null || !evidence.IsNexusModFileArtifact || recovery.TerminalStateFingerprint != null ||
				modManager.InstallationLog == null || modManager.ModRepository == null ||
				!StringComparer.OrdinalIgnoreCase.Equals(modManager.ModRepository.GameDomainName, evidence.NexusGameDomain))
				return;
			string modId = evidence.NexusModId.ToString(CultureInfo.InvariantCulture);
			string fileId = evidence.NexusFileId.ToString(CultureInfo.InvariantCulture);
			List<IMod> candidates = modManager.InstallationLog.ActiveMods.Where(x =>
				StringComparer.OrdinalIgnoreCase.Equals(x.Filename, evidence.IncomingFileName)).ToList();
			if (candidates.Count != 1 || !IsMetadataCompatibleForVerifiedContent(candidates[0], modId, fileId) ||
				(ModFileIdentity.IsUsableRepositoryId(candidates[0].Id) && ModFileIdentity.IsUsableRepositoryId(candidates[0].DownloadId)))
				return;
			CompleteMissingRepositoryMetadata(candidates[0], modId, fileId, recovery.IncomingArchive.ByteLength,
				recovery.IncomingArchive.ContentHash, cancellationToken);
		}

		/// <summary>Chooses a stable native record when several records refer to byte-identical installation input.</summary>
		internal static IMod SelectDeterministicEquivalentManagedMod(IEnumerable<IMod> candidates)
		{
			if (candidates == null)
				throw new ArgumentNullException(nameof(candidates));
			IMod selected = candidates.Where(x => x != null).OrderBy(
				x => CollectionArchiveContentMatcher.GetManagedArchivePath(x) ?? String.Empty, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
			if (selected == null)
				throw new InvalidOperationException("No managed archive candidate is available.");
			return selected;
		}

		/// <summary>
		/// Repairs a stale native registry entry when the exact verified archive already exists in the current game's mod library.
		/// </summary>
		internal static IMod TryRegisterExactLibraryArchive(ModManager modManager, long byteLength,
			CollectionContentHash contentHash, CancellationToken cancellationToken)
		{
			if (modManager == null) throw new ArgumentNullException(nameof(modManager));
			List<string> exactFiles = CollectionArchiveContentMatcher.FindExactLibraryArchiveFiles(
				modManager, byteLength, contentHash, cancellationToken);
			if (exactFiles.Count == 0)
				return null;
			if (exactFiles.Count > 1)
				throw new InvalidDataException("Multiple physical archives in the NMM mod library contain the exact verified Collection bytes; native registration cannot choose one safely.");

			IMod registered = modManager.RegisterLocalRestoreArchive(exactFiles[0], null);
			if (registered == null)
				throw new InvalidDataException("The exact verified Collection archive exists in the NMM mod library but could not be registered as a native managed mod.");
			string registeredPath = CollectionArchiveContentMatcher.GetManagedArchivePath(registered);
			if (!CollectionArchiveContentMatcher.MatchesFile(registeredPath, byteLength, contentHash, cancellationToken))
				throw new InvalidDataException("Native registration did not resolve back to the exact verified Collection archive bytes.");
			return registered;
		}

		internal static IMod TryRegisterExactLibraryArchive(ModManager modManager, CollectionsRetainedArtifact retained,
			CancellationToken cancellationToken)
		{
			if (retained == null) throw new ArgumentNullException(nameof(retained));
			return TryRegisterExactLibraryArchive(modManager, retained.ByteLength, retained.ContentHash, cancellationToken);
		}

		/// <summary>
		/// Materializes already-verified retained Collection bytes into the configured NMM mod library and registers them
		/// through the existing native registry. This is used only when acquisition has durable exact bytes but no native
		/// managed archive currently represents them.
		/// </summary>
		internal static IMod MaterializeAndRegisterRetainedArchive(ModManager modManager,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifact retained,
			string displayName, CancellationToken cancellationToken)
		{
			if (modManager == null) throw new ArgumentNullException(nameof(modManager));
			if (artifactStore == null) throw new ArgumentNullException(nameof(artifactStore));
			if (retained == null) throw new ArgumentNullException(nameof(retained));
			if (!artifactStore.VerifyArtifact(retained.ArtifactId, cancellationToken))
				throw new InvalidDataException("The retained Collection archive failed exact verification before native registration.");

			string libraryDirectory = Path.GetFullPath(modManager.CurrentGameModeModDirectory);
			Directory.CreateDirectory(libraryDirectory);
			string extension = DetectRetainedArchiveExtension(artifactStore, retained);
			string stem = CreateSafeArchiveStem(displayName);
			string hashSuffix = retained.ContentHash.Value.Substring(0, Math.Min(12, retained.ContentHash.Value.Length));
			string destination = Path.Combine(libraryDirectory, stem + ".collection-" + hashSuffix + extension);

			if (File.Exists(destination))
			{
				if (!CollectionArchiveContentMatcher.MatchesFile(destination, retained, cancellationToken))
					throw new IOException("The deterministic Collection archive destination already contains different bytes.");
			}
			else
			{
				string staging = destination + "." + Guid.NewGuid().ToString("N") + ".collectiontmp";
				try
				{
					using (Stream source = artifactStore.OpenRead(retained.ArtifactId))
					using (var target = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					{
						var buffer = new byte[81920];
						int read;
						while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
						{
							cancellationToken.ThrowIfCancellationRequested();
							target.Write(buffer, 0, read);
						}
						target.Flush(true);
					}
					if (!CollectionArchiveContentMatcher.MatchesFile(staging, retained, cancellationToken))
						throw new InvalidDataException("Materialized Collection archive bytes do not match their verified retained identity.");
					File.Move(staging, destination);
				}
				finally
				{
					if (File.Exists(staging))
						File.Delete(staging);
				}
			}

			IMod registered = modManager.RegisterLocalRestoreArchive(destination, null);
			if (registered == null)
				throw new InvalidDataException("The verified retained Collection archive was materialized but is not a native mod format supported by the current game mode.");
			string registeredPath = CollectionArchiveContentMatcher.GetManagedArchivePath(registered);
			if (!CollectionArchiveContentMatcher.MatchesFile(registeredPath, retained, cancellationToken))
				throw new InvalidDataException("Native registration did not resolve back to the materialized verified Collection archive bytes.");
			return registered;
		}

		private static string CreateSafeArchiveStem(string displayName)
		{
			string value = String.IsNullOrWhiteSpace(displayName) ? "Collection archive" : displayName.Trim();
			var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
			var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
			value = new string(chars).Trim().TrimEnd('.');
			if (String.IsNullOrWhiteSpace(value))
				value = "Collection archive";
			if (value.Length > 80)
				value = value.Substring(0, 80).TrimEnd();
			return value;
		}

		private static string DetectRetainedArchiveExtension(CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifact retained)
		{
			var header = new byte[8];
			int read = 0;
			using (Stream source = artifactStore.OpenRead(retained.ArtifactId))
			{
				while (read < header.Length)
				{
					int count = source.Read(header, read, header.Length - read);
					if (count == 0) break;
					read += count;
				}
			}
			if (read >= 4 && header[0] == 0x50 && header[1] == 0x4b &&
				((header[2] == 0x03 && header[3] == 0x04) || (header[2] == 0x05 && header[3] == 0x06) ||
				 (header[2] == 0x07 && header[3] == 0x08)))
				return ".zip";
			if (read >= 6 && header[0] == 0x37 && header[1] == 0x7a && header[2] == 0xbc &&
				header[3] == 0xaf && header[4] == 0x27 && header[5] == 0x1c)
				return ".7z";
			if (read >= 7 && header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 &&
				header[3] == 0x21 && header[4] == 0x1a && header[5] == 0x07)
				return ".rar";
			return ".archive";
		}

		/// <summary>Records exact repository identity after the adopter has verified this candidate's immutable bytes.</summary>
		internal void ConfirmVerifiedCandidate(CollectionManagedArchiveCandidate candidate, CollectionArtifactReference requestedArtifact,
			CollectionsRetainedArtifact verifiedArtifact, CancellationToken cancellationToken)
		{
			if (candidate == null)
				throw new ArgumentNullException(nameof(candidate));
			if (requestedArtifact == null)
				throw new ArgumentNullException(nameof(requestedArtifact));
			if (verifiedArtifact == null)
				throw new ArgumentNullException(nameof(verifiedArtifact));

			string expectedDomain;
			long expectedModId;
			long expectedFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(requestedArtifact, out expectedDomain, out expectedModId, out expectedFileId))
				throw new InvalidDataException("The verified managed archive does not use a Nexus mod/file artifact identity.");

			string currentDomain = _modManager.ModRepository == null ? null : _modManager.ModRepository.GameDomainName;
			if (!StringComparer.OrdinalIgnoreCase.Equals(currentDomain, expectedDomain))
				throw new InvalidDataException("The verified managed archive belongs to a different Nexus game domain.");

			string candidatePath = Path.GetFullPath(candidate.ArchivePath);
			List<IMod> matches = _modManager.ManagedMods.Where(x => x != null &&
				!String.IsNullOrWhiteSpace(CollectionArchiveContentMatcher.GetManagedArchivePath(x)) &&
				StringComparer.OrdinalIgnoreCase.Equals(
					Path.GetFullPath(CollectionArchiveContentMatcher.GetManagedArchivePath(x)), candidatePath)).ToList();
			if (matches.Count != 1)
				throw new InvalidDataException("The verified managed archive can no longer be resolved to exactly one native managed mod.");

			string modId = expectedModId.ToString(CultureInfo.InvariantCulture);
			string fileId = expectedFileId.ToString(CultureInfo.InvariantCulture);
			IMod mod = matches[0];
			if (!IsRepositoryFileCandidate(mod, _modManager.SortOrderService, modId, fileId) && !IsStaleRepositoryFileCandidate(mod, modId, fileId))
				throw new InvalidDataException("The managed archive repository metadata changed while exact Collection verification was running.");

			// Only inactive library metadata can be repaired. InstallLog identities included in preparation fingerprints stay unchanged.
			ConfirmVerifiedArchiveRepositoryFileIdentity(_modManager, mod, modId, fileId,
				verifiedArtifact.ByteLength, verifiedArtifact.ContentHash, cancellationToken);
		}

		/// <summary>
		/// Resolves the current live/resolved repository identity without consulting historical path rows.
		/// </summary>
		internal static bool TryResolveRepositoryFileIdentity(IMod mod, ModSortOrderService sortOrderService,
			out string modId, out string fileId)
		{
			modId = null;
			fileId = null;
			if (mod == null)
				return false;

			bool hasLiveModId = ModFileIdentity.IsUsableRepositoryId(mod.Id);
			bool hasLiveFileId = ModFileIdentity.IsUsableRepositoryId(mod.DownloadId);
			if (hasLiveModId && hasLiveFileId)
			{
				modId = mod.Id;
				fileId = mod.DownloadId;
				return true;
			}

			string archivePath = CollectionArchiveContentMatcher.GetManagedArchivePath(mod);
			if (sortOrderService == null || String.IsNullOrWhiteSpace(archivePath))
				return false;

			ModSortOrderRecord assignment;
			if (!sortOrderService.TryGetResolvedAssignment(archivePath, out assignment) || assignment == null ||
				!ModFileIdentity.IsUsableRepositoryId(assignment.ModId) ||
				!ModFileIdentity.IsUsableRepositoryId(assignment.DownloadId))
				return false;

			if ((hasLiveModId && !StringComparer.OrdinalIgnoreCase.Equals(mod.Id, assignment.ModId)) ||
				(hasLiveFileId && !StringComparer.OrdinalIgnoreCase.Equals(mod.DownloadId, assignment.DownloadId)))
				return false;

			modId = assignment.ModId;
			fileId = assignment.DownloadId;
			return true;
		}
	}
}
