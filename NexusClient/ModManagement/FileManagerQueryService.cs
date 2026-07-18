namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;

    using Nexus.Client.Games;

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
            Dictionary<string, FileManagerPathOwnership> ownershipByPath = BuildVirtualLinkLookup(virtualModActivator, gameMode, deploymentRoot);
            diagnostics.VirtualLinkIndexMilliseconds = stageWatch.ElapsedMilliseconds;

            stageWatch.Restart();
            HashSet<string> baseFiles = BuildBaseFileSet(gameMode.BaseGameFiles);
            diagnostics.BaseFileIndexMilliseconds = stageWatch.ElapsedMilliseconds;

            stageWatch.Restart();
            IDictionary<string, FileManagerSource> manualSources = LoadManualSources(gameMode.ModeId);
            diagnostics.ManualSourceLoadMilliseconds = stageWatch.ElapsedMilliseconds;

            List<FileManagerRow> rows = new List<FileManagerRow>();
            Dictionary<string, FileManagerRow> rowsByPath = new Dictionary<string, FileManagerRow>(StringComparer.OrdinalIgnoreCase);
            FileManagerSourceCounts counts = new FileManagerSourceCounts();
            string rootPrefix = GetNormalizedRootPrefix(deploymentRoot);
            int skippedFiles = 0;
            FileManagerEnumerationStats enumerationStats = new FileManagerEnumerationStats();
            long metadataTicks = 0;
            long classificationTicks = 0;
            Stopwatch enumerationWatch = Stopwatch.StartNew();

            foreach (string filePath in EnumerateFilesSafely(deploymentRoot, cancellationToken, enumerationStats))
            {
                cancellationToken.ThrowIfCancellationRequested();

                Stopwatch perFileWatch = Stopwatch.StartNew();
                FileInfo fileInfo;
                try
                {
                    fileInfo = new FileInfo(filePath);
                    if (!fileInfo.Exists)
                    {
                        skippedFiles++;
                        continue;
                    }
                }
                catch
                {
                    skippedFiles++;
                    continue;
                }

                long length = fileInfo.Length;
                metadataTicks += perFileWatch.ElapsedTicks;

                string relativePath = GetRelativePath(rootPrefix, filePath);
                string normalizedPath = NormalizePath(relativePath);
                FileManagerRow row = new FileManagerRow
                {
                    FullPath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileType = GetFileType(filePath),
                    RawSize = length,
                    SizeDisplay = FormatSize(length),
                    RelativePath = relativePath,
                    NormalizedRelativePath = normalizedPath
                };

                perFileWatch.Restart();
                FileManagerPathOwnership ownership;
                ownershipByPath.TryGetValue(normalizedPath, out ownership);
                ApplySourceClassification(row, ownership, baseFiles, manualSources);
                classificationTicks += perFileWatch.ElapsedTicks;

                rows.Add(row);
                if (!rowsByPath.ContainsKey(normalizedPath))
                    rowsByPath.Add(normalizedPath, row);
                counts.Add(row.Source);
            }

            long enumerationTicks = Math.Max(0, enumerationWatch.ElapsedTicks - metadataTicks - classificationTicks);
            diagnostics.FileEnumerationMilliseconds = TicksToMilliseconds(enumerationTicks);
            diagnostics.FileMetadataMilliseconds = TicksToMilliseconds(metadataTicks);
            diagnostics.ClassificationMilliseconds = TicksToMilliseconds(classificationTicks);
            stageWatch.Restart();
            rows.TrimExcess();
            diagnostics.IndexConstructionMilliseconds = stageWatch.ElapsedMilliseconds;
            totalWatch.Stop();
            diagnostics.TotalMilliseconds = totalWatch.ElapsedMilliseconds;

            Trace.TraceInformation("File Manager scan completed. Files={0}, skippedFiles={1}, skippedDirectories={2}, {3}", rows.Count, skippedFiles, enumerationStats.SkippedDirectories, diagnostics);
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
                ownershipByPath.TryGetValue(row.NormalizedRelativePath, out ownership);
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
                if (row == null || String.IsNullOrWhiteSpace(row.NormalizedRelativePath))
                {
                    rows.RemoveAt(index);
                    continue;
                }

                FileManagerPathOwnership ownership;
                bool hasActiveOwnership = ownershipByPath.TryGetValue(row.NormalizedRelativePath, out ownership) && ownership != null && ownership.HasActiveOwner;

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

                if (!rowsByNormalizedPath.ContainsKey(row.NormalizedRelativePath))
                    rowsByNormalizedPath.Add(row.NormalizedRelativePath, row);
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
                if (row == null || rowsByNormalizedPath.ContainsKey(row.NormalizedRelativePath))
                    continue;

                FileManagerPathOwnership canonicalOwnership;
                if (!ownershipByPath.TryGetValue(row.NormalizedRelativePath, out canonicalOwnership))
                    canonicalOwnership = ownership;

                ApplySourceClassification(row, canonicalOwnership, baseFiles, manualSources);
                rows.Add(row);
                rowsByNormalizedPath.Add(row.NormalizedRelativePath, row);
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

            FileManagerPathOwnership ownership = BuildOwnershipForPath(virtualModActivator, gameMode, row.NormalizedRelativePath);
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

            return _manualSourceStore.Load(gameModeId) ?? new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase);
        }

        public void SaveManualSource(string gameModeId, FileManagerRow row, FileManagerSource source)
        {
            if (_manualSourceStore == null)
                return;
            if (row == null) throw new ArgumentNullException("row");

            _manualSourceStore.SetSource(gameModeId, row.NormalizedRelativePath, source);
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

            if (baseFiles != null && baseFiles.Contains(row.NormalizedRelativePath))
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
            if (manualSources != null && manualSources.TryGetValue(row.NormalizedRelativePath, out manualSource) && FileManagerSourceDisplay.IsManualSource(manualSource) && manualSource != FileManagerSource.Untracked)
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

            return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();
        }

        public static string CreateOwnerKey(IVirtualModInfo modInfo)
        {
            if (modInfo == null)
                return String.Empty;

            return (modInfo.ModFileName ?? String.Empty).ToLowerInvariant() + "|" + (modInfo.DownloadId ?? String.Empty).ToLowerInvariant();
        }

        private static Dictionary<string, FileManagerPathOwnership> BuildVirtualLinkLookup(IVirtualModActivator virtualModActivator, IGameMode gameMode, string deploymentRoot)
        {
            IEnumerable<IVirtualModLink> links = virtualModActivator == null ? null : virtualModActivator.VirtualLinks;
            List<string> sourceRoots = GetVirtualSourceRoots(virtualModActivator);
            Dictionary<string, List<IVirtualModLink>> linksByPath = new Dictionary<string, List<IVirtualModLink>>(StringComparer.OrdinalIgnoreCase);
            if (links == null)
                return new Dictionary<string, FileManagerPathOwnership>(StringComparer.OrdinalIgnoreCase);

            List<IVirtualModLink> linkSnapshot = new List<IVirtualModLink>();
            foreach (IVirtualModLink link in links)
                if (link != null && !String.IsNullOrWhiteSpace(link.VirtualModPath))
                    linkSnapshot.Add(link);

            Dictionary<IVirtualModLink, string> deployedPaths = null;
            VirtualModActivator concreteActivator = virtualModActivator as VirtualModActivator;
            if (concreteActivator != null)
                deployedPaths = concreteActivator.GetDeployedFilePaths(linkSnapshot);

            foreach (IVirtualModLink link in linkSnapshot)
            {
                string deployedPath = String.Empty;
                if (deployedPaths != null)
                    deployedPaths.TryGetValue(link, out deployedPath);

                foreach (string key in GetFileManagerOwnershipKeys(link, gameMode, deploymentRoot, deployedPath))
                    AddLinkToOwnershipLookup(linksByPath, key, link);
            }

            Dictionary<string, FileManagerPathOwnership> ownershipByPath = new Dictionary<string, FileManagerPathOwnership>(linksByPath.Count, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<IVirtualModLink>> pair in linksByPath)
                ownershipByPath.Add(pair.Key, BuildOwnership(pair.Value, sourceRoots));

            return ownershipByPath;
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

        private static void AddLinkToOwnershipLookup(Dictionary<string, List<IVirtualModLink>> linksByPath, string key, IVirtualModLink link)
        {
            if (String.IsNullOrWhiteSpace(key))
                return;

            List<IVirtualModLink> fileLinks;
            if (!linksByPath.TryGetValue(key, out fileLinks))
            {
                fileLinks = new List<IVirtualModLink>();
                linksByPath.Add(key, fileLinks);
            }

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

            List<IVirtualModLink> orderedLinks = new List<IVirtualModLink>();
            foreach (IVirtualModLink link in pathLinks)
                if (link != null)
                    orderedLinks.Add(link);

            if (orderedLinks.Count == 0)
                return null;

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

            List<FileManagerOwnerCandidate> candidates = FileManagerRow.EmptyOwnerCandidates;
            if (activeOwner.Active)
            {
                candidates = new List<FileManagerOwnerCandidate>();
                HashSet<string> seenOwnerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (IVirtualModLink link in orderedLinks)
                {
                    string ownerKey = CreateOwnerKey(link.ModInfo);
                    if (!seenOwnerKeys.Add(ownerKey))
                        continue;

                    candidates.Add(new FileManagerOwnerCandidate(ownerKey, link.ModInfo == null ? String.Empty : link.ModInfo.ModName, link.Priority, ResolvePreviewFilePath(link, sourceRoots)));
                }
            }

            return new FileManagerPathOwnership(activeOwner.Active, CreateOwnerKey(activeOwner.ModInfo), activeOwner.ModInfo == null ? String.Empty : activeOwner.ModInfo.ModName, candidates);
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

        private static string ResolvePreviewFilePath(IVirtualModLink link, IList<string> sourceRoots)
        {
            if (link == null || String.IsNullOrWhiteSpace(link.RealModPath))
                return String.Empty;

            if (Path.IsPathRooted(link.RealModPath) && File.Exists(link.RealModPath))
                return link.RealModPath;

            if (sourceRoots != null)
            {
                foreach (string sourceRoot in sourceRoots)
                {
                    if (String.IsNullOrWhiteSpace(sourceRoot))
                        continue;

                    string filePath = Path.Combine(sourceRoot, link.RealModPath);
                    if (File.Exists(filePath))
                        return filePath;
                }
            }

            return String.Empty;
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

                return new FileManagerRow
                {
                    FullPath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileType = GetFileType(filePath),
                    RawSize = fileInfo.Length,
                    SizeDisplay = FormatSize(fileInfo.Length),
                    RelativePath = relativePath,
                    NormalizedRelativePath = normalizedPath
                };
            }
            catch
            {
                return null;
            }
        }

        private static string GetFileType(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            if (String.IsNullOrEmpty(extension))
                return String.Empty;

            return extension.TrimStart('.').ToLowerInvariant();
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

        private static IEnumerable<string> EnumerateFilesSafely(string root, CancellationToken cancellationToken, FileManagerEnumerationStats stats)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending.Pop();

                IEnumerator<string> fileEnumerator = null;
                try
                {
                    fileEnumerator = Directory.EnumerateFiles(directory).GetEnumerator();
                }
                catch
                {
                    stats.SkippedDirectories++;
                }

                if (fileEnumerator != null)
                {
                    using (fileEnumerator)
                    {
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string file;
                            try
                            {
                                if (!fileEnumerator.MoveNext())
                                    break;
                                file = fileEnumerator.Current;
                            }
                            catch
                            {
                                stats.SkippedDirectories++;
                                break;
                            }

                            yield return file;
                        }
                    }
                }

                IEnumerator<string> directoryEnumerator = null;
                try
                {
                    directoryEnumerator = Directory.EnumerateDirectories(directory).GetEnumerator();
                }
                catch
                {
                    stats.SkippedDirectories++;
                }

                if (directoryEnumerator == null)
                    continue;

                using (directoryEnumerator)
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string childDirectory;
                        try
                        {
                            if (!directoryEnumerator.MoveNext())
                                break;
                            childDirectory = directoryEnumerator.Current;
                        }
                        catch
                        {
                            stats.SkippedDirectories++;
                            break;
                        }

                        pending.Push(childDirectory);
                    }
                }
            }
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

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024 * 1024)
                return String.Format("{0:0.00} KB", bytes / 1024.0);

            return String.Format("{0:0.00} MB", bytes / 1024.0 / 1024.0);
        }

        private static long TicksToMilliseconds(long ticks)
        {
            return ticks <= 0 ? 0 : (ticks * 1000L) / Stopwatch.Frequency;
        }
    }

    internal sealed class FileManagerEnumerationStats
    {
        public int SkippedDirectories { get; set; }
    }
    internal sealed class FileManagerPathOwnership
    {
        public FileManagerPathOwnership(bool hasActiveOwner, string activeOwnerKey, string activeOwnerName, List<FileManagerOwnerCandidate> ownerCandidates)
        {
            HasActiveOwner = hasActiveOwner;
            ActiveOwnerKey = activeOwnerKey ?? String.Empty;
            ActiveOwnerName = activeOwnerName ?? String.Empty;
            OwnerCandidates = ownerCandidates ?? FileManagerRow.EmptyOwnerCandidates;
        }

        public bool HasActiveOwner { get; private set; }
        public string ActiveOwnerKey { get; private set; }
        public string ActiveOwnerName { get; private set; }
        public List<FileManagerOwnerCandidate> OwnerCandidates { get; private set; }
    }
}