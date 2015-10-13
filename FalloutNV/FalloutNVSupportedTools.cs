using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;
using Microsoft.Win32;

namespace Nexus.Client.Games.FalloutNV
{
	/// <summary>
	/// Launches FalloutNV.
	/// </summary>
	public class FalloutNVSupportedTools : SupportedToolsLauncherBase
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		public FalloutNVSupportedTools(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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
			if (strCommand != null)
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
				if (!String.IsNullOrWhiteSpace(strBOSS) && (strBOSS.IndexOfAny(Path.GetInvalidPathChars()) >= 0))
				{
					strBOSS = String.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["BOSS"] = String.Empty;
					EnvironmentInfo.Settings.Save();
				}
				else
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
				if (!String.IsNullOrWhiteSpace(strLOOT) && (strLOOT.IndexOfAny(Path.GetInvalidPathChars()) >= 0))
				{
					strLOOT = String.Empty;
					EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["LOOT"] = String.Empty;
					EnvironmentInfo.Settings.Save();
				}
				else
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

		#endregion
	}
}
