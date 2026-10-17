using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Nexus.Client.ModManagement.Witcher3
{
	public enum Witcher3ComponentDiskIssueKind
	{
		GameRootMissing = 0,
		EnumerationFailed = 1,
		ReparsePointSkipped = 2,
		ScanLimitReached = 3
	}

	public sealed class Witcher3ComponentDiskIssue
	{
		internal Witcher3ComponentDiskIssue(Witcher3ComponentDiskIssueKind kind, string path, string message)
		{
			Kind = kind;
			Path = path ?? String.Empty;
			Message = message;
		}

		public Witcher3ComponentDiskIssueKind Kind { get; }
		public string Path { get; }
		public string Message { get; }
	}

	/// <summary>
	/// Optional, detached disk evidence. This can be constructed from a test fixture or produced by Scan.
	/// IsComplete=false prohibits interpreting an omitted file/folder as absent.
	/// </summary>
	public sealed class Witcher3ComponentDiskObservation
	{
		public Witcher3ComponentDiskObservation(string physicalGameRoot, IEnumerable<string> gameRelativeFiles,
			IEnumerable<string> componentFolders, bool isComplete, IEnumerable<Witcher3ComponentDiskIssue> issues = null)
		{
			if (String.IsNullOrWhiteSpace(physicalGameRoot) || !Path.IsPathRooted(physicalGameRoot))
				throw new ArgumentException("An absolute game root is required.", nameof(physicalGameRoot));
			PhysicalGameRoot = Path.GetFullPath(physicalGameRoot);
			if (gameRelativeFiles == null) throw new ArgumentNullException(nameof(gameRelativeFiles));
			if (componentFolders == null) throw new ArgumentNullException(nameof(componentFolders));
			GameRelativeFiles = Copy(gameRelativeFiles, nameof(gameRelativeFiles));
			ComponentFolders = Copy(componentFolders, nameof(componentFolders));
			Issues = new ReadOnlyCollection<Witcher3ComponentDiskIssue>((issues ?? new Witcher3ComponentDiskIssue[0]).ToList());
			if (Issues.Any(x => x == null)) throw new ArgumentException("Disk issue cannot be null.", nameof(issues));
			if (isComplete && Issues.Count > 0)
				throw new ArgumentException("A scan with reported failures cannot be marked complete.", nameof(isComplete));
			IsComplete = isComplete;
		}

		public string PhysicalGameRoot { get; }
		public ReadOnlyCollection<string> GameRelativeFiles { get; }
		public ReadOnlyCollection<string> ComponentFolders { get; }
		public ReadOnlyCollection<Witcher3ComponentDiskIssue> Issues { get; }
		public bool IsComplete { get; }

		private static ReadOnlyCollection<string> Copy(IEnumerable<string> values, string parameter)
		{
			var copied = values.ToList();
			if (copied.Any(String.IsNullOrWhiteSpace)) throw new ArgumentException("Disk paths cannot be empty.", parameter);
			return new ReadOnlyCollection<string>(copied);
		}
	}

}
