using System;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.BackgroundTasks.UI;

namespace Nexus.Client.Games
{
	/// <summary>
	/// Prompts the user to select the working directory.
	/// </summary>
	/// <remarks>
	/// This form also provides an auto-detect feature if the user is unsure of which folder to select.
	/// </remarks>
	public partial class WorkingDirectorySelectionForm : Form
	{
		private string[] m_strSearchFiles = null;

		#region Properties

		/// <summary>
		/// Gets the selected working directory.
		/// </summary>
		/// <value>The selected working directory.</value>
		public string WorkingDirectory
		{
			get
			{
				return tbxWorkingDirectory.Text;
			}
			set
			{
				tbxWorkingDirectory.Text = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strMessage">The message to display in the window.</param>
		/// <param name="p_strLabel">The label of the working directory textbox.</param>
		/// <param name="p_strSearchFiles">The files to search for when auto-detecting.</param>
		public WorkingDirectorySelectionForm(string p_strMessage, string p_strLabel, string[] p_strSearchFiles)
		{
			m_strSearchFiles = p_strSearchFiles;
			InitializeComponent();
			autosizeLabel1.Text = p_strMessage;
			label2.Text = p_strLabel;
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the elipses button next to
		/// the working directory textbox.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog so the use can select the working directory.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelect_Click(object sender, EventArgs e)
		{
			fbdWorkingDirectory.SelectedPath = tbxWorkingDirectory.Text;
			if (fbdWorkingDirectory.ShowDialog() != DialogResult.Cancel)
				tbxWorkingDirectory.Text = fbdWorkingDirectory.SelectedPath;
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the OK button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butOK_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.OK;
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the auto-detect button.
		/// </summary>
		/// <remarks>
		/// This launches the auto-detection algorithm on another process using the
		/// <see cref="ProgressDialog"/> class.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butAutoDetect_Click(object sender, EventArgs e)
		{
			InstallationPathAutoDetector padDetector = new InstallationPathAutoDetector(ConfirmInstallationPath);
			padDetector.TaskEnded += new EventHandler<Nexus.Client.BackgroundTasks.TaskEndedEventArgs>(padDetector_TaskEnded);
			padDetector.Detect(m_strSearchFiles);
			ProgressDialog.ShowDialog(this, padDetector);
		}
		
		/// <summary>
		/// Handles the <see cref="IBackgroundTask.TaskEnded"/> event of the auto-detect task.
		/// </summary>
		/// <remarks>
		/// This retrieves the discovered installation path from the auto-detect task.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		void padDetector_TaskEnded(object sender, TaskEndedEventArgs e)
		{
			if (Disposing || IsDisposed)
				return;
			if (InvokeRequired)
			{
				try
				{
					Invoke((MethodInvoker)(() => padDetector_TaskEnded(sender, e)));
				}
				catch (ObjectDisposedException)
				{
					//if the control is disposed, we don't need to do anything
				}
				return;
			}
			if (e.Status != TaskStatus.Running)
			{
				if (!String.IsNullOrEmpty((string)e.ReturnValue))
					tbxWorkingDirectory.Text = (string)e.ReturnValue;
				else
					MessageBox.Show(this, "Could not find Installation Path.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		/// <summary>
		/// This asks the user to confirm that the given path is the correct installation path.
		/// </summary>
		/// <param name="p_strPath">The installation path to confirm.</param>
		/// <returns><c>true</c> if the given path is correct;
		/// <c>false</c> otherwise.</returns>
		protected bool ConfirmInstallationPath(string p_strPath)
		{
			if (InvokeRequired)
				return (bool)Invoke((Func<string,bool>)ConfirmInstallationPath, p_strPath);
			return (MessageBox.Show(this, "Found: " + p_strPath + Environment.NewLine + "Is this correct?", "Found Path", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
		}
	}
}
