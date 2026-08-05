namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;

    using Nexus.Client.Games;
	using Nexus.Client.Util;

    public sealed class FileManagerQueryService
    {
        private readonly IFileManagerManualSourceStore _manualSourceStore;

        public FileManagerQueryService()
            : this(null)
        {
        }

        public FileManagerQueryService(IFileManagerManualSourceStore manualSourceStore)
        {
            _manualSourceStore = manualSourceStore;
        }

        public FileManagerScanResult Scan(IGameMode gameMode, IVirtualModActivator virtualModActivator, CancellationToken cancellationToken)
        {
            if (gameMode == null) throw new ArgumentNullException("gameMode");
            if (virtualModActivator == null) throw new ArgumentNullException("virtualModActivator");

            Stopwatch totalWatch = Stopwatch.StartNew();
            FileManagerScanDiagnostics diagnostics = new FileManagerScanDiagnostics();
            string deploymentRoot = GetDeploymentRoot(gameMode);
            if (string.IsNullOrWhiteSpace(deploymentRoot) || !Directory.Exists(deploymentRoot))
                throw new DirectoryNotFoundException("The deployment root does not exist or is inaccessible: " + (deploymentRoot ?? String.Empty));

            Stopwatch stageWatch = Stopwatch.StartNew();
            Dictionary<string, FileManagerPathOwnership> ownershipByPath = BuildVirtualLinkLookup(virtualModActivator, gameMode, deploymentRoot, diagnostics);
            diagnostics.VirtualLinkIndexMilliseconds = stageWatch.ElapsedMilliseconds;

            stageWatch.Restart();
            HashSet<string> baseFiles = BuildBaseFileSet(gameMode.BaseGameFiles);
            diagnostics.BaseFileIndexMilliseconds = stageWatch.ElapsedMilliseconds;

            stageWatch.Restart();
            IDictionary<string, FileManagerSource> manualSources = LoadManualSources(gameMode.ModeId);
            diagnostics.ManualSourceLoadMilliseconds = stageWatch.ElapsedMilliseconds;

            int initialRowCapacity = EstimateInitialRowCapacity(ownershipByPath.Count, baseFiles.Count, manualSources.Count);
            List<FileManagerRow> rows = new List<FileManagerRow>(initialRowCapacity);
            Dictionary<string, FileManagerRow> rowsByPath = new Dictionary<string, FileManagerRow>(initialRowCapacity, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> fileTypeCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            FileManagerSourceCounts counts = new FileManagerSourceCounts();
            FileManagerEnumerationStats enumerationStats = new FileManagerEnumerationStats();
            long rowConstructionTicks = 0;
            long classificationTicks = 0;
            long indexConstructionTicks = 0;
            int publishedReparseFileCount = 0;
            long enumerationStart = Stopwatch.GetTimestamp();

            foreach (FileManagerFileEntry fileEntry in FileManagerNativeFileEnumerator.EnumerateFiles(deploymentRoot, cancellationToken, enumerationStats))
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileManagerRow row = null;
                string normalizedPath = String.Empty;
                bool isReparsePoint = false;
                long rowConstructionStart = Stopwatch.GetTimestamp();
                try
                {
                    normalizedPath = NormalizePath(fileEntry.RelativePath);
                    if (String.IsNullOrWhiteSpace(normalizedPath))
                    {
                        enumerationStats.SkippedFiles++;
                        continue;
                    }

                    isReparsePoint = (fileEntry.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                    row = new FileManagerRow
                    {
                        FullPath = fileEntry.FullPath,
                        FileName = fileEntry.FileName,
                        FileType = GetFileType(fileEntry.FileName, fileTypeCache),
                        RawSize = fileEntry.Length,
                        RelativePath = normalizedPath
                    };
                    if (isReparsePoint)
                        row.SetLinkTypeState(FileManagerLinkTypeState.SymbolicLink, false);
                }
                catch
                {
                    enumerationStats.SkippedFiles++;
                    continue;
                }
                finally
                {
                    rowConstructionTicks += Stopwatch.GetTimestamp() - rowConstructionStart;
                }

                long classificationStart = Stopwatch.GetTimestamp();
                FileManagerPathOwnership ownership;
                ownershipByPath.TryGetValue(normalizedPath, out ownership);
                ApplySourceClassification(row, ownership, baseFiles, manualSources);
                classificationTicks += Stopwatch.GetTimestamp() - classificationStart;

                long indexConstructionStart = Stopwatch.GetTimestamp();
                rows.Add(row);
                if (!rowsByPath.ContainsKey(normalizedPath))
                    rowsByPath.Add(normalizedPath, row);
                counts.Add(row.Source);
                if (isReparsePoint)
                    publishedReparseFileCount++;
                indexConstructionTicks += Stopwatch.GetTimestamp() - indexConstructionStart;
            }

            long enumerationTotalTicks = Stopwatch.GetTimestamp() - enumerationStart;
            long enumerationTicks = Math.Max(0, enumerationTotalTicks - rowConstructionTicks - classificationTicks - indexConstructionTicks);
            diagnostics.FileEnumerationMilliseconds = TicksToMilliseconds(enumerationTicks);
            diagnostics.FileMetadataMilliseconds = 0;
            diagnostics.RowConstructionMilliseconds = TicksToMilliseconds(rowConstructionTicks);
            diagnostics.ClassificationMilliseconds = TicksToMilliseconds(classificationTicks);
            diagnostics.IndexConstructionMilliseconds = TicksToMilliseconds(indexConstructionTicks);
            diagnostics.EnumeratedFileCount = enumerationStats.EnumeratedFiles;
            diagnostics.PublishedFileCount = rows.Count;
            diagnostics.NativeMetadataFileCount = enumerationStats.EnumeratedFiles;
            diagnostics.SkippedFileCount = enumerationStats.SkippedFiles;
            diagnostics.SkippedDirectoryCount = enumerationStats.SkippedDirectories;
            diagnostics.SkippedReparseDirectoryCount = enumerationStats.SkippedReparseDirectories;
            diagnostics.ReparseFileCount = enumerationStats.ReparseFiles;
            diagnostics.SymbolicLinkCount = publishedReparseFileCount;
            diagnostics.PendingLinkTypeCount = Math.Max(0, rows.Count - publishedReparseFileCount);
            totalWatch.Stop();
            diagnostics.TotalMilliseconds = totalWatch.ElapsedMilliseconds;

            Trace.TraceInformation("File Manager scan completed. {0}", diagnostics);
            return new FileManagerScanResult(deploymentRoot, rows, rowsByPath, counts, DateTime.Now, diagnostics);
        }

        public FileManagerSourceCounts ReclassifyRows(IList<FileManagerRow> rows, IGameMode gameMode, IVirtualModActivator virtualModActivator)
        {
            if (rows == null) throw new ArgumentNullException("rows");
            if (gameMode == null) throw new ArgumentNullException("gameMode");
            if (virtualModActivator == null) throw new ArgumentNullException("virtualModActivator");

            string deploymentRoot = GetDeploymentRoot(gameMode);
            Dictionary<string, FileManagerPathOwnership> ownershipByPath = BuildVirtualLinkLookup(virtualModActivator, gameMode, deploymentRoot);
            HashSet<string> baseFiles = BuildBaseFileSet(gameMode.BaseGameFiles);
            IDictionary<string, FileManagerSource> manualSources = LoadManualSources(gameMode.ModeId);
            FileManagerSourceCounts counts = new FileManagerSourceCounts();

            foreach (FileManagerRow row in rows)
            {
                if (row == null)
                    continue;

                FileManagerPathOwnership ownership;
                ownershipByPath.TryGetValue(row.RelativePath, out ownership);
                ApplySourceClassification(row, ownership, baseFiles, manualSources);
                counts.Add(row.Source);
            }

            return counts;
        }
        public FileManagerSourceCounts SynchronizeRowsAfterActivation(IList<FileManagerRow> rows, IDictionary<string, FileManagerRow> rowsByNormalizedPath, IGameMode gameMode, IVirtualModActivator virtualModActivator)
        {
            if (rows == null) throw new ArgumentNullException("rows");
            if (rowsByNormalizedPath == null) throw new ArgumentNullException("rowsByNormalizedPath");
            if (gameMode == null) throw new ArgumentNullException("gameMode");
            if (virtualModActivator == null) throw new ArgumentNullException("virtualModActivator");

            string deploymentRoot = GetDeploymentRoot(gameMode);
            if (String.IsNullOrWhiteSpace(deploymentRoot) || !Directory.Exists(deploymentRoot))
                return ReclassifyRows(rows, gameMode, virtualModActivator);

            Dictionary<string, FileManagerPathOwnership> ownershipByPath = BuildVirtualLinkLookup(virtualModActivator, gameMode, deploymentRoot);
            HashSet<string> baseFiles = BuildBaseFileSet(gameMode.BaseGameFiles);
            IDictionary<string, FileManagerSource> manualSources = LoadManualSources(gameMode.ModeId);
            string rootPrefix = GetNormalizedRootPrefix(deploymentRoot);

            rowsByNormalizedPath.Clear();
            for (int index = rows.Count - 1; index >= 0; index--)
            {
                FileManagerRow row = rows[index];
                if (row == null || String.IsNullOrWhiteSpace(row.RelativePath))
                {
                    rows.RemoveAt(index);
                    continue;
                }

                FileManagerPathOwnership ownership;
                bool hasActiveOwnership = ownershipByPath.TryGetValue(row.RelativePath, out ownership) && ownership != null && ownership.HasActiveOwner;

                if (hasActiveOwnership)
                {
                    ApplyNmmOwnership(row, ownership);
                }
                else if (row.Source == FileManagerSource.InstalledByNmm)
                {
                    if (String.IsNullOrWhiteSpace(row.FullPath) || !File.Exists(row.FullPath))
                    {
                        rows.RemoveAt(index);
                        continue;
                    }

                    ApplySourceClassification(row, (FileManagerPathOwnership)null, baseFiles, manualSources);
                }

                if (!rowsByNormalizedPath.ContainsKey(row.RelativePath))
                    rowsByNormalizedPath.Add(row.RelativePath, row);
            }

            foreach (KeyValuePair<string, FileManagerPathOwnership> pair in ownershipByPath)
            {
                FileManagerPathOwnership ownership = pair.Value;
                if (ownership == null || !ownership.HasActiveOwner || rowsByNormalizedPath.ContainsKey(pair.Key))
                    continue;

                string fullPath = GetSafeDeploymentFilePath(deploymentRoot, rootPrefix, pair.Key);
                if (String.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                    continue;

                FileManagerRow row = CreateRow(fullPath, rootPrefix);
                if (row == null || rowsByNormalizedPath.ContainsKey(row.RelativePath))
                    continue;

                FileManagerPathOwnership canonicalOwnership;
                if (!ownershipByPath.TryGetValue(row.RelativePath, out canonicalOwnership))
                    canonicalOwnership = ownership;

                ApplySourceClassification(row, canonicalOwnership, baseFiles, manualSources);
                rows.Add(row);
                rowsByNormalizedPath.Add(row.RelativePath, row);
            }

            FileManagerSourceCounts counts = new FileManagerSourceCounts();
            foreach (FileManagerRow row in rows)
                if (row != null)
                    counts.Add(row.Source);

            return counts;
        }

        public void RefreshRowOwnership(FileManagerRow row, IGameMode gameMode, IVirtualModActivator virtualModActivator)
        {
            if (row == null || virtualModActivator == null)
                return;

            FileManagerPathOwnership ownership = BuildOwnershipForPath(virtualModActivator, gameMode, row.RelativePath);
            if (ownership != null && ownership.HasActiveOwner)
                ApplyNmmOwnership(row, ownership);
        }

        public void ApplySelectedOwner(FileManagerRow row, string selectedOwnerKey)
        {
            if (row == null)
                return;

            FileManagerOwnerCandidate candidate = FindOwnerCandidate(row.OwnerCandidates, selectedOwnerKey);
            row.SourceEditable = false;
            row.Source = FileManagerSource.InstalledByNmm;
            row.OwnerKey = selectedOwnerKey ?? String.Empty;
            row.OwnerName = candidate == null ? String.Empty : candidate.ModName;
        }

        public void ApplyManualSource(FileManagerRow row, FileManagerSource source)
        {
            if (row == null) throw new ArgumentNullException("row");
            if (!row.SourceEditable)
                throw new InvalidOperationException("This file source was identified automatically and cannot be changed manually.");
            if (!FileManagerSourceDisplay.IsManualSource(source))
                throw new InvalidOperationException("The selected source cannot be assigned manually.");

            row.OwnerCandidates = FileManagerRow.EmptyOwnerCandidates;
            row.OwnerKey = String.Empty;
            row.OwnerName = String.Empty;
            row.Source = source;
        }

        public IDictionary<string, FileManagerSource> LoadManualSources(string gameModeId)
        {
            if (_manualSourceStore == null)
                return new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase);

            IDictionary<string, FileManagerSource> loadedSources = _manualSourceStore.Load(gameModeId);
            return loadedSources == null
                ? new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, FileManagerSource>(loadedSources, StringComparer.OrdinalIgnoreCase);
        }

        public void SaveManualSource(string gameModeId, FileManagerRow row, FileManagerSource source)
        {
            if (_manualSourceStore == null)
                return;
            if (row == null) throw new ArgumentNullException("row");

            _manualSourceStore.SetSource(gameModeId, row.RelativePath, source);
        }

        public void ChangeManualSource(string gameModeId, FileManagerRow row, FileManagerSource source, FileManagerSource previousSource)
        {
            if (row == null) throw new ArgumentNullException("row");

            try
            {
                SaveManualSource(gameModeId, row, source);
                ApplyManualSource(row, source);
            }
            catch
            {
                row.Source = previousSource;
                throw;
            }
        }

        public static void ApplySourceClassification(FileManagerRow row, IList<IVirtualModLink> pathLinks, ISet<string> baseFiles, IDictionary<string, FileManagerSource> manualSources)
        {
            ApplySourceClassification(row, BuildOwnership(pathLinks), baseFiles, manualSources);
        }

        internal static void ApplySourceClassification(FileManagerRow row, FileManagerPathOwnership ownership, ISet<string> baseFiles, IDictionary<string, FileManagerSource> manualSources)
        {
            if (row == null) throw new ArgumentNullException("row");

            if (ownership != null && ownership.HasActiveOwner)
            {
                ApplyNmmOwnership(row, ownership);
                return;
            }

            if (baseFiles != null && baseFiles.Contains(row.RelativePath))
            {
                row.SourceEditable = false;
                row.OwnerCandidates = FileManagerRow.EmptyOwnerCandidates;
                row.OwnerKey = String.Empty;
                row.OwnerName = String.Empty;
                row.Source = FileManagerSource.BaseGame;
                return;
            }

            FileManagerSource manualSource;
            row.SourceEditable = true;
            row.OwnerCandidates = FileManagerRow.EmptyOwnerCandidates;
            row.OwnerKey = String.Empty;
            row.OwnerName = String.Empty;
            if (manualSources != null && manualSources.TryGetValue(row.RelativePath, out manualSource) && FileManagerSourceDisplay.IsManualSource(manualSource) && manualSource != FileManagerSource.Untracked)
                row.Source = manualSource;
            else
                row.Source = FileManagerSource.Untracked;
        }

        public static string GetDeploymentRoot(IGameMode gameMode)
        {
            if (gameMode == null)
                return String.Empty;

            return gameMode.UsesPlugins ? gameMode.PluginDirectory : gameMode.InstallationPath;
        }

        public static string NormalizePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return String.Empty;

            int startIndex = 0;
            while (startIndex < path.Length && (path[startIndex] == Path.DirectorySeparatorChar || path[startIndex] == Path.AltDirectorySeparatorChar))
                startIndex++;

            bool replaceAlternateSeparators = path.IndexOf(Path.AltDirectorySeparatorChar, startIndex) >= 0 && Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar;
            if (startIndex == 0 && !replaceAlternateSeparators)
                return path;

            string normalizedPath = startIndex == 0 ? path : path.Substring(startIndex);
            return replaceAlternateSeparators
                ? normalizedPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                : normalizedPath;
        }

        public static string CreateOwnerKey(IVirtualModInfo modInfo)
        {
            if (modInfo == null)
                return String.Empty;

            return (modInfo.ModFileName ?? String.Empty).ToLowerInvariant() + "|" + (modInfo.DownloadId ?? String.Empty).ToLowerInvariant();
        }

        /// <summary>
        /// Builds the File Manager ownership lookup from the activator's indexed virtual-link snapshot.
        /// </summary>
        /// <param name="virtualModActivator">The activator supplying virtual-link ownership information.</param>
        /// <param name="gameMode">The current game mode.</param>
        /// <param name="deploymentRoot">The root represented by the File Manager.</param>
        /// <param name="diagnostics">The optional scan diagnostics receiving ownership timings and counts.</param>
        /// <returns>A case-insensitive ownership lookup keyed by normalized relative path.</returns>
        private static Dictionary<string, FileManagerPathOwnership> BuildVirtualLinkLookup(IVirtualModActivator virtualModActivator, IGameMode gameMode, string deploymentRoot, FileManagerScanDiagnostics diagnostics = null)
        {
            Stopwatch diagnosticWatch = diagnostics == null ? null : Stopwatch.StartNew();
            List<string> sourceRoots = GetVirtualSourceRoots(virtualModActivator);
            Dictionary<string, List<IVirtualModLink>> linksByPath = new Dictionary<string, List<IVirtualModLink>>(StringComparer.OrdinalIgnoreCase);
            VirtualModActivator concreteActivator = virtualModActivator as VirtualModActivator;
            if (concreteActivator != null)
            {
                VirtualLinkIndexSnapshot snapshot = concreteActivator.GetVirtualLinkIndexSnapshot();
                if (diagnostics != null)
                {
                    diagnostics.VirtualPathEntryCount = snapshot.VirtualPathEntries == null ? 0 : snapshot.VirtualPathEntries.Count;
                    diagnostics.DeploymentPathEntryCount = snapshot.DeploymentPathEntries == null ? 0 : snapshot.DeploymentPathEntries.Count;
                    diagnostics.VirtualLinkCount = CountSnapshotLinks(snapshot.VirtualPathEntries);
                    diagnostics.VirtualLinkSnapshotMilliseconds = diagnosticWatch.ElapsedMilliseconds;
                    diagnosticWatch.Restart();
                }

                AddVirtualPathSnapshotEntries(linksByPath, snapshot.VirtualPathEntries);
                if (diagnostics != null)
                {
                    diagnostics.VirtualPathMappingMilliseconds = diagnosticWatch.ElapsedMilliseconds;
                    diagnosticWatch.Restart();
                }

                int mappedDeploymentPathCount = AddDeploymentPathSnapshotEntries(linksByPath, snapshot.DeploymentPathEntries, deploymentRoot);
                if (diagnostics != null)
                {
                    diagnostics.MappedDeploymentPathEntryCount = mappedDeploymentPathCount;
                    diagnostics.DeploymentPathMappingMilliseconds = diagnosticWatch.ElapsedMilliseconds;
                    diagnosticWatch.Restart();
                }
            }
            else
            {
                if (diagnostics != null)
                    diagnosticWatch.Restart();

                int virtualLinkCount = AddUnindexedVirtualLinks(linksByPath, virtualModActivator, gameMode, deploymentRoot);
                if (diagnostics != null)
                {
                    diagnostics.VirtualLinkCount = virtualLinkCount;
                    diagnostics.VirtualPathMappingMilliseconds = diagnosticWatch.ElapsedMilliseconds;
                    diagnosticWatch.Restart();
                }
            }

            Dictionary<string, FileManagerPathOwnership> ownershipByPath = new Dictionary<string, FileManagerPathOwnership>(linksByPath.Count, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<IVirtualModLink>> pair in linksByPath)
            {
                FileManagerPathOwnership ownership = BuildOwnership(pair.Value, sourceRoots);
                ownershipByPath.Add(pair.Key, ownership);
                if (diagnostics == null)
                    continue;

                diagnostics.OwnershipLinkReferenceCount += pair.Value == null ? 0 : pair.Value.Count;
                if (ownership == null)
                    continue;

                if (ownership.HasActiveOwner)
                    diagnostics.ActiveOwnershipPathCount++;
                if (ownership.OwnerCount > 1)
                    diagnostics.ConflictingOwnerPathCount++;
                else
                    diagnostics.SingleOwnerPathCount++;
            }

            if (diagnostics != null)
            {
                diagnostics.OwnershipGroupingMilliseconds = diagnosticWatch.ElapsedMilliseconds;
                diagnostics.OwnershipPathCount = ownershipByPath.Count;
            }

            return ownershipByPath;
        }

        /// <summary>
        /// Counts the virtual-link references stored in the virtual-path side of an index snapshot.
        /// </summary>
        /// <param name="entries">The virtual-path snapshot entries.</param>
        /// <returns>The number of indexed virtual links.</returns>
        private static int CountSnapshotLinks(IList<VirtualLinkIndexSnapshotEntry> entries)
        {
            if (entries == null)
                return 0;

            int count = 0;
            foreach (VirtualLinkIndexSnapshotEntry entry in entries)
                if (entry != null && entry.Links != null)
                    count += entry.Links.Count;

            return count;
        }

        /// <summary>
        /// Adds raw virtual-path entries from an immutable index snapshot.
        /// </summary>
        /// <param name="linksByPath">The destination ownership grouping.</param>
        /// <param name="entries">The virtual-path snapshot entries.</param>
        private static void AddVirtualPathSnapshotEntries(Dictionary<string, List<IVirtualModLink>> linksByPath, IList<VirtualLinkIndexSnapshotEntry> entries)
        {
            if (entries == null)
                return;

            foreach (VirtualLinkIndexSnapshotEntry entry in entries)
            {
                if (entry == null)
                    continue;

                string normalizedPath = NormalizePath(entry.Key);
                AddLinksToOwnershipLookup(linksByPath, normalizedPath, entry.Links);
            }
        }

        /// <summary>
        /// Adds deployment-path entries that fall beneath the File Manager deployment root.
        /// </summary>
        /// <param name="linksByPath">The destination ownership grouping.</param>
        /// <param name="entries">The deployment-path snapshot entries.</param>
        /// <param name="deploymentRoot">The root represented by the File Manager.</param>
        /// <returns>The number of deployment-path entries mapped beneath the requested root.</returns>
        private static int AddDeploymentPathSnapshotEntries(Dictionary<string, List<IVirtualModLink>> linksByPath, IList<VirtualLinkIndexSnapshotEntry> entries, string deploymentRoot)
        {
            if (entries == null || String.IsNullOrWhiteSpace(deploymentRoot))
                return 0;

            string rootPrefix;
            try
            {
                rootPrefix = GetNormalizedRootPrefix(deploymentRoot);
            }
            catch
            {
                return 0;
            }

            int mappedEntryCount = 0;
            foreach (VirtualLinkIndexSnapshotEntry entry in entries)
            {
                if (entry == null || entry.Links == null || entry.Links.Count == 0 || String.IsNullOrWhiteSpace(entry.Key) || !entry.Key.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string normalizedPath = NormalizePath(entry.Key.Substring(rootPrefix.Length));
                if (String.IsNullOrWhiteSpace(normalizedPath))
                    continue;

                AddLinksToOwnershipLookup(linksByPath, normalizedPath, entry.Links);
                mappedEntryCount++;
            }

            return mappedEntryCount;
        }

        /// <summary>
        /// Builds ownership groups for activator implementations that do not expose the optimized index snapshot.
        /// </summary>
        /// <param name="linksByPath">The destination ownership grouping.</param>
        /// <param name="virtualModActivator">The virtual mod activator.</param>
        /// <param name="gameMode">The current game mode.</param>
        /// <param name="deploymentRoot">The root represented by the File Manager.</param>
        /// <returns>The number of valid virtual links inspected.</returns>
        private static int AddUnindexedVirtualLinks(Dictionary<string, List<IVirtualModLink>> linksByPath, IVirtualModActivator virtualModActivator, IGameMode gameMode, string deploymentRoot)
        {
            IEnumerable<IVirtualModLink> links = virtualModActivator == null ? null : virtualModActivator.VirtualLinks;
            if (links == null)
                return 0;

            int virtualLinkCount = 0;
            foreach (IVirtualModLink link in links)
            {
                if (link == null || String.IsNullOrWhiteSpace(link.VirtualModPath))
                    continue;

                virtualLinkCount++;
                foreach (string key in GetFileManagerOwnershipKeys(link, gameMode, deploymentRoot, String.Empty))
                    AddLinkToOwnershipLookup(linksByPath, key, link);
            }

            return virtualLinkCount;
        }

        private static FileManagerPathOwnership BuildOwnershipForPath(IVirtualModActivator virtualModActivator, IGameMode gameMode, string normalizedPath)
        {
            if (virtualModActivator == null || String.IsNullOrWhiteSpace(normalizedPath))
                return null;

            Dictionary<string, FileManagerPathOwnership> ownershipByPath = BuildVirtualLinkLookup(virtualModActivator, gameMode, GetDeploymentRoot(gameMode));
            FileManagerPathOwnership ownership;
            return ownershipByPath.TryGetValue(normalizedPath, out ownership) ? ownership : null;
        }

        private static IEnumerable<string> GetFileManagerOwnershipKeys(IVirtualModLink link, IGameMode gameMode, string deploymentRoot, string deployedPath)
        {
            if (link == null || String.IsNullOrWhiteSpace(link.VirtualModPath))
                yield break;

            string rawKey = NormalizePath(link.VirtualModPath);
            if (!String.IsNullOrWhiteSpace(rawKey))
                yield return rawKey;

            string deployedRelativePath = GetDeploymentRelativePath(link, gameMode, deploymentRoot, deployedPath);
            string deployedKey = NormalizePath(deployedRelativePath);
            if (!String.IsNullOrWhiteSpace(deployedKey) && !String.Equals(rawKey, deployedKey, StringComparison.OrdinalIgnoreCase))
                yield return deployedKey;
        }

        private static string GetDeploymentRelativePath(IVirtualModLink link, IGameMode gameMode, string deploymentRoot, string deployedPath)
        {
            if (link == null || gameMode == null || String.IsNullOrWhiteSpace(deploymentRoot) || String.IsNullOrWhiteSpace(link.VirtualModPath))
                return String.Empty;

            try
            {
                if (String.IsNullOrWhiteSpace(deployedPath))
                {
                    string adjustedPath = link.VirtualModPath;
                    if (link.InstallRoot != ModInstallRoot.GameRoot)
                        adjustedPath = gameMode.GetModFormatAdjustedPath(null, link.VirtualModPath, true);

                    string installRoot = link.InstallRoot == ModInstallRoot.GameRoot ? gameMode.InstallationPath : deploymentRoot;
                    if (String.IsNullOrWhiteSpace(installRoot) || String.IsNullOrWhiteSpace(adjustedPath))
                        return String.Empty;

                    deployedPath = Path.GetFullPath(Path.Combine(installRoot, adjustedPath));
                }

                string deploymentRootPrefix = GetNormalizedRootPrefix(deploymentRoot);
                if (deployedPath.StartsWith(deploymentRootPrefix, StringComparison.OrdinalIgnoreCase))
                    return deployedPath.Substring(deploymentRootPrefix.Length);
            }
            catch
            {
            }

            return String.Empty;
        }

        /// <summary>
        /// Adds all links from one snapshot bucket to a normalized ownership path without duplicates.
        /// </summary>
        /// <param name="linksByPath">The destination ownership grouping.</param>
        /// <param name="key">The normalized relative path.</param>
        /// <param name="links">The links to add.</param>
        private static void AddLinksToOwnershipLookup(Dictionary<string, List<IVirtualModLink>> linksByPath, string key, IList<IVirtualModLink> links)
        {
            if (links == null)
                return;

            foreach (IVirtualModLink link in links)
                AddLinkToOwnershipLookup(linksByPath, key, link);
        }

        private static void AddLinkToOwnershipLookup(Dictionary<string, List<IVirtualModLink>> linksByPath, string key, IVirtualModLink link)
        {
            if (String.IsNullOrWhiteSpace(key) || link == null)
                return;

            List<IVirtualModLink> fileLinks;
            if (!linksByPath.TryGetValue(key, out fileLinks))
            {
                fileLinks = new List<IVirtualModLink>(1);
                linksByPath.Add(key, fileLinks);
            }

            if (!fileLinks.Contains(link))
                fileLinks.Add(link);
        }

        private static FileManagerPathOwnership BuildOwnership(IList<IVirtualModLink> pathLinks)
        {
            return BuildOwnership(pathLinks, null);
        }

        private static FileManagerPathOwnership BuildOwnership(IList<IVirtualModLink> pathLinks, IList<string> sourceRoots)
        {
            if (pathLinks == null || pathLinks.Count == 0)
                return null;

            if (pathLinks.Count == 1 && pathLinks[0] != null)
            {
                IVirtualModLink soleOwner = pathLinks[0];
                return new FileManagerPathOwnership(
                    soleOwner.Active,
                    CreateOwnerKey(soleOwner.ModInfo),
                    soleOwner.ModInfo == null ? String.Empty : soleOwner.ModInfo.ModName,
                    1,
                    FileManagerRow.EmptyOwnerCandidates);
            }

            List<IVirtualModLink> orderedLinks = new List<IVirtualModLink>(pathLinks.Count);
            foreach (IVirtualModLink link in pathLinks)
                if (link != null)
                    orderedLinks.Add(link);

            if (orderedLinks.Count == 0)
                return null;

            if (orderedLinks.Count == 1)
            {
                IVirtualModLink soleOwner = orderedLinks[0];
                return new FileManagerPathOwnership(
                    soleOwner.Active,
                    CreateOwnerKey(soleOwner.ModInfo),
                    soleOwner.ModInfo == null ? String.Empty : soleOwner.ModInfo.ModName,
                    1,
                    FileManagerRow.EmptyOwnerCandidates);
            }

            orderedLinks.Sort(CompareVirtualLinksForOwnerDisplay);

            IVirtualModLink activeOwner = null;
            foreach (IVirtualModLink link in orderedLinks)
            {
                if (link.Active)
                {
                    activeOwner = link;
                    break;
                }
            }

            if (activeOwner == null)
                activeOwner = orderedLinks[0];

            HashSet<string> seenOwnerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<IVirtualModLink> distinctOwners = new List<IVirtualModLink>(orderedLinks.Count);
            foreach (IVirtualModLink link in orderedLinks)
            {
                string ownerKey = CreateOwnerKey(link.ModInfo);
                if (!seenOwnerKeys.Add(ownerKey))
                    continue;

                distinctOwners.Add(link);
            }

            List<FileManagerOwnerCandidate> candidates = FileManagerRow.EmptyOwnerCandidates;
            if (activeOwner.Active && distinctOwners.Count > 1)
            {
                candidates = new List<FileManagerOwnerCandidate>(distinctOwners.Count);
                foreach (IVirtualModLink link in distinctOwners)
                {
                    candidates.Add(new FileManagerOwnerCandidate(
                        CreateOwnerKey(link.ModInfo),
                        link.ModInfo == null ? String.Empty : link.ModInfo.ModName,
                        link.Priority,
                        link.RealModPath,
                        sourceRoots));
                }
            }

            return new FileManagerPathOwnership(
                activeOwner.Active,
                CreateOwnerKey(activeOwner.ModInfo),
                activeOwner.ModInfo == null ? String.Empty : activeOwner.ModInfo.ModName,
                distinctOwners.Count,
                candidates);
        }

        private static List<string> GetVirtualSourceRoots(IVirtualModActivator virtualModActivator)
        {
            List<string> sourceRoots = new List<string>();
            if (virtualModActivator == null)
                return sourceRoots;

            if (!String.IsNullOrWhiteSpace(virtualModActivator.VirtualPath))
                sourceRoots.Add(virtualModActivator.VirtualPath);

            if (virtualModActivator.MultiHDMode)
            {
                try
                {
                    if (!String.IsNullOrWhiteSpace(virtualModActivator.HDLinkFolder) && sourceRoots.FindIndex(x => String.Equals(x, virtualModActivator.HDLinkFolder, StringComparison.OrdinalIgnoreCase)) < 0)
                        sourceRoots.Add(virtualModActivator.HDLinkFolder);
                }
                catch
                {
                }
            }

            return sourceRoots;
        }

        private static string GetSafeDeploymentFilePath(string deploymentRoot, string rootPrefix, string normalizedRelativePath)
        {
            if (String.IsNullOrWhiteSpace(deploymentRoot) || String.IsNullOrWhiteSpace(rootPrefix) || String.IsNullOrWhiteSpace(normalizedRelativePath))
                return String.Empty;

            try
            {
                string fullPath = Path.GetFullPath(Path.Combine(deploymentRoot, normalizedRelativePath));
                return fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ? fullPath : String.Empty;
            }
            catch
            {
                return String.Empty;
            }
        }

        private static FileManagerRow CreateRow(string filePath, string rootPrefix)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                    return null;

                string relativePath = GetRelativePath(rootPrefix, filePath);
                string normalizedPath = NormalizePath(relativePath);
                if (String.IsNullOrWhiteSpace(normalizedPath))
                    return null;

                string fileName = Path.GetFileName(filePath);
                FileManagerRow row = new FileManagerRow
                {
                    FullPath = filePath,
                    FileName = fileName,
                    FileType = GetFileType(fileName, null),
                    RawSize = fileInfo.Length,
                    RelativePath = normalizedPath
                };
                row.SetLinkTypeState(FileManagerRow.GetLinkTypeState(FileLinkHelper.GetFileLinkType(filePath)), false);
                return row;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns a normalized file extension and reuses one shared string per extension during a bulk scan.
        /// </summary>
        /// <param name="filePath">The file name or path whose extension should be read.</param>
        /// <param name="fileTypeCache">The optional case-insensitive cache used by the current scan.</param>
        /// <returns>The lowercase extension without its leading period, or an empty string when no extension exists.</returns>
        private static string GetFileType(string filePath, IDictionary<string, string> fileTypeCache)
        {
            if (String.IsNullOrEmpty(filePath))
                return String.Empty;

            int extensionIndex = filePath.LastIndexOf('.');
            if (extensionIndex < 0 || extensionIndex == filePath.Length - 1)
                return String.Empty;

            string extension = filePath.Substring(extensionIndex + 1);
            string cachedExtension;
            if (fileTypeCache != null && fileTypeCache.TryGetValue(extension, out cachedExtension))
                return cachedExtension;

            string normalizedExtension = extension.ToLowerInvariant();
            if (fileTypeCache != null)
                fileTypeCache[extension] = normalizedExtension;

            return normalizedExtension;
        }

        /// <summary>
        /// Estimates a practical initial capacity from indexes already loaded before file enumeration begins.
        /// </summary>
        /// <param name="ownershipCount">The number of known deployed ownership paths.</param>
        /// <param name="baseFileCount">The number of known base-game paths.</param>
        /// <param name="manualSourceCount">The number of manually classified paths.</param>
        /// <returns>An initial row and dictionary capacity that avoids very small repeated growth.</returns>
        private static int EstimateInitialRowCapacity(int ownershipCount, int baseFileCount, int manualSourceCount)
        {
            int knownPathCount = Math.Max(ownershipCount, Math.Max(baseFileCount, manualSourceCount));
            return Math.Max(4096, knownPathCount);
        }
        private static int CompareVirtualLinksForOwnerDisplay(IVirtualModLink left, IVirtualModLink right)
        {
            int priorityComparison = left.Priority.CompareTo(right.Priority);
            if (priorityComparison != 0)
                return priorityComparison;

            string leftName = left.ModInfo == null ? String.Empty : left.ModInfo.ModName;
            string rightName = right.ModInfo == null ? String.Empty : right.ModInfo.ModName;
            return StringComparer.OrdinalIgnoreCase.Compare(leftName, rightName);
        }

        private static HashSet<string> BuildBaseFileSet(string baseGameFiles)
        {
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(baseGameFiles))
                return files;

            string[] lines = baseGameFiles.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                string trimmed = (line ?? String.Empty).Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                    continue;

                files.Add(NormalizePath(trimmed));
            }

            return files;
        }

        private static void ApplyNmmOwnership(FileManagerRow row, FileManagerPathOwnership ownership)
        {
            row.SourceEditable = false;
            row.Source = FileManagerSource.InstalledByNmm;
            row.OwnerCandidates = ownership.OwnerCandidates;
            row.SetOwnerCount(ownership.OwnerCount);
            row.OwnerKey = ownership.ActiveOwnerKey;
            row.OwnerName = ownership.ActiveOwnerName;
        }

        private static FileManagerOwnerCandidate FindOwnerCandidate(List<FileManagerOwnerCandidate> candidates, string ownerKey)
        {
            if (candidates == null)
                return null;

            foreach (FileManagerOwnerCandidate candidate in candidates)
                if (String.Equals(candidate.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
                    return candidate;

            return null;
        }

        private static string GetNormalizedRootPrefix(string root)
        {
            return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        private static string GetRelativePath(string fullRootPrefix, string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (fullPath.StartsWith(fullRootPrefix, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(fullRootPrefix.Length);

            return Path.GetFileName(filePath);
        }

        private static long TicksToMilliseconds(long ticks)
        {
            return ticks <= 0 ? 0 : (ticks * 1000L) / Stopwatch.Frequency;
        }
    }

    /// <summary>
    /// Collects native deployment-tree enumeration counters for File Manager diagnostics.
    /// </summary>
    internal sealed class FileManagerEnumerationStats
    {
        public int EnumeratedFiles { get; set; }
        public int ReparseFiles { get; set; }
        public int SkippedFiles { get; set; }
        public int SkippedDirectories { get; set; }
        public int SkippedReparseDirectories { get; set; }
    }
    internal sealed class FileManagerPathOwnership
    {
        /// <summary>
        /// Initializes the ownership information associated with one deployment path.
        /// </summary>
        /// <param name="hasActiveOwner">Whether the path currently has an active NMM owner.</param>
        /// <param name="activeOwnerKey">The stable key of the active owner.</param>
        /// <param name="activeOwnerName">The display name of the active owner.</param>
        /// <param name="ownerCount">The number of distinct owners represented by the path.</param>
        /// <param name="ownerCandidates">The selectable owner candidates, when the path has conflicts.</param>
        public FileManagerPathOwnership(bool hasActiveOwner, string activeOwnerKey, string activeOwnerName, int ownerCount, List<FileManagerOwnerCandidate> ownerCandidates)
        {
            HasActiveOwner = hasActiveOwner;
            ActiveOwnerKey = activeOwnerKey ?? String.Empty;
            ActiveOwnerName = activeOwnerName ?? String.Empty;
            OwnerCount = Math.Max(0, ownerCount);
            OwnerCandidates = ownerCandidates ?? FileManagerRow.EmptyOwnerCandidates;
        }

        public bool HasActiveOwner { get; private set; }
        public string ActiveOwnerKey { get; private set; }
        public string ActiveOwnerName { get; private set; }
        public int OwnerCount { get; private set; }
        public List<FileManagerOwnerCandidate> OwnerCandidates { get; private set; }
    }
}
