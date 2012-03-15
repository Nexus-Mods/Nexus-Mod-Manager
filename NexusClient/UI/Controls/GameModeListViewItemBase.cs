using Nexus.Client.Games;
using System;
using System.Windows.Forms;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// the base class for items displayed in a <see cref="GameModeListView"/>.
	/// </summary>
	public abstract class GameModeListViewItemBase : UserControl
	{
		#region Properties

		/// <summary>
		/// Gets or sets whether the item is selected in the list view.
		/// </summary>
		/// <value>Whether the item is selected in the list view.</value>
		public bool Selected
		{
			get
			{
				return ListView.SelectedItem == this;
			}
			set
			{
				if (!value)
				{
					if (ListView.SelectedItem == this)
						ListView.SelectedItem = null;
				}
				else
					ListView.SelectedItem = this;
			}
		}

		/// <summary>
		/// Gets the descriptor for the game mode being displayed by the item.
		/// </summary>
		/// <value>The descriptor for the game mode being displayed by the item.</value>
		public IGameModeDescriptor GameMode { get; private set; }

		/// <summary>
		/// Gets the list view that the item belongs to.
		/// </summary>
		/// <value>The list view that the item belongs to.</value>
		public GameModeListView ListView
		{
			get
			{
				Control ctlParent = Parent;
				while (!(ctlParent is GameModeListView) && (ctlParent != null))
					ctlParent = ctlParent.Parent;
				return (GameModeListView)ctlParent;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmdGameModeInfo">The descriptor for the game mode to display in the item.</param>
		public GameModeListViewItemBase(IGameModeDescriptor p_gmdGameModeInfo)
		{
			GameMode = p_gmdGameModeInfo;
		}

		#endregion

		/// <summary>
		/// Uses the mode id to represent the game mode.
		/// </summary>
		/// <returns>The mode id of the game mode being represented.</returns>
		public override string ToString()
		{
			return GameMode.ModeId;
		}
	}
}
