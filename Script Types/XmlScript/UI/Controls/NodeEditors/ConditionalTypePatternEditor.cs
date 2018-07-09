using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.Util;
using ConditionalTypePattern = Nexus.Client.ModManagement.Scripting.XmlScript.ConditionalOptionTypeResolver.ConditionalTypePattern;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// Edits a <see cref="ConditionalTypePattern"/>.
	/// </summary>
	public partial class ConditionalTypePatternEditor : UserControl
	{
		private ConditionalTypePatternEditorVM m_vmlViewModel = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ConditionalTypePatternEditorVM ViewModel
		{
			private get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				cndConditionEditor.ViewModel = value.ConditionEditorVM;
				cbxType.DataSource = value.OptionTypes;
				BindConditionalTypePattern(value.ConditionalTypePattern);
				value.PropertyChanged += new PropertyChangedEventHandler(ViewModel_PropertyChanged);
				value.ConditionalTypePatternValidated += new EventHandler(ConditionalTypePatternValidated);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ConditionalTypePatternEditor()
		{
			InitializeComponent();
		}

		#endregion
		
		/// <summary>
		/// Binds the <see cref="ConditionalTypePattern"/> being edited to the UI.
		/// </summary>
		/// <param name="p_ctpPattern">The <see cref="ConditionalTypePattern"/> being edited.</param>
		protected void BindConditionalTypePattern(ConditionalTypePattern p_ctpPattern)
		{
			erpErrors.SetError(butSave, null);
			if (p_ctpPattern == null)
			{
				butSave.Text = "Add";
				return;
			}
			else
				butSave.Text = "Save";
			cbxType.SelectedItem = p_ctpPattern.Type;
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This binds the UI to the updated <see cref="ConditionalTypePattern"/>.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<ConditionalTypePatternEditorVM>(x => x.ConditionalTypePattern)))
				BindConditionalTypePattern(ViewModel.ConditionalTypePattern);
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the save button.
		/// </summary>
		/// <remarks>
		/// This asks the view model to save the pattern.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSave_Click(object sender, EventArgs e)
		{
			ViewModel.SavePattern((OptionType)cbxType.SelectedItem);
		}

		/// <summary>
		/// This handles the <see cref="ConditionalTypePatternEditorVM.ConditionalTypePatternValidated"/>
		/// event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays any validation errors.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void ConditionalTypePatternValidated(object sender, EventArgs e)
		{
			erpErrors.SetError(butSave, ViewModel.Error);
		}
	}
}
