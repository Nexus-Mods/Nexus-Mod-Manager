using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Util;
using Nexus.Client.Mods;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// This deletes mods.
	/// </summary>
	public class ModDeleter : ModUninstaller
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_ilgModInstallLog">The install log that tracks mod install info
		/// for the current game mode</param>
		/// <param name="p_pmgPluginManager">The plugin manager.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		public ModDeleter(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, IInstallLog p_ilgModInstallLog, IPluginManager p_pmgPluginManager, ReadOnlyObservableList<IMod> p_rolActiveMods)
			:base(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_ivaVirtualModActivator, p_ilgModInstallLog, p_pmgPluginManager, p_rolActiveMods)
		{
		}

		#endregion

		/// <summary>
		/// Raises the <see cref="IBackgroundTaskSet.TaskSetCompleted"/> event.
		/// </summary>
		/// <remarks>
		/// This changes the message to reflect that fact that we are deleting the mod.
		/// </remarks>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the event arguments.</param>
		protected override void OnTaskSetCompleted(TaskSetCompletedEventArgs e)
		{
			string strMessage = null;
			if (e.Success)
				DeleteModFile((IMod)e.ReturnValue);
			else
				strMessage = "Could not delete mod.";
			base.OnTaskSetCompleted(new TaskSetCompletedEventArgs(e.Success, strMessage, e.ReturnValue));

		}

		/// <summary>
		/// Deletes the given mod file.
		/// </summary>
		/// <remarks>
		/// This deletes the physical mod file.
		/// </remarks>
		/// <param name="p_modMod">The mod to delete.</param>
		protected void DeleteModFile(IMod p_modMod)
		{
			FileUtil.ForceDelete(p_modMod.Filename);
			FileUtil.ForceDelete(Path.Combine(Path.GetDirectoryName(p_modMod.Filename), "cache", Path.GetFileNameWithoutExtension(p_modMod.Filename)));
			FileUtil.ForceDelete(Path.Combine(Path.Combine(Path.GetDirectoryName(p_modMod.Filename), "cache"), Path.GetFileName(p_modMod.Filename) + ".zip"));
		}
	}
}
