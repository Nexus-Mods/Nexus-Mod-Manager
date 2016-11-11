using System;
using System.Collections.Generic;
using System.Windows.Forms;
using ChinhDo.Transactions;
using Nexus.Client.Games.Tools;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Plugins;
using Nexus.Client.Settings.UI;
using Nexus.Client.Updating;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.Games
{
	/// <summary>
	/// Describes the properties and methods of a game mode.
	/// </summary>
	/// <remarks>
	/// A game mode exposes the info and functionality that describes a game-specific environemnt in which
	/// mods are managed.
	/// </remarks>
	public interface IGameMode : IGameModeDescriptor, IDisposable
	{
		#region Properties

		/// <summary>
		/// Gets the information about the game mode's environement.
		/// </summary>
		/// <value>The information about the game mode's environement.</value>
		IGameModeEnvironmentInfo GameModeEnvironmentInfo { get; }

		/// <summary>
		/// Gets the version of the installed game.
		/// </summary>
		/// <value>The version of the installed game.</value>
		Version GameVersion { get; }

		/// <summary>
		/// Gets a list of paths to which the game mode writes.
		/// </summary>
		/// <value>A list of paths to which the game mode writes.</value>
		IEnumerable<string> WritablePaths { get; }

		/// <summary>
		/// Gets the exported settings groups specific to the game mode.
		/// </summary>
		/// <returns>The exported settings groups specific to the game mode.</returns>
		IEnumerable<ISettingsGroupView> SettingsGroupViews { get; }

		/// <summary>
		/// Gets the game launcher for the game mode.
		/// </summary>
		/// <value>The game launcher for the game mode.</value>
		IGameLauncher GameLauncher { get; }

		/// <summary>
		/// Gets the tool launcher for the game mode.
		/// </summary>
		/// <value>The tool launcher for the game mode.</value>
		IToolLauncher GameToolLauncher { get; }
		
		/// <summary>
		/// Gets whether the game mode uses plugins.
		/// </summary>
		/// <remarks>
		/// This indicates whether the game mode used plugins that are
		/// installed by mods, or simply used mods, without
		/// plugins.
		/// 
		/// In games that use mods only, the installation of a mods package
		/// is sufficient to add the functionality to the game. The game
		/// will often have no concept of managable game modifications.
		/// 
		/// In games that use plugins, mods can install files that directly
		/// affect the game (similar to the mod-free use case), but can also
		/// install plugins that can be managed (for example activated/reordered)
		/// after the mod is installed.
		/// </remarks>
		/// <value>Whether the game mode uses plugins.</value>
		bool UsesPlugins { get; }

		/// <summary>
		/// Gets whether the game mode supports the automatic sorting
		/// functionality for plugins.
		/// </summary>
		bool SupportsPluginAutoSorting { get; }

		/// <summary>
		/// Gets the plugin loadorder manager.
		/// </summary>
		/// <value>The plugin loadorder manager.</value>
		ILoadOrderManager LoadOrderManager { get; }

		/// <summary>
		/// Gets the default game categories.
		/// </summary>
		/// <value>The default game categories stored in the resource file.</value>
		string GameDefaultCategories { get; }

		/// <summary>
		/// Gets the default game files.
		/// </summary>
		/// <value>The default game files stored in the resource file.</value>
		string BaseGameFiles { get; }

		/// <summary>
		/// Gets the max allowed number of active plugins.
		/// </summary>
		/// <value>The max allowed number of active plugins (0 if there's no limit).</value>
		Int32 MaxAllowedActivePluginsCount { get; }

		/// <summary>
		/// Whether the game requires mod file merging.
		/// </summary>
		bool RequiresModFileMerge { get; }

		/// <summary>
		/// The name of the game's merged file.
		/// </summary>
		string MergedFileName { get; }

		/// <summary>
		/// Whether the game has a secondary install path.
		/// </summary>
		bool HasSecondaryInstallPath { get; }

		/// <summary>
		/// Whether the game requires the profile manager to save optional files.
		/// </summary>
		bool RequiresOptionalFilesCheckOnProfileSwitch { get; }

		/// <summary>
		/// Gets the tool launcher for the SupportedTools.
		/// </summary>
		/// <value>The tool launcher for the SupportedTools.</value>
		ISupportedToolsLauncher SupportedToolsLauncher { get; }

		/// <summary>
		/// Whether the plugin sorter is properly initialized.
		/// </summary>
		bool PluginSorterInitialized { get; }

        /// <summary>
        /// Gets whether the gamemode requires mod sorting
        /// </summary>
        bool RequiresModSorting { get; }

		#endregion

		#region Plugin Management

		/// <summary>
		/// Gets the factory that builds plugins for this game mode.
		/// </summary>
		/// <returns>The factory that builds plugins for this game mode.</returns>
		IPluginFactory GetPluginFactory();

		/// <summary>
		/// Gets the serailizer that serializes and deserializes the list of active plugins
		/// for this game mode.
		/// </summary>
		/// <param name="p_polPluginOrderLog">The <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.</param>
		/// <returns>The serailizer that serializes and deserializes the list of active plugins
		/// for this game mode.</returns>
		IActivePluginLogSerializer GetActivePluginLogSerializer(IPluginOrderLog p_polPluginOrderLog);

		/// <summary>
		/// Gets the discoverer to use to find the plugins managed by this game mode.
		/// </summary>
		/// <returns>The discoverer to use to find the plugins managed by this game mode.</returns>
		IPluginDiscoverer GetPluginDiscoverer();

		/// <summary>
		/// Gets the serializer that serializes and deserializes the plugin order
		/// for this game mode.
		/// </summary>
		/// <returns>The serailizer that serializes and deserializes the plugin order
		/// for this game mode.</returns>
		IPluginOrderLogSerializer GetPluginOrderLogSerializer();

		/// <summary>
		/// Gets the object that validates plugin order for this game mode.
		/// </summary>
		/// <returns>The object that validates plugin order for this game mode.</returns>
		IPluginOrderValidator GetPluginOrderValidator();

		/// <summary>
		/// Determines if the given plugin is critical to the current game.
		/// </summary>
		/// <remarks>
		/// Critical plugins cannot be reordered, cannot be deleted, cannot be deactivated, and cannot have plugins ordered above them.
		/// </remarks>
		/// <param name="p_plgPlugin">The plugin for which it is to be determined whether or not it is critical.</param>
		/// <returns><c>true</c> if the specified pluing is critical;
		/// <c>false</c> otherwise.</returns>
		bool IsCriticalPlugin(Plugin p_plgPlugin);

		/// <summary>
		/// Automatically sorts the given plugin list.
		/// </summary>
		/// <returns>The sorted list.</returns>
		/// <param name="p_lstPlugins">The plugin list to sort.</param>
		string[] SortPlugins(IList<Plugin> p_lstPlugins);

		#endregion

		#region Game Specific Value Management

		/// <summary>
		/// Gets the installer to use to install game specific values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_ilgInstallLog">The install log to use to log the installation of the game specific values.</param>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <returns>The installer to use to manage game specific values, or <c>null</c> if the game mode does not
		/// install any game specific values.</returns>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		IGameSpecificValueInstaller GetGameSpecificValueInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate);

		/// <summary>
		/// Gets the installer to use to upgrade game specific values.
		/// </summary>
		/// <param name="p_modMod">The mod being upgraded.</param>
		/// <param name="p_ilgInstallLog">The install log to use to log the installation of the game specific values.</param>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <returns>The installer to use to manage game specific values, or <c>null</c> if the game mode does not
		/// install any game specific values.</returns>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		IGameSpecificValueInstaller GetGameSpecificValueUpgradeInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate);

		#endregion

		/// <summary>
		/// Gets the updaters used by the game mode.
		/// </summary>
		/// <returns>The updaters used by the game mode.</returns>
		IEnumerable<IUpdater> GetUpdaters();

		/// <summary>
		/// Adjusts the given path to be relative to the installation path of the game mode.
		/// </summary>
		/// <remarks>
		/// This is basically a hack to allow older FOMods to work. Older FOMods assumed
		/// the installation path of Fallout games to be &lt;games>/data, but this new manager specifies
		/// the installation path to be &lt;games>. This breaks the older FOMods, so this method can detect
		/// the older FOMods (or other mod formats that needs massaging), and adjusts the given path
		/// to be relative to the new instaalation path to make things work.
		/// </remarks>
		/// <param name="p_mftModFormat">The mod format for which to adjust the path.</param>
		/// <param name="p_strPath">The path to adjust</param>
		/// <param name="p_booIgnoreIfPresent">Whether to ignore the path if the specific root is already present</param>
		/// <returns>The given path, adjusted to be relative to the installation path of the game mode.</returns>
		string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, bool p_booIgnoreIfPresent);

		/// <summary>
		/// Adjusts the given path to be relative to the installation path of the game mode.
		/// </summary>
		/// <remarks>
		/// This is basically a hack to allow older FOMods to work. Older FOMods assumed
		/// the installation path of Fallout games to be &lt;games>/data, but this new manager specifies
		/// the installation path to be &lt;games>. This breaks the older FOMods, so this method can detect
		/// the older FOMods (or other mod formats that needs massaging), and adjusts the given path
		/// to be relative to the new instaalation path to make things work.
		/// </remarks>
		/// <param name="p_mftModFormat">The mod format for which to adjust the path.</param>
		/// <param name="p_strPath">The path to adjust.</param>
		/// <param name="p_modMod">The mod.</param>
		/// <param name="p_booIgnoreIfPresent">Whether to ignore the path if the specific root is already present</param>
		/// <returns>The given path, adjusted to be relative to the installation path of the game mode.</returns>
		string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, IMod p_modMod, bool p_booIgnoreIfPresent);

		/// <summary>
		/// Merges the mod files if requested by the game.
		/// </summary>
		/// <returns>Merges the mod files if requested by the game.</returns>
		/// <param name="p_lstActiveMods">The list of active mods.</param>
		/// <param name="p_modMod">The current mod.</param>
		/// <param name="p_booRemove">Whether we're adding or removing the mod.</param>
		void ModFileMerge(IList<IMod> p_lstActiveMods, IMod p_modMod, bool p_booRemove);

		/// <summary>
		/// Checks whether the current game mode requires external config steps to be taken before installing mods.
		/// </summary>
		/// <returns>Whether the current game mode requires external config steps to be taken before installing mods.</returns>
		/// <param name="p_strMessage">The message to show to the user.</param>
		bool RequiresExternalConfig(out string p_strMessage);

		/// <summary>
		/// Checks whether to use the secondary mod install method.
		/// </summary>
		/// <returns>Whether to use the secondary mod install method.</returns>
		/// <param name="p_modMod">The mod to be installed.</param>
		bool CheckSecondaryInstall(IMod p_modMod);

		/// <summary>
		/// Checks whether the system needs to uninstall secondary parameters.
		/// </summary>
		/// <returns>Whether the system needs to uninstall secondary parameters.</returns>
		/// <param name="p_strFileName">The filename.</param>
		bool CheckSecondaryUninstall(string p_strFileName);

		/// <summary>
		/// Checks whether the file's type requires a hardlink for the current game mode.
		/// </summary>
		/// <returns>Whether the file's type requires a hardlink for the current game mode.</returns>
		/// <param name="p_strFileName">The filename.</param>
		bool HardlinkRequiredFilesType(string p_strFileName);

		/// <summary>
		/// Whether to run a secondary tools if present.
		/// </summary>
		/// <returns>The path to the optional tool to run.</returns>
		/// <param name="p_strMessage">The message to show to the user.</param>
		string PostProfileSwitchTool(out string p_strMessage);

		/// <summary>
		/// Whether the profile manager should save extra files for the current game mode.
		/// </summary>
		/// <returns>The list of optional files to save (if present) in a profile.</returns>
		/// <param name="p_strMessage">The list of files/plugins/mods to save.</param>
		string[] GetOptionalFilesList(string[] p_strList);

		/// <summary>
		/// Whether the profile manager should load extra files for the current game mode.
		/// </summary>
		/// <returns>The list of optional files to load (if present) in a profile.</returns>
		/// <param name="p_strMessage">The list of files/plugins/mods to load.</param>
		void SetOptionalFilesList(string[] p_strList);

		/// <summary>
		/// The supported formats list.
		/// </summary>
		List<string> SupportedFormats { get; }

        /// <summary>
        /// Defines whether or not files require special installation instructions
        /// </summary>
        /// <returns>Whether or not files require special installation instructions</returns>
        bool RequiresSpecialFileInstallation { get; }

        /// <summary>
        /// Defines whether or not files use a special load order
        /// </summary>
        bool UsesModLoadOrder { get; }

        void SortMods(Action<IMod, IMod> p_actReinstallMethod, ReadOnlyObservableList<IMod> p_lstActiveMods);

        /// <summary>
        /// Handles special file installation
        /// </summary>
        /// <param name="p_modSelectedMod">The mod with special files to handle</param>
        /// <returns>The list of new files to install</returns>
        IEnumerable<string> SpecialFileInstall(IMod p_modSelectedMod);

        /// <summary>
        /// Checks whether any of the files require SpecialFileInstall
        /// </summary>
        /// <param name="p_strFiles">List of files to check</param>
        /// <returns>Whether any of the files need special installation</returns>
        bool IsSpecialFile(IEnumerable<string> p_strFiles);
	}
}
