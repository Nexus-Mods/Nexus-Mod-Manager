using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Nexus.Client.Util;
using Nexus.Client.ModManagement.Scripting;
using Nexus.UI.Controls;

namespace Nexus.Client.ModAuthoring.UI.Controls
{
	/// <summary>
	/// A UI view that allows the editing of mod scripts.
	/// </summary>
	public partial class ScriptEditor : UserControl
	{
		private ModScriptEditorVM m_vmlViewModel = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ModScriptEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;

				foreach (IScriptEditor sedEditor in m_vmlViewModel.Editors)
				{
					DropDownTabPage tpgScriptType = new DropDownTabPage();
					tpgScriptType.Text = sedEditor.ScriptTypeName;
					tpgScriptType.Tag = sedEditor;
					Control ctlEditor = (Control)sedEditor;
					ctlEditor.Dock = DockStyle.Fill;
					tpgScriptType.Controls.Add(ctlEditor);
					dtcScriptEditors.TabPages.Add(tpgScriptType);
				}
				m_vmlViewModel.PropertyChanged += new PropertyChangedEventHandler(ViewModel_PropertyChanged);
				
				BindingHelper.CreateFullBinding(ckbUseScript, () => ckbUseScript.Checked, value, () => value.UseScript).DataSourceUpdateMode = DataSourceUpdateMode.OnPropertyChanged;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ScriptEditor()
		{
			InitializeComponent();
			BindingHelper.CreateFullBinding(dtcScriptEditors, () => dtcScriptEditors.Enabled, ckbUseScript, () => ckbUseScript.Checked);
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="DropDownTabControl.SelectedIndexChanged"/> of the script type selector.
		/// </summary>
		/// <remarks>
		/// This sets the currently selected script type.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void dtcScriptEditors_SelectedIndexChanged(object sender, EventArgs e)
		{
			ViewModel.CurrentEditor = (IScriptEditor)dtcScriptEditors.SelectedTabPage.Tag;
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> of the view model.
		/// </summary>
		/// <remarks>
		/// This sets the currently selected editor to the current script type.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<ModScriptEditorVM>(x => x.CurrentEditor)))
				foreach (DropDownTabPage tpgScriptType in dtcScriptEditors.TabPages)
					if (tpgScriptType.Tag == ViewModel.CurrentEditor)
					{
						dtcScriptEditors.SelectedTabPage = tpgScriptType;
						break;
					}
		}
	}
}
