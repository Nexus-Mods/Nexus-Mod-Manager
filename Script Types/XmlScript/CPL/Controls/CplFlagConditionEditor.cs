using System;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	public partial class CplFlagConditionEditor : CplConditionEditor
	{
		public override string ConditionCPL
		{
			get
			{
				return String.Format("${0}$ = \"{1}\"", tbxFlagName.Text.Trim('$'), tbxFlagValue.Text);
			}
		}

		public CplFlagConditionEditor()
		{
			InitializeComponent();
			BindingHelper.CreateFullBinding(tlpEditFlag, () => tlpEditFlag.Enabled, this, () => this.Selected);
		}

		public override bool ValidateCPL()
		{
			erpErrors.SetError(tbxFlagName,null);
			if (String.IsNullOrEmpty(tbxFlagName.Text))
			{
				erpErrors.SetError(tbxFlagName, "Flag name is required.");
				return false;
			}
			return true;
		}
	}
}
