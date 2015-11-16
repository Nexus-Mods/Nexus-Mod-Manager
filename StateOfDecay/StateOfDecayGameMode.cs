using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using ChinhDo.Transactions;
using Nexus.Client.Games.StateOfDecay.Settings;
using Nexus.Client.Games.StateOfDecay.Settings.UI;
using Nexus.Client.Games.StateOfDecay.Tools;
using Nexus.Client.Games.Tools;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Settings.UI;
using Nexus.Client.Updating;
using Nexus.Client.Util;
using Nexus.UI.Controls;

namespace Nexus.Client.Games.StateOfDecay
{
    /// <summary>
    /// Provides information required for the program to manage StateOfDecay's mods.
    /// </summary>
    public class StateOfDecayGameMode : GameModeBase
    {
        private StateOfDecayGameModeDescriptor m_gmdGameModeInfo = null;
        private StateOfDecayLauncher m_glnGameLauncher = null;
        private StateOfDecayToolLauncher m_gtlToolLauncher = null;

        #region Properties

        /// <summary>
        /// Gets the version of the installed game.
        /// </summary>
        /// <value>The version of the installed game.</value>
        public override Version GameVersion
        {
            get
            {
                string strFullPath = null;
                foreach (string strExecutable in GameExecutables)
                {
                    strFullPath = Path.Combine(GameModeEnvironmentInfo.InstallationPath, strExecutable);
                    if (File.Exists(strFullPath))
                        return new Version(System.Diagnostics.FileVersionInfo.GetVersionInfo(strFullPath).FileVersion.Replace(", ", "."));
                }
                return null;
            }
        }

        /// <summary>
        /// Gets a list of paths to which the game mode writes.
        /// </summary>
        /// <value>A list of paths to which the game mode writes.</value>
        public override IEnumerable<string> WritablePaths
        {
            get
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the installed version of the script extender.
        /// </summary>
        /// <remarks>
        /// <c>null</c> is returned if the script extender is not installed.
        /// </remarks>
        /// <value>The installed version of the script extender.</value>
        public virtual Version ScriptExtenderVersion
        {
            get
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the path to the per user StateOfDecay data.
        /// </summary>
        /// <value>The path to the per user StateOfDecay data.</value>
        public string UserGameDataPath
        {
            get
            {
                return GameModeEnvironmentInfo.InstallationPath;
            }
        }

        /// <summary>
        /// Gets the game launcher for the game mode.
        /// </summary>
        /// <value>The game launcher for the game mode.</value>
        public override IGameLauncher GameLauncher
        {
            get
            {
                if (m_glnGameLauncher == null)
                    m_glnGameLauncher = new StateOfDecayLauncher(this, EnvironmentInfo);
                return m_glnGameLauncher;
            }
        }

        /// <summary>
        /// Gets the tool launcher for the game mode.
        /// </summary>
        /// <value>The tool launcher for the game mode.</value>
        public override IToolLauncher GameToolLauncher
        {
            get
            {
                if (m_gtlToolLauncher == null)
                    m_gtlToolLauncher = new StateOfDecayToolLauncher(this, EnvironmentInfo);
                return m_gtlToolLauncher;
            }
        }

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
        public override bool UsesPlugins
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the default game categories.
        /// </summary>
        /// <value>The default game categories stored in the resource file.</value>
        public override string GameDefaultCategories
        {
            get
            {
                return Properties.Resources.Categories;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
        /// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
        public StateOfDecayGameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
            : base(p_eifEnvironmentInfo)
        {
            SettingsGroupViews = new List<ISettingsGroupView>();
            GeneralSettingsGroup gsgGeneralSettings = new GeneralSettingsGroup(p_eifEnvironmentInfo, this);
            ((List<ISettingsGroupView>)SettingsGroupViews).Add(new Nexus.Client.Games.StateOfDecay.Settings.UI.GeneralSettingsPage(gsgGeneralSettings));
        }

        #endregion

        #region Initialization

        #endregion

        #region Plugin Management

        /// <summary>
        /// Gets the factory that builds plugins for this game mode.
        /// </summary>
        /// <returns>The factory that builds plugins for this game mode.</returns>
        public override IPluginFactory GetPluginFactory()
        {
            return null;
        }

        /// <summary>
        /// Gets the serailizer that serializes and deserializes the list of active plugins
        /// for this game mode.
        /// </summary>
        /// <param name="p_polPluginOrderLog">The <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.</param>
        /// <returns>The serailizer that serializes and deserializes the list of active plugins
        /// for this game mode.</returns>
        public override IActivePluginLogSerializer GetActivePluginLogSerializer(IPluginOrderLog p_polPluginOrderLog)
        {
            return null;
        }

        /// <summary>
        /// Gets the discoverer to use to find the plugins managed by this game mode.
        /// </summary>
        /// <returns>The discoverer to use to find the plugins managed by this game mode.</returns>
        public override IPluginDiscoverer GetPluginDiscoverer()
        {
            return null;
        }

        /// <summary>
        /// Gets the serializer that serializes and deserializes the plugin order
        /// for this game mode.
        /// </summary>
        /// <returns>The serailizer that serializes and deserializes the plugin order
        /// for this game mode.</returns>
        public override IPluginOrderLogSerializer GetPluginOrderLogSerializer()
        {
            return null;
        }

        /// <summary>
        /// Gets the object that validates plugin order for this game mode.
        /// </summary>
        /// <returns>The object that validates plugin order for this game mode.</returns>
        public override IPluginOrderValidator GetPluginOrderValidator()
        {
            return null;
        }

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
        public override IGameSpecificValueInstaller GetGameSpecificValueInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
        {
            return null;
        }

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
        public override IGameSpecificValueInstaller GetGameSpecificValueUpgradeInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
        {
            return null;
        }

        /// <summary>
        /// Loads the mod descriptor (content.xml) from an IMod.
        /// </summary>
        /// <param name="p_modMod">The mod to be activated</param>
        /// <returns>Null on error otherwise the mod name</returns>
        private string LoadModNameInternal(IMod p_modMod)
        {
            var bteContentFile = p_modMod.GetFile("content.xml");
            var strContentXml = Encoding.UTF8.GetString(bteContentFile);

            var xdocContentXml = XDocument.Parse(strContentXml);
            if (xdocContentXml.Root != null)
            {
                return xdocContentXml.Root.Attribute("name").Value;
            }
            return null;
        }

        /// <summary>
        /// Checks a mod if it needs a path adjust.
        /// Mods need path adjusts if they either are not exactly one folder deep.
        /// Also more than one content.xml is not allowed in one package.
        /// 
        /// Ex:
        /// ZIP/
        ///     Mod1/
        ///         content.xml
        /// 
        /// Wrong:
        /// ZIP/
        ///     Mod1/
        ///         XYZ/
        ///             content.xml
        /// </summary>
        /// <param name="p_modMod"></param>
        /// <returns></returns>
        private bool NeedsPathAdjustForContentXml(IMod p_modMod)
        {
            bool adjustPath = false;

            if (p_modMod.GetFileList().Exists(DoesContentFileExist))
            {
                // this is ok to throw if nessecary, handled in calling function
                var strContentXmlPath = p_modMod.GetFileList().Single(f => f.Contains("content.xml"));
                var intNestLevels = strContentXmlPath.Split('\\').Count();

                if (intNestLevels == 1 || intNestLevels > 2)
                {
                    adjustPath = true;
                }
            }

            return adjustPath;
        }

        private bool DoesContentFileExist(String s)
        {
            bool strFound = false;

            if (s.Contains("content.xml"))
            {
                strFound = true;
            }

            return strFound;
        }

        #endregion

        /// <summary>
        /// Gets the updaters used by the game mode.
        /// </summary>
        /// <returns>The updaters used by the game mode.</returns>
        public override IEnumerable<IUpdater> GetUpdaters()
        {
            return null;
        }

        /// <summary>
        /// Creates a game mode descriptor for the current game mode.
        /// </summary>
        /// <returns>A game mode descriptor for the current game mode.</returns>
        protected override IGameModeDescriptor CreateGameModeDescriptor()
        {
            if (m_gmdGameModeInfo == null)
                m_gmdGameModeInfo = new StateOfDecayGameModeDescriptor(EnvironmentInfo);
            return m_gmdGameModeInfo;
        }

        /// <summary>
        /// Adjusts the given path to be relative to the installation path of the game mode.
        /// </summary>
        /// <remarks>
        /// This is basically a hack to allow older FOMod/OMods to work. Older FOMods assumed
        /// the installation path of Fallout games to be &lt;games>/data, but this new manager specifies
        /// the installation path to be &lt;games>. This breaks the older FOMods, so this method can detect
        /// the older FOMods (or other mod formats that needs massaging), and adjusts the given path
        /// to be relative to the new instaalation path to make things work.
        /// </remarks>
        /// <param name="p_mftModFormat">The mod format for which to adjust the path.</param>
        /// <param name="p_strPath">The path to adjust</param>
		/// <param name="p_modMod">The mod.</param>
		/// <param name="p_booIgnoreIfPresent">Whether to ignore the path if the specific root is already present</param>
        /// <returns>The given path, adjusted to be relative to the installation path of the game mode.</returns>
		public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, IMod p_modMod, bool p_booIgnoreIfPresent)
        {
            try
            {
                // check for single folder mods
                if (NeedsPathAdjustForContentXml(p_modMod))
                {
                    try
                    {
                        // if this throws or returns null, the mod package is invalid
                        var strModInternalName = LoadModNameInternal(p_modMod);
                        if (string.IsNullOrEmpty(strModInternalName))
                            throw new ArgumentException(
                                "The attribute 'name' is set improperly in the file 'content.xml'.");

                        // JAM 2014-02-07 This Path is not taken most of the time
                        return Path.Combine(strModInternalName, p_strPath);
                    }
                    catch (FileNotFoundException exFileNotFound)
                    {
                        if (exFileNotFound.FileName.Equals("content.xml"))
                            // return friendly error message
                            throw new FileNotFoundException("Mod package does not include a content.xml file.");
                        // we don't know what this is, rethrow it.
                        throw;
                    }
                }

                // Used to return null for invalid paths
                if (!validPathForModifications(p_strPath))
                {
                    p_strPath = null;
                }

                p_strPath = getAdjustedModPath(p_strPath);

                return p_strPath;
            }
            // IOEX from NeedsPathAdjust - Multiple content.xml found.
            catch (InvalidOperationException)
            {
                throw new InvalidDataException("This package contains multiple mods, which is not supported.");
            }
        }

        private String getAdjustedModPath(String p_strPath)
        {
            if (p_strPath != null)
            {
                String lowercasePath = p_strPath.ToLower();

                int gameIndex = lowercasePath.IndexOf("game");
                int engineIndex = lowercasePath.IndexOf("engine");
                int userIndex = lowercasePath.IndexOf("user");

                bool checkForSlash = p_strPath.Contains("\\"); // Means the file path contains at a subfolder

                if (checkForSlash == false)
                {
                    return p_strPath;
                }
                else if (gameIndex != 0 && engineIndex != 0 && userIndex != 0)
                {
                    throw new InvalidDataException("This package does not met the layout standards for State of Decay mods, which is not supported.");
                }
            }

            return p_strPath;
        }

        /* Checks to see if the path within the mod goes to a valid file or if it can be skipped
         *
         * Example of an invalid path is one that points to a read me file
         * 
         * Returns true if the file in the path is acceptable
         */
        private bool validPathForModifications(String p_strPath)
        {
            bool result = false;

            if (p_strPath.Contains("content.xml") == false)
            {
                if (p_strPath.Contains("components.xml") == false)
                {
                    if (p_strPath.ToLower().Contains("readme") == false && p_strPath.ToLower().Contains("read me") == false)
                    {
                        if (p_strPath.ToLower().Contains(".url") == false)
                        {
                            if (p_strPath.ToLower().Contains("jsgme") == false)
                            {
                                result = true;
                            }
                        }
                    }
                }
            }

            return result;
        }

        public override bool CheckSecondaryUninstall(string p_strFileName)
        {
            if (p_strFileName.Contains("content.xml"))
            {
                try
                {
                    var xdocContentXml = XDocument.Load(Path.Combine(InstallationPath, p_strFileName));
                    var strModName = xdocContentXml.Root.Attribute("name").Value;
                    var strSaveAttribute = xdocContentXml.Root.Attribute("save");
                    string strSaveValue = null;
                    if (strSaveAttribute != null)
                    {
                        strSaveValue = strSaveAttribute.Value;
                    }

                    if (string.IsNullOrEmpty(strSaveValue) || strSaveValue.Equals("true") || strSaveValue.Equals("1"))
                    {
                        DialogResult drResult = DialogResult.None;
                        try
                        {
                            ThreadStart actShowMessage =
                                () =>
                                    drResult =
                                        ExtendedMessageBox.Show(null,
                                            string.Format(
                                                "The author of the {0} mod has chosen that his mod should get recorded in your save games.\nIf you played the game while this mod was active, your save files may no longer work.\nDo you want to continue?",
                                                strModName), "Warning", MessageBoxButtons.YesNo,
                                            MessageBoxIcon.Exclamation);

                            ApartmentState astState = ApartmentState.Unknown;
                            Thread.CurrentThread.TrySetApartmentState(astState);
                            if (astState == ApartmentState.STA)
                                actShowMessage();
                            else
                            {
                                var thdMessage = new Thread(actShowMessage);
                                thdMessage.SetApartmentState(ApartmentState.STA);
                                thdMessage.Start();
                                thdMessage.Join();

                                if (drResult == DialogResult.No)
                                    return true;
                            }
                        }
                        catch (Exception)
                        {
                            drResult = MessageBox.Show(string.Format(
                                "The author of the {0} mod has chosen that his mod should get recorded in your save games.\nIf you played the game while this mod was active, your save files may no longer work.\nDo you want to continue?",
                                strModName), "Warning", MessageBoxButtons.YesNo,
                                MessageBoxIcon.Exclamation);
                            if (drResult == DialogResult.No)
                                return true;
                        }
                    }
                }
                catch
                {
                    return true;
                }

            }

            return false;
        }

        public override bool HasSecondaryInstallPath
        {
            get { return true; }
        }

        /// <summary>
        /// Disposes of the unamanged resources.
        /// </summary>
        /// <param name="p_booDisposing">Whether the method is being called from the <see cref="IDisposable.Dispose()"/> method.</param>
        protected override void Dispose(bool p_booDisposing) { }
    }
}
