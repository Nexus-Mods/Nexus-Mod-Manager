using System;
using System.Windows.Forms;
using Nexus.Client.Games.Settings;
using Nexus.Client.UI;

namespace Nexus.Client.Games.DataDriven
{
	public class DataDrivenSetupForm : ManagedFontForm, IView
	{
		private readonly SetupDirectoriesControl _directoriesControl;
		private SetupBaseVM _viewModel;

		public DataDrivenSetupForm(SetupBaseVM viewModel)
		{
			Text = string.Format("{0} Setup", viewModel.GameModeDescriptor.Name);
			StartPosition = FormStartPosition.CenterScreen;
			Size = new System.Drawing.Size(520, 420);
			MinimizeBox = false;
			MaximizeBox = false;

			var title = new Label
			{
				AutoSize = true,
				Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
				Location = new System.Drawing.Point(12, 12),
				Text = Text
			};

			_directoriesControl = new SetupDirectoriesControl
			{
				Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
				InstallInfoLabel = "Install Info:",
				ModDirectoryLabel = "Mod Directory:",
				Location = new System.Drawing.Point(12, 44),
				Size = new System.Drawing.Size(480, 270)
			};

			var finishButton = new Button
			{
				Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
				DialogResult = DialogResult.OK,
				Location = new System.Drawing.Point(326, 334),
				Size = new System.Drawing.Size(80, 25),
				Text = "Finish"
			};
			finishButton.Click += FinishSetup;

			var cancelButton = new Button
			{
				Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
				DialogResult = DialogResult.Cancel,
				Location = new System.Drawing.Point(412, 334),
				Size = new System.Drawing.Size(80, 25),
				Text = "Cancel"
			};
			cancelButton.Click += (sender, args) => Close();

			Controls.Add(title);
			Controls.Add(_directoriesControl);
			Controls.Add(finishButton);
			Controls.Add(cancelButton);
			AcceptButton = finishButton;
			CancelButton = cancelButton;

			ViewModel = viewModel;
		}

		public SetupBaseVM ViewModel
		{
			get => _viewModel;
			set
			{
				_viewModel = value;
				_directoriesControl.ViewModel = _viewModel.SetupDirectoriesControlVM;
			}
		}

		private void FinishSetup(object sender, EventArgs e)
		{
			if (ViewModel.Save())
				Close();
			else
				DialogResult = DialogResult.None;
		}
	}
}
