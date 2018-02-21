using System;
using System.Collections;
using Nexus.Client.Commands.Generic;
using Nexus.UI.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;
using ConditionalTypePattern = Nexus.Client.ModManagement.Scripting.XmlScript.ConditionalOptionTypeResolver.ConditionalTypePattern;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display a <see cref="ConditionallyInstalledFileSet"/> order editor.
	/// </summary>
	public class ConditionalTypeEditorVM : IViewModel
	{
		#region Events

		/// <summary>
		/// Raised when a <see cref="ConditionalTypePattern"/> has been added the the
		/// <see cref="ConditionalOptionTypeResolver"/> being edited.
		/// </summary>
		public event EventHandler<EventArgs<ConditionalTypePattern>> PatternAdded = delegate { };

		/// <summary>
		/// Raised when a <see cref="ConditionalTypePattern"/> has been edited.
		/// </summary>
		public event EventHandler<EventArgs<ConditionalTypePattern>> PatternEdited = delegate { };

		/// <summary>
		/// Raised when a <see cref="ConditionalTypePattern"/> has been deleted.
		/// </summary>
		public event EventHandler<EventArgs<ConditionalTypePattern>> PatternDeleted = delegate { };

		#endregion

		private ConditionalTypePatternEditorVM m_tpeConditionalTypePatternEditorVM = null;
		
		#region Properties

		#region Commands

		/// <summary>
		/// Gets the command to edit a <see cref="ConditionalTypePattern"/>.
		/// </summary>
		/// <value>The command to edit a <see cref="ConditionalTypePattern"/>.</value>
		public Command<ConditionalTypePattern> EditCommand { get; private set; }

		/// <summary>
		/// Gets the command to add a <see cref="ConditionalTypePattern"/>.
		/// </summary>
		/// <value>The command to add a <see cref="ConditionalTypePattern"/>.</value>
		public Command<ConditionalTypePattern> AddCommand { get; private set; }

		/// <summary>
		/// Gets the command to delete a <see cref="ConditionalTypePattern"/>.
		/// </summary>
		/// <value>The command to delete a <see cref="ConditionalTypePattern"/>.</value>
		public Command<ConditionalTypePattern> DeleteCommand { get; private set; }

		#endregion

		/// <summary>
		/// Gets the <see cref="NodeEditors.ConditionalTypePatternEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="ConditionalTypePattern"/> editor.
		/// </summary>
		/// <value>The <see cref="NodeEditors.ConditionalTypePatternEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="ConditionalTypePattern"/> editor.</value>
		public ConditionalTypePatternEditorVM ConditionalTypePatternEditorVM
		{
			get
			{
				return m_tpeConditionalTypePatternEditorVM;
			}
			private set
			{
				m_tpeConditionalTypePatternEditorVM = value;
				value.ConditionalTypePatternSaved += new EventHandler<EventArgs<ConditionalTypePattern>>(ConditionalTypePatternSaved);
			}
		}

		/// <summary>
		/// Gets or sets the <see cref="ConditionalOptionTypeResolver"/> being edited.
		/// </summary>
		/// <value>The <see cref="ConditionalOptionTypeResolver"/> being edited.</value>
		public ConditionalOptionTypeResolver TypeResolver { get; set; }

		/// <summary>
		/// Gets an enumeration of possible <see cref="OptionType"/>s.
		/// </summary>
		/// <value>An enumeration of possible <see cref="OptionType"/>s.</value>
		public IEnumerable OptionTypes { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="CPLConverter"/> to use to convert <see cref="ICondition"/>s
		/// conditions to a string.
		/// </summary>
		/// <value>The <see cref="CPLConverter"/> to use to convert <see cref="ICondition"/>s
		/// conditions to a string.</value>
		protected CPLConverter Converter { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view model with its dependencies.
		/// </summary>
		/// <param name="p_tpeConditionalTypePatternEditorVM">The <see cref="NodeEditors.ConditionalTypePatternEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="ConditionalTypePattern"/> editor.</param>
		/// <param name="p_ctrCPLConverter">The <see cref="CPLConverter"/> to use to convert <see cref="ICondition"/>s
		/// conditions to a string.</param>
		/// <param name="p_ctrTypeResolver">The <see cref="ConditionalOptionTypeResolver"/> being edited.</param>
		public ConditionalTypeEditorVM(ConditionalTypePatternEditorVM p_tpeConditionalTypePatternEditorVM, CPLConverter p_ctrCPLConverter, ConditionalOptionTypeResolver p_ctrTypeResolver)
		{
			ConditionalTypePatternEditorVM = p_tpeConditionalTypePatternEditorVM;
			Converter = p_ctrCPLConverter;
			TypeResolver = p_ctrTypeResolver;
			OptionTypes = Enum.GetValues(typeof(OptionType));

			EditCommand = new Command<ConditionalTypePattern>("Edit", "Edit the selected conditional type.", EditConditionalTypePattern, false);
			AddCommand = new Command<ConditionalTypePattern>("Add", "Add a conditional type.", AddConditionalTypePattern);
			DeleteCommand = new Command<ConditionalTypePattern>("Delete", "Delete the selected conditional type.", DeleteConditionalTypePattern, false);
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="NodeEditors.ConditionalTypePatternEditorVM.ConditionalTypePatternSaved"/>
		/// event of the pattern editor's view model.
		/// </summary>
		/// <remarks>
		/// This raises either the <see cref="PatternAdded"/> event or the <see cref="PatternEdited"/>
		/// even, as appropriate.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{ConditionalTypePattern}"/> describing the event arugments.</param>
		private void ConditionalTypePatternSaved(object sender, EventArgs<ConditionalTypePattern> e)
		{
			if (!TypeResolver.ConditionalTypePatterns.Contains(e.Argument))
			{
				TypeResolver.ConditionalTypePatterns.Add(e.Argument);
				PatternAdded(this, new EventArgs<ConditionalTypePattern>(e.Argument));
			}
			else
				PatternEdited(this, new EventArgs<ConditionalTypePattern>(e.Argument));
		}

		#region Commands

		/// <summary>
		/// Performs the <see cref="EditCommand"/> work.
		/// </summary>
		/// <remarks>
		/// This sets the given <see cref="ConditionalTypePattern"/> to be the currently edited pattern on the
		/// <see cref="NodeEditors.ConditionalTypePatternEditorVM"/> view model.
		/// </remarks>
		/// <param name="p_ctpArgument">The conditional type pattern to edit.</param>
		private void EditConditionalTypePattern(ConditionalTypePattern p_ctpArgument)
		{
			ConditionalTypePatternEditorVM.ConditionalTypePattern = p_ctpArgument;
		}

		/// <summary>
		/// Performs the <see cref="AddCommand"/> work.
		/// </summary>
		/// <remarks>
		/// This sets the currently edited <see cref="ConditionalTypePattern"/> on the
		/// <see cref="NodeEditors.ConditionalTypePatternEditorVM"/> view model to a new pattern. 
		/// </remarks>
		/// <param name="p_ctpArgument">The conditional type pattern to add. This is always ignored.</param>
		private void AddConditionalTypePattern(ConditionalTypePattern p_ctpArgument)
		{
			ConditionalTypePatternEditorVM.ConditionalTypePattern = null;
		}

		/// <summary>
		/// Performs the <see cref="DeleteCommand"/> work.
		/// </summary>
		/// <remarks>
		/// This removes the given pattern from the <see cref="ConditionalOptionTypeResolver"/> being edited. This
		/// also raises the <see cref="PatternDeleted"/> event.
		/// </remarks>
		/// <param name="p_ctpArgument">The conditional type pattern to delete.</param>
		private void DeleteConditionalTypePattern(ConditionalTypePattern p_ctpArgument)
		{
			TypeResolver.ConditionalTypePatterns.Remove(p_ctpArgument);
			PatternDeleted(this, new EventArgs<ConditionalTypePattern>(p_ctpArgument));
		}

		#endregion

		/// <summary>
		/// Converts the given <see cref="ICondition"/> to a string representation.
		/// </summary>
		/// <param name="p_cndCondition">The <see cref="ICondition"/> to convert.</param>
		/// <returns>The string representation of the given <see cref="ICondition"/>.</returns>
		public string GetConditionString(ICondition p_cndCondition)
		{
			return Converter.ConditionToCpl(p_cndCondition);
		}

		#region IViewModel Members

		/// <summary>
		/// Validates the current state of the <see cref="ConditionalOptionTypeResolver"/>.
		/// </summary>
		/// <returns>Always <c>true</c>.</returns>
		public bool Validate()
		{
			return true;
		}

		#endregion
	}
}
