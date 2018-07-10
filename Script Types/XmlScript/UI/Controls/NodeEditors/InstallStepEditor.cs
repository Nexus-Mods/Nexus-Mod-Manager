using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Nexus.Client.Util;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public partial class InstallStepEditor : NodeEditor
	{
		private InstallStepEditorVM m_vmlViewModel = null;
		private InstallStepVM m_ispInstallStepVM = null;

		#region Properties

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public InstallStepEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				if (value.VisibilityVisible)
					cedVisibilityCondition.ViewModel = value.ConditionEditorVM;
				InstallStepVM = value.InstallStepVM;
				cbxSortOrder.DataSource = value.SortOrders;

				pnlName.Visible = value.NameVisible;
				pnlSortOrder.Visible = value.GroupSortOrderVisible;
				if (!value.VisibilityVisible)
					tclInstallStep.TabPages.Remove(tpgVisibility);
				else if (!tclInstallStep.TabPages.Contains(tpgVisibility))
					tclInstallStep.TabPages.Add(tpgVisibility);

				value.InstallStepValidated += new EventHandler(InstallStepValidated);
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected InstallStepVM InstallStepVM
		{
			get
			{
				return m_ispInstallStepVM;
			}
			set
			{
				m_ispInstallStepVM = value;
				erpErrors.SetError(tbxName, null);

				tbxName.DataBindings.Clear();
				cbxSortOrder.DataBindings.Clear();

				BindingHelper.CreateFullBinding(tbxName, () => tbxName.Text, value, () => value.Name);
				BindingHelper.CreateFullBinding(cbxSortOrder, () => cbxSortOrder.SelectedItem, value, () => value.GroupSortOrder);

				rlvGroups.Items.Clear();
				foreach (OptionGroup grpGroup in value.OptionGroups)
				{
					ListViewItem lviItem = new ListViewItem(grpGroup.Name);
					lviItem.Tag = grpGroup;
					rlvGroups.Items.Add(lviItem);
				}
			}
		}

		#endregion

		public InstallStepEditor(InstallStepEditorVM p_vmlViewModel)
		{
			InitializeComponent();
			ViewModel = p_vmlViewModel;
		}

		private void tbxName_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
			{
				Binding bndBinding = ((Control)sender).DataBindings[0];
				ViewModel.InstallStepVM.Reset(bndBinding.BindingMemberInfo.BindingField);
				bndBinding.ReadValue();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}

		private void InstallStepValidated(object sender, EventArgs e)
		{
			erpErrors.SetError(tbxName, ViewModel.Errors.GetError<OptionGroupVM>(x => x.Name));
		}

		private void Control_Validated(object sender, EventArgs e)
		{
			Binding bndBinding = ((Control)sender).DataBindings[0];
			ViewModel.SaveInstallStep(bndBinding.BindingMemberInfo.BindingField);
		}

		/// <summary>
		/// Gets the view model of the editor.
		/// </summary>
		/// <returns>The view model of the editor.</returns>
		public override IViewModel GetViewModel()
		{
			return ViewModel;
		}

		private void rlvGroups_Resize(object sender, EventArgs e)
		{
			clmGroupName.Width = rlvGroups.ClientSize.Width;
		}

		private void cbxSortOrder_SelectedIndexChanged(object sender, EventArgs e)
		{
			rlvGroups.Enabled = ((SortOrder)cbxSortOrder.SelectedItem == SortOrder.Explicit);
			//we have to explicitly write the value to the bound datasource, as this handler may be
			// called before the binding updates itself, even if the update mode is set
			// to OnPropertyChange (presumably this is because the datasource uses the same event
			// to write to the datasource, and our handler can be called first).
			Binding bndBinding = ((Control)sender).DataBindings[0];
			bndBinding.WriteValue();
			ViewModel.SaveInstallStep(bndBinding.BindingMemberInfo.BindingField);
		}

		private void rlvGroups_ItemsReordered(object sender, ReorderedItemsEventArgs e)
		{
			foreach (ReorderedItemsEventArgs.ReorderedListViewItem rliItem in e.ReorderedListViewItems)
			{
				InstallStepVM.OptionGroups.RemoveAt(rliItem.OldIndex);
				InstallStepVM.OptionGroups.Insert(rliItem.NewIndex, (OptionGroup)rliItem.Item.Tag);
			}
			ViewModel.SaveInstallStep(ObjectHelper.GetPropertyName<InstallStep>(x => x.OptionGroups));
		}

		private void tclInstallStep_Deselecting(object sender, TabControlCancelEventArgs e)
		{
			if (e.TabPage == tpgVisibility)
				e.Cancel = !ViewModel.Validate();
		}
	}
}
