using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Games.ManagedContent;
using Nexus.Client.ModManagement.ManagedContent;

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

		/// <summary>Adapts shared native contributor observations without changing Witcher public model contracts.</summary>
		internal Witcher3ComponentNativeOwner(ManagedContentComponentNativeOwner owner)
		{
			NativeModKey = owner.NativeModKey;
			ModName = owner.ModName;
			ArchivePath = owner.ArchivePath;
			InstallMethod = owner.InstallMethod;
			FileCount = owner.FileCount;
			WinningFileCount = owner.WinningFileCount;
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

	/// <summary>Preserves the Witcher read model and its game-specific heuristics over the shared native inventory.</summary>
	public static class Witcher3NativeComponentIndexer
	{
		private static readonly ManagedContentLayoutRule[] ComponentRules =
		{
			new ManagedContentLayoutRule("mods", "component-folder", "Mods", "mod", "content", "Data", "Mods", "mod-folder", 63),
			new ManagedContentLayoutRule("dlc", "component-folder", "DLC", "dlc", "content", "Data", "DLC", "dlc-folder", 255)
		};

		/// <summary>Shares the fixed historical read scope with the compatibility disk scanner.</summary>
		internal static IReadOnlyList<ManagedContentLayoutRule> NativeComponentRules => Array.AsReadOnly(ComponentRules);

		/// <summary>Projects the same native ownership evidence while retaining Witcher ordering and merged-folder classification.</summary>
		public static Witcher3NativeComponentIndex Build(NativeStateCaptureSnapshot nativeState,
			string physicalGameRoot, Witcher3ComponentDiskObservation disk = null)
		{
			ManagedContentComponentDiskObservation observation = disk == null ? null : new ManagedContentComponentDiskObservation(
				disk.PhysicalGameRoot, disk.GameRelativeFiles, disk.ComponentFolders, disk.IsComplete);
			ManagedContentNativeComponentIndex index = ManagedContentNativeComponentIndexer.Build(nativeState, physicalGameRoot, ComponentRules, observation);
			IEnumerable<Witcher3NativeComponent> components = index.Components.Select(component => new Witcher3NativeComponent(
				component.RootRelativeFolderPath, GetKind(component), component.ObservedOnDisk,
				component.Owners.Select(owner => new Witcher3ComponentNativeOwner(owner)),
				component.Files.Select(file => new Witcher3ComponentFile(file.RootRelativePath,
					(Witcher3ComponentFileSource)file.Source, file.NativeOwnerKeys, file.IntendedWinnerKey,
					file.HasOriginalFallback, file.WinnerAmbiguous, file.ObservedOnDisk))));
			return new Witcher3NativeComponentIndex(index.NativeDeploymentCommitSequence, components,
				index.Issues.Select(issue => new Witcher3ComponentIssue((Witcher3ComponentIssueKind)issue.Kind, issue.Path, issue.Message)),
				index.HasCompleteDiskInventory);
		}

		/// <summary>Keeps DLC order exclusion and known generated-output names specific to Witcher.</summary>
		private static Witcher3ComponentKind GetKind(ManagedContentNativeComponent component)
		{
			if (component.ComponentKind == "dlc-folder") return Witcher3ComponentKind.Dlc;
			return String.Equals(component.FolderName, "mod0000_MergedFiles", StringComparison.OrdinalIgnoreCase) ||
				String.Equals(component.FolderName, "mod0000____CompilationTrigger", StringComparison.OrdinalIgnoreCase)
				? Witcher3ComponentKind.MergedOutputCandidate : Witcher3ComponentKind.Mod;
		}
	}
}
