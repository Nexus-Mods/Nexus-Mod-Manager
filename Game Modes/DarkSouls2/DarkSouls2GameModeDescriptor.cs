using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Text;
using Nexus.Client.Games.DarkSouls2;

namespace Nexus.Client.Games.DarkSouls2
{
	/// <summary>
	/// Provides common information about DarkSouls2 based games.
	/// </summary>
	public class DarkSouls2GameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "DarkSoulsII.exe" };
		private static string[] REQUIRED_TOOL_FILES = { @"GeDoSaTo\GeDoSaToTool.exe", @"GeDoSaTo\GeDoSaTo.dll" };
		private const string MODE_ID = "DarkSouls2";
		private const string REQUIRED_TOOL = "GeDoSaTo";

		#region Properties

		/// <summary>
		/// Gets the directory where DarkSouls2 plugins are installed.
		/// </summary>
		/// <value>The directory where DarkSouls2 plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				return String.Empty;
			}
		}

		/// <summary>
		/// Gets the path to which mod files should be installed.
		/// </summary>
		/// <value>The path to which mod files should be installed.</value>
		public override string InstallationPath
		{
			get
			{
				if (EnvironmentInfo.Settings.ToolFolder.ContainsKey(MODE_ID))
					if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.ToolFolder[MODE_ID]))
						return Path.Combine(EnvironmentInfo.Settings.ToolFolder[MODE_ID], @"textures\DarkSoulsII");
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
				return "Dark Souls 2";
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
		/// Gets the name of the required tool (if any) for the current game mode.
		/// </summary>
		/// <value>The name of the required tool (if any) for the current game mode.</value>
		public override string RequiredToolName 
		{
			get
			{
				return REQUIRED_TOOL;
			}
		}

		/// <summary>
		/// Gets the list of required tools file names, ordered by load order.
		/// </summary>
		/// <value>The list of required tools file names, ordered by load order.</value>
		public override string[] OrderedRequiredToolFileNames
		{
			get
			{
				if (!String.IsNullOrEmpty(ExecutablePath))
					for (int i = 0; i < REQUIRED_TOOL_FILES.Length; i++)
						REQUIRED_TOOL_FILES[i] = Path.Combine(ExecutablePath, REQUIRED_TOOL_FILES[i]);
				return REQUIRED_TOOL_FILES;
			}
		}

		/// <summary>
		/// Gets the error message specific to a missing required tool.
		/// </summary>
		/// <value>The error message specific to a missing required tool.</value>
		public override string RequiredToolErrorMessage
		{
			get
			{
				StringBuilder stbPromptMessage = new StringBuilder();
				stbPromptMessage.AppendFormat("You need to set the {0} tool folder to be able to install and use Dark Souls 2 mods.", REQUIRED_TOOL).AppendLine();
				stbPromptMessage.AppendLine("You can download the latest version of this tool at:");
				stbPromptMessage.AppendLine("http://blog.metaclassofnil.com/");
				stbPromptMessage.AppendLine("Install the program, you can install it anywhere, by default NMM will look for it in:");
				stbPromptMessage.AppendLine(@"\Steam\steamapps\common\Dark Souls II\Game\GeDoSaTo");
				stbPromptMessage.AppendLine("After installing it open NMM's Settings menu (gears icon), go to the Dark Souls 2 tab,");
				stbPromptMessage.AppendFormat("input the correct folder for {0} in the proper field and press OK.", REQUIRED_TOOL).AppendLine();
				stbPromptMessage.AppendLine("Don't forget to set enableTextureOverride to true in the GeDoSaTo.ini file!");
				return stbPromptMessage.ToString();
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
				return new Theme(Properties.Resources.DarkSouls2_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public DarkSouls2GameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
