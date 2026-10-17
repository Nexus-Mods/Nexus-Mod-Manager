using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement.InstallationLog;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>Game-level components are folders, not NMM archives or Bethesda plugins.</summary>
	public enum Witcher3ComponentKind
	{
		Mod = 0,
		Dlc = 1,
		MergedOutputCandidate = 2
	}

	public enum Witcher3ComponentIssueKind
	{
		NativeCaptureIncomplete = 0,
		PhysicalRootUnavailable = 1,
		TargetOutsideGameRoot = 2,
		InvalidComponentPath = 3,
		UnresolvedNativeOwner = 4,
		AmbiguousVirtualWinner = 5,
		ConflictingNativeTarget = 6,
		UnverifiedVirtualTarget = 7,
		MissingObservedNativeFile = 8,
		UnrecognizedComponentFolder = 9,
		InconsistentComponentCase = 10,
		DiskScanIncomplete = 11
	}

	public sealed class Witcher3ComponentIssue
	{
		internal Witcher3ComponentIssue(Witcher3ComponentIssueKind kind, string path, string message)
		{
			Kind = kind;
			Path = path ?? String.Empty;
			Message = message;
		}

		public Witcher3ComponentIssueKind Kind { get; }
		public string Path { get; }
		public string Message { get; }
	}

	public sealed class Witcher3ComponentNativeOwner
	{
		internal Witcher3ComponentNativeOwner(InstallLogReadMod mod, int fileCount, int winningFileCount)
		{
			NativeModKey = mod.ModKey;
			ModName = mod.ModName;
			ArchivePath = mod.ArchivePath;
			InstallMethod = mod.InstallMethod;
			FileCount = fileCount;
			WinningFileCount = winningFileCount;
		}

		public string NativeModKey { get; }
		public string ModName { get; }
		public string ArchivePath { get; }
		public ModInstallMethod InstallMethod { get; }
		/// <summary>Number of target files whose native histories mention this mod, whether winning or shadowed.</summary>
		public int FileCount { get; }
		/// <summary>Native intended winners, not a verification of the live bytes on disk.</summary>
		public int WinningFileCount { get; }
	}

	public enum Witcher3ComponentFileSource
	{
		LegacyInstallLog = 0,
		VirtualLink = 1,
		PromotedDeployment = 2,
		UnmanagedDiskFile = 3
	}

	public sealed class Witcher3ComponentFile
	{
		internal Witcher3ComponentFile(string gameRelativePath, Witcher3ComponentFileSource source,
			IEnumerable<string> nativeOwnerKeys, string intendedWinnerKey, bool hasOriginalFallback,
			bool winnerAmbiguous, bool? observedOnDisk)
		{
			GameRelativePath = gameRelativePath;
			Source = source;
			NativeOwnerKeys = new ReadOnlyCollection<string>(nativeOwnerKeys.ToList());
			IntendedWinnerKey = intendedWinnerKey;
			HasOriginalFallback = hasOriginalFallback;
			WinnerAmbiguous = winnerAmbiguous;
			ObservedOnDisk = observedOnDisk;
		}

		public string GameRelativePath { get; }
		public Witcher3ComponentFileSource Source { get; }
		/// <summary>Registered native mod keys only; OriginalValues and unresolved keys are excluded.</summary>
		public ReadOnlyCollection<string> NativeOwnerKeys { get; }
		/// <summary>Last effective registered native owner, or null for original/unmanaged/ambiguous state.</summary>
		public string IntendedWinnerKey { get; }
		public bool HasOriginalFallback { get; }
		public bool WinnerAmbiguous { get; }
		/// <summary>Null when the disk was not completely observed. False is meaningful only after a complete scan.</summary>
		public bool? ObservedOnDisk { get; }
	}

	public sealed class Witcher3NativeComponent
	{
		internal Witcher3NativeComponent(string path, Witcher3ComponentKind kind, bool? observedOnDisk,
			IEnumerable<Witcher3ComponentNativeOwner> owners, IEnumerable<Witcher3ComponentFile> files)
		{
			GameRelativeFolderPath = path;
			FolderName = path.Substring(path.IndexOf('\\') + 1);
			Kind = kind;
			ObservedOnDisk = observedOnDisk;
			Owners = new ReadOnlyCollection<Witcher3ComponentNativeOwner>(owners.ToList());
			Files = new ReadOnlyCollection<Witcher3ComponentFile>(files.ToList());
			UnmanagedFileCount = Files.Count(x => x.Source == Witcher3ComponentFileSource.UnmanagedDiskFile);
			HasContentPayload = Files.Any(x => x.GameRelativePath.StartsWith(path + @"\content\", StringComparison.OrdinalIgnoreCase));
		}

		public string GameRelativeFolderPath { get; }
		public string FolderName { get; }
		public Witcher3ComponentKind Kind { get; }
		/// <summary>DLC components are never inserted into Witcher mod-folder load order.</summary>
		public bool IsModLoadOrderCandidate => Kind != Witcher3ComponentKind.Dlc;
		/// <summary>A filename heuristic for generated outputs, NOT proof that the component is safe to lock or uninstall.</summary>
		public bool IsMergeCandidate => Kind == Witcher3ComponentKind.MergedOutputCandidate;
		public bool? ObservedOnDisk { get; }
		public bool HasContentPayload { get; }
		public int UnmanagedFileCount { get; }
		/// <summary>Observed unmanaged files or an empty observed folder; never inferred merely from an unresolved native owner.</summary>
		public bool IsEntirelyExternal => Owners.Count == 0 &&
			Files.All(x => x.Source == Witcher3ComponentFileSource.UnmanagedDiskFile) && ObservedOnDisk == true;
		public bool HasMixedManagedAndUnmanagedFiles => Owners.Count > 0 && UnmanagedFileCount > 0;
		public ReadOnlyCollection<Witcher3ComponentNativeOwner> Owners { get; }
		public ReadOnlyCollection<Witcher3ComponentFile> Files { get; }
	}

	public sealed class Witcher3NativeComponentIndex
	{
		internal Witcher3NativeComponentIndex(long commitSequence, IEnumerable<Witcher3NativeComponent> components,
			IEnumerable<Witcher3ComponentIssue> issues, bool hasCompleteDiskInventory)
		{
			NativeDeploymentCommitSequence = commitSequence;
			Components = new ReadOnlyCollection<Witcher3NativeComponent>(components.ToList());
			Issues = new ReadOnlyCollection<Witcher3ComponentIssue>(issues.ToList());
			HasCompleteDiskInventory = hasCompleteDiskInventory;
		}

		public long NativeDeploymentCommitSequence { get; }
		public ReadOnlyCollection<Witcher3NativeComponent> Components { get; }
		public ReadOnlyCollection<Witcher3ComponentIssue> Issues { get; }
		public bool HasCompleteDiskInventory { get; }
		/// <summary>For diagnostics only. Even a complete index NEVER authorizes deployment or file modification.</summary>
		public bool HasNoReportedIssues => Issues.Count == 0;
	}

	/// <summary>
	/// Read-only projection of *committed native ownership* into game folder components. Promoted InstallLog
	/// stacks win over legacy file records and VMA links; active VMA links win over unpromoted legacy
	/// Virtual history. Physical root equivalence is proved from NativeStateCaptureSnapshot.Roots rather
	/// than guessed from ModInstallRoot. Optional disk observations identify unmanaged files and drift.
	/// This index is not a new ownership store, a deployment authority, or a mod-order writer.
	/// </summary>
	public static class Witcher3NativeComponentIndexer
	{
		private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

		private sealed class MutableFile
		{
			internal string Path;
			internal Witcher3ComponentFileSource Source;
			internal readonly List<string> Owners = new List<string>();
			internal string Winner;
			internal bool WinnerAmbiguous;
			internal bool OriginalFallback;
			internal bool Observed;
		}

		private sealed class MutableComponent
		{
			internal string Path;
			internal Witcher3ComponentKind Kind;
			internal readonly List<MutableFile> Files = new List<MutableFile>();
			internal bool Observed;
		}

		/// <param name="nativeState">One detached committed capture from NMM's native readers, not archive contents.</param>
		/// <param name="physicalGameRoot">Absolute Witcher installation root; no directory is created or accessed here.</param>
		/// <param name="disk">Optional read-only inventory of disk files; null means unobserved, not missing.</param>
		public static Witcher3NativeComponentIndex Build(NativeStateCaptureSnapshot nativeState,
			string physicalGameRoot, Witcher3ComponentDiskObservation disk = null)
		{
			if (nativeState == null) throw new ArgumentNullException(nameof(nativeState));
			if (String.IsNullOrWhiteSpace(physicalGameRoot) || !Path.IsPathRooted(physicalGameRoot))
				throw new ArgumentException("An absolute game root is required.", nameof(physicalGameRoot));
			string gameRoot = NormalizeRoot(physicalGameRoot);
			if (disk != null && !Names.Equals(NormalizeRoot(disk.PhysicalGameRoot), gameRoot))
				throw new ArgumentException("Disk evidence was captured from a different game root.", nameof(disk));
			var issues = new List<Witcher3ComponentIssue>();
			if (nativeState.DeploymentCoverage == NativeStateCaptureCoverage.Unavailable ||
				nativeState.DeploymentCoverage == NativeStateCaptureCoverage.Partial || nativeState.Issues.Count > 0)
				AddIssue(issues, Witcher3ComponentIssueKind.NativeCaptureIncomplete, null,
					"Native capture reports unavailable, partial, or inconsistent state. The index is diagnostic only.");
			var roots = new Dictionary<ModDeploymentRoot, string>();
			foreach (NativeStateCaptureRoot root in nativeState.Roots)
			{
				if (String.IsNullOrWhiteSpace(root.PhysicalPath) || !Path.IsPathRooted(root.PhysicalPath) || roots.ContainsKey(root.Root))
				{
					AddIssue(issues, Witcher3ComponentIssueKind.PhysicalRootUnavailable, root.Root.ToString(),
						"A native deployment root was missing, relative, or duplicated.");
					continue;
				}
				roots.Add(root.Root, NormalizeRoot(root.PhysicalPath));
			}
			var modsByKey = new Dictionary<string, InstallLogReadMod>(Names);
			foreach (InstallLogReadMod mod in nativeState.InstallLog.Mods)
			{
				if (modsByKey.ContainsKey(mod.ModKey))
					AddIssue(issues, Witcher3ComponentIssueKind.UnresolvedNativeOwner, mod.ModKey,
						"Duplicate native mod registration; owner mapping is uncertain.");
				else if (!mod.Hidden)
					modsByKey.Add(mod.ModKey, mod);
			}

			// Two read surfaces describe the same promoted targets. A divergent capture is
			// useful evidence of a race or native inconsistency, never a valid file winner.
			var registeredPromotions = nativeState.InstallLog.DeploymentTargets
				.GroupBy(x => x.Target).ToDictionary(x => x.Key, x => x.First().OwnerKeys);
			if (registeredPromotions.Count > 0 && nativeState.DeploymentCoverage == NativeStateCaptureCoverage.NotApplicable)
				AddIssue(issues, Witcher3ComponentIssueKind.NativeCaptureIncomplete, null,
					"The InstallLog records promoted targets but native deployment coverage says not applicable.");
			if (nativeState.DeploymentCoverage == NativeStateCaptureCoverage.Complete &&
				nativeState.DeploymentTargets.Count != registeredPromotions.Count)
				AddIssue(issues, Witcher3ComponentIssueKind.NativeCaptureIncomplete, null,
					"The deployed-target observation does not cover every promoted InstallLog target.");
			foreach (NativeStateCaptureDeploymentTarget current in nativeState.DeploymentTargets)
			{
				ReadOnlyCollection<string> registered;
				if (!registeredPromotions.TryGetValue(current.Target, out registered) ||
					!registered.SequenceEqual(current.Owners.Select(x => x.OwnerKey), Names))
					AddIssue(issues, Witcher3ComponentIssueKind.ConflictingNativeTarget, current.Target.ToString(),
						"Promoted deployment observation disagrees with committed InstallLog ownership.");
			}
			var files = new Dictionary<string, MutableFile>(Names);
			var components = new Dictionary<string, MutableComponent>(Names);
			var promoted = new HashSet<ModDeploymentTarget>();
			foreach (InstallLogReadDeploymentTarget record in nativeState.InstallLog.DeploymentTargets)
			{
				if (!promoted.Add(record.Target))
				{
					AddIssue(issues, Witcher3ComponentIssueKind.ConflictingNativeTarget, record.Target.ToString(),
						"Duplicate promoted registry target.");
					continue;
				}
				AddNativeFile(record.Target, record.OwnerKeys, record.OwnerKeys.LastOrDefault(),
					Witcher3ComponentFileSource.PromotedDeployment, nativeState.InstallLog.OriginalValuesKey,
					modsByKey, roots, gameRoot, files, components, issues, false);
			}

			// A pure Virtual file exists in the game only while the VMA reports an active link.
			// Inactive links are included as historical owners only when another link is active.
			var activeVirtualTargets = new HashSet<ModDeploymentTarget>();
			foreach (var group in nativeState.VirtualState.Links.GroupBy(x => x.Target))
			{
				if (promoted.Contains(group.Key)) continue;
				var active = group.Where(x => x.Active).ToList();
				if (active.Count == 0) continue;
				activeVirtualTargets.Add(group.Key);
				bool ambiguous = active.Count != 1 || active.Any(x => String.IsNullOrWhiteSpace(x.OwnerKey));
				if (ambiguous)
					AddIssue(issues, Witcher3ComponentIssueKind.AmbiguousVirtualWinner, group.Key.ToString(),
						"The pure Virtual target has multiple or unidentified active links.");
				var owners = group.OrderBy(x => x.Priority).ThenBy(x => x.OwnerKey, Names)
					.Select(x => x.OwnerKey).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(Names).ToList();
				AddNativeFile(group.Key, owners, ambiguous ? null : active[0].OwnerKey,
					Witcher3ComponentFileSource.VirtualLink, nativeState.InstallLog.OriginalValuesKey,
					modsByKey, roots, gameRoot, files, components, issues, ambiguous);
			}

			foreach (var group in nativeState.InstallLog.Files.GroupBy(x => x.Target))
			{
				if (promoted.Contains(group.Key)) continue;
				if (activeVirtualTargets.Contains(group.Key))
				{
					AddIssue(issues, Witcher3ComponentIssueKind.ConflictingNativeTarget, group.Key.ToString(),
						"An unpromoted target has both legacy file history and active Virtual links; VMA wins for display.");
					continue;
				}
				var entries = group.ToList();
				bool duplicate = entries.Count != 1;
				if (duplicate)
					AddIssue(issues, Witcher3ComponentIssueKind.ConflictingNativeTarget, group.Key.ToString(),
						"Multiple legacy file records resolve to the same native target.");
				var ownerKeys = entries.SelectMany(x => x.OwnerKeys).Distinct(Names).ToList();
				string winner = duplicate ? null : entries[0].OwnerKeys.LastOrDefault();
				InstallLogReadMod winningMod;
				if (winner != null && modsByKey.TryGetValue(winner, out winningMod) &&
					winningMod.InstallMethod == ModInstallMethod.Virtual)
				{
					AddIssue(issues, Witcher3ComponentIssueKind.UnverifiedVirtualTarget, group.Key.ToString(),
						"Virtual appears as a legacy file winner but has no active VMA link; do not call it deployed.");
					continue;
				}
				AddNativeFile(group.Key, ownerKeys, winner, Witcher3ComponentFileSource.LegacyInstallLog,
					nativeState.InstallLog.OriginalValuesKey, modsByKey, roots, gameRoot,
					files, components, issues, duplicate);
			}

			int issuesBeforeDisk = issues.Count;
			if (disk != null)
			{
					if (!disk.IsComplete)
						AddIssue(issues, Witcher3ComponentIssueKind.DiskScanIncomplete, null,
							"Disk enumeration is incomplete; absent files and folders cannot be inferred.");
					foreach (string folder in disk.ComponentFolders)
					{
						string path; Witcher3ComponentKind kind;
						if (TryComponent(folder, true, out path, out kind, issues))
							GetComponent(path, kind, components, issues).Observed = true;
					}
					foreach (string entry in disk.GameRelativeFiles)
					{
						string componentPath; Witcher3ComponentKind kind;
						string observedPath = NormalizeRelative(entry);
						if (!TryComponent(entry, false, out componentPath, out kind, issues)) continue;
						MutableComponent component = GetComponent(componentPath, kind, components, issues);
						component.Observed = true;
						MutableFile existing;
						if (files.TryGetValue(observedPath, out existing)) existing.Observed = true;
						else
						{
							var unmanaged = new MutableFile
							{
								Path = observedPath,
								Source = Witcher3ComponentFileSource.UnmanagedDiskFile,
								Observed = true
							};
							files.Add(unmanaged.Path, unmanaged);
							component.Files.Add(unmanaged);
						}
					}
			}

			// Caller-supplied observations may be malformed even when marked complete. Never
			// infer a missing native file from an invalid or contradictory disk listing.
			bool fullDisk = disk != null && disk.IsComplete && !issues.Skip(issuesBeforeDisk).Any(x =>
				x.Kind == Witcher3ComponentIssueKind.InvalidComponentPath ||
				x.Kind == Witcher3ComponentIssueKind.UnrecognizedComponentFolder ||
				x.Kind == Witcher3ComponentIssueKind.InconsistentComponentCase);
			var result = new List<Witcher3NativeComponent>();
			foreach (MutableComponent component in components.Values.OrderBy(x => x.Path, Names).ThenBy(x => x.Path, StringComparer.Ordinal))
			{
				var records = component.Files.OrderBy(x => x.Path, Names).ThenBy(x => x.Path, StringComparer.Ordinal).ToList();
				var counts = new Dictionary<string, int[]>(Names);
				foreach (MutableFile file in records)
				{
					foreach (string key in file.Owners)
					{
						int[] count;
						if (!counts.TryGetValue(key, out count)) counts.Add(key, count = new int[2]);
						count[0]++;
						if (Names.Equals(file.Winner, key)) count[1]++;
					}
				}
				var owners = counts.OrderBy(x => x.Key, Names).ThenBy(x => x.Key, StringComparer.Ordinal)
					.Select(x => new Witcher3ComponentNativeOwner(modsByKey[x.Key], x.Value[0], x.Value[1])).ToList();
				var publicFiles = records.Select(record => new Witcher3ComponentFile(record.Path, record.Source,
					record.Owners, record.Winner, record.OriginalFallback, record.WinnerAmbiguous,
					record.Observed ? (bool?)true : fullDisk ? (bool?)false : null));
				result.Add(new Witcher3NativeComponent(component.Path, component.Kind,
					component.Observed ? (bool?)true : fullDisk ? (bool?)false : null,
					owners, publicFiles));
			}
			if (fullDisk)
				foreach (MutableFile missing in files.Values.Where(x => x.Source != Witcher3ComponentFileSource.UnmanagedDiskFile && !x.Observed))
					AddIssue(issues, Witcher3ComponentIssueKind.MissingObservedNativeFile, missing.Path,
						"Native state names a target that is absent from a complete disk inventory.");

			return new Witcher3NativeComponentIndex(nativeState.InstallLog.DeploymentCommitSequence,
					result, issues, fullDisk);
		}

		private static void AddNativeFile(ModDeploymentTarget target, IEnumerable<string> ownerKeys,
			string winner, Witcher3ComponentFileSource source, string originalKey,
			IDictionary<string, InstallLogReadMod> mods, IDictionary<ModDeploymentRoot, string> roots,
			string gameRoot, IDictionary<string, MutableFile> files,
			IDictionary<string, MutableComponent> components, List<Witcher3ComponentIssue> issues, bool ambiguous)
		{
			string physicalRoot;
			if (!roots.TryGetValue(target.Root, out physicalRoot))
			{
				AddIssue(issues, Witcher3ComponentIssueKind.PhysicalRootUnavailable, target.ToString(),
					"No proven physical root exists for this deployment target.");
				return;
			}
			string relative;
			if (!TryProjectPath(gameRoot, physicalRoot, target.RelativePath, out relative))
			{
				AddIssue(issues, Witcher3ComponentIssueKind.TargetOutsideGameRoot, target.ToString(),
					"Native target is outside the requested Witcher game directory.");
				return;
			}
			string componentPath; Witcher3ComponentKind kind;
			if (!TryComponent(relative, false, out componentPath, out kind, issues)) return;
			MutableComponent component = GetComponent(componentPath, kind, components, issues);
			var managed = new List<string>();
			bool original = false;
			foreach (string key in ownerKeys)
			{
				if (Names.Equals(key, originalKey)) { original = true; continue; }
				if (!mods.ContainsKey(key))
				{
					AddIssue(issues, Witcher3ComponentIssueKind.UnresolvedNativeOwner, relative,
						"Native owner registration is missing or hidden: " + key);
					continue;
				}
				if (!managed.Contains(key, Names)) managed.Add(key);
			}
			string managedWinner = winner != null && mods.ContainsKey(winner) ? winner : null;
			if (winner != null && !Names.Equals(winner, originalKey) && managedWinner == null)
				ambiguous = true;
			MutableFile existing;
			if (files.TryGetValue(relative, out existing))
			{
					// Two different native roots can alias the same physical path. Do not select a winner.
					AddIssue(issues, Witcher3ComponentIssueKind.ConflictingNativeTarget, relative,
						"More than one native target addresses the same physical Witcher file.");
					foreach (string key in managed)
						if (!existing.Owners.Contains(key, Names)) existing.Owners.Add(key);
					existing.Winner = null;
					existing.WinnerAmbiguous = true;
					existing.OriginalFallback |= original;
					return;
			}
			var file = new MutableFile
			{
				Path = relative,
				Source = source,
				Winner = managedWinner,
				WinnerAmbiguous = ambiguous,
				OriginalFallback = original
			};
			file.Owners.AddRange(managed);
			files.Add(relative, file);
			component.Files.Add(file);
		}

		private static MutableComponent GetComponent(string path, Witcher3ComponentKind kind,
			IDictionary<string, MutableComponent> components, List<Witcher3ComponentIssue> issues)
		{
			MutableComponent existing;
			if (components.TryGetValue(path, out existing))
			{
				if (!String.Equals(existing.Path, path, StringComparison.Ordinal))
					AddIssue(issues, Witcher3ComponentIssueKind.InconsistentComponentCase, path,
						"Different component spellings alias the same Windows folder.");
				return existing;
			}
			existing = new MutableComponent { Path = path, Kind = kind };
			components.Add(path, existing);
			return existing;
		}

		/// <summary>Maps an already canonical native target into a game-relative path without filesystem writes.</summary>
		private static bool TryProjectPath(string gameRoot, string physicalRoot, string relative, out string gameRelative)
		{
			gameRelative = null;
			try
			{
				string target = Path.GetFullPath(Path.Combine(physicalRoot,
					(relative ?? String.Empty).Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
				string prefix = gameRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
					? gameRoot : gameRoot + Path.DirectorySeparatorChar;
				if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
				string normalized = NormalizeRelative(target.Substring(prefix.Length));
				if (normalized == null) return false;
				gameRelative = normalized;
				return true;
			}
			catch (Exception exception) when (exception is ArgumentException || exception is IOException ||
				exception is NotSupportedException || exception is PathTooLongException)
			{
				return false;
			}
		}

		private static string NormalizeRoot(string path)
		{
			string full = Path.GetFullPath(path);
			string root = Path.GetPathRoot(full);
			return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
		}

		private static string NormalizeRelative(string path)
		{
			if (String.IsNullOrWhiteSpace(path)) return null;
			string normalized = path.Replace('/', '\\');
			if (normalized.StartsWith("\\", StringComparison.Ordinal) || normalized.IndexOf(':') >= 0) return null;
			string[] parts = normalized.Split('\\');
			if (parts.Any(x => x.Length == 0 || x == "." || x == ".." || x.EndsWith(".", StringComparison.Ordinal) ||
				x.EndsWith(" ", StringComparison.Ordinal))) return null;
			return String.Join("\\", parts);
		}

		private static bool TryComponent(string path, bool folder, out string componentPath,
			out Witcher3ComponentKind kind, List<Witcher3ComponentIssue> issues)
		{
			componentPath = null;
			kind = Witcher3ComponentKind.Mod;
			string rel = NormalizeRelative(path);
			if (rel == null)
			{
				AddIssue(issues, Witcher3ComponentIssueKind.InvalidComponentPath, path,
					"A component observation contains a non-canonical path.");
				return false;
			}
			string[] segments = rel.Split('\\');
			bool mod = Names.Equals(segments[0], "Mods");
			if (!mod && !Names.Equals(segments[0], "DLC")) return false;
			if (segments.Length < (folder ? 2 : 3) || (folder && segments.Length != 2))
			{
				AddIssue(issues, Witcher3ComponentIssueKind.InvalidComponentPath, rel,
					"A component path lacks its required folder/file segments.");
				return false;
			}
			string prefix = mod ? "mod" : "dlc";
			if (!segments[1].StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || segments[1].Length <= prefix.Length)
			{
				AddIssue(issues, Witcher3ComponentIssueKind.UnrecognizedComponentFolder, rel,
					"A folder under Mods/DLC has no recognized mod*/dlc* identity.");
				return false;
			}
			componentPath = (mod ? "Mods" : "DLC") + "\\" + segments[1];
			kind = mod && (Names.Equals(segments[1], "mod0000_MergedFiles") ||
				Names.Equals(segments[1], "mod0000____CompilationTrigger"))
				? Witcher3ComponentKind.MergedOutputCandidate
				: mod ? Witcher3ComponentKind.Mod : Witcher3ComponentKind.Dlc;
			return true;
		}

		private static void AddIssue(ICollection<Witcher3ComponentIssue> issues, Witcher3ComponentIssueKind kind,
			string path, string message)
		{
			issues.Add(new Witcher3ComponentIssue(kind, path, message));
		}
	}
}
