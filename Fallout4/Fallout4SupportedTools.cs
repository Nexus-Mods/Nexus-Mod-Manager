using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Nexus.Client.Commands;
using Nexus.Client.Util;
using Microsoft.Win32;

namespace Nexus.Client.Games.Fallout4
{
	/// <summary>
	/// Launches Fallout4.
	/// </summary>
	public class Fallout4SupportedTools : SupportedToolsLauncherBase
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		public Fallout4SupportedTools(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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
						
			string strCommand = GetLOOTLaunchCommand();
			Trace.TraceInformation("LOOT Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("LOOTLaunch", "Launch LOOT", "Launches LOOT.", imgIcon, LaunchLOOT, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#LOOT", "Config LOOT", "Configures LOOT.", imgIcon, ConfigLOOT, true));
			}
			
			strCommand = GetF4EditLaunchCommand();
			Trace.TraceInformation("F4Edit Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("F4EditLaunch", "Launch F4Edit", "Launches F4Edit.", imgIcon, LaunchF4Edit, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#F4Edit", "Config F4Edit", "Configures F4Edit.", imgIcon, ConfigF4Edit, true));
			}
						
			strCommand = GetBS2LaunchCommand();
			Trace.TraceInformation("BodySlide 2 Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("BS2Launch", "Launch BodySlide 2", "Launches BodySlide 2.", imgIcon, LaunchBS2, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#BodySlide 2", "Config BodySlide 2", "Configures BodySlide 2.", imgIcon, ConfigBS2, true));
			}
			
			Trace.Unindent();
		}

		#region Launch Commands

		private void LaunchLOOT()
		{
			Trace.TraceInformation("Launching LOOT");
			Trace.Indent();
			string strCommand = GetLOOTLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}
				
		private void LaunchF4Edit()
		{
			Trace.TraceInformation("Launching F4Edit");
			Trace.Indent();
			string strCommand = GetF4EditLaunchCommand();
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
		
		/// <summary>
		/// Launches the default command if any.
		/// </summary>
		public override void LaunchDefaultCommand()
		{
			Trace.TraceInformation("Launching FNIS");
			Trace.Indent();
			string strCommand = GetF4EditLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
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
		/// Gets the F4Edit launch command.
		/// </summary>
		/// <returns>The F4Edit launch command.</returns>
		private string GetF4EditLaunchCommand()
		{
			string strF4Edit = String.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("F4Edit"))
			{
				strF4Edit = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["F4Edit"];
				if (!String.IsNullOrEmpty(strF4Edit))
					strF4Edit = Path.Combine(strF4Edit, @"F4Edit.exe");
			}

			return strF4Edit;
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
				if (!String.IsNullOrWhiteSpace(strBS2) && ((strBS2.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strBS2)))
				{
					strBS2 = String.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BS2"] = String.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (String.IsNullOrEmpty(strBS2))
			{
				string strBS2Path = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, @"Data\CalienteTools\BodySlide");
				if (Directory.Exists(strBS2Path))
				{
					strBS2 = strBS2Path;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BS2"] = strBS2;
					EnvironmentInfo.Settings.Save();
				}
			}

			if (!String.IsNullOrEmpty(strBS2))
			{
				string str64bit = Path.Combine(strBS2, "BodySlide x64.exe");

				if (Environment.Is64BitProcess && File.Exists(str64bit))
					strBS2 = str64bit;
				else
					strBS2 = Path.Combine(strBS2, "BodySlide.exe");
			}

			return strBS2;
		}
				
		#endregion

		#region Config Commands
				
		private void ConfigF4Edit()
		{
			string p_strToolName = "F4Edit";
			string p_strExecutableName = "F4Edit.exe";
			string p_strToolID = "F4Edit";
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

		private void ConfigBS2()
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
		
		#endregion
	}
}
