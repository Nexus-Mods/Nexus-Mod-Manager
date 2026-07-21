using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.Commands;
using Nexus.Client.Commands.Generic;
using Nexus.UI.Controls;
using Nexus.Client.Util;
using Nexus.Client.BackgroundTasks.UI;
using Nexus.Client.BackgroundTasks;
using System.Collections.Generic;

namespace Nexus.Client.ModAuthoring.UI
{
	/// <summary>
	/// Encapsulates the packaging of a mod.
	/// </summary>
	public partial class ModPackagingForm : Form
	{
		private string m_strWindowTitle = null;
		private ModPackagingFormVM m_vmlViewModel = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected ModPackagingFormVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				sedScriptEditor.ViewModel = value.ScriptEditorVM;
				mieModInfo.ViewModel = value.InfoEditorVM;
				value.PropertyChanged += new PropertyChangedEventHandler(ViewModel_PropertyChanged);
				value.ModPackingStarted += new EventHandler<EventArgs<Nexus.Client.BackgroundTasks.IBackgroundTask>>(ViewModel_ModPackingStarted);
				value.ProjectValidated += new EventHandler(ViewModel_ProjectValidated);
				BindProject(value.ModProject);

				tsbMakeMod.Click += new EventHandler(ToolStripButton_Click);

				new ToolStripItemCommandBinding<string>(tsbSave, value.SaveCommand, () => null);
				new ToolStripItemCommandBinding(tsbNew, value.NewCommand);
				new ToolStripItemCommandBinding(tsbOpen, value.OpenCommand);
				new ToolStripItemCommandBinding<string>(tsbMakeMod, value.BuildCommand, () => null);
				value.GetOpenPath = GetOpenPath;
				value.GetProjectSavePath = GetProjectSavePath;
				value.GetNewModSavePath = GetNewModSavePath;
				value.ConfirmSaveCurrentProject = ConfirmSaveCurrentProject;
				value.GetIgnoreWarnings = GetIgnoreWarnings;
			}
		}

		#endregion

		#region Constructor

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ModPackagingForm(ModPackagingFormVM p_vmlModPackagingVM)
		{
			InitializeComponent();
			m_strWindowTitle = this.Text;
			this.DoubleBuffered = true;

			ViewModel = p_vmlModPackagingVM;
		}

		#endregion

		#region View Model Callback Delegates

		/// <summary>
		/// Gets whether the current <see cref="Project"/> should be saved.
		/// </summary>
		/// <returns><c>true</c> if the user wants to save the current <see cref="Project"/>, or
		/// <c>false</c> if the user wants to discard any changes to the current <see cref="Project"/>, or
		/// <c>null</c> if the user wants to keep the current <see cref="Project"/> as the <see cref="Project"/>
		/// being edited.</returns>
		private bool? ConfirmSaveCurrentProject()
		{
			if (ViewModel.ModProject.IsDirty)
			{
				switch (MessageBox.Show(this, "Would you like to save the current project?", "Save Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
				{
					case DialogResult.Cancel:
						return null;
					case DialogResult.Yes:
						return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Gets the path where the project should be saved.
		/// </summary>
		/// <returns>The path where the project should be saved.</returns>
		protected string GetProjectSavePath()
		{
			if (sfdProject.ShowDialog(this) == DialogResult.OK)
				return sfdProject.FileName;
			return null;
		}

		/// <summary>
		/// Gets the path where the new mod should be saved.
		/// </summary>
		/// <returns>The path where the new mod should be saved.</returns>
		protected string GetNewModSavePath()
		{
			if (sfdNewMod.ShowDialog(this) == DialogResult.OK)
				return sfdNewMod.FileName;
			return null;
		}

		/// <summary>
		/// Gets the path of the project to open.
		/// </summary>
		/// <returns>The path of the project to open.</returns>
		protected string GetOpenPath()
		{
			if (ofdProject.ShowDialog(this) == DialogResult.OK)
				return ofdProject.FileName;
			return null;
		}

		/// <summary>
		/// Determines if the validation warnings should be ignored.
		/// </summary>
		/// <returns><c>true</c> if the validation warnings should be ignored;
		/// <c>false</c> otherwise.</returns>
		protected bool GetIgnoreWarnings()
		{
			return (MessageBox.Show(this, "There are warnings." + Environment.NewLine + "Warnings can be ignored, but they can indicate missing information that you meant to enter." + Environment.NewLine + "Would you like to continue?", "Warning", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK);
		}

		#endregion

		/// <summary>
		/// Binds the <see cref="Project"/> to the control.
		/// </summary>
		/// <param name="p_prjProject">The <see cref="Project"/> to bind to the control.</param>
		protected void BindProject(Project p_prjProject)
		{
			p_prjProject.PropertyChanged -= Project_PropertyChanged;
			p_prjProject.PropertyChanged += new PropertyChangedEventHandler(Project_PropertyChanged);
			SetWindowTitle(p_prjProject);
			redReadme.DataBindings.Clear();
			ftvModFilesEditor.DataBindings.Clear();
			BindingHelper.CreateFullBinding(redReadme, () => redReadme.Readme, p_prjProject, () => p_prjProject.ModReadme);
			BindingHelper.CreateFullBinding(ftvModFilesEditor, () => ftvModFilesEditor.Sources, p_prjProject, () => p_prjProject.ModFiles).DataSourceUpdateMode = DataSourceUpdateMode.OnPropertyChanged;
		}

		/// <summary>
		/// Sets the window's title to reflect project name and dirty status.
		/// </summary>
		/// <param name="p_prjProject">The <see cref="Project"/> being edited.</param>
		protected void SetWindowTitle(Project p_prjProject)
		{
			string strTitle = String.Format("{0}{1}", p_prjProject.ModName, p_prjProject.IsDirty ? "*" : "");
			this.Text = String.Format("{0}{1}{2}", m_strWindowTitle, String.IsNullOrEmpty(strTitle) ? "" : " - ", strTitle);
		}

		#region Property Change Handlers

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the view model.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName(() => ViewModel.ModProject)))
				BindProject(ViewModel.ModProject);
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the <see cref="Project"/>
		/// being edited.
		/// </summary>
		/// <remarks>
		/// This method alters the form display to indicate whether the <see cref="Project"/> is dirty.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void Project_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			SetWindowTitle((Project)sender);
		}

		#endregion


		/// <summary>
		/// Handles the <see cref="ModPackagingFormVM.ProjectValidated"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays warning and error icons.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ViewModel_ProjectValidated(object sender, EventArgs e)
		{
			Dictionary<string, VerticalTabPage> dicProperties = new Dictionary<string, VerticalTabPage>()
																	{	{ ObjectHelper.GetPropertyName<Project>(x => x.ModFiles), vtpModFiles },
																		{ ObjectHelper.GetPropertyName<Project>(x => x.InstallScript), vtpScript},
																		{ ObjectHelper.GetPropertyName<Project>(x => x.ModName), vtpModInfo},
																		{ ObjectHelper.GetPropertyName<Project>(x => x.ModReadme), vtpReadme}
																	};
			foreach (string strProperty in dicProperties.Keys)
			{
				if (String.IsNullOrEmpty(ViewModel.Errors[strProperty]))
					sspWarnings.SetStatus(dicProperties[strProperty], ViewModel.Warnings[strProperty]);
				else
					sspErrors.SetStatus(dicProperties[strProperty], ViewModel.Errors[strProperty]);
			}
		}

		/// <summary>
		/// Handles the <see cref="ModPackagingFormVM.ModPackingStarted"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_ModPackingStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ModPackingStarted, sender, e);
				return;
			}
			ProgressDialog.ShowDialog(this, e.Argument);
		}

		/// <summary>
		/// Handles the <see cref="VerticalTabControl.SelectedTabPageChanged"/> event of the navigation
		/// tab control.
		/// </summary>
		/// <remarks>
		/// This makes sure each page has the information it requires from the other pages.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="VerticalTabControl.TabPageEventArgs"/> describing the event arguments.</param>
		private void verticalTabControl1_SelectedTabPageChanged(object sender, VerticalTabControl.TabPageEventArgs e)
		{
			if (e.TabPage == vtpScript)
				ViewModel.ScriptEditorVM.ModFiles = ftvModFilesEditor.Sources;
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the build button.
		/// </summary>
		/// <remarks>
		/// This makes sure all control values have been committed, so that they can be validated.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ToolStripButton_Click(object sender, EventArgs e)
		{
			this.ValidateChildren();
		}
	}
}
