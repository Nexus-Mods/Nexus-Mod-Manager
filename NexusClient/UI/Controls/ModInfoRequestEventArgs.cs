using System.Collections.Generic;
using Nexus.Client.Mods;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// Provides data for the <see cref='CategoryListView.ModInfoRequested'/> event.
	/// </summary>
	public class ModInfoRequestEventArgs : ModEventArgs
	{
		/// <summary>
		/// Initializes a new instance of the <see cref='ModInfoRequestEventArgs'/> class.
		/// </summary>
		/// <param name="p_modMod">The mod which triggered the event.</param>
		public ModInfoRequestEventArgs(IMod p_modMod)
			: base(p_modMod)
		{
			ReadmeFiles = new List<string>();
		}


		/// <summary>
		/// The mod's readme files.
		/// </summary>
		public List<string> ReadmeFiles { get; private set; }
	}
}