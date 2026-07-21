using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A wizard control.
	/// </summary>
	[DefaultProperty("SelectedPage")]
	[DefaultEvent("SelectedTabPageChanged")]
	[Designer(typeof(WizardControlDesigner))]
	public class WizardControl : VerticalTabControl
	{
		/// <summary>
		/// Raised when the finish button is clicked.
		/// </summary>
		[Category("Action")]
		public event EventHandler Finished = delegate { };

		/// <summary>
		/// Raised when the cancel button is clicked.
		/// </summary>
		[Category("Action")]
		public event EventHandler Cancelled = delegate { };

		private Panel m_pnlNavigation = null;
		private Panel m_pnlNavigationShadow = null;
		private Panel m_pnlNavigationLight = null;
		private Button m_butPrevious = null;
		private Button m_butNext = null;
		private Button m_butCancel = null;

		#region Properties

		/// <summary>
		/// Gets the wizard's previous button.
		/// </summary>
		/// <value>The wizard's previous button.</value>
		[Browsable(false)]
		public Button PreviousButton
		{
			get
			{
				return m_butPrevious;
			}
		}

		/// <summary>
		/// Gets the wizard's next button.
		/// </summary>
		/// <value>The wizard's next button.</value>
		[Browsable(false)]
		public Button NextButton
		{
			get
			{
				return m_butNext;
			}
		}

		/// <summary>
		/// Gets or sets whether the tabs are visible.
		/// </summary>
		/// <value>Whether the tabs are visible.</value>
		[Category("Appearance")]
		[DefaultValue(false)]
		public override bool TabsVisible
		{
			get
			{
				return base.TabsVisible;
			}
			set
			{
				base.TabsVisible = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public WizardControl()
		{
			TabsVisible = false;
			BackColor = Color.FromKnownColor(KnownColor.Control);

			m_pnlNavigation = new Panel();
			m_pnlNavigation.Dock = DockStyle.Bottom;
			m_pnlNavigation.Height = 23 + 2 * 12;
			m_pnlNavigation.DataBindings.Add("BackColor", this, "BackColor");

			m_pnlNavigationLight = new Panel();
			m_pnlNavigationLight.BackColor = System.Drawing.SystemColors.ControlLightLight;
			m_pnlNavigationLight.Dock = System.Windows.Forms.DockStyle.Top;
			m_pnlNavigationLight.Location = new System.Drawing.Point(0, 1);
			m_pnlNavigationLight.Size = new System.Drawing.Size(444, 1);
			m_pnlNavigationLight.TabIndex = 1;

			m_pnlNavigationShadow = new Panel();
			m_pnlNavigationShadow.BackColor = System.Drawing.SystemColors.ControlDark;
			m_pnlNavigationShadow.Dock = System.Windows.Forms.DockStyle.Top;
			m_pnlNavigationShadow.Location = new System.Drawing.Point(0, 0);
			m_pnlNavigationShadow.Size = new System.Drawing.Size(444, 1);
			m_pnlNavigationShadow.TabIndex = 2;

			m_pnlNavigation.Controls.Add(m_pnlNavigationLight);
			m_pnlNavigation.Controls.Add(m_pnlNavigationShadow);

			Controls.Add(m_pnlNavigation);

			m_butCancel = new Button();
			m_butCancel.Text = "Cancel";
			m_butCancel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
			m_butCancel.Size = new Size(75, 23);
			m_butCancel.Location = new Point(m_pnlNavigation.Width - 12 - m_butCancel.Width, 12);
			m_butCancel.Click += new EventHandler(Cancel_Click);
			m_pnlNavigation.Controls.Add(m_butCancel);

			m_butNext = new Button();
			m_butNext.Text = "Next >>";
			m_butNext.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
			m_butNext.Size = new Size(75, 23);
			m_butNext.Location = new Point(m_butCancel.Left - 12 - m_butNext.Width, 12);
			m_butNext.Click += new EventHandler(Next_Click);
			m_pnlNavigation.Controls.Add(m_butNext);

			m_butPrevious = new Button();
			m_butPrevious.Text = "<< Back";
			m_butPrevious.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
			m_butPrevious.Size = new Size(75, 23);
			m_butPrevious.Location = new Point(m_butNext.Left - 6 - m_butPrevious.Width, 12);
			m_butPrevious.Click += new EventHandler(Previous_Click);
			m_pnlNavigation.Controls.Add(m_butPrevious);

			this.Dock = DockStyle.Fill;
		}

		#endregion

		/// <summary>
		/// Raises the <see cref="Control.ControlAdded"/> event.
		/// </summary>
		/// <remarks>
		/// This ensures that the wizard buttons are enabled/disabled correctly.
		/// </remarks>
		/// <param name="e">A <see cref="ControlEventArgs"/> describing the event arguments.</param>
		protected override void OnControlAdded(ControlEventArgs e)
		{
			base.OnControlAdded(e);
			if (e.Control is VerticalTabPage)
				MovePage(0);
		}

		/// <summary>
		/// Raises the <see cref="Control.ControlRemoved"/> event.
		/// </summary>
		/// <remarks>
		/// This ensures that the wizard buttons are enabled/disabled correctly.
		/// </remarks>
		/// <param name="e">A <see cref="ControlEventArgs"/> describing the event arguments.</param>
		protected override void OnControlRemoved(ControlEventArgs e)
		{
			base.OnControlRemoved(e);
			if (e.Control is VerticalTabPage)
				MovePage(0);
		}

		#region Page Navigation

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the cancel button.
		/// </summary>
		/// <remarks>
		/// This raises the <see cref="Cancelled"/> event.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void Cancel_Click(object sender, EventArgs e)
		{
			Cancelled(this, new EventArgs());
		}

		/// <summary>
		/// This navigates to the page whose index is <paramref name="p_intJumpSize"/>
		/// away from the current page's index.
		/// </summary>
		/// <remarks>
		/// This makes sure that the selected index resulting from the jump is never 
		/// out of bounds. It also enables/disables buttons and changes button text as
		/// appropriate.
		/// </remarks>
		/// <param name="p_intJumpSize">The number of pages to jump.</param>
		protected void MovePage(Int32 p_intJumpSize)
		{
			Int32 intNewIndex = SelectedIndex + p_intJumpSize;
			if (intNewIndex < 0)
				intNewIndex = 0;
			else if (intNewIndex >= TabPages.Count)
				intNewIndex = TabPages.Count - 1;

			m_butPrevious.Enabled = (intNewIndex > 0);
			if (intNewIndex == TabPages.Count - 1)
				m_butNext.Text = "Finish";
			else
				m_butNext.Text = "Next >>";
			SelectedIndex = intNewIndex;
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the previous button.
		/// </summary>
		/// <remarks>
		/// This navigates to the previous page, if there is one.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void Previous_Click(object sender, EventArgs e)
		{
			MovePage(-1);
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the next button.
		/// </summary>
		/// <remarks>
		/// This navigates to the next page, if there is one.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void Next_Click(object sender, EventArgs e)
		{
			if (m_butNext.Text.Equals("Finish"))
				Finished(this, new EventArgs());
			else
				MovePage(1);
		}

		#endregion
	}
}
