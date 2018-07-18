using System;
using System.Collections.Generic;
using Nexus.UI.Controls;
using System.Collections;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	public class CPLEditorVM
	{
		public List<CplConditionEditor> ConditionEditors { get; private set; }
		public CPLTextEditorVM TextEditorVM { get; private set; }
		public IEnumerable PluginStates { get; private set; }

		public bool IsAndOperationAllowed { get; private set; }
		public bool IsOrOperationAllowed { get; private set; }

		public CPLEditorVM(CPLTextEditorVM p_temTextEditorVM, List<CplConditionEditor> p_lstCplConditionEditors, ConditionOperator p_copAllowedOperations)
		{
			TextEditorVM = p_temTextEditorVM;
			ConditionEditors = p_lstCplConditionEditors;

			IsAndOperationAllowed = (p_copAllowedOperations & ConditionOperator.And) > 0;
			IsOrOperationAllowed = (p_copAllowedOperations & ConditionOperator.Or) > 0;

			PluginStates = Enum.GetValues(typeof(PluginState));
		}

		public void AddCondition(ConditionOperator p_oprOperator, string p_strConditionCPL)
		{
			Int32 intCaretPosition = TextEditorVM.CaretPosition;
			Int32 intPreTextLength = TextEditorVM.Code.Substring(0, intCaretPosition).Trim().Length;
			Int32 intPostTextLength = TextEditorVM.Code.Substring(intCaretPosition).Trim().Length;
			string strPreOperator = null;
			string strPostOperator = null;
			if (intPreTextLength > 0)
				strPreOperator = (p_oprOperator == ConditionOperator.And) ? " AND " : " OR ";
			if (intPostTextLength > 0)
				strPostOperator = (p_oprOperator == ConditionOperator.And) ? " AND " : " OR ";
			string strExpression = String.Format("{0}{1}{2}", strPreOperator, p_strConditionCPL, strPostOperator);
			TextEditorVM.Code = TextEditorVM.Code.Insert(intCaretPosition, strExpression);
			TextEditorVM.ValidateCPL();
		}
	}
}
