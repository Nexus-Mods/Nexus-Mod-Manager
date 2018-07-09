using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public class PrerequisitesEditorVM : ConditionEditorVM
	{
		private XmlScript m_xscScript = null;

		public XmlScript Script
		{
			get
			{
				return m_xscScript;
			}
			set
			{
				if (m_xscScript != value)
				{
					m_xscScript = value;
					Condition = m_xscScript.ModPrerequisites;
				}
			}
		}

		public PrerequisitesEditorVM(CPLEditorVM p_edtEditorViewModel, CPLConverter p_ctrCPLConverter, XmlScript p_xscScript)
			: base(p_edtEditorViewModel, p_ctrCPLConverter, null)
		{
			Script = p_xscScript;
		}

		protected override void OnConditionSaved()
		{
			base.OnConditionSaved();
			Script.ModPrerequisites = Condition;
		}
	}
}
