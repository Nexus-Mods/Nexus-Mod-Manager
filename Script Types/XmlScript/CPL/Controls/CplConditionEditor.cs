using System.Windows.Forms;
using System;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	public class CplConditionEditor : UserControl
	{
		public event EventHandler SelectedChanged = delegate { };

		private RadioButton p_radSelector = new RadioButton();

		public bool Selected
		{
			get
			{
				return p_radSelector.Checked;
			}
			set
			{
				p_radSelector.Checked = value;
			}
		}

		public virtual string ConditionCPL
		{
			get
			{
				return null;
			}
		}

		public CplConditionEditor()
		{
			p_radSelector.Dock = DockStyle.Left;
			p_radSelector.Text = null;
			p_radSelector.AutoSize = true;
			p_radSelector.Padding = new Padding(3);
			p_radSelector.CheckedChanged += new EventHandler(CheckedChanged);
			Controls.Add(p_radSelector);
		}

		private void CheckedChanged(object sender, EventArgs e)
		{
			SelectedChanged(this, new EventArgs());
		}

		protected override void OnControlAdded(ControlEventArgs e)
		{
			base.OnControlAdded(e);
			p_radSelector.SendToBack();
		}

		public virtual bool ValidateCPL()
		{
			return true;
		}
	}
}
