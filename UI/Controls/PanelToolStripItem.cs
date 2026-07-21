using System;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// The object that can be added to <see cref="PanelToolStrip"/>s.
	/// </summary>
	/// <remarks>
	/// This class is a wrapper for the actual controls that get added to the <see cref="PanelToolStrip"/>.
	/// The purpose of this wrapper is to provide a single event that gets raised in a consistent manner
	/// to which commands can be attached. This class also serves to track various metadata about the
	/// controls.
	/// </remarks>
	public class PanelToolStripItem
	{
		/// <summary>
		/// Raised when the index of the item changes.
		/// </summary>
		public event EventHandler IndexChanged;

		private Control m_ctlButton = null;
		private Int32 m_intIndex = 0;
		private ToolTip m_ttpToolTip = new ToolTip();
		private ToolStripItemDisplayStyle m_tdsDisplayStyle = ToolStripItemDisplayStyle.Image;

		#region Properties

		/// <summary>
		/// Gets or sets the Display style of the PanelToolStripItem.
		/// </summary>
		/// <value>The Display style of the PanelToolStripItem.</value>
		public ToolStripItemDisplayStyle Display
		{
			get
			{
				return m_tdsDisplayStyle;
			}
			set
			{
				if (m_tdsDisplayStyle != value)
				{
					m_tdsDisplayStyle = value;
					//setDisplayStyle();
				}
			}
		}

		/// <summary>
		/// Gets or sets the index of the PanelToolStripItem.
		/// </summary>
		/// <value>The index of the PanelToolStripItem.</value>
		public Int32 Index
		{
			get
			{
				return m_intIndex;
			}
			set
			{
				if (value != m_intIndex)
				{
					m_intIndex = value;
					if (IndexChanged != null)
						IndexChanged(this, new EventArgs());
				}
			}
		}

		/// <summary>
		/// Gets or sets the Enabled of the button.
		/// </summary>
		/// <value>The Enabled of the button.</value>
		public bool Enabled
		{
			get
			{
				return m_ctlButton.Enabled;
			}
			set
			{
				m_ctlButton.Enabled = value;
			}
		}

		/// <summary>
		/// Gets or sets the Visible of the button.
		/// </summary>
		/// <value>The Visible of the button.</value>
		public bool Visible
		{
			get
			{
				return m_ctlButton.Visible;
			}
			set
			{
				m_ctlButton.Visible = value;
			}
		}

		/// <summary>
		/// Gets the actual control that is to be added to the <see cref="PanelToolStrip"/>.
		/// </summary>
		/// <value>The actual control that is to be added to the <see cref="PanelToolStrip"/>.</value>
		public Control Button
		{
			get
			{
				return m_ctlButton;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor.
		/// </summary>
		/// <remarks>
		/// This constructor wires the appropriate event <paramref name="p_ctlButton"/> to the
		/// <see cref="Selected"/> event.
		/// </remarks>
		/// <param name="p_ctlButton">The actual control that is to be added to the <see cref="PanelToolStrip"/>.</param>
		/// <param name="p_strEvent">The name of the event on the button control to which to bind the <see cref="Selected"/> event.</param>
		/// <param name="p_intIndex">The index of this item in the panel.</param>
		/// <param name="p_tdsDisplayStyle">The <see cref="ToolStripItemDisplayStyle"/> indicating how text and
		/// images are displayed on this item.</param>
		public PanelToolStripItem(Control p_ctlButton, string p_strEvent, Int32 p_intIndex, ToolStripItemDisplayStyle p_tdsDisplayStyle)
		{
			m_ctlButton = p_ctlButton;
			m_tdsDisplayStyle = p_tdsDisplayStyle;

			Type tpeButtonType = m_ctlButton.GetType();

			m_ctlButton.Tag = this;
			m_intIndex = p_intIndex;
			m_ttpToolTip.SetToolTip(m_ctlButton, m_ctlButton.Text);

			tpeButtonType.GetEvent(p_strEvent).AddEventHandler(p_ctlButton, Delegate.CreateDelegate(typeof(EventHandler), this, "OnSelected"));
		}

		#endregion

		/// <summary>
		/// The homogeneous event to which commands can be attached to the <see cref="PanelToolStrip"/>'s buttons.
		/// </summary>
		public event EventHandler<EventArgs> Selected;

		/// <summary>
		/// Raises the <see cref="Selected"/> event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> desribing the event arguments.</param>
		protected virtual void OnSelected(object sender, EventArgs e)
		{
			if (Selected != null)
				Selected(this, e);
		}

		/// <summary>
		/// Sets the item as not selected.
		/// </summary>
		public virtual void SetUnselected()
		{
		}

		/// <summary>
		/// Sets the item as selected.
		/// </summary>
		public virtual void SetSelected()
		{
			OnSelected(this, new EventArgs());
		}
	}
}
