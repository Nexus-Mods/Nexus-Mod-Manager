namespace Nexus.Client
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;
    using System.Xml.Serialization;

    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.Games;
    using Nexus.Client.ModRepositories;
    using Nexus.Client.Properties;
    using Nexus.Client.Settings;
    using Nexus.Client.Util;
    using Nexus.UI;
    using Nexus.UI.Controls;
    using Nexus.Client.UI;

    using SevenZip;

    /// <summary>
    /// This class is responsible for creating all the services the application needs, and making sure that the
    /// environment is sane.
    /// </summary>
    public class Bootstrapper
	{
		private readonly EnvironmentInfo _environmentInfo;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="environmentInfo">The application's environment info.</param>
		public Bootstrapper(EnvironmentInfo environmentInfo)
		{
			_environmentInfo = environmentInfo;
		}

        #endregion

	    /// <summary>
	    /// Helper method to allow overrides of the game mode for NMM links.
	    /// </summary>
	    /// <param name="gameModeFromUri">Game mode specified by NMM URI.</param>
	    /// <returns>The appropriate game mode.</returns>
	    private string DetermineRequestedGameMode(string gameModeFromUri)
	    {
	        // Hack to allow Skyrim VR to use NMM links from the website.
	        // If the default mode is Skyrim VR and a Skyrim SE link is opened, we rewrite the requested game mode.
	        if ((gameModeFromUri.Equals("skyrimse", StringComparison.OrdinalIgnoreCase) || gameModeFromUri.Equals("skyrimspecialedition", StringComparison.OrdinalIgnoreCase)) 
                && _environmentInfo.Settings.RememberedGameMode.Equals("skyrimvr", StringComparison.OrdinalIgnoreCase))
	        {
	            return "SkyrimVR";
	        }
	        
            // Hack to allow Fallout 4 VR to use NMM links from the website.
	        // If the default mode is Fallout 4 VR and a Fallout 4 link is opened, we rewrite the requested game mode.
            if (gameModeFromUri.Equals("fallout4", StringComparison.OrdinalIgnoreCase) && _environmentInfo.Settings.RememberedGameMode.Equals("fallout4vr", StringComparison.OrdinalIgnoreCase))
	        {
	            return "Fallout4VR";
	        }

			// Work-around for new game ID's from Nexus.
            if (gameModeFromUri.Equals("skyrimspecialedition", StringComparison.OrdinalIgnoreCase))
			{
				if (!string.IsNullOrEmpty(_environmentInfo.Settings.SkyrimSEDownloadOverride) && _environmentInfo.Settings.SkyrimSEDownloadOverride.Equals("SkyrimGOG", StringComparison.OrdinalIgnoreCase))
					return "SkyrimGOG";
				else
					return "SkyrimSE";
			}
			else if (gameModeFromUri.Equals("newvegas", StringComparison.OrdinalIgnoreCase))
			{
				return "FalloutNV";
			}
			else if (gameModeFromUri.Equals("elderscrollsonline", StringComparison.OrdinalIgnoreCase))
			{
				return "TESO";
			}

			return gameModeFromUri;
	    }

        /// <summary>
        /// Runs the applications
        /// </summary>
        /// <remarks>
        /// This method makes sure the environment is sane. If so, it creates the required services
        /// and launches the main form.
        /// </remarks>
        /// <param name="args">The command line arguments passed to the application.</param>
        /// <returns><c>true</c> if the application started as expected;
        /// <c>false</c> otherwise.</returns>
        public bool RunMainForm(string[] args)
		{
			if (!SandboxCheck(_environmentInfo))
            {
                return false;
            }

            SetCompressorPath(_environmentInfo);

			var deletedDll = CheckModScriptDLL();

			string requestedGameMode = null;
            
			Uri modToAdd = null;

		    if (args.Length > 0 && !args[0].StartsWith("-"))
			{
				if (Uri.TryCreate(args[0], UriKind.Absolute, out modToAdd) && modToAdd.Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase))
                {
                    requestedGameMode = DetermineRequestedGameMode(modToAdd.Host);
                }
            }
			else
            {
                for (var i = 0; i < args.Length; i++)
				{
					var strArg = args[i];

				    if (strArg == "-game")
				    {
				        requestedGameMode = args[i + 1];
				        Trace.Write("Game Specified On Command line: " + requestedGameMode + ") ");
				    }
                }
            }

            var changeDefaultGameMode = false;
		    var supportedGames = GetSupportedGameModes();

		    do
			{
				var fontSetResolver = SetUpFonts();

				var installedGames = GetInstalledGameModes(supportedGames);

				if (installedGames == null)
				{
					Trace.TraceInformation("No installed games.");
					MessageBox.Show($"No games were detected! {CommonData.ModManagerName} will now close.", "No Games", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}

			    CheckIfDefaultGameModeIsInstalled(installedGames);

				var selector = new GameModeSelector(supportedGames, installedGames, _environmentInfo);
				var gameModeFactory = selector.SelectGameMode(requestedGameMode, changeDefaultGameMode);

			    if (selector.RescanRequested)
				{
					_environmentInfo.Settings.InstalledGamesDetected = false;
					_environmentInfo.Settings.Save();
					changeDefaultGameMode = true;
					continue;
				}

			    if (gameModeFactory == null)
                {
                    return false;
                }

                Trace.TraceInformation($"Game Mode Factory Selected: {gameModeFactory.GameModeDescriptor.Name} ({gameModeFactory.GameModeDescriptor.ModeId})");

				Mutex gameModeMutex = null;
				var ownsMutex = false;

			    try
				{
					for (var attemptCount = 0; attemptCount < 3; attemptCount++)
					{
						Trace.TraceInformation($"Creating Game Mode mutex (Attempt: {attemptCount})");
						gameModeMutex = new Mutex(true, $"{CommonData.ModManagerName}-{gameModeFactory.GameModeDescriptor.ModeId}-GameModeMutex", out ownsMutex);

						//If the mutex is owned, you are the first instance of the mod manager for game mode, so break out of loop.
						if (ownsMutex)
                        {
                            break;
                        }

                        try
						{
							//If the mutex isn't owned, attempt to talk across the messager.
							using (var messager = MessagerClient.GetMessager(_environmentInfo, gameModeFactory.GameModeDescriptor))
							{
								if (messager != null)
								{
									//Messenger was created OK, send download request, or bring to front.
									if (modToAdd != null)
									{
										Trace.TraceInformation($"Messaging to add: {modToAdd}");
										messager.AddMod(modToAdd.ToString());
									}
									else
									{
										Trace.TraceInformation("Messaging to bring to front.");
										messager.BringToFront();
									}

									return true;
								}
							}

							gameModeMutex.Close();
							gameModeMutex = null;
						}
						catch (InvalidOperationException)
						{
							var stbPromptMessage = new StringBuilder();
							stbPromptMessage.AppendLine($"{CommonData.ModManagerName} was unable to start. It appears another instance of {CommonData.ModManagerName} is already running.");
							stbPromptMessage.AppendLine($"If you were trying to download multiple files, wait for {CommonData.ModManagerName} to start before clicking on a new file download.");
							MessageBox.Show(stbPromptMessage.ToString(), "Already running", MessageBoxButtons.OK, MessageBoxIcon.Information);
							return false;
						}

						//Messenger couldn't be created, so sleep for a few seconds to give time for opening
						// the running copy of the mod manager to start up/shut down
						System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5.0d));
					}

					if (!ownsMutex)
					{
						var htlListener = (HeaderlessTextWriterTraceListener)Trace.Listeners["DefaultListener"];
						htlListener.ChangeFilePath(Path.Combine(Path.GetDirectoryName(htlListener.FilePath), "Messager" + Path.GetFileName(htlListener.FilePath)));

					    Trace.TraceInformation("THIS IS A MESSAGER TRACE LOG.");

					    if (!htlListener.TraceIsForced)
                        {
                            htlListener.SaveToFile();
                        }

                        var stbPromptMessage = new StringBuilder();
						stbPromptMessage.AppendLine($"{CommonData.ModManagerName} was unable to start. It appears another instance of {CommonData.ModManagerName} is already running.");
						stbPromptMessage.AppendLine("A Trace Log file was created at:");
						stbPromptMessage.AppendLine(htlListener.FilePath);
						stbPromptMessage.AppendLine("Before reporting the issue, don't close this window and check for a fix here (you can close it afterwards):");
						stbPromptMessage.AppendLine(Links.FAQs);
						stbPromptMessage.AppendLine("If you can't find a solution, please make a bug report and attach the TraceLog file here:");
						stbPromptMessage.AppendLine(Links.Instance.Issues);
						MessageBox.Show(stbPromptMessage.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Information);

					    return false;
					}

					ApplicationInitializer appInitializer = null;

				    while ((appInitializer == null) || (appInitializer.Status == TaskStatus.Retrying))
					{
						appInitializer = new ApplicationInitializer(_environmentInfo, fontSetResolver, deletedDll);
						var appInitializerForm = new ApplicationInitializationForm(appInitializer);
						appInitializer.Initialize(gameModeFactory, SynchronizationContext.Current);
						appInitializerForm.ShowDialog();
					}

					if (appInitializer.Status != TaskStatus.Complete)
					{
						if (appInitializer.Status == TaskStatus.Error)
                        {
                            return false;
                        }

                        changeDefaultGameMode = true;
						DisposeServices(appInitializer.Services);

					    continue;
					}

					var gameMode = appInitializer.GameMode;
					var services = appInitializer.Services;

					var mainFormViewModel = new MainFormVM(_environmentInfo, installedGames, gameMode, services.ModRepository, services.DownloadMonitor, services.ModActivationMonitor, services.ModManager, services.PluginManager);
					var mainForm = new MainForm(mainFormViewModel);

					using (var msgMessager = MessagerServer.InitializeListener(_environmentInfo, gameMode, services.ModManager, mainForm))
					{
						if (modToAdd != null)
						{
							Trace.TraceInformation("Adding mod: " + modToAdd);
							msgMessager.AddMod(modToAdd.ToString());
							modToAdd = null;
						}

						Trace.TraceInformation("Running Application.");

					    try
						{
							Application.Run(mainForm);
							services.ModInstallLog.Backup();
							requestedGameMode = mainFormViewModel.RequestedGameMode;
							changeDefaultGameMode = mainFormViewModel.DefaultGameModeChangeRequested;
						}
						finally
						{
							DisposeServices(services);
							gameMode.Dispose();
                        }
					}
				}
				finally
				{
					if (gameModeMutex != null)
					{
						if (ownsMutex)
                        {
                            gameModeMutex.ReleaseMutex();
                        }

                        gameModeMutex.Close();
					}

				    FileUtil.ForceDelete(_environmentInfo.TemporaryPath);

					//Clean up created font's.
					FontManager.Dispose();
				}
			} while (!string.IsNullOrEmpty(requestedGameMode) || changeDefaultGameMode);

		    return true;
		}

		#region Pre Game Mode Selection

		/// <summary>
		/// Checks to see if a sandbox is interfering with dynamic code generation.
		/// </summary>
		/// <param name="environmentInfo">The application's envrionment info.</param>
		/// <returns><c>true</c> if the check passed;
		/// <c>false</c> otherwise.</returns>
		protected bool SandboxCheck(EnvironmentInfo environmentInfo)
		{
			try
			{
				new XmlSerializer(typeof(WindowPositions));
			}
			catch (InvalidOperationException)
			{

				const string message = "{0} has detected that it is running in a sandbox. {1}" +
				                       "The sandbox is preventing {0} from performing {1}" +
				                       "important operations. Please run {0} again, {1}" +
				                       "without the sandbox.";
				const string details = "This error commonly occurs on computers running Comodo Antivirus.<br/>" +
				                       "If you are running Comodo or any antivirus, please add {0} and its folders to the exception list.<br/><br/>";
				ExtendedMessageBox.Show(null, string.Format(message, CommonData.ModManagerName, Environment.NewLine), "Sandbox Detected", string.Format(details, CommonData.ModManagerName), MessageBoxButtons.OK, MessageBoxIcon.Warning);

			    return false;
			}
			catch (System.Runtime.InteropServices.ExternalException)
			{
			    const string message = "{0} has detected that it is running in a sandbox. {1}" +
			                           "The sandbox is preventing {0} from performing {1}" +
			                           "important operations. Please run {0} again, {1}" +
			                           "without the sandbox.";
                const string details = "This error commonly occurs on computers running Zone Alarm.<br/>" +
                                       "If you are running Zone Alarm or any similar security suite, please add {0} and its folders to the exception list.<br/><br/>";
				ExtendedMessageBox.Show(null, string.Format(message, CommonData.ModManagerName, Environment.NewLine), "Sandbox Detected", string.Format(details, CommonData.ModManagerName), MessageBoxButtons.OK, MessageBoxIcon.Warning);

			    return false;
			}

			return true;
		}

		/// <summary>
		/// Sets the path to the external compression library.
		/// </summary>
		/// <param name="environmentInfo">The application's envrionment info.</param>
		protected void SetCompressorPath(EnvironmentInfo environmentInfo)
		{
			var sevenZipPath = Path.Combine(environmentInfo.ProgrammeInfoDirectory, environmentInfo.Is64BitProcess ? "7z-64bit.dll" : "7z-32bit.dll");
			SevenZipCompressor.SetLibraryPath(sevenZipPath);
		}

        /// <summary>
        /// Checks if the default game mode is installed, and clears the setting if that is not the case.
        /// </summary>
        /// <param name="installedGames">GameModeRegistry of installed games.</param>
	    private void CheckIfDefaultGameModeIsInstalled(GameModeRegistry installedGames)
	    {
	        var found = false;
            
	        foreach (var availableModes in installedGames.RegisteredGameModes)
	        {
	            if (availableModes.ModeId.Equals(_environmentInfo.Settings.RememberedGameMode, StringComparison.OrdinalIgnoreCase))
	            {
	                found = true;
	            }
	        }

	        if (!found)
	        {
	            Trace.TraceWarning($"Remembered game mode \"{_environmentInfo.Settings.RememberedGameMode}\" was not found in installed games list, clearing setting.");
	            _environmentInfo.Settings.RememberGameMode = false;
	        }
        }

		#endregion

		#region Font Management

		/// <summary>
		/// Sets up the fonts.
		/// </summary>
		/// <returns>The <see cref="NexusFontSetResolver"/> to be used.</returns>
		private NexusFontSetResolver SetUpFonts()
		{
			FontManager.Add("LinBiolinum", Resources.LinBiolinum_RB);
			FontManager.Add("LinBiolinum", Resources.LinBiolinum_RI);

			var fstDefault = new FontSet(new [] { "Microsoft Sans Serif", "Arial" });
			var fsgDefault = new FontSetGroup(fstDefault);
			fsgDefault.AddFontSet("StandardText", fstDefault);
			fsgDefault.AddFontSet("HeadingText", fstDefault);
			fsgDefault.AddFontSet("SmallText", new FontSet(new [] { "Segoe UI", "Arial" }));
			fsgDefault.AddFontSet("MenuText", new FontSet(new [] { "Segoe UI", "Arial" }));
			fsgDefault.AddFontSet("GameSearchText", new FontSet(new [] { "LinBiolinum" }));
			fsgDefault.AddFontSet("TestText", new FontSet(new [] { "Wingdings" }));

			var fsrResolver = new NexusFontSetResolver();
			fsrResolver.AddFontSets(fsgDefault);
			FontProvider.SetFontSetResolver(fsrResolver);

		    return fsrResolver;
		}

		#endregion

		#region Support Methods

		/// <summary>
		/// This disposes of the services we created.
		/// </summary>
		/// <param name="serviceManager">The services to dispose.</param>
		protected void DisposeServices(ServiceManager serviceManager)
		{
		    serviceManager?.ModInstallLog.Release();
		    serviceManager?.ActivePluginLog?.Release();
		    serviceManager?.PluginOrderLog?.Release();
		    serviceManager?.PluginManager?.Release();
		    serviceManager?.ModManager.Release();
		}

		#endregion

		#region Game Detection/Selection

		/// <summary>
		/// Gets a registry of supported game modes.
		/// </summary>
		/// <returns>A registry of supported game modes.</returns>
		protected GameModeRegistry GetSupportedGameModes()
		{
			return GameModeRegistry.DiscoverSupportedGameModes(_environmentInfo);
		}

		/// <summary>
		/// Gets a registry of installed game modes.
		/// </summary>
		/// <param name="supportedGameModes">The games modes supported by the mod manager.</param>
		/// <returns>A registry of installed game modes.</returns>
		protected GameModeRegistry GetInstalledGameModes(GameModeRegistry supportedGameModes)
		{
			if (!_environmentInfo.Settings.InstalledGamesDetected)
			{
				var gdrGameDetector = new GameDiscoverer();
				var vmlGameDetection = new GameDetectionVM(_environmentInfo, gdrGameDetector, supportedGameModes);
				var frmGameDetector = new GameDetectionForm(vmlGameDetection);

			    gdrGameDetector.Find(supportedGameModes.RegisteredGameModeFactories);
				frmGameDetector.ShowDialog();

			    if (gdrGameDetector.Status != TaskStatus.Complete)
                {
                    return null;
                }

                if (gdrGameDetector.DiscoveredGameModes.Count == 0)
                {
                    return null;
                }

                _environmentInfo.Settings.InstalledGames.Clear();
				var j = 0;

			    foreach (var gidGameMode in gdrGameDetector.DiscoveredGameModes)
				{
					if (gidGameMode?.GameMode != null)
					{
						var gmfGameModeFactory = supportedGameModes.GetGameMode(gidGameMode.GameMode.ModeId);
						_environmentInfo.Settings.InstallationPaths[gidGameMode.GameMode.ModeId] = gmfGameModeFactory.GetInstallationPath(gidGameMode.GameInstallPath);
						_environmentInfo.Settings.ExecutablePaths[gidGameMode.GameMode.ModeId] = gmfGameModeFactory.GetExecutablePath(gidGameMode.GameInstallPath);
						_environmentInfo.Settings.InstalledGames.Add(gidGameMode.GameMode.ModeId);
					}
					else
					{
						MessageBox.Show($"An error occured during the scan of the game {gdrGameDetector.DiscoveredGameModes[j].GameMode.ModeId} : {Environment.NewLine + "The object GameMode is NULL"}");
					}

					j++;
				}

				_environmentInfo.Settings.InstalledGamesDetected = true;
				_environmentInfo.Settings.CacheOverhaulSetup = false;
				_environmentInfo.Settings.Save();
			}

			var gmrInstalledGameModes = GameModeRegistry.LoadInstalledGameModes(supportedGameModes, _environmentInfo);

		    return gmrInstalledGameModes;
		}

		#endregion

		/// <summary>
		/// This checks and deletes all the ModScript.dll in the NMM GameModes folder.
		/// </summary>
		private List<string> CheckModScriptDLL()
		{
		    var dllsToDelete = new List<string>
		    {
		        "DarkSouls.ModScript.dll",
		        "DarkSouls2.ModScript.dll",
		        "DragonAge.ModScript.dll",
		        "DragonAge2.ModScript.dll",
		        "Grimrock.ModScript.dll",
		        "Morrowind.ModScript.dll",
		        "Oblivion.ModScript.dll",
		        "Starbound.ModScript.dll",
		        "StateOfDecay.ModScript.dll",
		        "Witcher2.ModScript.dll",
		        "WorldOfTanks.ModScript.dll"
		    };

		    foreach (var dll in dllsToDelete)
			{
				var dllFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "GameModes", dll);

				try
				{
					if (File.Exists(dllFile))
                    {
                        FileUtil.ForceDelete(dllFile);
                    }
                }
				catch {	}
			}

			return dllsToDelete;

		}
	}
}
