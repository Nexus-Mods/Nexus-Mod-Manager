using System;
using System.IO;
using System.Collections.Generic;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Util.Collections;


namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Performs a standard mod uninstallation.
	/// </summary>
	/// <remarks>
	/// A basic uninstall uninstalls all of the changes made when the mod was installed.
	/// </remarks>
	public class BasicUninstallTask : BackgroundTask
	{
		protected ReadOnlyObservableList<IMod> ActiveMods { get; set; }
		private bool boo_CheckUninstallError = false;
		public string strPopupErrorMessageType = string.Empty;

		#region Properties

		/// <summary>
		/// Gets or sets the mod being installed.
		/// </summary>
		/// <value>The mod being installed.</value>
		protected IMod Mod { get; set; }

		/// <summary>
		/// Gets the installer group to use to install mod items.
		/// </summary>
		/// <value>The installer group to use to install mod items.</value>
		protected InstallerGroup Installers { get; private set; }

		/// <summary>
		/// Gets the install log that tracks mod install info
		/// for the current game mode.
		/// </summary>
		/// <value>The install log that tracks mod install info
		/// for the current game mode.</value>
		protected IInstallLog ModInstallLog { get; private set; }

 		/// <summary>
		/// Gets the current virtual mod activator.
		/// </summary>
		/// <value>The current virtual mod activator.</value>
		protected IVirtualModActivator VirtualModActivator { get; private set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The the current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
		/// <param name="p_ilgModInstallLog">The install log that tracks mod install info
		/// for the current game mode</param>
		/// <param name="p_gmdGameMode">The the current game mode.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		public BasicUninstallTask(IMod p_modMod, IVirtualModActivator p_ivaVirtualModActivator, InstallerGroup p_igpInstallers, IInstallLog p_ilgModInstallLog, IGameMode p_gmdGameMode, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			Mod = p_modMod;
			VirtualModActivator = p_ivaVirtualModActivator;
			Installers = p_igpInstallers;
			ModInstallLog = p_ilgModInstallLog;
			GameMode = p_gmdGameMode;
			ActiveMods = p_rolActiveMods;
		}

		#endregion

		/// <summary>
		/// Runs the uninstall task.
		/// </summary>
		/// <returns><c>true</c> if the mod was successfully
		/// uninstalled; <c>false</c> otherwise.</returns>
		public bool Execute()
		{
			bool booSuccess = UninstallFiles();
			Status = Status == TaskStatus.Cancelling ? TaskStatus.Cancelled : TaskStatus.Complete;
			OnTaskEnded(booSuccess);
			return booSuccess;
		}

		/// <summary>
		/// Performs the actual uninstallation.
		/// </summary>
		/// <returns><c>true</c> if the uninstall was successful;
		/// <c>false</c> otherwise.</returns>
		protected bool UninstallFiles()
		{
			bool booSecondaryInstall = false;
			OverallMessage = "Uninstalling Mod...";
			ShowItemProgress = true;
			OverallProgressStepSize = 1;
			ItemProgressStepSize = 1;

			IList<string> lstFiles = ModInstallLog.GetInstalledModFiles(Mod);
			IList<IniEdit> lstIniEdits = ModInstallLog.GetInstalledIniEdits(Mod);
			IList<string> lstGameSpecificValueEdits = ModInstallLog.GetInstalledGameSpecificValueEdits(Mod);
			OverallProgressMaximum = 3;

			ItemProgressMaximum = lstFiles.Count;
			ItemProgress = 0;
			ItemMessage = "Uninstalling Files...";

			if (GameMode.HasSecondaryInstallPath)
				booSecondaryInstall = GameMode.CheckSecondaryInstall(Mod);

			foreach (string strFile in lstFiles)
			{
				if (Status == TaskStatus.Cancelling)
					return false;
				if (GameMode.HasSecondaryInstallPath)
				{
					if (GameMode.CheckSecondaryUninstall(strFile))
						return false;
				}
				try
				{
					Installers.FileInstaller.UninstallDataFile(strFile);
					//VirtualModActivator.RemoveFileLink(strFile, Mod);
				}
				catch (UnauthorizedAccessException)
				{
					string strDetails = "Access to the path: " + Environment.NewLine + strFile + " is denied." + Environment.NewLine +
										"This error commonly occurs when Antivirus programs (even if disabled) prevents NMM from interacting with game/mod files." + Environment.NewLine +
										"Please add NMM and its folders to the antivirus' exception list.";
					Installers.FileInstaller.InstallErrors.Add(strDetails);
					strPopupErrorMessageType = "Error";
					return false;
				}
				catch (IllegalFilePathException)
				{
					string strDetails = Environment.NewLine +
										"The mod has been deleted with success, but the manager was unable to remove one or more files. " + Environment.NewLine +
										"An IllegalFilePathException was thrown, a path is safe to be written to if it contains no charaters disallowed by the operating system and if it is in the Data directory or one of its sub-directories." + Environment.NewLine;
					if (!boo_CheckUninstallError)
					{
						Installers.FileInstaller.InstallErrors.Add(strDetails);
						boo_CheckUninstallError = true;
					}
					strPopupErrorMessageType = "Warning";
				}
				catch (NullReferenceException ex)
				{
					string strDetails = ex.Message;
					Installers.FileInstaller.InstallErrors.Add(strFile);
					strPopupErrorMessageType = "Error";
					return false;
				}
				catch(Exception ex)
				{
					string strDetails = ex.Message;
					Installers.FileInstaller.InstallErrors.Add(strDetails);
					strPopupErrorMessageType = "Error";
					return false;
				}
				StepItemProgress();
			}

			//VirtualModActivator.SaveList();
			StepOverallProgress();

			if (Installers.FileInstaller.InstallErrors.Count > 0)
			{
				if (!boo_CheckUninstallError)
				{
					string strDetails = Environment.NewLine + "The mod has been deleted with success, but the manager was unable to remove one or more files. " + Environment.NewLine;
					Installers.FileInstaller.InstallErrors.Add(strDetails);
					boo_CheckUninstallError = true;
				}
				strPopupErrorMessageType = "Warning";
			}

			ItemProgressMaximum = lstIniEdits.Count;
			ItemProgress = 0;
			ItemMessage = "Undoing Ini Edits...";
			foreach (IniEdit iniEdit in lstIniEdits)
			{
				if (Status == TaskStatus.Cancelling)
					return false;
				if (File.Exists(iniEdit.File))
					Installers.IniInstaller.UneditIni(iniEdit.File, iniEdit.Section, iniEdit.Key);
				StepItemProgress();
			}
			StepOverallProgress();

			ItemProgressMaximum = lstGameSpecificValueEdits.Count;
			ItemProgress = 0;
			ItemMessage = "Undoing Game Specific Value Edits...";
			foreach (string strEdit in lstGameSpecificValueEdits)
			{
				if (Status == TaskStatus.Cancelling)
					return false;
				Installers.GameSpecificValueInstaller.UnEditGameSpecificValue(strEdit);
				StepItemProgress();
			}
			StepOverallProgress();

			if (GameMode.RequiresModFileMerge)
				GameMode.ModFileMerge(ActiveMods, Mod, true);

			return true;
		}
	}
}
