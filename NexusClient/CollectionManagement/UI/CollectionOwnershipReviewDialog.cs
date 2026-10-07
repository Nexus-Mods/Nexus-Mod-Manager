using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Reviews unknown Collection ownership in one grid before saving any decisions.</summary>
	internal sealed class CollectionOwnershipReviewDialog : ManagedFontXtraForm
	{
		private readonly BindingList<OwnershipRow> _rows;
		private readonly GridView _gridView;
		private readonly LabelControl _selectionSummary;
		private readonly DevExpressDisplaySettings _displaySettings;

		/// <summary>Creates a batch review whose unchecked members are kept independently by default.</summary>
		internal CollectionOwnershipReviewDialog(IEnumerable<CollectionRevisionUpdateMemberPlan> members)
		{
			if (members == null) throw new ArgumentNullException(nameof(members));
			_rows = new BindingList<OwnershipRow>(members.Select(x => new OwnershipRow(x))
				.OrderBy(x => x.ModName, StringComparer.CurrentCultureIgnoreCase).ToList());
			_displaySettings = DevExpressDisplaySettings.CreateFromSettings(Properties.Settings.Default);
			Font = _displaySettings.Font;
			AutoScaleMode = AutoScaleMode.Font;
			Text = LanguageManager.Get("Collections.Ownership.WindowTitle", "Review Collection ownership");
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.Sizable;
			MinimizeBox = false;
			ShowInTaskbar = false;
			MinimumSize = new Size(720, 480);
			ClientSize = new Size(860, 600);

			TableLayoutPanel root = new TableLayoutPanel
			{
				Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12)
			};
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			DevExpressDisplaySettingsApplier.ApplySkinSurface(root);
			Controls.Add(root);

			LabelControl instructions = new LabelControl
			{
				Dock = DockStyle.Fill, AutoSizeMode = LabelAutoSizeMode.Vertical, UseMnemonic = false,
				Margin = new Padding(0, 0, 0, 8),
				Text = LanguageManager.Get("Collections.Ownership.GridHelp",
					"Check the mods you use only through Collections. NMM may replace or remove them during an approved revision change or Collection uninstall, provided no other Collection needs them.\r\n\r\nLeave mods unchecked to keep their installed versions independently, including mods that were present before the Collection. All mods start unchecked.\r\n\r\nSave choices records ownership and opens a new comparison. Changing installed mods still requires separate approval. Cancel discards every choice in this window.")
			};
			instructions.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
			instructions.Appearance.Options.UseTextOptions = true;
			root.Controls.Add(instructions, 0, 0);

			FlowLayoutPanel bulkActions = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 6)
			};
			DevExpressDisplaySettingsApplier.ApplySkinSurface(bulkActions);
			SimpleButton checkAll = new SimpleButton
			{
				AutoSize = true, Text = LanguageManager.Get("Collections.Ownership.CheckAll", "Check all mods")
			};
			SimpleButton uncheckAll = new SimpleButton
			{
				AutoSize = true, Text = LanguageManager.Get("Collections.Ownership.UncheckAll", "Uncheck all mods")
			};
			checkAll.Click += (sender, args) => SetAllChoices(true);
			uncheckAll.Click += (sender, args) => SetAllChoices(false);
			bulkActions.Controls.Add(checkAll);
			bulkActions.Controls.Add(uncheckAll);
			root.Controls.Add(bulkActions, 0, 1);

			GridControl grid = new GridControl { Dock = DockStyle.Fill, Margin = new Padding(0) };
			_gridView = new GridView(grid);
			grid.MainView = _gridView;
			grid.ViewCollection.Add(_gridView);
			_gridView.OptionsBehavior.AllowAddRows = DefaultBoolean.False;
			_gridView.OptionsBehavior.AllowDeleteRows = DefaultBoolean.False;
			_gridView.OptionsBehavior.EditorShowMode = EditorShowMode.Click;
			_gridView.OptionsCustomization.AllowColumnMoving = false;
			_gridView.OptionsView.ShowGroupPanel = false;
			_gridView.OptionsView.ShowIndicator = false;
			_gridView.OptionsFind.AlwaysVisible = true;
			RepositoryItemCheckEdit checkbox = new RepositoryItemCheckEdit { AllowGrayed = false };
			grid.RepositoryItems.Add(checkbox);
			GridColumn collectionOnly = _gridView.Columns.AddVisible(nameof(OwnershipRow.CollectionOnly),
				LanguageManager.Get("Collections.Ownership.CollectionOnly", "Collection only"));
			collectionOnly.ColumnEdit = checkbox;
			collectionOnly.Width = 130;
			collectionOnly.OptionsColumn.FixedWidth = true;
			GridColumn name = _gridView.Columns.AddVisible(nameof(OwnershipRow.ModName),
				LanguageManager.Get("Collections.Ownership.ModName", "Installed mod"));
			name.OptionsColumn.AllowEdit = false;
			name.Width = 680;
			root.Controls.Add(grid, 0, 2);

			_selectionSummary = new LabelControl
			{
				Dock = DockStyle.Fill, AutoSizeMode = LabelAutoSizeMode.Vertical, UseMnemonic = false,
				Margin = new Padding(0, 8, 0, 0)
			};
			_selectionSummary.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
			_selectionSummary.Appearance.Options.UseTextOptions = true;
			root.Controls.Add(_selectionSummary, 0, 3);
			grid.DataSource = _rows;
			checkbox.EditValueChanged += (sender, args) => _gridView.PostEditor();
			_gridView.CellValueChanged += (sender, args) => UpdateSelectionSummary();

			SimpleButton save = new SimpleButton
			{
				AutoSize = true, Text = LanguageManager.Get("Collections.Ownership.SaveChoices", "Save choices")
			};
			save.Click += (sender, args) =>
			{
				CommitCurrentChoice();
				DialogResult = DialogResult.OK;
			};
			SimpleButton cancel = new SimpleButton
			{
				AutoSize = true, Text = LanguageManager.Get("Common.Action.Cancel", "Cancel"),
				DialogResult = DialogResult.Cancel
			};
			NmmIconProvider.BindDialogButton(save, NmmIconAction.Apply);
			NmmIconProvider.BindDialogButton(cancel, NmmIconAction.Cancel);
			FlowLayoutPanel buttons = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
				Margin = new Padding(0, 8, 0, 0)
			};
			DevExpressDisplaySettingsApplier.ApplySkinSurface(buttons);
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(save);
			root.Controls.Add(buttons, 0, 4);
			AcceptButton = save;
			CancelButton = cancel;
			DevExpressDisplaySettingsApplier.ApplyToControlTree(this, _displaySettings);
			UpdateSelectionSummary();
		}

		/// <summary>Gets the exact members checked for Collection-only use.</summary>
		internal IReadOnlyList<CollectionMemberKey> CollectionOnlyMembers
		{
			get { return _rows.Where(x => x.CollectionOnly).Select(x => x.MemberKey).ToArray(); }
		}

		/// <summary>Gets the exact unchecked members to retain independently.</summary>
		internal IReadOnlyList<CollectionMemberKey> IndependentMembers
		{
			get { return _rows.Where(x => !x.CollectionOnly).Select(x => x.MemberKey).ToArray(); }
		}

		/// <summary>Commits the active checkbox before a bulk action or saving the complete review.</summary>
		private void CommitCurrentChoice()
		{
			_gridView.PostEditor();
			_gridView.CloseEditor();
		}

		/// <summary>Sets every member's choice, including rows hidden by the grid search.</summary>
		private void SetAllChoices(bool collectionOnly)
		{
			CommitCurrentChoice();
			_gridView.BeginDataUpdate();
			try
			{
				foreach (OwnershipRow row in _rows) row.CollectionOnly = collectionOnly;
				_gridView.RefreshData();
			}
			finally
			{
				_gridView.EndDataUpdate();
			}
			UpdateSelectionSummary();
		}

		/// <summary>Shows the outcome of all choices, including members hidden by a search.</summary>
		private void UpdateSelectionSummary()
		{
			int collectionOnly = _rows.Count(x => x.CollectionOnly);
			_selectionSummary.Text = LanguageManager.Format("Collections.Ownership.SelectionSummary",
				"All {0} mods: {1} Collection only; {2} kept independently. Bulk buttons apply to all mods, including rows hidden by search.",
				_rows.Count, collectionOnly, _rows.Count - collectionOnly);
		}

		/// <summary>Releases the dialog's display font after disposing its controls.</summary>
		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (disposing && _displaySettings != null) _displaySettings.Dispose();
		}

		/// <summary>Keeps a grid choice bound to its stable member identity when rows are sorted or filtered.</summary>
		private sealed class OwnershipRow
		{
			/// <summary>Creates one unchecked choice for a member requiring an ownership decision.</summary>
			internal OwnershipRow(CollectionRevisionUpdateMemberPlan member)
			{
				if (member == null || !member.RequiresStandaloneUseConfirmation)
					throw new ArgumentException("An ownership row must require a standalone-use decision.", nameof(member));
				MemberKey = member.MemberKey;
				ResolvedCollectionMemberPlan source = member.OldMember ?? member.NewMember;
				ModName = source == null || String.IsNullOrWhiteSpace(source.DisplayName)
					? member.MemberKey.ToString() : source.DisplayName;
			}

			internal CollectionMemberKey MemberKey { get; }
			public string ModName { get; }
			public bool CollectionOnly { get; set; }
		}
	}
}
