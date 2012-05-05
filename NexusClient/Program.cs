using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Nexus.Client.Util;
using Nexus.Client.Util.Threading;
using Nexus.Client.Controls;

namespace Nexus.Client
{
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
					if (strArg.ToLower().Equals("/trace") || strArg.ToLower().Equals("-trace"))
					{
						booTrace = true;
						break;
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
					Directory.CreateDirectory(EnvironmentInfo.ApplicationPersonalDataFolderPath);
				EnableTracing(EnvironmentInfo, booTrace);

#if !DEBUG
				try
				{
#endif
				Bootstrapper btsInitializer = new Bootstrapper(EnvironmentInfo);
				btsInitializer.RunMainForm(p_strArgs);
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
			finally
			{
				if (mtxAppRunningMutex != null)
					mtxAppRunningMutex.Close();
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
				ttlTraceFile = new HeaderlessTextWriterTraceListener(Path.Combine(p_eifEnvironmentInfo.ApplicationPersonalDataFolderPath, strTraceFile));
			else
				ttlTraceFile = new HeaderlessTextWriterTraceListener(new MemoryStream(), Path.Combine(p_eifEnvironmentInfo.ApplicationPersonalDataFolderPath, strTraceFile));
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
			
			stbStatus.AppendFormat("Tracing is forced: {0}", p_booForceTrace).AppendLine();
			Trace.TraceInformation(stbStatus.ToString());
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

			Trace.WriteLine("");
			Trace.TraceError("Tracing an Unhandled Exception:");
			TraceUtil.TraceException(ex);

			if (!htlListener.TraceIsForced)
				htlListener.SaveToFile();

			StringBuilder stbPromptMessage = new StringBuilder();
			stbPromptMessage.AppendFormat("{0} has encountered an error and needs to close.", EnvironmentInfo.Settings.ModManagerName).AppendLine();
			stbPromptMessage.AppendLine("A Trace Log was created at:");
			stbPromptMessage.AppendLine(htlListener.FilePath);
			stbPromptMessage.AppendLine("Please include the contents of that file if you want to make a bug report:");
			stbPromptMessage.AppendLine("http://forums.nexusmods.com/index.php?/tracker/project-3-mod-manager-open-beta/");
			try
			{
				//the extended message box contains an activex control wich must be run in an STA thread,
				// we can't control what thread this gets called on, so create one if we need to
				string strException = "The following information is in the Trace Log:" + Environment.NewLine + TraceUtil.CreateTraceExceptionString(ex);
				ThreadStart actShowMessage = () => ExtendedMessageBox.Show(null, stbPromptMessage.ToString(), "Error", strException, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
