using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Remoting;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Controls;
using Nexus.Client.Games;
using Nexus.Client.Settings;
using Nexus.Client.Util;
using SevenZip;

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

			string[] strArgs = p_strArgs;
			Uri uriModToAdd = null;
			if ((p_strArgs.Length > 0) && !p_strArgs[0].StartsWith("-"))
			{
				if (Uri.TryCreate(p_strArgs[0], UriKind.Absolute, out uriModToAdd) && uriModToAdd.Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase))
				{
					strArgs = new string[p_strArgs.Length + 2];
					Array.Copy(p_strArgs, strArgs, p_strArgs.Length);
					strArgs[strArgs.Length - 2] = "-game";
					strArgs[strArgs.Length - 1] = uriModToAdd.Host;
				}
			}
			bool booChangeGameMode = false;
			do
			{
				GameModeSelector gmsSelector = new GameModeSelector(m_eifEnvironmentInfo);
				IGameModeFactory gmfGameModeFactory = gmsSelector.SelectGameMode(strArgs, booChangeGameMode);

				Trace.TraceInformation(String.Format("Game Mode Factory Selected: {0} ({1})", gmfGameModeFactory.GameModeDescriptor.Name, gmfGameModeFactory.GameModeDescriptor.ModeId));

				Mutex mtxGameModeMutex = null;
				bool booOwnsMutex = false;
				try
				{
					Trace.TraceInformation("Creating Game Mode mutex.");
					mtxGameModeMutex = new Mutex(true, String.Format("{0}-{1}-GameModeMutex", m_eifEnvironmentInfo.Settings.ModManagerName, gmfGameModeFactory.GameModeDescriptor.ModeId), out booOwnsMutex);
					if (!booOwnsMutex)
					{
						try
						{
							if (uriModToAdd != null)
							{
								Trace.TraceInformation(String.Format("Messaging to add: {0}", uriModToAdd));
								Messager.GetMessager(m_eifEnvironmentInfo, gmfGameModeFactory.GameModeDescriptor).AddMod(uriModToAdd.ToString());
							}
							else
							{
								Trace.TraceInformation(String.Format("Messaging to bring to front."));
								Messager.GetMessager(m_eifEnvironmentInfo, gmfGameModeFactory.GameModeDescriptor).BringToFront();
							}
						}
						catch (RemotingException)
						{
							//the running client crashed; nothing we can or want to do.
							// is that true? let's log for a while to see if it is.

							HeaderlessTextWriterTraceListener htlListener = (HeaderlessTextWriterTraceListener)Trace.Listeners["DefaultListener"];
							htlListener.ChangeFilePath(Path.Combine(Path.GetDirectoryName(htlListener.FilePath), "Messager" + Path.GetFileName(htlListener.FilePath)));
							Trace.TraceInformation("THIS IS A MESSAGER TRACE LOG.");
							throw;
						}

						return false;
					}

					ApplicationInitializer ainInitializer = new ApplicationInitializer(m_eifEnvironmentInfo);
					ApplicationInitializationForm frmAppInitilizer = new ApplicationInitializationForm(ainInitializer);
					ainInitializer.Initialize(gmfGameModeFactory, SynchronizationContext.Current);
					frmAppInitilizer.ShowDialog();
					if (ainInitializer.Status != TaskStatus.Complete)
					{
						if (ainInitializer.Status == TaskStatus.Error)
							return false;
						booChangeGameMode = true;
						DisposeServices(ainInitializer.Services);
						continue;
					}

					IGameMode gmdGameMode = ainInitializer.GameMode;
					ServiceManager svmServices = ainInitializer.Services;

					MainFormVM vmlMainForm = new MainFormVM(m_eifEnvironmentInfo, gmdGameMode, svmServices.ModRepository, svmServices.ActivityMonitor, svmServices.UpdateManager, svmServices.ModManager, svmServices.PluginManager);
					MainForm frmMain = new MainForm(vmlMainForm);

					using (Messager msgMessager = Messager.InitializeListener(m_eifEnvironmentInfo, gmdGameMode, svmServices.ModManager, frmMain))
					{
						if (uriModToAdd != null)
						{
							Trace.TraceInformation("Adding mod: " + uriModToAdd.ToString());
							msgMessager.AddMod(uriModToAdd.ToString());
						}

						Trace.TraceInformation("Running Application.");
						try
						{
							Application.Run(frmMain);
							svmServices.ModInstallLog.Backup();
							booChangeGameMode = vmlMainForm.GameModeChangeRequested;
						}
						finally
						{
							DisposeServices(svmServices);
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
				}
			} while (booChangeGameMode);
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
									"If you are running Comodo, please add {0} to the exception list.<br/><br/>" +
									"If you are not running in a sandbox, press Cancel, and make a bug report " +
									"with the generated TraceLog.";
				if (ExtendedMessageBox.Show(null, String.Format(strMessage, p_eifEnvironmentInfo.Settings.ModManagerName), "Sandbox Detected", String.Format(strDetails, p_eifEnvironmentInfo.Settings.ModManagerName), MessageBoxButtons.OKCancel, MessageBoxIcon.Error) == DialogResult.Cancel)
					throw;
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
			p_smgServices.ActivePluginLog.Release();
			p_smgServices.PluginOrderLog.Release();
			p_smgServices.PluginManager.Release();
			p_smgServices.ModManager.Release();
		}

		#endregion
	}
}
