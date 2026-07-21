using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.Mods;
using Nexus.Client.UI;

namespace Nexus.Client.ModManagement.UI
{
	/// <summary>
	/// A view that allows editing of mod tags.
	/// </summary>
	public partial class ModTaggerForm : ManagedFontForm
	{
		private ModTaggerVM m_vmlViewModel = null;
		private bool m_booResizing = false;
		private Timer m_tmrColumnSizer = new Timer();

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ModTaggerVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				mieTagEditor.ViewModel = m_vmlViewModel.ModInfoEditorVM;
				Icon = m_vmlViewModel.CurrentTheme.Icon;
				foreach (IModInfo mifInfo in m_vmlViewModel.TagCandidates)
				{
					ListViewItem lviTag = new ListViewItem(mifInfo.ModName);
					lviTag.Tag = mifInfo;
					lviTag.SubItems.Add(mifInfo.HumanReadableVersion);
					lvwTagCandidates.Items.Add(lviTag);
				}
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_mtgTaggerVM">The view model that provides the data and operations for this view.</param>
		public ModTaggerForm(ModTaggerVM p_mtgTaggerVM)
		{
			InitializeComponent();
			ViewModel = p_mtgTaggerVM;
			m_tmrColumnSizer.Interval = 100;
			m_tmrColumnSizer.Tick += new EventHandler(ColumnSizer_Tick);
		}

		#endregion

		#region Control Metrics Serialization

		/// <summary>
		/// Raises the <see cref="UserControl.Load"/> event of the control.
		/// </summary>
		/// <remarks>
		/// This loads any saved control metrics.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			if (!DesignMode)
			{
				ViewModel.Settings.SplitterSizes.LoadSplitterSizes("modTagger", splitContainer1);
				ViewModel.Settings.ColumnWidths.LoadColumnWidths("modTagger", lvwTagCandidates);

				FormClosing += new FormClosingEventHandler(ModTaggerForm_FormClosing);
				SizeColumnsToFit();
			}
		}

		/// <summary>
		/// Handles the <see cref="Form.Closing"/> event of the parent form.
		/// </summary>
		/// <remarks>
		/// This save the control's metrics.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="FormClosingEventArgs"/> describing the event arguments.</param>
		private void ModTaggerForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			ViewModel.Settings.SplitterSizes.SaveSplitterSizes("modTagger", splitContainer1);
			ViewModel.Settings.ColumnWidths.SaveColumnWidths("modTagger", lvwTagCandidates);
			ViewModel.Settings.Save();
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="ListView.SelectedIndexChanged"/> event of the list of tag candidates.
		/// </summary>
		/// <remarks>
		/// This loads the selected candidate into the tag editor.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwTagCandidates_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (lvwTagCandidates.SelectedItems.Count > 0)
				ViewModel.LoadTagOption((IModInfo)lvwTagCandidates.SelectedItems[0].Tag);
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the OK button.
		/// </summary>
		/// <remarks>
		/// This save the edited tags.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butOK_Click(object sender, EventArgs e)
		{
			ViewModel.SaveTags();
			DialogResult = DialogResult.OK;
		}

		#region Column Resizing

		/// <summary>
		/// Handles the <see cref="Timer.Tick"/> event of the column sizer timer.
		/// </summary>
		/// <remarks>
		/// We use a timer to autosize the columns in the list view. This is because
		/// there is a bug in the control such that if we reszize the columns continuously
		/// while the list view is being resized, the item will sometimes disappear.
		/// 
		/// To work around this, the list view resize event continually resets the timer.
		/// This means the timer will only fire occasionally during the resize, and avoid
		/// the disappearing items issue.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ColumnSizer_Tick(object sender, EventArgs e)
		{
			((Timer)sender).Stop();
			SizeColumnsToFit();
		}

		/// <summary>
		/// This resizes the columns to fill the list view. The index column is fixed width.
		/// </summary>
		protected void SizeColumnsToFit()
		{
			if (lvwTagCandidates.Columns.Count == 0)
				return;
			m_booResizing = true;
			ColumnHeader[] clmFixedWidthColumns = new ColumnHeader[] { clmVersion };
			Int32 intFixedWidth = 0;
			foreach (ColumnHeader clmFixed in clmFixedWidthColumns)
				intFixedWidth += clmFixed.Width;
			Int32 intWidth = (lvwTagCandidates.ClientSize.Width - intFixedWidth) / (lvwTagCandidates.Columns.Count - clmFixedWidthColumns.Length);
			for (Int32 i = 0; i < lvwTagCandidates.Columns.Count; i++)
				if (Array.IndexOf(clmFixedWidthColumns, lvwTagCandidates.Columns[i]) < 0)
					lvwTagCandidates.Columns[i].Width = intWidth;
			m_booResizing = false;
		}

		/// <summary>
		/// Handles the <see cref="Control.Resize"/> event of the plugin list.
		/// </summary>
		/// <remarks>
		/// This resizes the columns to fill the list view. The index column is fixed width.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwTagCandidates_Resize(object sender, EventArgs e)
		{
			m_tmrColumnSizer.Stop();
			m_tmrColumnSizer.Start();
		}

		/// <summary>
		/// Handles the <see cref="ListView.ColumnWidthChanging"/> event of the plugin list.
		/// </summary>
		/// <remarks>
		/// This resizes the column next to the column being resized to resize as well,
		/// so that the columns keep the list view filled.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ColumnWidthChangingEventArgs"/> describing the event arguments.</param>
		private void lvwTagCandidates_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
		{
			if (m_booResizing)
				return;
			ColumnHeader clmThis = lvwTagCandidates.Columns[e.ColumnIndex];
			ColumnHeader clmOther = null;
			if (e.ColumnIndex == lvwTagCandidates.Columns.Count - 1)
				clmOther = lvwTagCandidates.Columns[e.ColumnIndex - 1];
			else
				clmOther = lvwTagCandidates.Columns[e.ColumnIndex + 1];
			m_booResizing = true;
			clmOther.Width += (clmThis.Width - e.NewWidth);
			m_booResizing = false;
		}

		#endregion
	}
}
