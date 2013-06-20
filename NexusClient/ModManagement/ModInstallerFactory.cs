using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Util;
using System;
using Nexus.Client.PluginManagement;
using Nexus.Client.ModManagement.UI;
using System.Threading;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// The class that creates <see cref="ModInstaller"/>s.
	/// </summary>
	public class ModInstallerFactory
	{
		private IGameMode m_gmdGameMode = null;
		private IEnvironmentInfo m_eifEnvironmentInfo = null;
		private IInstallLog m_ilgInstallLog = null;
		private IPluginManager m_pmgPluginManager = null;
		private FileUtil m_futFileUtility = null;
		private SynchronizationContext m_scxUIContext = null;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the factory with the required dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode for which the created installer will be installing mods.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_ilgInstallLog">The install log that tracks mod install info
		/// for the current game mode.</param>
		/// <param name="p_pmgPluginManager">The plugin manager to use to work with plugins.</param>
		public ModInstallerFactory(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgInstallLog, IPluginManager p_pmgPluginManager)
		{
			m_gmdGameMode = p_gmdGameMode;
			m_eifEnvironmentInfo = p_eifEnvironmentInfo;
			m_futFileUtility = p_futFileUtility;
			m_scxUIContext = p_scxUIContext;
			m_ilgInstallLog = p_ilgInstallLog;
			m_pmgPluginManager = p_pmgPluginManager;
		}

		#endregion

		/// <summary>
		/// Creates a mod installer for the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod for which to create the installer.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		/// <returns>A mod installer for the given mod.</returns>
		public ModInstaller CreateInstaller(IMod p_modMod, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			return new ModInstaller(p_modMod, m_gmdGameMode, m_eifEnvironmentInfo, m_futFileUtility, m_scxUIContext, m_ilgInstallLog, m_pmgPluginManager, p_dlgOverwriteConfirmationDelegate, p_rolActiveMods);
		}

		/// <summary>
		/// Creates a mod upgrader for the given mod.
		/// </summary>
		/// <param name="p_modOldMod">The mod from which to upgrade.</param>
		/// <param name="p_modNewMod">The mod to which to upgrade.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>A mod upgrader for the given mod.</returns>
		public ModInstaller CreateUpgradeInstaller(IMod p_modOldMod, IMod p_modNewMod, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return new ModUpgrader(p_modOldMod, p_modNewMod, m_gmdGameMode, m_eifEnvironmentInfo, m_futFileUtility, m_scxUIContext, m_ilgInstallLog, m_pmgPluginManager, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// Creates a mod uninstaller for the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod for which to create the uninstaller.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		/// <returns>A mod uninstaller for the given mod.</returns>
		public ModUninstaller CreateUninstaller(IMod p_modMod, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			return new ModUninstaller(p_modMod, m_gmdGameMode, m_eifEnvironmentInfo, m_ilgInstallLog, m_pmgPluginManager, p_rolActiveMods);
		}

		/// <summary>
		/// Creates a mod deleter for the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod for which to create the deleter.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		/// <returns>A mod deleter for the given mod.</returns>
		public ModDeleter CreateDelete(IMod p_modMod, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			return new ModDeleter(p_modMod, m_gmdGameMode, m_eifEnvironmentInfo, m_ilgInstallLog, m_pmgPluginManager, p_rolActiveMods);
		}
	}
}
