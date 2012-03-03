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
				if (!btsInitializer.RunMainForm(p_strArgs))
					return;
#if !DEBUG
				}
				catch (Exception e)
				{
					HandleException(e, "Something bad seems to have happened.", "Error");
				}
#endif

				Trace.TraceInformation(String.Format("Running Threads ({0})", TrackedThreadManager.Threads.Count));
				Trace.Indent();
				foreach (TrackedThread thdThread in TrackedThreadManager.Threads)
					Trace.TraceInformation(String.Format("{0} ({1}) ", thdThread.Thread.ManagedThreadId, thdThread.Thread.Name));
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
			HandleException(e.ExceptionObject as Exception, "Something bad seems to have happened.", "Error");
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
			HandleException(e.Exception, "Something bad seems to have happened.", "Error");
			Application.Exit();
		}

		/// <summary>
		/// This lets the user know a problem has occurred, and logs the exception.
		/// </summary>
		/// <param name="ex">The exception that is being handled.</param>
		/// <param name="p_strPromptMessage">The message to display to the user.</param>
		/// <param name="p_strPromptCaption">The caption of the form that will be displayed to the user.</param>
		private static void HandleException(Exception ex, string p_strPromptMessage, string p_strPromptCaption)
		{
			HeaderlessTextWriterTraceListener htlListener = (HeaderlessTextWriterTraceListener)Trace.Listeners["DefaultListener"];

			MessageBox.Show(p_strPromptMessage + Environment.NewLine +
							"As long as it wasn't too bad, a crash dump will have been saved in" + Environment.NewLine +
							htlListener.FilePath + Environment.NewLine +
							"Please include the contents of that file if you want to make a bug report", p_strPromptCaption, MessageBoxButtons.OK, MessageBoxIcon.Error);

			Trace.WriteLine("");
			Trace.TraceError("Tracing an Unhandled Exception:");
			TraceUtil.TraceException(ex);

			if (!htlListener.TraceIsForced)
				htlListener.SaveToFile();
		}
	}
}
