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

namespace Nexus.Client.Games.Witcher2
{
	/// <summary>
	/// The base game mode factory that provides the commond functionality for
	/// factories that build game modes for The Witcher2 based games.
	/// </summary>
	public class Witcher2GameModeFactory : IGameModeFactory
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
		public Witcher2GameModeFactory(IEnvironmentInfo p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			m_gmdGameModeDescriptor = new Witcher2GameModeDescriptor(p_eifEnvironmentInfo);
		}

		#endregion

		/// <summary>
		/// Gets the path where mod files should be installed.
		/// </summary>
		/// <returns>The path where mod files should be installed, or
		/// <c>null</c> if the path could not be determined.</returns>
		public string GetInstallationPath()
		{

			var registryKey = @"HKEY_CURRENT_USER\Software\Valve\Steam\Apps\20920";
			Trace.TraceInformation(@"Checking for steam install: {0}\Installed", registryKey);
			Trace.Indent();

			string strValue = null;
			try
			{
				var steamKey = Registry.GetValue(registryKey, "Installed", 0);
				if (steamKey != null)
				{
					var isSteamInstall = steamKey.ToString() == "1";
					if (isSteamInstall)
					{
						Trace.TraceInformation("Getting Steam install folder.");

						var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null).ToString();

						// convert path to windows path. (steam uses C:/x/y we want C:\\x\\y
						steamPath = Path.GetFullPath(steamPath);
						var appPath = Path.Combine(steamPath, @"steamapps\common\the witcher 2\bin");

						// check if game is installed in the default directory
						if (!Directory.Exists(appPath))
						{
							Trace.TraceInformation(
								"Witcher 2 is not installed in standard directory. Checking steam config.vdf...");

							// second try, check steam config.vdf
							// if any of this fails, no problem... just drop through the catch
							var steamConfig = Path.Combine(Path.Combine(steamPath, "config"), "config.vdf");
							var kv = KeyValue.LoadAsText(steamConfig);
							var node =
								kv.Children[0].Children[0].Children[0].Children.Single(x => x.Name == "apps")
									.Children.Single(x => x.Name == "20920");
							if (node != null)
							{
								appPath = node.Children.Single(x => x.Name == "installdir").Value;
								if (Directory.Exists(appPath) && File.Exists(Path.Combine(appPath, "witcher2.exe")))
									strValue = appPath;
							}
						}
						else
							strValue = appPath;
					}
				}
			}
			catch
			{
				//if we can't read the registry or config.vdf, just return null
			}

			try
			{
				if (string.IsNullOrWhiteSpace(strValue))
				{
					Trace.TraceInformation("Getting install folder from Uninstall.");

					var uniPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 20920", "InstallLocation", null).ToString();

					if (Directory.Exists(uniPath))
						strValue = uniPath;
				}
			}
			catch
			{
			}

			Trace.TraceInformation("Found {0}", strValue);
			Trace.Unindent();

			if (strValue == null)
			{
				string strRegistryKey = null;
				if (EnvironmentInfo.Is64BitProcess)
					strRegistryKey = @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\CD Projekt RED\The Witcher 2";
				else
					strRegistryKey = @"HKEY_LOCAL_MACHINE\Software\CD Projekt RED\The Witcher 2";
				Trace.TraceInformation(@"Checking: {0}\InstallFolder", strRegistryKey);
				Trace.Indent();

				try
				{
					strValue = Registry.GetValue(String.Format(strRegistryKey, GameModeDescriptor.ModeId), "InstallFolder", null) as string;
				}
				catch
				{
					//if we can't read the registry, just return null
				}
				if (!String.IsNullOrEmpty(strValue))
					strValue = Path.Combine(strValue, "bin");
				Trace.TraceInformation(String.Format("Found {0}", strValue));
				Trace.Unindent();
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
			string strPath = Path.Combine(Path.GetDirectoryName(p_strGameInstallPath), "CookedPC");
			if (!Directory.Exists(strPath))
				Directory.CreateDirectory(strPath);
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

			Witcher2GameMode gmdGameMode = InstantiateGameMode(p_futFileUtility);
			p_imsWarning = null;

			return gmdGameMode;
		}

		/// <summary>
		/// Instantiates the game mode.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <returns>The game mode for which this is a factory.</returns>
		protected Witcher2GameMode InstantiateGameMode(FileUtil p_futFileUtility)
		{
			return new Witcher2GameMode(EnvironmentInfo, p_futFileUtility);
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

			Witcher2SetupVM vmlSetup = new Witcher2SetupVM(EnvironmentInfo, GameModeDescriptor);
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
