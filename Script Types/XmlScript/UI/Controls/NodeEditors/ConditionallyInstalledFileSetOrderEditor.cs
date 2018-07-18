using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows.Forms;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// Edits the order of an <see cref="XmlScript"/>'s <see cref="ConditionallyInstalledFileSet"/>s.
	/// </summary>
	public partial class ConditionallyInstalledFileSetOrderEditor : NodeEditor
	{
		private ConditionallyInstalledFileSetOrderEditorVM m_vmlViewModel = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ConditionallyInstalledFileSetOrderEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				BindConditionallyInstalledFileSets(value.ConditionallyInstalledFileSets);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view with its dependencies.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		public ConditionallyInstalledFileSetOrderEditor(ConditionallyInstalledFileSetOrderEditorVM p_vmlViewModel)
		{
			InitializeComponent();
			ViewModel = p_vmlViewModel;
		}

		#endregion

		/// <summary>
		/// Binds the <see cref="ConditionallyInstalledFileSet"/>s whose order is being edited to the UI.
		/// </summary>
		/// <param name="p_lstConditionallyInstalledFileSets">The <see cref="ConditionallyInstalledFileSet"/>s
		/// whose order is being edited.</param>
		protected void BindConditionallyInstalledFileSets(IList<ConditionallyInstalledFileSet> p_lstConditionallyInstalledFileSets)
		{
			rlvConditionalInstalls.Items.Clear();
			foreach (ConditionallyInstalledFileSet cipPattern in p_lstConditionallyInstalledFileSets)
			{
				ListViewItem lviItem = new ListViewItem(ViewModel.GetConditionString(cipPattern.Condition));
				StringBuilder stbFiles = new StringBuilder();
				foreach (InstallableFile iflFile in cipPattern.Files)
					stbFiles.Append(iflFile.Destination).Append(", ");
				if (stbFiles.Length > 0)
					stbFiles.Remove(stbFiles.Length - 2, 2);
				lviItem.SubItems.Add(stbFiles.ToString());
				lviItem.Tag = cipPattern;
				rlvConditionalInstalls.Items.Add(lviItem);
			}
		}

		/// <summary>
		/// Gets the view model of the editor.
		/// </summary>
		/// <returns>The view model of the editor.</returns>
		public override IViewModel GetViewModel()
		{
			return ViewModel;
		}

		/// <summary>
		/// Handles the <see cref="Control.Resize"/> event of the list view of file sets.
		/// </summary>
		/// <remarks>
		/// This resizes the list view's columns.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void rlvConditionalInstalls_Resize(object sender, EventArgs e)
		{
			clmCondition.Width = rlvConditionalInstalls.ClientSize.Width / 2;
			clmFiles.Width = rlvConditionalInstalls.ClientSize.Width / 2;
		}

		/// <summary>
		/// Handles the <see cref="ReorderableListView.ItemsReordered"/> event of the list view of file sets.
		/// </summary>
		/// <remarks>
		/// This propagates the order change to the view model.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ReorderedItemsEventArgs"/> describing the event arguments.</param>
		private void rlvConditionalInstalls_ItemsReordered(object sender, ReorderedItemsEventArgs e)
		{
			foreach (ReorderedItemsEventArgs.ReorderedListViewItem rliItem in e.ReorderedListViewItems)
				ViewModel.MovePattern(rliItem.OldIndex, rliItem.NewIndex);
		}
	}
}
