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
	public partial class InstallStepsEditor : NodeEditor
	{
		private InstallStepsEditorVM m_vmlViewModel = null;
		private InstallStepsVM m_issInstallStepsVM = null;

		#region Properties

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public InstallStepsEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				InstallStepsVM = value.InstallStepsVM;
				cbxSortOrder.DataSource = value.SortOrders;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected InstallStepsVM InstallStepsVM
		{
			get
			{
				return m_issInstallStepsVM;
			}
			set
			{
				m_issInstallStepsVM = value;
				
				cbxSortOrder.DataBindings.Clear();

				BindingHelper.CreateFullBinding(cbxSortOrder, () => cbxSortOrder.SelectedItem, value, () => value.InstallStepSortOrder);

				rlvSteps.Items.Clear();
				foreach (InstallStep ispStep in value.InstallSteps)
				{
					ListViewItem lviItem = new ListViewItem(ispStep.Name);
					lviItem.Tag = ispStep;
					rlvSteps.Items.Add(lviItem);
				}
			}
		}

		#endregion

		public InstallStepsEditor(InstallStepsEditorVM p_vmlViewModel)
		{
			InitializeComponent();
			ViewModel = p_vmlViewModel;
		}

		/// <summary>
		/// Gets the view model of the editor.
		/// </summary>
		/// <returns>The view model of the editor.</returns>
		public override IViewModel GetViewModel()
		{
			return ViewModel;
		}

		private void rlvSteps_Resize(object sender, EventArgs e)
		{
			clmStepName.Width = rlvSteps.ClientSize.Width;
		}

		private void cbxSortOrder_SelectedIndexChanged(object sender, EventArgs e)
		{
			rlvSteps.Enabled = ((SortOrder)cbxSortOrder.SelectedItem == SortOrder.Explicit);
			//we have to explicitly write the value to the bound datasource, as this handler may be
			// called before the binding updates itself, even if the update mode is set
			// to OnPropertyChange (presumably this is because the datasource uses the same event
			// to write to the datasource, and our handler can be called first).
			Binding bndBinding = ((Control)sender).DataBindings[0];
			bndBinding.WriteValue();
			ViewModel.SaveInstallSteps(bndBinding.BindingMemberInfo.BindingField);
		}

		private void rlvSteps_ItemsReordered(object sender, ReorderedItemsEventArgs e)
		{
			foreach (ReorderedItemsEventArgs.ReorderedListViewItem rliItem in e.ReorderedListViewItems)
			{
				InstallStepsVM.InstallSteps.RemoveAt(rliItem.OldIndex);
				InstallStepsVM.InstallSteps.Insert(rliItem.NewIndex, (InstallStep)rliItem.Item.Tag);
			}
			ViewModel.SaveInstallSteps(ObjectHelper.GetPropertyName<InstallStepsVM>(x => x.InstallSteps));
		}
	}
}
