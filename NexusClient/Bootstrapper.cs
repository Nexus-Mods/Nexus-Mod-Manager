using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Properties;
using Nexus.Client.Settings;
using Nexus.Client.Util;
using Nexus.UI;
using Nexus.UI.Controls;
using SevenZip;
using Nexus.Client.UI;

namespace Nexus.Client
{
	/// <summary>
	/// This class is responsible for creating all the services the application needs, and making sure that the
	/// environment is sane.
	/// </summary>
	public class Bootstrapper
	{
		private EnvironmentInfo m_eifEnvironmentInfo = null;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public Bootstrapper(EnvironmentInfo p_eifEnvironmentInfo)
		{
			m_eifEnvironmentInfo = p_eifEnvironmentInfo;
		}

		#endregion

		/// <summary>
		/// Runs the applications
		/// </summary>
		/// <remarks>
		/// This method makes sure the environment is sane. If so, it creates the required services
		/// and launches the main form.
		/// </remarks>
		/// <param name="p_strArgs">The command line arguments passed to the application.</param>
		/// <returns><c>true</c> if the application started as expected;
		/// <c>false</c> otherwise.</returns>
		public bool RunMainForm(string[] p_strArgs)
		{
			if (!SandboxCheck(m_eifEnvironmentInfo))
				return false;
			SetCompressorPath(m_eifEnvironmentInfo);

			List<string> lstDeletedDLL = CheckModScriptDLL();

			string strRequestedGameMode = null;
			string[] strArgs = p_strArgs;
			Uri uriModToAdd = null;
			if ((p_strArgs.Length > 0) && !p_strArgs[0].StartsWith("-"))
			{
				if (Uri.TryCreate(p_strArgs[0], UriKind.Absolute, out uriModToAdd) && uriModToAdd.Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase))
					strRequestedGameMode = uriModToAdd.Host;
			}
			else
				for (Int32 i = 0; i < p_strArgs.Length; i++)
				{
					string strArg = p_strArgs[i];
					if (strArg.StartsWith("-"))
					{
						switch (strArg)
						{
							case "-game":
								strRequestedGameMode = p_strArgs[i + 1];
								Trace.Write("Game Specified On Command line: " + strRequestedGameMode + ") ");
								break;
						}
					}
				}

			bool booChangeDefaultGameMode = false;
			GameModeRegistry gmrSupportedGames = GetSupportedGameModes();
			do
			{
				NexusFontSetResolver nfrResolver = SetUpFonts();

				GameModeRegistry gmrInstalledGames = GetInstalledGameModes(gmrSupportedGames);
				if (gmrInstalledGames == null)
				{
					Trace.TraceInformation("No installed games.");
					MessageBox.Show(String.Format("No games were detected! {0} will now close.", m_eifEnvironmentInfo.Settings.ModManagerName), "No Games", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}

				GameModeSelector gmsSelector = new GameModeSelector(gmrSupportedGames, gmrInstalledGames, m_eifEnvironmentInfo);
				IGameModeFactory gmfGameModeFactory = gmsSelector.SelectGameMode(strRequestedGameMode, booChangeDefaultGameMode);
				if (gmsSelector.RescanRequested)
				{
					m_eifEnvironmentInfo.Settings.InstalledGamesDetected = false;
					m_eifEnvironmentInfo.Settings.Save();
					booChangeDefaultGameMode = true;
					continue;
				}
				if (gmfGameModeFactory == null)
					return false;

				Trace.TraceInformation(String.Format("Game Mode Factory Selected: {0} ({1})", gmfGameModeFactory.GameModeDescriptor.Name, gmfGameModeFactory.GameModeDescriptor.ModeId));

				Mutex mtxGameModeMutex = null;
				bool booOwnsMutex = false;
				try
				{
					for (Int32 intAttemptCount = 0; intAttemptCount < 3; intAttemptCount++)
					{
						Trace.TraceInformation("Creating Game Mode mutex (Attempt: {0})", intAttemptCount);
						mtxGameModeMutex = new Mutex(true, String.Format("{0}-{1}-GameModeMutex", m_eifEnvironmentInfo.Settings.ModManagerName, gmfGameModeFactory.GameModeDescriptor.ModeId), out booOwnsMutex);

						//If the mutex is owned, you are the first instance of the mod manager for game mode, so break out of loop.
						if (booOwnsMutex)
							break;

						try
						{
							//If the mutex isn't owned, attempt to talk across the messager.
							using (IMessager msgMessager = MessagerClient.GetMessager(m_eifEnvironmentInfo, gmfGameModeFactory.GameModeDescriptor))
							{
								if (msgMessager != null)
								{
									//Messenger was created OK, send download request, or bring to front.
									if (uriModToAdd != null)
									{
										Trace.TraceInformation(String.Format("Messaging to add: {0}", uriModToAdd));
										msgMessager.AddMod(uriModToAdd.ToString());
									}
									else
									{
										Trace.TraceInformation(String.Format("Messaging to bring to front."));
										msgMessager.BringToFront();
									}
									return true;
								}
							}
							mtxGameModeMutex.Close();
							mtxGameModeMutex = null;
						}
						catch (InvalidOperationException)
						{
							StringBuilder stbPromptMessage = new StringBuilder();
							stbPromptMessage.AppendFormat("{0} was unable to start. It appears another instance of {0} is already running.", m_eifEnvironmentInfo.Settings.ModManagerName).AppendLine();
							stbPromptMessage.AppendFormat("If you were trying to download multiple files, wait for {0} to start before clicking on a new file download.", m_eifEnvironmentInfo.Settings.ModManagerName).AppendLine();
							MessageBox.Show(stbPromptMessage.ToString(), "Already running", MessageBoxButtons.OK, MessageBoxIcon.Information);
							return false;
						}
						//Messenger couldn't be created, so sleep for a few seconds to give time for opening
						// the running copy of the mod manager to start up/shut down
						Thread.Sleep(TimeSpan.FromSeconds(5.0d));
					}
					if (!booOwnsMutex)
					{
						HeaderlessTextWriterTraceListener htlListener = (HeaderlessTextWriterTraceListener)Trace.Listeners["DefaultListener"];
						htlListener.ChangeFilePath(Path.Combine(Path.GetDirectoryName(htlListener.FilePath), "Messager" + Path.GetFileName(htlListener.FilePath)));
						Trace.TraceInformation("THIS IS A MESSAGER TRACE LOG.");
						if (!htlListener.TraceIsForced)
							htlListener.SaveToFile();

						StringBuilder stbPromptMessage = new StringBuilder();
						stbPromptMessage.AppendFormat("{0} was unable to start. It appears another instance of {0} is already running.", m_eifEnvironmentInfo.Settings.ModManagerName).AppendLine();
						stbPromptMessage.AppendLine("A Trace Log file was created at:");
						stbPromptMessage.AppendLine(htlListener.FilePath);
						stbPromptMessage.AppendLine("Before reporting the issue, don't close this window and check for a fix here (you can close it afterwards):");
						stbPromptMessage.AppendLine("http://forums.nexusmods.com/index.php?/topic/721054-read-here-first-nexus-mod-manager-frequent-issues/");
						stbPromptMessage.AppendLine("If you can't find a solution, please make a bug report and attach the TraceLog file here:");
						stbPromptMessage.AppendLine("http://forums.nexusmods.com/index.php?/tracker/project-3-mod-manager-open-beta/");
						MessageBox.Show(stbPromptMessage.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Information);
						return false;
					}

					//ApplicationInitializer ainInitializer = new ApplicationInitializer(m_eifEnvironmentInfo, nfrResolver);
					//ApplicationInitializationForm frmAppInitilizer = new ApplicationInitializationForm(ainInitializer);
					//ainInitializer.Initialize(gmfGameModeFactory, SynchronizationContext.Current);
					//frmAppInitilizer.ShowDialog();
					ApplicationInitializer ainInitializer = null;
					ApplicationInitializationForm frmAppInitilizer = null;
					while ((ainInitializer == null) || (ainInitializer.Status == TaskStatus.Retrying))
					{
						ainInitializer = new ApplicationInitializer(m_eifEnvironmentInfo, nfrResolver, lstDeletedDLL);
						frmAppInitilizer = new ApplicationInitializationForm(ainInitializer);
						ainInitializer.Initialize(gmfGameModeFactory, SynchronizationContext.Current);
						frmAppInitilizer.ShowDialog();
					}

					if (ainInitializer.Status != TaskStatus.Complete)
					{
						if (ainInitializer.Status == TaskStatus.Error)
							return false;
						booChangeDefaultGameMode = true;
						DisposeServices(ainInitializer.Services);
						continue;
					}

					IGameMode gmdGameMode = ainInitializer.GameMode;
					ServiceManager svmServices = ainInitializer.Services;

					MainFormVM vmlMainForm = new MainFormVM(m_eifEnvironmentInfo, gmrInstalledGames, gmdGameMode, svmServices.ModRepository, svmServices.DownloadMonitor, svmServices.ModActivationMonitor, svmServices.ModManager, svmServices.PluginManager);
					MainForm frmMain = new MainForm(vmlMainForm);

					using (IMessager msgMessager = MessagerServer.InitializeListener(m_eifEnvironmentInfo, gmdGameMode, svmServices.ModManager, frmMain))
					{
						if (uriModToAdd != null)
						{
							Trace.TraceInformation("Adding mod: " + uriModToAdd.ToString());
							msgMessager.AddMod(uriModToAdd.ToString());
							uriModToAdd = null;
						}

						Trace.TraceInformation("Running Application.");
						try
						{
							Application.Run(frmMain);
							svmServices.ModInstallLog.Backup();
							strRequestedGameMode = vmlMainForm.RequestedGameMode;
							booChangeDefaultGameMode = vmlMainForm.DefaultGameModeChangeRequested;
						}
						finally
						{
							DisposeServices(svmServices);
							gmdGameMode.Dispose();
                        }
					}
				}
				finally
				{
					if (mtxGameModeMutex != null)
					{
						if (booOwnsMutex)
							mtxGameModeMutex.ReleaseMutex();
						mtxGameModeMutex.Close();
					}
					FileUtil.ForceDelete(m_eifEnvironmentInfo.TemporaryPath);

					//Clean up created font's.
					FontManager.Dispose();
				}
			} while (!String.IsNullOrEmpty(strRequestedGameMode) || booChangeDefaultGameMode);
			return true;
		}

		#region Pre Game Mode Selection

		/// <summary>
		/// Checks to see if a sandbox is interfering with dynamic code generation.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <returns><c>true</c> if the check passed;
		/// <c>false</c> otherwise.</returns>
		protected bool SandboxCheck(EnvironmentInfo p_eifEnvironmentInfo)
		{
			try
			{
				new XmlSerializer(typeof(WindowPositions));
			}
			catch (InvalidOperationException)
			{

				string strMessage = "{0} has detected that it is running in a sandbox." + Environment.NewLine +
								"The sandbox is preventing {0} from performing" + Environment.NewLine +
								"important operations. Please run {0} again," + Environment.NewLine +
								"without the sandbox.";
				string strDetails = "This error commonly occurs on computers running Comodo Antivirus.<br/>" +
									"If you are running Comodo or any antivirus, please add {0} and its folders to the exception list.<br/><br/>";
				ExtendedMessageBox.Show(null, String.Format(strMessage, p_eifEnvironmentInfo.Settings.ModManagerName), "Sandbox Detected", String.Format(strDetails, p_eifEnvironmentInfo.Settings.ModManagerName), MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return false;
			}
			catch (System.Runtime.InteropServices.ExternalException)
			{
				string strMessage = "{0} has detected that it is running in a sandbox." + Environment.NewLine +
								"The sandbox is preventing {0} from performing" + Environment.NewLine +
								"important operations. Please run {0} again," + Environment.NewLine +
								"without the sandbox.";
				string strDetails = "This error commonly occurs on computers running Zone Alarm.<br/>" +
									"If you are running Zone Alarm or any similar security suite, please add {0} and its folders to the exception list.<br/><br/>";
				ExtendedMessageBox.Show(null, String.Format(strMessage, p_eifEnvironmentInfo.Settings.ModManagerName), "Sandbox Detected", String.Format(strDetails, p_eifEnvironmentInfo.Settings.ModManagerName), MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return false;
			}

			return true;
		}

		/// <summary>
		/// Sets the path to the external compression library.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		protected void SetCompressorPath(EnvironmentInfo p_eifEnvironmentInfo)
		{
			string str7zPath = Path.Combine(p_eifEnvironmentInfo.ProgrammeInfoDirectory, p_eifEnvironmentInfo.Is64BitProcess ? "7z-64bit.dll" : "7z-32bit.dll");
			SevenZipCompressor.SetLibraryPath(str7zPath);
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

			FontSet fstDefault = new FontSet(new string[] { "Microsoft Sans Serif", "Arial" });
			FontSetGroup fsgDefault = new FontSetGroup(fstDefault);
			fsgDefault.AddFontSet("StandardText", fstDefault);
			fsgDefault.AddFontSet("HeadingText", fstDefault);
			fsgDefault.AddFontSet("SmallText", new FontSet(new string[] { "Segoe UI", "Arial" }));
			fsgDefault.AddFontSet("MenuText", new FontSet(new string[] { "Segoe UI", "Arial" }));
			fsgDefault.AddFontSet("GameSearchText", new FontSet(new string[] { "LinBiolinum" }));
			fsgDefault.AddFontSet("TestText", new FontSet(new string[] { "Wingdings" }));

			NexusFontSetResolver fsrResolver = new NexusFontSetResolver();
			fsrResolver.AddFontSets(fsgDefault);

			FontProvider.SetFontSetResolver(fsrResolver);
			return fsrResolver;
		}

		#endregion

		#region Support Methods

		/// <summary>
		/// This disposes of the services we created.
		/// </summary>
		/// <param name="p_smgServices">The services to dispose.</param>
		protected void DisposeServices(ServiceManager p_smgServices)
		{
			if (p_smgServices == null)
				return;
			p_smgServices.ModInstallLog.Release();
			if (p_smgServices.ActivePluginLog != null)
				p_smgServices.ActivePluginLog.Release();
			if (p_smgServices.PluginOrderLog != null)
				p_smgServices.PluginOrderLog.Release();
			if (p_smgServices.PluginManager != null)
				p_smgServices.PluginManager.Release();
			p_smgServices.ModManager.Release();
		}

		#endregion

		#region Game Detection/Selection

		/// <summary>
		/// Gets a registry of supported game modes.
		/// </summary>
		/// <returns>A registry of supported game modes.</returns>
		protected GameModeRegistry GetSupportedGameModes()
		{
			return GameModeRegistry.DiscoverSupportedGameModes(m_eifEnvironmentInfo);
		}

		/// <summary>
		/// Gets a registry of installed game modes.
		/// </summary>
		/// <param name="p_gmrSupportedGameModes">The games modes supported by the mod manager.</param>
		/// <returns>A registry of installed game modes.</returns>
		protected GameModeRegistry GetInstalledGameModes(GameModeRegistry p_gmrSupportedGameModes)
		{
			if (!m_eifEnvironmentInfo.Settings.InstalledGamesDetected)
			{
				GameDiscoverer gdrGameDetector = new GameDiscoverer();
				GameDetectionVM vmlGameDetection = new GameDetectionVM(m_eifEnvironmentInfo, gdrGameDetector, p_gmrSupportedGameModes);
				GameDetectionForm frmGameDetector = new GameDetectionForm(vmlGameDetection);
				gdrGameDetector.Find(p_gmrSupportedGameModes.RegisteredGameModeFactories);
				frmGameDetector.ShowDialog();
				if (gdrGameDetector.Status != TaskStatus.Complete)
					return null;
				if (gdrGameDetector.DiscoveredGameModes.Count == 0)
					return null;
				m_eifEnvironmentInfo.Settings.InstalledGames.Clear();
				Int32 j = 0;
				foreach (GameDiscoverer.GameInstallData gidGameMode in gdrGameDetector.DiscoveredGameModes)
				{
					if ((gidGameMode != null) && (gidGameMode.GameMode != null))
					{
						IGameModeFactory gmfGameModeFactory = p_gmrSupportedGameModes.GetGameMode(gidGameMode.GameMode.ModeId);
						m_eifEnvironmentInfo.Settings.InstallationPaths[gidGameMode.GameMode.ModeId] = gmfGameModeFactory.GetInstallationPath(gidGameMode.GameInstallPath);
						m_eifEnvironmentInfo.Settings.ExecutablePaths[gidGameMode.GameMode.ModeId] = gmfGameModeFactory.GetExecutablePath(gidGameMode.GameInstallPath);
						m_eifEnvironmentInfo.Settings.InstalledGames.Add(gidGameMode.GameMode.ModeId);
					}
					else
					{
						MessageBox.Show(string.Format("An error occured during the scan of the game {0} : {1}", gdrGameDetector.DiscoveredGameModes[j].GameMode.ModeId, Environment.NewLine + "The object GameMode is NULL"));
					}
					j++;
				}
				m_eifEnvironmentInfo.Settings.InstalledGamesDetected = true;
				m_eifEnvironmentInfo.Settings.CacheOverhaulSetup = false;
				m_eifEnvironmentInfo.Settings.Save();
			}
			GameModeRegistry gmrInstalledGameModes = GameModeRegistry.LoadInstalledGameModes(p_gmrSupportedGameModes, m_eifEnvironmentInfo);
			return gmrInstalledGameModes;
		}

		#endregion

		/// <summary>
		/// This checks and deletes all the ModScript.dll in the NMM GameModes folder.
		/// </summary>
		private List<string> CheckModScriptDLL()
		{
			List<string> lstDeletedDLL = new List<string>();
			lstDeletedDLL.Add("DarkSouls.ModScript.dll");
			lstDeletedDLL.Add("DarkSouls2.ModScript.dll");
			lstDeletedDLL.Add("DragonAge.ModScript.dll");
			lstDeletedDLL.Add("DragonAge2.ModScript.dll");
			lstDeletedDLL.Add("Grimrock.ModScript.dll");
			lstDeletedDLL.Add("Morrowind.ModScript.dll");
			lstDeletedDLL.Add("Oblivion.ModScript.dll");
			lstDeletedDLL.Add("Starbound.ModScript.dll");
			lstDeletedDLL.Add("StateOfDecay.ModScript.dll");
			lstDeletedDLL.Add("Witcher2.ModScript.dll");
			lstDeletedDLL.Add("WorldOfTanks.ModScript.dll");

			foreach (string DLL in lstDeletedDLL)
			{
				string DLLFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "GameModes", DLL);

				try
				{
					if (File.Exists(DLLFile))
						FileUtil.ForceDelete(DLLFile);
				}
				catch {	}
			}

			return lstDeletedDLL;

		}
	}
}
