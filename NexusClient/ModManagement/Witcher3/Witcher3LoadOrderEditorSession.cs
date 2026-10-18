using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>
	/// The source row is external game state. DraftPosition and DraftEnabled are unsaved
	/// UI intentions, never NMM archive Sort values or native mod activation states.
	/// </summary>
	public sealed class Witcher3LoadOrderEditorRow
	{
		internal Witcher3LoadOrderEditorRow(Witcher3LoadOrderRow source)
		{
			Source = source ?? throw new ArgumentNullException(nameof(source));
			DraftEnabled = source.ConfiguredEnabled;
			OwnerNames = String.Join(", ", source.NativeContributors.Select(x => x.ModName));
		}

		public Witcher3LoadOrderRow Source { get; }
		public string FolderName => Source.FolderName;
		/// <summary>Present in the observed file, even when currently missing on disk.</summary>
		public bool IsConfigured => Source.IsListedInSettings;
		public bool IsNewInDraft => !IsConfigured && DraftPosition.HasValue;
		public int? GamePriority => Source.ConfiguredPriority;
		public bool? GameEnabled => Source.ConfiguredEnabled;
		public int? DraftPosition { get; internal set; }
		public bool? DraftEnabled { get; internal set; }
		public bool HasEnabledEdit => DraftEnabled != GameEnabled;
		public string OwnerNames { get; }
	}

	/// <summary>
	/// A pure draft of an existing Legacy settings file. Native/manual configured sections
	/// are always retained. An observed unconfigured folder joins ONLY upon explicit Add;
	/// a missing/ambiguous folder is never automatically adopted or registered as an NMM mod.
	/// </summary>
	public sealed class Witcher3LoadOrderEditorSession
	{
		private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
		private readonly Witcher3ModSettingsFileReadResult _observed;
		private readonly Witcher3Edition _edition;
		private readonly List<Witcher3LoadOrderEditorRow> _configured;
		private readonly List<Witcher3LoadOrderEditorRow> _other;
		private readonly List<Witcher3LoadOrderEditorRow> _originalConfigured;
		private readonly List<Witcher3LoadOrderEditorRow> _originalOther;
		private readonly Dictionary<string, Witcher3LoadOrderEditorRow> _byName;

		public Witcher3LoadOrderEditorSession(string modeId, Witcher3Edition edition,
			bool pendingPublication, Witcher3ModSettingsFileReadResult observed,
			Witcher3LoadOrderProjection projection)
		{
			if (observed == null) throw new ArgumentNullException(nameof(observed));
			if (projection == null) throw new ArgumentNullException(nameof(projection));
			if (projection.SettingsReadStatus != observed.Status ||
				!Object.ReferenceEquals(projection.SettingsDocument, observed.Document))
				throw new ArgumentException("The displayed folders and settings must be from one read observation.", nameof(projection));

			_observed = observed;
			Observed = observed;
			Projection = projection;
			_edition = edition;
			HasPendingPublication = pendingPublication;
			CanEdit = String.Equals(modeId, "Witcher3", StringComparison.OrdinalIgnoreCase) &&
				edition == Witcher3Edition.Legacy && !pendingPublication && observed.IsLoaded &&
				observed.Document != null && observed.Document.IsUnambiguous &&
				observed.Encoding.HasValue && !String.IsNullOrEmpty(observed.ContentSha256) &&
				observed.Document.ModSections.Count() <= Witcher3ModSettingsChangePlanner.MaxGamePriority;

			var rows = projection.Rows.Select(x => new Witcher3LoadOrderEditorRow(x)).ToList();
			_originalConfigured = rows.Where(x => x.IsConfigured).ToList();
			_originalOther = rows.Where(x => !x.IsConfigured).ToList();
			_configured = new List<Witcher3LoadOrderEditorRow>(_originalConfigured);
			_other = new List<Witcher3LoadOrderEditorRow>(_originalOther);
			_byName = rows.GroupBy(x => x.FolderName, Names)
				.Where(x => x.Count() == 1).ToDictionary(x => x.Key, x => x.Single(), Names);
			UpdateDraftPositions();
		}

		public Witcher3ModSettingsFileReadResult Observed { get; }
		public Witcher3LoadOrderProjection Projection { get; }
		public bool HasPendingPublication { get; }
		public bool CanEdit { get; }
		public bool CanRestoreGameDefaults => CanEdit &&
			_observed.Document.ModSections.Any(x => x.HasEnabledField || x.HasPriorityField);
		public int ConfiguredCount => _originalConfigured.Count;
		public int DraftCount => _configured.Count;
		public int UnconfiguredCount => _other.Count + _configured.Count(x => x.IsNewInDraft);
		public bool HasNewFolders => _configured.Any(x => x.IsNewInDraft);
		public ReadOnlyCollection<Witcher3LoadOrderEditorRow> Rows =>
			new ReadOnlyCollection<Witcher3LoadOrderEditorRow>(_configured.Concat(_other).ToList());
		public bool IsDirty => _configured.Count != _originalConfigured.Count ||
			_configured.Where((row, index) => index >= _originalConfigured.Count ||
				!String.Equals(row.FolderName, _originalConfigured[index].FolderName, StringComparison.OrdinalIgnoreCase) ||
				row.HasEnabledEdit).Any();

		public Witcher3LoadOrderEditorRow Find(string folderName)
		{
			Witcher3LoadOrderEditorRow row;
			return folderName != null && _byName.TryGetValue(folderName, out row) ? row : null;
		}

		public bool CanAddToOrder(string folderName)
		{
			if (!CanEdit || _configured.Count >= Witcher3ModSettingsChangePlanner.MaxGamePriority ||
				!Projection.NativeIndex.HasCompleteDiskInventory) return false;
			Witcher3LoadOrderEditorRow row = Find(folderName);
			return row != null && !row.IsConfigured && !row.DraftPosition.HasValue &&
				Witcher3ModSettingsSection.IsModFolderName(row.FolderName) &&
				row.Source.Presence == Witcher3LoadOrderPresence.Observed &&
				row.Source.Component?.ObservedOnDisk == true &&
				row.Source.Origin != Witcher3LoadOrderOrigin.GeneratedMergeCandidate;
		}

		public bool AddToOrder(string folderName)
		{
			if (!CanAddToOrder(folderName)) return false;
			Witcher3LoadOrderEditorRow row = Find(folderName);
			_other.Remove(row);
			_configured.Add(row);
			row.DraftEnabled = true;
			UpdateDraftPositions();
			return true;
		}

		public bool RemoveDraftAddition(string folderName)
		{
			Witcher3LoadOrderEditorRow row = Find(folderName);
			if (!CanEdit || row == null || !row.IsNewInDraft) return false;
			_configured.Remove(row);
			_other.Add(row);
			row.DraftEnabled = row.GameEnabled;
			UpdateDraftPositions();
			return true;
		}

		public bool Move(string folderName, int direction)
		{
			if (!CanEdit || (direction != -1 && direction != 1)) return false;
			Witcher3LoadOrderEditorRow row = Find(folderName);
			if (row == null || !row.DraftPosition.HasValue) return false;
			int index = _configured.IndexOf(row);
			int target = index + direction;
			if (target < 0 || target >= _configured.Count) return false;
			_configured.RemoveAt(index);
			_configured.Insert(target, row);
			UpdateDraftPositions();
			return true;
		}

		public bool SetGameEnabled(string folderName, bool enabled)
		{
			if (!CanEdit) return false;
			Witcher3LoadOrderEditorRow row = Find(folderName);
			if (row == null || !row.DraftPosition.HasValue) return false;
			row.DraftEnabled = enabled;
			return true;
		}

		public void ResetDraft()
		{
			_configured.Clear();
			_configured.AddRange(_originalConfigured);
			_other.Clear();
			_other.AddRange(_originalOther);
			foreach (Witcher3LoadOrderEditorRow row in _byName.Values)
				row.DraftEnabled = row.GameEnabled;
			UpdateDraftPositions();
		}

		/// <summary>
		/// Before approval, verify every newly selected folder against a NEW complete disk
		/// observation. Existing manual sections can stay configured even if missing on disk.
		/// </summary>
		public bool AddedFoldersStillObserved(Witcher3LoadOrderProjection current)
		{
			if (!HasNewFolders) return true;
			if (current == null || !current.NativeIndex.HasCompleteDiskInventory) return false;
			var indexed = current.Rows.Where(x => !x.IsListedInSettings &&
				x.Presence == Witcher3LoadOrderPresence.Observed &&
				x.Component?.ObservedOnDisk == true).ToDictionary(x => x.FolderName, Names);
			return _configured.Where(x => x.IsNewInDraft).All(x => indexed.ContainsKey(x.FolderName));
		}

		/// <summary>Compares exact file identity, not a timestamp or a cached edition.</summary>
		public bool MatchesObservedFile(Witcher3ModSettingsFileReadResult current)
		{
			return current != null && current.Status == _observed.Status &&
				current.ByteLength == _observed.ByteLength &&
				String.Equals(current.ContentSha256, _observed.ContentSha256, StringComparison.OrdinalIgnoreCase) &&
				String.Equals(current.FilePath, _observed.FilePath, StringComparison.OrdinalIgnoreCase);
		}

		public Witcher3ModSettingsChangePreview PrepareChange()
		{
			return Witcher3ModSettingsChangePlanner.PrepareWithNewFolders(_observed, _edition,
				_configured.Select(x => new Witcher3ModSettingsOrderEntry(x.FolderName,
					x.HasEnabledEdit ? x.DraftEnabled : (bool?)null)), CanEdit && IsDirty,
				_configured.Where(x => x.IsNewInDraft).Select(x => x.FolderName));
		}

		public Witcher3ModSettingsChangePreview PrepareRestoreGameDefaults()
		{
			return Witcher3ModSettingsChangePlanner.PrepareRestoreGameDefaults(_observed,
				_edition, CanRestoreGameDefaults && !IsDirty);
		}

		private void UpdateDraftPositions()
		{
			for (int index = 0; index < _configured.Count; index++)
				_configured[index].DraftPosition = index + 1;
			foreach (Witcher3LoadOrderEditorRow row in _other)
				row.DraftPosition = null;
		}
	}
}
