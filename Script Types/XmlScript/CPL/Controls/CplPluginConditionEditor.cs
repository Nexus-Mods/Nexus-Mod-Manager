using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Nexus.UI.Controls;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	public partial class CplPluginConditionEditor : CplConditionEditor
	{
		private FileSelectionDialog m_fsdFileChooser = new FileSelectionDialog();

		public override string ConditionCPL
		{
			get
			{
				return String.Format("\"{0}\" is {1}", tbxPlugin.Text.Trim('"'), cbxPluginState.SelectedItem);
			}
		}

		public CplPluginConditionEditor(IList<VirtualFileSystemItem> p_lstModFiles)
		{
			InitializeComponent();
			m_fsdFileChooser.FileSystemItems = p_lstModFiles;
			cbxPluginState.DataSource = Enum.GetValues(typeof(PluginState));
			BindingHelper.CreateFullBinding(pnlEditPlugin, () => pnlEditPlugin.Enabled, this, () => Selected);
		}
		
		public override bool ValidateCPL()
		{
			erpErrors.SetError(tbxPlugin, null);
			if (String.IsNullOrEmpty(tbxPlugin.Text))
			{
				erpErrors.SetError(tbxPlugin, "Plugin path is required.");
				return false;
			}
			return true;
		}

		private void butSelectPlugin_Click(object sender, EventArgs e)
		{
			if (m_fsdFileChooser.ShowDialog(this) == DialogResult.OK)
			{
				tbxPlugin.Text = m_fsdFileChooser.SelectedPath;
				tbxPlugin.Focus();
			}
		}
	}
}
