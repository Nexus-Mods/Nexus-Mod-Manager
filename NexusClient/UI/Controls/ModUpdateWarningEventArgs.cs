using System;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// Provides data for the <see cref='CategoryListView.UpdateWarningToggled'/>,
	/// <see cref='CategoryListView.AllUpdateWarningsToggled'/> events.
	/// </summary>
	public class ModUpdateWarningEventArgs : EventArgs
	{
		/// <summary>
		/// Initializes a new instance of the <see cref='ModUpdateWarningEventArgs'/> class.
		/// </summary>
		/// <param name="p_booEnableWarning">Indicates whether the "update warning" is enabled.</param>
		public ModUpdateWarningEventArgs(bool p_booEnableWarning)
		{
			EnableWarning = p_booEnableWarning;
		}

		/// <summary>
		/// Indicates whether the "update warning" is enabled.
		/// </summary>
		public bool EnableWarning { get; private set; }
	}
}