using System.Drawing;

namespace Nexus.Client.Games.Fallout3
{
	/// <summary>
	/// Provides the basic information about the Fallout 3 game mode.
	/// </summary>
	public class Fallout3GameModeDescriptor : IGameModeDescriptor
	{
		private static string[] EXECUTABLES = { "fallout3.exe", "fallout3ng.exe" };
		private const string MODE_ID = "Fallout3";

		#region Properties

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public string Name
		{
			get
			{
				return "Fallout 3";
			}
		}

		/// <summary>
		/// Gets the unique id of the game mode.
		/// </summary>
		/// <value>The unique id of the game mode.</value>
		public string ModeId
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
		public string[] GameExecutables
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
		public Theme ModeTheme
		{
			get
			{
				return new Theme(Properties.Resources.fo3_logo, Color.FromArgb(161, 180, 75));
			}
		}

		#endregion
	}
}
