using System;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display a <see cref="ConditionallyInstalledFileSet"/> editor.
	/// </summary>
	public class ConditionallyInstalledFileSetEditorVM : IViewModel
	{
		private ConditionallyInstalledFileSet m_cisConditionallyInstalledFileSet = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="ConditionallyInstalledFileSet"/> being edited.
		/// </summary>
		/// <value>The <see cref="ConditionallyInstalledFileSet"/> being edited.</value>
		public ConditionallyInstalledFileSet ConditionallyInstalledFileSet
		{
			get
			{
				return m_cisConditionallyInstalledFileSet;
			}
			private set
			{
				m_cisConditionallyInstalledFileSet = value;
				ConditionEditorVM.Condition = value.Condition;
			}
		}

		/// <summary>
		/// Gets the <see cref="NodeEditors.ConditionEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="ICondition"/> editor.
		/// </summary>
		/// <value>The <see cref="NodeEditors.ConditionEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="ICondition"/> editor.</value>
		public ConditionEditorVM ConditionEditorVM { get; private set; }

		/// <summary>
		/// Gets the <see cref="NodeEditors.FileListEditorVM"/> that encapsulates the data
		/// and operations for diaplying the file list editor.
		/// </summary>
		/// <value>The <see cref="NodeEditors.FileListEditorVM"/> that encapsulates the data
		/// and operations for diaplying the file list editor.</value>
		public FileListEditorVM FileListEditorVM { get; private set; }

		#endregion

		#region Constructor

		/// <summary>
		/// A simple constructor that initializes the view model with its dependencies.
		/// </summary>
		/// <param name="p_vmlConditionEditor">The <see cref="ConditionEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="ICondition"/> editor.</param>
		/// <param name="p_vmlFileList">The <see cref="FileListEditorVM"/> that encapsulates the data
		/// and operations for diaplying the file list editor.</param>
		/// <param name="p_cisConditionallyInstalledFileSet">The <see cref="ConditionallyInstalledFileSet"/> being edited.</param>
		public ConditionallyInstalledFileSetEditorVM(ConditionEditorVM p_vmlConditionEditor, FileListEditorVM p_vmlFileList, ConditionallyInstalledFileSet p_cisConditionallyInstalledFileSet)
		{
			ConditionEditorVM = p_vmlConditionEditor;
			FileListEditorVM = p_vmlFileList;
			ConditionallyInstalledFileSet = p_cisConditionallyInstalledFileSet;

			ConditionEditorVM.ConditionSaved += new EventHandler(ConditionSaved);
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="NodeEditors.ConditionEditorVM.ConditionSaved"/> event of the <see cref="ICondition"/>
		/// editor's view model.
		/// </summary>
		/// <remarks>
		/// This persistes the new condition to the <see cref="ConditionallyInstalledFileSet"/>.
		/// </remarks>
		/// <param name="sender">the object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ConditionSaved(object sender, EventArgs e)
		{
			ConditionallyInstalledFileSet.Condition = ConditionEditorVM.Condition;
		}

		#region IViewModel Members

		/// <summary>
		/// This validates the current state of the <see cref="ConditionallyInstalledFileSet"/>.
		/// </summary>
		/// <returns>Always <c>true</c>.</returns>
		public bool Validate()
		{
			return true;
		}

		#endregion
	}
}
