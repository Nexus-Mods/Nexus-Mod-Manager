using System;
using System.IO;
using System.Drawing;
using Nexus.Client.Games.WarThunder;

namespace Nexus.Client.Games.WarThunder
{
	/// <summary>
	/// Provides common information aboutWarThunder based games.
	/// </summary>
	public class WarThunderGameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "aces.exe" };
		private const string MODE_ID = "WarThunder";

		#region Properties

		/// <summary>
		/// Gets the directory whereWarThunder plugins are installed.
		/// </summary>
		/// <value>The directory whereWarThunder plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				return String.Empty;
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
				return "War Thunder";
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
				return new Theme(Properties.Resources.WarThunder_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public WarThunderGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
