using System.Drawing;
using Nexus.Client.Plugins;

namespace Nexus.Client.Games.Gamebryo.Plugins
{
	/// <summary>
	/// Encapsulates the information about a Gamebryo based game plugin.
	/// </summary>
	public class GamebryoPlugin : Plugin
	{
		#region Properties

		/// <summary>
		/// Gets whether the plugin is a master file.
		/// </summary>
		/// <value>Whether the plugin is a master file.</value>
		public bool IsMaster { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strPath">The filename of the plugin.</param>
		/// <param name="p_strDescription">The description of the plugin.</param>
		/// <param name="p_imgPicture">The picture of the plugin.</param>
		/// <param name="p_booIsMaster">Whether the plugin is a master file.</param>
		public GamebryoPlugin(string p_strPath, string p_strDescription, Image p_imgPicture, bool p_booIsMaster)
			: base(p_strPath, p_strDescription, p_imgPicture)
		{
			IsMaster = p_booIsMaster;
		}

		#endregion
	}
}
