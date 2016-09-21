using Nexus.Client.Mods;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// Provides data for the <see cref='CategoryListView.ModInfoRequested'/> event.
	/// </summary>
	public class ModActionEventArgs : ModEventArgs
	{
		/// <summary>
		/// Initializes a new instance of the <see cref='ModActionEventArgs'/> class.
		/// </summary>
		/// <param name="p_modMod">The mod which triggered the event.</param>
		public ModActionEventArgs(IMod p_modMod, ModAction p_action)
			: base(p_modMod)
		{
			Action = p_action;
		}


		/// <summary>
		/// The mod's readme files.
		/// </summary>
		public ModAction Action { get; private set; }
	}

	/// <summary>
	/// Defines a list of actions can be performed on a mod.
	/// </summary>
	public enum ModAction
	{
		/// <summary>
		///  No action.
		/// </summary>
		None = 0,
		/// <summary>
		/// Mod requires activation.
		/// </summary>
		Activate,
		/// <summary>
		/// Mod requires deactivation.
		/// </summary>
		Deactivate,
		/// <summary>
		/// Mod requires deactivation and uninstallation from the current profile 
		/// and its files are to be removed from the Virtual Install folder.
		/// </summary>
		Uninstall,
		/// <summary>
		/// Mod requires reinstall.
		/// </summary>
		Reinstall,
		/// <summary>
		/// Mod requires deactivation and uninstallation from all profiles
		/// and its files are to be removed from the Virtual Install folder and 
		/// the mod's file entries are to be removed from the configuration files.
		/// </summary>
		UninstallAll,
		/// <summary>
		/// Mod requires full deactivation and uninstallation and all its files
		/// (including the archive) are to be deleted from the hard drive.
		/// </summary>
		Delete
    }
}