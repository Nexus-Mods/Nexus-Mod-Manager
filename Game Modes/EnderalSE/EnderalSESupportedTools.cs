using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using System.Windows.Forms;

namespace Nexus.Client.Games.EnderalSE
{
	/// <summary>
	/// Launches EnderalSE.
	/// </summary>
	public class EnderalSESupportedTools : SupportedToolsLauncherBase
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		public EnderalSESupportedTools(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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

			string strCommand = GetEnderalSEEditLaunchCommand();
			Trace.TraceInformation("EnderalSEEdit Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if ((strCommand != null) && (File.Exists(strCommand)))
			{
				imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
				AddLaunchCommand(new Command("EnderalSEEdit", "Launch EnderalSEEdit", "Launches EnderalSEEdit.", imgIcon, LaunchEnderalSEEdit, true));
			}
			else
			{
				imgIcon = null;
				AddLaunchCommand(new Command("Config#EnderalSEEdit", "Config EnderalSEEdit", "Configures EnderalSEEdit.", imgIcon, ConfigEnderalSEEdit, true));
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

			Trace.Unindent();
		}

		#region Launch Commands

		private void LaunchEnderalSEEdit()
		{
			Trace.TraceInformation("Launching EnderalSEEdit");
			Trace.Indent();
			string strCommand = GetEnderalSEEditLaunchCommand();
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

		/// <summary>
		/// Launches the default command if any.
		/// </summary>
		public override void LaunchDefaultCommand()
		{
			Trace.TraceInformation("Launching EnderalSEEdit");
			Trace.Indent();
			string strCommand = GetEnderalSEEditLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		/// <summary>
		/// Gets the EnderalSEEdit launch command.
		/// </summary>
		/// <returns>The EnderalSEEdit launch command.</returns>
		private string GetEnderalSEEditLaunchCommand()
		{
			string strEnderalSEEdit = String.Empty;

			if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey("EnderalSEEdit"))
			{
				strEnderalSEEdit = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId]["EnderalSEEdit"];
				if (!String.IsNullOrEmpty(strEnderalSEEdit))
					strEnderalSEEdit = Path.Combine(strEnderalSEEdit, @"EnderalSEEdit.exe");
			}

			return strEnderalSEEdit;
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
				case "BS2":
					ConfigBS();
					break;

				case "EnderalSEEdit":
					ConfigEnderalSEEdit();
					break;

				default:
					break;
			}
		}

		private void ConfigEnderalSEEdit()
		{
			string p_strToolName = "EnderalSEEdit";
			string p_strExecutableName = "EnderalSEEdit.exe";
			string p_strToolID = "EnderalSEEdit";
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
		
		#endregion
	}
}
