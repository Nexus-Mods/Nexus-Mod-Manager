using System.Windows.Forms;
using Nexus.Client.Util;
using System.ComponentModel;
using System;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public partial class HeaderEditor : NodeEditor
	{
		private HeaderEditorVM m_vmlViewModel = null;
		private FileSelectionDialog m_fsdFileChooser = new FileSelectionDialog();

		#region Properties

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public HeaderEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				HeaderInfoVM = value.HeaderInfoVM;
				cbxAlignment.DataSource = value.TextPositions;
				m_fsdFileChooser.FileSystemItems = value.ModFiles;

				pnlTitle.Visible = value.TitleVisible;
				pnlColour.Visible = value.TextColourVisible;
				pnlAlignment.Visible = value.TextPositionVisible;
				pnlImage.Visible = value.ImageVisible;
				pnlHeight.Visible = value.HeightVisible;

				value.HeaderInfoValidated += new EventHandler(HeaderInfoValidated);
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected HeaderInfoVM HeaderInfoVM
		{
			set
			{
				tbxModName.DataBindings.Clear();
				cpkColour.DataBindings.Clear();
				cbxAlignment.DataBindings.Clear();
				tbxImagePath.DataBindings.Clear();
				ckbShowImage.DataBindings.Clear();
				ckbShowFade.DataBindings.Clear();
				nudHeight.DataBindings.Clear();

				BindingHelper.CreateFullBinding(tbxModName, () => tbxModName.Text, value, () => value.Title);
				BindingHelper.CreateFullBinding(cpkColour, () => cpkColour.Colour, value, () => value.TextColour);
				BindingHelper.CreateFullBinding(cbxAlignment, () => cbxAlignment.SelectedItem, value, () => value.TextPosition);
				BindingHelper.CreateFullBinding(tbxImagePath, () => tbxImagePath.Text, value, () => value.ImagePath);
				BindingHelper.CreateFullBinding(ckbShowImage, () => ckbShowImage.Checked, value, () => value.ShowImage);
				BindingHelper.CreateFullBinding(ckbShowFade, () => ckbShowFade.Checked, value, () => value.ShowFade);
				BindingHelper.CreateFullBinding(nudHeight, () => nudHeight.Value, value, () => value.Height);

				m_fsdFileChooser.SelectedPath = value.ImagePath;
			}
		}

		#endregion

		public HeaderEditor(HeaderEditorVM p_vmlViewModel)
		{
			InitializeComponent();
			ViewModel = p_vmlViewModel;
		}

		private void tbxModName_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
			{
				Binding bndBinding = ((Control)sender).DataBindings[0];
				ViewModel.HeaderInfoVM.Reset(bndBinding.BindingMemberInfo.BindingField);
				bndBinding.ReadValue();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}

		private void HeaderInfoValidated(object sender, EventArgs e)
		{
			
		}

		private void Control_Validated(object sender, EventArgs e)
		{
			ViewModel.SaveHeaderInfo();
		}

		private void butSelectImage_Click(object sender, EventArgs e)
		{
			if (m_fsdFileChooser.ShowDialog(this) == DialogResult.OK)
			{
				butSelectImage.Text = m_fsdFileChooser.SelectedPath;
				butSelectImage.Focus();
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
