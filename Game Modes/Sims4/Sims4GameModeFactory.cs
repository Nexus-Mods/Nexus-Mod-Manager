using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using Nexus.Client.Util;
using System.Xml;
using Nexus.Client.Games.Steam;
using System.Linq;

namespace Nexus.Client.Games.Sims4
{
	/// <summary>
	/// The base game mode factory that provides the commond functionality for
	/// factories that build game modes for Sims4 based games.
	/// </summary>
	public class Sims4GameModeFactory : IGameModeFactory
	{
		private readonly IGameModeDescriptor m_gmdGameModeDescriptor = null;

		#region Properties

		/// <summary>
		/// Gets the application's environement info.
		/// </summary>
		/// <value>The application's environement info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the descriptor of the game mode that this factory builds.
		/// </summary>
		/// <value>The descriptor of the game mode that this factory builds.</value>
		public IGameModeDescriptor GameModeDescriptor
		{
			get
			{
				return m_gmdGameModeDescriptor;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple consturctor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's environement info.</param>
		public Sims4GameModeFactory(IEnvironmentInfo p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			m_gmdGameModeDescriptor = new Sims4GameModeDescriptor(p_eifEnvironmentInfo);
			FixOldInstallationPath(p_eifEnvironmentInfo, m_gmdGameModeDescriptor.ModeId);
		}

		#endregion

		/// <summary>
		/// Gets the path where mod files should be installed.
		/// </summary>
		/// <returns>The path where mod files should be installed, or
		/// <c>null</c> if the path could not be determined.</returns>
		public string GetInstallationPath()
		{
            string strValue = SteamInstallationPathDetector.Instance.GetSteamInstallationPath("1222670", "The Sims 4", @"Game\Bin\TS4_x64.exe");

			try
			{
				if (string.IsNullOrWhiteSpace(strValue))
				{
					Trace.TraceInformation("Getting Origin install path.");

					string originPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Maxis\The Sims 4", "Install Dir", null)?.ToString();

					if (!string.IsNullOrWhiteSpace(originPath) && Directory.Exists(originPath))
						strValue = originPath;
				}
			}
			catch
			{
				//if we can't read the registry or config.vdf, just return null
			}

			return strValue;
		}

		/// <summary>
		/// Gets the path where mod files should be installed.
		/// </summary>
		/// <remarks>
		/// This method uses the given path to the installed game
		/// to determine the installaiton path for mods.
		/// </remarks>
		/// <returns>The path where mod files should be installed, or
		/// <c>null</c> if the path could be be determined.</returns>
		public string GetInstallationPath(string p_strGameInstallPath)
		{
			string strPath = string.Empty;

			if (EnvironmentInfo.Settings.InstallationPaths.ContainsKey(m_gmdGameModeDescriptor.ModeId))
			{
				strPath = EnvironmentInfo.Settings.InstallationPaths[m_gmdGameModeDescriptor.ModeId];
			}

			if (string.IsNullOrEmpty(strPath))
			{
				strPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				strPath = Path.Combine(strPath, @"Electronic Arts\The Sims 4");
				EnvironmentInfo.Settings.InstallationPaths[m_gmdGameModeDescriptor.ModeId] = strPath;
				EnvironmentInfo.Settings.Save();
			}

			return strPath;
			//string strPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			//strPath = Path.Combine(strPath, @"Electronic Arts\The Sims 4");
			//return strPath;
		}

		/// <summary>
		/// Gets the path to the game executable.
		/// </summary>
		/// <returns>The path to the game executable, or
		/// <c>null</c> if the path could not be determined.</returns>
		public string GetExecutablePath(string p_strPath)
		{
			return p_strPath;
		}

		/// <summary>
		/// Builds the game mode.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <param name="p_imsWarning">The resultant warning resultant from the creation of the game mode.
		/// <c>null</c> if there are no warnings.</param>
		/// <returns>The game mode.</returns>
		public IGameMode BuildGameMode(FileUtil p_futFileUtility, out ViewMessage p_imsWarning)
		{
			if (EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] == null)
				EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] = new PerGameModeSettings<object>();
			if (!EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId].ContainsKey("AskAboutReadOnlySettingsFiles"))
			{
				EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["AskAboutReadOnlySettingsFiles"] = true;
				EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["UnReadOnlySettingsFiles"] = true;
				EnvironmentInfo.Settings.Save();
			}

			Sims4GameMode gmdGameMode = InstantiateGameMode(p_futFileUtility);
			p_imsWarning = null;

			return gmdGameMode;
		}

		/// <summary>
		/// Instantiates the game mode.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <returns>The game mode for which this is a factory.</returns>
		protected Sims4GameMode InstantiateGameMode(FileUtil p_futFileUtility)
		{
			return new Sims4GameMode(EnvironmentInfo, p_futFileUtility);
		}

		/// <summary>
		/// Performs the initial setup for the game mode being created.
		/// </summary>
		/// <param name="p_dlgShowView">The delegate to use to display a view.</param>
		/// <param name="p_dlgShowMessage">The delegate to use to display a message.</param>
		/// <returns><c>true</c> if the setup completed successfully;
		/// <c>false</c> otherwise.</returns>
		public bool PerformInitialSetup(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
		{
			if (EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] == null)
				EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] = new PerGameModeSettings<object>();

			Sims4SetupVM vmlSetup = new Sims4SetupVM(EnvironmentInfo, GameModeDescriptor);
			SetupForm frmSetup = new SetupForm(vmlSetup);
			if (((DialogResult)p_dlgShowView(frmSetup, true)) == DialogResult.Cancel)
				return false;
			return vmlSetup.Save();
		}

		/// <summary>
		/// Performs the initializtion for the game mode being created.
		/// </summary>
		/// <param name="p_dlgShowView">The delegate to use to display a view.</param>
		/// <param name="p_dlgShowMessage">The delegate to use to display a message.</param>
		/// <returns><c>true</c> if the setup completed successfully;
		/// <c>false</c> otherwise.</returns>
		public bool PerformInitialization(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
		{
			return true;
		}

		/// <summary>
		/// This will fix the game's stored install path for people who were in the beta test of this game mode.
		/// </summary>
		/// <param name="environmentInfo"></param>
		/// <param name="modeId"></param>
		private void FixOldInstallationPath(IEnvironmentInfo environmentInfo, string modeId)
		{
			if (environmentInfo.Settings.InstallationPaths.ContainsKey(modeId))
			{
				string installPath = environmentInfo.Settings.InstallationPaths[modeId];
				string newPath = GetInstallationPath(string.Empty);

				if (!installPath.Equals(newPath))
				{
					environmentInfo.Settings.InstallationPaths[modeId] = newPath;
					environmentInfo.Settings.Save();
				}
			}

		}
	}
}
