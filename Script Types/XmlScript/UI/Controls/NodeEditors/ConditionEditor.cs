using System;
using System.ComponentModel;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// The view for editing an <see cref="ICondition"/>.
	/// </summary>
	public partial class ConditionEditor : NodeEditor
	{
		private ConditionEditorVM m_vmlViewModel = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ConditionEditorVM ViewModel
		{
			private get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				cpePatternEditor.ViewModel = value.EditorViewModel;
				erpErrors.SetError(cpePatternEditor, null);
				value.ConditionValidated += new EventHandler(ConditionalTypePatternValidated);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ConditionEditor()
		{
			InitializeComponent();
		}

		/// <summary>
		/// A simple constructor the initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		public ConditionEditor(ConditionEditorVM p_vmlViewModel)
			:this()
		{
			ViewModel = p_vmlViewModel;
		}

		#endregion

		/// <summary>
		/// Hanldes the <see cref="ConditionEditorVM.ConditionValidated"/> event of the view model.
		/// </summary>
		/// <remarks>This displays any validation errors.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ConditionalTypePatternValidated(object sender, EventArgs e)
		{
			erpErrors.SetError(cpePatternEditor, ViewModel.Error);
		}

		/// <summary>
		/// Hanldes the <see cref="Control.Validated"/> event of the CPL editor.
		/// </summary>
		/// <remarks>This saves the edited CPL.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void cpePatternEditor_Validated(object sender, EventArgs e)
		{
			ViewModel.SaveCondition();
		}

		/// <summary>
		/// Gets the view model of the editor.
		/// </summary>
		/// <returns>The view model of the editor.</returns>
		public override IViewModel GetViewModel()
		{
			return ViewModel;
		}
	}
}
