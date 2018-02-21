using System;
using System.Windows.Forms;
using System.Collections.Generic;
using Nexus.UI.Controls;
using System.ComponentModel;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	public partial class CPLEditor : UserControl
	{
		private CPLEditorVM m_vmlViewModel = null;
		private List<CplConditionEditor> m_lstConditionEditors = new List<CplConditionEditor>();

		#region Properties

		public CPLEditorVM ViewModel
		{
			private get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				cleCPLEditor.ViewModel = value.TextEditorVM;

				butAnd.Visible = value.IsAndOperationAllowed;
				butOr.Visible = value.IsOrOperationAllowed;

				Panel pnlEditorContainer = splitContainer1.Panel2;
				for (Int32 i = pnlEditorContainer.Controls.Count - 1; i >= 0; i--)
					if (pnlEditorContainer.Controls[i] is CplConditionEditor)
						pnlEditorContainer.Controls.RemoveAt(i);
				m_lstConditionEditors.Clear();
				foreach (CplConditionEditor cceEditor in ViewModel.ConditionEditors)
				{
					cceEditor.Dock = DockStyle.Top;
					pnlEditorContainer.Controls.Add(cceEditor);
					cceEditor.SendToBack();
					m_lstConditionEditors.Add(cceEditor);
					cceEditor.SelectedChanged += new EventHandler(ConditionEditorSelectedChanged);
				}
				m_lstConditionEditors[m_lstConditionEditors.Count - 1].Selected = true;
			}
		}

		#endregion

		public CPLEditor()
		{
			InitializeComponent();
			this.DoubleBuffered = true;
		}

		public CPLEditor(CPLEditorVM p_vmlViewModel)
			:this()
		{
			ViewModel = p_vmlViewModel;
		}


		protected CplConditionEditor GetSelectedEditor()
		{
			foreach (CplConditionEditor cceEditor in m_lstConditionEditors)
				if (cceEditor.Selected)
					return cceEditor;
			return m_lstConditionEditors[0];
		}

		private void Operator_Click(object sender, EventArgs e)
		{
			CplConditionEditor cceEditor = GetSelectedEditor();
			if (cceEditor.ValidateCPL())
			{
				ConditionOperator copOperator = (sender == butAnd) ? ConditionOperator.And : ConditionOperator.Or;
				ViewModel.AddCondition(copOperator, cceEditor.ConditionCPL);
			}
			
		}

		private void ConditionEditorSelectedChanged(object sender, EventArgs e)
		{
			CplConditionEditor cceSelected = (CplConditionEditor)sender;
			if (!cceSelected.Selected)
				return;
			foreach (CplConditionEditor cceButton in m_lstConditionEditors)
				if (cceButton != cceSelected)
					cceButton.Selected = false;
		}
	}
}
