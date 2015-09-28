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

			strCommand = GetLOOTLaunchCommand();
			Trace.TraceInformation("LOOT Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("LOOTLaunch", "Launch LOOT", "Launches LOOT.", imgIcon, LaunchLOOT, true));
			}

			strCommand = GetWryeBashLaunchCommand();
			Trace.TraceInformation("Wrye Bash Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("WryeBashLaunch", "Launch Wrye Bash", "Launches Wrye Bash.", imgIcon, LaunchWryeBash, true));
			}

			strCommand = GetTES5EditLaunchCommand();
			Trace.TraceInformation("TES5Edit Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("TES5EditLaunch", "Launch TES5Edit", "Launches TES5Edit.", imgIcon, LaunchTES5Edit, true));
			}

			strCommand = GetFNISLaunchCommand();
			Trace.TraceInformation("FNIS Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("FNISLaunch", "Launch FNIS", "Launches FNIS.", imgIcon, LaunchFNIS, true));
			}

			strCommand = GetBS2LaunchCommand();
			Trace.TraceInformation("BodySlide 2 Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("BS2Launch", "Launch BodySlide 2", "Launches BodySlide 2.", imgIcon, LaunchBS2, true));
			}

			strCommand = GetDSRPLaunchCommand();
			Trace.TraceInformation("Dual Sheat Redux Patch Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("DSRPLaunch", "Launch Dual Sheat Redux Patch", "Launches Dual Sheat Redux Patch.", imgIcon, LaunchDSRP, true));
			}

			strCommand = GetPMLaunchCommand();
			Trace.TraceInformation("Patchus Maximus Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("PMLaunch", "Launch Patchus Maximus", "Launches Patchus Maximus.", imgIcon, LaunchPM, true));
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

		private void LaunchLOOT()
		{
			Trace.TraceInformation("Launching LOOT");
			Trace.Indent();
			string strCommand = GetLOOTLaunchCommand();
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

		private void LaunchTES5Edit()
		{
			Trace.TraceInformation("Launching TES5Edit");
			Trace.Indent();
			string strCommand = GetTES5EditLaunchCommand();
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

		private void LaunchBS2()
		{
			Trace.TraceInformation("Launching BodySlide 2");
			Trace.Indent();
			string strCommand = GetBS2LaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		private void LaunchDSRP()
		{
			Trace.TraceInformation("Launching Dual Sheat Redux Patch");
			Trace.Indent();
			string strCommand = GetDSRPLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		private void LaunchPM()
		{
			Trace.TraceInformation("Launching Patchus Maximus");
			Trace.Indent();
			string strCommand = GetPMLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		/// <summary>
		/// Launches the default command if any.
		/// </summary>
		public override void LaunchDefaultCommand()
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
			bool booEmptySettings = true;
			string strBOSS = String.Empty;
			string strRegBoss = String.Empty;
			if (IntPtr.Size == 8)
				strRegBoss = @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\BOSS\";
			else
				strRegBoss = @"HKEY_LOCAL_MACHINE\SOFTWARE\BOSS\";

			if (EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameMode.ModeId) && EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("BOSS"))
			{
				strBOSS = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BOSS"];
				booEmptySettings = false;
			}

			if (String.IsNullOrEmpty(strBOSS))
				if (RegistryUtil.CanReadKey(strRegBoss))
					strBOSS = (string)Registry.GetValue(strRegBoss, "Installed Path", null);

			if (!String.IsNullOrEmpty(strBOSS))
			{
				if (booEmptySettings)
				{
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BOSS"] = strBOSS;
					EnvironmentInfo.Settings.Save();
				}
				strBOSS = Path.Combine(strBOSS, "boss.exe");
			}

			if (!String.IsNullOrWhiteSpace(strBOSS))
				if (File.Exists(strBOSS))
					return strBOSS;

			return null;
		}

		/// <summary>
		/// Gets the LOOT launch command.
		/// </summary>
		/// <returns>The LOOT launch command.</returns>
		private string GetLOOTLaunchCommand()
		{
			bool booEmptySettings = true;
			string strLOOT = String.Empty;
			string strRegLOOT = String.Empty;
			if (IntPtr.Size == 8)
				strRegLOOT = @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\LOOT\";
			else
				strRegLOOT = @"HKEY_LOCAL_MACHINE\SOFTWARE\LOOT\";

			if (EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameMode.ModeId) && EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("LOOT"))
			{
				strLOOT = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["LOOT"];
				booEmptySettings = false;
			}

			if (String.IsNullOrEmpty(strLOOT))
				if (RegistryUtil.CanReadKey(strRegLOOT))
					strLOOT = (string)Registry.GetValue(strRegLOOT, "Installed Path", null);

			if (!String.IsNullOrEmpty(strLOOT))
			{
				if (booEmptySettings)
				{
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["LOOT"] = strLOOT;
					EnvironmentInfo.Settings.Save();
				}
				strLOOT = Path.Combine(strLOOT, "LOOT.exe");
			}

			if (!String.IsNullOrWhiteSpace(strLOOT))
				if (File.Exists(strLOOT))
					return strLOOT;

			return null;
		}

		/// <summary>
		/// Gets the Wrye Bash launch command.
		/// </summary>
		/// <returns>The Wrye Bash launch command.</returns>
		private string GetWryeBashLaunchCommand()
		{
			string strWryePath = String.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("WryeBash"))
			{
				strWryePath = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["WryeBash"];
				if(!String.IsNullOrEmpty(strWryePath))
					strWryePath = Path.Combine(strWryePath, @"Wrye Bash.exe");
			}
	
			return strWryePath;
		}

		/// <summary>
		/// Gets the TES5Edit launch command.
		/// </summary>
		/// <returns>The TES5Edit launch command.</returns>
		private string GetTES5EditLaunchCommand()
		{
			string strTES5Edit = String.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("TES5Edit"))
			{
				strTES5Edit = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["TES5Edit"];
				if (!String.IsNullOrEmpty(strTES5Edit))
					strTES5Edit = Path.Combine(strTES5Edit, @"TES5Edit.exe");
			}

			return strTES5Edit;
		}

		/// <summary>
		/// Gets the FNIS launch command.
		/// </summary>
		/// <returns>The FNIS launch command.</returns>
		private string GetFNISLaunchCommand()
		{
			string strFNIS = String.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("FNIS"))
			{
				strFNIS = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["FNIS"];
				if (!String.IsNullOrEmpty(strFNIS))
					strFNIS = Path.Combine(strFNIS, @"GenerateFNISforUsers.exe");
			}

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("FNIS"))
				strFNIS = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["FNIS"];

			if (String.IsNullOrEmpty(strFNIS))
			{
				string strFNISPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\tools\GenerateFNIS_for_Users");
				if (Directory.Exists(strFNISPath))
					strFNIS = strFNISPath;
			}
			if (!String.IsNullOrEmpty(strFNIS))
				strFNIS = Path.Combine(strFNIS, "GenerateFNISforUsers.exe");

			return strFNIS;
		}

		/// <summary>
		/// Gets the BodySlide 2 launch command.
		/// </summary>
		/// <returns>The BodySlide 2 launch command.</returns>
		private string GetBS2LaunchCommand()
		{
			string strBS2 = String.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("BS2"))
			{
				strBS2 = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BS2"];
				if (!String.IsNullOrEmpty(strBS2))
					strBS2 = Path.Combine(strBS2, @"BodySlide.exe");
			}

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("BS2"))
				strBS2 = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BS2"];

			if (String.IsNullOrEmpty(strBS2))
			{
				string strBS2Path = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\CalienteTools\BodySlide");
				if (Directory.Exists(strBS2Path))
					strBS2 = strBS2Path;
			}
			if (!String.IsNullOrEmpty(strBS2))
				strBS2 = Path.Combine(strBS2, "BodySlide.exe");

			return strBS2;
		}

		/// <summary>
		/// Gets the Dual Sheat Redux Patch launch command.
		/// </summary>
		/// <returns>The Dual Sheat Redux Patch launch command.</returns>
		private string GetDSRPLaunchCommand()
		{
			string strDSRP = String.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("DSRP"))
			{
				strDSRP = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["DSRP"];
				if (!String.IsNullOrEmpty(strDSRP))
					strDSRP = Path.Combine(strDSRP, @"Dual Sheath Redux Patch.jar");
			}

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("DSRP"))
				strDSRP = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["DSRP"];

			if (String.IsNullOrEmpty(strDSRP))
			{
				string strDSRPPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\SkyProc Patchers\Dual Sheath Redux Patch");
				if (Directory.Exists(strDSRPPath))
					strDSRP = strDSRPPath;
			}
			if (!String.IsNullOrEmpty(strDSRP))
				strDSRP = Path.Combine(strDSRP, "Dual Sheath Redux Patch.jar");

			return strDSRP;
		}

		/// <summary>
		/// Gets the Patchus Maximus launch command.
		/// </summary>
		/// <returns>The Patchus Maximus launch command.</returns>
		private string GetPMLaunchCommand()
		{
			string strPM = String.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("PM"))
			{
				strPM = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["PM"];
				if (!String.IsNullOrEmpty(strPM))
					strPM = Path.Combine(strPM, @"PatchusMaximus.jar");
			}

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("PM"))
				strPM = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["PM"];

			if (String.IsNullOrEmpty(strPM))
			{
				string strPMPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\SkyProc Patchers\T3nd0_PatchusMaximus");
				if (Directory.Exists(strPMPath))
					strPM = strPMPath;
			}
			if (!String.IsNullOrEmpty(strPM))
				strPM = Path.Combine(strPM, "PatchusMaximus.jar");

			return strPM;
		}
		#endregion
	}
}
