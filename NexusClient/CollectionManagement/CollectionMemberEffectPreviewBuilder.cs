using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Newtonsoft.Json;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Util;
using SevenZip;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Converts an already translated C5 native recipe into detached C6.4 impact-planning effects without executing it.
	/// </summary>
	public sealed class CollectionMemberEffectPreviewBuilder
	{
		// A preparation can be repeated to refresh a review. Do not unpack and hash unchanged
		// multi-gigabyte archive entries every time. This is read-only preview evidence only;
		// native submission and the installer still perform their own fresh verification.
		private const int MaxCachedArchiveEntryDigests = 131072;
		private readonly string _sourceDigestCacheDirectory;
		private readonly bool _collectPreparationContent;
		private readonly object _archiveEntryCacheLock = new object();
		private readonly Queue<string> _archiveEntryCacheOrder = new Queue<string>();
		private readonly Dictionary<string, CachedArchiveEntryDigest> _archiveEntryDigests =
			new Dictionary<string, CachedArchiveEntryDigest>(StringComparer.Ordinal);

		/// <summary>Creates a preview builder with preparation-local digest reuse.</summary>
		public CollectionMemberEffectPreviewBuilder()
		{
		}

		/// <summary>Configures optional content verification with digest reuse across preparations and application restarts.</summary>
		internal CollectionMemberEffectPreviewBuilder(CollectionsStore store, bool collectPreparationContent = false)
		{
			if (store == null) throw new ArgumentNullException(nameof(store));
			_sourceDigestCacheDirectory = Path.Combine(store.StoreDirectory, "ArchiveEntryHashCache");
			_collectPreparationContent = collectPreparationContent;
		}

		private sealed class CachedArchiveEntryDigest
		{
			internal readonly IMod Mod;
			internal readonly long ArchiveByteLength;
			internal readonly long ArchiveLastWriteUtcTicks;
			internal readonly CollectionContentHash Hash;
			internal readonly long SourceByteLength;

			internal CachedArchiveEntryDigest(IMod mod, FileInfo archive, CollectionContentHash hash, long sourceByteLength)
			{
				Mod = mod;
				ArchiveByteLength = archive.Length;
				ArchiveLastWriteUtcTicks = archive.LastWriteTimeUtc.Ticks;
				Hash = hash;
				SourceByteLength = sourceByteLength;
			}

			internal bool Matches(IMod mod, FileInfo archive)
			{
				return ReferenceEquals(Mod, mod) && ArchiveByteLength == archive.Length &&
					ArchiveLastWriteUtcTicks == archive.LastWriteTimeUtc.Ticks;
			}
		}
		/// <summary>
		/// Builds a root-aware read-only effect preview from one exact resolved member and translated native recipe.
		/// </summary>
		public CollectionMemberEffectPreview Build(ResolvedCollectionMemberPlan member, ModInstallationRecipeInput recipeInput,
			IGameMode gameMode, IMod mod, IPluginManager pluginManager = null)
		{
			return BuildCore(member, recipeInput, gameMode, mod, pluginManager, false, null);
		}

		/// <summary>
		/// Plans destinations and installer effects from archive metadata without decompressing or hashing source files.
		/// Exact source digests are collected using the native installer's already-open archive transaction.
		/// Explicit verify/repair callers can request the content-preview path instead.
		/// </summary>
		internal CollectionMemberEffectPreview BuildForPreparation(ResolvedCollectionMemberPlan member,
			ModInstallationRecipeInput recipeInput, IGameMode gameMode, IMod mod, IPluginManager pluginManager,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (_collectPreparationContent)
				return BuildForContentPreparation(member, recipeInput, gameMode, mod, pluginManager, cancellationToken);
			return BuildCore(member, recipeInput, gameMode, mod, pluginManager, false, null, cancellationToken, true);
		}

		/// <summary>Collects exact digests without opening or closing the archive transaction owned by the native installer.</summary>
		internal CollectionMemberEffectPreview BuildFromOpenArchive(ResolvedCollectionMemberPlan member,
			ModInstallationRecipeInput recipeInput, IGameMode gameMode, IMod mod, IPluginManager pluginManager,
			CancellationToken cancellationToken)
		{
			return BuildCore(member, recipeInput, gameMode, mod, pluginManager, false, null, cancellationToken);
		}

		/// <summary>Retains the explicit content-preview path for callers that need source hashes before native installation.</summary>
		internal CollectionMemberEffectPreview BuildForContentPreparation(ResolvedCollectionMemberPlan member,
			ModInstallationRecipeInput recipeInput, IGameMode gameMode, IMod mod, IPluginManager pluginManager,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			CollectionPreparationProgress.Report(LanguageManager.Format("Collections.Workflow.PreparingMemberFiles",
				"Preparing mod files: {0}", member.DisplayName), true);
			LoadSavedSourceDigests(mod, recipeInput);
			if (HasCachedSourceDigests(mod, recipeInput) || !ShouldUseReadOnlyArchiveTransaction(mod, recipeInput))
			{
				CollectionMemberEffectPreview preview = BuildCore(member, recipeInput, gameMode, mod, pluginManager, true, null, cancellationToken);
				if (preview.IsComplete) SaveSourceDigests(mod, recipeInput);
				return preview;
			}

			// NMM's native archive read transaction keeps one extractor alive for the whole preview.
			// Solid 7z archives are expanded once into bounded scratch storage, not reopened and
			// decompressed for every selected entry. Always release the scratch data, even on failure.
			CancelProgressEventHandler cancelOnRequest = (sender, progress) =>
			{
				if (cancellationToken.IsCancellationRequested) progress.Cancel = true;
				CollectionPreparationProgress.Report(LanguageManager.Format("Collections.Workflow.UnpackingMember",
					"Unpacking mod once for content checks: {0} ({1:P0})", member.DisplayName, progress.PercentComplete));
			};
			mod.ReadOnlyInitProgressUpdated += cancelOnRequest;
			try
			{
				CollectionPreparationProgress.Report(LanguageManager.Format("Collections.Workflow.OpeningMemberArchive",
					"Opening mod archive for content checks: {0}", member.DisplayName), true);
				try { mod.BeginReadOnlyTransaction(new FileUtil()); }
				catch (Exception) when (cancellationToken.IsCancellationRequested)
				{
					throw new OperationCanceledException(cancellationToken);
				}
				cancellationToken.ThrowIfCancellationRequested();
				CollectionMemberEffectPreview preview = BuildCore(member, recipeInput, gameMode, mod, pluginManager, true, null, cancellationToken);
				if (preview.IsComplete) SaveSourceDigests(mod, recipeInput);
				return preview;
			}
			finally
			{
				mod.ReadOnlyInitProgressUpdated -= cancelOnRequest;
				mod.EndReadOnlyTransaction();
			}
		}

		/// <summary>
		/// Avoid whole-archive extraction when scratch storage is insufficient or the preview has only one file.
		/// A normal non-solid archive only keeps its native extractor open and doesn't need
		/// extra bulk disk space; for a solid archive NMM's read-only mode extracts all entries.
		/// This decision affects I/O strategy only, never hash identity or review contents.
		/// </summary>
		private static bool ShouldUseReadOnlyArchiveTransaction(IMod mod, ModInstallationRecipeInput recipeInput)
		{
			if (mod == null || recipeInput == null || !recipeInput.HasNativePlan ||
				recipeInput.NativeOperations.OfType<InstallModFileOperation>().Select(x => x.SourcePath)
					.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() < 2)
				return false;
			string path = mod.ModArchivePath;
			if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
				return false;
			try
			{
				using (SevenZipExtractor extractor = Archive.GetExtractor(path))
				{
					if (!extractor.IsSolid)
						return true;
					string driveRoot = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()));
					if (String.IsNullOrEmpty(driveRoot)) return false;
					// A fixed 2 GiB ceiling forced large solid archives to restart decompression for every file.
					// Bound scratch by the actual available space instead, retaining a reserve for other work.
					long maxScratchBytes = new DriveInfo(driveRoot).AvailableFreeSpace - 512L * 1024 * 1024;
					if (maxScratchBytes <= 0) return false;
					long unpackedBytes = 0;
					foreach (ArchiveFileInfo entry in extractor.ArchiveFileData)
					{
						// Bulk extraction must never consume an unsafe archive path.
						string entryPath = (entry.FileName ?? String.Empty).Replace('/', '\\');
						if (Path.IsPathRooted(entryPath) || entryPath.Contains(":") || entryPath.Split('\\').Any(x => x == ".." || x == "."))
							return false;
						if (entry.IsDirectory) continue;
						long length = Convert.ToInt64(entry.Size);
						if (length < 0 || length > maxScratchBytes - unpackedBytes)
							return false;
						unpackedBytes += length;
					}
					return unpackedBytes <= maxScratchBytes;
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
				ex is OverflowException || ex is ArgumentException || ex is NotSupportedException)
			{
				// Header probe is an optimization. Let ordinary preparation do authoritative validation.
				return false;
			}
		}

		/// <summary>Skips read-only initialization when every selected source already has a digest for this unchanged archive.</summary>
		private bool HasCachedSourceDigests(IMod mod, ModInstallationRecipeInput recipeInput)
		{
			if (mod == null || recipeInput == null || !recipeInput.HasNativePlan ||
				recipeInput.Validation == null || recipeInput.Validation.ExpectedContent == null)
				return false;
			string path = !String.IsNullOrWhiteSpace(mod.ModArchivePath) ? mod.ModArchivePath : mod.Filename;
			if (String.IsNullOrWhiteSpace(path)) return false;
			var archive = new FileInfo(Path.GetFullPath(path));
			if (!archive.Exists || archive.Length != recipeInput.Validation.ExpectedContent.ByteLength)
				return false;
			List<string> sources = recipeInput.NativeOperations.OfType<InstallModFileOperation>()
				.Select(x => x.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (sources.Count == 0) return false;
			lock (_archiveEntryCacheLock)
			{
				foreach (string source in sources)
				{
					string key = recipeInput.Validation.ExpectedContent.Sha256 + "\n" + archive.FullName + "\n" + source;
					CachedArchiveEntryDigest cached;
					if (!_archiveEntryDigests.TryGetValue(key, out cached) || !cached.Matches(mod, archive))
						return false;
				}
			}
			return true;
		}

		/// <summary>Evicts individual old digests instead of discarding every mod's preparation evidence at once.</summary>
		private void CacheSourceDigest(string key, CachedArchiveEntryDigest digest)
		{
			if (!_archiveEntryDigests.ContainsKey(key))
			{
				while (_archiveEntryDigests.Count >= MaxCachedArchiveEntryDigests)
					_archiveEntryDigests.Remove(_archiveEntryCacheOrder.Dequeue());
				_archiveEntryCacheOrder.Enqueue(key);
			}
			_archiveEntryDigests[key] = digest;
		}

		/// <summary>Restores optional preparation evidence only for the same archive identity, stamp and recipe context.</summary>
		private void LoadSavedSourceDigests(IMod mod, ModInstallationRecipeInput recipeInput)
		{
			if (_sourceDigestCacheDirectory == null || HasCachedSourceDigests(mod, recipeInput)) return;
			try
			{
				FileInfo archive = GetCacheArchive(mod, recipeInput);
				if (archive == null) return;
				string context = GetSourceDigestCacheContext(mod, recipeInput, archive);
				var file = new FileInfo(Path.Combine(_sourceDigestCacheDirectory, context + ".json"));
				if (!file.Exists || file.Length > 64L * 1024 * 1024) return;
				SourceDigestCacheRecord record = JsonConvert.DeserializeObject<SourceDigestCacheRecord>(File.ReadAllText(file.FullName));
				if (record == null || record.SchemaVersion != 1 || record.ContextIdentity != context ||
					!StringComparer.OrdinalIgnoreCase.Equals(record.ArchivePath, archive.FullName) ||
					record.ArchiveSha256 != recipeInput.Validation.ExpectedContent.Sha256 ||
					record.ArchiveByteLength != archive.Length || record.ArchiveLastWriteUtcTicks != archive.LastWriteTimeUtc.Ticks ||
					record.Files == null || record.Files.Count > MaxCachedArchiveEntryDigests)
					return;
				var restored = new Dictionary<string, CachedArchiveEntryDigest>(StringComparer.Ordinal);
				foreach (SourceDigestCacheEntry entry in record.Files)
				{
					if (entry == null || String.IsNullOrWhiteSpace(entry.SourcePath) || entry.ByteLength < 0 || restored.ContainsKey(entry.SourcePath))
						return;
					restored.Add(entry.SourcePath, new CachedArchiveEntryDigest(mod, archive,
						CollectionContentHash.FromSha256(entry.Sha256), entry.ByteLength));
				}
				var after = new FileInfo(archive.FullName);
				if (!after.Exists || after.Length != archive.Length || after.LastWriteTimeUtc.Ticks != archive.LastWriteTimeUtc.Ticks) return;
				lock (_archiveEntryCacheLock)
				{
					foreach (KeyValuePair<string, CachedArchiveEntryDigest> entry in restored)
						CacheSourceDigest(recipeInput.Validation.ExpectedContent.Sha256 + "\n" + archive.FullName + "\n" + entry.Key, entry.Value);
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException ||
				ex is ArgumentException || ex is NotSupportedException)
			{
				System.Diagnostics.Trace.TraceWarning("Collection source digest cache could not be read: {0}", ex.Message);
			}
		}

		/// <summary>Persists only digests actually read from the unchanged archive, separately from authoritative plans.</summary>
		private void SaveSourceDigests(IMod mod, ModInstallationRecipeInput recipeInput)
		{
			if (_sourceDigestCacheDirectory == null) return;
			string temporaryPath = null;
			try
			{
				FileInfo archive = GetCacheArchive(mod, recipeInput);
				if (archive == null) return;
				var record = new SourceDigestCacheRecord
				{
					SchemaVersion = 1, ContextIdentity = GetSourceDigestCacheContext(mod, recipeInput, archive),
					ArchivePath = archive.FullName, ArchiveSha256 = recipeInput.Validation.ExpectedContent.Sha256,
					ArchiveByteLength = archive.Length, ArchiveLastWriteUtcTicks = archive.LastWriteTimeUtc.Ticks,
					Files = new List<SourceDigestCacheEntry>()
				};
				lock (_archiveEntryCacheLock)
				{
					foreach (string source in recipeInput.NativeOperations.OfType<InstallModFileOperation>()
						.Select(x => x.SourcePath).Distinct(StringComparer.Ordinal))
					{
						CachedArchiveEntryDigest cached;
						string key = record.ArchiveSha256 + "\n" + archive.FullName + "\n" + source;
						if (!_archiveEntryDigests.TryGetValue(key, out cached) || !cached.Matches(mod, archive)) return;
						record.Files.Add(new SourceDigestCacheEntry { SourcePath = source, Sha256 = cached.Hash.Value, ByteLength = cached.SourceByteLength });
					}
				}
				if (record.Files.Count == 0) return;
				Directory.CreateDirectory(_sourceDigestCacheDirectory);
				string cachePath = Path.Combine(_sourceDigestCacheDirectory, record.ContextIdentity + ".json");
				temporaryPath = cachePath + "." + Nexus.Client.Util.FileUtil.CreateTempToken() + ".tmp";
				File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(record));
				var after = new FileInfo(archive.FullName);
				if (!after.Exists || after.Length != archive.Length || after.LastWriteTimeUtc.Ticks != archive.LastWriteTimeUtc.Ticks) return;
				if (File.Exists(cachePath)) File.Replace(temporaryPath, cachePath, null);
				else File.Move(temporaryPath, cachePath);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException ||
				ex is ArgumentException || ex is NotSupportedException)
			{
				System.Diagnostics.Trace.TraceWarning("Collection source digest cache could not be saved: {0}", ex.Message);
			}
			finally
			{
				try { if (temporaryPath != null && File.Exists(temporaryPath)) File.Delete(temporaryPath); }
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					System.Diagnostics.Trace.TraceWarning("Collection source digest cache temporary file could not be removed: {0}", ex.Message);
				}
			}
		}

		/// <summary>Checks the managed archive's stamp without reading its contents.</summary>
		private static FileInfo GetCacheArchive(IMod mod, ModInstallationRecipeInput recipeInput)
		{
			if (mod == null || recipeInput == null || !recipeInput.HasNativePlan || recipeInput.Validation == null ||
				recipeInput.Validation.ExpectedContent == null) return null;
			string path = !String.IsNullOrWhiteSpace(mod.ModArchivePath) ? mod.ModArchivePath : mod.Filename;
			if (String.IsNullOrWhiteSpace(path)) return null;
			var archive = new FileInfo(Path.GetFullPath(path));
			return archive.Exists && archive.Length == recipeInput.Validation.ExpectedContent.ByteLength ? archive : null;
		}

		/// <summary>Scopes logical source digests to the archive, mod implementation, format and recorded recipe.</summary>
		private static string GetSourceDigestCacheContext(IMod mod, ModInstallationRecipeInput recipeInput, FileInfo archive)
		{
			string identity = "archive-entry-digests/1\n" + archive.FullName.ToUpperInvariant() + "\n" +
				recipeInput.Validation.ExpectedContent.Sha256 + "\n" + recipeInput.RecipeFingerprint + "\n" +
				mod.GetType().AssemblyQualifiedName + "\n" + (mod.Format == null ? String.Empty : mod.Format.Id);
			using (SHA256 sha256 = SHA256.Create())
				return BitConverter.ToString(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(identity))).Replace("-", String.Empty).ToLowerInvariant();
		}

		/// <summary>Versioned, optional preparation cache; never used as native installation or recovery evidence.</summary>
		private sealed class SourceDigestCacheRecord
		{
			public int SchemaVersion { get; set; }
			public string ContextIdentity { get; set; }
			public string ArchivePath { get; set; }
			public string ArchiveSha256 { get; set; }
			public long ArchiveByteLength { get; set; }
			public long ArchiveLastWriteUtcTicks { get; set; }
			public List<SourceDigestCacheEntry> Files { get; set; }
		}

		/// <summary>Stores one already observed logical source's exact hash and byte length.</summary>
		private sealed class SourceDigestCacheEntry
		{
			public string SourcePath { get; set; }
			public string Sha256 { get; set; }
			public long ByteLength { get; set; }
		}

		/// <summary>
		/// Rebuilds live path/plugin/config effects without re-extracting archive members after the caller has
		/// just SHA-256 verified the COMPLETE incoming archive against the exact retained C5 recipe identity.
		/// A snapshot without exact file digests is not eligible. The native installer still hashes the full
		/// archive before mutation and C6.8 separately hashes the physical installed file contents.
		/// </summary>
		internal CollectionMemberEffectPreview BuildFromFreshVerifiedArchive(ResolvedCollectionMemberPlan member,
			ModInstallationRecipeInput recipeInput, IGameMode gameMode, IMod mod, IPluginManager pluginManager,
			CollectionMemberEffectPreview reviewed)
		{
			if (member == null) throw new ArgumentNullException(nameof(member));
			if (recipeInput == null) throw new ArgumentNullException(nameof(recipeInput));
			if (reviewed == null || !reviewed.IsComplete || reviewed.Files.Any(x => !x.HasExactContentIdentity ||
				x.ExpectedContentHash.Algorithm != CollectionContentHashAlgorithm.Sha256))
				throw new InvalidDataException("Fast Collection submission requires exact reviewed SHA-256 identities for every file effect.");
			if (!reviewed.MemberKey.Equals(member.MemberKey) || !reviewed.RecipeIdentity.Equals(member.RecipeIdentity) ||
				reviewed.InstallMethod != recipeInput.InstallContext.Method || reviewed.InstallRoot != recipeInput.InstallContext.InstallRoot)
				throw new InvalidDataException("The reviewed file identities do not belong to the exact submitting member and native recipe context.");
			return BuildCore(member, recipeInput, gameMode, mod, pluginManager, false,
				reviewed.Files.ToDictionary(x => x.Target));
		}

		private CollectionMemberEffectPreview BuildCore(ResolvedCollectionMemberPlan member, ModInstallationRecipeInput recipeInput,
			IGameMode gameMode, IMod mod, IPluginManager pluginManager, bool reusePreparationDigests,
			IDictionary<ModDeploymentTarget, CollectionPlannedFileEffect> freshlyVerifiedArchiveFiles,
			CancellationToken cancellationToken = default(CancellationToken), bool deferArchiveDigests = false)
		{
			if (member == null)
				throw new ArgumentNullException(nameof(member));
			if (recipeInput == null)
				throw new ArgumentNullException(nameof(recipeInput));
			if (gameMode == null)
				throw new ArgumentNullException(nameof(gameMode));
			if (mod == null)
				throw new ArgumentNullException(nameof(mod));
			if (!StringComparer.Ordinal.Equals(member.RecipeIdentity.Fingerprint, recipeInput.RecipeFingerprint))
				throw new ArgumentException("The translated native recipe must match the resolved Collection member recipe identity.", nameof(recipeInput));
			if (!recipeInput.HasNativePlan)
				throw new ArgumentException("C6.4 effect preview requires an already translated C5 native operation plan.", nameof(recipeInput));

			long performanceStarted = CollectionPerformanceMetrics.StartTiming();
			var files = new Dictionary<ModDeploymentTarget, CollectionPlannedFileEffect>();
			var freshlyReadSources = new Dictionary<string, CollectionPlannedFileEffect>(StringComparer.OrdinalIgnoreCase);
			var iniEdits = new Dictionary<CollectionNativeIniKey, CollectionPlannedIniEffect>();
			var gameValues = new Dictionary<string, CollectionPlannedGameValueEffect>(StringComparer.Ordinal);
			var requestedPluginActivations = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			var pluginOrderingEffects = new List<CollectionPlannedPluginEffect>();
			var issues = new List<CollectionEffectPreviewIssue>();

			foreach (ScriptedInstallOperation operation in recipeInput.NativeOperations)
			{
				cancellationToken.ThrowIfCancellationRequested();
				InstallModFileOperation installFile = operation as InstallModFileOperation;
				if (installFile != null)
				{
					ModDeploymentTarget target = ResolveTarget(gameMode, mod, installFile.DestinationPath, recipeInput.InstallContext.InstallRoot);
					if (deferArchiveDigests)
						files[target] = new CollectionPlannedFileEffect(target);
					else
						AddArchiveSourceFile(files, target, member.MemberKey, recipeInput, mod, installFile.SourcePath,
							reusePreparationDigests, freshlyVerifiedArchiveFiles, freshlyReadSources, cancellationToken);
					AddImplicitPluginActivation(requestedPluginActivations, gameMode, pluginManager, target, installFile);
					continue;
				}

				GenerateDataFileOperation generatedFile = operation as GenerateDataFileOperation;
				if (generatedFile != null)
				{
					// Native C5 execution registers generated plugins but deliberately does not request implicit activation.
					byte[] generatedBytes = generatedFile.Data ?? new byte[0];
					AddFile(files, ResolveTarget(gameMode, mod, generatedFile.DestinationPath, recipeInput.InstallContext.InstallRoot),
						ComputeContentHash(generatedBytes), generatedBytes.LongLength);
					continue;
				}

				EditIniOperation editIni = operation as EditIniOperation;
				if (editIni != null)
				{
					var key = new CollectionNativeIniKey(editIni.SettingsFileName, editIni.Section, editIni.Key);
					iniEdits[key] = new CollectionPlannedIniEffect(key, editIni.Value);
					continue;
				}

				EditGameSpecificValueOperation editGameValue = operation as EditGameSpecificValueOperation;
				if (editGameValue != null)
				{
					gameValues[editGameValue.Key] = new CollectionPlannedGameValueEffect(editGameValue.Key, editGameValue.Value);
					continue;
				}

				SetPluginActivationOperation activation = operation as SetPluginActivationOperation;
				if (activation != null)
				{
					AddExplicitPluginActivation(requestedPluginActivations, gameMode, mod, pluginManager, activation);
					continue;
				}

				SetPluginOrderIndexOperation absoluteOrder = operation as SetPluginOrderIndexOperation;
				if (absoluteOrder != null)
				{
					pluginOrderingEffects.Add(CollectionPlannedPluginEffect.AbsoluteOrder(absoluteOrder.PluginPath, absoluteOrder.NewIndex));
					continue;
				}

				SetRelativeLoadOrderOperation relativeOrder = operation as SetRelativeLoadOrderOperation;
				if (relativeOrder != null)
				{
					pluginOrderingEffects.Add(CollectionPlannedPluginEffect.RelativeOrder(relativeOrder.PluginPaths));
					continue;
				}

				if (operation is PerformBasicInstallOperation)
				{
					issues.Add(new CollectionEffectPreviewIssue(CollectionEffectPreviewIssueKind.UnboundedBasicInstall,
						operation.GetType().FullName, "Basic install must be expanded to explicit native effects before exact Collection impact planning."));
					continue;
				}

				if (operation is SetLoadOrderOperation || operation is MovePluginsInLoadOrderOperation)
				{
					issues.Add(new CollectionEffectPreviewIssue(CollectionEffectPreviewIssueKind.LegacyPluginIndexOrdering,
						operation.GetType().FullName, "Legacy plugin-index ordering is not a stable cross-member impact identity and must be translated to stable plugin paths first."));
					continue;
				}

				issues.Add(new CollectionEffectPreviewIssue(CollectionEffectPreviewIssueKind.UnsupportedNativeOperation,
					operation.GetType().FullName, "The translated native operation has no characterized C6.4 impact adapter."));
			}

			var plugins = requestedPluginActivations
				.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
				.Select(x => CollectionPlannedPluginEffect.Activation(x.Key, x.Value))
				.Concat(pluginOrderingEffects)
				.ToList();

			var result = new CollectionMemberEffectPreview(member.MemberKey, member.RecipeIdentity,
				recipeInput.InstallContext.Method, recipeInput.InstallContext.InstallRoot,
				files.Values.OrderBy(x => x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase),
				iniEdits.Values.OrderBy(x => x.Key.File, StringComparer.OrdinalIgnoreCase)
					.ThenBy(x => x.Key.Section, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key.Key, StringComparer.OrdinalIgnoreCase),
				gameValues.Values.OrderBy(x => x.Key, StringComparer.Ordinal), plugins, issues);
			CollectionPerformanceMetrics.RecordEffectPreview(performanceStarted);
			return result;
		}

		private void AddArchiveSourceFile(IDictionary<ModDeploymentTarget, CollectionPlannedFileEffect> files,
			ModDeploymentTarget target, CollectionMemberKey memberKey, ModInstallationRecipeInput recipeInput,
			IMod mod, string sourcePath, bool reusePreparationDigests,
			IDictionary<ModDeploymentTarget, CollectionPlannedFileEffect> freshlyVerifiedArchiveFiles,
			IDictionary<string, CollectionPlannedFileEffect> freshlyReadSources, CancellationToken cancellationToken)
		{
			if (freshlyVerifiedArchiveFiles != null)
			{
				CollectionPlannedFileEffect approved;
				if (!freshlyVerifiedArchiveFiles.TryGetValue(target, out approved) || !approved.HasExactContentIdentity ||
					approved.ExpectedContentHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
					throw new InvalidDataException("The freshly verified archive has no exact approved output identity for the native file target.");
				AddFile(files, target, approved.ExpectedContentHash, approved.ExpectedByteLength.Value);
				return;
			}

			// Multiple destinations can use the same archive entry. Read it once per build, never reuse this proof across builds.
			CollectionPlannedFileEffect sourceDigest;
			if (freshlyReadSources.TryGetValue(sourcePath, out sourceDigest))
			{
				AddFile(files, target, sourceDigest.ExpectedContentHash, sourceDigest.ExpectedByteLength.Value);
				return;
			}
			FileInfo before = null;
			string cacheKey = null;
			if (reusePreparationDigests && recipeInput.Validation != null && recipeInput.Validation.ExpectedContent != null)
			{
				string archivePath = !String.IsNullOrWhiteSpace(mod.ModArchivePath) ? mod.ModArchivePath : mod.Filename;
				if (!String.IsNullOrWhiteSpace(archivePath))
				{
					string fullPath = Path.GetFullPath(archivePath);
					before = new FileInfo(fullPath);
					if (!before.Exists || before.Length != recipeInput.Validation.ExpectedContent.ByteLength)
						before = null;
					else
						cacheKey = recipeInput.Validation.ExpectedContent.Sha256 + "\n" + fullPath + "\n" + sourcePath;
				}
			}

			if (cacheKey != null)
			{
				lock (_archiveEntryCacheLock)
				{
					CachedArchiveEntryDigest cached;
					if (_archiveEntryDigests.TryGetValue(cacheKey, out cached) && cached.Matches(mod, before))
					{
						AddFile(files, target, cached.Hash, cached.SourceByteLength);
						freshlyReadSources[sourcePath] = files[target];
						return;
					}
				}
			}

			CollectionContentHash hash;
			long byteLength;
			CollectionPreparationProgress.Report(LanguageManager.Format("Collections.Workflow.CheckingMemberFile",
				"Checking mod content: {0} — {1}", mod.ModName, sourcePath));
			IModFileStreamSource streamingSource = mod as IModFileStreamSource;
			if (streamingSource != null)
			{
				using (SHA256 sha256 = SHA256.Create())
				using (var destination = new ArchiveDigestStream(sha256, cancellationToken, CollectionPreparationProgress.CaptureByteReporter()))
				{
					try { streamingSource.CopyFileTo(sourcePath, destination); }
					catch (Exception) when (cancellationToken.IsCancellationRequested)
					{
						throw new OperationCanceledException(cancellationToken);
					}
					catch (Exception) when (destination.RequiresRandomAccess)
					{
						// A codec may need to seek or rewrite its output. Preserve NMM's file-backed path for that codec.
					}
					cancellationToken.ThrowIfCancellationRequested();
					if (destination.RequiresRandomAccess)
					{
						using (FileStream source = mod.GetFileStream(sourcePath))
						{
							byteLength = source.Length;
							hash = ComputeContentHash(source);
						}
					}
					else
					{
						sha256.TransformFinalBlock(new byte[0], 0, 0);
						hash = CollectionContentHash.FromSha256(BitConverter.ToString(sha256.Hash).Replace("-", String.Empty).ToLowerInvariant());
						byteLength = destination.Length;
					}
				}
			}
			else
			{
				using (FileStream source = mod.GetFileStream(sourcePath))
				{
					byteLength = source.Length;
					hash = ComputeContentHash(source);
				}
			}
			CollectionPerformanceMetrics.RecordArchiveSourceRead(memberKey, sourcePath, byteLength);

			if (cacheKey != null)
			{
				// Re-check after extraction: a changed mutable archive must never populate the preview cache.
				var after = new FileInfo(before.FullName);
				if (!after.Exists || after.Length != before.Length ||
					after.LastWriteTimeUtc.Ticks != before.LastWriteTimeUtc.Ticks)
					throw new InvalidDataException("The managed archive changed while its Collection file effects were being prepared.");
				lock (_archiveEntryCacheLock)
				{
					CacheSourceDigest(cacheKey, new CachedArchiveEntryDigest(mod, after, hash, byteLength));
				}
			}
			AddFile(files, target, hash, byteLength);
			freshlyReadSources[sourcePath] = files[target];
		}

		private static ModDeploymentTarget ResolveTarget(IGameMode gameMode, IMod mod, string destinationPath, ModInstallRoot installRoot)
		{
			return ModDeploymentTargetResolver.Resolve(gameMode, mod, destinationPath, installRoot);
		}

		private static void AddImplicitPluginActivation(IDictionary<string, bool> requestedActivations, IGameMode gameMode,
			IPluginManager pluginManager, ModDeploymentTarget target, InstallModFileOperation operation)
		{
			if (!WillBecomePhysicalWinner(operation))
				return;
			if (pluginManager == null)
			{
				if (gameMode.UsesPlugins)
					throw new ArgumentNullException(nameof(pluginManager), "Plugin-enabled Collection file-effect preview requires the live plugin manager so implicit activation can be characterized exactly.");
				return;
			}

			string deployedPath = ModDeploymentTargetResolver.GetPhysicalPath(gameMode, target);
			bool activatable = pluginManager.IsActivatiblePluginFile(deployedPath);
			// Gamebryo's native plugin factory requires the file to exist. Collection review happens before
			// mutation, so use the game mode's declared plugin directory/extensions to characterize a future
			// plugin path when the exact reviewed file is not on disk yet. Never override a rejection for an
			// already-existing file.
			if (!activatable && !File.Exists(deployedPath))
				activatable = IsPotentialActivatablePluginPath(gameMode, deployedPath);
			if (activatable && !requestedActivations.ContainsKey(deployedPath))
				requestedActivations.Add(deployedPath, true);
		}

		private static void AddExplicitPluginActivation(IDictionary<string, bool> requestedActivations, IGameMode gameMode, IMod mod,
			IPluginManager pluginManager, SetPluginActivationOperation activation)
		{
			if (pluginManager == null)
			{
				if (gameMode.UsesPlugins)
					throw new ArgumentNullException(nameof(pluginManager), "Plugin-enabled Collection activation preview requires the live plugin manager.");
				return;
			}

			string fixedPath = gameMode.GetModFormatAdjustedPath(mod.Format, activation.PluginPath, false);
			string physicalPath = Path.IsPathRooted(fixedPath)
				? fixedPath
				: Path.Combine(gameMode.GameModeEnvironmentInfo.InstallationPath, fixedPath);
			if (activation.RequireActivatablePlugin)
			{
				bool activatable = pluginManager.IsActivatiblePluginFile(physicalPath);
				if (!activatable && !File.Exists(physicalPath))
					activatable = IsPotentialActivatablePluginPath(gameMode, physicalPath);
				if (!activatable)
					return;
			}

			requestedActivations[physicalPath] = activation.Activate;
		}

		private static bool IsPotentialActivatablePluginPath(IGameMode gameMode, string physicalPath)
		{
			if (gameMode == null || !gameMode.UsesPlugins || String.IsNullOrWhiteSpace(physicalPath) ||
				gameMode.PluginExtensions == null)
				return false;

			string extension = Path.GetExtension(physicalPath);
			if (String.IsNullOrEmpty(extension) || !gameMode.PluginExtensions.Any(candidate =>
				String.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase)))
				return false;

			string pluginDirectory = gameMode.PluginDirectory;
			if (String.IsNullOrWhiteSpace(pluginDirectory))
				return true;

			string normalizedPluginDirectory = Path.GetFullPath(pluginDirectory).TrimEnd(
				Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string normalizedFileDirectory = Path.GetDirectoryName(Path.GetFullPath(physicalPath));
			return String.Equals(normalizedPluginDirectory, normalizedFileDirectory, StringComparison.OrdinalIgnoreCase);
		}

		private static bool WillBecomePhysicalWinner(InstallModFileOperation operation)
		{
			return operation.DeploymentDecision == null || operation.DeploymentDecision.Activate;
		}

		private static void AddFile(IDictionary<ModDeploymentTarget, CollectionPlannedFileEffect> files, ModDeploymentTarget target,
			CollectionContentHash contentHash, long byteLength)
		{
			files[target] = new CollectionPlannedFileEffect(target, contentHash, byteLength);
		}

		/// <summary>Hashes sequential decompressed bytes without retaining large texture or archive files on disk.</summary>
		private sealed class ArchiveDigestStream : Stream
		{
			private readonly HashAlgorithm _hash;
			private readonly CancellationToken _cancellationToken;
			private readonly Action<long, long> _progress;
			private long _length;
			private long _expectedLength;

			/// <summary>Creates a write-only hash sink; the caller owns the hash algorithm.</summary>
			internal ArchiveDigestStream(HashAlgorithm hash, CancellationToken cancellationToken, Action<long, long> progress)
			{
				_hash = hash;
				_cancellationToken = cancellationToken;
				_progress = progress;
			}

			public override bool CanRead { get { return false; } }
			public override bool CanSeek { get { return false; } }
			public override bool CanWrite { get { return true; } }
			public override long Length { get { return _length; } }
			/// <summary>Identifies codecs whose output semantics require the legacy file-backed extraction path.</summary>
			internal bool RequiresRandomAccess { get; private set; }
			public override long Position
			{
				get { return _length; }
				set
				{
					if (value == _length) return;
					RequiresRandomAccess = true;
					throw new NotSupportedException();
				}
			}

			/// <summary>Hashes each buffer once and checks cancellation during native extraction.</summary>
			public override void Write(byte[] buffer, int offset, int count)
			{
				_cancellationToken.ThrowIfCancellationRequested();
				_hash.TransformBlock(buffer, offset, count, null, 0);
				_length += count;
				_progress?.Invoke(_length, _expectedLength);
			}

			/// <summary>No buffered file payload needs flushing.</summary>
			public override void Flush() { }
			/// <summary>Reading from a hash sink is unsupported.</summary>
			public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
			/// <summary>Allows native position queries without permitting output bytes to be skipped or rewritten.</summary>
			public override long Seek(long offset, SeekOrigin origin)
			{
				long position;
				switch (origin)
				{
					case SeekOrigin.Begin: position = offset; break;
					case SeekOrigin.Current: position = _length + offset; break;
					case SeekOrigin.End: position = Math.Max(_length, _expectedLength) + offset; break;
					default: throw new ArgumentOutOfRangeException(nameof(origin));
				}
				if (position != _length)
				{
					RequiresRandomAccess = true;
					throw new NotSupportedException();
				}
				return _length;
			}
			/// <summary>Accepts SevenZip's output-size declaration without preallocating a temporary file or changing hashed bytes.</summary>
			public override void SetLength(long value)
			{
				if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
				if (value < _length)
				{
					RequiresRandomAccess = true;
					throw new NotSupportedException();
				}
				_expectedLength = value;
			}
		}

		private static CollectionContentHash ComputeContentHash(Stream stream)
		{
			if (stream == null) throw new ArgumentNullException(nameof(stream));
			long originalPosition = stream.CanSeek ? stream.Position : 0;
			try
			{
				if (stream.CanSeek) stream.Position = 0;
				using (SHA256 sha256 = SHA256.Create())
					return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", String.Empty).ToLowerInvariant());
			}
			finally
			{
				if (stream.CanSeek) stream.Position = originalPosition;
			}
		}

		private static CollectionContentHash ComputeContentHash(byte[] bytes)
		{
			using (SHA256 sha256 = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(bytes ?? new byte[0])).Replace("-", String.Empty).ToLowerInvariant());
		}
	}
}
