using System;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// Provides data for the <see cref='CategoryListView.UpdateCheckToggled'/>,
	/// </summary>
	public class ModUpdateCheckEventArgs : EventArgs
	{
		/// <summary>
		/// Initializes a new instance of the <see cref='ModUpdateCheckEventArgs'/> class.
		/// </summary>
		/// <param name="p_booEnableCheck">Indicates whether the "update check and rename" is enabled.</param>
		public ModUpdateCheckEventArgs(bool p_booEnableCheck)
		{
			EnableCheck = p_booEnableCheck;
		}

		/// <summary>
		/// Indicates whether the "update check and rename" is enabled.
		/// </summary>
		public bool EnableCheck { get; private set; }
	}
}