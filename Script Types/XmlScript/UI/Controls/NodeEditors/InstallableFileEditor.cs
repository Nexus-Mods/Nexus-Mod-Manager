using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.UI.Controls;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public partial class InstallableFileEditor : UserControl
	{
		private InstallableFileEditorVM m_vmlViewModel = null;
		private FileSelectionDialog m_fsdFileChooser = new FileSelectionDialog();

		#region Properties

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public InstallableFileEditorVM ViewModel
		{
			private get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				InstallableFileVM = value.InstallableFileVM;
				m_fsdFileChooser.FileSystemItems = value.FileSystemItems;
				value.PropertyChanged += new PropertyChangedEventHandler(ViewModel_PropertyChanged);
				value.InstallableFileValidated += new EventHandler(InstallableFileValidated);
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected InstallableFileVM InstallableFileVM
		{
			set
			{
				erpErrors.SetError(butSource, null);

				tbxSource.DataBindings.Clear();
				tbxDestination.DataBindings.Clear();
				ckbAlwaysInstall.DataBindings.Clear();
				ckbInstallIfUsable.DataBindings.Clear();
				nudPriority.DataBindings.Clear();

				BindingHelper.CreateFullBinding(tbxSource, () => tbxSource.Text, value, () => value.Source);
				BindingHelper.CreateFullBinding(tbxDestination, () => tbxDestination.Text, value, () => value.Destination);
				BindingHelper.CreateFullBinding(ckbAlwaysInstall, () => ckbAlwaysInstall.Checked, value, () => value.AlwaysInstall);
				BindingHelper.CreateFullBinding(ckbInstallIfUsable, () => ckbInstallIfUsable.Checked, value, () => value.InstallIfUsable);
				BindingHelper.CreateFullBinding(nudPriority, () => nudPriority.Value, value, () => value.Priority);

				butSave.Text = String.IsNullOrEmpty(value.Source) ? "Add" : "Save";
			}
		}

		#endregion

		public InstallableFileEditor()
		{
			InitializeComponent();
			nudPriority.Minimum = Int32.MinValue;
			nudPriority.Maximum = Int32.MaxValue;
		}

		private void EditControl_KeyDown(object sender, KeyEventArgs e)
		{
			switch (e.KeyCode)
			{
				case Keys.Escape:
					//this readvalue is to revert changes that have not been written to the InstallFileVM
					((Control)sender).DataBindings[0].ReadValue();
					//this reset is to revert changes that have been written to the InstallFileVM, but not
					// committed
					ViewModel.InstallableFileVM.Reset();
					ViewModel.ValidateSource();
					e.Handled = true;
					e.SuppressKeyPress = true;
					break;
				case Keys.Enter:
					ViewModel.SaveInstallableFile();
					e.Handled = true;
					e.SuppressKeyPress = true;
					tbxSource.Focus();
					break;
			}
		}

		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<InstallableFileEditorVM>(x => x.InstallableFileVM)))
				InstallableFileVM = ViewModel.InstallableFileVM;
		}

		private void butSave_Click(object sender, EventArgs e)
		{
			ViewModel.SaveInstallableFile();
		}

		private void InstallableFileValidated(object sender, EventArgs e)
		{
			erpErrors.SetError(butSource, ViewModel.Errors.GetError<InstallableFile>(x => x.Source));
		}

		private void tbxSource_Validated(object sender, EventArgs e)
		{
			ViewModel.ValidateSource();
		}

		private void butSource_Click(object sender, EventArgs e)
		{
			if (m_fsdFileChooser.ShowDialog(this) == DialogResult.OK)
			{
				tbxSource.Text = m_fsdFileChooser.SelectedPath;
				tbxSource.Focus();
			}
		}
	}
}
