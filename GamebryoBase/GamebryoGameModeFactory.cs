using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using Nexus.Client.Games.Gamebryo.PluginManagement.Sorter;
using Nexus.Client.Games.Gamebryo.PluginManagement.LoadOrder;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Gamebryo
{
	/// <summary>
	/// The base game mode factory that provides the commond functionality for
	/// factories that build game modes for Gamebryo based games.
	/// </summary>
	public abstract class GamebryoGameModeFactory : IGameModeFactory
	{
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
		public abstract IGameModeDescriptor GameModeDescriptor { get; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple consturctor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's environement info.</param>
		public GamebryoGameModeFactory(IEnvironmentInfo p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
		}

		#endregion

		/// <summary>
		/// Gets the path where mod files should be installed.
		/// </summary>
		/// <returns>The path where mod files should be installed, or
		/// <c>null</c> if the path could not be determined.</returns>
		public virtual string GetInstallationPath()
		{
			string strRegistryKey = null;
			if (EnvironmentInfo.Is64BitProcess)
				strRegistryKey = @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\Bethesda Softworks\{0}";
			else
				strRegistryKey = @"HKEY_LOCAL_MACHINE\Software\Bethesda Softworks\{0}";
			Trace.TraceInformation(@"Checking: {0}\Installed Path", String.Format(strRegistryKey, GameModeDescriptor.ModeId));
			Trace.Indent();
			string strValue = null;
			try
			{
				strValue = Registry.GetValue(String.Format(strRegistryKey, GameModeDescriptor.ModeId), "Installed Path", null) as string;
			}
			catch
			{
				//if we can't read the registry, just return null
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
			return p_strGameInstallPath;
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
			GamebryoGameModeBase gmdGameMode = null;
			if (EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] == null)
				EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] = new PerGameModeSettings<object>();
			if (!EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId].ContainsKey("AskAboutReadOnlySettingsFiles"))
			{
				EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["AskAboutReadOnlySettingsFiles"] = true;
				EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["UnReadOnlySettingsFiles"] = true;
				EnvironmentInfo.Settings.Save();
			}

			try
			{
				gmdGameMode = InstantiateGameMode(p_futFileUtility);

				if (!File.Exists(((GamebryoGameModeBase)gmdGameMode).SettingsFiles.IniPath))
					p_imsWarning = new ViewMessage(String.Format("You have no {0} INI file. Please run {0} to initialize the file before installing any mods or turning on Archive Invalidation.", gmdGameMode.Name), null, "Missing INI", MessageBoxIcon.Warning);
				else
					p_imsWarning = null;
			}
			catch (SorterException e)
			{
				gmdGameMode = null;
				p_imsWarning = new ViewMessage(String.Format(e.Message), null, "SorterException", MessageBoxIcon.Error);
			}
            catch (FileNotFoundException e)
            {
                gmdGameMode = null;
                p_imsWarning = new ViewMessage(string.Format(e.Message), null, "FileNotFoundException", MessageBoxIcon.Error);
            }

			return gmdGameMode;
		}

		/// <summary>
		/// Instantiates the game mode.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <returns>The game mode for which this is a factory.</returns>
		protected abstract GamebryoGameModeBase InstantiateGameMode(FileUtil p_futFileUtility);

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

			GamebryoSetupVM vmlSetup = new GamebryoSetupVM(EnvironmentInfo, GameModeDescriptor);
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
		public abstract bool PerformInitialization(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage);
	}
}
