using Nexus.Client.Games.Gamebryo;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Games.Steam;
using System.Linq;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;

namespace Nexus.Client.Games.Oblivion
{
	/// <summary>
	/// The game mode factory that builds <see cref="OblivionGameMode"/>s.
	/// </summary>
	public class OblivionGameModeFactory : GamebryoGameModeFactory
	{
		private readonly IGameModeDescriptor m_gmdGameModeDescriptor = null;

		#region Properties

		/// <summary>
		/// Gets the descriptor of the game mode that this factory builds.
		/// </summary>
		/// <value>The descriptor of the game mode that this factory builds.</value>
		public override IGameModeDescriptor GameModeDescriptor
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
		public OblivionGameModeFactory(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
			m_gmdGameModeDescriptor = new OblivionGameModeDescriptor(p_eifEnvironmentInfo);
		}

		#endregion

		/// <summary>
		/// Instantiates the game mode.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <returns>The game mode for which this is a factory.</returns>
		protected override GamebryoGameModeBase InstantiateGameMode(FileUtil p_futFileUtility)
		{
			return new OblivionGameMode(EnvironmentInfo, p_futFileUtility);
		}

		/// <summary>
		/// Performs the initializtion for the game mode being created.
		/// </summary>
		/// <param name="p_dlgShowView">The delegate to use to display a view.</param>
		/// <param name="p_dlgShowMessage">The delegate to use to display a message.</param>
		/// <returns><c>true</c> if the setup completed successfully;
		/// <c>false</c> otherwise.</returns>
		public override bool PerformInitialization(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
		{
			return true;
		}

		/// <summary>
		/// Gets the path where mod files should be installed.
		/// </summary>
		/// <returns>The path where mod files should be installed, or
		/// <c>null</c> if the path could not be determined.</returns>
		public override string GetInstallationPath()
		{
			var registryKey = @"HKEY_CURRENT_USER\Software\Valve\Steam\Apps\22330";
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
						var appPath = Path.Combine(steamPath, @"steamapps\common\Oblivion");

						// check if game is installed in the default directory
						if (!Directory.Exists(appPath))
						{
							Trace.TraceInformation(
								"Oblivion is not installed in standard directory. Checking steam config.vdf...");

							// second try, check steam config.vdf
							// if any of this fails, no problem... just drop through the catch
							var steamConfig = Path.Combine(Path.Combine(steamPath, "config"), "config.vdf");
							var kv = KeyValue.LoadAsText(steamConfig);
							var node =
								kv.Children[0].Children[0].Children[0].Children.Single(x => x.Name == "apps")
									.Children.Single(x => x.Name == "22330");
							if (node != null)
							{
								appPath = node.Children.Single(x => x.Name == "installdir").Value;
								if (Directory.Exists(appPath) && File.Exists(Path.Combine(appPath, "oblivion.exe")))
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

					var uniPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 22330", "InstallLocation", null).ToString();

					if (Directory.Exists(uniPath))
						strValue = uniPath;
				}
			}
			catch
			{ }

			Trace.TraceInformation("Found {0}", strValue);
			Trace.Unindent();

			if (string.IsNullOrEmpty(strValue))
				strValue = base.GetInstallationPath();

			return strValue;
		}
	}
}
