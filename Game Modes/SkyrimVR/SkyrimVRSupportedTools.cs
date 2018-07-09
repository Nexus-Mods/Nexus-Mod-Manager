namespace Nexus.Client.Games.SkyrimVR
{
    using System;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Windows.Forms;

    using Nexus.Client.Commands;
    using Nexus.Client.Util;
    
    using Microsoft.Win32;

    /// <summary>
    /// Launches SkyrimSE.
    /// </summary>
    public class SkyrimVRSupportedTools : SupportedToolsLauncherBase
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		public SkyrimVRSupportedTools(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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
			Image imgIcon;

			ClearLaunchCommands();

			var strCommand = GetBOSSLaunchCommand();
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

			strCommand = GetSSEEditLaunchCommand();
			Trace.TraceInformation("SSEEdit Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("SSEEdit", "Launch SSEEdit", "Launches SSEEdit.", imgIcon, LaunchSSEEdit, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#SSEEdit", "Config SSEEdit", "Configures SSEEdit.", imgIcon, ConfigSSEEdit, true));
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
			var strCommand = GetBOSSLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		private void LaunchLOOT()
		{
			Trace.TraceInformation("Launching LOOT");
			Trace.Indent();
			var strCommand = GetLOOTLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
            Launch(strCommand, null);
        }

		private void LaunchWryeBash()
		{
			Trace.TraceInformation("Launching Wrye Bash");
			Trace.Indent();
			var strCommand = GetWryeBashLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		private void LaunchSSEEdit()
		{
			Trace.TraceInformation("Launching SSEEdit");
			Trace.Indent();
			var strCommand = GetSSEEditLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		private void LaunchFNIS()
		{
			Trace.TraceInformation("Launching FNIS");
			Trace.Indent();
			var strCommand = GetFNISLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		private void LaunchBS()
		{
			Trace.TraceInformation("Launching BodySlide");
			Trace.Indent();
			var strCommand = GetBSLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		private void LaunchDSRP()
		{
			Trace.TraceInformation("Launching Dual Sheat Redux Patch");
			Trace.Indent();
			var strCommand = GetDSRPLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		private void LaunchPM()
		{
			Trace.TraceInformation("Launching Patchus Maximus");
			Trace.Indent();
			var strCommand = GetPMLaunchCommand();
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
			var strCommand = GetFNISLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		/// <summary>
		/// Gets the BOSS launch command.
		/// </summary>
		/// <returns>The BOSS launch command.</returns>
		private string GetBOSSLaunchCommand()
		{
			string strBOSS = string.Empty;
			string strRegBoss = string.Empty;
		    if (IntPtr.Size == 8)
		    {
		        strRegBoss = @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\BOSS\";
		    }
		    else
		    {
		        strRegBoss = @"HKEY_LOCAL_MACHINE\SOFTWARE\BOSS\";
		    }

			if (EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameMode.ModeId) && EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("BOSS"))
			{
				strBOSS = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BOSS"];
				if (!string.IsNullOrWhiteSpace(strBOSS) && ((strBOSS.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strBOSS)))
				{
					strBOSS = string.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BOSS"] = string.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (string.IsNullOrEmpty(strBOSS))
				if (RegistryUtil.CanReadKey(strRegBoss))
				{
					string strRegPath = (string)Registry.GetValue(strRegBoss, "Installed Path", null);
					if (!String.IsNullOrWhiteSpace(strRegPath) && ((strRegPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strRegPath)))
					{
						strBOSS = string.Empty;
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BOSS"] = strBOSS;
						EnvironmentInfo.Settings.Save();
					}
					else
					{
					    strBOSS = strRegPath;
					}
				}

		    if (!string.IsNullOrWhiteSpace(strBOSS))
		    {
		        strBOSS = Path.Combine(strBOSS, "boss.exe");
		    }

			return strBOSS;
		}

		/// <summary>
		/// Gets the LOOT launch command.
		/// </summary>
		/// <returns>The LOOT launch command.</returns>
		private string GetLOOTLaunchCommand()
		{
			var strLOOT = string.Empty;
			var strRegLOOT = string.Empty;

		    if (IntPtr.Size == 8)
		    {
		        strRegLOOT = @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\LOOT\";
		    }
		    else
		    {
		        strRegLOOT = @"HKEY_LOCAL_MACHINE\SOFTWARE\LOOT\";
		    }

			if (EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameMode.ModeId) && EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("LOOT"))
			{
				strLOOT = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["LOOT"];

			    if (!string.IsNullOrWhiteSpace(strLOOT) && ((strLOOT.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strLOOT)))
				{
					strLOOT = string.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["LOOT"] = string.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (string.IsNullOrEmpty(strLOOT))
			{
			    if (RegistryUtil.CanReadKey(strRegLOOT))
				{
					var strRegPath = (string)Registry.GetValue(strRegLOOT, "Installed Path", null);

				    if (!string.IsNullOrWhiteSpace(strRegPath) && ((strRegPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strRegPath)))
					{
						strLOOT = string.Empty;
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["LOOT"] = strLOOT;
						EnvironmentInfo.Settings.Save();
					}
					else
					{
					    strLOOT = strRegPath;
					}
				}
			}

		    if (!string.IsNullOrWhiteSpace(strLOOT))
		    {
		        strLOOT = Path.Combine(strLOOT, "LOOT.exe");
		    }
		
			return strLOOT;
		}

		/// <summary>
		/// Gets the Wrye Bash launch command.
		/// </summary>
		/// <returns>The Wrye Bash launch command.</returns>
		private string GetWryeBashLaunchCommand()
		{
			var strWryePath = string.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("WryeBash"))
			{
				strWryePath = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["WryeBash"];
			    if (!string.IsNullOrEmpty(strWryePath))
			    {
			        strWryePath = Path.Combine(strWryePath, @"Wrye Bash.exe");
			    }
			}
	
			return strWryePath;
		}

		/// <summary>
		/// Gets the SSEEdit launch command.
		/// </summary>
		/// <returns>The SSEEdit launch command.</returns>
		private string GetSSEEditLaunchCommand()
		{
			var strSSEEdit = string.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("SSEEdit"))
			{
				strSSEEdit = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["SSEEdit"];
			    if (!string.IsNullOrEmpty(strSSEEdit))
			    {
			        strSSEEdit = Path.Combine(strSSEEdit, @"SSEEdit.exe");
			    }
			}

			return strSSEEdit;
		}

		/// <summary>
		/// Gets the FNIS launch command.
		/// </summary>
		/// <returns>The FNIS launch command.</returns>
		private string GetFNISLaunchCommand()
		{
			var strFNIS = string.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("FNIS"))
			{
				strFNIS = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["FNIS"];

			    if (!string.IsNullOrWhiteSpace(strFNIS) && ((strFNIS.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strFNIS)))
				{
					strFNIS = string.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["FNIS"] = string.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (string.IsNullOrEmpty(strFNIS))
			{
				var strFNISPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\tools\GenerateFNIS_for_Users");

			    if (Directory.Exists(strFNISPath))
				{
					strFNIS = strFNISPath;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["FNIS"] = strFNIS;
					EnvironmentInfo.Settings.Save();
				}
			}

		    if (!string.IsNullOrEmpty(strFNIS))
		    {
		        strFNIS = Path.Combine(strFNIS, "GenerateFNISforUsers.exe");
		    }

			return strFNIS;
		}

		/// <summary>
		/// Gets the BodySlide launch command.
		/// </summary>
		/// <returns>The BodySlide launch command.</returns>
		private string GetBSLaunchCommand()
		{
			var strBS = string.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("BS2"))
			{
				strBS = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BS2"];

			    if (!string.IsNullOrWhiteSpace(strBS) && ((strBS.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strBS)))
				{
					strBS = string.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BS2"] = string.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (string.IsNullOrEmpty(strBS))
			{
				var strBSPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\CalienteTools\BodySlide");

			    if (Directory.Exists(strBSPath))
				{
					strBS = strBSPath;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BS2"] = strBS;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (!string.IsNullOrEmpty(strBS))
			{
				var str64bit = Path.Combine(strBS, "BodySlide x64.exe");

			    if (Environment.Is64BitProcess && File.Exists(str64bit))
			    {
			        strBS = str64bit;
			    }
			    else
			    {
			        strBS = Path.Combine(strBS, "BodySlide.exe");
			    }
			}

			return strBS;
		}

		/// <summary>
		/// Gets the Dual Sheat Redux Patch launch command.
		/// </summary>
		/// <returns>The Dual Sheat Redux Patch launch command.</returns>
		private string GetDSRPLaunchCommand()
		{
			var strDSRP = string.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("DSRP"))
			{
				strDSRP = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["DSRP"];

			    if (!string.IsNullOrWhiteSpace(strDSRP) && ((strDSRP.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strDSRP)))
				{
					strDSRP = string.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["DSRP"] = string.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (string.IsNullOrEmpty(strDSRP))
			{
				var strDSRPPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\SkyProc Patchers\Dual Sheath Redux Patch");

			    if (Directory.Exists(strDSRPPath))
				{
					strDSRP = strDSRPPath;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["DSRP"] = strDSRP;
					EnvironmentInfo.Settings.Save();
				}
			}

		    if (!string.IsNullOrEmpty(strDSRP))
		    {
		        strDSRP = Path.Combine(strDSRP, "Dual Sheath Redux Patch.jar");
		    }

			return strDSRP;
		}

		/// <summary>
		/// Gets the Patchus Maximus launch command.
		/// </summary>
		/// <returns>The Patchus Maximus launch command.</returns>
		private string GetPMLaunchCommand()
		{
			var strPM = string.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("PM"))
			{
				strPM = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["PM"];

			    if (!string.IsNullOrWhiteSpace(strPM) && ((strPM.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strPM)))
				{
					strPM = string.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["PM"] = string.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (string.IsNullOrEmpty(strPM))
			{
				var strPMPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\SkyProc Patchers\T3nd0_PatchusMaximus");

			    if (Directory.Exists(strPMPath))
				{
					strPM = strPMPath;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["PM"] = strPM;
					EnvironmentInfo.Settings.Save();
				}
			}

		    if (!string.IsNullOrEmpty(strPM))
		    {
		        strPM = Path.Combine(strPM, "PatchusMaximus.jar");
		    }

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
		    {
		        return;
		    }

			switch (p_strCommandID)
			{
				case "LOOT":
					ConfigLOOT();
					break;

				case "BS2":
					ConfigBS();
					break;

				case "SSEEdit":
					ConfigSSEEdit();
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
			}
		}

		private void ConfigBOSS()
		{
			var p_strToolName = "BOSS";
			var p_strExecutableName = "BOSS.exe";
			var p_strToolID = "BOSS";
			Trace.TraceInformation($"Configuring {p_strToolName}");
			Trace.Indent();

		    var fbd = new FolderBrowserDialog
		    {
		        Description = $"Select the folder where the {p_strToolName} executable is located.",
		        ShowNewFolderButton = false
		    };

		    fbd.ShowDialog();

			var strPath = fbd.SelectedPath;

			if (!string.IsNullOrEmpty(strPath))
			{
			    if (Directory.Exists(strPath))
				{
					var strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
			}
		}

		private void ConfigSSEEdit()
		{
			var p_strToolName = "SSEEdit";
			var p_strExecutableName = "SSEEdit.exe";
			var p_strToolID = "SSEEdit";
			Trace.TraceInformation($"Configuring {p_strToolName}");
			Trace.Indent();

		    var fbd = new FolderBrowserDialog
		    {
		        Description = $"Select the folder where the {p_strToolName} executable is located.",
		        ShowNewFolderButton = false
		    };

		    fbd.ShowDialog();

			var strPath = fbd.SelectedPath;

			if (!string.IsNullOrEmpty(strPath))
			{
			    if (Directory.Exists(strPath))
				{
					var strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
			}
		}

		private void ConfigLOOT()
		{
			var p_strToolName = "LOOT";
			var p_strExecutableName = "LOOT.exe";
			var p_strToolID = "LOOT";
			Trace.TraceInformation($"Configuring {p_strToolName}");
			Trace.Indent();

		    FolderBrowserDialog fbd = new FolderBrowserDialog
		    {
		        Description = $"Select the folder where the {p_strToolName} executable is located.",
		        ShowNewFolderButton = false
		    };

		    fbd.ShowDialog();

			var strPath = fbd.SelectedPath;

			if (!string.IsNullOrEmpty(strPath))
			{
			    if (Directory.Exists(strPath))
				{
					var strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
			}
		}

		private void ConfigWryeBash()
		{
			var p_strToolName = "WryeBash";
			var p_strExecutableName = "Wrye Bash.exe";
			var p_strToolID = "WryeBash";
			Trace.TraceInformation($"Configuring {p_strToolName}");
			Trace.Indent();

		    var fbd = new FolderBrowserDialog
		    {
		        Description = $"Select the folder where the {p_strToolName} executable is located.",
		        ShowNewFolderButton = false
		    };

		    fbd.ShowDialog();

			var strPath = fbd.SelectedPath;

			if (!string.IsNullOrEmpty(strPath))
            {
                if (Directory.Exists(strPath))
				{
					var strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
            }
        }

		private void ConfigFNIS()
		{
			var p_strToolName = "FNIS";
			var p_strExecutableName = "GenerateFNISforUsers.exe";
			var p_strToolID = "FNIS";
			Trace.TraceInformation($"Configuring {p_strToolName}");
			Trace.Indent();

		    FolderBrowserDialog fbd = new FolderBrowserDialog
		    {
		        Description = $"Select the folder where the {p_strToolName} executable is located.",
		        ShowNewFolderButton = false
		    };

		    fbd.ShowDialog();

			var strPath = fbd.SelectedPath;

			if (!string.IsNullOrEmpty(strPath))
            {
                if (Directory.Exists(strPath))
				{
					var strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
            }
        }

		private void ConfigBS()
		{
			var p_strToolName = "BS2";
			var p_strExecutableName = "BodySlide.exe";
			var p_strToolID = "BS2";
			Trace.TraceInformation($"Configuring {p_strToolName}");
			Trace.Indent();

		    var fbd = new FolderBrowserDialog
		    {
		        Description = $"Select the folder where the {p_strToolName} executable is located.",
		        ShowNewFolderButton = false
		    };

		    fbd.ShowDialog();

			var strPath = fbd.SelectedPath;

			if (!string.IsNullOrEmpty(strPath))
            {
                if (Directory.Exists(strPath))
				{
					var strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
            }
        }

		private void ConfigDSRP()
		{
			var p_strToolName = "DSRP";
			var p_strExecutableName = "Dual Sheath Redux Patch.jar";
			var p_strToolID = "DSRP";
			Trace.TraceInformation($"Configuring {p_strToolName}");
			Trace.Indent();

		    var fbd = new FolderBrowserDialog
		    {
		        Description = $"Select the folder where the {p_strToolName} executable is located.",
		        ShowNewFolderButton = false
		    };

		    fbd.ShowDialog();

			var strPath = fbd.SelectedPath;

			if (!string.IsNullOrEmpty(strPath))
            {
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
        }

		private void ConfigPM()
		{
			var p_strToolName = "PM";
			var p_strExecutableName = "PatchusMaximus.jar";
			var p_strToolID = "PM";
			Trace.TraceInformation($"Configuring {p_strToolName}");
			Trace.Indent();

		    FolderBrowserDialog fbd = new FolderBrowserDialog
		    {
		        Description = $"Select the folder where the {p_strToolName} executable is located.",
		        ShowNewFolderButton = false
		    };

		    fbd.ShowDialog();

			var strPath = fbd.SelectedPath;

			if (!string.IsNullOrEmpty(strPath))
            {
                if (Directory.Exists(strPath))
				{
					var strExecutablePath = Path.Combine(strPath, p_strExecutableName);

					if (!string.IsNullOrWhiteSpace(strExecutablePath) && File.Exists(strExecutablePath))
					{
						EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][p_strToolID] = strPath;
						EnvironmentInfo.Settings.Save();
						OnChangedToolPath(new EventArgs());
					}
				}
            }
        }
		
		#endregion
	}
}
