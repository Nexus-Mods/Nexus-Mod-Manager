using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A multiline label that resizes with the content.
	/// </summary>
	/// <remarks>
	/// Currently, the label only resizes its height.
	/// </remarks>
	public class AutosizeLabel : ScrollableControl
	{
		/// <summary>
		/// A <see cref="RichTextBox"/> that is styled to behave like a label.
		/// </summary>
		private class RichTextLabel : RichTextBox
		{
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

			#endregion

			#region Constructors

			/// <summary>
			/// The default constructor.
			/// </summary>
			public RichTextLabel()
			{
				//set this to true so that when we set AllowSelection to false,
				// the setter doesn't short circut, and actually performs the
				// required set up.
				m_booAllowSelection = true;
				SetTextColor();
			}

			#endregion

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
			/// Resizes the label as the content size changes.
			/// </summary>
			/// <param name="e">A <see cref="ContentsResizedEventArgs"/> describing the event arguments.</param>
			protected override void OnContentsResized(ContentsResizedEventArgs e)
			{
				if (!AllowSelection)
					Height = e.NewRectangle.Height + 5;
				//we need to call this AFTER we set our height, as the label may wish to override us
				base.OnContentsResized(e);
			}

			/// <summary>
			/// Forces the text color not to look disabled.
			/// </summary>
			protected void SetTextColor()
			{
				SelectAll();
				SelectionColor = ForeColor;
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
				Parent.Focus();
			}
		}

		RichTextLabel m_rtlLabel = new RichTextLabel();
		private RichTextBoxScrollBars m_sbrScrollBars = RichTextBoxScrollBars.None;

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
				return m_rtlLabel.AllowSelection;
			}
			set
			{
				if (m_rtlLabel.AllowSelection != value)
				{
					m_rtlLabel.AllowSelection = value;
					base.Cursor = value ? Cursors.IBeam : Cursors.Arrow;
					m_rtlLabel.Cursor = value ? Cursors.IBeam : Cursors.Arrow;
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
		[Browsable(true)]
		[Category("Behavior")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		[DefaultValue(true)]
		public bool Multiline
		{
			get
			{
				return m_rtlLabel.Multiline;
			}
			set
			{
				m_rtlLabel.Multiline = true;
			}
		}

		/// <summary>
		/// Gets or sets whether the label has scrollbars.
		/// </summary>
		/// <value>Whether the label has scrollbars.</value>
		[Browsable(true)]
		[Category("Appearance")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		[DefaultValue(typeof(RichTextBoxScrollBars), "None")]
		public RichTextBoxScrollBars ScrollBars
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
		/// Gets or sets the border style.
		/// </summary>
		/// <value>The border style.</value>
		[Browsable(true)]
		[Category("Appearance")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		[DefaultValue(typeof(BorderStyle), "None")]
		public BorderStyle BorderStyle
		{
			get
			{
				return m_rtlLabel.BorderStyle;
			}
			set
			{
				m_rtlLabel.BorderStyle = value;
			}
		}

		/// <summary>
		/// Gets or sets the back colour.
		/// </summary>
		/// <value>The back colour.</value>
		[DefaultValue(typeof(SystemColors), "Control")]
		public override Color BackColor
		{
			get
			{
				return base.BackColor;
			}
			set
			{
				base.BackColor = value;
				m_rtlLabel.BackColor = value;
			}
		}

		/// <summary>
		/// Gets or sets the text colour.
		/// </summary>
		/// <value>The text colour.</value>
		[Browsable(true)]
		[Category("Appearance")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		[DefaultValue(typeof(SystemColors), "ControlText")]
		public override Color ForeColor
		{
			get
			{
				return m_rtlLabel.ForeColor;
			}
			set
			{
				m_rtlLabel.ForeColor = value;
			}
		}

		/// <summary>
		/// Gets or sets the text font.
		/// </summary>
		/// <value>The text font.</value>
		public override Font Font
		{
			get
			{
				return base.Font;
			}
			set
			{
				m_rtlLabel.Font = value;
				base.Font = value;
			}
		}

		/// <summary>
		/// Gets or sets the label's text.
		/// </summary>
		/// <value>The label's text.</value>
		public override string Text
		{
			get
			{
				return m_rtlLabel.Text;
			}
			set
			{
				m_rtlLabel.Text = value;
			}
		}

		/// <summary>
		/// Gets or sets whether the label is readonly.
		/// </summary>
		/// <remarks>
		/// Since this is a label, this is always true.
		/// </remarks>
		/// <value>Always <c>true</c>.</value>
		[Browsable(true)]
		[Category("Behavior")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		[DefaultValue(true)]
		public bool ReadOnly
		{
			get
			{
				return m_rtlLabel.ReadOnly;
			}
			set
			{
				m_rtlLabel.ReadOnly = true;
			}
		}

		/// <summary>
		/// Gets or sets whether the label will automatically detect, and create links for, URLs.
		/// </summary>
		/// <value>Whether the label will automatically detect, and create links for, URLs.</value>
		[Browsable(true)]
		[Category("Behavior")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		[DefaultValue(true)]
		public bool DetectUrls
		{
			get
			{
				return m_rtlLabel.DetectUrls;
			}
			set
			{
				m_rtlLabel.DetectUrls = true;
			}
		}

		/// <summary>
		/// Gets or sets whether the label has a tab stop.
		/// </summary>
		/// <remarks>
		/// Since this is a label, this is always false.
		/// </remarks>
		/// <value>Always <c>false</c>.</value>
		[Browsable(true)]
		[Category("Behavior")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		[DefaultValue(false)]
		public new bool TabStop
		{
			get
			{
				return m_rtlLabel.TabStop;
			}
			set
			{
				m_rtlLabel.TabStop = false;
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
		public override Cursor Cursor
		{
			get
			{
				return base.Cursor;
			}
			set
			{
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public AutosizeLabel()
		{
			AutoScroll = true;
			m_rtlLabel.ContentsResized += new ContentsResizedEventHandler(Label_ContentsResized);
			m_rtlLabel.TextChanged += new EventHandler(Label_TextChanged);
			Controls.Add(m_rtlLabel);
			Multiline = true;
			ScrollBars = RichTextBoxScrollBars.None;
			BorderStyle = BorderStyle.None;
			BackColor = SystemColors.Control;
			ReadOnly = true;
			TabStop = false;
			base.Cursor = Cursors.Arrow;
			AllowSelection = false;
			SetUpScrollHandling();
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="RichTextBox.ContentsResized"/> event of the rich text label.
		/// </summary>
		/// <remarks>
		/// Resizes the label as the content size changes.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ContentsResizedEventArgs"/> describing the event arguments.</param>
		private void Label_ContentsResized(object sender, ContentsResizedEventArgs e)
		{
			if (AllowSelection || (m_sbrScrollBars == RichTextBoxScrollBars.None))
				Height = e.NewRectangle.Height + 5;
			
		}

		/// <summary>
		/// Handles the <see cref="Control.TextChanged"/> event of the rich text label.
		/// </summary>
		/// <remarks>
		/// Raises the <see cref="Control.TextChanged"/> event for the control.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void Label_TextChanged(object sender, EventArgs e)
		{
			OnTextChanged(e);
		}

		/// <summary>
		/// Sets up the control to handle srolling.
		/// </summary>
		private void SetUpScrollHandling()
		{
			if (AllowSelection)
			{
				m_rtlLabel.ScrollBars = m_sbrScrollBars;
				m_rtlLabel.Dock = DockStyle.Fill;
			}
			else
			{
				m_rtlLabel.ScrollBars = RichTextBoxScrollBars.None;
				switch (m_sbrScrollBars)
				{
					case RichTextBoxScrollBars.Both:
					case RichTextBoxScrollBars.ForcedBoth:
					case RichTextBoxScrollBars.ForcedVertical:
					case RichTextBoxScrollBars.Vertical:
						m_rtlLabel.Dock = DockStyle.Top;
						break;
					case RichTextBoxScrollBars.ForcedHorizontal:
					case RichTextBoxScrollBars.Horizontal:
						m_rtlLabel.Dock = DockStyle.Left;
						break;
					case RichTextBoxScrollBars.None:
					default:
						m_rtlLabel.Dock = DockStyle.Fill;
						break;
				}
			}
		}
	}
}
