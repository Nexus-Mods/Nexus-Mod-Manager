using System;
using System.Windows.Forms;
using Nexus.Client.Util;
using System.ComponentModel;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// A list view of game modes.
	/// </summary>
	public class GameModeListView : FlowLayoutPanel
	{
		#region Events

		/// <summary>
		/// Raised when the selected item has changed.
		/// </summary>
		[Category("Behavior")]
		[Browsable(true)]
		public event EventHandler<SelectedItemEventArgs> SelectedItemChanged;

		#endregion

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
					if (value == lviItem)
					{
						if (SelectedItem != lviItem)
						{
							lviItem.Selected = true;
							OnSelectedItemChanged(lviItem);
						}
					}
					else
						lviItem.Selected = false;
				m_booSettingSelected = false;
			}
		}

		/// <summary>
		/// Gets or sets the selected value.
		/// </summary>
		/// <remarks>
		/// Setting this property selectes the item whose values matches the given value.
		/// </remarks>
		/// <value>The selected value.</value>
		public object SelectedValue
		{
			get
			{
				foreach (IGameModeListViewItem lviItem in Controls)
					if (lviItem.Selected)
						return lviItem.Value;
				return null;
			}
			set
			{
				foreach (IGameModeListViewItem lviItem in Controls)
					if (value.Equals(lviItem.Value))
						SelectedItem = lviItem;
			}
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
		protected virtual void OnSelectedItemChanged(IGameModeListViewItem p_lviSelected)
		{
			OnSelectedItemChanged(new SelectedItemEventArgs(p_lviSelected));
		}

		#endregion
	}
}
