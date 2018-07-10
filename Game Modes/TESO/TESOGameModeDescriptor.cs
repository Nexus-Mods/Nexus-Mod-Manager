using System;
using System.IO;
using System.Drawing;
using Nexus.Client.Games.TESO;

namespace Nexus.Client.Games.TESO
{
	/// <summary>
	/// Provides common information about TESO based games.
	/// </summary>
	public class TESOGameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "eso.exe" };
		private const string MODE_ID = "TESO";

		#region Properties

		/// <summary>
		/// Gets the directory where TESO plugins are installed.
		/// </summary>
		/// <value>The directory where TESO plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				string strPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				string strLive = "live";
				if (!String.IsNullOrEmpty(ExecutablePath))
					if (Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(ExecutablePath))).Equals("The Elder Scrolls Online EU"))
						strLive = "liveeu";
				strPath = Path.Combine(strPath, String.Format(@"Elder Scrolls Online\{0}\Addons", strLive));
				if (!Directory.Exists(strPath))
					Directory.CreateDirectory(strPath);
				return strPath;
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
				return "The Elder Scrolls Online";
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
				return new Theme(Properties.Resources.eso_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public TESOGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
