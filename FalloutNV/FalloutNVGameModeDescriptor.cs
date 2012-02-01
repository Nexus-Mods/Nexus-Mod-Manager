using System.Drawing;

namespace Nexus.Client.Games.FalloutNV
{
	/// <summary>
	/// Provides the basic information about the Fallout: New Vegas game mode.
	/// </summary>
	public class FalloutNVGameModeDescriptor : IGameModeDescriptor
	{
		private static string[] EXECUTABLES = { "falloutNV.exe", "falloutNVng.exe" };
		private const string MODE_ID = "FalloutNV";

		#region Properties

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public string Name
		{
			get
			{
				return "Fallout: New Vegas";
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
				return new Theme(Properties.Resources.fonv_logo, Color.FromArgb(250, 167, 64));
			}
		}

		#endregion
	}
}
