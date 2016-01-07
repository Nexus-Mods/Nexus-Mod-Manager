using System;
using Nexus.Client.Mods;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// Provides data for the <see cref='CategoryListView.ModDeleteRequested'/> event or servers as a base class for other mod management events.
	/// </summary>
	public class ModEventArgs : EventArgs
	{
		/// <summary>
		/// Initializes a new instance of the <see cref='ModInfoRequestEventArgs'/> class.
		/// </summary>
		/// <param name="p_modMod">The mod which triggered the event.</param>
		public ModEventArgs(IMod p_modMod)
		{
			Mod = p_modMod;
		}


		/// <summary>
		/// Gets the mod which triggered the event.
		/// </summary>
		public IMod Mod { get; private set; }
	}
}