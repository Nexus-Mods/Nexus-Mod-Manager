using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Nexus.Client.Games;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// A list view of game modes.
	/// </summary>
	public class GameModeListView : UserControl
	{
		#region Events

		/// <summary>
		/// Raised when the selected item has changed.
		/// </summary>
		[Category("Behavior")]
		[Browsable(true)]
		public event EventHandler<SelectedItemEventArgs> SelectedItemChanged;

		#endregion

		private FlowLayoutPanel m_flpPanel = new FlowLayoutPanel();
		private GameModeListViewItemBase m_lviSelectedItem = null;
		private GameModeListViewItemBase m_lviFocussedItem = null;

		#region Properties

		/// <summary>
		/// Gets the list of items in the view.
		/// </summary>
		/// <value>The list of items in the view.</value>
		public IList<GameModeListViewItemBase> Items
		{
			get
			{
				List<GameModeListViewItemBase> lstItems = new List<GameModeListViewItemBase>();
				for (Int32 i = 0; i < m_flpPanel.Controls.Count; i++)
					if (m_flpPanel.Controls[i] is GameModeListViewItemBase)
						lstItems.Add((GameModeListViewItemBase)m_flpPanel.Controls[i]);
				return lstItems;
			}
		}

		/// <summary>
		/// Gets or sets the selected item in the list.
		/// </summary>
		/// <value>The selected item in the list.</value>
		public GameModeListViewItemBase SelectedItem
		{
			get
			{
				return m_lviSelectedItem;
			}
			set
			{
				if (m_lviSelectedItem != value)
				{
					if (m_lviSelectedItem != null)
						m_lviSelectedItem.BackColor = BackColor;
					m_lviSelectedItem = value;
					if (m_lviSelectedItem != null)
					{
						m_lviSelectedItem.BackColor = SystemColors.Highlight;
						m_lviSelectedItem.Focus();
					}
				}
			}
		}

		/// <summary>
		/// Gets or sets the game mode that is selected.
		/// </summary>
		/// <remarks>
		/// Setting this property selects the item whose game mode matches the given game mode.
		/// </remarks>
		/// <value>The selected game mode.</value>
		public IGameModeDescriptor SelectedGameMode
		{
			get
			{
				if (SelectedItem != null)
					return SelectedItem.GameMode;
				return null;
			}
			set
			{
				foreach (GameModeListViewItem lviItem in m_flpPanel.Controls)
					if (value.ModeId.Equals(lviItem.GameMode.ModeId))
						SelectedItem = lviItem;
			}
		}

		/// <summary>
		/// Gets or sets the list view item that has focus.
		/// </summary>
		/// <remarks>
		/// Setting this property doesn't give focus to a list view item. This
		/// property simply tracks the item that currently has focus.
		/// </remarks>
		/// <value>The list view item that has focus.</value>
		protected GameModeListViewItemBase FocussedItem
		{
			get
			{
				return m_lviFocussedItem;
			}
			set
			{
				m_lviFocussedItem = value;
			}
		}

		/// <summary>
		/// Gets or sets the flow direction of the items in the list.
		/// </summary>
		/// <value>The flow direction of the items in the list.</value>
		[DefaultValue(typeof(Enum), "TopDown")]
		public FlowDirection FlowDirection
		{
			get
			{
				return m_flpPanel.FlowDirection;
			}
			set
			{
				m_flpPanel.FlowDirection = value;
			}
		}

		/// <summary>
		/// Gets or sets the back colour.
		/// </summary>
		/// <value>The back colour.</value>
		[DefaultValue(typeof(SystemColors), "Window")]
		public override Color BackColor
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
		/// Gets or sets the border style.
		/// </summary>
		/// <value>The border style.</value>
		[DefaultValue(typeof(BorderStyle), "Fixed3D")]
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

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public GameModeListView()
		{
			m_flpPanel.AutoScroll = true;
			m_flpPanel.ControlAdded += new ControlEventHandler(Panel_ControlAdded);
			m_flpPanel.AutoSize = true;
			m_flpPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			m_flpPanel.Dock = DockStyle.Fill;
			Controls.Add(m_flpPanel);
			BorderStyle = BorderStyle.Fixed3D;
			BackColor = SystemColors.Window;
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="SelectedItemChanged"/> event.
		/// </summary>
		/// <param name="e">The <see cref="SelectedItemEventArgs"/> describing the event arguments.</param>
		protected void OnSelectedItemChanged(SelectedItemEventArgs e)
		{
			SelectedItemChanged(this, e);
		}

		/// <summary>
		/// Raises the <see cref="SelectedItemChanged"/> event.
		/// </summary>
		/// <param name="p_lviSelected">The newly selected list view item.</param>
		protected virtual void OnSelectedItemChanged(GameModeListViewItemBase p_lviSelected)
		{
			OnSelectedItemChanged(new SelectedItemEventArgs(p_lviSelected));
		}

		/// <summary>
		/// Raises the <see cref="Control.ControlAdded"/> event.
		/// </summary>
		/// <remarks>
		/// This wires up the required events for <see cref="GameModeListViewItemBase"/> view items,
		/// so that we can track focus, and perform required formatting.
		/// </remarks>
		/// <param name="e">The <see cref="ControlEventArgs"/> describing the event arguments.</param>
		protected override void OnControlAdded(ControlEventArgs e)
		{
			if (e.Control is GameModeListViewItemBase)
			{
				Controls.Remove(e.Control);
				m_flpPanel.Controls.Add(e.Control);
				e.Control.Click += new EventHandler(GameModeListView_Click);
				e.Control.MouseDoubleClick += new MouseEventHandler(GameModeListView_MouseDoubleClick);
				e.Control.GotFocus += new EventHandler(GameModeListView_GotFocus);
				e.Control.LostFocus += new EventHandler(GameModeListView_LostFocus);
				e.Control.PreviewKeyDown += new PreviewKeyDownEventHandler(GameModeListView_PreviewKeyDown);
				e.Control.KeyDown += new KeyEventHandler(GameModeListView_KeyDown);
			}
			else
				base.OnControlAdded(e);
		}

		#endregion

		#region List View Item Event Handling

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of list view items.
		/// </summary>
		/// <remarks>
		/// This selects the clicked item.
		/// </remarks>
		/// <param name="sender">The <see cref="GameModeListViewItemBase"/> that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> descriibng the event arguments.</param>
		private void GameModeListView_Click(object sender, EventArgs e)
		{
			SelectedItem = (GameModeListViewItemBase)sender;
		}

		/// <summary>
		/// Handles the <see cref="Control.MouseDoubleClick"/> event of list view items.
		/// </summary>
		/// <remarks>
		/// This selects the clicked item.
		/// </remarks>
		/// <param name="sender">The <see cref="GameModeListViewItemBase"/> that raised the event.</param>
		/// <param name="e">An <see cref="MouseEventArgs"/> descriibng the event arguments.</param>
		private void GameModeListView_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			SelectedItem = (GameModeListViewItemBase)sender;
			OnMouseDoubleClick(e);
		}

		/// <summary>
		/// Handles the <see cref="Control.PreviewKeyDown"/> event of list view items.
		/// </summary>
		/// <remarks>
		/// This enables the use of the Enter key to select items.
		/// </remarks>
		/// <param name="sender">The <see cref="GameModeListViewItemBase"/> that raised the event.</param>
		/// <param name="e">A <see cref="PreviewKeyDownEventArgs"/> descriibng the event arguments.</param>
		private void GameModeListView_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
		{
			switch (e.KeyData)
			{
				case Keys.Enter:
					e.IsInputKey = true;
					break;
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.KeyDown"/> event of list view items.
		/// </summary>
		/// <remarks>
		/// This enables the use of the Enter key to select items.
		/// </remarks>
		/// <param name="sender">The <see cref="GameModeListViewItemBase"/> that raised the event.</param>
		/// <param name="e">A <see cref="KeyEventArgs"/> descriibng the event arguments.</param>
		private void GameModeListView_KeyDown(object sender, KeyEventArgs e)
		{
			switch (e.KeyData)
			{
				case Keys.Enter:
					SelectedItem = FocussedItem;
					break;
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.GotFocus"/> event of list view items.
		/// </summary>
		/// <remarks>
		/// This tracks the currently focussed item, and decorates the item to look focussed.
		/// </remarks>
		/// <param name="sender">The <see cref="GameModeListViewItemBase"/> that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> descriibng the event arguments.</param>
		private void GameModeListView_GotFocus(object sender, EventArgs e)
		{
			GameModeListViewItemBase lviItem = (GameModeListViewItemBase)sender;
			FocussedItem = lviItem;
			lviItem.BorderStyle = BorderStyle.FixedSingle;
		}

		/// <summary>
		/// Handles the <see cref="Control.LostFocus"/> event of list view items.
		/// </summary>
		/// <remarks>
		/// This tracks the currently focussed item, and decorates the item to look unfocussed.
		/// </remarks>
		/// <param name="sender">The <see cref="GameModeListViewItemBase"/> that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> descriibng the event arguments.</param>
		private void GameModeListView_LostFocus(object sender, EventArgs e)
		{
			GameModeListViewItemBase lviItem = (GameModeListViewItemBase)sender;
			if (FocussedItem == lviItem)
				FocussedItem = null;
			lviItem.BorderStyle = BorderStyle.None;
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="Control.ControlAdded"/> event of flow layout panel.
		/// </summary>
		/// <remarks>
		/// This ensures only <see cref="GameModeListViewItemBase"/> controls are added, and
		/// formats the items to fit into the list.
		/// </remarks>
		/// <param name="sender">The <see cref="GameModeListViewItemBase"/> that raised the event.</param>
		/// <param name="e">A <see cref="ControlEventArgs"/> descriibng the event arguments.</param>
		private void Panel_ControlAdded(object sender, ControlEventArgs e)
		{
			if (!(e.Control is GameModeListViewItemBase))
				Controls.Remove(e.Control);
			else
			{
				e.Control.AutoSize = false;
				e.Control.Dock = DockStyle.Fill;
				e.Control.MinimumSize = e.Control.PreferredSize;
			}
		}

		/// <summary>
		/// Raises the <see cref="Control.GotFocus"/> event.
		/// </summary>
		/// <remarks>
		/// This focuses the last focussed list view item.
		/// </remarks>
		/// <param name="e">The <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnGotFocus(EventArgs e)
		{
			if (FocussedItem != null)
				FocussedItem.Focus();
			else if (m_flpPanel.Controls.Count > 0)
				m_flpPanel.Controls[0].Focus();
			base.OnGotFocus(e);
		}
	}
}
