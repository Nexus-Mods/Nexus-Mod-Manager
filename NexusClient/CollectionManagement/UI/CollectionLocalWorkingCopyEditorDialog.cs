using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Small C9 editor for the mutable metadata/member set of one Local Collection working copy.</summary>
	internal sealed class CollectionLocalWorkingCopyEditorDialog : Form
	{
		private readonly TextBox _nameTextBox;
		private readonly TextBox _summaryTextBox;
		private readonly DataGridView _memberGrid;
		private readonly Button _okButton;

		internal CollectionLocalWorkingCopyEditorDialog(CollectionLocalWorkingCopyEditSnapshot snapshot)
		{
			if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
			Text = L("Collections.WorkingCopy.EditTitle", "Edit Local working copy");
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.Sizable;
			MinimizeBox = false;
			MaximizeBox = true;
			ShowInTaskbar = false;
			MinimumSize = new Size(700, 480);
			ClientSize = new Size(820, 560);

			var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(10) };
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(root);

			root.Controls.Add(new Label
			{
				AutoSize = true,
				Text = L("Collections.WorkingCopy.Name", "Name:"),
				Margin = new Padding(0, 0, 0, 4)
			}, 0, 0);
			_nameTextBox = new TextBox { Dock = DockStyle.Top, Text = snapshot.Definition.DisplayName ?? String.Empty };
			root.Controls.Add(_nameTextBox, 0, 1);
			root.Controls.Add(new Label
			{
				AutoSize = true,
				Text = L("Collections.WorkingCopy.Summary", "Summary:"),
				Margin = new Padding(0, 6, 0, 4)
			}, 0, 2);
			_summaryTextBox = new TextBox
			{
				Dock = DockStyle.Fill,
				Multiline = true,
				ScrollBars = ScrollBars.Vertical,
				Text = snapshot.Definition.Summary ?? String.Empty,
				Margin = new Padding(0, 6, 0, 6)
			};
			root.Controls.Add(_summaryTextBox, 0, 3);

			string note = L("Collections.WorkingCopy.MemberEditHelp",
				"Edit the cloned recipe only. Unchecking Include removes that member from the next Local revision; Optional controls whether the recipe requires it. Installed mods are not changed.");
			note += " " + LanguageManager.Format("Collections.WorkingCopy.SavedRevisionCount",
				"Saved Local revisions: {0}.", snapshot.SavedRevisionCount);
			if (snapshot.LockedMemberCount > 0)
				note += " " + LanguageManager.Format("Collections.WorkingCopy.LockedMembers",
					"{0} member(s) have unresolved identity and are preserved unchanged.", snapshot.LockedMemberCount);
			root.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(790, 0), Text = note, Margin = new Padding(0, 0, 0, 6) }, 0, 4);

			_memberGrid = new DataGridView
			{
				Dock = DockStyle.Fill,
				AllowUserToAddRows = false,
				AllowUserToDeleteRows = false,
				AllowUserToOrderColumns = false,
				AutoGenerateColumns = false,
				RowHeadersVisible = false,
				SelectionMode = DataGridViewSelectionMode.FullRowSelect,
				MultiSelect = false
			};
			_memberGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = L("Collections.WorkingCopy.Include", "Include"), Width = 60 });
			_memberGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Optional", HeaderText = L("Collections.WorkingCopy.Optional", "Optional"), Width = 68 });
			_memberGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Member", HeaderText = L("Collections.Fields.Member", "Member"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
			_memberGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Identity", HeaderText = L("Collections.WorkingCopy.MemberIdentity", "Member identity"), Width = 245, ReadOnly = true });
			foreach (CollectionLocalWorkingCopyMemberEditState member in snapshot.Members)
			{
				int rowIndex = _memberGrid.Rows.Add(member.Included,
					member.Requirement == CollectionMemberRequirement.Optional, member.DisplayName, member.MemberKey.ToString());
				_memberGrid.Rows[rowIndex].Tag = member;
			}
			root.Controls.Add(_memberGrid, 0, 5);

			_okButton = new Button { AutoSize = true, Text = L("Common.Save", "Save"), DialogResult = DialogResult.OK };
			var cancel = new Button { AutoSize = true, Text = L("Common.Cancel", "Cancel"), DialogResult = DialogResult.Cancel };
			var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(_okButton);
			root.Controls.Add(buttons, 0, 6);
			AcceptButton = _okButton;
			CancelButton = cancel;
			_okButton.Click += OkButton_Click;
		}

		internal string DisplayName { get { return _nameTextBox.Text.Trim(); } }
		internal string Summary { get { return _summaryTextBox.Text; } }

		internal IReadOnlyList<CollectionLocalWorkingCopyMemberDecision> GetDecisions()
		{
			var result = new List<CollectionLocalWorkingCopyMemberDecision>();
			foreach (DataGridViewRow row in _memberGrid.Rows)
			{
				CollectionLocalWorkingCopyMemberEditState member = row.Tag as CollectionLocalWorkingCopyMemberEditState;
				if (member == null) continue;
				bool included = Convert.ToBoolean(row.Cells["Include"].Value ?? false);
				bool optional = Convert.ToBoolean(row.Cells["Optional"].Value ?? false);
				result.Add(new CollectionLocalWorkingCopyMemberDecision(member.MemberKey, included,
					optional ? CollectionMemberRequirement.Optional : CollectionMemberRequirement.Required));
			}
			return result.AsReadOnly();
		}

		private void OkButton_Click(object sender, EventArgs e)
		{
			if (!String.IsNullOrWhiteSpace(DisplayName)) return;
			MessageBox.Show(this, L("Collections.WorkingCopy.NameRequired", "Enter a name for the Local working copy."),
				Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			DialogResult = DialogResult.None;
			_nameTextBox.Focus();
		}

		private static string L(string key, string fallback)
		{
			return LanguageManager.Get(key, fallback);
		}
	}
}
