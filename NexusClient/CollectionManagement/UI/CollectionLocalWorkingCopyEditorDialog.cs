using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Themed C9 editor for the mutable metadata/member set of one Local Collection working copy.</summary>
	internal sealed class CollectionLocalWorkingCopyEditorDialog : ManagedFontXtraForm
	{
		private readonly TextEdit _nameTextBox;
		private readonly MemoEdit _summaryTextBox;
		private readonly GridView _memberView;
		private readonly BindingList<MemberEditRow> _rows;
		private readonly SimpleButton _okButton;
		private readonly DevExpressDisplaySettings _displaySettings;

		internal CollectionLocalWorkingCopyEditorDialog(CollectionLocalWorkingCopyEditSnapshot snapshot)
		{
			if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
			_displaySettings = DevExpressDisplaySettings.CreateFromSettings(Properties.Settings.Default);
			Font = _displaySettings.Font;
			AutoScaleMode = AutoScaleMode.Font;
			Text = L("Collections.WorkingCopy.EditTitle", "Edit Local working copy");
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.Sizable;
			MinimizeBox = false;
			MaximizeBox = true;
			ShowInTaskbar = false;
			MinimumSize = new Size(700, 480);
			ClientSize = new Size(820, 560);

			var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(10), BackColor = Color.Transparent };
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(root);

			root.Controls.Add(new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Horizontal,
				Text = L("Collections.WorkingCopy.Name", "Name:"),
				Margin = new Padding(0, 0, 0, 4)
			}, 0, 0);
			_nameTextBox = new TextEdit { Dock = DockStyle.Top, Text = snapshot.Definition.DisplayName ?? String.Empty };
			root.Controls.Add(_nameTextBox, 0, 1);
			root.Controls.Add(new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Horizontal,
				Text = L("Collections.WorkingCopy.Summary", "Summary:"),
				Margin = new Padding(0, 6, 0, 4)
			}, 0, 2);
			_summaryTextBox = new MemoEdit
			{
				Dock = DockStyle.Fill,
				Text = snapshot.Definition.Summary ?? String.Empty,
				Margin = new Padding(0, 6, 0, 6)
			};
			_summaryTextBox.Properties.ScrollBars = ScrollBars.Vertical;
			root.Controls.Add(_summaryTextBox, 0, 3);

			string note = L("Collections.WorkingCopy.MemberEditHelp",
				"Edit the cloned recipe only. Unchecking Include removes that member from the next Local revision; Optional controls whether the recipe requires it. Installed mods are not changed.");
			note += " " + LanguageManager.Format("Collections.WorkingCopy.SavedRevisionCount",
				"Saved Local revisions: {0}.", snapshot.SavedRevisionCount);
			if (snapshot.LockedMemberCount > 0)
				note += " " + LanguageManager.Format("Collections.WorkingCopy.LockedMembers",
					"{0} member(s) have unresolved identity and are preserved unchanged.", snapshot.LockedMemberCount);
			var noteLabel = new LabelControl
			{
				Dock = DockStyle.Fill, AutoSizeMode = LabelAutoSizeMode.Vertical, UseMnemonic = false,
				Text = note, Margin = new Padding(0, 0, 0, 6)
			};
			noteLabel.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
			noteLabel.Appearance.Options.UseTextOptions = true;
			root.Controls.Add(noteLabel, 0, 4);

			_rows = new BindingList<MemberEditRow>(snapshot.Members.Select(member => new MemberEditRow(member)).ToList());
			var memberGrid = new GridControl { Dock = DockStyle.Fill };
			_memberView = new GridView(memberGrid);
			memberGrid.MainView = _memberView;
			memberGrid.ViewCollection.Add(_memberView);
			_memberView.OptionsView.ShowGroupPanel = false;
			_memberView.OptionsView.ShowIndicator = false;
			_memberView.OptionsView.ColumnAutoWidth = false;
			_memberView.OptionsSelection.MultiSelect = false;
			_memberView.OptionsCustomization.AllowColumnMoving = false;
			_memberView.OptionsBehavior.AllowAddRows = DefaultBoolean.False;
			_memberView.OptionsBehavior.AllowDeleteRows = DefaultBoolean.False;
			_memberView.OptionsBehavior.EditorShowMode = EditorShowMode.Click;
			RepositoryItemCheckEdit check = new RepositoryItemCheckEdit { AllowGrayed = false };
			check.EditValueChanged += (sender, args) => _memberView.PostEditor();
			memberGrid.RepositoryItems.Add(check);
			GridColumn include = _memberView.Columns.AddVisible(nameof(MemberEditRow.Included), L("Collections.WorkingCopy.Include", "Include"));
			include.ColumnEdit = check;
			include.Width = 72;
			GridColumn optional = _memberView.Columns.AddVisible(nameof(MemberEditRow.Optional), L("Collections.WorkingCopy.Optional", "Optional"));
			optional.ColumnEdit = check;
			optional.Width = 80;
			GridColumn name = _memberView.Columns.AddVisible(nameof(MemberEditRow.DisplayName), L("Collections.Fields.Member", "Member"));
			name.OptionsColumn.AllowEdit = false;
			name.Width = 360;
			GridColumn identity = _memberView.Columns.AddVisible(nameof(MemberEditRow.Identity), L("Collections.WorkingCopy.MemberIdentity", "Member identity"));
			identity.OptionsColumn.AllowEdit = false;
			identity.Width = 270;
			memberGrid.DataSource = _rows;
			root.Controls.Add(memberGrid, 0, 5);

			_okButton = new SimpleButton { AutoSize = true, Text = L("Common.Save", "Save"), DialogResult = DialogResult.OK };
			var cancel = new SimpleButton { AutoSize = true, Text = L("Common.Cancel", "Cancel"), DialogResult = DialogResult.Cancel };
			var buttons = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
				Margin = new Padding(0, 8, 0, 0), BackColor = Color.Transparent
			};
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(_okButton);
			root.Controls.Add(buttons, 0, 6);
			AcceptButton = _okButton;
			CancelButton = cancel;
			_okButton.Click += OkButton_Click;
			DevExpressDisplaySettingsApplier.ApplyToControlTree(this, _displaySettings);
		}

		internal string DisplayName { get { return _nameTextBox.Text.Trim(); } }
		internal string Summary { get { return _summaryTextBox.Text; } }

		internal IReadOnlyList<CollectionLocalWorkingCopyMemberDecision> GetDecisions()
		{
			// Commit any in-place checkbox editor before reading the underlying identity-bound rows.
			_memberView.CloseEditor();
			_memberView.UpdateCurrentRow();
			return _rows.Select(row => new CollectionLocalWorkingCopyMemberDecision(row.Member.MemberKey, row.Included,
				row.Optional ? CollectionMemberRequirement.Optional : CollectionMemberRequirement.Required)).ToList().AsReadOnly();
		}

		private void OkButton_Click(object sender, EventArgs e)
		{
			if (!String.IsNullOrWhiteSpace(DisplayName)) return;
			XtraMessageBox.Show(this, L("Collections.WorkingCopy.NameRequired", "Enter a name for the Local working copy."),
				Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			DialogResult = DialogResult.None;
			_nameTextBox.Focus();
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (disposing && _displaySettings != null) _displaySettings.Dispose();
		}

		private sealed class MemberEditRow
		{
			internal MemberEditRow(CollectionLocalWorkingCopyMemberEditState member)
			{
				Member = member;
				Included = member.Included;
				Optional = member.Requirement == CollectionMemberRequirement.Optional;
				DisplayName = member.DisplayName;
				Identity = member.MemberKey.ToString();
			}

			internal CollectionLocalWorkingCopyMemberEditState Member { get; }
			public bool Included { get; set; }
			public bool Optional { get; set; }
			public string DisplayName { get; }
			public string Identity { get; }
		}

		private static string L(string key, string fallback)
		{
			return LanguageManager.Get(key, fallback);
		}
	}
}
