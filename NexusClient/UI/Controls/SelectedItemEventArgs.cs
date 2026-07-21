using System;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// An event arguments class that describes a selected item.
	/// </summary>
	public class SelectedItemEventArgs : EventArgs
	{
		#region Properties

		/// <summary>
		/// Gets the selected item.
		/// </summary>
		/// <value>The selected item.</value>
		public GameModeListViewItemBase SelectedItem { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_lviSelectedItem">The selected item.</param>
		public SelectedItemEventArgs(GameModeListViewItemBase p_lviSelectedItem)
		{
			SelectedItem = p_lviSelectedItem;
		}

		#endregion
	}
}
