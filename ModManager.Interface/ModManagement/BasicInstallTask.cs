using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Performs a standard mod installation.
	/// </summary>
	/// <remarks>
	/// A basic install installs all of the files in the mod to the installation directory,
	/// and activates all plugin files.
	/// </remarks>
	public class BasicInstallTask : ThreadedBackgroundTask
	{
		#region Properties

		/// <summary>
		/// Gets or sets the mod being installed.
		/// </summary>
		/// <value>The mod being installed.</value>
		protected IMod Mod { get; set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The the current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets or sets the installer to use to install files.
		/// </summary>
		/// <value>The installer to use to install files.</value>
		protected IModFileInstaller FileInstaller { get; set; }

		/// <summary>
		/// Gets the manager to use to manage plugins.
		/// </summary>
		/// <value>The manager to use to manage plugins.</value>
		protected IPluginManager PluginManager { get; private set; }

		/// <summary>
		/// Gets the virtual mod activator to use.
		/// </summary>
		/// <value>The virtual mod activator to use.</value>
		protected IVirtualModActivator VirtualModActivator { get; private set; }

		/// <summary>
		/// Gets or sets whether the installer should skip readme files.
		/// </summary>
		/// <value>Whether the installer should skip readme files.</value>
		protected bool SkipReadme { get; set; }

		protected ReadOnlyObservableList<IMod> ActiveMods { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The the current game mode.</param>
		/// <param name="p_mfiFileInstaller">The file installer to use.</param>
		/// <param name="p_pmgPluginManager">The plugin manager.</param>
		/// <param name="p_booSkipReadme">Whether to skip the installation of readme files.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		public BasicInstallTask(IMod p_modMod, IGameMode p_gmdGameMode, IModFileInstaller p_mfiFileInstaller, IPluginManager p_pmgPluginManager, IVirtualModActivator p_ivaVirtualModActivator, bool p_booSkipReadme, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			FileInstaller = p_mfiFileInstaller;
			PluginManager = p_pmgPluginManager;
			VirtualModActivator = p_ivaVirtualModActivator;
			SkipReadme = p_booSkipReadme;
			ActiveMods = p_rolActiveMods;
		}

		#endregion

		/// <summary>
		/// Runs the basic install task.
		/// </summary>
		/// <returns><c>true</c> if the installation succeed;
		/// <c>false</c> otherwise.</returns>
		public bool Execute()
		{
			OverallMessage = "Installing Mod...";
			ShowItemProgress = false;
			OverallProgressStepSize = 1;
			return (bool)StartWait();
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <remarks>
		/// This method installs all of the files in the <see cref="IMod"/> being installed.
		/// </remarks>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>A return value.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			IModLinkInstaller ModLinkInstaller = VirtualModActivator.GetModLinkInstaller();
			char[] chrDirectorySeperators = new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
			List<string> lstFiles = Mod.GetFileList();
			OverallProgressMaximum = lstFiles.Count;

			if (GameMode.RequiresModFileMerge)
				GameMode.ModFileMerge(ActiveMods, Mod, false);

			foreach (string strFile in lstFiles)
			{
				if (Status == TaskStatus.Cancelling)
					return false;
				string strFixedPath = GameMode.GetModFormatAdjustedPath(Mod.Format, strFile, true);
				string strVirtualPath = Path.Combine(Path.Combine(VirtualModActivator.VirtualPath, Path.GetFileNameWithoutExtension(Mod.Filename)), strFile);

				if (!string.IsNullOrEmpty(strFixedPath))
				{
					if (!(GameMode.RequiresModFileMerge && (Path.GetFileName(strFile) == GameMode.MergedFileName)))
					{
						if (!(SkipReadme && Readme.IsValidExtension(Path.GetExtension(strFile).ToLower()) && (Path.GetDirectoryName(strFixedPath) == Path.GetFileName(GameMode.PluginDirectory))))
						{
							FileInstaller.InstallFileFromMod(strFile, strVirtualPath);
							
							if (!VirtualModActivator.DisableLinkCreation)
							{
								string strFileLink = ModLinkInstaller.AddFileLink(Mod, strFile, false);

								if (!string.IsNullOrEmpty(strFileLink))
									if (FileInstaller.PluginCheck(strFileLink, false))
										if (PluginManager.IsActivatiblePluginFile(strFileLink))
											PluginManager.ActivatePlugin(strFileLink);
							}
						}
					}
				}
				StepOverallProgress();
			}
			VirtualModActivator.SaveList();
			return true;
		}
	}
}
