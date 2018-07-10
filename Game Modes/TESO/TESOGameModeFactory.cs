using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using Nexus.Client.Util;
using System.Xml;

namespace Nexus.Client.Games.TESO
{
	/// <summary>
	/// The base game mode factory that provides the commond functionality for
	/// factories that build game modes for TESO based games.
	/// </summary>
	public class TESOGameModeFactory : IGameModeFactory
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
		public TESOGameModeFactory(IEnvironmentInfo p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			m_gmdGameModeDescriptor = new TESOGameModeDescriptor(p_eifEnvironmentInfo);
		}

		#endregion

		/// <summary>
		/// Gets the path where mod files should be installed.
		/// </summary>
		/// <returns>The path where mod files should be installed, or
		/// <c>null</c> if the path could not be determined.</returns>
		public string GetInstallationPath()
		{
			string strValue = null;

			string strRegistryKey = null;
			if (EnvironmentInfo.Is64BitProcess)
				strRegistryKey = @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\Zenimax_Online\Launcher";
			else
				strRegistryKey = @"HKEY_LOCAL_MACHINE\Software\Zenimax_Online\Launcher";
			Trace.TraceInformation(@"Checking: {0}\InstallFolder", strRegistryKey);
			Trace.Indent();

			try
			{
				strValue = Registry.GetValue(String.Format(strRegistryKey, GameModeDescriptor.ModeId), "InstallPath", null) as string;
			}
			catch
			{
				//if we can't read the registry, just return null
			}
			if (!String.IsNullOrEmpty(strValue))
			{
				strValue = Path.GetDirectoryName(strValue);
				if (!String.IsNullOrEmpty(strValue))
				{
					string strPathNA = Path.Combine(strValue, @"The Elder Scrolls Online\game\client\eso.exe");
					string strPathEU = Path.Combine(strValue, @"The Elder Scrolls Online EU\game\client\eso.exe");

					if (File.Exists(strPathNA))
						strValue = Path.GetDirectoryName(strPathNA);
					if (File.Exists(strPathEU))
						strValue = Path.GetDirectoryName(strPathEU);
				}
			}
			Trace.TraceInformation(String.Format("Found {0}", strValue));
			Trace.Unindent();

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
			string strPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			string strLive = "live";
			if (!String.IsNullOrEmpty(GameModeDescriptor.ExecutablePath))
				if (Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(GameModeDescriptor.ExecutablePath))).Equals("The Elder Scrolls Online EU"))
					strLive = "liveeu";
			strPath = Path.Combine(strPath, String.Format(@"Elder Scrolls Online\{0}\Addons", strLive));
			return strPath;
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

			TESOGameMode gmdGameMode = InstantiateGameMode(p_futFileUtility);
			p_imsWarning = null;

			return gmdGameMode;
		}

		/// <summary>
		/// Instantiates the game mode.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <returns>The game mode for which this is a factory.</returns>
		protected TESOGameMode InstantiateGameMode(FileUtil p_futFileUtility)
		{
			return new TESOGameMode(EnvironmentInfo, p_futFileUtility);
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

			TESOSetupVM vmlSetup = new TESOSetupVM(EnvironmentInfo, GameModeDescriptor);
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
	}
}
