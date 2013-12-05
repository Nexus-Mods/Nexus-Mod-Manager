﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using Nexus.Client.Games.XRebirth.Steam;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.Games.XRebirth
{
	/// <summary>
	/// The base game mode factory that provides the commond functionality for
	/// factories that build game modes for X Rebirth based games.
	/// </summary>
	public class XRebirthGameModeFactory : IGameModeFactory
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
		public XRebirthGameModeFactory(IEnvironmentInfo p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			m_gmdGameModeDescriptor = new XRebirthGameModeDescriptor(p_eifEnvironmentInfo);
		}

		#endregion

		/// <summary>
		/// Gets the path where mod files should be installed.
		/// </summary>
		/// <returns>The path where mod files should be installed, or
		/// <c>null</c> if the path could not be determined.</returns>
		public string GetInstallationPath()
		{
			var registryKey = @"HKEY_CURRENT_USER\Software\Valve\Steam\Apps\2870";
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
						var appPath = Path.Combine(steamPath, @"steamapps\common\X Rebirth");

						// check if game is installed in the default directory
						if (!Directory.Exists(appPath))
						{
							Trace.TraceInformation(
								"XRebirth is not installed in standard directory. Checking steam config.vdf...");

							// second try, check steam config.vdf
							// if any of this fails, no problem... just drop through the catch
							var steamConfig = Path.Combine(Path.Combine(steamPath, "config"), "config.vdf");
							var kv = KeyValue.LoadAsText(steamConfig);
							var node =
								kv.Children[0].Children[0].Children[0].Children.Single(x => x.Name == "apps")
									.Children.Single(x => x.Name == "2870");
							if (node != null)
							{
								appPath = node.Children.Single(x => x.Name == "installdir").Value;
								if (Directory.Exists(appPath) && File.Exists(Path.Combine(appPath, "XRebirth.exe")))
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

			Trace.TraceInformation("Found {0}", strValue);
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
			return Path.Combine(p_strGameInstallPath, "extensions");
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

			XRebirthGameMode gmdGameMode = InstantiateGameMode(p_futFileUtility);
			p_imsWarning = null;

			return gmdGameMode;
		}

		/// <summary>
		/// Instantiates the game mode.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <returns>The game mode for which this is a factory.</returns>
		protected XRebirthGameMode InstantiateGameMode(FileUtil p_futFileUtility)
		{
			return new XRebirthGameMode(EnvironmentInfo, p_futFileUtility);
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

			XRebirthSetupVM vmlSetup = new XRebirthSetupVM(EnvironmentInfo, GameModeDescriptor);
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
