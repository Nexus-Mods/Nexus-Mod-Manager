using System;
using System.Collections;
using Nexus.UI.Controls;
using Nexus.Client.Util;
using ConditionalTypePattern = Nexus.Client.ModManagement.Scripting.XmlScript.ConditionalOptionTypeResolver.ConditionalTypePattern;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display a <see cref="ConditionalTypePattern"/> editor.
	/// </summary>
	public class ConditionalTypePatternEditorVM : ObservableObject, IViewModel
	{
		#region Events

		/// <summary>
		/// Raised when the edited pattern has been validated.
		/// </summary>
		public event EventHandler ConditionalTypePatternValidated = delegate { };

		/// <summary>
		/// Raised when the edited pattern has been saved.
		/// </summary>
		public event EventHandler<EventArgs<ConditionalTypePattern>> ConditionalTypePatternSaved = delegate { };

		#endregion

		private ConditionalTypePattern m_ctpPattern = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="NodeEditors.ConditionEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="ICondition"/> editor.
		/// </summary>
		/// <value>The <see cref="NodeEditors.ConditionEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="ICondition"/> editor.</value>
		public ConditionEditorVM ConditionEditorVM { get; private set; }

		/// <summary>
		/// Gets an enumeration of possible <see cref="OptionType"/>s.
		/// </summary>
		/// <value>An enumeration of possible <see cref="OptionType"/>s.</value>
		public IEnumerable OptionTypes { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="ConditionalTypePattern"/> being edited.
		/// </summary>
		/// <value>The <see cref="ConditionalTypePattern"/> being edited.</value>
		public ConditionalTypePattern ConditionalTypePattern
		{
			get
			{
				if (m_ctpPattern == null)
					m_ctpPattern = new ConditionalTypePattern(OptionType.NotUsable, null);
				return m_ctpPattern;
			}
			set
			{
				if (SetPropertyIfChanged(ref m_ctpPattern, value, () => ConditionalTypePattern))
					ConditionEditorVM.Condition = (value != null) ? value.Condition : null;
			}
		}

		/// <summary>
		/// Gets the current validation error.
		/// </summary>
		/// <value>The current validation error, or <c>null</c> if there is no error.</value>
		public string Error { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view model with its dependencies.
		/// </summary>
		/// <param name="p_cemConditionEditorVM">The <see cref="NodeEditors.ConditionEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="ICondition"/> editor.</param>
		/// <param name="p_ctpConditionalTypePattern">The <see cref="ConditionalTypePattern"/> being edited.</param>
		public ConditionalTypePatternEditorVM(ConditionEditorVM p_cemConditionEditorVM, ConditionalTypePattern p_ctpConditionalTypePattern)
		{
			ConditionEditorVM = p_cemConditionEditorVM;
			ConditionalTypePattern = p_ctpConditionalTypePattern;
			OptionTypes = Enum.GetValues(typeof(OptionType));
		}

		#endregion

		/// <summary>
		/// This saves the <see cref="ConditionalTypePattern"/> being edited.
		/// </summary>
		/// <remarks>
		/// The pattern is only saved if it is valid.
		/// </remarks>
		/// <param name="p_otpType">The <see cref="OptionType"/> to save to the pattern.</param>
		public void SavePattern(OptionType p_otpType)
		{
			if (Validate())
			{
				ConditionalTypePattern.Type = p_otpType;
				ConditionalTypePattern.Condition = ConditionEditorVM.Condition;
				ConditionalTypePatternSaved(this, new EventArgs<ConditionalTypePattern>(ConditionalTypePattern));
				ConditionalTypePattern = null;
			}
		}

		/// <summary>
		/// Ensures that the pattern is valid.
		/// </summary>
		/// <returns><c>true</c> if the source is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidatePattern()
		{
			if (ConditionEditorVM.Condition==null)
			{
				Error = "You must enter a pattern.";
				OnConditionalTypePatternValidated();
				return false;
			}
			OnConditionalTypePatternValidated();
			return true;
		}

		/// <summary>
		/// Raises the <see cref="ConditionalTypePatternValidated"/> event.
		/// </summary>
		protected void OnConditionalTypePatternValidated()
		{
			ConditionalTypePatternValidated(this, new EventArgs());
		}

		#region IViewModel Members

		/// <summary>
		/// Validates the pattern being edited.
		/// </summary>
		/// <returns><c>true</c> if the pattern is valid;
		/// <c>false</c> otherwise.</returns>
		/// <seealso cref="ValidatePattern"/>
		public bool Validate()
		{
			return ValidatePattern();
		}

		#endregion
	}
}
