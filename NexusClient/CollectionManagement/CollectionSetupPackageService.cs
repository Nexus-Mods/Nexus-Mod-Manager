using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.ModManagement.Witcher3;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Reviews and journals explicit post-install file overlays without changing native archive ownership or staging.</summary>
	public sealed class CollectionSetupPackageService
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _storage;
		private readonly CollectionsStore _store;
		private readonly CollectionTargetOwnershipAuthorityValidator _validator;
		private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings {
			TypeNameHandling = TypeNameHandling.None, MaxDepth = 32, Formatting = Formatting.Indented };

		/// <summary>Uses the established active-game services and Collection target mutation reservation.</summary>
		internal CollectionSetupPackageService(ServiceManager services, GameStorageService storage, CollectionsStore store)
		{
			_services = services; _storage = storage; _store = store;
			_validator = new CollectionTargetOwnershipAuthorityValidator(storage, services);
		}

		private const string WabbajackSetupRole = "wabbajack-setup";

		/// <summary>Identifies converted profiles from the persisted revision, including after reopening an installed Collection.</summary>
		public bool IsWabbajackRevision(CollectionRevisionIdentity revision)
		{
			CollectionRevision stored = _store.Exists ? new CollectionsCatalogStore(_store).GetRevision(revision) : null;
			return stored?.Notes != null && stored.Notes.StartsWith("Wabbajack: ", StringComparison.Ordinal);
		}

		/// <summary>Retains generated profile settings under the exact Collection revision instead of the mutable export path.</summary>
		public void RetainWabbajackSetup(CollectionRevisionIdentity revision, string path)
		{
			CollectionsRetainedArtifact artifact = new CollectionsRetainedArtifactStore(_store).PublishFile(path);
			BindWabbajackSetup(revision, artifact);
		}

		/// <summary>Opens revision-owned profile settings, recovering embedded or older companion packages when necessary.</summary>
		public CollectionSetupPackage PreviewWabbajack(CollectionRevisionIdentity revision)
		{
			CollectionsRetainedArtifactStore artifacts = new CollectionsRetainedArtifactStore(_store);
			CollectionsRetainedArtifactReferenceRecord reference = new CollectionsRetainedArtifactReferenceStore(_store).GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Revision, CollectionsRevisionSourceStore.GetRevisionOwnerId(revision), WabbajackSetupRole);
			string artifactId = reference?.ArtifactId;
			if (artifactId == null)
			{
				using (Stream source = new CollectionsRevisionSourceStore(_store).OpenBundle(revision))
				using (ZipArchive bundle = new ZipArchive(source, ZipArchiveMode.Read))
				{
					ZipArchiveEntry entry = bundle.GetEntry("wabbajack/setup.zip");
					if (entry != null)
						using (Stream input = entry.Open())
						{
							CollectionsRetainedArtifact artifact = artifacts.Publish(input);
							BindWabbajackSetup(revision, artifact); artifactId = artifact.ArtifactId;
						}
				}
				if (artifactId == null)
				{
					// Earlier exports kept the settings ZIP beside the converted Collection rather than inside its bundle.
					CollectionsCatalogStore catalog = new CollectionsCatalogStore(_store);
					string name = catalog.GetDefinition(revision.Collection).DisplayName;
					string version = catalog.GetRevision(revision).RevisionLabel;
					string fileName = string.Concat(("Wabbajack - " + name + " - " + version).Split(Path.GetInvalidFileNameChars())) + " - Setup.zip";
					string legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "NMM Wabbajack", fileName);
					if (!File.Exists(legacyPath)) throw new FileNotFoundException(L("WabbajackSetupMissing", "This older Collection did not retain its settings ZIP. Recreate the Collection from the already downloaded Wabbajack list to attach its settings; the mods do not need downloading again."));
					CollectionsRetainedArtifact artifact = artifacts.PublishFile(legacyPath);
					BindWabbajackSetup(revision, artifact); artifactId = artifact.ArtifactId;
				}
			}
			using (Stream input = artifacts.OpenRead(artifactId))
				return PreparePreview(CollectionSetupPackageReader.Read(input, "Wabbajack profile settings.zip"), revision);
		}

		/// <summary>Uses the existing immutable artifact lifetime reference for the generated setup package.</summary>
		private void BindWabbajackSetup(CollectionRevisionIdentity revision, CollectionsRetainedArtifact artifact)
		{
			new CollectionsRetainedArtifactReferenceStore(_store).AcquireExclusiveRoleReference(artifact.ArtifactId,
				CollectionsRetainedArtifactOwnerKind.Revision, CollectionsRevisionSourceStore.GetRevisionOwnerId(revision), WabbajackSetupRole);
		}

		/// <summary>Inspects an archive against a selected revision without changing the user's game or settings.</summary>
		public CollectionSetupPackage Preview(string archive, CollectionRevisionIdentity revision)
		{
			if (revision == null) throw new ArgumentNullException(nameof(revision));
			return PreparePreview(CollectionSetupPackageReader.Read(archive), revision);
		}

		/// <summary>Resolves the active game's destinations for imported and retained setup archives.</summary>
		private CollectionSetupPackage PreparePreview(CollectionSetupPackage package, CollectionRevisionIdentity revision)
		{
			GameStoragePathSet paths = Paths();
			package.Revision = revision.ToString();
			package.Target = Authority(paths).Target.Fingerprint;
			package.GameDirectory = Path.GetFullPath(paths.GameInstallPath);
			if (Witcher3ModSettingsLifecycleService.SupportsMode(paths.GameId))
				package.DocumentsDirectory = Path.GetDirectoryName(Witcher3ModSettingsFileReader.GetFilePath(PersonalDirectory()));
			else if (_services.ModManager.GameMode is GamebryoGameModeBase)
				package.DocumentsDirectory = ((GamebryoGameModeBase)_services.ModManager.GameMode).UserGameDataPath;
			Refresh(package);
			return package;
		}

		/// <summary>Resolves approved roots and captures exact current bytes for every proposed mapping.</summary>
		public void Refresh(CollectionSetupPackage package)
		{
			long originals = 0;
			foreach (CollectionSetupFile file in package.Files)
			{
				file.Reviewed = false; file.Before = null; file.Destination = null; file.Detail = null;
				try
				{
					if (file.Root == CollectionSetupRoot.Skip || file.Root == CollectionSetupRoot.Unmapped)
					{ file.Status = L("NeedsMapping", "Choose a destination or skip"); continue; }
					file.Destination = Resolve(package.GameDirectory, package.DocumentsDirectory, file.Root, file.RelativePath);
					file.Before = ReadFile(file.Destination);
					originals += file.Before == null ? 0 : file.Before.Length;
					if (originals > CollectionSetupPackageReader.MaximumTotalBytes) throw new InvalidDataException(L("ValidationExistingSetupFilesExceedTheSupportedBackupSize", "Existing setup files exceed the supported backup size."));
					bool same = Equal(file.Before, file.Content);
					file.Status = same ? L("Identical", "Already identical — skipped") : file.Before == null ? L("New", "New file") : L("Replace", "Replace existing file");
					file.Detail = Describe(file);
					file.Reviewed = true;
				}
				catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException || error is NotSupportedException)
				{ file.Status = L("Blocked", "Blocked") + ": " + error.Message; }
			}
		}

		/// <summary>Publishes only the reviewed selection, retaining pre/postimages and intent before the first file changes.</summary>
		public string Apply(CollectionSetupPackage package, CollectionRevisionIdentity revision, CancellationToken token)
		{
			GameStoragePathSet paths = Paths(); CollectionTargetAuthority authority = Authority(paths);
			using (CollectionTargetMutationLease lease = CollectionTargetMutationLeaseManager.Shared.Acquire(authority, token))
			{
				RequireIdle(paths, authority, revision, lease);
				if (package.Revision != revision.ToString() || package.Target != authority.Target.Fingerprint ||
					!SamePath(package.GameDirectory, paths.GameInstallPath)) throw new InvalidOperationException(L("ValidationTheCollectionOrActiveGameChangedAfterReview", "The Collection or active game changed after review."));
				if (Receipts().Any(x => x.State == "Prepared" || x.State == "Undoing"))
					throw new InvalidOperationException(L("ValidationAnInterruptedSetupPackageNeedsUndoRecoverBeforeAnother", "An interrupted setup package needs Undo / recover before another package can be applied."));
				List<CollectionSetupFile> selected = package.Files.Where(x => x.Selected && x.Root != CollectionSetupRoot.Skip).ToList();
				if (selected.Count == 0) throw new InvalidOperationException(L("ValidationSelectAtLeastOneSetupFile", "Select at least one setup file."));
				var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (CollectionSetupFile file in selected)
				{
					string destination = Resolve(package.GameDirectory, package.DocumentsDirectory, file.Root, file.RelativePath);
					if (!file.Reviewed || !SamePath(destination, file.Destination) || !destinations.Add(destination))
						throw new InvalidOperationException(L("ValidationASetupMappingIsUnresolvedDuplicatedOrChangedAfter", "A setup mapping is unresolved, duplicated, or changed after review."));
					RequireImage(destination, CollectionSetupPackageReader.Hash(file.Before));
				}
				selected = selected.Where(x => !Equal(x.Before, x.Content)).ToList();
				if (selected.Count == 0) return L("NothingChanged", "All selected files are already identical. Nothing was changed.");
				string id = Guid.NewGuid().ToString("N");
				string directory = ReceiptDirectory(id);
				Regular(directory); Directory.CreateDirectory(directory);
				var receipt = new SetupReceipt { Version = 1, Id = id, Revision = revision.ToString(), Target = package.Target,
					GameDirectory = package.GameDirectory, DocumentsDirectory = package.DocumentsDirectory,
					ArchiveName = package.ArchiveName, ArchiveHash = package.ArchiveHash, CreatedUtc = DateTime.UtcNow,
					State = "Prepared", Files = new List<SetupReceiptFile>() };
				foreach (CollectionSetupFile file in selected) CaptureNewDirectories(receipt, file);
				for (int index = 0; index < selected.Count; index++)
				{
					token.ThrowIfCancellationRequested();
					CollectionSetupFile file = selected[index];
					WriteNew(Path.Combine(directory, index + ".after"), file.Content);
					if (file.Before != null) WriteNew(Path.Combine(directory, index + ".before"), file.Before);
					receipt.Files.Add(new SetupReceiptFile { Index = index, Root = file.Root,
						RelativePath = CollectionSetupPackageReader.Relative(file.RelativePath),
						BeforeHash = CollectionSetupPackageReader.Hash(file.Before), AfterHash = CollectionSetupPackageReader.Hash(file.Content) });
				}
				Save(receipt); // Durable intent and all backups precede game/settings publication.
				foreach (SetupReceiptFile file in receipt.Files)
				{
					token.ThrowIfCancellationRequested();
					string target = Resolve(receipt.GameDirectory, receipt.DocumentsDirectory, file.Root, file.RelativePath);
					byte[] after = Backup(receipt, file, false);
					Publish(target, after, file.BeforeHash);
				}
				foreach (SetupReceiptFile file in receipt.Files)
					RequireImage(Resolve(receipt.GameDirectory, receipt.DocumentsDirectory, file.Root, file.RelativePath), file.AfterHash);
				receipt.State = "Applied"; Save(receipt);
				return LanguageManager.Format("Collections.Setup.Applied", "Applied {0} setup files. Backups and undo information were retained.", receipt.Files.Count);
			}
		}

		/// <summary>Restores the latest package only when every affected file still matches a recorded pre/postimage.</summary>
		public string Undo(CollectionRevisionIdentity revision, CancellationToken token)
		{
			GameStoragePathSet paths = Paths(); CollectionTargetAuthority authority = Authority(paths);
			using (CollectionTargetMutationLease lease = CollectionTargetMutationLeaseManager.Shared.Acquire(authority, token))
			{
				RequireIdle(paths, authority, revision, lease, false);
				List<SetupReceipt> all = Receipts().Where(x => x.State != "Undone").OrderByDescending(x => x.CreatedUtc).ToList();
				SetupReceipt receipt = all.FirstOrDefault();
				if (receipt == null) return L("NoUndo", "There is no setup package to undo.");
				if (receipt.Revision != revision.ToString()) throw new InvalidOperationException(L("ValidationUndoTheMostRecentlyAppliedSetupPackageInIts", "Undo the most recently applied setup package in its own Collection first."));
				if (receipt.Target != authority.Target.Fingerprint || !SamePath(receipt.GameDirectory, paths.GameInstallPath))
					throw new InvalidOperationException(L("ValidationTheSetupBackupBelongsToADifferentGameStorage", "The setup backup belongs to a different game/storage target."));
				foreach (SetupReceiptFile file in receipt.Files)
				{
					string path = Resolve(receipt.GameDirectory, receipt.DocumentsDirectory, file.Root, file.RelativePath);
					string hash = CollectionSetupPackageReader.Hash(ReadFile(path));
					if (hash != file.BeforeHash && hash != file.AfterHash)
						throw new InvalidOperationException(L("UndoChanged", "Undo stopped because this file changed after setup: ") + path);
					Backup(receipt, file, false);
					if (file.BeforeHash != null) Backup(receipt, file, true);
				}
				receipt.State = "Undoing"; Save(receipt);
				foreach (SetupReceiptFile file in receipt.Files.AsEnumerable().Reverse())
				{
					token.ThrowIfCancellationRequested();
					string path = Resolve(receipt.GameDirectory, receipt.DocumentsDirectory, file.Root, file.RelativePath);
					if (CollectionSetupPackageReader.Hash(ReadFile(path)) == file.BeforeHash) continue;
					Publish(path, file.BeforeHash == null ? null : Backup(receipt, file, true), file.AfterHash);
				}
				foreach (SetupReceiptFile file in receipt.Files)
					RequireImage(Resolve(receipt.GameDirectory, receipt.DocumentsDirectory, file.Root, file.RelativePath), file.BeforeHash);
				foreach (SetupReceiptDirectory created in receipt.CreatedDirectories.OrderByDescending(x => x.RelativePath.Length))
				{
					string path = ResolveCreatedDirectory(receipt, created);
					if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, false);
				}
				receipt.State = "Undone"; Save(receipt);
				return L("Undone", "Setup files were restored. The package backups were kept.");
			}
		}

		/// <summary>Summarizes recorded setup overlays, including interrupted operations requiring explicit recovery.</summary>
		public string History(CollectionRevisionIdentity revision)
		{
			return String.Join(Environment.NewLine, Receipts().Where(x => x.Revision == revision.ToString()).OrderByDescending(x => x.CreatedUtc)
				.Select(x => x.CreatedUtc.ToLocalTime().ToString("g") + " — " + x.ArchiveName + " — " + L("State" + x.State, x.State) + Environment.NewLine +
					String.Join(Environment.NewLine, x.Files.Select(f => "  " + L(f.Root == CollectionSetupRoot.Game ? "RootGame" : "RootDocuments", f.Root.ToString()) + ": " + f.RelativePath))));
		}

		/// <summary>Reports whether the latest active receipt belongs to the displayed revision, including interrupted publications.</summary>
		public bool CanUndo(CollectionRevisionIdentity revision) { SetupReceipt latest = Receipts().Where(x => x.State != "Undone").OrderByDescending(x => x.CreatedUtc).FirstOrDefault(); return latest != null && latest.Revision == revision.ToString(); }

		/// <summary>Returns recorded publication state without treating instruction acknowledgement as package installation.</summary>
		public CollectionSetupPackageState GetState(CollectionRevisionIdentity revision)
		{
			List<SetupReceipt> receipts = Receipts().Where(x => x.Revision == revision.ToString()).ToList();
			SetupReceipt latest = receipts.Where(x => x.State != "Undone").OrderByDescending(x => x.CreatedUtc).FirstOrDefault();
			if (latest != null) return latest.State == "Applied" ? CollectionSetupPackageState.Applied : CollectionSetupPackageState.RecoveryRequired;
			return receipts.Count > 0 ? CollectionSetupPackageState.Restored : CollectionSetupPackageState.None;
		}

		/// <summary>Checks an instruction acknowledgement bound to the exact revision and curator text.</summary>
		public bool InstructionsAcknowledged(CollectionRevisionIdentity revision, string text)
		{
			string path = AcknowledgementPath(revision, text);
			Regular(path); return File.Exists(path) && new FileInfo(path).Length < 1024 && File.ReadAllText(path) == "nmm-ce.setup-instructions/1\n" + CollectionSetupPackageReader.Hash(Encoding.UTF8.GetBytes(text ?? String.Empty));
		}

		/// <summary>Records explicit user acknowledgement without claiming that the game's runtime has been verified.</summary>
		public void AcknowledgeInstructions(CollectionRevisionIdentity revision, string text)
		{
			string path = AcknowledgementPath(revision, text); Regular(path);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			AtomicWrite(path, Encoding.UTF8.GetBytes("nmm-ce.setup-instructions/1\n" + CollectionSetupPackageReader.Hash(Encoding.UTF8.GetBytes(text ?? String.Empty))));
		}

		/// <summary>Rejects active native work, an incomplete Collection and conflicting Witcher authorities before publication.</summary>
		private void RequireIdle(GameStoragePathSet paths, CollectionTargetAuthority authority, CollectionRevisionIdentity revision, CollectionTargetMutationLease lease, bool requireInstalled = true)
		{
			if (_services.ModActivationMonitor != null && _services.ModActivationMonitor.Tasks.Any(x => !x.IsCompleted))
				throw new InvalidOperationException(L("ValidationWaitForTheModActivationQueueToFinishBefore", "Wait for the mod activation queue to finish before applying setup files."));
			if (Witcher3ModSettingsLifecycleService.SupportsMode(paths.GameId) && Process.GetProcessesByName("witcher3").Any())
				throw new InvalidOperationException(L("ValidationCloseTheWitcher3BeforeChangingItsSetupFiles", "Close The Witcher 3 before changing its setup files."));
			if (Witcher3ModSettingsLifecycleService.SupportsMode(paths.GameId) &&
				Witcher3ModSettingsPublisher.InspectRecovery(PersonalDirectory(), Witcher3Edition.Legacy).Status != Witcher3ModSettingsRecoveryStatus.None)
				throw new InvalidOperationException(L("PendingOrder", "Resolve the pending Witcher load-order recovery before applying or undoing setup files."));
			if (!_store.Exists) throw new InvalidOperationException(L("ValidationFinishInstallingThisCollectionBeforeApplyingSetupFiles", "Finish installing this Collection before applying setup files."));
			_validator.ValidateAndReload(lease, authority, paths);
			if (new CollectionsOperationStore(_store).GetIncompleteOperations(authority.Target).Any(x => x.HasCrossedNativeBoundary || x.RequiresRecovery))
				throw new InvalidOperationException(L("ValidationFinishCollectionInstallationOrRecoveryBeforeApplyingOrUndoing", "Finish Collection installation or recovery before applying or undoing setup files."));
			if (requireInstalled && !new CollectionsAssociationStore(_store).GetAssociationsForTarget(authority.Target).Any(x =>
				x.Revision.Equals(revision) && x.State == CollectionAssociationState.Applied))
				throw new InvalidOperationException(L("ValidationThisExactCollectionRevisionMustFinishInstallationBeforeSetup", "This exact Collection revision must finish installation before setup files can be applied or undone."));
		}

		/// <summary>Resolves only reviewed roots; the archive's drive and username placeholders never become authority.</summary>
		private string Resolve(string game, string documents, CollectionSetupRoot root, string relative)
		{
			relative = CollectionSetupPackageReader.Relative(relative);
			string directory;
			if (root == CollectionSetupRoot.Game)
			{
				directory = game;
			}
			else if (root == CollectionSetupRoot.Documents)
			{
				if (String.IsNullOrWhiteSpace(documents)) throw new InvalidDataException(L("ValidationChooseThisGameSDocumentsFolderFirst", "Choose this game's Documents folder first."));
				RequireContained(PersonalDirectory(), documents); directory = documents;
				if (!new[] { ".settings", ".ini", ".cfg", ".xml", ".txt", ".json" }.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase))
					throw new InvalidDataException(L("ValidationDocumentsSetupSupportsSettingsConfigurationTextFilesOnly", "Documents setup supports settings/configuration text files only."));
			}
			else throw new InvalidDataException(L("ValidationTheSetupFileNeedsAnApprovedDestination", "The setup file needs an approved destination."));
			if (new[] { ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".lnk", ".url" }.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase))
				throw new InvalidDataException(L("ValidationExecutableSetupPayloadsRequireManualReviewOutsideThisImporter", "Executable setup payloads require manual review outside this importer."));
			string path = Path.GetFullPath(Path.Combine(directory, relative)); RequireContained(directory, path); Regular(path);
			if (Directory.Exists(path)) throw new InvalidDataException(L("ValidationASetupFileDestinationIsADirectory", "A setup file destination is a directory."));
			return path;
		}

		/// <summary>Describes exact replacement semantics without collapsing duplicate input bindings.</summary>
		private static string Describe(CollectionSetupFile file)
		{
			string detail = file.PersonalSettings ? L("PersonalWarning", "Optional preset: replaces personal settings, graphics/gameplay preferences or key bindings. Repeated bindings and original encoding are preserved byte for byte.") :
				L("WholeFile", "Exact file replacement with a complete original backup. Native archive staging is not modified.");
			if (Path.GetFileName(file.Source).Equals("mods.settings", StringComparison.OrdinalIgnoreCase))
			{
				string section = String.Empty; var disabled = new List<string>();
				foreach (string line in CollectionSetupPackageReader.Decode(file.Content).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
				{
					string text = line.Trim();
					if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal)) section = text;
					else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^Enabled\s*=\s*0\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) disabled.Add(section);
				}
				detail += Environment.NewLine + L("DisabledFolders", "Game mod folders disabled by this preset:") + " " + String.Join(", ", disabled);
			}
			return detail;
		}

		/// <summary>Reads a bounded, regular physical file or records its absence.</summary>
		private static byte[] ReadFile(string path) { Regular(path); if (!File.Exists(path)) return null; using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return CollectionSetupPackageReader.ReadBounded(input); }
		/// <summary>Checks stale-review and undo preconditions against exact bytes.</summary>
		private static void RequireImage(string path, string hash) { if (CollectionSetupPackageReader.Hash(ReadFile(path)) != hash) throw new InvalidOperationException(L("ChangedAfterReview", "The file changed after review: ") + path); }
		/// <summary>Compares absence and complete byte content.</summary>
		private static bool Equal(byte[] a, byte[] b) { return a == null ? b == null : b != null && a.SequenceEqual(b); }
		/// <summary>Rejects links at the file and every existing ancestor.</summary>
		private static void Regular(string path) { Witcher3CollectionRecordStore.RequireRegularAncestors(path); }
		/// <summary>Requires containment beneath, rather than equality with, an approved root.</summary>
		private static void RequireContained(string root, string path) { string prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar; if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L("ValidationSetupDestinationEscapesItsApprovedFolder", "Setup destination escapes its approved folder.")); }
		/// <summary>Compares configured physical paths using Windows path semantics.</summary>
		private static bool SamePath(string a, string b) { return a != null && b != null && String.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
		/// <summary>Writes a new sealed backup with durable file data.</summary>
		private static void WriteNew(string path, byte[] bytes) { Regular(path); using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); } }
		/// <summary>Atomically replaces a file instead of writing through a native hardlink into its staging source.</summary>
		private static void AtomicWrite(string path, byte[] bytes, string expected = null, bool checkImage = false)
		{
			Regular(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
			string temporary = path + ".nmm-setup-" + Guid.NewGuid().ToString("N") + ".tmp";
			try { WriteNew(temporary, bytes); Regular(path); if (checkImage) RequireImage(path, expected); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
			finally { if (File.Exists(temporary)) File.Delete(temporary); }
		}
		/// <summary>Rechecks the expected image immediately before an atomic replacement or a known newly-created file removal.</summary>
		private static void Publish(string path, byte[] bytes, string expected) { RequireImage(path, expected); if (bytes == null) File.Delete(path); else AtomicWrite(path, bytes, expected, true); RequireImage(path, CollectionSetupPackageReader.Hash(bytes)); }
		/// <summary>Records only absent destination directories so undo can remove newly-created empty DLC folders without recursion.</summary>
		private static void CaptureNewDirectories(SetupReceipt receipt, CollectionSetupFile file)
		{
			string root = Path.GetFullPath(file.Root == CollectionSetupRoot.Game ? receipt.GameDirectory : receipt.DocumentsDirectory).TrimEnd('\\') + "\\";
			for (string path = Path.GetDirectoryName(file.Destination); path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(path); path = Path.GetDirectoryName(path))
			{
				string relative = path.Substring(root.Length);
				if (!receipt.CreatedDirectories.Any(x => x.Root == file.Root && x.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase)))
					receipt.CreatedDirectories.Add(new SetupReceiptDirectory { Root = file.Root, RelativePath = relative });
			}
		}
		/// <summary>Validates a recorded newly-created directory beneath the current reviewed roots.</summary>
		private string ResolveCreatedDirectory(SetupReceipt receipt, SetupReceiptDirectory directory)
		{
			if (directory == null || (directory.Root != CollectionSetupRoot.Game && directory.Root != CollectionSetupRoot.Documents)) throw new InvalidDataException(L("ValidationInvalidSetupDirectoryRoot", "Invalid setup directory root."));
			string root = directory.Root == CollectionSetupRoot.Game ? receipt.GameDirectory : receipt.DocumentsDirectory;
			if (directory.Root == CollectionSetupRoot.Documents) RequireContained(PersonalDirectory(), root);
			string path = Path.GetFullPath(Path.Combine(root, CollectionSetupPackageReader.Relative(directory.RelativePath)));
			RequireContained(root, path); Regular(path); return path;
		}
		/// <summary>Opens and validates retained recovery bytes before any restore.</summary>
		private byte[] Backup(SetupReceipt receipt, SetupReceiptFile file, bool before) { byte[] bytes = ReadFile(Path.Combine(ReceiptDirectory(receipt.Id), file.Index + (before ? ".before" : ".after"))); if (bytes == null || CollectionSetupPackageReader.Hash(bytes) != (before ? file.BeforeHash : file.AfterHash)) throw new InvalidDataException(L("ValidationASetupBackupFailedItsIntegrityCheck", "A setup backup failed its integrity check.")); return bytes; }
		/// <summary>Persists a versioned receipt atomically at the existing Collection storage location.</summary>
		private void Save(SetupReceipt receipt) { AtomicWrite(Path.Combine(ReceiptDirectory(receipt.Id), "receipt.json"), Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(receipt, JsonSettings))); }
		/// <summary>Loads bounded versioned receipts; unknown or corrupt journals block mutation rather than disappearing.</summary>
		private List<SetupReceipt> Receipts()
		{
			string root = PackageDirectory(); Regular(root); var results = new List<SetupReceipt>();
			if (!Directory.Exists(root)) return results;
			string[] directories = Directory.EnumerateDirectories(root).Take(1025).ToArray();
			if (directories.Length > 1024) throw new InvalidDataException(L("ValidationSetupHistoryExceedsItsSupportedSize", "Setup history exceeds its supported size."));
			foreach (string directory in directories)
			{
				Regular(directory); string path = Path.Combine(directory, "receipt.json");
				if (!File.Exists(path)) continue;
				if (results.Count == 1024 || new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException(L("ValidationSetupHistoryExceedsItsSupportedSize", "Setup history exceeds its supported size."));
				SetupReceipt item = JsonConvert.DeserializeObject<SetupReceipt>(Encoding.UTF8.GetString(ReadFile(path)), JsonSettings);
				Guid id;
				if (item == null || item.Version != 1 || !Guid.TryParseExact(item.Id, "N", out id) || Path.GetFileName(directory) != item.Id ||
					item.Files == null || item.Files.Count == 0 || item.Files.Count > CollectionSetupPackageReader.MaximumFiles || item.Files.Any(x => x == null) || item.Files.Select(x => x.Index).Distinct().Count() != item.Files.Count ||
					!new[] { "Prepared", "Applied", "Undoing", "Undone" }.Contains(item.State)) throw new InvalidDataException(L("ValidationInvalidOrUnsupportedSetupRecoveryReceipt", "Invalid or unsupported setup recovery receipt."));
				foreach (SetupReceiptFile file in item.Files) { if (file.Index < 0 || !ValidHash(file.AfterHash) || (file.BeforeHash != null && !ValidHash(file.BeforeHash))) throw new InvalidDataException(L("ValidationInvalidSetupBackupIdentity", "Invalid setup backup identity.")); Resolve(item.GameDirectory, item.DocumentsDirectory, file.Root, file.RelativePath); }
				if (item.CreatedDirectories == null || item.CreatedDirectories.Count > 65536) throw new InvalidDataException(L("ValidationInvalidSetupDirectoryHistory", "Invalid setup directory history."));
				foreach (SetupReceiptDirectory directoryEntry in item.CreatedDirectories) ResolveCreatedDirectory(item, directoryEntry);
				results.Add(item);
			}
			return results;
		}
		/// <summary>Validates the only supported content identity before loading retained bytes.</summary>
		private static bool ValidHash(string hash) { return hash != null && System.Text.RegularExpressions.Regex.IsMatch(hash, "^[0-9a-f]{64}$"); }
		/// <summary>Gets the target-local setup receipt directory without changing the Collections database schema.</summary>
		private string PackageDirectory() { return Path.Combine(_store.StoreDirectory, "SetupPackages"); }
		/// <summary>Resolves a generated receipt identity beneath setup storage.</summary>
		private string ReceiptDirectory(string id) { Guid value; if (!Guid.TryParseExact(id, "N", out value)) throw new InvalidDataException(L("ValidationInvalidSetupReceiptIdentity", "Invalid setup receipt identity.")); return Path.Combine(PackageDirectory(), id); }
		/// <summary>Keys acknowledgement by revision and the complete instruction content.</summary>
		private string AcknowledgementPath(CollectionRevisionIdentity revision, string text) { return Path.Combine(PackageDirectory(), "instructions-" + CollectionSetupPackageReader.Hash(Encoding.UTF8.GetBytes(revision + "\n" + text)) + ".ack"); }
		/// <summary>Uses the active game/storage paths on every operation.</summary>
		private GameStoragePathSet Paths() { return _storage.FromGameMode(_services.ModManager.GameMode); }
		/// <summary>Resolves the canonical physical target identity.</summary>
		private CollectionTargetAuthority Authority(GameStoragePathSet paths) { return new CollectionTargetIdentityResolver(_storage).Resolve(paths); }
		/// <summary>Uses the actual redirected Documents root supplied by the application.</summary>
		private string PersonalDirectory() { return _services.ModManager.EnvironmentInfo.PersonalDataFolderPath; }
		/// <summary>Resolves localized setup text.</summary>
		private static string L(string key, string text) { return LanguageManager.Get("Collections.Setup." + key, text); }
	}

	/// <summary>Versioned durable intent for an explicit setup overlay and its inverse.</summary>
	internal sealed class SetupReceipt
	{
		public int Version { get; set; }
		public string Id { get; set; }
		public string Revision { get; set; }
		public string Target { get; set; }
		public string GameDirectory { get; set; }
		public string DocumentsDirectory { get; set; }
		public string ArchiveName { get; set; }
		public string ArchiveHash { get; set; }
		public DateTime CreatedUtc { get; set; }
		public string State { get; set; }
		public List<SetupReceiptDirectory> CreatedDirectories { get; set; } = new List<SetupReceiptDirectory>();
		public List<SetupReceiptFile> Files { get; set; }
	}

	/// <summary>A directory absent before setup, eligible for non-recursive removal only while empty.</summary>
	internal sealed class SetupReceiptDirectory
	{
		public CollectionSetupRoot Root { get; set; }
		public string RelativePath { get; set; }
	}

	/// <summary>Root-relative destination and pre/postimage identities for one reversible setup file.</summary>
	internal sealed class SetupReceiptFile
	{
		public int Index { get; set; }
		public CollectionSetupRoot Root { get; set; }
		public string RelativePath { get; set; }
		public string BeforeHash { get; set; }
		public string AfterHash { get; set; }
	}
}
