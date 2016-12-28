using System;
using System.Collections.Generic;
using System.Drawing;

namespace Nexus.Client.Games.BreakingWheel
{
	/// <summary>
	/// Provides common information aboutBreakingWheel based games.
	/// </summary>
	public class BreakingWheelGameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { @"Main Game\Ellie_Ball_Project.exe" };
		private static readonly List<string> STOP_FOLDERS = new List<string>() { "Content" };
		private const string MODE_ID = "BreakingWheel";

		#region Properties

		/// <summary>
		/// Gets the directory whereBreakingWheel plugins are installed.
		/// </summary>
		/// <value>The directory whereBreakingWheel plugins are installed.</value>
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
				return "Breaking Wheel";
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
		/// Gets a list of possible folders that should be looked for in mod archives to determine
		/// file structure.
		/// </summary>
		/// <value>A list of possible folders that should be looked for in mod archives to determine
		/// file structure.</value>
		public override IEnumerable<string> StopFolders
		{
			get
			{
				return STOP_FOLDERS;
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
				return new Theme(Properties.Resources.BreakingWheel_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public BreakingWheelGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
