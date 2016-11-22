using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;
using System.Windows.Forms;
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
				AddLaunchCommand(new Command("BOSS", "Launch BOSS", "Launches BOSS.", imgIcon, LaunchBOSS, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#BOSS", "Config BOSS", "Configures BOSS.", imgIcon, ConfigBOSS, true));
			}
			
			strCommand = GetLOOTLaunchCommand();
			Trace.TraceInformation("LOOT Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("LOOT", "Launch LOOT", "Launches LOOT.", imgIcon, LaunchLOOT, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#LOOT", "Config LOOT", "Configures LOOT.", imgIcon, ConfigLOOT, true));
			}

			strCommand = GetWryeBashLaunchCommand();
			Trace.TraceInformation("Wrye Bash Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("WryeBash", "Launch Wrye Bash", "Launches Wrye Bash.", imgIcon, LaunchWryeBash, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#Wrye Bash", "Config Wrye Bash", "Configures Wrye Bash.", imgIcon, ConfigWryeBash, true));
			}

			strCommand = GetTES5EditLaunchCommand();
			Trace.TraceInformation("TES5Edit Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("TES5Edit", "Launch TES5Edit", "Launches TES5Edit.", imgIcon, LaunchTES5Edit, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#TES5Edit", "Config TES5Edit", "Configures TES5Edit.", imgIcon, ConfigTES5Edit, true));
			}

			strCommand = GetFNISLaunchCommand();
			Trace.TraceInformation("FNIS Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("FNIS", "Launch FNIS", "Launches FNIS.", imgIcon, LaunchFNIS, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#FNIS", "Config FNIS", "Configures FNIS.", imgIcon, ConfigFNIS, true));
			}

			strCommand = GetBSLaunchCommand();
			Trace.TraceInformation("BodySlide Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("BS2", "Launch BodySlide", "Launches BodySlide.", imgIcon, LaunchBS, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#BodySlide", "Config BodySlide", "Configures BodySlide.", imgIcon, ConfigBS, true));
			}

			strCommand = GetDSRPLaunchCommand();
			Trace.TraceInformation("Dual Sheat Redux Patch Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("DSRP", "Launch Dual Sheat Redux Patch", "Launches Dual Sheat Redux Patch.", imgIcon, LaunchDSRP, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#Dual Sheat Redux Patch", "Config Dual Sheat Redux Patch", "Configures Dual Sheat Redux Patch.", imgIcon, ConfigDSRP, true));
			}

			strCommand = GetPMLaunchCommand();
			Trace.TraceInformation("Patchus Maximus Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("PM", "Launch Patchus Maximus", "Launches Patchus Maximus.", imgIcon, LaunchPM, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#Patchus Maximus", "Config Patchus Maximus", "Configures Patchus Maximus.", imgIcon, ConfigPM, true));
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

		private void LaunchBS()
		{
			Trace.TraceInformation("Launching BodySlide");
			Trace.Indent();
			string strCommand = GetBSLaunchCommand();
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
			string strBOSS = String.Empty;
			string strRegBoss = String.Empty;
			if (IntPtr.Size == 8)
				strRegBoss = @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\BOSS\";
			else
				strRegBoss = @"HKEY_LOCAL_MACHINE\SOFTWARE\BOSS\";

			if (EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameMode.ModeId) && EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("BOSS"))
			{
				strBOSS = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BOSS"];
				if (!String.IsNullOrWhiteSpace(strBOSS) && ((strBOSS.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strBOSS)))
				{
					strBOSS = String.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BOSS"] = String.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (String.IsNullOrEmpty(strBOSS))
				if (RegistryUtil.CanReadKey(strRegBoss))
				{
					string strRegPath = (string)Registry.GetValue(strRegBoss, "Installed Path", null);
					if (!String.IsNullOrWhiteSpace(strRegPath) && ((strRegPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strRegPath)))
					{
						strBOSS = String.Empty;
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BOSS"] = strBOSS;
						EnvironmentInfo.Settings.Save();
					}
					else
						strBOSS = strRegPath;
				}

			if (!String.IsNullOrWhiteSpace(strBOSS))
				strBOSS = Path.Combine(strBOSS, "boss.exe");

			return strBOSS;
		}

		/// <summary>
		/// Gets the LOOT launch command.
		/// </summary>
		/// <returns>The LOOT launch command.</returns>
		private string GetLOOTLaunchCommand()
		{
			string strLOOT = String.Empty;
			string strRegLOOT = String.Empty;
			if (IntPtr.Size == 8)
				strRegLOOT = @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\LOOT\";
			else
				strRegLOOT = @"HKEY_LOCAL_MACHINE\SOFTWARE\LOOT\";

			if (EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameMode.ModeId) && EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("LOOT"))
			{
				strLOOT = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["LOOT"];
				if (!String.IsNullOrWhiteSpace(strLOOT) && ((strLOOT.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strLOOT)))
				{
					strLOOT = String.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["LOOT"] = String.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (String.IsNullOrEmpty(strLOOT))
				if (RegistryUtil.CanReadKey(strRegLOOT))
				{
					string strRegPath = (string)Registry.GetValue(strRegLOOT, "Installed Path", null);
					if (!String.IsNullOrWhiteSpace(strRegPath) && ((strRegPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strRegPath)))
					{
						strLOOT = String.Empty;
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["LOOT"] = strLOOT;
						EnvironmentInfo.Settings.Save();
					}
					else
						strLOOT = strRegPath;
				}

			if (!String.IsNullOrWhiteSpace(strLOOT))
				strLOOT = Path.Combine(strLOOT, "LOOT.exe");
		
			return strLOOT;
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
				if (!String.IsNullOrWhiteSpace(strFNIS) && ((strFNIS.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strFNIS)))
				{
					strFNIS = String.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["FNIS"] = String.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (String.IsNullOrEmpty(strFNIS))
			{
				string strFNISPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\tools\GenerateFNIS_for_Users");
				if (Directory.Exists(strFNISPath))
				{
					strFNIS = strFNISPath;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["FNIS"] = strFNIS;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (!String.IsNullOrEmpty(strFNIS))
				strFNIS = Path.Combine(strFNIS, "GenerateFNISforUsers.exe");

			return strFNIS;
		}

		/// <summary>
		/// Gets the BodySlide launch command.
		/// </summary>
		/// <returns>The BodySlide launch command.</returns>
		private string GetBSLaunchCommand()
		{
			string strBS = String.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("BS2"))
			{
				strBS = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BS2"];
				if (!String.IsNullOrWhiteSpace(strBS) && ((strBS.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strBS)))
				{
					strBS = String.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BS2"] = String.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (String.IsNullOrEmpty(strBS))
			{
				string strBSPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\CalienteTools\BodySlide");
				if (Directory.Exists(strBSPath))
				{
					strBS = strBSPath;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BS2"] = strBS;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (!String.IsNullOrEmpty(strBS))
			{
				string str64bit = Path.Combine(strBS, "BodySlide x64.exe");

				if (Environment.Is64BitProcess && File.Exists(str64bit))
					strBS = str64bit;
				else
					strBS = Path.Combine(strBS, "BodySlide.exe");
			}

			return strBS;
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
				if (!String.IsNullOrWhiteSpace(strDSRP) && ((strDSRP.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strDSRP)))
				{
					strDSRP = String.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["DSRP"] = String.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (String.IsNullOrEmpty(strDSRP))
			{
				string strDSRPPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\SkyProc Patchers\Dual Sheath Redux Patch");
				if (Directory.Exists(strDSRPPath))
				{
					strDSRP = strDSRPPath;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["DSRP"] = strDSRP;
					EnvironmentInfo.Settings.Save();
				}
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
				if (!String.IsNullOrWhiteSpace(strPM) && ((strPM.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strPM)))
				{
					strPM = String.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["PM"] = String.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (String.IsNullOrEmpty(strPM))
			{
				string strPMPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\SkyProc Patchers\T3nd0_PatchusMaximus");
				if (Directory.Exists(strPMPath))
				{
					strPM = strPMPath;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["PM"] = strPM;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (!String.IsNullOrEmpty(strPM))
				strPM = Path.Combine(strPM, "PatchusMaximus.jar");

			return strPM;
		}

		#endregion

		#region Config Commands

		/// <summary>
		/// Configures the selected command.
		/// </summary>
		public override void ConfigCommand(string p_strCommandID)
		{
			if (string.IsNullOrWhiteSpace(p_strCommandID))
				return;

			switch (p_strCommandID)
			{
				case "LOOT":
					ConfigLOOT();
					break;

				case "BS2":
					ConfigBS();
					break;

				case "TES5Edit":
					ConfigTES5Edit();
					break;

				case "WryeBash":
					ConfigWryeBash();
					break;

				case "PM":
					ConfigPM();
					break;

				case "DSRP":
					ConfigDSRP();
					break;

				case "FNIS":
					ConfigFNIS();
					break;

				default:
					break;
			}
		}

		private void ConfigBOSS()
		{
			string p_strToolName = "BOSS";
			string p_strExecutableName = "BOSS.exe";
			string p_strToolID = "BOSS";
			Trace.TraceInformation(string.Format("Configuring {0}", p_strToolName));
			Trace.Indent();

			FolderBrowserDialog fbd = new FolderBrowserDialog();
			fbd.Description = string.Format("Select the folder where the {0} executable is located.", p_strToolName);
			fbd.ShowNewFolderButton = false;

			fbd.ShowDialog();

			string strPath = fbd.SelectedPath;

			if (!String.IsNullOrEmpty(strPath))
				if (Directory.Exists(strPath))
				{
					string strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
		}

		private void ConfigTES5Edit()
		{
			string p_strToolName = "TES5Edit";
			string p_strExecutableName = "TES5Edit.exe";
			string p_strToolID = "TES5Edit";
			Trace.TraceInformation(string.Format("Configuring {0}", p_strToolName));
			Trace.Indent();

			FolderBrowserDialog fbd = new FolderBrowserDialog();
			fbd.Description = string.Format("Select the folder where the {0} executable is located.", p_strToolName);
			fbd.ShowNewFolderButton = false;

			fbd.ShowDialog();

			string strPath = fbd.SelectedPath;

			if (!String.IsNullOrEmpty(strPath))
				if (Directory.Exists(strPath))
				{
					string strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
		}

		private void ConfigLOOT()
		{
			string p_strToolName = "LOOT";
			string p_strExecutableName = "LOOT.exe";
			string p_strToolID = "LOOT";
			Trace.TraceInformation(string.Format("Configuring {0}", p_strToolName));
			Trace.Indent();

			FolderBrowserDialog fbd = new FolderBrowserDialog();
			fbd.Description = string.Format("Select the folder where the {0} executable is located.", p_strToolName);
			fbd.ShowNewFolderButton = false;

			fbd.ShowDialog();

			string strPath = fbd.SelectedPath;

			if (!String.IsNullOrEmpty(strPath))
				if (Directory.Exists(strPath))
				{
					string strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
		}

		private void ConfigWryeBash()
		{
			string p_strToolName = "WryeBash";
			string p_strExecutableName = "Wrye Bash.exe";
			string p_strToolID = "WryeBash";
			Trace.TraceInformation(string.Format("Configuring {0}", p_strToolName));
			Trace.Indent();

			FolderBrowserDialog fbd = new FolderBrowserDialog();
			fbd.Description = string.Format("Select the folder where the {0} executable is located.", p_strToolName);
			fbd.ShowNewFolderButton = false;

			fbd.ShowDialog();

			string strPath = fbd.SelectedPath;

			if (!String.IsNullOrEmpty(strPath))
				if (Directory.Exists(strPath))
				{
					string strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
		}

		private void ConfigFNIS()
		{
			string p_strToolName = "FNIS";
			string p_strExecutableName = "GenerateFNISforUsers.exe";
			string p_strToolID = "FNIS";
			Trace.TraceInformation(string.Format("Configuring {0}", p_strToolName));
			Trace.Indent();

			FolderBrowserDialog fbd = new FolderBrowserDialog();
			fbd.Description = string.Format("Select the folder where the {0} executable is located.", p_strToolName);
			fbd.ShowNewFolderButton = false;

			fbd.ShowDialog();

			string strPath = fbd.SelectedPath;

			if (!String.IsNullOrEmpty(strPath))
				if (Directory.Exists(strPath))
				{
					string strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
		}

		private void ConfigBS()
		{
			string p_strToolName = "BS2";
			string p_strExecutableName = "BodySlide.exe";
			string p_strToolID = "BS2";
			Trace.TraceInformation(string.Format("Configuring {0}", p_strToolName));
			Trace.Indent();

			FolderBrowserDialog fbd = new FolderBrowserDialog();
			fbd.Description = string.Format("Select the folder where the {0} executable is located.", p_strToolName);
			fbd.ShowNewFolderButton = false;

			fbd.ShowDialog();

			string strPath = fbd.SelectedPath;

			if (!String.IsNullOrEmpty(strPath))
				if (Directory.Exists(strPath))
				{
					string strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
		}

		private void ConfigDSRP()
		{
			string p_strToolName = "DSRP";
			string p_strExecutableName = "Dual Sheath Redux Patch.jar";
			string p_strToolID = "DSRP";
			Trace.TraceInformation(string.Format("Configuring {0}", p_strToolName));
			Trace.Indent();

			FolderBrowserDialog fbd = new FolderBrowserDialog();
			fbd.Description = string.Format("Select the folder where the {0} executable is located.", p_strToolName);
			fbd.ShowNewFolderButton = false;

			fbd.ShowDialog();

			string strPath = fbd.SelectedPath;

			if (!String.IsNullOrEmpty(strPath))
				if (Directory.Exists(strPath))
				{
					string strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
		}

		private void ConfigPM()
		{
			string p_strToolName = "PM";
			string p_strExecutableName = "PatchusMaximus.jar";
			string p_strToolID = "PM";
			Trace.TraceInformation(string.Format("Configuring {0}", p_strToolName));
			Trace.Indent();

			FolderBrowserDialog fbd = new FolderBrowserDialog();
			fbd.Description = string.Format("Select the folder where the {0} executable is located.", p_strToolName);
			fbd.ShowNewFolderButton = false;

			fbd.ShowDialog();

			string strPath = fbd.SelectedPath;

			if (!String.IsNullOrEmpty(strPath))
				if (Directory.Exists(strPath))
				{
					string strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
		}
		
		#endregion
	}
}
