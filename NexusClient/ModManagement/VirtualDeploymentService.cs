namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Nexus.Client.Mods;

    /// <summary>
    /// Compatibility deployment boundary backed by the existing virtual activator and link installer.
    /// </summary>
    public sealed class VirtualDeploymentService : IVirtualDeploymentService
    {
        private readonly IVirtualModActivator _virtualModActivator;

        public VirtualDeploymentService(IVirtualModActivator virtualModActivator)
        {
            if (virtualModActivator == null) throw new ArgumentNullException(nameof(virtualModActivator));
            _virtualModActivator = virtualModActivator;
        }

        public VirtualDeploymentResult ActivateModLinks(IMod mod, VirtualDeploymentOptions options)
        {
            if (mod == null) throw new ArgumentNullException(nameof(mod));

            VirtualDeploymentOptions deploymentOptions = options ?? new VirtualDeploymentOptions();
            VirtualDeploymentResult result = new VirtualDeploymentResult();
            IVirtualDeploymentSession session = new VirtualDeploymentSession(mod);
            VirtualModActivator.ModInfoUpdateBatch modInfoUpdateBatch = BeginModInfoUpdateBatch(_virtualModActivator);
            Exception deploymentException = null;

            try
            {
                string modFilenamePath = Path.Combine(_virtualModActivator.VirtualPath, Path.GetFileNameWithoutExtension(mod.Filename).Trim());
                string linkFilenamePath = _virtualModActivator.MultiHDMode ? Path.Combine(_virtualModActivator.HDLinkFolder, Path.GetFileNameWithoutExtension(mod.Filename).Trim()) : string.Empty;
                string modDownloadIdPath = string.IsNullOrWhiteSpace(mod.DownloadId) || mod.DownloadId.Length <= 1 || mod.DownloadId.Equals("-1", StringComparison.OrdinalIgnoreCase) ? string.Empty : Path.Combine(_virtualModActivator.VirtualPath, mod.DownloadId);
                string linkDownloadIdPath = _virtualModActivator.MultiHDMode ? string.IsNullOrWhiteSpace(mod.DownloadId) || mod.DownloadId.Length <= 1 || mod.DownloadId.Equals("-1", StringComparison.OrdinalIgnoreCase) ? string.Empty : Path.Combine(_virtualModActivator.HDLinkFolder, mod.DownloadId) : string.Empty;
                string modFolderPath = modFilenamePath;
                string linkFolderPath = linkFilenamePath;

                if (!string.IsNullOrWhiteSpace(modDownloadIdPath) && Directory.Exists(modDownloadIdPath))
                    modFolderPath = modDownloadIdPath;

                if (_virtualModActivator.MultiHDMode && !string.IsNullOrWhiteSpace(linkDownloadIdPath) && Directory.Exists(linkDownloadIdPath))
                    linkFolderPath = linkDownloadIdPath;

                if (!Directory.Exists(modFolderPath) && !(_virtualModActivator.MultiHDMode && Directory.Exists(linkFolderPath)))
                    return result;

                IEnumerable<string> files;
                try
                {
                    if (_virtualModActivator.MultiHDMode && Directory.Exists(linkFolderPath))
                    {
                        files = EnumerateDeploymentFiles(linkFolderPath, Directory.Exists(modFolderPath) ? modFolderPath : null);
                        result.SourceRoot = linkFolderPath;
                    }
                    else
                    {
                        files = EnumerateDeploymentFiles(modFolderPath, null);
                        result.SourceRoot = modFolderPath;
                    }

                    result.FileCount = files.Count();
                }
                catch (Exception ex)
                {
                    result.Failure = ex;
                    return result;
                }

                session.SetSourceRoot(result.SourceRoot);
                session.SetFileCount(result.FileCount);
                ReportProgress(deploymentOptions, result.SourceRoot, result.FileCount, 0, null);

                IModLinkInstaller modLinkInstaller = _virtualModActivator.GetModLinkInstaller();
                int processedFileCount = 0;
                using (IEnumerator<string> fileEnumerator = files.GetEnumerator())
                {
                    while (true)
                    {
                        string file;
                        try
                        {
                            if (!fileEnumerator.MoveNext())
                                break;

                            file = fileEnumerator.Current;
                        }
                        catch (Exception ex)
                        {
                            result.Failure = ex;
                            return result;
                        }

                        string relativeFilePath = _virtualModActivator.MultiHDMode && file.Contains(linkFolderPath)
                            ? file.Replace(linkFolderPath + Path.DirectorySeparatorChar, string.Empty)
                            : file.Replace(modFolderPath + Path.DirectorySeparatorChar, string.Empty);
                        string sourceFilePath = GetKnownSourceFilePath(file, modFolderPath, _virtualModActivator.MultiHDMode);
                        string linkedFilePath;
                        try
                        {
                            linkedFilePath = modLinkInstaller.AddFileLink(mod, relativeFilePath, sourceFilePath, false, false, deploymentOptions.InstallRoot);
                        }
                        catch (Exception ex)
                        {
                            result.Failure = ex;
                            return result;
                        }

                        if (!string.IsNullOrEmpty(linkedFilePath))
                        {
                            result.LinkedFileCount++;
                            session.RecordLinkedFile(linkedFilePath);
                            if (deploymentOptions.LinkedFileHandler != null && deploymentOptions.LinkedFileHandler(linkedFilePath))
                            {
                                result.PluginCandidatePaths.Add(linkedFilePath);
                                session.RecordPluginCandidate(linkedFilePath);
                            }
                        }

                        processedFileCount++;
                        ReportProgress(deploymentOptions, result.SourceRoot, result.FileCount, processedFileCount, file);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                deploymentException = ex;
                throw;
            }
            finally
            {
                try
                {
                    FlushModInfoUpdateBatch(modInfoUpdateBatch, result, deploymentException);
                }
                finally
                {
                    try
                    {
                        if (modInfoUpdateBatch != null)
                            modInfoUpdateBatch.Dispose();
                    }
                    finally
                    {
                        session.TraceSummary();
                    }
                }
            }
        }

        public VirtualFileOwnerSwitchResult SwitchFileOwner(string relativePath, string selectedOwnerKey)
        {
            VirtualModActivator compatibilityActivator = _virtualModActivator as VirtualModActivator;
            if (compatibilityActivator == null)
                return VirtualFileOwnerSwitchResult.Failed("The current virtual mod activator does not support file-owner switching.");

            try
            {
                return compatibilityActivator.SwitchFileOwner(relativePath, selectedOwnerKey);
            }
            catch (Exception ex)
            {
                return VirtualFileOwnerSwitchResult.Failed(ex);
            }
        }

        private static VirtualModActivator.ModInfoUpdateBatch BeginModInfoUpdateBatch(IVirtualModActivator virtualModActivator)
        {
            VirtualModActivator compatibilityActivator = virtualModActivator as VirtualModActivator;
            if (compatibilityActivator == null)
                return null;

            return compatibilityActivator.BeginModInfoUpdateBatch();
        }

        private static void FlushModInfoUpdateBatch(VirtualModActivator.ModInfoUpdateBatch modInfoUpdateBatch, VirtualDeploymentResult result, Exception deploymentException)
        {
            if (modInfoUpdateBatch == null)
                return;

            try
            {
                modInfoUpdateBatch.Flush();
            }
            catch
            {
                if (deploymentException != null || (result != null && result.Failure != null))
                    return;

                throw;
            }
        }

        private static IEnumerable<string> EnumerateDeploymentFiles(string primaryFolderPath, string secondaryFolderPath)
        {
            foreach (string file in Directory.EnumerateFiles(primaryFolderPath, "*", SearchOption.AllDirectories))
                yield return file;

            if (string.IsNullOrEmpty(secondaryFolderPath))
                yield break;

            foreach (string file in Directory.EnumerateFiles(secondaryFolderPath, "*", SearchOption.AllDirectories))
                yield return file;
        }
        private static string GetKnownSourceFilePath(string filePath, string sourceRoot, bool multiHDMode)
        {
            if (multiHDMode || string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sourceRoot) || !File.Exists(filePath))
                return null;

            try
            {
                string fullSourceRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullFilePath = Path.GetFullPath(filePath);

                if (fullFilePath.StartsWith(fullSourceRoot, StringComparison.OrdinalIgnoreCase))
                    return filePath;
            }
            catch
            {
            }

            return null;
        }

        private static void ReportProgress(VirtualDeploymentOptions options, string sourceRoot, int fileCount, int processedFileCount, string currentFilePath)
        {
            if (options.Progress != null)
                options.Progress(new VirtualDeploymentProgress(sourceRoot, fileCount, processedFileCount, currentFilePath));
        }
    }
}
