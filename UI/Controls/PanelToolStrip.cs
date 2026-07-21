using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Nexus.Client.Util.Collections;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// This is a tool strip control.
	/// </summary>
	/// <remarks>
	/// It is implemented as a simple panel to allow the use of any control as
	/// tool strip "buttons."
	/// </remarks>
	public class PanelToolStrip : Panel
	{
		#region Region Internal ToolStripPanel Class

		/// <summary>
		/// This is the inside panel that actually contains the toolstrip items.
		/// </summary>
		protected class ToolStripPanel : Panel
		{
			#region ItemComparer Class

			/// <summary>
			/// A comparer that orders controls base on their oreder in a given list.
			/// </summary>
			protected class ItemComparer : IComparer<Control>
			{
				private List<Control> m_lstOrderAdded = null;

				/// <summary>
				/// A simple contructor.
				/// </summary>
				/// <param name="p_lstOrderAdded">The list dictating the order of the controls.</param>
				public ItemComparer(List<Control> p_lstOrderAdded)
				{
					m_lstOrderAdded = p_lstOrderAdded;
				}

				#region IComparer<Control> Members

				/// <seealso cref="IComparer{T}.Compare"/>
				public int Compare(Control x, Control y)
				{
					if (x == null)
					{
						if (y == null)
							return 0;
						return -1;
					}
					if (y == null)
						return 1;
					return m_lstOrderAdded.IndexOf(x).CompareTo(m_lstOrderAdded.IndexOf(y));
				}

				#endregion
			}

			#endregion

			private Orientation m_otnDirection = Orientation.Vertical;
			private List<Control> m_lstOrderAdded = new List<Control>();
			private bool m_booNeedScroll = false;
			private bool m_booEnableUpScroll = false;
			private bool m_booEnableDownScroll = false;
			private Int32 m_intScrollAmount = 5;
			private FlatStyle m_fstFlatStyle = FlatStyle.Flat;
			private Int32 m_intItemBorderWidth = 0;

			#region Properties

			/// <summary>
			/// Gets or sets the scrollAmount of the ToolStripPanel.
			/// </summary>
			/// <value>The scrollAmount of the ToolStripPanel.</value>
			public Int32 ScrollAmount
			{
				get
				{
					return m_intScrollAmount;
				}
				set
				{
					m_intScrollAmount = value;
				}
			}

			/// <summary>
			/// Gets or sets the direction of the ToolStripPanel.
			/// </summary>
			/// <value>The direction of the ToolStripPanel.</value>
			public Orientation Direction
			{
				get
				{
					return m_otnDirection;
				}
				set
				{
					m_otnDirection = value;

					foreach (Control ctlButton in Controls)
					{
						if (ctlButton.Tag is PanelToolStripItem)
						{
							PanelToolStripItem tsiStripItem = (PanelToolStripItem)ctlButton.Tag;
							ctlButton.Dock = (m_otnDirection == Orientation.Horizontal) ? DockStyle.Left : DockStyle.Top;
						}
					}
					SortToolStripItems();
				}
			}


			/// <summary>
			/// Gets whether an up scroll control is needed.
			/// </summary>
			/// <value>Whether an up scroll control is needed.</value>
			public bool NeedScroll
			{
				get
				{
					return m_booNeedScroll;
				}
			}

			/// <summary>
			/// Gets whether the up scroll should be enabled.
			/// </summary>
			/// <value>Whether the up scroll should be enabled.</value>
			public bool EnableUpScroll
			{
				get
				{
					return m_booEnableUpScroll;
				}
			}

			/// <summary>
			/// Gets whether the down scroll should be enabled.
			/// </summary>
			/// <value>Whether the down scroll should be enabled.</value>
			public bool EnableDownScroll
			{
				get
				{
					return m_booEnableDownScroll;
				}
			}

			/// <summary>
			/// Gets or sets the flatStyle of the ToolStripItems.
			/// </summary>
			/// <value>The flatStyle of the ToolStripItems.</value>
			public FlatStyle ButtonFlatStyle
			{
				get
				{
					return m_fstFlatStyle;
				}
				set
				{
					m_fstFlatStyle = value;
				}
			}

			/// <summary>
			/// Gets or sets the BorderWidth of the ToolStripItems.
			/// </summary>
			/// <value>The BorderWidth of the ToolStripItems.</value>
			public Int32 ButtonBorderWidth
			{
				get
				{
					return m_intItemBorderWidth;
				}
				set
				{
					m_intItemBorderWidth = value;
				}
			}

			#endregion

			#region Constructors

			/// <summary>
			/// The default constructor.
			/// </summary>
			public ToolStripPanel()
			{
				this.AutoScroll = true;
			}

			#endregion

			/// <summary>
			/// Sorts the tool strip items.
			/// </summary>
			protected void SortToolStripItems()
			{
				SortedList<Int32, SortedList<Control>> sltItems = new SortedList<Int32, SortedList<Control>>();

				Int32 intIndex = -1;
				Control ctlControl = null;
				ItemComparer icpComparer = new ItemComparer(m_lstOrderAdded);
				for (Int32 i = Controls.Count - 1; i >= 0; i--)
				{
					ctlControl = Controls[i];

					intIndex = ((PanelToolStripItem)ctlControl.Tag).Index;
					if (!sltItems.ContainsKey(intIndex))
						sltItems[intIndex] = new SortedList<Control>(icpComparer);
					sltItems[intIndex].Add(ctlControl);
				}

				SortedList<Control> lstButtons = null;
				//the lower the index, the higher up/further to the left
				// so index 0 is at the top/left
				// (top or left depending on orientation)
				intIndex = 0;
				Control ctlButton = null;
				for (Int32 i = sltItems.Values.Count - 1; i >= 0; i--)
				//for (Int32 i = 0; i < sltItems.Values.Count; i++)
				{
					lstButtons = sltItems.Values[i];
					for (Int32 j = lstButtons.Count - 1; j >= 0; j--)
					{
						ctlButton = lstButtons[j];
						this.Controls.SetChildIndex(ctlButton, intIndex++);

						if ((i == sltItems.Values.Count - 1) && (j == lstButtons.Count - 1))
						{
							if (m_otnDirection == Orientation.Vertical)
							{
								m_booNeedScroll = (m_booNeedScroll || (ctlButton.Bounds.Bottom > this.Height));
								m_booEnableDownScroll = m_booNeedScroll;
							}
							else
							{
								m_booNeedScroll = (m_booNeedScroll || (ctlButton.Bounds.Right > this.Width));
								m_booEnableUpScroll = m_booNeedScroll;
							}
						}
					}
				}
			}

			#region Control Addition/Removal

			/// <summary>
			/// UI.Controls the addition of controls to the panel.
			/// </summary>
			/// <remarks>
			/// This makes sure the added toolstrip items are sized, positioned, and ordered correctly.
			/// </remarks>
			/// <param name="e">A <see cref="ControlEventArgs"/> describing the event arguments.</param>
			protected override void OnControlAdded(ControlEventArgs e)
			{
				Control ctlButton = e.Control;

				if (ctlButton.Tag is PanelToolStripItem)
				{
					PanelToolStripItem tsiStripItem = (PanelToolStripItem)ctlButton.Tag;
					m_lstOrderAdded.Add(ctlButton);
					((PanelToolStripItem)ctlButton.Tag).IndexChanged += new EventHandler(ToolStripPanel_IndexChanged);

					ctlButton.Dock = (m_otnDirection == Orientation.Horizontal) ? DockStyle.Left : DockStyle.Top;
					tsiStripItem.SetUnselected();

					SortToolStripItems();
				}
				base.OnControlAdded(e);
			}

			/// <summary>
			/// Raises the <see cref="Control.ControlRemoved"/> event.
			/// </summary>
			/// <remarks>
			/// This unwires <see cref="PanelToolStripItem"/>s as they are removed.
			/// </remarks>
			/// <param name="e">A <see cref="ControlEventArgs"/> describing the event arguments.</param>
			protected override void OnControlRemoved(ControlEventArgs e)
			{
				Control ctlButton = e.Control;
				if (ctlButton.Tag is PanelToolStripItem)
				{
					((PanelToolStripItem)ctlButton.Tag).IndexChanged -= new EventHandler(ToolStripPanel_IndexChanged);
				}
				base.OnControlRemoved(e);
			}

			void ToolStripPanel_IndexChanged(object sender, EventArgs e)
			{
				SortToolStripItems();
			}

			/// <summary>
			/// Adds a <see cref="PanelToolStripItem"/> to the panel.
			/// </summary>
			/// <param name="p_pdiItem">The <see cref="PanelToolStripItem"/> to add.</param>
			public void addToolStripItem(PanelToolStripItem p_pdiItem)
			{
				p_pdiItem.Selected += new EventHandler<EventArgs>(psiButton_Selected);
				Controls.Add(p_pdiItem.Button);
			}

			/// <summary>
			/// Removes a <see cref="PanelToolStripItem"/> to the panel.
			/// </summary>
			/// <param name="p_pdiItem">The <see cref="PanelToolStripItem"/> to remove.</param>
			public void removeToolStripItem(PanelToolStripItem p_pdiItem)
			{
				p_pdiItem.Selected -= new EventHandler<EventArgs>(psiButton_Selected);
				Controls.Remove(p_pdiItem.Button);
			}

			#endregion

			#region Scrolling

			/// <summary>
			/// Scrolls the toolstrip items up by one unit of <see cref="ScrollAmount"/> pixels.
			/// </summary>
			public void scrollUp()
			{
				Int32 intNewX = this.DisplayRectangle.X;
				Int32 intNewY = this.DisplayRectangle.Y;
				if (m_otnDirection == Orientation.Horizontal)
				{
					intNewX -= m_intScrollAmount;
					if (this.DisplayRectangle.Right < this.Width)
						intNewX += this.Width - this.DisplayRectangle.Right + 1;
				}
				else
				{
					intNewY += m_intScrollAmount;
					if (intNewY > 0)
						intNewY = 0;
				}
				this.SetDisplayRectLocation(intNewX, intNewY);

				m_booEnableDownScroll = true;
				checkScrollUp();
			}

			/// <summary>
			/// Scrolls the toolstrip items down by one unit of <see cref="ScrollAmount"/> pixels.
			/// </summary>
			public void scrollDown()
			{
				Int32 intNewX = this.DisplayRectangle.X;
				Int32 intNewY = this.DisplayRectangle.Y;
				if (m_otnDirection == Orientation.Horizontal)
				{
					intNewX += m_intScrollAmount;
					if (intNewX > 0)
						intNewX = 0;
				}
				else
				{
					intNewY -= m_intScrollAmount;
					if (this.DisplayRectangle.Bottom < this.Height)
						intNewY += this.Height - this.DisplayRectangle.Bottom + 1;
				}
				this.SetDisplayRectLocation(intNewX, intNewY);

				m_booEnableUpScroll = true;
				checkScrollDown();
			}

			/// <summary>
			/// This checks to see if the scroll up button needs to be enabled.
			/// </summary>
			protected void checkScrollUp()
			{
				Control ctlButton = null;
				if (m_otnDirection == Orientation.Vertical)
				{
					ctlButton = this.Controls[this.Controls.Count - 1];
					m_booEnableUpScroll = (ctlButton.Bounds.Top < 0);
				}
				else
				{
					ctlButton = this.Controls[0];
					m_booEnableUpScroll = (ctlButton.Bounds.Right > this.Width);
				}
			}

			/// <summary>
			/// This checks to see if the scroll down button needs to be enabled.
			/// </summary>
			protected void checkScrollDown()
			{
				Control ctlButton = null;
				if (m_otnDirection == Orientation.Vertical)
				{
					ctlButton = this.Controls[0];
					m_booEnableDownScroll = (ctlButton.Bounds.Bottom > this.Height);
				}
				else
				{
					ctlButton = this.Controls[this.Controls.Count - 1];
					m_booEnableDownScroll = (ctlButton.Bounds.Left < 0);
				}
			}

			/// <summary>
			/// This checks to see if we need scroll buttons, and, if so,
			/// which buttons need to be enabled.
			/// </summary>
			public void checkScroll()
			{
				if (DesignMode)
				{
					m_booNeedScroll = true;
					return;
				}

				if (this.Controls.Count == 0)
					return;

				Control ctlButton = null;
				if (m_otnDirection == Orientation.Vertical)
				{
					ctlButton = this.Controls[0];
					m_booNeedScroll = (ctlButton.Bounds.Bottom > this.Height);

					ctlButton = this.Controls[this.Controls.Count - 1];
					m_booNeedScroll = (m_booNeedScroll || (ctlButton.Bounds.Top < 0));
				}
				else
				{
					ctlButton = this.Controls[0];
					m_booNeedScroll = (ctlButton.Bounds.Right > this.Width);

					ctlButton = this.Controls[this.Controls.Count - 1];
					m_booNeedScroll = (m_booNeedScroll || (ctlButton.Bounds.Left < 0));
				}

				if (m_booNeedScroll)
				{
					checkScrollUp();
					checkScrollDown();
				}
			}

			#endregion

			/// <summary>
			/// Handles the colour change of the toolstrip items when selected/unselected.
			/// </summary>
			/// <param name="sender">The object that triggered the event.</param>
			/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
			void psiButton_Selected(object sender, EventArgs e)
			{
				Control ctlButton = ((PanelToolStripItem)sender).Button;
				Control ctlOther = null;
				for (Int32 i = Controls.Count - 1; i >= 0; i--)
				{
					ctlOther = Controls[i];
					if ((ctlOther != ctlButton) && (ctlOther.Tag is PanelToolStripItem))
						((PanelToolStripItem)ctlOther.Tag).SetUnselected();
				}
			}
		}

		#endregion

		private ToolStripPanel m_pnlToolStrip = new ToolStripPanel();
		private Timer m_tmrScrollTimer = new Timer();
		private Int32 m_intScrollTimerInterval = 25;
		private Button m_butDown = null;
		private Button m_butUp = null;
		private Int32 m_intMinScrollButtonWidth = 20;

		#region Properties

		/// <summary>
		/// Gets or sets how many pixels the tool strip scrolls per tick.
		/// </summary>
		/// <value>How many pixels the tool strip scrolls per tick.</value>
		[Category("Behavior")]
		[DefaultValue(5)]
		public Int32 ScrollAmount
		{
			get
			{
				return m_pnlToolStrip.ScrollAmount;
			}
			set
			{
				Int32 intValue = (value == 0) ? 1 : value;
				m_pnlToolStrip.ScrollAmount = intValue;
			}
		}

		/// <summary>
		/// Gets or sets the timerScrollAmountRatio of the PanelToolStrip.
		/// </summary>
		/// <remarks>
		/// The suggested value for this property is 5 times the ScrollAmount.
		/// </remarks>
		/// <value>The timerScrollAmountRatio of the PanelToolStrip.</value>
		[Category("Behavior")]
		[DefaultValue(25)]
		public Int32 ScrollInterval
		{
			get
			{
				return m_intScrollTimerInterval;
			}
			set
			{
				m_intScrollTimerInterval = value;
				m_tmrScrollTimer.Interval = m_intScrollTimerInterval;
			}
		}


		/// <summary>
		/// Gets or sets the direction the tool strip is oriented.
		/// </summary>
		/// <value>
		/// The direction the tool strip is oriented.
		/// </value>
		[Category("Appearance")]
		[DefaultValue(Orientation.Vertical)]
		public Orientation Direction
		{
			get
			{
				return m_pnlToolStrip.Direction;
			}
			set
			{
				m_pnlToolStrip.Direction = value;
				m_butDown.Dock = (m_pnlToolStrip.Direction == Orientation.Horizontal) ? DockStyle.Left : DockStyle.Bottom;
				m_butUp.Dock = (m_pnlToolStrip.Direction == Orientation.Horizontal) ? DockStyle.Right : DockStyle.Top;
				m_butDown.MinimumSize = (m_pnlToolStrip.Direction == Orientation.Horizontal) ? new Size(m_intMinScrollButtonWidth, 0) : new Size(0, m_intMinScrollButtonWidth);
				m_butDown.MinimumSize = (m_pnlToolStrip.Direction == Orientation.Horizontal) ? new Size(m_intMinScrollButtonWidth, 0) : new Size(0, m_intMinScrollButtonWidth);
			}
		}

		/// <summary>
		/// Gest or sets the minimum width or height of the scroll buttons.
		/// </summary>
		/// <value>
		/// The minimum width or height of the scroll buttons.
		/// </value>
		[Category("Appearance")]
		[DefaultValue(20)]
		public Int32 MinimumScrollButtonWidth
		{
			get
			{
				return m_intMinScrollButtonWidth;
			}
			set
			{
				m_intMinScrollButtonWidth = value;
			}
		}

		/// <summary>
		/// Gets or sets the background colour of the conrol.
		/// </summary>
		/// <value>The background colour of the conrol.</value>
		public override Color BackColor
		{
			get
			{
				return m_pnlToolStrip.BackColor;
			}
			set
			{
				m_pnlToolStrip.BackColor = value;
			}
		}

		/// <summary>
		/// Gets or sets the image displayed on the scroll down button.
		/// </summary>
		/// <value>The image displayed on the scroll down button.</value>
		[Category("Appearance")]
		public Image ScrollDownImage
		{
			get
			{
				return m_butDown.Image;
			}
			set
			{
				m_butDown.Image = value;
			}
		}

		/// <summary>
		/// Gets or sets the scroll down button image alignment.
		/// </summary>
		/// <value>The scroll down button image alignment.</value>
		[Category("Appearance")]
		[DefaultValue(ContentAlignment.MiddleCenter)]
		public ContentAlignment ScrollDownImageAlign
		{
			get
			{
				return m_butDown.ImageAlign;
			}
			set
			{
				m_butDown.ImageAlign = value;
			}
		}

		/// <summary>
		/// Gets or sets the scroll down button text.
		/// </summary>
		/// <value>The scroll down button text.</value>
		[Category("Appearance")]
		[DefaultValue("")]
		public string ScrollDownText
		{
			get
			{
				return m_butDown.Text;
			}
			set
			{
				m_butDown.Text = value;
			}
		}

		/// <summary>
		/// Gets or sets the scroll down button text alignment.
		/// </summary>
		/// <value>The scroll down button text alignment.</value>
		[Category("Appearance")]
		[DefaultValue(ContentAlignment.MiddleCenter)]
		public ContentAlignment ScrollDownTextAlign
		{
			get
			{
				return m_butDown.TextAlign;
			}
			set
			{
				m_butDown.TextAlign = value;
			}
		}

		/// <summary>
		/// Gets or sets the position of the text and image relative to each other on the scroll bown button.
		/// </summary>
		/// <value>The position of the text and image relative to each other on the scroll bown button.</value>
		[Category("Appearance")]
		[DefaultValue(TextImageRelation.ImageAboveText)]
		public TextImageRelation ScrollDownTextImageRelation
		{
			get
			{
				return m_butDown.TextImageRelation;
			}
			set
			{
				m_butDown.TextImageRelation = value;
			}
		}

		/// <summary>
		/// Gets or sets the image displayed on the scroll Up button.
		/// </summary>
		/// <value>The image displayed on the scroll Up button.</value>
		[Category("Appearance")]
		public Image ScrollUpImage
		{
			get
			{
				return m_butUp.Image;
			}
			set
			{
				m_butUp.Image = value;
			}
		}

		/// <summary>
		/// Gets or sets the scroll Up button image alignment.
		/// </summary>
		/// <value>The scroll Up button image alignment.</value>
		[Category("Appearance")]
		[DefaultValue(ContentAlignment.MiddleCenter)]
		public ContentAlignment ScrollUpImageAlign
		{
			get
			{
				return m_butUp.ImageAlign;
			}
			set
			{
				m_butUp.ImageAlign = value;
			}
		}

		/// <summary>
		/// Gets or sets the scroll Up button text.
		/// </summary>
		/// <value>The scroll Up button text.</value>
		[Category("Appearance")]
		[DefaultValue("")]
		public string ScrollUpText
		{
			get
			{
				return m_butUp.Text;
			}
			set
			{
				m_butUp.Text = value;
			}
		}

		/// <summary>
		/// Gets or sets the scroll Up button text alignment.
		/// </summary>
		/// <value>The scroll Up button text alignment.</value>
		[Category("Appearance")]
		[DefaultValue(ContentAlignment.MiddleCenter)]
		public ContentAlignment ScrollUpTextAlign
		{
			get
			{
				return m_butUp.TextAlign;
			}
			set
			{
				m_butUp.TextAlign = value;
			}
		}

		/// <summary>
		/// Gets or sets the position of the text and image relative to each other on the scroll bown button.
		/// </summary>
		/// <value>The position of the text and image relative to each other on the scroll bown button.</value>
		[Category("Appearance")]
		[DefaultValue(TextImageRelation.ImageAboveText)]
		public TextImageRelation ScrollUpTextImageRelation
		{
			get
			{
				return m_butUp.TextImageRelation;
			}
			set
			{
				m_butUp.TextImageRelation = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public PanelToolStrip()
		{
			this.BackColor = m_pnlToolStrip.BackColor;
			this.Controls.Add(m_pnlToolStrip);
			m_pnlToolStrip.ControlAdded += new ControlEventHandler(m_pnlToolStrip_ControlAdded);
			m_pnlToolStrip.Width = this.Width;
			m_pnlToolStrip.Height = this.Height;

			m_butDown = new Button();
			m_butDown.Text = "";
			m_butDown.Image = Properties.Resources.arrow_down_black_small;
			m_butDown.TextImageRelation = TextImageRelation.ImageAboveText;
			m_butDown.FlatStyle = m_pnlToolStrip.ButtonFlatStyle;
			m_butDown.AutoSize = true;
			m_butDown.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			m_butDown.MinimumSize = (m_pnlToolStrip.Direction == Orientation.Horizontal) ? new Size(m_intMinScrollButtonWidth, 0) : new Size(0, m_intMinScrollButtonWidth);
			m_butDown.MouseEnter += new EventHandler(scrollStart);
			m_butDown.MouseLeave += new EventHandler(scrollStop);
			Controls.Add(m_butDown);

			m_butUp = new Button();
			m_butUp.Text = "";
			m_butUp.Image = Properties.Resources.arrow_up_black_small;
			m_butUp.TextImageRelation = TextImageRelation.ImageAboveText;
			m_butUp.FlatStyle = m_pnlToolStrip.ButtonFlatStyle;
			m_butUp.AutoSize = true;
			m_butUp.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			m_butUp.MinimumSize = (m_pnlToolStrip.Direction == Orientation.Horizontal) ? new Size(m_intMinScrollButtonWidth, 0) : new Size(0, m_intMinScrollButtonWidth);
			m_butUp.MouseEnter += new EventHandler(scrollStart);
			m_butUp.MouseLeave += new EventHandler(scrollStop);
			Controls.Add(m_butUp);

			m_butDown.Dock = (m_pnlToolStrip.Direction == Orientation.Horizontal) ? DockStyle.Left : DockStyle.Bottom;
			m_butUp.Dock = (m_pnlToolStrip.Direction == Orientation.Horizontal) ? DockStyle.Right : DockStyle.Top;

			m_tmrScrollTimer.Interval = m_intScrollTimerInterval;
			m_tmrScrollTimer.Tick += new EventHandler(m_tmrScrollTimer_Tick);
		}

		#endregion

		#region Control Addition/Removal

		/// <summary>
		/// Adds a <see cref="PanelToolStripItem"/>.
		/// </summary>
		/// <param name="p_pdiItem">The <see cref="PanelToolStripItem"/> to add.</param>
		public void addToolStripItem(PanelToolStripItem p_pdiItem)
		{
			if (m_pnlToolStrip.Direction == Orientation.Horizontal)
				m_pnlToolStrip.Height = this.Height + SystemInformation.HorizontalScrollBarHeight;
			else
				m_pnlToolStrip.Width = this.Width + SystemInformation.VerticalScrollBarWidth;
			m_pnlToolStrip.addToolStripItem(p_pdiItem);
		}

		/// <summary>
		/// Adds a <see cref="Control"/> to the toolstrip.
		/// </summary>
		/// <remarks>
		/// This method creates a <see cref="PanelToolStripItem"/> base on the given
		/// values and adds it to the toolstrip.
		/// </remarks>
		/// <param name="p_ctlButton">The <see cref="Control"/> to add.</param>
		/// <param name="p_strEventName">The name of the event on the control to which to bind the <see cref="PanelToolStripItem.Selected"/> event.</param>
		/// <param name="p_intIndex">The index at which to insert the added item.</param>
		/// <param name="p_tdsDisplayStyle">The <see cref="ToolStripItemDisplayStyle"/> indicating how text and
		/// images are displayed on the added item.</param>
		public void addToolStripItem(Control p_ctlButton, string p_strEventName, Int32 p_intIndex, ToolStripItemDisplayStyle p_tdsDisplayStyle)
		{
			addToolStripItem(new PanelToolStripItem(p_ctlButton, p_strEventName, p_intIndex, p_tdsDisplayStyle));
		}

		/// <summary>
		/// Removes a <see cref="PanelToolStripItem"/>.
		/// </summary>
		/// <param name="p_pdiItem">The <see cref="PanelToolStripItem"/> to remove.</param>
		public void removeToolStripItem(PanelToolStripItem p_pdiItem)
		{
			m_pnlToolStrip.removeToolStripItem(p_pdiItem);
		}

		/// <summary>
		/// This ensures that all the scrolling controls are properly positioned whenever a new control is added.
		/// </summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">A <see cref="ControlEventArgs"/> describing the event arguments.</param>
		private void m_pnlToolStrip_ControlAdded(object sender, ControlEventArgs e)
		{
			checkScroll();
		}

		#endregion

		#region ToolStripPanel Positioning

		/// <summary>
		/// This positions the contained <see cref="ToolStripPanel"/> so that is isn't hidden
		/// by the scroll controls, and so its scrollbar is not visible.
		/// </summary>
		protected void positionToolStripPanel()
		{
			this.SuspendLayout();

			Int32 intButtonSpace = 0;
			if (m_pnlToolStrip.Direction == Orientation.Horizontal)
			{
				if (m_butDown.Visible)
				{
					intButtonSpace += m_butDown.Width;
					m_pnlToolStrip.Left = m_butDown.Width;
				}
				else
					m_pnlToolStrip.Left = 0;

				if (m_butUp.Visible)
					intButtonSpace += m_butUp.Width;

				m_pnlToolStrip.Height = (m_pnlToolStrip.NeedScroll) ? this.Height + SystemInformation.HorizontalScrollBarHeight : this.Height;
				m_pnlToolStrip.Width = this.Width - intButtonSpace;
				m_pnlToolStrip.Top = 0;
			}
			else
			{
				if (m_butUp.Visible)
				{
					intButtonSpace += m_butUp.Height;
					m_pnlToolStrip.Top = m_butUp.Height;
				}
				else
					m_pnlToolStrip.Top = 0;

				if (m_butDown.Visible)
					intButtonSpace += m_butDown.Height;

				m_pnlToolStrip.Width = (m_pnlToolStrip.NeedScroll) ? this.Width + SystemInformation.VerticalScrollBarWidth : this.Width;
				m_pnlToolStrip.Height = this.Height - intButtonSpace;
				m_pnlToolStrip.Left = 0;
			}

			this.ResumeLayout();
		}

		#endregion

		#region Scrolling

		/// <summary>
		/// Raises the resize event.
		/// </summary>
		/// <remarks>
		/// This redraws the contain toolstrip panel to conform to our new dimensions.
		/// </remarks>
		/// <param name="eventargs">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnResize(EventArgs eventargs)
		{
			base.OnResize(eventargs);

			this.SuspendLayout();

			if (m_pnlToolStrip.Direction == Orientation.Horizontal)
			{
				m_pnlToolStrip.Height = this.Height + SystemInformation.HorizontalScrollBarHeight;
				m_pnlToolStrip.Width = this.Width;
			}
			else
			{
				m_pnlToolStrip.Width = this.Width + SystemInformation.VerticalScrollBarWidth;
				m_pnlToolStrip.Height = this.Height;
			}

			m_pnlToolStrip.checkScroll();
			checkScroll();

			this.ResumeLayout();
		}

		/// <summary>
		/// This checks to see if the scroll contrls should be visible.
		/// </summary>
		/// <remarks>
		/// This makes the scroll controls visible or not as required, and ensures that
		/// the contained <see cref="ToolStripPanel"/> is correctly positioned.
		/// </remarks>
		protected void checkScroll()
		{
			m_butDown.Visible = m_pnlToolStrip.NeedScroll;
			m_butDown.Enabled = m_pnlToolStrip.EnableDownScroll;
			m_butUp.Visible = m_pnlToolStrip.NeedScroll;
			m_butUp.Enabled = m_pnlToolStrip.EnableUpScroll;

			positionToolStripPanel();
		}

		/// <summary>
		/// This starts the scrolling whenever the mouse is over a scroll control.
		/// </summary>
		/// <param name="sender">The scroll control that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected void scrollStart(object sender, EventArgs e)
		{
			m_tmrScrollTimer.Tag = sender;
			m_tmrScrollTimer.Start();
		}

		/// <summary>
		/// This stops the scrolling whenever the mouse leaves a scroll control.
		/// </summary>
		/// <param name="sender">The scroll control that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected void scrollStop(object sender, EventArgs e)
		{
			m_tmrScrollTimer.Stop();
		}

		/// <summary>
		/// This scrolls the toolstrip while the mouse is over a scroll control.
		/// </summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void m_tmrScrollTimer_Tick(object sender, EventArgs e)
		{
			if (m_tmrScrollTimer.Tag == m_butUp)
				m_pnlToolStrip.scrollUp();
			else
				m_pnlToolStrip.scrollDown();
			checkScroll();
		}

		#endregion
	}
}
