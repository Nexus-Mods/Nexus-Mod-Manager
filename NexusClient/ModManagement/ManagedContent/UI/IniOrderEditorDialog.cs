using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.ManagedContent.UI
{
	/// <summary>A configured-section draft; it never installs, adopts or removes component folders.</summary>
	internal sealed class IniOrderEditorDialog : ManagedFontXtraForm
	{
		private readonly IniOrderFileObservation _observed;
		private readonly List<DraftRow> _rows;
		private readonly GridControl _grid;
		private readonly GridView _view;

		/// <summary>Opens a detached draft of one existing, unambiguous observed file.</summary>
		internal IniOrderEditorDialog(IniOrderFileObservation observed)
		{
			if (observed?.IsLoaded != true || observed.Document?.IsUnambiguous != true)
				throw new ArgumentException("A complete existing order file is required.", nameof(observed));
			_observed = observed;
			_rows = observed.Document.ComponentSections.OrderBy(section => section.Priority ?? Int32.MaxValue)
				.ThenBy(section => section.Name, StringComparer.OrdinalIgnoreCase).Select(section => new DraftRow(section)).ToList();
			Text = L("ManagedContent.Order.Editor.Title", "Edit component order");
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(640, 420);
			ClientSize = new Size(840, 560);
			ShowInTaskbar = false;
			var intro = new LabelControl { Dock = DockStyle.Top, AutoSizeMode = LabelAutoSizeMode.Vertical,
				Padding = new Padding(8), UseMnemonic = false, Text = L("ManagedContent.Order.Editor.Introduction",
					"Arrange all configured components. Position assigns sequential priorities starting at 1. Game-enabled values are independent of NMM activation. Review shows the complete proposed file before writing.") + Environment.NewLine + observed.FilePath };
			intro.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
			var commands = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, AutoScroll = true, WrapContents = false };
			AddButton(commands, "ManagedContent.Order.Editor.Up", "Move up", () => MoveSelection(-1));
			AddButton(commands, "ManagedContent.Order.Editor.Down", "Move down", () => MoveSelection(1));
			AddButton(commands, "ManagedContent.Order.Editor.Enable", "Game enable", () => SetEnabled(true));
			AddButton(commands, "ManagedContent.Order.Editor.Disable", "Game disable", () => SetEnabled(false));
			_grid = new GridControl { Dock = DockStyle.Fill };
			_view = new GridView(_grid);
			_grid.MainView = _view;
			_grid.ViewCollection.Add(_view);
			_view.OptionsBehavior.Editable = false;
			_view.OptionsBehavior.AutoPopulateColumns = false;
			_view.OptionsCustomization.AllowSort = false;
			_view.OptionsView.ShowGroupPanel = false;
			_view.Columns.AddVisible(nameof(DraftRow.Position), L("ManagedContent.Order.Editor.Position", "Draft position"));
			_view.Columns.AddVisible(nameof(DraftRow.Name), L("ManagedContent.Components.Columns.Folder", "Component folder"));
			_view.Columns.AddVisible(nameof(DraftRow.OriginalPriority), L("ManagedContent.Order.Columns.Priority", "Configured priority"));
			_view.Columns.AddVisible(nameof(DraftRow.EnabledText), L("ManagedContent.Order.Editor.Enabled", "Draft game enabled"));
			var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
			var review = AddButton(actions, "ManagedContent.Order.Editor.Review", "Review changes", () => Prepare(false));
			review.Enabled = _rows.Count > 0 && _rows.Count <= IniOrderChangePlanner.MaxGamePriority;
			var restore = AddButton(actions, "ManagedContent.Order.Editor.Defaults", "Review restoring defaults", () => Prepare(true));
			restore.Enabled = observed.Document.ComponentSections.Any(section => section.HasPriorityField || section.HasEnabledField);
			var cancel = new SimpleButton { Text = L("Common.Action.Cancel", "Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
			actions.Controls.Add(cancel);
			CancelButton = cancel;
			Controls.Add(_grid);
			Controls.Add(actions);
			Controls.Add(commands);
			Controls.Add(intro);
			Bind(null);
		}

		public IniOrderChangePreview Preview { get; private set; }

		/// <summary>Uses the current localization infrastructure for all static captions.</summary>
		private static string L(string key, string fallback) => LanguageManager.Get(key, fallback);

		/// <summary>Adds one themed command without creating a separate deployment action.</summary>
		private static SimpleButton AddButton(FlowLayoutPanel panel, string key, string fallback, Action action)
		{
			var button = new SimpleButton { Text = L(key, fallback), AutoSize = true };
			button.Click += (sender, args) => action();
			panel.Controls.Add(button);
			return button;
		}

		/// <summary>Moves only a selected draft position; configured sections cannot be discarded.</summary>
		private void MoveSelection(int direction)
		{
			DraftRow row = _view.GetFocusedRow() as DraftRow;
			if (row == null) return;
			int index = _rows.IndexOf(row), next = index + direction;
			if (next < 0 || next >= _rows.Count) return;
			_rows.RemoveAt(index);
			_rows.Insert(next, row);
			Bind(row.Name);
		}

		/// <summary>Records an explicit game flag; absent flags remain absent unless changed.</summary>
		private void SetEnabled(bool enabled)
		{
			DraftRow row = _view.GetFocusedRow() as DraftRow;
			if (row == null) return;
			row.Enabled = enabled;
			Bind(row.Name);
		}

		/// <summary>Rebinds detached draft rows, keeping the selected component.</summary>
		private void Bind(string name)
		{
			for (int i = 0; i < _rows.Count; i++) _rows[i].Position = i + 1;
			_grid.DataSource = new BindingList<DraftRow>(_rows.ToList());
			int index = _rows.FindIndex(row => row.Name == name);
			if (index >= 0) _view.FocusedRowHandle = _view.GetRowHandle(index);
		}

		/// <summary>Prepares a pure preview; restoration explicitly replaces the draft with removal of all known overrides.</summary>
		private void Prepare(bool restore)
		{
			IniOrderChangePreview preview = restore ? IniOrderChangePlanner.PrepareRestoreGameDefaults(_observed, true) :
				IniOrderChangePlanner.Prepare(_observed, _rows.Select(row => new IniOrderEntry(row.Name,
					row.Enabled == row.OriginalEnabled ? (bool?)null : row.Enabled)), true);
			if (preview.Status != IniOrderChangePlanStatus.PreviewAvailable)
			{
				XtraMessageBox.Show(this, preview.Diagnostic ?? L("ManagedContent.Order.Editor.Unchanged", "No file changes are proposed."),
					Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			Preview = preview;
			DialogResult = DialogResult.OK;
			Close();
		}

		/// <summary>One unsaved component intention, distinct from observed priority and enabled state.</summary>
		private sealed class DraftRow
		{
			/// <summary>Preserves the original enabled flag so unchanged/missing values need no rewrite.</summary>
			internal DraftRow(IniOrderSection section)
			{
				Name = section.Name;
				OriginalPriority = section.Priority;
				Enabled = OriginalEnabled = section.Enabled;
			}
			public string Name { get; }
			public int Position { get; internal set; }
			public int? OriginalPriority { get; }
			internal bool? OriginalEnabled { get; }
			internal bool? Enabled { get; set; }
			public string EnabledText => Enabled.HasValue ? Enabled.Value ? L("ManagedContent.Order.Enabled", "Enabled") :
				L("ManagedContent.Order.Disabled", "Disabled") : L("ManagedContent.Components.Unknown", "Unknown");
		}
	}
}
