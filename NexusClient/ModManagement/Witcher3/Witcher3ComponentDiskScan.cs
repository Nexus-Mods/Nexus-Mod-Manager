using System;
using System.Collections.Generic;
using System.IO;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>
	/// Bounded opt-in read-only enumeration of Mods and DLC. No package extraction, file reads, writes,
	/// junction traversal or background scheduling. The caller must run this outside the UI render path.
	/// A partial/failed scan is returned as such; never use it as proof of absent native targets.
	/// </summary>
	public static class Witcher3ComponentDiskScan
	{
		private sealed class WalkEntry
		{
			internal string Path;
			internal string RelativePath;
			internal int Depth;
		}

		public static Witcher3ComponentDiskObservation Scan(string physicalGameRoot,
			int maxFiles = 100000, int maxDirectories = 20000, int maxDepth = 16)
		{
			if (String.IsNullOrWhiteSpace(physicalGameRoot) || !Path.IsPathRooted(physicalGameRoot))
				throw new ArgumentException("An absolute game root is required.", nameof(physicalGameRoot));
			if (maxFiles < 1) throw new ArgumentOutOfRangeException(nameof(maxFiles));
			if (maxDirectories < 1) throw new ArgumentOutOfRangeException(nameof(maxDirectories));
			if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth));

			var files = new List<string>();
			var folders = new List<string>();
			var issues = new List<Witcher3ComponentDiskIssue>();
			string root = Path.GetFullPath(physicalGameRoot);
			if (!Directory.Exists(root))
			{
				issues.Add(new Witcher3ComponentDiskIssue(Witcher3ComponentDiskIssueKind.GameRootMissing,
					root, "The Witcher installation root cannot be enumerated."));
				return new Witcher3ComponentDiskObservation(root, files, folders, false, issues);
			}
			if (IsReparsePoint(root, issues))
				return new Witcher3ComponentDiskObservation(root, files, folders, false, issues);

			var queue = new Queue<WalkEntry>();
			int directoryCount = 0;
			foreach (string rootFolder in new[] { "Mods", "DLC" })
			{
				string dir = Path.Combine(root, rootFolder);
				if (!Directory.Exists(dir)) continue;
				if (IsReparsePoint(dir, issues)) continue;
				try
				{
					foreach (string component in Directory.EnumerateDirectories(dir))
					{
						if (++directoryCount > maxDirectories)
						{
							issues.Add(new Witcher3ComponentDiskIssue(Witcher3ComponentDiskIssueKind.ScanLimitReached,
								rootFolder, "The directory count limit was reached."));
							return new Witcher3ComponentDiskObservation(root, files, folders, false, issues);
						}
						string relative = rootFolder + "\\" + Path.GetFileName(component);
						if (IsReparsePoint(component, issues)) continue;
						folders.Add(relative);
						queue.Enqueue(new WalkEntry { Path = component, RelativePath = relative, Depth = 0 });
					}
				}
				catch (Exception ex) when (IsEnumerationError(ex))
				{
					issues.Add(new Witcher3ComponentDiskIssue(Witcher3ComponentDiskIssueKind.EnumerationFailed,
						dir, ex.Message));
				}
			}

			while (queue.Count > 0)
			{
				WalkEntry current = queue.Dequeue();
				try
				{
					foreach (string file in Directory.EnumerateFiles(current.Path))
					{
						if (IsReparsePoint(file, issues)) continue;
						if (files.Count >= maxFiles)
						{
							issues.Add(new Witcher3ComponentDiskIssue(Witcher3ComponentDiskIssueKind.ScanLimitReached,
								current.RelativePath, "The file count limit was reached."));
							return new Witcher3ComponentDiskObservation(root, files, folders, false, issues);
						}
						files.Add(current.RelativePath + "\\" + Path.GetFileName(file));
					}
					foreach (string dir in Directory.EnumerateDirectories(current.Path))
					{
						if (++directoryCount > maxDirectories || current.Depth >= maxDepth)
						{
							issues.Add(new Witcher3ComponentDiskIssue(Witcher3ComponentDiskIssueKind.ScanLimitReached,
								current.RelativePath, "The directory count/depth limit was reached."));
							return new Witcher3ComponentDiskObservation(root, files, folders, false, issues);
						}
						if (IsReparsePoint(dir, issues)) continue;
						queue.Enqueue(new WalkEntry
						{
							Path = dir,
							RelativePath = current.RelativePath + "\\" + Path.GetFileName(dir),
							Depth = current.Depth + 1
						});
					}
				}
				catch (Exception ex) when (IsEnumerationError(ex))
				{
					issues.Add(new Witcher3ComponentDiskIssue(Witcher3ComponentDiskIssueKind.EnumerationFailed,
						current.RelativePath, ex.Message));
				}
			}
			return new Witcher3ComponentDiskObservation(root, files, folders, issues.Count == 0, issues);
		}

		private static bool IsReparsePoint(string path, List<Witcher3ComponentDiskIssue> issues)
		{
			try
			{
				if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return false;
				issues.Add(new Witcher3ComponentDiskIssue(Witcher3ComponentDiskIssueKind.ReparsePointSkipped,
					path, "Skipped junction/symlink; the scan cannot prove its contents are inside the game root."));
				return true;
			}
			catch (Exception ex) when (IsEnumerationError(ex))
			{
				issues.Add(new Witcher3ComponentDiskIssue(Witcher3ComponentDiskIssueKind.EnumerationFailed,
					path, ex.Message));
				return true;
			}
		}

		private static bool IsEnumerationError(Exception error)
		{
			return error is IOException || error is UnauthorizedAccessException ||
				error is ArgumentException || error is NotSupportedException;
		}
	}
}
