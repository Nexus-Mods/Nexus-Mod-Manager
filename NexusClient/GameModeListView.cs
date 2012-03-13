using System.Windows.Forms;
using System;

namespace Nexus.Client
{
	/// <summary>
	/// A list view of game modes.
	/// </summary>
	public class GameModeListView : FlowLayoutPanel
	{
		private bool m_booSettingSelected = false;

		public void Select(IGameModeListViewItem p_lviSelectedItem)
		{
			if (m_booSettingSelected)
				return;
			m_booSettingSelected = true;
			foreach (IGameModeListViewItem lviItem in Controls)
				lviItem.SetSelected(p_lviSelectedItem == lviItem);
			m_booSettingSelected = false;
		}
	}
}
