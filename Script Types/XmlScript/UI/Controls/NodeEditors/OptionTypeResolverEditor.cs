using System;
using System.Windows.Forms;
using Nexus.Client.Util;
using Nexus.UI.Controls;
using System.ComponentModel;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public partial class OptionTypeResolverEditor : NodeEditor
	{
		private OptionTypeResolverEditorVM m_vmlViewModel = null;
		private bool m_booIsBinding = false;

		#region Properties

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public OptionTypeResolverEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				cteCondionalEditor.ViewModel = value.ConditionalTypeEditorVM;
				
				cbxSimpleType.DataBindings.Clear();
				cbxSimpleType.DataSource = value.OptionTypes;
				BindingHelper.CreateFullBinding<ComboBox, StaticOptionTypeResolver>(cbxSimpleType, x => x.SelectedItem, value.StaticOptionTypeResolver, x => x.Type);

				m_booIsBinding = true;
				switch (value.OptionTypeResolverType)
				{
					case OptionTypeResolverType.Static:
						radStaticType.Checked = true;
						break;
					case OptionTypeResolverType.Conditional:
						radConditionalType.Checked = true;
						break;
					default:
						throw new Exception("Invalid value for " + ObjectHelper.GetPropertyName(() => value.OptionTypeResolverType));
				}
				m_booIsBinding = false;
			}
		}

		#endregion

		public OptionTypeResolverEditor()
		{
			InitializeComponent();

			gbxConditionalType.DataBindings.Add("Enabled", radConditionalType, "Checked");
			gbxSimpleType.DataBindings.Add("Enabled", radStaticType, "Checked");
		}

		private void radStaticType_CheckedChanged(object sender, EventArgs e)
		{
			if (m_booIsBinding)
				return;
			if (radStaticType.Checked)
				ViewModel.OptionTypeResolverType = OptionTypeResolverType.Static;
			else if (radConditionalType.Checked)
				ViewModel.OptionTypeResolverType = OptionTypeResolverType.Conditional;
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
