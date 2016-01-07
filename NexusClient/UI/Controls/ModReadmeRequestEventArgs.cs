using Nexus.Client.Mods;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// Provides data for the <see cref='CategoryListView.ModInfoRequested'/> event.
	/// </summary>
	public class ModReadmeRequestEventArgs : ModEventArgs
	{
		/// <summary>
		/// Initializes a new instance of the <see cref='ModReadmeRequestEventArgs'/> class.
		/// </summary>
		/// <param name="p_modMod">The mod which triggered the event.</param>
		/// <param name="p_strReadmeFileName">The readme file name.</param>
		public ModReadmeRequestEventArgs(IMod p_modMod, string p_strReadmeFileName)
			: base(p_modMod)
		{
			ReadmeFileName = p_strReadmeFileName;
		}


		/// <summary>
		/// The mod's readme file name.
		/// </summary>
		public string ReadmeFileName { get; private set; }
	}
}