using System;
using System.Windows.Forms;
using Nexus.Client.Games.Settings;
using Nexus.Client.Settings;
using Nexus.Client.Settings.UI;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven
{
	public class DataDrivenGameModeSettingsPage : ManagedFontUserControl, ISettingsGroupView
	{
		private readonly RequiredDirectoriesControl _directoriesControl;
		private readonly Label _workingDirectoryLabel;
		private readonly TextBox _workingDirectoryTextBox;
		private readonly Button _browseWorkingDirectoryButton;
		private readonly GroupBox _customCommandGroupBox;
		private TextBox _commandTextBox;
		private TextBox _argumentsTextBox;
		private readonly FolderBrowserDialog _workingDirectoryDialog;

		public DataDrivenGameModeSettingsPage(DataDrivenGameModeSettingsGroup settingsGroup)
		{
			SettingsGroup = settingsGroup;

			_directoriesControl = new RequiredDirectoriesControl
			{
				InstallInfoLabel = "Install Info*:",
				ModDirectoryLabel = "Mod Directory*:",
				Location = new System.Drawing.Point(0, 3),
				Size = new System.Drawing.Size(393, 85),
				ViewModel = settingsGroup.RequiredDirectoriesVM
			};

			_workingDirectoryLabel = new Label
			{
				AutoSize = true,
				Location = new System.Drawing.Point(1, 90),
				Text = settingsGroup.Title + " Mods Directory*:"
			};
			_workingDirectoryTextBox = new TextBox
			{
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
				Location = new System.Drawing.Point(24, 106),
				Size = new System.Drawing.Size(314, 20)
			};
			_browseWorkingDirectoryButton = new Button
			{
				Anchor = AnchorStyles.Top | AnchorStyles.Right,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Location = new System.Drawing.Point(344, 104),
				Text = "..."
			};
			_browseWorkingDirectoryButton.Click += SelectWorkingDirectory;

			_customCommandGroupBox = BuildCustomCommandGroupBox();
			_customCommandGroupBox.Visible = settingsGroup.AllowCustomLaunchCommand;
			_customCommandGroupBox.Enabled = settingsGroup.AllowCustomLaunchCommand;

			var restartLabel = new Label
			{
				Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
				AutoSize = true,
				Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Italic),
				Location = new System.Drawing.Point(233, 272),
				Text = "* requires application restart"
			};

			_workingDirectoryDialog = new FolderBrowserDialog();

			Controls.Add(_directoriesControl);
			Controls.Add(_customCommandGroupBox);
			Controls.Add(_browseWorkingDirectoryButton);
			Controls.Add(_workingDirectoryTextBox);
			Controls.Add(_workingDirectoryLabel);
			Controls.Add(restartLabel);

			BackColor = System.Drawing.Color.Transparent;
			Size = new System.Drawing.Size(403, 307);

			BindingHelper.CreateFullBinding(_workingDirectoryTextBox, () => _workingDirectoryTextBox.Text, settingsGroup, () => settingsGroup.InstallationPath);
			BindingHelper.CreateFullBinding(_commandTextBox, () => _commandTextBox.Text, settingsGroup, () => settingsGroup.CustomLaunchCommand);
			BindingHelper.CreateFullBinding(_argumentsTextBox, () => _argumentsTextBox.Text, settingsGroup, () => settingsGroup.CustomLaunchCommandArguments);
		}

		public SettingsGroup SettingsGroup { get; }

		private GroupBox BuildCustomCommandGroupBox()
		{
			var groupBox = new GroupBox
			{
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
				Location = new System.Drawing.Point(24, 133),
				Size = new System.Drawing.Size(346, 78),
				Text = "Custom Launch Command"
			};

			var commandLabel = new Label { AutoSize = true, Location = new System.Drawing.Point(16, 22), Text = "Command:" };
			var argumentsLabel = new Label { AutoSize = true, Location = new System.Drawing.Point(13, 48), Text = "Arguments:" };
			_commandTextBox = new TextBox { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Location = new System.Drawing.Point(79, 19), Size = new System.Drawing.Size(257, 20) };
			_argumentsTextBox = new TextBox { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Location = new System.Drawing.Point(79, 45), Size = new System.Drawing.Size(257, 20) };

			groupBox.Controls.Add(commandLabel);
			groupBox.Controls.Add(argumentsLabel);
			groupBox.Controls.Add(_commandTextBox);
			groupBox.Controls.Add(_argumentsTextBox);
			return groupBox;
		}

		private void SelectWorkingDirectory(object sender, EventArgs e)
		{
			_workingDirectoryDialog.SelectedPath = _workingDirectoryTextBox.Text;
			if (_workingDirectoryDialog.ShowDialog(FindForm()) == DialogResult.OK)
				_workingDirectoryTextBox.Text = _workingDirectoryDialog.SelectedPath;
		}
	}
}
