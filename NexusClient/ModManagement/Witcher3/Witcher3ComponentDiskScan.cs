using System;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>Preserves the bounded Witcher Mods/DLC scan entry point through the shared configured scanner.</summary>
	public static class Witcher3ComponentDiskScan
	{
		/// <summary>Default directory depth relative to a component folder used by native mutation review.</summary>
		public const int DefaultMaximumDepth = 16;

		/// <summary>Reads detached Mods/DLC observations with the existing limits and Witcher diagnostic model.</summary>
		public static Witcher3ComponentDiskObservation Scan(string physicalGameRoot,
			int maxFiles = 100000, int maxDirectories = 20000, int maxDepth = DefaultMaximumDepth)
		{
			if (String.IsNullOrWhiteSpace(physicalGameRoot) || !Path.IsPathRooted(physicalGameRoot))
				throw new ArgumentException("An absolute game root is required.", nameof(physicalGameRoot));
			ManagedContentComponentDiskObservation scan = ManagedContentComponentDiskScan.Scan(physicalGameRoot,
				Witcher3NativeComponentIndexer.NativeComponentRules, maxFiles, maxDirectories, maxDepth);
			return new Witcher3ComponentDiskObservation(scan.PhysicalRoot, scan.RootRelativeFiles, scan.ComponentFolders,
				scan.IsComplete, scan.Issues.Select(issue => new Witcher3ComponentDiskIssue(
					(Witcher3ComponentDiskIssueKind)issue.Kind, issue.Path, GetMessage(issue))));
		}

		/// <summary>Retains the existing Witcher-specific diagnostic text at the compatibility boundary.</summary>
		private static string GetMessage(ManagedContentComponentDiskIssue issue)
		{
			if (issue.Kind == ManagedContentComponentDiskIssueKind.RootMissing)
				return "The Witcher installation root cannot be enumerated.";
			if (issue.Kind == ManagedContentComponentDiskIssueKind.ReparsePointSkipped)
				return "Skipped junction/symlink; the scan cannot prove its contents are inside the game root.";
			return issue.Message;
		}
	}
}
