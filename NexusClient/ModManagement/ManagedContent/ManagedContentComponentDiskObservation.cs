using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.Games.ManagedContent;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Reasons a bounded disk scan could not provide complete component evidence.</summary>
	public enum ManagedContentComponentDiskIssueKind
	{
		RootMissing = 0,
		EnumerationFailed = 1,
		ReparsePointSkipped = 2,
		ScanLimitReached = 3
	}

	/// <summary>One immutable disk enumeration diagnostic; it never conveys ownership.</summary>
	public sealed class ManagedContentComponentDiskIssue
	{
		/// <summary>Captures an enumeration limitation without repairing or changing the target.</summary>
		public ManagedContentComponentDiskIssue(ManagedContentComponentDiskIssueKind kind, string path, string message)
		{
			if (!Enum.IsDefined(typeof(ManagedContentComponentDiskIssueKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			Path = path ?? String.Empty;
			Message = message ?? String.Empty;
		}

		public ManagedContentComponentDiskIssueKind Kind { get; }
		public string Path { get; }
		public string Message { get; }
	}

	/// <summary>Detached optional evidence limited to configured component folders; incomplete observations cannot prove absence.</summary>
	public sealed class ManagedContentComponentDiskObservation
	{
		/// <summary>Captures caller-supplied paths without reading directories, following links or claiming native ownership.</summary>
		public ManagedContentComponentDiskObservation(string physicalRoot, IEnumerable<string> rootRelativeFiles,
			IEnumerable<string> componentFolders, bool isComplete)
			: this(physicalRoot, rootRelativeFiles, componentFolders, isComplete, null, null)
		{
		}

		/// <summary>Freezes scan diagnostics and the optional explicit target-folder coverage supplied by a scanner.</summary>
		public ManagedContentComponentDiskObservation(string physicalRoot, IEnumerable<string> rootRelativeFiles,
			IEnumerable<string> componentFolders, bool isComplete, IEnumerable<ManagedContentComponentDiskIssue> issues,
			IEnumerable<string> targetFolders = null)
		{
			if (String.IsNullOrWhiteSpace(physicalRoot) || !Path.IsPathRooted(physicalRoot))
				throw new ArgumentException("An absolute component root is required.", nameof(physicalRoot));
			PhysicalRoot = Path.GetFullPath(physicalRoot);
			RootRelativeFiles = Copy(rootRelativeFiles, nameof(rootRelativeFiles));
			ComponentFolders = Copy(componentFolders, nameof(componentFolders));
			Issues = new ReadOnlyCollection<ManagedContentComponentDiskIssue>((issues ?? Enumerable.Empty<ManagedContentComponentDiskIssue>()).ToList());
			if (Issues.Any(issue => issue == null)) throw new ArgumentException("Disk issues cannot be null.", nameof(issues));
			if (isComplete && Issues.Count > 0) throw new ArgumentException("A scan with reported failures cannot be marked complete.", nameof(isComplete));
			if (targetFolders != null)
			{
				List<string> scope = targetFolders.Take(33).ToList();
				if (scope.Count == 0 || scope.Count > 32) throw new ArgumentException("Scan coverage requires 1 to 32 literal target folders.", nameof(targetFolders));
				foreach (string folder in scope)
				{
					string[] parts;
					if (!ManagedContentPath.TryParse(folder, out parts) || parts.Length != 1)
						throw new ArgumentException("Scan coverage requires safe literal target-folder segments.", nameof(targetFolders));
				}
				TargetFolders = new ReadOnlyCollection<string>(scope.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
			}
			IsComplete = isComplete;
		}

		public string PhysicalRoot { get; }
		public ReadOnlyCollection<string> RootRelativeFiles { get; }
		public ReadOnlyCollection<string> ComponentFolders { get; }
		public bool IsComplete { get; }
		public ReadOnlyCollection<ManagedContentComponentDiskIssue> Issues { get; }
		/// <summary>Scanned target folders, or null for caller-supplied legacy evidence whose scope is the caller's responsibility.</summary>
		public ReadOnlyCollection<string> TargetFolders { get; }

		/// <summary>Freezes supplied evidence; path interpretation and diagnostics belong to the indexer.</summary>
		private static ReadOnlyCollection<string> Copy(IEnumerable<string> values, string parameter)
		{
			if (values == null) throw new ArgumentNullException(parameter);
			List<string> copy = values.ToList();
			if (copy.Any(String.IsNullOrWhiteSpace)) throw new ArgumentException("Observed paths cannot be empty.", parameter);
			return new ReadOnlyCollection<string>(copy);
		}
	}
}
