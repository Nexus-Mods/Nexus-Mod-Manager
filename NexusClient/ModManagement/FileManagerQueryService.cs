namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;

    using Nexus.Client.Games;

    public sealed class FileManagerQueryService
    {
        public FileManagerScanResult Scan(IGameMode gameMode, IVirtualModActivator virtualModActivator, CancellationToken cancellationToken)
        {
            if (gameMode == null) throw new ArgumentNullException("gameMode");
            if (virtualModActivator == null) throw new ArgumentNullException("virtualModActivator");

            string deploymentRoot = GetDeploymentRoot(gameMode);
            if (string.IsNullOrWhiteSpace(deploymentRoot) || !Directory.Exists(deploymentRoot))
                throw new DirectoryNotFoundException("The deployment root does not exist or is inaccessible: " + (deploymentRoot ?? String.Empty));

            Dictionary<string, List<IVirtualModLink>> linksByPath = BuildVirtualLinkLookup(virtualModActivator.VirtualLinks);
            HashSet<string> baseFiles = BuildBaseFileSet(gameMode.BaseGameFiles);
            List<FileManagerRow> rows = new List<FileManagerRow>();

            foreach (string filePath in EnumerateFilesSafely(deploymentRoot, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileInfo fileInfo;
                try
                {
                    fileInfo = new FileInfo(filePath);
                    if (!fileInfo.Exists)
                        continue;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("File Manager skipped file '{0}': {1}", filePath, ex.Message);
                    continue;
                }

                string relativePath = GetRelativePath(deploymentRoot, filePath);
                string normalizedPath = NormalizePath(relativePath);
                FileManagerRow row = new FileManagerRow
                {
                    FullPath = filePath,
                    FileName = Path.GetFileName(filePath),
                    RawSize = fileInfo.Length,
                    SizeDisplay = FormatSize(fileInfo.Length),
                    RelativePath = relativePath,
                    NormalizedRelativePath = normalizedPath
                };

                List<IVirtualModLink> pathLinks;
                if (linksByPath.TryGetValue(normalizedPath, out pathLinks) && pathLinks.Any(x => x.Active))
                {
                    ApplyNmmOwnership(row, pathLinks);
                }
                else if (baseFiles.Contains(normalizedPath))
                {
                    row.Source = FileManagerSource.BaseGameFile;
                }
                else
                {
                    row.Source = FileManagerSource.Untracked;
                }

                rows.Add(row);
            }

            return new FileManagerScanResult(deploymentRoot, rows, DateTime.Now);
        }

        public void RefreshRowOwnership(FileManagerRow row, IVirtualModActivator virtualModActivator)
        {
            if (row == null || virtualModActivator == null)
                return;

            List<IVirtualModLink> links = virtualModActivator.VirtualLinks
                .Where(x => x != null && String.Equals(NormalizePath(x.VirtualModPath), row.NormalizedRelativePath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (links.Any(x => x.Active))
                ApplyNmmOwnership(row, links);
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

        private static Dictionary<string, List<IVirtualModLink>> BuildVirtualLinkLookup(IEnumerable<IVirtualModLink> links)
        {
            Dictionary<string, List<IVirtualModLink>> lookup = new Dictionary<string, List<IVirtualModLink>>(StringComparer.OrdinalIgnoreCase);
            if (links == null)
                return lookup;

            foreach (IVirtualModLink link in links)
            {
                if (link == null || String.IsNullOrWhiteSpace(link.VirtualModPath))
                    continue;

                string key = NormalizePath(link.VirtualModPath);
                List<IVirtualModLink> fileLinks;
                if (!lookup.TryGetValue(key, out fileLinks))
                {
                    fileLinks = new List<IVirtualModLink>();
                    lookup[key] = fileLinks;
                }

                fileLinks.Add(link);
            }

            return lookup;
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

        private static void ApplyNmmOwnership(FileManagerRow row, List<IVirtualModLink> pathLinks)
        {
            List<IVirtualModLink> orderedLinks = pathLinks
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.ModInfo == null ? String.Empty : x.ModInfo.ModName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            IVirtualModLink activeOwner = orderedLinks.FirstOrDefault(x => x.Active) ?? orderedLinks.First();
            row.Source = FileManagerSource.InstalledByNmm;
            row.OwnerCandidates = orderedLinks
                .Select(x => new FileManagerOwnerCandidate(CreateOwnerKey(x.ModInfo), x.ModInfo == null ? String.Empty : x.ModInfo.ModName, x.Priority))
                .GroupBy(x => x.OwnerKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
            row.OwnerKey = CreateOwnerKey(activeOwner.ModInfo);
            row.OwnerName = activeOwner.ModInfo == null ? String.Empty : activeOwner.ModInfo.ModName;
        }

        private static IEnumerable<string> EnumerateFilesSafely(string root, CancellationToken cancellationToken)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending.Pop();

                string[] files;
                try
                {
                    files = Directory.GetFiles(directory);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("File Manager skipped directory '{0}': {1}", directory, ex.Message);
                    continue;
                }

                foreach (string file in files)
                    yield return file;

                string[] directories;
                try
                {
                    directories = Directory.GetDirectories(directory);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("File Manager skipped subdirectories under '{0}': {1}", directory, ex.Message);
                    continue;
                }

                for (int i = directories.Length - 1; i >= 0; i--)
                    pending.Push(directories[i]);
            }
        }

        private static string GetRelativePath(string root, string filePath)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(filePath);
            if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(fullRoot.Length);

            return Path.GetFileName(filePath);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024 * 1024)
                return String.Format("{0:0.00} KB", bytes / 1024.0);

            return String.Format("{0:0.00} MB", bytes / 1024.0 / 1024.0);
        }
    }
}