using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>W3R.8b findings are observations, never install, unlink, rename or merge instructions.</summary>
	public enum Witcher3RemasteredComponentHealthCode
	{
		InventoryIncomplete = 0,
		FolderNameExceedsLimit = 1,
		LegacyMergedFolderObserved = 2,
		LegacyMergedScriptsObserved = 3,
		LegacyMergePresenceUnconfirmed = 4
	}

	/// <summary>Provenance from native file ownership and an optional disk observation.</summary>
	public enum Witcher3RemasteredComponentOrigin
	{
		Unconfirmed = 0,
		NativeManaged = 1,
		ExternalObserved = 2,
		MixedNativeAndExternal = 3
	}

	public sealed class Witcher3RemasteredComponentHealthIssue
	{
		internal Witcher3RemasteredComponentHealthIssue(
			Witcher3RemasteredComponentHealthCode code,
			Witcher3RemasteredHealthSeverity severity, string gameRelativeFolderPath,
			Witcher3RemasteredComponentOrigin origin, int nativeContributorCount,
			int folderNameLength, int observedScriptCount)
		{
			Code = code;
			Severity = severity;
			GameRelativeFolderPath = gameRelativeFolderPath;
			Origin = origin;
			NativeContributorCount = nativeContributorCount;
			FolderNameLength = folderNameLength;
			ObservedScriptCount = observedScriptCount;
		}

		public Witcher3RemasteredComponentHealthCode Code { get; }
		public Witcher3RemasteredHealthSeverity Severity { get; }
		/// <summary>Null for an inventory-wide finding.</summary>
		public string GameRelativeFolderPath { get; }
		public Witcher3RemasteredComponentOrigin Origin { get; }
		public int NativeContributorCount { get; }
		/// <summary>Conservative UTF-16 code-unit count, excluding Mods\.</summary>
		public int FolderNameLength { get; }
		/// <summary>Only files whose disk presence was positively observed.</summary>
		public int ObservedScriptCount { get; }
	}

	public sealed class Witcher3RemasteredComponentHealthReport
	{
		internal Witcher3RemasteredComponentHealthReport(int modFolderCount,
			int observedModFolderCount, bool hasCompleteDiskInventory,
			IEnumerable<Witcher3RemasteredComponentHealthIssue> issues)
		{
			ModFolderCount = modFolderCount;
			ObservedModFolderCount = observedModFolderCount;
			HasCompleteDiskInventory = hasCompleteDiskInventory;
			Issues = new ReadOnlyCollection<Witcher3RemasteredComponentHealthIssue>(issues.ToList());
		}

		public int ModFolderCount { get; }
		public int ObservedModFolderCount { get; }
		public bool HasCompleteDiskInventory { get; }
		public ReadOnlyCollection<Witcher3RemasteredComponentHealthIssue> Issues { get; }
	}

	/// <summary>
	/// Pure, O(components + indexed files) inspection over the existing W3R.3 index.
	/// A name matching a legacy merge convention cannot prove script format, producer
	/// version, or game incompatibility. Never independently enumerate/read game files.
	/// A missing or incomplete disk inventory does not prove any folder is installed.
	/// </summary>
	public static class Witcher3RemasteredComponentHealthInspector
	{
		public const int MaximumSafeModFolderNameLength = 63;
		private const string LegacyMergedFolder = "mod0000_MergedFiles";

		public static Witcher3RemasteredComponentHealthReport Inspect(Witcher3NativeComponentIndex index)
		{
			if (index == null) throw new ArgumentNullException(nameof(index));
			var issues = new List<Witcher3RemasteredComponentHealthIssue>();
			int total = 0;
			int observed = 0;
			if (!index.HasCompleteDiskInventory)
				issues.Add(new Witcher3RemasteredComponentHealthIssue(
					Witcher3RemasteredComponentHealthCode.InventoryIncomplete,
					Witcher3RemasteredHealthSeverity.Information, null,
					Witcher3RemasteredComponentOrigin.Unconfirmed, 0, 0, 0));

			foreach (Witcher3NativeComponent component in index.Components)
			{
				if (component == null || !component.IsModLoadOrderCandidate) continue;
				total++;
				if (component.ObservedOnDisk == true) observed++;
				Witcher3RemasteredComponentOrigin origin = ResolveOrigin(component);
				// The 63-character rule applies to the *folder name*, not Mods\ or DLC\.
				// A missing folder in a complete scan must not be reported as a live risk.
				if (component.FolderName.Length > MaximumSafeModFolderNameLength &&
					component.ObservedOnDisk != false)
				{
					issues.Add(NewIssue(Witcher3RemasteredComponentHealthCode.FolderNameExceedsLimit,
						component.ObservedOnDisk == true ? Witcher3RemasteredHealthSeverity.Warning
							: Witcher3RemasteredHealthSeverity.Information, component, origin, 0));
				}
				// Compilation triggers and other names that merely resemble a merger are
				// not proof of legacy Script Merger output. Use only the characterized name.
				if (!String.Equals(component.FolderName, LegacyMergedFolder, StringComparison.OrdinalIgnoreCase))
					continue;
				if (component.ObservedOnDisk != true)
				{
					if (component.ObservedOnDisk == null)
						issues.Add(NewIssue(Witcher3RemasteredComponentHealthCode.LegacyMergePresenceUnconfirmed,
							Witcher3RemasteredHealthSeverity.Information, component, origin, 0));
					continue;
				}
				issues.Add(NewIssue(Witcher3RemasteredComponentHealthCode.LegacyMergedFolderObserved,
					Witcher3RemasteredHealthSeverity.Warning, component, origin, 0));
				string scriptPrefix = component.GameRelativeFolderPath + @"\content\scripts\";
				int scripts = component.Files.Count(file => file.ObservedOnDisk == true &&
					file.GameRelativePath.StartsWith(scriptPrefix, StringComparison.OrdinalIgnoreCase) &&
					file.GameRelativePath.EndsWith(".ws", StringComparison.OrdinalIgnoreCase));
				if (scripts > 0)
					issues.Add(NewIssue(Witcher3RemasteredComponentHealthCode.LegacyMergedScriptsObserved,
						Witcher3RemasteredHealthSeverity.Warning, component, origin, scripts));
			}
			return new Witcher3RemasteredComponentHealthReport(total, observed,
				index.HasCompleteDiskInventory, issues);
		}

		private static Witcher3RemasteredComponentHealthIssue NewIssue(
			Witcher3RemasteredComponentHealthCode code,
			Witcher3RemasteredHealthSeverity severity, Witcher3NativeComponent component,
			Witcher3RemasteredComponentOrigin origin, int scripts)
		{
			return new Witcher3RemasteredComponentHealthIssue(code, severity,
				component.GameRelativeFolderPath, origin, component.Owners.Count,
				component.FolderName.Length, scripts);
		}

		private static Witcher3RemasteredComponentOrigin ResolveOrigin(Witcher3NativeComponent component)
		{
			if (component.HasMixedManagedAndUnmanagedFiles)
				return Witcher3RemasteredComponentOrigin.MixedNativeAndExternal;
			if (component.IsEntirelyExternal)
				return Witcher3RemasteredComponentOrigin.ExternalObserved;
			if (component.Owners.Count > 0)
				return Witcher3RemasteredComponentOrigin.NativeManaged;
			return Witcher3RemasteredComponentOrigin.Unconfirmed;
		}
	}
}
