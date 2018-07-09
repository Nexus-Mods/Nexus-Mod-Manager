using System;
using Nexus.UI.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that enable editing of an <see cref="ICondition"/>.
	/// </summary>
	public class ConditionEditorVM : IViewModel
	{
		#region Events

		/// <summary>
		/// Raised when an <see cref="ICondition"/> is saved.
		/// </summary>
		public event EventHandler ConditionSaved = delegate { };

		/// <summary>
		/// Raised when an <see cref="ICondition"/> is validated.
		/// </summary>
		public event EventHandler ConditionValidated = delegate { };

		#endregion

		private ICondition m_cndCondition = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="CPLEditorVM"/> that encapsulates the data
		/// and operations for diaplying the CPL editor.
		/// </summary>
		/// <value>The <see cref="CPLEditorVM"/> that encapsulates the data
		/// and operations for diaplying the CPL editor.</value>
		public CPLEditorVM EditorViewModel { get; set; }

		/// <summary>
		/// Gets the CPL converter.
		/// </summary>
		/// <value>The CPL converter.</value>
		public CPLConverter CPLConverter { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="ICondition"/> being edited.
		/// </summary>
		/// <value>The <see cref="ICondition"/> being edited.</value>
		public ICondition Condition
		{
			get
			{
				return m_cndCondition;
			}
			set
			{
				if (m_cndCondition != value)
				{
					m_cndCondition = value;
					EditorViewModel.TextEditorVM.Code = (value != null) ? CPLConverter.ConditionToCpl(value) : null;
					EditorViewModel.TextEditorVM.CaretPosition = EditorViewModel.TextEditorVM.Code.Length;
				}
			}
		}

		/// <summary>
		/// Gets the current validation error.
		/// </summary>
		/// <value>The current validation error.</value>
		public string Error { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view model with its dependencies.
		/// </summary>
		/// <param name="p_edtEditorViewModel">The <see cref="CPLEditorVM"/> that encapsulates the data
		/// and operations for diaplying the CPL editor.</param>
		/// <param name="p_ctrCPLConverter">The CPL converter.</param>
		/// <param name="p_cndCondition">The <see cref="ICondition"/> being edited.</param>
		public ConditionEditorVM(CPLEditorVM p_edtEditorViewModel, CPLConverter p_ctrCPLConverter, ICondition p_cndCondition)
		{
			EditorViewModel = p_edtEditorViewModel;
			CPLConverter = p_ctrCPLConverter;
			Condition = p_cndCondition;
		}

		#endregion

		/// <summary>
		/// Saves the changes that have been made to the <see cref="ICondition"/>.
		/// </summary>
		public void SaveCondition()
		{
			if (Validate())
			{
				Condition = CPLConverter.CplToCondition(EditorViewModel.TextEditorVM.Code);
				OnConditionSaved();
			}
		}

		/// <summary>
		/// Raises the <see cref="ConditionSaved"/> event.
		/// </summary>
		protected virtual void OnConditionSaved()
		{
			ConditionSaved(this, new EventArgs());
		}

		/// <summary>
		/// Ensures that the condition is valid.
		/// </summary>
		/// <returns><c>true</c> if the source is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateCondition()
		{
			Error = null;
			if (!EditorViewModel.TextEditorVM.ValidateCPL())
			{
				Error = "The pattern is invalid.";
				OnConditionValidated();
				return false;
			}
			OnConditionValidated();
			return true;
		}

		/// <summary>
		/// Raises the <see cref="ConditionValidated"/> event.
		/// </summary>
		protected void OnConditionValidated()
		{
			ConditionValidated(this, new EventArgs());
		}

		#region IViewModel Members

		/// <summary>
		/// Validates the current state of the condition.
		/// </summary>
		/// <returns><c>true</c> if the condition is valid CPL;
		/// <c>false</c> otherwise.</returns>
		public bool Validate()
		{
			return ValidateCondition();
		}

		#endregion
	}
}
