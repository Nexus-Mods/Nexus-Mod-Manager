using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.Util;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public partial class OptionGroupEditor : NodeEditor
	{
		private OptionGroupEditorVM m_vmlViewModel = null;
		private OptionGroupVM m_opgOptionGroupVM = null;

		#region Properties

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public OptionGroupEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				OptionGroupVM = value.OptionGroupVM;
				cbxType.DataSource = value.OptionGroupTypes;
				cbxSortOrder.DataSource = value.SortOrders;

				pnlName.Visible = value.NameVisible;
				pnlSortOrder.Visible = value.OptionSortOrderVisible;

				value.OptionGroupValidated += new EventHandler(OptionGroupValidated);
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected OptionGroupVM OptionGroupVM
		{
			get
			{
				return m_opgOptionGroupVM;
			}
			set
			{
				m_opgOptionGroupVM = value;
				erpErrors.SetError(tbxName, null);

				tbxName.DataBindings.Clear();
				cbxType.DataBindings.Clear();
				cbxSortOrder.DataBindings.Clear();

				BindingHelper.CreateFullBinding(tbxName, () => tbxName.Text, value, () => value.Name);
				BindingHelper.CreateFullBinding(cbxType, () => cbxType.SelectedItem, value, () => value.Type);
				BindingHelper.CreateFullBinding(cbxSortOrder, () => cbxSortOrder.SelectedItem, value, () => value.OptionSortOrder);

				rlvOptions.Items.Clear();
				foreach (Option optOption in value.Options)
				{
					ListViewItem lviItem = new ListViewItem(optOption.Name);
					lviItem.Tag = optOption;
					rlvOptions.Items.Add(lviItem);
				}
			}
		}

		#endregion

		public OptionGroupEditor(OptionGroupEditorVM p_vmlViewModel)
		{
			InitializeComponent();
			ViewModel = p_vmlViewModel;
		}

		private void tbxName_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
			{
				Binding bndBinding = ((Control)sender).DataBindings[0];
				ViewModel.OptionGroupVM.Reset(bndBinding.BindingMemberInfo.BindingField);
				bndBinding.ReadValue();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}

		private void OptionGroupValidated(object sender, EventArgs e)
		{
			erpErrors.SetError(tbxName, ViewModel.Errors.GetError<OptionGroupVM>(x => x.Name));
			erpErrors.SetError(cbxType, ViewModel.Errors.GetError<OptionGroupVM>(x => x.Type));
			erpErrors.SetError(cbxSortOrder, ViewModel.Errors.GetError<OptionGroupVM>(x => x.OptionSortOrder));
		}

		private void Control_Validated(object sender, EventArgs e)
		{	
			Binding bndBinding = ((Control)sender).DataBindings[0];
			ViewModel.SaveOptionGroup(bndBinding.BindingMemberInfo.BindingField);
		}

		/// <summary>
		/// Gets the view model of the editor.
		/// </summary>
		/// <returns>The view model of the editor.</returns>
		public override IViewModel GetViewModel()
		{
			return ViewModel;
		}

		private void rlvOptions_Resize(object sender, EventArgs e)
		{
			clmOptionName.Width = rlvOptions.ClientSize.Width;
		}

		private void cbxSortOrder_SelectedIndexChanged(object sender, EventArgs e)
		{
			rlvOptions.Enabled = ((SortOrder)cbxSortOrder.SelectedItem == SortOrder.Explicit);
			//we have to explicitly write the value to the bound datasource, as this handler may be
			// called before the binding updates itself, even if the update mode is set
			// to OnPropertyChange (presumably this is because the datasource uses the same event
			// to write to the datasource, and our handler can be called first).
			Binding bndBinding = ((Control)sender).DataBindings[0];
			bndBinding.WriteValue();
			ViewModel.SaveOptionGroup(bndBinding.BindingMemberInfo.BindingField);
		}

		private void rlvOptions_ItemsReordered(object sender, ReorderedItemsEventArgs e)
		{
			foreach (ReorderedItemsEventArgs.ReorderedListViewItem rliItem in e.ReorderedListViewItems)
			{
				OptionGroupVM.Options.RemoveAt(rliItem.OldIndex);
				OptionGroupVM.Options.Insert(rliItem.NewIndex, (Option)rliItem.Item.Tag);
			}
			ViewModel.SaveOptionGroup(ObjectHelper.GetPropertyName<OptionGroup>(x => x.Options));
		}
	}
}
