using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Nexus.Client.Controls
{
	/// <summary>
	/// A multiline label that resizes with the content.
	/// </summary>
	/// <remarks>
	/// Currently, the label only resizes its height.
	/// </remarks>
	public class AutosizeLabel : RichTextBox
	{
		private ScrollableControl m_ctlNonExistant = new ScrollableControl();
		private RichTextBoxScrollBars m_sbrScrollBars = RichTextBoxScrollBars.None;
		private DockStyle m_dksDockStyle = DockStyle.None;
		private bool m_booAllowSelection = false;

		#region Properties

		/// <summary>
		/// Gets or sets whether text selection is allowed.
		/// </summary>
		/// <value>Whether text selection is allowed.</value>
		[Browsable(true)]
		[Category("Behavior")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		[DefaultValue(false)]
		public bool AllowSelection
		{
			get
			{
				return m_booAllowSelection;
			}
			set
			{
				if (m_booAllowSelection != value)
				{
					m_booAllowSelection = value;
					if (m_booAllowSelection)
						this.Enter -= new EventHandler(AutosizeLabel_Enter);
					else
						this.Enter += new EventHandler(AutosizeLabel_Enter);
					SetUpScrollHandling();
				}
			}
		}

		/// <summary>
		/// Gets or sets whether the label is multiline.
		/// </summary>
		/// <remarks>
		/// Since the label autosizes, this is always true.
		/// </remarks>
		/// <value>Always <c>true</c>.</value>
		[DefaultValue(true)]
		public new bool Multiline
		{
			get
			{
				return base.Multiline;
			}
			set
			{
				base.Multiline = true;
			}
		}

		/// <summary>
		/// Gets or sets whether the label has scrollbars.
		/// </summary>
		/// <value>Whether the label has scrollbars.</value>
		[DefaultValue(typeof(RichTextBoxScrollBars), "None")]
		public new RichTextBoxScrollBars ScrollBars
		{
			get
			{
				return m_sbrScrollBars;
			}
			set
			{
				if (m_sbrScrollBars != value)
				{
					m_sbrScrollBars = value;
					SetUpScrollHandling();
				}
			}
		}

		/// <summary>
		/// Gets or sets the control's dock style.
		/// </summary>
		/// <value>The control's dock style.</value>
		[DefaultValue(typeof(DockStyle), "None")]
		public new DockStyle Dock
		{
			get
			{
				return m_dksDockStyle;
			}
			set
			{
				if (m_dksDockStyle != value)
				{
					m_dksDockStyle = value;
					SetUpScrollHandling();
				}
			}
		}

		/// <summary>
		/// Gets or sets the border style.
		/// </summary>
		/// <value>The border style.</value>
		[DefaultValue(typeof(BorderStyle), "None")]
		public new BorderStyle BorderStyle
		{
			get
			{
				return base.BorderStyle;
			}
			set
			{
				base.BorderStyle = value;
			}
		}

		/// <summary>
		/// Gets or sets the back colour.
		/// </summary>
		/// <value>The back colour.</value>
		[DefaultValue(typeof(SystemColors), "Control")]
		public new Color BackColor
		{
			get
			{
				return base.BackColor;
			}
			set
			{
				base.BackColor = value;
			}
		}

		/// <summary>
		/// Gets or sets whether the label is readonly.
		/// </summary>
		/// <remarks>
		/// Since this is a label, this is always true.
		/// </remarks>
		/// <value>Always <c>true</c>.</value>
		[DefaultValue(true)]
		public new bool ReadOnly
		{
			get
			{
				return base.ReadOnly;
			}
			set
			{
				base.ReadOnly = true;
			}
		}

		/// <summary>
		/// Gets or sets whether the label has a tab stop.
		/// </summary>
		/// <remarks>
		/// Since this is a label, this is always false.
		/// </remarks>
		/// <value>Always <c>false</c>.</value>
		[DefaultValue(false)]
		public new bool TabStop
		{
			get
			{
				return base.TabStop;
			}
			set
			{
				base.TabStop = false;
			}
		}

		/// <summary>
		/// Gets or sets the cursor for the label.
		/// </summary>
		/// <remarks>
		/// Since this is a label, this is always an arrow.
		/// </remarks>
		/// <value>Always <see cref="Cursors.Arrow"/>.</value>
		[DefaultValue(typeof(Cursors), "Arrow")]
		public new Cursor Cursor
		{
			get
			{
				return base.Cursor;
			}
			set
			{
				base.Cursor = Cursors.Arrow;
			}
		}

		#endregion

		/// <summary>
		/// The default constructor.
		/// </summary>
		public AutosizeLabel()
		{
			m_ctlNonExistant.Name = "NonExistant";
			m_ctlNonExistant.AutoScroll = true;
			this.Multiline = true;
			this.ScrollBars = RichTextBoxScrollBars.None;
			this.BorderStyle = BorderStyle.None;
			this.BackColor = SystemColors.Control;
			this.ReadOnly = true;
			this.TabStop = false;
			this.Cursor = Cursors.Arrow;
			//set this to true so that when we set AllowSelection to false,
			// the setter doesn't short circut, and actually performs the
			// required set up.
			m_booAllowSelection = true;
			AllowSelection = false;
			SetTextColor();
		}

		/// <summary>
		/// Raises the <see cref="Control.ParentChanged"/> event.
		/// </summary>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnParentChanged(EventArgs e)
		{
			base.OnParentChanged(e);
			if (Parent == m_ctlNonExistant)
				return;
			if (Parent != null)
				SetUpScrollHandling();
		}

		/// <summary>
		/// Sets up the control to handle srolling.
		/// </summary>
		private void SetUpScrollHandling()
		{
			if (AllowSelection)
			{
				base.ScrollBars = m_sbrScrollBars;
				base.Dock = m_dksDockStyle;
				if ((m_ctlNonExistant.Parent != null) && (m_ctlNonExistant.Parent != Parent))
				{
					Control ctlParent = m_ctlNonExistant.Parent;
					m_ctlNonExistant.Parent = null;
					ctlParent.Controls.Add(this);
				}
			}
			else
			{
				base.ScrollBars = RichTextBoxScrollBars.None;
				switch (m_sbrScrollBars)
				{
					case RichTextBoxScrollBars.Both:
					case RichTextBoxScrollBars.ForcedBoth:
					case RichTextBoxScrollBars.ForcedVertical:
					case RichTextBoxScrollBars.Vertical:
						base.Dock = DockStyle.Top;
						break;
					case RichTextBoxScrollBars.ForcedHorizontal:
					case RichTextBoxScrollBars.Horizontal:
						base.Dock = DockStyle.Left;
						break;
					case RichTextBoxScrollBars.None:
					default:
						base.Dock = DockStyle.Fill;
						break;
				}
				m_ctlNonExistant.Dock = m_dksDockStyle;
				if ((Parent != null) && (Parent != m_ctlNonExistant))
					Parent.Controls.Add(m_ctlNonExistant);
				if (Parent != m_ctlNonExistant)
					m_ctlNonExistant.Controls.Add(this);
				m_ctlNonExistant.Size = Size;
			}
		}

		/// <summary>
		/// Resizes the label as the content size changes.
		/// </summary>
		/// <param name="e">A <see cref="ContentsResizedEventArgs"/> describing the event arguments.</param>
		protected override void OnContentsResized(ContentsResizedEventArgs e)
		{
			Height = e.NewRectangle.Height + 5;
			base.OnContentsResized(e);
		}

		/// <summary>
		/// Raises the <see cref="Control.SizeChanged"/> event.
		/// </summary>
		/// <remarks>
		/// This resizes the non-existant control that privodes scrolling for the label.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnSizeChanged(EventArgs e)
		{
			if (m_ctlNonExistant.Parent != null)
			{
				switch (m_ctlNonExistant.Dock)
				{
					case DockStyle.Bottom:
					case DockStyle.Top:
						m_ctlNonExistant.Size = new Size(m_ctlNonExistant.Width, Size.Height);
						break;
					case DockStyle.Left:
					case DockStyle.Right:
						m_ctlNonExistant.Size = new Size(Size.Width, m_ctlNonExistant.Height);
						break;
					case DockStyle.None:
						m_ctlNonExistant.Size = Size;
						break;
				}
			}
			base.OnSizeChanged(e);
		}

		/// <summary>
		/// Makes sure all text doesn't look disabled.
		/// </summary>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnTextChanged(EventArgs e)
		{
			base.OnTextChanged(e);
			SetTextColor();
		}

		/// <summary>
		/// Forces the text color not to look disabled.
		/// </summary>
		protected void SetTextColor()
		{
			SelectAll();
			SelectionColor = SystemColors.ControlText;
			Select(0, 0);
		}

		/// <summary>
		/// Raises the <see cref="RichTextBox.LinkClicked"/> event.
		/// </summary>
		/// <remarks>
		/// This ask the OS to launch the clicked link.
		/// </remarks>
		/// <param name="e">A <see cref="LinkClickedEventArgs"/> describing the event arguments.</param>
		protected override void OnLinkClicked(LinkClickedEventArgs e)
		{
			Uri uriUrl = new Uri(e.LinkText);
			System.Diagnostics.Process.Start(uriUrl.ToString());
			base.OnLinkClicked(e);
		}

		/// <summary>
		/// Handles the <see cref="Control.Enter"/> event of the autosize label.
		/// </summary>
		/// <remarks>
		/// This gives the hidden scrollable panel focus instead of the label,
		/// so that the user can't select any text.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		public void AutosizeLabel_Enter(object sender, EventArgs e)
		{
			m_ctlNonExistant.Focus();
		}

		/// <summary>
		/// Brings the label to the foreground.
		/// </summary>
		public new void BringToFront()
		{
			if (AllowSelection)
				base.BringToFront();
			else
				m_ctlNonExistant.BringToFront();
		}

		/// <summary>
		/// Sends the label to the background.
		/// </summary>
		public new void SendToBack()
		{
			if (AllowSelection)
				base.BringToFront();
			else
				m_ctlNonExistant.BringToFront();
		}
	}
}
