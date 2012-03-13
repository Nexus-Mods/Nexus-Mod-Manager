
namespace Nexus.Client
{
	/// <summary>
	/// An item that is displayed in a <see cref="GameModeListView"/>.
	/// </summary>
	public interface IGameModeListViewItem
	{
		#region Properties

		/// <summary>
		/// Gets or sets whether the item is selected in the list view.
		/// </summary>
		/// <value>Whether the item is selected in the list view.</value>
		bool Selected { get; set; }

		#endregion
	}
}
