using System;
using System.IO;
using System.Drawing;
using Nexus.Client.Games.DragonAge2;

namespace Nexus.Client.Games.DragonAge2
{
	/// <summary>
    /// Provides common information about DragonAge2 based games.
	/// </summary>
    public class DragonAge2GameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "DragonAge2.exe" };
        private const string MODE_ID = "DragonAge2";

		#region Properties

		/// <summary>
		/// Gets the directory where Dragon Age plugins are installed.
		/// </summary>
		/// <value>The directory where Dragon Age plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				return null;
			}
		}

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override string Name
		{
			get
			{
				return "Dragon Age 2";
			}
		}

		/// <summary>
		/// Gets the unique id of the game mode.
		/// </summary>
		/// <value>The unique id of the game mode.</value>
		public override string ModeId
		{
			get
			{
				return MODE_ID;
			}
		}

		/// <summary>
		/// Gets the list of possible executable files for the game.
		/// </summary>
		/// <value>The list of possible executable files for the game.</value>
		public override string[] GameExecutables
		{
			get
			{
				return EXECUTABLES;
			}
		}

		/// <summary>
		/// Gets the theme to use for this game mode.
		/// </summary>
		/// <value>The theme to use for this game mode.</value>
		public override Theme ModeTheme
		{
			get
			{
				return new Theme(Properties.Resources.dragonage_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public DragonAge2GameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
