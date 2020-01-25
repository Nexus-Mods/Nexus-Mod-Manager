namespace Nexus.Client
{
    using System;
    using System.Configuration;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;

    using Microsoft.Win32;

    using Nexus.Client.ModRepositories;
    using Nexus.Client.Util;
    using Nexus.Client.Util.Threading;
    using Nexus.UI.Controls;

    /// <summary>
    /// The entry class of the application.
    /// </summary>
    static class Program
	{
		private static EnvironmentInfo EnvironmentInfo = null;

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main(string[] p_strArgs)
		{
			Mutex mtxAppRunningMutex = null;
			try
			{
				mtxAppRunningMutex = new Mutex(false, "Global\\6af12c54-643b-4752-87d0-8335503010de");

				bool booTrace = false;

				foreach (string strArg in p_strArgs)
				{
					if (strArg.ToLower().Equals("/trace") || strArg.ToLower().Equals("-trace"))
					{
						booTrace = true;
						break;
					}
				}

				foreach (string strArg in p_strArgs)
				{
					string[] strArgParts = strArg.Split('=');
					if (strArgParts[0].ToLower().Equals("/u") || strArgParts[0].ToLower().Equals("-u"))
					{
						string strGuid = strArgParts[1];
						string strPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
						ProcessStartInfo psiInfo = new ProcessStartInfo(strPath + @"\msiexec.exe", "/x " + strGuid);
						Process.Start(psiInfo);
						return;
					}
				}

#if DEBUG
				Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException, true);
#else
				Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException, false);
#endif

				Application.ThreadException += new System.Threading.ThreadExceptionEventHandler(Application_ThreadException);
				AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);

				UpgradeSettings(Properties.Settings.Default);
				EnvironmentInfo = new EnvironmentInfo(Properties.Settings.Default);

                if (!Directory.Exists(EnvironmentInfo.ApplicationPersonalDataFolderPath))
                {
                    Directory.CreateDirectory(EnvironmentInfo.ApplicationPersonalDataFolderPath);
                }

                EnableTracing(EnvironmentInfo, booTrace);

#if !DEBUG
				try
				{
#endif
					Bootstrapper btsInitializer = new Bootstrapper(EnvironmentInfo);
					try
					{
						btsInitializer.RunMainForm(p_strArgs);
					}
					catch (MissingMethodException)
					{
						if (MessageBox.Show("You're running an older version of the .Net Framework!" + Environment.NewLine + "Please download .Net Framework 4.6 from the Microsoft website or using Windows Update." +
								Environment.NewLine + Environment.NewLine + "Click YES if you want Nexus Mod Manager to automatically take you to the download page on your default browser." + Environment.NewLine +
								"Click NO if you want to close the program and download it later.", "Warning!", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
						{
							Process.Start("https://www.microsoft.com/en-us/download/details.aspx?id=48137");
						}

						Application.Exit();
					}
#if !DEBUG
				}
				catch (Exception e)
				{
					HandleException(e);
				}
#endif

				Trace.TraceInformation(String.Format("Running Threads ({0})", TrackedThreadManager.Threads.Length));
				Trace.Indent();
				TrackedThread[] thdThreads = TrackedThreadManager.Threads;
				foreach (TrackedThread thdThread in thdThreads)
				{
					Trace.TraceInformation(String.Format("{0} ({1}) ", thdThread.Thread.ManagedThreadId, thdThread.Thread.Name));
					Trace.Indent();
					if (thdThread.Thread.IsAlive)
					{
						Trace.TraceInformation("Aborted");
						thdThread.Thread.Abort();
					}
					else
						Trace.TraceInformation("Ended Cleanly");
					Trace.Unindent();
				}
				Trace.Unindent();
			}
			catch (ConfigurationErrorsException e)
			{
			    var userChoice = MessageBox.Show("It seems your Nexus Mod Manager application settings file has been corrupted, we can reset this file for you.\n\n" + 
				    "Yes: Will reset your NMM related settings (scanned game locations, mod storage path, etc.) but will not remove your installed mods.\n\n" + 
				    "No: Your settings will remain corrupted and NMM will crash when trying to start.", "Settings file corrupted", 
				    MessageBoxButtons.YesNo, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);

			    if (userChoice == DialogResult.Yes)
			    {
				    var filename = e.Filename;

				    if (string.IsNullOrEmpty(filename) && e.InnerException.GetType() == typeof(ConfigurationErrorsException))
				    {
					    var inner = e.InnerException as ConfigurationErrorsException;
					    filename = inner.Filename;
				    }

				    try
				    {
					    File.Delete(filename);
					    MessageBox.Show("We've deleted your corrupted settings file, please restart Nexus Mod Manager.", "Settings reset", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
				    }
				    catch
				    {
					    MessageBox.Show("Something went wrong when trying to delete the settings file \"" + filename + "\".", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
				    }
			    }
			    else
			    {
				    MessageBox.Show("Nothing has been done, Nexus Mod Manager will now shut down.", "Settings unchanged", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
			    }
			}
			finally
			{
				if (mtxAppRunningMutex != null)
				{
					mtxAppRunningMutex.Close();
				}
			}
		}

		/// <summary>
		/// This ensures that the settings have been upgraded to work with the current
		/// version of the programme.
		/// </summary>
		/// <param name="p_setSettings">The settings to upgrade.</param>
		private static void UpgradeSettings(Properties.Settings p_setSettings)
		{
			if (!p_setSettings.SettingsUpgraded)
			{
				try
				{
					p_setSettings.Upgrade();
				}
				catch (ConfigurationException e)
				{
					string strFilename = e.Filename;
					File.Delete(strFilename);
					p_setSettings.Reload();
				}
				p_setSettings.Upgrade();
				p_setSettings.SettingsUpgraded = true;
				p_setSettings.Save();
			}
		}

		/// <summary>
		/// Truns on tracing.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_booForceTrace">Whether to force the trace file to be written.</param>
		private static void EnableTracing(IEnvironmentInfo p_eifEnvironmentInfo, bool p_booForceTrace)
		{
			Trace.AutoFlush = true;
			string strTraceFile = "TraceLog" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt";
			TextWriterTraceListener ttlTraceFile = null;

            if (p_booForceTrace)
            {
                ttlTraceFile = new HeaderlessTextWriterTraceListener(Path.Combine(String.IsNullOrEmpty(p_eifEnvironmentInfo.Settings.TraceLogFolder) ? p_eifEnvironmentInfo.ApplicationPersonalDataFolderPath : p_eifEnvironmentInfo.Settings.TraceLogFolder, strTraceFile));
            }
            else
            {
                ttlTraceFile = new HeaderlessTextWriterTraceListener(new MemoryStream(), Path.Combine(String.IsNullOrEmpty(p_eifEnvironmentInfo.Settings.TraceLogFolder) ? p_eifEnvironmentInfo.ApplicationPersonalDataFolderPath : p_eifEnvironmentInfo.Settings.TraceLogFolder, strTraceFile));
            }

			ttlTraceFile.Name = "DefaultListener";
			Trace.Listeners.Add(ttlTraceFile);
			Trace.TraceInformation("Trace file has been created: " + strTraceFile);

			StringBuilder stbStatus = new StringBuilder();
			stbStatus.AppendFormat("Mod Manager Version: {0}{1}", Assembly.GetExecutingAssembly().GetName().Version, p_eifEnvironmentInfo.IsMonoMode ? "(mono)" : "").AppendLine();
			stbStatus.AppendFormat("OS version: {0}", Environment.OSVersion.ToString()).AppendLine();

			stbStatus.AppendLine("Installed .NET Versions:");
			RegistryKey rkyIinstalledVersions = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP");
			string[] strVersionNames = rkyIinstalledVersions.GetSubKeyNames();
			foreach (string strFrameworkVersion in strVersionNames)
			{
				string strSP = rkyIinstalledVersions.OpenSubKey(strFrameworkVersion).GetValue("SP", 0).ToString();
				stbStatus.AppendFormat("\t{0} SP {1}", strFrameworkVersion, strSP).AppendLine();
			}

			using (RegistryKey ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey("SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full\\"))
			{
                if (ndpKey != null)
                {
                    int releaseKey = Convert.ToInt32(ndpKey.GetValue("Release"));
                    stbStatus.AppendFormat("\tv4.5: {0}", CheckFor45DotVersion(releaseKey)).AppendLine();
                }
				else
                {
                    stbStatus.AppendLine("\tv4.5: Not found.");
                }
			}

			stbStatus.AppendFormat("Tracing is forced: {0}", p_booForceTrace).AppendLine();
			Trace.TraceInformation(stbStatus.ToString());
		}

		private static string CheckFor45DotVersion(int releaseKey)
		{
			if ((releaseKey >= 381029))
			{
				return "4.6 or later";
			}
			if ((releaseKey >= 379893))
			{
				return "4.5.2 or later";
			}
			if ((releaseKey >= 378758))
			{
				return "4.5.1 or later";
			}
			if ((releaseKey >= 378675))
			{
				return "4.5.1 with Windows 8.1";
			}
			if ((releaseKey >= 378389))
			{
				return "4.5 or later";
			}

			return "No 4.5 or later version detected";
		}

		/// <summary>
		/// Handles the <see cref="AppDomain.UnhandledException"/> event of the current <see cref="AppDomain"/>.
		/// </summary>
		/// <remarks>
		/// This logs the exception, and terminates the application.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="UnhandledExceptionEventArgs"/> describing the event arguments.</param>
		private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			HandleException(e.ExceptionObject as Exception);
			Application.Exit();
		}

		/// <summary>
		/// Handles the <see cref="Application.ThreadException"/> event of the application.
		/// </summary>
		/// <remarks>
		/// This logs the exception, and terminates the application.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="ThreadExceptionEventArgs "/> describing the event arguments.</param>
		private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
		{
			HandleException(e.Exception);
			Application.Exit();
		}

		/// <summary>
		/// This lets the user know a problem has occurred, and logs the exception.
		/// </summary>
		/// <param name="ex">The exception that is being handled.</param>
		private static void HandleException(Exception ex)
		{
			HeaderlessTextWriterTraceListener htlListener = (HeaderlessTextWriterTraceListener)Trace.Listeners["DefaultListener"];
			DialogResult drResult = DialogResult.No;
			Trace.WriteLine("");
			Trace.TraceError("Tracing an Unhandled Exception:");
			TraceUtil.TraceException(ex);

			if (!htlListener.TraceIsForced)
				htlListener.SaveToFile();

			StringBuilder stbPromptMessage = new StringBuilder();
			stbPromptMessage.AppendFormat("{0} has encountered an error and needs to close.", CommonData.ModManagerName).AppendLine();
			stbPromptMessage.AppendLine("A Trace Log file was created at:");
			stbPromptMessage.AppendLine(htlListener.FilePath);
			stbPromptMessage.AppendLine("Before reporting the issue, don't close this window and check for a fix here (you can close it afterwards):");
			stbPromptMessage.AppendLine(Links.FAQs);
			stbPromptMessage.AppendLine("If you can't find a solution, please make a bug report and attach the TraceLog file here:");
			stbPromptMessage.AppendLine(Links.Instance.Issues);
			stbPromptMessage.AppendLine(Environment.NewLine + "Do you want to open the TraceLog folder?");
			try
			{
				//the extended message box contains an activex control wich must be run in an STA thread,
				// we can't control what thread this gets called on, so create one if we need to
				string strException = "The following information is in the Trace Log:" + Environment.NewLine + TraceUtil.CreateTraceExceptionString(ex);
				ThreadStart actShowMessage = () => drResult = ExtendedMessageBox.Show(null, stbPromptMessage.ToString(), "Error", strException, MessageBoxButtons.YesNo, MessageBoxIcon.Information);

				ApartmentState astState = ApartmentState.Unknown;
				Thread.CurrentThread.TrySetApartmentState(astState);
				if (astState == ApartmentState.STA)
					actShowMessage();
				else
				{
					Thread thdMessage = new Thread(actShowMessage);
					thdMessage.SetApartmentState(ApartmentState.STA);
					thdMessage.Start();
					thdMessage.Join();
					if (drResult == DialogResult.Yes)
					{
						try
						{
							System.Diagnostics.Process prc = new System.Diagnostics.Process();
							prc.StartInfo.FileName = Path.GetDirectoryName(htlListener.FilePath);
							prc.Start();
						}
						catch { }
					}
				}
			}
			catch
			{
				//backup, in case on extended message box starts to act up
				MessageBox.Show(stbPromptMessage.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}
	}
}
