using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.UI.Controls;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public partial class OptionInfoEditor : NodeEditor
	{
		private OptionInfoEditorVM m_vmlViewModel = null;
		private FileSelectionDialog m_fsdFileChooser = new FileSelectionDialog();

		#region Properties

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public OptionInfoEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				OptionInfoVM = value.OptionInfoVM;
				m_fsdFileChooser.FileSystemItems = value.ModFiles;
				value.OptionValidated += new EventHandler(OptionValidated);
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected OptionInfoVM OptionInfoVM
		{
			set
			{
				erpErrors.SetError(tbxName, null);

				tbxName.DataBindings.Clear();
				tbxDescription.DataBindings.Clear();
				tbxImagePath.DataBindings.Clear();

				BindingHelper.CreateFullBinding(tbxName, () => tbxName.Text, value, () => value.Name);
				BindingHelper.CreateFullBinding(tbxDescription, () => tbxDescription.Text, value, () => value.Description);
				BindingHelper.CreateFullBinding(tbxImagePath, () => tbxImagePath.Text, value, () => value.ImagePath);

				LoadImage();
				m_fsdFileChooser.SelectedPath = value.ImagePath;
			}
		}

		#endregion

		public OptionInfoEditor()
		{
			InitializeComponent();
		}

		private void Control_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
			{
				Binding bndBinding = ((Control)sender).DataBindings[0];
				ViewModel.OptionInfoVM.Reset(bndBinding.BindingMemberInfo.BindingField);
				bndBinding.ReadValue();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}

		private void OptionValidated(object sender, EventArgs e)
		{
			erpErrors.SetError(tbxName, ViewModel.Errors.GetError<Option>(x => x.Name));
			erpErrors.SetError(tbxDescription, ViewModel.Errors.GetError<Option>(x => x.Description));
			erpErrors.SetError(tbxImagePath, ViewModel.Errors.GetError<Option>(x => x.ImagePath));
		}

		private void tbxName_Validated(object sender, EventArgs e)
		{
			ViewModel.SaveOptionInfo();
		}

		protected void LoadImage()
		{
			pbxImage.Image = ViewModel.GetImage(tbxImagePath.Text);
		}

		private void butSelectImage_Click(object sender, EventArgs e)
		{
			if (m_fsdFileChooser.ShowDialog(this) == DialogResult.OK)
			{
				tbxImagePath.Text = m_fsdFileChooser.SelectedPath;
				tbxImagePath.Focus();
				LoadImage();
			}
		}

		/// <summary>
		/// Gets the view model of the editor.
		/// </summary>
		/// <returns>The view model of the editor.</returns>
		public override IViewModel GetViewModel()
		{
			return (IViewModel)ViewModel;
		}
	}
}
