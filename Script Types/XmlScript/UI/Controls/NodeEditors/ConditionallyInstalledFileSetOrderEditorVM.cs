using System.Collections.Generic;
using Nexus.UI.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;
using System;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display a <see cref="ConditionallyInstalledFileSet"/> order editor.
	/// </summary>
	public class ConditionallyInstalledFileSetOrderEditorVM : IViewModel
	{
		#region Properties

		/// <summary>
		/// Gets or sets the list of <see cref="ConditionallyInstalledFileSet"/>s whose order
		/// is being edited.
		/// </summary>
		/// <value>The list of <see cref="ConditionallyInstalledFileSet"/>s whose order
		/// is being edited.</value>
		public IList<ConditionallyInstalledFileSet> ConditionallyInstalledFileSets { get; protected set; }

		/// <summary>
		/// Gets or sets the <see cref="CPLConverter"/> to use to convert the <see cref="ConditionallyInstalledFileSet"/>'s
		/// conditions to a string.
		/// </summary>
		/// <value>The <see cref="CPLConverter"/> to use to convert the <see cref="ConditionallyInstalledFileSet"/>'s
		/// conditions to a string.</value>
		protected CPLConverter Converter { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view model with its dependencies.
		/// </summary>
		/// <param name="p_lstConditionallyInstalledFileSets">The list of <see cref="ConditionallyInstalledFileSet"/>s whose order
		/// is being edited.</param>
		/// <param name="p_cvtConverter">The <see cref="CPLConverter"/> to use to convert the <see cref="ConditionallyInstalledFileSet"/>'s
		/// conditions to a string.</param>
		public ConditionallyInstalledFileSetOrderEditorVM(IList<ConditionallyInstalledFileSet> p_lstConditionallyInstalledFileSets, CPLConverter p_cvtConverter)
		{
			ConditionallyInstalledFileSets = p_lstConditionallyInstalledFileSets;
			Converter = p_cvtConverter;
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

		/// <summary>
		/// Moves the pattern at the specified old index to the new index.
		/// </summary>
		/// <param name="p_intOldIndex">The index of the pattern to move.</param>
		/// <param name="p_intNewIndex">The new index to which to move the pattern.</param>
		public void MovePattern(Int32 p_intOldIndex, Int32 p_intNewIndex)
		{
			ConditionallyInstalledFileSet ifsSetToMove = ConditionallyInstalledFileSets[p_intOldIndex];
			ConditionallyInstalledFileSets.RemoveAt(p_intOldIndex);
			ConditionallyInstalledFileSets.Insert(p_intNewIndex, ifsSetToMove);
		}

		#region IViewModel Members

		/// <summary>
		/// Validates the current <see cref="ConditionallyInstalledFileSet"/> order.
		/// </summary>
		/// <returns>Always <c>true</c>.</returns>
		public bool Validate()
		{
			return true;
		}

		#endregion
	}
}
