using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Games;
using Nexus.Client.Games.ManagedContent;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Limitations observed while projecting native files into configured component folders.</summary>
	public enum ManagedContentComponentIssueKind
	{
		NativeCaptureIncomplete = 0,
		PhysicalRootUnavailable = 1,
		TargetOutsideComponentRoot = 2,
		InvalidComponentPath = 3,
		UnresolvedNativeOwner = 4,
		AmbiguousVirtualWinner = 5,
		ConflictingNativeTarget = 6,
		UnverifiedVirtualTarget = 7,
		MissingObservedNativeFile = 8,
		UnrecognizedComponentFolder = 9,
		InconsistentComponentCase = 10,
		DiskScanIncomplete = 11,
		AmbiguousComponentRule = 12
	}

	/// <summary>One immutable diagnostic about native ownership or optional disk evidence.</summary>
	public sealed class ManagedContentComponentIssue
	{
		/// <summary>Captures a diagnostic without granting any authority over its target.</summary>
		internal ManagedContentComponentIssue(ManagedContentComponentIssueKind kind, string path, string message)
		{
			Kind = kind;
			Path = path ?? String.Empty;
			Message = message;
		}

		public ManagedContentComponentIssueKind Kind { get; }
		public string Path { get; }
		public string Message { get; }
	}

	/// <summary>One native archive contributor and its intended winning-file counts within a component.</summary>
	public sealed class ManagedContentComponentNativeOwner
	{
		/// <summary>Copies the existing native mod registration and derived per-component file counts.</summary>
		internal ManagedContentComponentNativeOwner(InstallLogReadMod mod, int fileCount, int winningFileCount)
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

	/// <summary>The authoritative native surface, or external disk evidence, from which a file was observed.</summary>
	public enum ManagedContentComponentFileSource
	{
		LegacyInstallLog = 0,
		VirtualLink = 1,
		PromotedDeployment = 2,
		UnmanagedDiskFile = 3
	}

	/// <summary>One detached component file with native contributor provenance and optional disk presence.</summary>
	public sealed class ManagedContentComponentFile
	{
		/// <summary>Freezes native contributor keys, intended winner and observational state for one file.</summary>
		internal ManagedContentComponentFile(string rootRelativePath, ManagedContentComponentFileSource source,
			IEnumerable<string> nativeOwnerKeys, string intendedWinnerKey, bool hasOriginalFallback,
			bool winnerAmbiguous, bool? observedOnDisk)
		{
			RootRelativePath = rootRelativePath;
			Source = source;
			NativeOwnerKeys = new ReadOnlyCollection<string>(nativeOwnerKeys.ToList());
			IntendedWinnerKey = intendedWinnerKey;
			HasOriginalFallback = hasOriginalFallback;
			WinnerAmbiguous = winnerAmbiguous;
			ObservedOnDisk = observedOnDisk;
		}

		public string RootRelativePath { get; }
		public ManagedContentComponentFileSource Source { get; }
		/// <summary>Registered native mod keys only; OriginalValues and unresolved keys are excluded.</summary>
		public ReadOnlyCollection<string> NativeOwnerKeys { get; }
		/// <summary>Last effective registered native owner, or null for original/unmanaged/ambiguous state.</summary>
		public string IntendedWinnerKey { get; }
		public bool HasOriginalFallback { get; }
		public bool WinnerAmbiguous { get; }
		/// <summary>Null when the disk was not completely observed. False is meaningful only after a complete scan.</summary>
		public bool? ObservedOnDisk { get; }
	}

	/// <summary>A configured folder component with native contributors; folder identity never replaces file ownership.</summary>
	public sealed class ManagedContentNativeComponent
	{
		/// <summary>Aggregates native files and observed content using the configured target-side rule.</summary>
		internal ManagedContentNativeComponent(string path, ManagedContentLayoutRule rule, bool? observedOnDisk,
			IEnumerable<ManagedContentComponentNativeOwner> owners, IEnumerable<ManagedContentComponentFile> files)
		{
			RootRelativeFolderPath = path;
			FolderName = path.Substring(path.IndexOf('\\') + 1);
			ComponentKind = rule.ComponentKind;
			ObservedOnDisk = observedOnDisk;
			Owners = new ReadOnlyCollection<ManagedContentComponentNativeOwner>(owners.ToList());
			Files = new ReadOnlyCollection<ManagedContentComponentFile>(files.ToList());
			UnmanagedFileCount = Files.Count(x => x.Source == ManagedContentComponentFileSource.UnmanagedDiskFile);
			HasContentPayload = Files.Any(x => x.RootRelativePath.StartsWith(path + "\\" + rule.ContentFolder + "\\", StringComparison.OrdinalIgnoreCase));
		}

		public string RootRelativeFolderPath { get; }
		public string FolderName { get; }
		public string ComponentKind { get; }
		public bool? ObservedOnDisk { get; }
		public bool HasContentPayload { get; }
		public int UnmanagedFileCount { get; }
		/// <summary>Observed unmanaged files or an empty observed folder; never inferred merely from an unresolved native owner.</summary>
		public bool IsEntirelyExternal => Owners.Count == 0 &&
			Files.All(x => x.Source == ManagedContentComponentFileSource.UnmanagedDiskFile) && ObservedOnDisk == true;
		public bool HasMixedManagedAndUnmanagedFiles => Owners.Count > 0 && UnmanagedFileCount > 0;
		public ReadOnlyCollection<ManagedContentComponentNativeOwner> Owners { get; }
		public ReadOnlyCollection<ManagedContentComponentFile> Files { get; }
	}

	/// <summary>Detached component inventory derived from a committed native capture.</summary>
	public sealed class ManagedContentNativeComponentIndex
	{
		/// <summary>Freezes components and diagnostics at the captured native deployment sequence.</summary>
		internal ManagedContentNativeComponentIndex(long commitSequence, IEnumerable<ManagedContentNativeComponent> components,
			IEnumerable<ManagedContentComponentIssue> issues, bool hasCompleteDiskInventory)
		{
			NativeDeploymentCommitSequence = commitSequence;
			Components = new ReadOnlyCollection<ManagedContentNativeComponent>(components.ToList());
			Issues = new ReadOnlyCollection<ManagedContentComponentIssue>(issues.ToList());
			HasCompleteDiskInventory = hasCompleteDiskInventory;
		}

		public long NativeDeploymentCommitSequence { get; }
		public ReadOnlyCollection<ManagedContentNativeComponent> Components { get; }
		public ReadOnlyCollection<ManagedContentComponentIssue> Issues { get; }
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
	public static class ManagedContentNativeComponentIndexer
	{
		private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

		/// <summary>Accumulates native provenance for one physical component file during projection.</summary>
		private sealed class MutableFile
		{
			internal string Path;
			internal ManagedContentComponentFileSource Source;
			internal readonly List<string> Owners = new List<string>();
			internal string Winner;
			internal bool WinnerAmbiguous;
			internal bool OriginalFallback;
			internal bool Observed;
		}

		/// <summary>Accumulates files for one configured component during projection.</summary>
		private sealed class MutableComponent
		{
			internal string Path;
			internal ManagedContentLayoutRule Rule;
			internal readonly List<MutableFile> Files = new List<MutableFile>();
			internal bool Observed;
		}

		/// <summary>Reads configured components from detached native authority without accessing disk or changing game state.</summary>
		public static ManagedContentNativeComponentIndex Build(NativeStateCaptureSnapshot nativeState,
			IGameMode gameMode, ManagedContentComponentDiskObservation disk = null)
		{
			if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));
			IManagedContentComponentProvider provider = gameMode as IManagedContentComponentProvider;
			if (provider == null) throw new ArgumentException("The game mode does not expose configured component rules.", nameof(gameMode));
			return Build(nativeState, ModDeploymentTargetResolver.GetPhysicalRootPath(gameMode, ModDeploymentRoot.Data), provider.ComponentRules, disk);
		}

		/// <summary>Projects committed native file histories into configured folder components and optional disk observations.</summary>
		/// <param name="nativeState">One detached committed capture from NMM's native readers, not archive contents.</param>
		/// <param name="physicalGameRoot">Absolute configured native root; no directory is created or accessed here.</param>
		/// <param name="componentRules">Target-side identity rules, independent of archive source shapes.</param>
		/// <param name="disk">Optional read-only inventory of disk files; null means unobserved, not missing.</param>
		public static ManagedContentNativeComponentIndex Build(NativeStateCaptureSnapshot nativeState,
			string physicalGameRoot, IEnumerable<ManagedContentLayoutRule> componentRules, ManagedContentComponentDiskObservation disk = null)
		{
			if (nativeState == null) throw new ArgumentNullException(nameof(nativeState));
			if (componentRules == null) throw new ArgumentNullException(nameof(componentRules));
			ManagedContentLayoutRule[] rules = componentRules.Take(33).ToArray();
			if (rules.Length == 0 || rules.Length > 32 || rules.Any(rule => rule == null))
				throw new ArgumentException("Component inventory requires 1 to 32 configured layout rules.", nameof(componentRules));
			if (String.IsNullOrWhiteSpace(physicalGameRoot) || !Path.IsPathRooted(physicalGameRoot))
				throw new ArgumentException("An absolute game root is required.", nameof(physicalGameRoot));
			string gameRoot = NormalizeRoot(physicalGameRoot);
			if (disk != null && !Names.Equals(NormalizeRoot(disk.PhysicalRoot), gameRoot))
				throw new ArgumentException("Disk evidence was captured from a different game root.", nameof(disk));
			var issues = new List<ManagedContentComponentIssue>();
			if (nativeState.DeploymentCoverage == NativeStateCaptureCoverage.Unavailable ||
				nativeState.DeploymentCoverage == NativeStateCaptureCoverage.Partial || nativeState.Issues.Count > 0)
				AddIssue(issues, ManagedContentComponentIssueKind.NativeCaptureIncomplete, null,
					"Native capture reports unavailable, partial, or inconsistent state. The index is diagnostic only.");
			var roots = new Dictionary<ModDeploymentRoot, string>();
			foreach (NativeStateCaptureRoot root in nativeState.Roots)
			{
				if (String.IsNullOrWhiteSpace(root.PhysicalPath) || !Path.IsPathRooted(root.PhysicalPath) || roots.ContainsKey(root.Root))
				{
					AddIssue(issues, ManagedContentComponentIssueKind.PhysicalRootUnavailable, root.Root.ToString(),
						"A native deployment root was missing, relative, or duplicated.");
					continue;
				}
				roots.Add(root.Root, NormalizeRoot(root.PhysicalPath));
			}
			var modsByKey = new Dictionary<string, InstallLogReadMod>(Names);
			foreach (InstallLogReadMod mod in nativeState.InstallLog.Mods)
			{
				if (modsByKey.ContainsKey(mod.ModKey))
					AddIssue(issues, ManagedContentComponentIssueKind.UnresolvedNativeOwner, mod.ModKey,
						"Duplicate native mod registration; owner mapping is uncertain.");
				else if (!mod.Hidden)
					modsByKey.Add(mod.ModKey, mod);
			}

			// Two read surfaces describe the same promoted targets. A divergent capture is
			// useful evidence of a race or native inconsistency, never a valid file winner.
			var registeredPromotions = nativeState.InstallLog.DeploymentTargets
				.GroupBy(x => x.Target).ToDictionary(x => x.Key, x => x.First().OwnerKeys);
			if (registeredPromotions.Count > 0 && nativeState.DeploymentCoverage == NativeStateCaptureCoverage.NotApplicable)
				AddIssue(issues, ManagedContentComponentIssueKind.NativeCaptureIncomplete, null,
					"The InstallLog records promoted targets but native deployment coverage says not applicable.");
			if (nativeState.DeploymentCoverage == NativeStateCaptureCoverage.Complete &&
				nativeState.DeploymentTargets.Count != registeredPromotions.Count)
				AddIssue(issues, ManagedContentComponentIssueKind.NativeCaptureIncomplete, null,
					"The deployed-target observation does not cover every promoted InstallLog target.");
			foreach (NativeStateCaptureDeploymentTarget current in nativeState.DeploymentTargets)
			{
				ReadOnlyCollection<string> registered;
				if (!registeredPromotions.TryGetValue(current.Target, out registered) ||
					!registered.SequenceEqual(current.Owners.Select(x => x.OwnerKey), Names))
					AddIssue(issues, ManagedContentComponentIssueKind.ConflictingNativeTarget, current.Target.ToString(),
						"Promoted deployment observation disagrees with committed InstallLog ownership.");
			}
			var files = new Dictionary<string, MutableFile>(Names);
			var components = new Dictionary<string, MutableComponent>(Names);
			var promoted = new HashSet<ModDeploymentTarget>();
			foreach (InstallLogReadDeploymentTarget record in nativeState.InstallLog.DeploymentTargets)
			{
				if (!promoted.Add(record.Target))
				{
					AddIssue(issues, ManagedContentComponentIssueKind.ConflictingNativeTarget, record.Target.ToString(),
						"Duplicate promoted registry target.");
					continue;
				}
				AddNativeFile(record.Target, record.OwnerKeys, record.OwnerKeys.LastOrDefault(),
					ManagedContentComponentFileSource.PromotedDeployment, nativeState.InstallLog.OriginalValuesKey,
					modsByKey, roots, gameRoot, files, components, issues, false, rules);
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
					AddIssue(issues, ManagedContentComponentIssueKind.AmbiguousVirtualWinner, group.Key.ToString(),
						"The pure Virtual target has multiple or unidentified active links.");
				var owners = group.OrderBy(x => x.Priority).ThenBy(x => x.OwnerKey, Names)
					.Select(x => x.OwnerKey).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(Names).ToList();
				AddNativeFile(group.Key, owners, ambiguous ? null : active[0].OwnerKey,
					ManagedContentComponentFileSource.VirtualLink, nativeState.InstallLog.OriginalValuesKey,
					modsByKey, roots, gameRoot, files, components, issues, ambiguous, rules);
			}

			foreach (var group in nativeState.InstallLog.Files.GroupBy(x => x.Target))
			{
				if (promoted.Contains(group.Key)) continue;
				if (activeVirtualTargets.Contains(group.Key))
				{
					AddIssue(issues, ManagedContentComponentIssueKind.ConflictingNativeTarget, group.Key.ToString(),
						"An unpromoted target has both legacy file history and active Virtual links; VMA wins for display.");
					continue;
				}
				var entries = group.ToList();
				bool duplicate = entries.Count != 1;
				if (duplicate)
					AddIssue(issues, ManagedContentComponentIssueKind.ConflictingNativeTarget, group.Key.ToString(),
						"Multiple legacy file records resolve to the same native target.");
				var ownerKeys = entries.SelectMany(x => x.OwnerKeys).Distinct(Names).ToList();
				string winner = duplicate ? null : entries[0].OwnerKeys.LastOrDefault();
				InstallLogReadMod winningMod;
				if (winner != null && modsByKey.TryGetValue(winner, out winningMod) &&
					winningMod.InstallMethod == ModInstallMethod.Virtual)
				{
					AddIssue(issues, ManagedContentComponentIssueKind.UnverifiedVirtualTarget, group.Key.ToString(),
						"Virtual appears as a legacy file winner but has no active VMA link; do not call it deployed.");
					continue;
				}
				AddNativeFile(group.Key, ownerKeys, winner, ManagedContentComponentFileSource.LegacyInstallLog,
					nativeState.InstallLog.OriginalValuesKey, modsByKey, roots, gameRoot,
					files, components, issues, duplicate, rules);
			}

			bool coversScope = disk == null || disk.TargetFolders == null ||
				new HashSet<string>(disk.TargetFolders, Names).IsSupersetOf(rules.Select(rule => rule.TargetFolder));
			int issuesBeforeDisk = issues.Count;
			if (disk != null)
			{
					if (!disk.IsComplete || !coversScope)
						AddIssue(issues, ManagedContentComponentIssueKind.DiskScanIncomplete, null,
							coversScope ? "Disk enumeration is incomplete; absent files and folders cannot be inferred." :
							"Disk enumeration does not cover every configured target folder; absent files and folders cannot be inferred.");
					foreach (string folder in disk.ComponentFolders)
					{
						string path; ManagedContentLayoutRule rule;
						if (TryComponent(folder, true, out path, out rule, issues, rules))
							GetComponent(path, rule, components, issues).Observed = true;
					}
					foreach (string entry in disk.RootRelativeFiles)
					{
						string componentPath; ManagedContentLayoutRule rule;
						string observedPath = NormalizeRelative(entry);
						if (!TryComponent(entry, false, out componentPath, out rule, issues, rules)) continue;
						MutableComponent component = GetComponent(componentPath, rule, components, issues);
						component.Observed = true;
						MutableFile existing;
						if (files.TryGetValue(observedPath, out existing)) existing.Observed = true;
						else
						{
							var unmanaged = new MutableFile
							{
								Path = observedPath,
								Source = ManagedContentComponentFileSource.UnmanagedDiskFile,
								Observed = true
							};
							files.Add(unmanaged.Path, unmanaged);
							component.Files.Add(unmanaged);
						}
					}
			}

			// Caller-supplied observations may be malformed even when marked complete. Never
			// infer a missing native file from an invalid or contradictory disk listing.
			bool fullDisk = disk != null && disk.IsComplete && coversScope && !issues.Skip(issuesBeforeDisk).Any(x =>
				x.Kind == ManagedContentComponentIssueKind.InvalidComponentPath ||
				x.Kind == ManagedContentComponentIssueKind.UnrecognizedComponentFolder ||
				x.Kind == ManagedContentComponentIssueKind.InconsistentComponentCase ||
				x.Kind == ManagedContentComponentIssueKind.AmbiguousComponentRule);
			var result = new List<ManagedContentNativeComponent>();
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
					.Select(x => new ManagedContentComponentNativeOwner(modsByKey[x.Key], x.Value[0], x.Value[1])).ToList();
				var publicFiles = records.Select(record => new ManagedContentComponentFile(record.Path, record.Source,
					record.Owners, record.Winner, record.OriginalFallback, record.WinnerAmbiguous,
					record.Observed ? (bool?)true : fullDisk ? (bool?)false : null));
				result.Add(new ManagedContentNativeComponent(component.Path, component.Rule,
					component.Observed ? (bool?)true : fullDisk ? (bool?)false : null,
					owners, publicFiles));
			}
			if (fullDisk)
				foreach (MutableFile missing in files.Values.Where(x => x.Source != ManagedContentComponentFileSource.UnmanagedDiskFile && !x.Observed))
					AddIssue(issues, ManagedContentComponentIssueKind.MissingObservedNativeFile, missing.Path,
						"Native state names a target that is absent from a complete disk inventory.");

			return new ManagedContentNativeComponentIndex(nativeState.InstallLog.DeploymentCommitSequence,
					result, issues, fullDisk);
		}

		/// <summary>Reconciles a native file history with its physical root and configured component identity.</summary>
		private static void AddNativeFile(ModDeploymentTarget target, IEnumerable<string> ownerKeys,
			string winner, ManagedContentComponentFileSource source, string originalKey,
			IDictionary<string, InstallLogReadMod> mods, IDictionary<ModDeploymentRoot, string> roots,
			string gameRoot, IDictionary<string, MutableFile> files,
			IDictionary<string, MutableComponent> components, List<ManagedContentComponentIssue> issues, bool ambiguous, ManagedContentLayoutRule[] rules)
		{
			string physicalRoot;
			if (!roots.TryGetValue(target.Root, out physicalRoot))
			{
				AddIssue(issues, ManagedContentComponentIssueKind.PhysicalRootUnavailable, target.ToString(),
					"No proven physical root exists for this deployment target.");
				return;
			}
			string relative;
			if (!TryProjectPath(gameRoot, physicalRoot, target.RelativePath, out relative))
			{
				AddIssue(issues, ManagedContentComponentIssueKind.TargetOutsideComponentRoot, target.ToString(),
					"Native target is outside the requested component directory.");
				return;
			}
			string componentPath; ManagedContentLayoutRule rule;
			if (!TryComponent(relative, false, out componentPath, out rule, issues, rules)) return;
			MutableComponent component = GetComponent(componentPath, rule, components, issues);
			var managed = new List<string>();
			bool original = false;
			foreach (string key in ownerKeys)
			{
				if (Names.Equals(key, originalKey)) { original = true; continue; }
				if (!mods.ContainsKey(key))
				{
					AddIssue(issues, ManagedContentComponentIssueKind.UnresolvedNativeOwner, relative,
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
					AddIssue(issues, ManagedContentComponentIssueKind.ConflictingNativeTarget, relative,
						"More than one native target addresses the same physical component file.");
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

		/// <summary>Groups case-insensitive component paths while reporting conflicting Windows spellings.</summary>
		private static MutableComponent GetComponent(string path, ManagedContentLayoutRule rule,
			IDictionary<string, MutableComponent> components, List<ManagedContentComponentIssue> issues)
		{
			MutableComponent existing;
			if (components.TryGetValue(path, out existing))
			{
				if (!String.Equals(existing.Path, path, StringComparison.Ordinal))
					AddIssue(issues, ManagedContentComponentIssueKind.InconsistentComponentCase, path,
						"Different component spellings alias the same Windows folder.");
				return existing;
			}
			existing = new MutableComponent { Path = path, Rule = rule };
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

		/// <summary>Normalizes an absolute comparison root while preserving volume roots.</summary>
		private static string NormalizeRoot(string path)
		{
			string full = Path.GetFullPath(path);
			string root = Path.GetPathRoot(full);
			return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
		}

		/// <summary>Preserves the legacy observation-path grammar without interpreting archive layouts.</summary>
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

		/// <summary>Recognizes deployed folders from target-side rules without interpreting an archive or claiming ownership.</summary>
		private static bool TryComponent(string path, bool folder, out string componentPath,
			out ManagedContentLayoutRule rule, List<ManagedContentComponentIssue> issues, ManagedContentLayoutRule[] rules)
		{
			componentPath = null;
			rule = null;
			string rel = NormalizeRelative(path);
			if (rel == null)
			{
				AddIssue(issues, ManagedContentComponentIssueKind.InvalidComponentPath, path,
					"A component observation contains a non-canonical path.");
				return false;
			}
			string[] segments = rel.Split('\\');
			ManagedContentLayoutRule[] rootRules = rules.Where(candidate => Names.Equals(segments[0], candidate.TargetFolder)).ToArray();
			if (rootRules.Length == 0) return false;
			if (segments.Length < (folder ? 2 : 3) || (folder && segments.Length != 2))
			{
				AddIssue(issues, ManagedContentComponentIssueKind.InvalidComponentPath, rel,
					"A component path lacks its required folder/file segments.");
				return false;
			}
			ManagedContentLayoutRule[] matches = rootRules.Where(candidate =>
				segments[1].StartsWith(candidate.ComponentPrefix, StringComparison.OrdinalIgnoreCase) &&
				segments[1].Length > candidate.ComponentPrefix.Length).ToArray();
			if (matches.Length == 0)
			{
				AddIssue(issues, ManagedContentComponentIssueKind.UnrecognizedComponentFolder, rel,
					"A folder under a configured component root has no recognized prefix.");
				return false;
			}
			rule = matches[0];
			// Several source/archive shapes may intentionally describe one deployed component.
			// Conflicting target metadata cannot establish an unambiguous diagnostic identity.
			ManagedContentLayoutRule first = rule;
			if (matches.Any(candidate => !String.Equals(candidate.TargetFolder, first.TargetFolder, StringComparison.Ordinal) ||
				!String.Equals(candidate.ContentFolder, first.ContentFolder, StringComparison.Ordinal) ||
				!String.Equals(candidate.ComponentKind, first.ComponentKind, StringComparison.Ordinal)))
			{
				AddIssue(issues, ManagedContentComponentIssueKind.AmbiguousComponentRule, rel,
					"Configured rules disagree on this deployed component's identity or content folder.");
				return false;
			}
			componentPath = rule.TargetFolder + "\\" + segments[1];
			return true;
		}

		/// <summary>Appends an immutable diagnostic to the current detached projection.</summary>
		private static void AddIssue(ICollection<ManagedContentComponentIssue> issues, ManagedContentComponentIssueKind kind,
			string path, string message)
		{
			issues.Add(new ManagedContentComponentIssue(kind, path, message));
		}
	}
}
