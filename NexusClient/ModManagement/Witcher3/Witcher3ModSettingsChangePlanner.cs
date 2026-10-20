using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>The requested game-folder position and, optionally, a new game-enabled flag.</summary>
	public sealed class Witcher3ModSettingsOrderEntry
	{
		public Witcher3ModSettingsOrderEntry(string folderName, bool? enabled = null)
		{
			FolderName = folderName;
			Enabled = enabled;
		}

		public string FolderName { get; }
		/// <summary>Null preserves the source Enabled key, including its absence.</summary>
		public bool? Enabled { get; }
	}

	public enum Witcher3ModSettingsChangePlanStatus
	{
		Blocked = 0,
		Unchanged = 1,
		PreviewAvailable = 2
	}

	public enum Witcher3ModSettingsChangeKind
	{
		ManagedOrder = 0,
		RestoreGameDefaults = 1,
		RemoveMissingEntries = 2
	}

	/// <summary>A semantic delta for one game mod folder, not an NMM archive or plugin.</summary>
	public sealed class Witcher3ModSettingsOrderChange
	{
		internal Witcher3ModSettingsOrderChange(string folderName, int? previousPriority,
			int? proposedPriority, bool? previousEnabled, bool? proposedEnabled)
		{
			FolderName = folderName;
			PreviousPriority = previousPriority;
			ProposedPriority = proposedPriority;
			PreviousEnabled = previousEnabled;
			ProposedEnabled = proposedEnabled;
		}

		public string FolderName { get; }
		public int? PreviousPriority { get; }
		public int? ProposedPriority { get; }
		public bool? PreviousEnabled { get; }
		public bool? ProposedEnabled { get; }
	}

	/// <summary>
	/// Read-only, immutable review result. Hashes include the original BOM and all bytes.
	/// No caller is authorized to write mods.settings merely because a preview is available.
	/// </summary>
	public sealed class Witcher3ModSettingsChangePreview
	{
		internal Witcher3ModSettingsChangePreview(Witcher3ModSettingsChangePlanStatus status,
			string diagnostic, Witcher3ModSettingsFileReadResult observed, string proposedText,
			long? proposedByteLength, string proposedSha256,
			IEnumerable<Witcher3ModSettingsOrderChange> changes,
			Witcher3ModSettingsChangeKind kind = Witcher3ModSettingsChangeKind.ManagedOrder,
			Witcher3ModSettingsTextEncoding? newFileEncoding = null)
		{
			Status = status;
			Kind = kind;
			Diagnostic = diagnostic;
			FilePath = observed.FilePath;
			ObservedStatus = observed.Status;
			ObservedByteLength = observed.ByteLength;
			ObservedSha256 = observed.ContentSha256;
			TextEncoding = observed.Encoding ?? newFileEncoding;
			ProposedText = proposedText;
			ProposedByteLength = proposedByteLength;
			ProposedSha256 = proposedSha256;
			Changes = new ReadOnlyCollection<Witcher3ModSettingsOrderChange>(changes.ToList());
		}

		public Witcher3ModSettingsChangePlanStatus Status { get; }
		public Witcher3ModSettingsChangeKind Kind { get; }
		public string Diagnostic { get; }
		public string FilePath { get; }
		public Witcher3ModSettingsReadStatus ObservedStatus { get; }
		public long? ObservedByteLength { get; }
		public string ObservedSha256 { get; }
		public Witcher3ModSettingsTextEncoding? TextEncoding { get; }
		/// <summary>Null when blocked; otherwise exact replacement text in the original encoding.</summary>
		public string ProposedText { get; }
		public long? ProposedByteLength { get; }
		public string ProposedSha256 { get; }
		public ReadOnlyCollection<Witcher3ModSettingsOrderChange> Changes { get; }
		/// <summary>Adapts the immutable preview without reading disk or widening Legacy authorization.</summary>
		internal IniOrderChangePreview ToShared()
		{
			var observed = new IniOrderFileObservation(FilePath, (IniOrderReadStatus)ObservedStatus,
				ObservedStatus == Witcher3ModSettingsReadStatus.Missing ? null :
					IniOrderParser.Parse(String.Empty, Witcher3ModSettingsParser.Format),
				ObservedStatus == Witcher3ModSettingsReadStatus.Missing ? null :
					(TextEncoding.HasValue ? (IniOrderTextEncoding?)TextEncoding.Value : null),
				ObservedByteLength, ObservedSha256, null, null);
			return new IniOrderChangePreview((IniOrderChangePlanStatus)Status, Diagnostic, observed, ProposedText,
				ProposedByteLength, ProposedSha256, Changes.Select(change => new IniOrderChange(change.FolderName,
					change.PreviousPriority, change.ProposedPriority, change.PreviousEnabled, change.ProposedEnabled)),
				(IniOrderChangeKind)Kind, Witcher3ModSettingsParser.Format,
				TextEncoding.HasValue ? (IniOrderTextEncoding?)TextEncoding.Value : null);
		}

		/// <summary>W3R.5a does not publish any file mutation capability.</summary>
		public bool AuthorizesFileWrite => false;
	}

	/// <summary>Legacy-only compatibility policy over the shared lossless sequential INI planner.</summary>
	public static class Witcher3ModSettingsChangePlanner
	{
		public const int MaxGamePriority = IniOrderChangePlanner.MaxGamePriority;

		/// <summary>Preserves the configured-only Legacy review contract.</summary>
		public static Witcher3ModSettingsChangePreview Prepare(Witcher3ModSettingsFileReadResult observed,
			Witcher3Edition edition, IEnumerable<Witcher3ModSettingsOrderEntry> desiredOrder, bool explicitlyRequestedManagedOrder)
		{
			return PrepareWithNewFolders(observed, edition, desiredOrder, explicitlyRequestedManagedOrder, Enumerable.Empty<string>());
		}

		/// <summary>Requires Legacy policy before delegating complete-order and explicit-addition validation.</summary>
		public static Witcher3ModSettingsChangePreview PrepareWithNewFolders(Witcher3ModSettingsFileReadResult observed,
			Witcher3Edition edition, IEnumerable<Witcher3ModSettingsOrderEntry> desiredOrder, bool explicitlyRequestedManagedOrder,
			IEnumerable<string> allowedNewFolders, bool preserveExistingPriorities = false)
		{
			if (observed == null) throw new ArgumentNullException(nameof(observed));
			if (desiredOrder == null) throw new ArgumentNullException(nameof(desiredOrder));
			if (allowedNewFolders == null) throw new ArgumentNullException(nameof(allowedNewFolders));
			if (explicitlyRequestedManagedOrder && edition != Witcher3Edition.Legacy &&
				edition != Witcher3Edition.Remastered) return Blocked(observed);
			return FromShared(IniOrderChangePlanner.PrepareWithNewFolders(observed.ToShared(),
				desiredOrder.Select(entry => entry == null ? null : new IniOrderEntry(entry.FolderName, entry.Enabled)),
				explicitlyRequestedManagedOrder, allowedNewFolders, Witcher3ModSettingsParser.Format,
				preserveExistingPriorities), observed);
		}

		/// <summary>Removes only configured order/enabled overrides after the existing Legacy check.</summary>
		public static Witcher3ModSettingsChangePreview PrepareRestoreGameDefaults(Witcher3ModSettingsFileReadResult observed,
			Witcher3Edition edition, bool explicitlyRequestedRestore)
		{
			if (observed == null) throw new ArgumentNullException(nameof(observed));
			if (explicitlyRequestedRestore && edition != Witcher3Edition.Legacy &&
				edition != Witcher3Edition.Remastered) return Blocked(observed);
			return FromShared(IniOrderChangePlanner.PrepareRestoreGameDefaults(observed.ToShared(), explicitlyRequestedRestore), observed);
		}

		/// <summary>Removes stale sections only after a complete scan confirms that their folders contain no files.</summary>
		public static Witcher3ModSettingsChangePreview PrepareRemoveMissingEntries(Witcher3ModSettingsFileReadResult observed,
			Witcher3Edition edition, Witcher3ComponentDiskObservation disk)
		{
			if (observed == null) throw new ArgumentNullException(nameof(observed));
			if ((edition != Witcher3Edition.Legacy && edition != Witcher3Edition.Remastered) || disk == null || !disk.IsComplete)
				return new Witcher3ModSettingsChangePreview(Witcher3ModSettingsChangePlanStatus.Blocked,
					"Cleanup requires a confirmed game edition and a complete disk scan. No entries were removed.",
					observed, null, null, null, new Witcher3ModSettingsOrderChange[0]);
			var populated = new HashSet<string>(disk.GameRelativeFiles.Select(file => file.Split('\\'))
				.Where(parts => parts.Length >= 3 && String.Equals(parts[0], "Mods", StringComparison.OrdinalIgnoreCase))
				.Select(parts => parts[1]), StringComparer.OrdinalIgnoreCase);
			string[] missing = observed.Document?.ModSections.Where(section => !populated.Contains(section.Name))
				.Select(section => section.Name).ToArray() ?? new string[0];
			return FromShared(IniOrderChangePlanner.PrepareRemoveEntries(observed.ToShared(), missing), observed);
		}

		/// <summary>Retains public Witcher preview/status types for existing consumers.</summary>
		private static Witcher3ModSettingsChangePreview FromShared(IniOrderChangePreview preview, Witcher3ModSettingsFileReadResult observed)
		{
			return new Witcher3ModSettingsChangePreview((Witcher3ModSettingsChangePlanStatus)preview.Status,
				preview.Diagnostic, observed, preview.ProposedText, preview.ProposedByteLength, preview.ProposedSha256,
				preview.Changes.Select(change => new Witcher3ModSettingsOrderChange(change.FolderName, change.PreviousPriority,
					change.ProposedPriority, change.PreviousEnabled, change.ProposedEnabled)),
				(Witcher3ModSettingsChangeKind)preview.Kind,
				preview.TextEncoding.HasValue ? (Witcher3ModSettingsTextEncoding?)preview.TextEncoding.Value : null);
		}

		/// <summary>Preserves the blocked Remastered/unknown-edition result.</summary>
		private static Witcher3ModSettingsChangePreview Blocked(Witcher3ModSettingsFileReadResult observed) =>
			new Witcher3ModSettingsChangePreview(Witcher3ModSettingsChangePlanStatus.Blocked,
				"Ordering requires a confirmed Witcher 3 Legacy or Remastered installation.", observed,
				null, null, null, new Witcher3ModSettingsOrderChange[0]);

		/// <summary>Preserves exact encoding for existing publication callers.</summary>
		internal static byte[] Encode(string text, Witcher3ModSettingsTextEncoding kind) => IniOrderByteCodec.Encode(text, (IniOrderTextEncoding)kind);
	}
}
