using System.Windows.Forms;
using Nexus.Client.Games;
using Nexus.UI.Controls;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// The base class for items displayed in a <see cref="GameModeListView"/>.
	/// </summary>
	public class GameModeListViewItemBase : ManagedFontUserControl
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
				if (ListView == null)
					return false;
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
		/// The default constructor.
		/// </summary>
		/// <remarks>
		/// This constructor is not meant to be used, but it is required so the IDE designer
		/// can create controls that are derived from this class, and thus allow visual editing.
		/// </remarks>
		private GameModeListViewItemBase()
		{
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmdGameModeInfo">The descriptor for the game mode to display in the item.</param>
		public GameModeListViewItemBase(IGameModeDescriptor p_gmdGameModeInfo)
			: this()
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
