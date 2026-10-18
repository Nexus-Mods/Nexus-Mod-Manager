using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games;
using Nexus.Client.Games.ManagedContent;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>
	/// Bounded opt-in metadata enumeration of configured component target folders. Skips observed reparse points
	/// without file-content access, writes or background scheduling. Run outside the UI render path.
	/// A partial/failed scan is returned as such; never use it as proof of absent native targets.
	/// </summary>
	public static class ManagedContentComponentDiskScan
	{
		/// <summary>Tracks one queued directory and its depth relative to a component folder.</summary>
		private sealed class WalkEntry
		{
			internal string Path;
			internal string RelativePath;
			internal int Depth;
		}

		/// <summary>Scans the current native Data root using the mode's immutable configured component rules.</summary>
		public static ManagedContentComponentDiskObservation Scan(IGameMode gameMode,
			int maxFiles = 100000, int maxDirectories = 20000, int maxDepth = 16)
		{
			if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));
			IManagedContentComponentProvider provider = gameMode as IManagedContentComponentProvider;
			if (provider == null) throw new ArgumentException("The game mode does not expose configured component rules.", nameof(gameMode));
			return Scan(ModDeploymentTargetResolver.GetPhysicalRootPath(gameMode, ModDeploymentRoot.Data),
				provider.ComponentRules, maxFiles, maxDirectories, maxDepth);
		}

		/// <summary>Enumerates configured target folders without archive reads, content reads or native state changes.</summary>
		public static ManagedContentComponentDiskObservation Scan(string physicalRoot,
			IEnumerable<ManagedContentLayoutRule> componentRules, int maxFiles = 100000, int maxDirectories = 20000, int maxDepth = 16)
		{
			if (String.IsNullOrWhiteSpace(physicalRoot) || !Path.IsPathRooted(physicalRoot))
				throw new ArgumentException("An absolute game root is required.", nameof(physicalRoot));
			if (maxFiles < 1) throw new ArgumentOutOfRangeException(nameof(maxFiles));
			if (maxDirectories < 1) throw new ArgumentOutOfRangeException(nameof(maxDirectories));
			if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth));

			if (componentRules == null) throw new ArgumentNullException(nameof(componentRules));
			ManagedContentLayoutRule[] rules = componentRules.Take(33).ToArray();
			if (rules.Length == 0 || rules.Length > 32 || rules.Any(rule => rule == null))
				throw new ArgumentException("Component scanning requires 1 to 32 configured layout rules.", nameof(componentRules));
			string[] targetFolders = rules.Select(rule => rule.TargetFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

			var files = new List<string>();
			var folders = new List<string>();
			var issues = new List<ManagedContentComponentDiskIssue>();
			string root = Path.GetFullPath(physicalRoot);
			if (!CanEnumerateDirectory(root, true, issues))
				return new ManagedContentComponentDiskObservation(root, files, folders, false, issues, targetFolders);

			var queue = new Queue<WalkEntry>();
			int directoryCount = 0;
			foreach (string rootFolder in targetFolders)
			{
				string dir = Path.Combine(root, rootFolder);
				if (!CanEnumerateDirectory(dir, false, issues)) continue;
				try
				{
					foreach (string component in Directory.EnumerateDirectories(dir))
					{
						if (++directoryCount > maxDirectories)
						{
							issues.Add(new ManagedContentComponentDiskIssue(ManagedContentComponentDiskIssueKind.ScanLimitReached,
								rootFolder, "The directory count limit was reached."));
							return new ManagedContentComponentDiskObservation(root, files, folders, false, issues, targetFolders);
						}
						string relative = rootFolder + "\\" + Path.GetFileName(component);
						if (IsReparsePoint(component, issues)) continue;
						folders.Add(relative);
						queue.Enqueue(new WalkEntry { Path = component, RelativePath = relative, Depth = 0 });
					}
				}
				catch (Exception ex) when (IsEnumerationError(ex))
				{
					issues.Add(new ManagedContentComponentDiskIssue(ManagedContentComponentDiskIssueKind.EnumerationFailed,
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
							issues.Add(new ManagedContentComponentDiskIssue(ManagedContentComponentDiskIssueKind.ScanLimitReached,
								current.RelativePath, "The file count limit was reached."));
							return new ManagedContentComponentDiskObservation(root, files, folders, false, issues, targetFolders);
						}
						files.Add(current.RelativePath + "\\" + Path.GetFileName(file));
					}
					foreach (string dir in Directory.EnumerateDirectories(current.Path))
					{
						if (++directoryCount > maxDirectories || current.Depth >= maxDepth)
						{
							issues.Add(new ManagedContentComponentDiskIssue(ManagedContentComponentDiskIssueKind.ScanLimitReached,
								current.RelativePath, "The directory count/depth limit was reached."));
							return new ManagedContentComponentDiskObservation(root, files, folders, false, issues, targetFolders);
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
					issues.Add(new ManagedContentComponentDiskIssue(ManagedContentComponentDiskIssueKind.EnumerationFailed,
						current.RelativePath, ex.Message));
				}
			}
			return new ManagedContentComponentDiskObservation(root, files, folders, issues.Count == 0, issues, targetFolders);
		}

		/// <summary>Distinguishes an absent optional folder from access failures before treating a scan as complete.</summary>
		private static bool CanEnumerateDirectory(string path, bool requiredRoot, List<ManagedContentComponentDiskIssue> issues)
		{
			try
			{
				FileAttributes attributes = File.GetAttributes(path);
				if ((attributes & FileAttributes.ReparsePoint) != 0)
				{
					AddReparseIssue(path, issues);
					return false;
				}
				if ((attributes & FileAttributes.Directory) != 0) return true;
				issues.Add(new ManagedContentComponentDiskIssue(requiredRoot ? ManagedContentComponentDiskIssueKind.RootMissing :
					ManagedContentComponentDiskIssueKind.EnumerationFailed, path, "The configured component directory is not a directory."));
				return false;
			}
			catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
			{
				if (requiredRoot)
					issues.Add(new ManagedContentComponentDiskIssue(ManagedContentComponentDiskIssueKind.RootMissing,
						path, "The configured component root cannot be enumerated."));
				return false;
			}
			catch (Exception ex) when (IsEnumerationError(ex))
			{
				issues.Add(new ManagedContentComponentDiskIssue(ManagedContentComponentDiskIssueKind.EnumerationFailed, path, ex.Message));
				return false;
			}
		}

		/// <summary>Skips observed junctions/symlinks and records inaccessible entries as incomplete evidence.</summary>
		private static bool IsReparsePoint(string path, List<ManagedContentComponentDiskIssue> issues)
		{
			try
			{
				if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return false;
				AddReparseIssue(path, issues);
				return true;
			}
			catch (Exception ex) when (IsEnumerationError(ex))
			{
				issues.Add(new ManagedContentComponentDiskIssue(ManagedContentComponentDiskIssueKind.EnumerationFailed,
					path, ex.Message));
				return true;
			}
		}

		/// <summary>Records why an observed reparse point prevents a complete component inventory.</summary>
		private static void AddReparseIssue(string path, List<ManagedContentComponentDiskIssue> issues)
		{
			issues.Add(new ManagedContentComponentDiskIssue(ManagedContentComponentDiskIssueKind.ReparsePointSkipped,
				path, "Skipped junction/symlink; the scan cannot prove its contents are inside the component root."));
		}

		/// <summary>Limits recoverable scan failures to filesystem and path-access errors.</summary>
		private static bool IsEnumerationError(Exception error)
		{
			return error is IOException || error is UnauthorizedAccessException ||
				error is ArgumentException || error is NotSupportedException;
		}
	}
}
