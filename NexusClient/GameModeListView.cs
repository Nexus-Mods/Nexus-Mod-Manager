using System.Windows.Forms;

namespace Nexus.Client
{
	/// <summary>
	/// A list view of game modes.
	/// </summary>
	public class GameModeListView : FlowLayoutPanel
	{
		private bool m_booSettingSelected = false;

		#region Properties

		/// <summary>
		/// Gets or sets the selected item in the list.
		/// </summary>
		/// <value>The selected item in the list.</value>
		public IGameModeListViewItem SelectedItem
		{
			get
			{
				foreach (IGameModeListViewItem lviItem in Controls)
					if (lviItem.Selected)
						return lviItem;
				return null;
			}
			set
			{
				if (m_booSettingSelected)
					return;
				m_booSettingSelected = true;
				foreach (IGameModeListViewItem lviItem in Controls)
					lviItem.Selected = (value == lviItem);
				m_booSettingSelected = false;
			}
		}

		#endregion
	}
}
