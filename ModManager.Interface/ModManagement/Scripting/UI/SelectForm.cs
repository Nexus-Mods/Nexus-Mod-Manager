using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Nexus.Client.ModManagement.Scripting.UI
{
	/// <summary>
	/// A form that allows the selection from a list of options.
	/// </summary>
	public partial class SelectForm : Form
	{
		#region Properties

		/// <summary>
		/// Gets the names of the selected options.
		/// </summary>
		public string[] SelectedOptionNames
		{
			get
			{
				if (DialogResult == DialogResult.Cancel)
					return new string[0];
				List<String> lstSelected = new List<string>();
				IEnumerable enmSelections = lvwOptions.CheckBoxes ? (IEnumerable)lvwOptions.CheckedItems : (IEnumerable)lvwOptions.SelectedItems;
				foreach (ListViewItem lviOption in enmSelections)
				{
					SelectOption sopOption = (SelectOption)lviOption.Tag;
					lstSelected.Add(sopOption.Name);
				}
				return lstSelected.ToArray();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_lstOptions">The list of option to display in the form.</param>
		/// <param name="p_strTitle">The title of the form.</param>
		/// <param name="p_booMultiSelect">Whether to allow the selection of multiple options.</param>
		public SelectForm(IList<SelectOption> p_lstOptions, string p_strTitle, bool p_booMultiSelect)
		{
			this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
			InitializeComponent();

			Text = p_strTitle;
			lvwOptions.CheckBoxes = p_booMultiSelect;

			LoadOptions(p_lstOptions);
			if ((lvwOptions.Items.Count > 0) && (lvwOptions.SelectedItems.Count == 0))
				lvwOptions.Items[0].Selected = true;
			HidePanels();
		}

		#endregion

		/// <summary>
		/// Loads the options into the form.
		/// </summary>
		/// <param name="p_lstOptions">The list of options.</param>
		private void LoadOptions(IList<SelectOption> p_lstOptions)
		{
			AdjustListViewColumnWidth();
			foreach (SelectOption sopOption in p_lstOptions)
				AddOption(sopOption);
		}

		/// <summary>
		/// Sizes the column of the list view of options to fill the control.
		/// </summary>
		private void AdjustListViewColumnWidth()
		{
			lvwOptions.Columns[0].Width = lvwOptions.Width - SystemInformation.VerticalScrollBarWidth - 6;
		}

		/// <summary>
		/// Adds an option to the list of options.
		/// </summary>
		/// <param name="p_sopOption">The option to add.</param>
		private void AddOption(SelectOption p_sopOption)
		{
			string strName = p_sopOption.Name;
			ListViewItem lviOption = null;
			foreach (ListViewItem lviExistingOption in lvwOptions.Items)
				if (lviExistingOption.Text.Equals(strName))
				{
					lviOption = lviExistingOption;
					break;
				}
			if (lviOption == null)
			{
				lviOption = new ListViewItem();
				lvwOptions.Items.Add(lviOption);
			}

			lviOption.Text = strName;
			lviOption.Tag = p_sopOption;
			lviOption.Checked = (lvwOptions.CheckBoxes && p_sopOption.IsDefault);
			if (!lvwOptions.CheckBoxes && p_sopOption.IsDefault)
				lviOption.Selected = true;
		}

		/// <summary>
		/// Handles the <see cref="Control.SizeChanged"/> event of the list view of options.
		/// </summary>
		/// <remarks>
		/// This ensures that the column of the list view of options fills the control.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwOptions_SizeChanged(object sender, EventArgs e)
		{
			AdjustListViewColumnWidth();
		}

		/// <summary>
		/// Handles the <see cref="ListView.SelectedIndexChanged"/> event of the list view of options.
		/// </summary>
		/// <remarks>
		/// This changes the displayed description to that of the selected option.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwOptions_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (lvwOptions.SelectedItems.Count > 0)
			{
				SelectOption sopOption = (SelectOption)lvwOptions.SelectedItems[0].Tag;
				tbxDescription.Text = sopOption.Description;
				pbxImage.Image = sopOption.Image;
				HidePanels();
			}
			else
			{
				tbxDescription.Text = "";
				pbxImage.Image = null;
			}
		}

		/// <summary>
		/// Hides the description and image panels if they are empty.
		/// </summary>
		private void HidePanels()
		{
			bool booCollapseImage = (pbxImage.Image == null);
			bool booCollapseDescription = String.IsNullOrEmpty(tbxDescription.Text);
			sptPlugins.Panel2Collapsed = booCollapseImage && booCollapseDescription;
			if (!(booCollapseImage && booCollapseDescription))
			{
				sptImage.Panel2Collapsed = booCollapseImage;
				sptImage.Panel1Collapsed = booCollapseDescription;
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the OK button.
		/// </summary>
		/// <remarks>
		/// This closes the form.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butOK_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.OK;
		}
	}
}
