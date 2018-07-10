using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	public partial class CplVersionConditionEditor : CplConditionEditor
	{
		public override string ConditionCPL
		{
			get
			{
				return String.Format("{0} >= {1}", cbxVersionName.SelectedValue, tbxMinimumVersion.Text);
			}
		}

		public CplVersionConditionEditor(List<KeyValuePair<string, string>> p_lstVersionNames)
		{
			InitializeComponent();
			cbxVersionName.DataSource = p_lstVersionNames;
			cbxVersionName.DisplayMember = "Key";
			cbxVersionName.ValueMember = "Value";
			BindingHelper.CreateFullBinding(pnlEditVersion, () => pnlEditVersion.Enabled, this, () => Selected);
		}
		
		public override bool ValidateCPL()
		{
			erpErrors.SetError(tbxMinimumVersion, null);
			if (String.IsNullOrEmpty(tbxMinimumVersion.Text))
			{
				erpErrors.SetError(tbxMinimumVersion, "Minimum version is required.");
				return false;
			}
			else
			{
				try
				{
					new Version(tbxMinimumVersion.Text);
				}
				catch
				{
					erpErrors.SetError(tbxMinimumVersion, "Minimum version must be in #.#.#.# format.");
					return false;
				}
			}
			return true;
		}
	}
}
