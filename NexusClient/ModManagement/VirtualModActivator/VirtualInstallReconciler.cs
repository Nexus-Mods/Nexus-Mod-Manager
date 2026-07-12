namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;

	internal sealed class VirtualInstallReconciler : IVirtualInstallReconciler
	{
		public VirtualInstallReconciliationReport Inspect(IList<IVirtualModInfo> virtualMods, IList<IVirtualModLink> virtualLinks, IList<string> sourceRoots, IList<string> gameDataRoots)
		{
			VirtualInstallReconciliationReport report = new VirtualInstallReconciliationReport();
			List<IVirtualModInfo> lstVirtualMods = virtualMods == null ? new List<IVirtualModInfo>() : new List<IVirtualModInfo>(virtualMods);
			List<IVirtualModLink> lstVirtualLinks = virtualLinks == null ? new List<IVirtualModLink>() : new List<IVirtualModLink>(virtualLinks);
			List<string> lstSourceRoots = sourceRoots == null ? new List<string>() : sourceRoots.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			List<string> lstGameDataRoots = gameDataRoots == null ? new List<string>() : gameDataRoots.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			DetectLinkIssues(report, lstVirtualMods, lstVirtualLinks, lstSourceRoots, lstGameDataRoots);
			DetectModInfoIssues(report, lstVirtualMods, lstVirtualLinks);
			DetectDuplicateVirtualPaths(report, lstVirtualLinks);

			return report;
		}

		private static void DetectLinkIssues(VirtualInstallReconciliationReport report, IList<IVirtualModInfo> virtualMods, IList<IVirtualModLink> virtualLinks, IList<string> sourceRoots, IList<string> gameDataRoots)
		{
			foreach (IVirtualModLink link in virtualLinks)
			{
				if (link == null)
				{
					report.AddIssue(VirtualInstallReconciliationIssueKind.NullVirtualLink, "Virtual link list contains a null link entry.", (IVirtualModLink)null, true);
					continue;
				}

				if (link.ModInfo == null || !ContainsMatchingModInfo(virtualMods, link.ModInfo))
					report.AddIssue(VirtualInstallReconciliationIssueKind.MissingModInfo, "Virtual link does not have a matching virtual mod metadata entry.", link, !link.Active);

				if (string.IsNullOrWhiteSpace(link.RealModPath))
					report.AddIssue(VirtualInstallReconciliationIssueKind.EmptyRealPath, "Virtual link has an empty real/source path.", link, !link.Active);
				else if (!SourceFileExists(sourceRoots, link.RealModPath))
				{
					VirtualInstallReconciliationIssueKind kind = link.Active ? VirtualInstallReconciliationIssueKind.MissingSourceFile : VirtualInstallReconciliationIssueKind.MissingInactiveSourceFile;
					report.AddIssue(kind, "Virtual link source file is missing from the known virtual install source roots.", link, !link.Active);
				}

				if (string.IsNullOrWhiteSpace(link.VirtualModPath))
					report.AddIssue(VirtualInstallReconciliationIssueKind.EmptyVirtualPath, "Virtual link has an empty game-visible virtual path.", link, !link.Active);
				else if (link.Active && !DeployedFileExists(gameDataRoots, link.VirtualModPath))
					report.AddIssue(VirtualInstallReconciliationIssueKind.MissingDeployedActiveFile, "Active virtual link is missing from the game folder.", link, false);
			}
		}

		private static void DetectModInfoIssues(VirtualInstallReconciliationReport report, IList<IVirtualModInfo> virtualMods, IList<IVirtualModLink> virtualLinks)
		{
			foreach (IVirtualModInfo modInfo in virtualMods)
			{
				if (modInfo == null)
				{
					report.AddIssue(VirtualInstallReconciliationIssueKind.MetadataInconsistency, "Virtual mod metadata list contains a null mod entry.", (string)null, null, null, null, null, true);
					continue;
				}

				if (!virtualLinks.Any(x => x != null && ModInfosMatch(x.ModInfo, modInfo)))
					report.AddIssue(VirtualInstallReconciliationIssueKind.ModInfoWithoutLinks, "Virtual mod metadata entry is not referenced by any virtual link.", modInfo, true);
			}
		}

		private static void DetectDuplicateVirtualPaths(VirtualInstallReconciliationReport report, IList<IVirtualModLink> virtualLinks)
		{
			Dictionary<string, List<IVirtualModLink>> dicLinksByVirtualPath = new Dictionary<string, List<IVirtualModLink>>(StringComparer.OrdinalIgnoreCase);
			foreach (IVirtualModLink link in virtualLinks)
			{
				if (link == null || string.IsNullOrWhiteSpace(link.VirtualModPath))
					continue;

				string strVirtualPath = NormalizePathKey(link.VirtualModPath);
				List<IVirtualModLink> lstLinks;
				if (!dicLinksByVirtualPath.TryGetValue(strVirtualPath, out lstLinks))
				{
					lstLinks = new List<IVirtualModLink>();
					dicLinksByVirtualPath.Add(strVirtualPath, lstLinks);
				}

				lstLinks.Add(link);
			}

			foreach (KeyValuePair<string, List<IVirtualModLink>> kvpLinks in dicLinksByVirtualPath.Where(x => x.Value.Count > 1))
				report.AddIssue(VirtualInstallReconciliationIssueKind.DuplicateVirtualPath, "Multiple virtual links target the same game-visible path; conflict ownership is report-only.", null, null, kvpLinks.Key, null, null, false);
		}

		private static bool ContainsMatchingModInfo(IList<IVirtualModInfo> virtualMods, IVirtualModInfo modInfo)
		{
			return virtualMods.Any(x => ModInfosMatch(x, modInfo));
		}

		private static bool ModInfosMatch(IVirtualModInfo left, IVirtualModInfo right)
		{
			if (left == null || right == null)
				return false;
			if (ReferenceEquals(left, right))
				return true;
			if (!string.IsNullOrEmpty(left.ModFileName) && !string.IsNullOrEmpty(right.ModFileName) && left.ModFileName.Equals(right.ModFileName, StringComparison.InvariantCultureIgnoreCase))
				return true;
			if (!string.IsNullOrEmpty(left.DownloadId) && !string.IsNullOrEmpty(right.DownloadId) && left.DownloadId.Equals(right.DownloadId, StringComparison.InvariantCultureIgnoreCase))
				return true;
			if (!string.IsNullOrEmpty(left.UpdatedDownloadId) && !string.IsNullOrEmpty(right.DownloadId) && left.UpdatedDownloadId.Equals(right.DownloadId, StringComparison.InvariantCultureIgnoreCase))
				return true;
			if (!string.IsNullOrEmpty(right.UpdatedDownloadId) && !string.IsNullOrEmpty(left.DownloadId) && left.DownloadId.Equals(right.UpdatedDownloadId, StringComparison.OrdinalIgnoreCase))
				return true;

			return false;
		}

		private static bool SourceFileExists(IList<string> sourceRoots, string realModPath)
		{
			foreach (string strSourceRoot in sourceRoots)
			{
				if (FileExistsInRoot(strSourceRoot, realModPath))
					return true;
			}

			return false;
		}

		private static bool DeployedFileExists(IList<string> gameDataRoots, string virtualModPath)
		{
			foreach (string strGameDataRoot in gameDataRoots)
			{
				if (FileExistsInRoot(strGameDataRoot, virtualModPath))
					return true;
			}

			return false;
		}

		private static bool FileExistsInRoot(string rootPath, string relativePath)
		{
			try
			{
				return File.Exists(Path.Combine(rootPath, relativePath));
			}
			catch
			{
				return false;
			}
		}

		private static string NormalizePathKey(string path)
		{
			return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim();
		}
	}
}