using System;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// The <see cref="PanelToolStripItem"/> wrapper for the control
	/// used as tabs for the <see cref="VerticalTabControl"/>.
	/// </summary>
	/// <see cref="PanelToolStripItem"/>
	public class VerticalTabButton : PanelToolStripItem
	{
		private VerticalTabPage m_tpgPage = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="StatusButton"/> that is used for this tab.
		/// </summary>
		/// <value>The <see cref="StatusButton"/> that is used for this tab.</value>
		protected StatusButton StatusButton
		{
			get
			{
				return (StatusButton)Button;
			}
		}

		/// <summary>
		/// Gets the <see cref="VerticalTabPage"/> associated with this tab.
		/// </summary>
		/// <value>The <see cref="VerticalTabPage"/> associated with this tab.</value>
		public VerticalTabPage TabPage
		{
			get
			{
				return m_tpgPage;
			}
		}

		/// <summary>
		/// Gets or sets the text of the tab.
		/// </summary>
		/// <value>The text of the tab.</value>
		public string Text
		{
			get
			{
				return StatusButton.Text;
			}
			set
			{
				StatusButton.Text = value;
			}
		}

		#endregion

		#region Consturctors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_tpgPage">The <see cref="VerticalTabPage"/> associated with this tab.</param>
		internal VerticalTabButton(VerticalTabPage p_tpgPage)
			: base(new StatusButton(), "Click", -1, ToolStripItemDisplayStyle.Text)
		{
			m_tpgPage = p_tpgPage;
			StatusButton.Button.FlatStyle = FlatStyle.Flat;
			StatusButton.Button.FlatAppearance.BorderSize = 0;
			m_tpgPage.BackColorChanged += new EventHandler(m_tpgPage_BackColorChanged);
		}

		#endregion

		/// <summary>
		/// Sets this tab as unselected.
		/// </summary>
		public override void SetUnselected()
		{
			if (TabPage.Parent != null)
				StatusButton.BackColor = TabPage.Parent.BackColor;
		}

		/// <summary>
		/// Sets this tab as selected.
		/// </summary>
		public override void SetSelected()
		{
			base.SetSelected();
			StatusButton.BackColor = TabPage.BackColor;
		}

		/// <summary>
		/// Handles the <see cref="Control.BackColorChanged"/> event of the tab page associated with
		/// this tab button.
		/// </summary>
		/// <remarks>
		/// This keeps the button background colour synchronized with the tab page's background colour
		/// when this is the select tab.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		void m_tpgPage_BackColorChanged(object sender, EventArgs e)
		{
			if ((TabPage.Parent != null) && (((VerticalTabControl)TabPage.Parent).SelectedTabPage == TabPage))
				StatusButton.BackColor = TabPage.BackColor;
		}
	}
}
