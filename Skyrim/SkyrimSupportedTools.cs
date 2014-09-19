using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;
using Microsoft.Win32;

namespace Nexus.Client.Games.Skyrim
{
	/// <summary>
	/// Launches Skyrim.
	/// </summary>
	public class SkyrimSupportedTools : SupportedToolsLauncherBase
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		public SkyrimSupportedTools(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_gmdGameMode, p_eifEnvironmentInfo)
		{
		}

		#endregion

		/// <summary>
		/// Initializes the supported tools launch commands.
		/// </summary>
		public override void SetupCommands()
		{
			
			Trace.TraceInformation("Launch Commands:");
			Trace.Indent();
			Image imgIcon = null;

			ClearLaunchCommands();

			string strCommand = GetBOSSLaunchCommand();
			Trace.TraceInformation("BOSS Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("BOSSLaunch", "Launch BOSS", "Launches BOSS.", imgIcon, LaunchBOSS, true));
			}

			strCommand = GetWryeBashLaunchCommand();
			Trace.TraceInformation("Wrye Bash Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("WryeBashLaunch", "Launch Wrye Bash", "Launches Wrye Bash.", imgIcon, LaunchWryeBash, true));
			}

			strCommand = GetFNISLaunchCommand();
			Trace.TraceInformation("FNIS Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("FNISLaunch", "Launch FNIS", "Launches FNIS.", imgIcon, LaunchFNIS, true));
			}
			
			Trace.Unindent();
		}

		#region Launch Commands

		private void LaunchBOSS()
		{
			Trace.TraceInformation("Launching BOSS");
			Trace.Indent();
			string strCommand = GetBOSSLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		private void LaunchWryeBash()
		{
			Trace.TraceInformation("Launching Wrye Bash");
			Trace.Indent();
			string strCommand = GetWryeBashLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		private void LaunchFNIS()
		{
			Trace.TraceInformation("Launching FNIS");
			Trace.Indent();
			string strCommand = GetFNISLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		/// <summary>
		/// Gets the BOSS launch command.
		/// </summary>
		/// <returns>The BOSS launch command.</returns>
		private string GetBOSSLaunchCommand()
		{
			string strBOSS = null;

			strBOSS = EnvironmentInfo.Settings.BOSSFolder[GameMode.ModeId];
			if (strBOSS == null)
			{
				if (IntPtr.Size == 8)
					strBOSS = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\BOSS\", "Installed Path", null);
				else
					strBOSS = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\BOSS\", "Installed Path", null);
			}
			if (strBOSS != null)
			    strBOSS = Path.Combine(strBOSS, "boss.exe");

			return strBOSS;
		}

		/// <summary>
		/// Gets the Wrye Bash launch command.
		/// </summary>
		/// <returns>The Wrye Bash launch command.</returns>
		private string GetWryeBashLaunchCommand()
		{
			string strWryePath = null;
			if(EnvironmentInfo.Settings.WryeBashFolder[GameMode.ModeId] != null)
				strWryePath = Path.Combine(EnvironmentInfo.Settings.WryeBashFolder[GameMode.ModeId], @"Wrye Bash.exe");

			return strWryePath;
		}

		/// <summary>
		/// Gets the FNIS launch command.
		/// </summary>
		/// <returns>The Wrye Bash launch command.</returns>
		private string GetFNISLaunchCommand()
		{
			string strFNIS = null;
			if(EnvironmentInfo.Settings.FNISFolder[GameMode.ModeId] != null)
				strFNIS = Path.Combine(EnvironmentInfo.Settings.FNISFolder[GameMode.ModeId], @"GenerateFNISforUsers.exe");

			return strFNIS;
		}
		
		#endregion
	}
}
