namespace Nexus.Client.ModManagement
{
    using System;
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

                string[] files;
                try
                {
                    if (_virtualModActivator.MultiHDMode && Directory.Exists(linkFolderPath))
                    {
                        files = Directory.Exists(modFolderPath)
                            ? Directory.GetFiles(linkFolderPath, "*", SearchOption.AllDirectories).Concat(Directory.GetFiles(modFolderPath, "*", SearchOption.AllDirectories)).ToArray()
                            : Directory.GetFiles(linkFolderPath, "*", SearchOption.AllDirectories);
                        result.SourceRoot = linkFolderPath;
                    }
                    else
                    {
                        files = Directory.GetFiles(modFolderPath, "*", SearchOption.AllDirectories);
                        result.SourceRoot = modFolderPath;
                    }
                }
                catch (Exception ex)
                {
                    result.Failure = ex;
                    return result;
                }

                session.SetSourceRoot(result.SourceRoot);
                result.FileCount = files.Length;
                session.SetFileCount(result.FileCount);
                ReportProgress(deploymentOptions, result.SourceRoot, result.FileCount, 0, null);

                IModLinkInstaller modLinkInstaller = _virtualModActivator.GetModLinkInstaller();
                for (int index = 0; index < files.Length; index++)
                {
                    string file = files[index];
                    string relativeFilePath = _virtualModActivator.MultiHDMode && file.Contains(linkFolderPath)
                        ? file.Replace(linkFolderPath + Path.DirectorySeparatorChar, string.Empty)
                        : file.Replace(modFolderPath + Path.DirectorySeparatorChar, string.Empty);
                    string linkedFilePath;
                    try
                    {
                        linkedFilePath = modLinkInstaller.AddFileLink(mod, relativeFilePath, null, false);
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

                    ReportProgress(deploymentOptions, result.SourceRoot, result.FileCount, index + 1, file);
                }

                return result;
            }
            finally
            {
                session.TraceSummary();
            }
        }

        private static void ReportProgress(VirtualDeploymentOptions options, string sourceRoot, int fileCount, int processedFileCount, string currentFilePath)
        {
            if (options.Progress != null)
                options.Progress(new VirtualDeploymentProgress(sourceRoot, fileCount, processedFileCount, currentFilePath));
        }
    }
}
