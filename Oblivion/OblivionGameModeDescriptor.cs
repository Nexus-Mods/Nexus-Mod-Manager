using System.Drawing;

namespace Nexus.Client.Games.Oblivion
{
	/// <summary>
	/// Provides the basic information about the Oblivion game mode.
	/// </summary>
	public class OblivionGameModeDescriptor : IGameModeDescriptor
	{
		private static string[] EXECUTABLES = { "oblivion.exe" };
		private const string MODE_ID = "Oblivion";
		
		#region Properties

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public string Name
		{
			get
			{
				return "Oblivion";
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
				return new Theme(Properties.Resources.tes_logo, Color.FromArgb(250, 167, 64));
			}
		}

		#endregion
	}
}
