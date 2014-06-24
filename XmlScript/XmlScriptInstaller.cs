using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.ModManagement.Scripting.XmlScript
{
	/// <summary>
	/// Performs the mod installation based on the XML script.
	/// </summary>
	public class XmlScriptInstaller : BackgroundTask
	{
		#region Properties

		/// <summary>
		/// Gets the mod for which the script is running.
		/// </summary>
		/// <value>The mod for which the script is running.</value>
		protected IMod Mod { get; private set; }

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the installer group to use to install mod items.
		/// </summary>
		/// <value>The installer group to use to install mod items.</value>
		protected InstallerGroup Installers { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the required dependencies.
		/// </summary>
		/// <param name="p_modMod">The mod for which the script is running.</param>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
		public XmlScriptInstaller(IMod p_modMod, IGameMode p_gmdGameMode, InstallerGroup p_igpInstallers)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			Installers = p_igpInstallers;
		}

		#endregion

		/// <summary>
		/// Performs the mod installation based on the XML script.
		/// </summary>
		/// <param name="p_strModName">The name of the mod whose script in executing.</param>
		/// <param name="p_xscScript">The script that is executing.</param>
		/// <param name="p_csmStateManager">The state manager managing the install state.</param>
		/// <param name="p_colFilesToInstall">The list of files to install.</param>
		/// <param name="p_colPluginsToActivate">The list of plugins to activate.</param>
		/// <returns><c>true</c> if the installation succeeded;
		/// <c>false</c> otherwise.</returns>
		public bool Install(string p_strModName, XmlScript p_xscScript, ConditionStateManager p_csmStateManager, ICollection<InstallableFile> p_colFilesToInstall, ICollection<InstallableFile> p_colPluginsToActivate)
		{
			OverallMessage = String.Format("Installing {0}", p_strModName);
			OverallProgressStepSize = 1;
			ItemProgressStepSize = 1;
			ShowItemProgress = true;
			bool booSuccess = false;
			try
			{
				booSuccess = InstallFiles(p_xscScript, p_csmStateManager, p_colFilesToInstall, p_colPluginsToActivate);
				Status = Status == TaskStatus.Cancelling ? TaskStatus.Cancelled : TaskStatus.Complete;
			}
			catch
			{
				booSuccess = false;
				Status = TaskStatus.Error;
			}
			OnTaskEnded(booSuccess);
			return booSuccess;
		}

		/// <summary>
		/// Installs and activates files are required. This method is used by the background worker.
		/// </summary>
		/// <param name="p_scpScript">The XMl Script to execute.</param>
		protected bool InstallFiles(XmlScript p_xscScript, ConditionStateManager p_csmStateManager, ICollection<InstallableFile> p_colFilesToInstall, ICollection<InstallableFile> p_colPluginsToActivate)
		{
			IList<InstallableFile> lstRequiredFiles = p_xscScript.RequiredInstallFiles;
			IList<ConditionallyInstalledFileSet> lstConditionallyInstalledFileSets = p_xscScript.ConditionallyInstalledFileSets;
			OverallProgressMaximum = lstRequiredFiles.Count + p_colFilesToInstall.Count + lstConditionallyInstalledFileSets.Count;

			foreach (InstallableFile iflRequiredFile in lstRequiredFiles)
			{
				if (Status == TaskStatus.Cancelling)
					return false;
				if (!InstallFile(iflRequiredFile, true))
					return false;
				StepOverallProgress();
			}

			foreach (InstallableFile ilfFile in p_colFilesToInstall)
			{
				if (Status == TaskStatus.Cancelling)
					return false;
				if (!InstallFile(ilfFile, p_colPluginsToActivate.Contains(ilfFile)))
					return false;
				StepOverallProgress();
			}

			foreach (ConditionallyInstalledFileSet cisFileSet in lstConditionallyInstalledFileSets)
			{
				if (cisFileSet.Condition.GetIsFulfilled(p_csmStateManager))
					foreach (InstallableFile ilfFile in cisFileSet.Files)
					{
						if (Status == TaskStatus.Cancelling)
							return false;
						if (!InstallFile(ilfFile, true))
							return false;
					}
				StepOverallProgress();
			}
			return true;
		}

		/// <summary>
		/// Installs the given <see cref="InstallableFile"/>, and activates any
		/// plugins it encompasses as requested.
		/// </summary>
		/// <param name="p_ilfFile">The file to install.</param>
		/// <param name="p_booActivate">Whether or not to activate the given file, if it is a plugin.</param>
		/// <returns><c>false</c> if the user cancelled the install;
		/// <c>true</c> otherwise.</returns>
		protected bool InstallFile(InstallableFile p_ilfFile, bool p_booActivate)
		{
			string strSource = p_ilfFile.Source;
			string strDest = p_ilfFile.Destination;
			ItemMessage = "Installing " + (String.IsNullOrEmpty(strDest) ? strSource : strDest);
			if (p_ilfFile.IsFolder)
			{
				if (!InstallFolderFromMod(p_ilfFile))
					return false;

				//if the destination length is greater than 0, then nothing in
				// this folder is directly in the Data folder as so cannot be
				// activated
				if (strDest.Length == 0)
				{
					List<string> lstFiles = Mod.GetFileList(strSource, true);
					ItemMessage = "Activating " + (String.IsNullOrEmpty(strDest) ? strSource : strDest);
					ItemProgress = 0;
					ItemProgressMaximum = lstFiles.Count;

					if (!strSource.EndsWith("/"))
						strSource += "/";
					foreach (string strFile in lstFiles)
					{
						string strNewFileName = GameMode.GetModFormatAdjustedPath(Mod.Format, strFile.Substring(strSource.Length, strFile.Length - strSource.Length));
						if (Installers.PluginManager != null)
							if (Installers.PluginManager.IsActivatiblePluginFile(strNewFileName))
								Installers.PluginManager.SetPluginActivation(strNewFileName, p_booActivate);
						if (Status == TaskStatus.Cancelling)
							return false;
						StepItemProgress();
					}
				}
			}
			else
			{
				ItemProgress = 0;
				ItemProgressMaximum = 2;

				Installers.FileInstaller.InstallFileFromMod(strSource, GameMode.GetModFormatAdjustedPath(Mod.Format, strDest), false);

				StepItemProgress();

				string strPluginPath = GameMode.GetModFormatAdjustedPath(Mod.Format, String.IsNullOrEmpty(strDest) ? strSource : strDest);
				if (Installers.PluginManager != null)
					if (Installers.PluginManager.IsActivatiblePluginFile(strPluginPath))
						Installers.PluginManager.SetPluginActivation(strPluginPath, p_booActivate);

				StepItemProgress();
			}
			return true;
		}

		#region Helper Methods

		/// <summary>
		/// Recursively copies all files and folders from one location to another.
		/// </summary>
		/// <param name="p_ilfFile">The folder to install.</param>
		/// <returns><c>false</c> if the user cancelled the install;
		/// <c>true</c> otherwise.</returns>
		protected bool InstallFolderFromMod(InstallableFile p_ilfFile)
		{
			List<string> lstModFiles = Mod.GetFileList(p_ilfFile.Source, true);
			ItemProgress = 0;
			ItemProgressMaximum = lstModFiles.Count;

			String strFrom = p_ilfFile.Source.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
			if (!strFrom.EndsWith(Path.DirectorySeparatorChar.ToString()))
				strFrom += Path.DirectorySeparatorChar;
			String strTo = p_ilfFile.Destination.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			if ((strTo.Length > 0) && (!strTo.EndsWith(Path.DirectorySeparatorChar.ToString())))
				strTo += Path.DirectorySeparatorChar;
			String strMODFile = null;
			for (Int32 i = 0; i < lstModFiles.Count; i++)
			{
				if (Status == TaskStatus.Cancelling)
					return false;

				strMODFile = lstModFiles[i];
				string strNewFileName = strMODFile.Substring(strFrom.Length, strMODFile.Length - strFrom.Length);
				Installers.FileInstaller.InstallFileFromMod(strMODFile, GameMode.GetModFormatAdjustedPath(Mod.Format, Path.Combine(strTo, strNewFileName)), false);

				StepItemProgress();
			}
			return true;
		}

		#endregion
	}
}
