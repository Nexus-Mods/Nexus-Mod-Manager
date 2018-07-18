using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.Commands.Generic;
using Nexus.UI.Controls;
using Nexus.Client.Util;
using ConditionalTypePattern = Nexus.Client.ModManagement.Scripting.XmlScript.ConditionalOptionTypeResolver.ConditionalTypePattern;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// Edits a <see cref="ConditionalOptionTypeResolver"/>.
	/// </summary>
	public partial class ConditionalTypeEditor : NodeEditor
	{
		private ConditionalTypeEditorVM m_vmlViewModel = null;
		private ConditionalOptionTypeResolver m_otrTypeResolver = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ConditionalTypeEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				cpePatternEditor.ViewModel = value.ConditionalTypePatternEditorVM;

				cbxDefaultType.DataSource = value.OptionTypes;
				BindTypeResolver(value.TypeResolver);

				value.PatternAdded += new EventHandler<EventArgs<ConditionalTypePattern>>(PatternAdded);
				value.PatternEdited += new EventHandler<EventArgs<ConditionalTypePattern>>(PatternEdited);
				value.PatternDeleted += new EventHandler<EventArgs<ConditionalTypePattern>>(PatternDeleted);

				value.EditCommand.Executed += new EventHandler<EventArgs<ConditionalTypePattern>>(EditCommand_Executed);
				new ToolStripItemCommandBinding<ConditionalTypePattern>(tsbEdit, value.EditCommand, GetSelectedConditionalTypePattern);
				new EventCommandBinding<ConditionalTypePattern>(lvwConditionalTypes, "ItemActivate", value.EditCommand, GetSelectedConditionalTypePattern);

				value.AddCommand.Executed += new EventHandler<EventArgs<ConditionalTypePattern>>(EditCommand_Executed);
				new ToolStripItemCommandBinding<ConditionalTypePattern>(tsbAdd, value.AddCommand, () => { return null; });

				new ToolStripItemCommandBinding<ConditionalTypePattern>(tsbDelete, value.DeleteCommand, GetSelectedConditionalTypePattern);
				new KeyDownCommandBinding<ConditionalTypePattern>(lvwConditionalTypes, value.DeleteCommand, GetSelectedConditionalTypePattern, Keys.Delete);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ConditionalTypeEditor()
		{
			InitializeComponent();
		}

		/// <summary>
		/// A simple constructor that initializes the view with its dependencies.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		public ConditionalTypeEditor(ConditionalTypeEditorVM p_vmlViewModel)
			: this()
		{
			ViewModel = p_vmlViewModel;
		}

		#endregion

		/// <summary>
		/// Binds the <see cref="ConditionalOptionTypeResolver"/> being edited to the UI.
		/// </summary>
		/// <param name="p_ctpTypeResolver">The <see cref="ConditionalOptionTypeResolver"/> being edited.</param>
		protected void BindTypeResolver(ConditionalOptionTypeResolver p_ctpTypeResolver)
		{
			m_otrTypeResolver = p_ctpTypeResolver;
			cbxDefaultType.DataBindings.Clear();
			BindingHelper.CreateFullBinding(cbxDefaultType, () => cbxDefaultType.SelectedItem, p_ctpTypeResolver, () => p_ctpTypeResolver.DefaultType);

			lvwConditionalTypes.Items.Clear();
			foreach (ConditionalOptionTypeResolver.ConditionalTypePattern ctpPattern in p_ctpTypeResolver.ConditionalTypePatterns)
				AddConditionalTypePattern(ctpPattern);
		}

		#region Pattern Binding

		/// <summary>
		/// Binds a <see cref="ConditionalTypePattern"/> to the list displaying the
		/// <see cref="ConditionalOptionTypeResolver"/>'s patterns.
		/// </summary>
		/// <param name="p_ctpPattern">The <see cref="ConditionalTypePattern"/> to bind.</param>
		protected void AddConditionalTypePattern(ConditionalTypePattern p_ctpPattern)
		{
			ListViewItem lviType = new ListViewItem(p_ctpPattern.Type.ToString());

			lviType.SubItems[0].Name = "Type";
			lviType.SubItems.Add(ViewModel.GetConditionString(p_ctpPattern.Condition));
			lviType.SubItems[1].Name = "Condition";
			lviType.Tag = p_ctpPattern;
			lvwConditionalTypes.Items.Add(lviType);
		}

		/// <summary>
		/// Handles the <see cref="NodeEditors.ConditionalTypeEditorVM.PatternAdded"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This binds the new <see cref="ConditionalTypePattern"/> to the list of patterns.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{ConditionalTypePattern}"/> describing the event arguments.</param>
		private void PatternAdded(object sender, EventArgs<ConditionalTypePattern> e)
		{
			AddConditionalTypePattern(e.Argument);
			AdjustColumnWidths();
		}

		/// <summary>
		/// Handles the <see cref="NodeEditors.ConditionalTypeEditorVM.PatternEdited"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This updates the binding to the list of patterns of the edited <see cref="ConditionalTypePattern"/>.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{ConditionalTypePattern}"/> describing the event arguments.</param>
		private void PatternEdited(object sender, EventArgs<ConditionalTypePattern> e)
		{
			ConditionalTypePattern ctpPattern = e.Argument;
			ListViewItem lviPattern = null;
			for (Int32 i = lvwConditionalTypes.Items.Count - 1; i >= 0; i--)
				if (lvwConditionalTypes.Items[i].Tag == ctpPattern)
				{
					lviPattern = lvwConditionalTypes.Items[i];
					break;
				}

			lviPattern.SubItems["Type"].Text = ctpPattern.Type.ToString();
			lviPattern.SubItems["Condition"].Text = ViewModel.GetConditionString(ctpPattern.Condition);
			AdjustColumnWidths();
		}

		/// <summary>
		/// Handles the <see cref="NodeEditors.ConditionalTypeEditorVM.PatternDeleted"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This removes the deleted <see cref="ConditionalTypePattern"/> from the list of patterns.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{ConditionalTypePattern}"/> describing the event arguments.</param>
		private void PatternDeleted(object sender, EventArgs<ConditionalTypePattern> e)
		{
			ConditionalTypePattern ctpPattern = e.Argument;
			for (Int32 i = lvwConditionalTypes.Items.Count - 1; i >= 0; i--)
				if (lvwConditionalTypes.Items[i].Tag == ctpPattern)
				{
					lvwConditionalTypes.Items.RemoveAt(i);
					break;
				}
			AdjustColumnWidths();
		}

		#endregion

		#region Command Handling

		/// <summary>
		/// The retrieves the <see cref="ConditionalTypePattern"/> currently selected in the list of patterns.
		/// </summary>
		/// <returns>The currently selected <see cref="ConditionalTypePattern"/>. <c>null</c> if no pattern is selected.</returns>
		private ConditionalTypePattern GetSelectedConditionalTypePattern()
		{
			if (lvwConditionalTypes.SelectedItems.Count == 0)
				return null;
			return (ConditionalTypePattern)lvwConditionalTypes.SelectedItems[0].Tag;
		}

		/// <summary>
		/// Handles the <see cref="Command{ConditionalTypePattern}.Executed"/> event of the edit command.
		/// </summary>
		/// <remarks>
		/// This passes focus to the pattern editor control.
		/// </remarks>
		/// <param name="p_objCommand">The command that executed.</param>
		/// <param name="p_eeaArguments">An <see cref="EventArgs{ConditionalTypePattern}"/> of the command that executed.</param>
		private void EditCommand_Executed(object p_objCommand, EventArgs<ConditionalTypePattern> p_eeaArguments)
		{
			cpePatternEditor.Focus();
		}

		/// <summary>
		/// Handles the <see cref="ListView.SelectedIndexChanged"/> of the pattern list view.
		/// </summary>
		/// <remarks>
		/// This updates the executable status based on whether a pattern is selected.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwConditionalTypes_SelectedIndexChanged(object sender, EventArgs e)
		{
			ViewModel.EditCommand.CanExecute = (lvwConditionalTypes.SelectedItems.Count > 0);
			ViewModel.DeleteCommand.CanExecute = (lvwConditionalTypes.SelectedItems.Count > 0);
		}

		#endregion

		/// <summary>
		/// Hanldes the <see cref="Control.Resize"/> event of the pattern list view.
		/// </summary>
		/// <remarks>
		/// This adjusts the column widths.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwConditionalTypes_Resize(object sender, EventArgs e)
		{
			AdjustColumnWidths();
		}

		/// <summary>
		/// This resizes the columns of the pattern list.
		/// </summary>
		/// <remarks>
		/// This sizes the type column to its content, and then resizes the remaining columns to take up the rest
		/// of the space.
		/// </remarks>
		protected void AdjustColumnWidths()
		{
			clmType.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
			Int32 intWidth = (lvwConditionalTypes.ClientSize.Width - clmType.Width) / (lvwConditionalTypes.Columns.Count - 1);
			for (Int32 i = 0; i < lvwConditionalTypes.Columns.Count; i++)
				if (lvwConditionalTypes.Columns[i] != clmType)
					lvwConditionalTypes.Columns[i].Width = intWidth;
		}

		/// <summary>
		/// Gets the view model of the editor.
		/// </summary>
		/// <returns>The view model of the editor.</returns>
		public override IViewModel GetViewModel()
		{
			return (IViewModel)ViewModel;
		}
	}
}
