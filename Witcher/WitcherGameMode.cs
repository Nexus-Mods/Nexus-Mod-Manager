using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ChinhDo.Transactions;
using Nexus.Client.Games.Tools;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Updating;
using Nexus.Client.Util;
using Nexus.Client.Games.Witcher.Tools;
using Nexus.Client.Settings.UI;
using Nexus.Client.Games.Witcher.Settings;
using Nexus.Client.Games.Witcher.Settings.UI;
using System.IO;

namespace Nexus.Client.Games.Witcher
{
    public class WitcherGameMode : GameModeBase
    {
        private WitcherGameModeDescriptor m_gmdGameModeInfo = null;
        private WitcherLauncher m_glnGameLauncher = null;
        private WitcherToolLauncher m_gtlToolLauncher = null;

        public WitcherGameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility) : base(p_eifEnvironmentInfo)
        {
            SettingsGroupViews = new List<ISettingsGroupView>();
            GeneralSettingsGroup gsgGeneralSettings = new GeneralSettingsGroup(p_eifEnvironmentInfo, this);
            ((List<ISettingsGroupView>)SettingsGroupViews).Add(new GeneralSettingsPage(gsgGeneralSettings));
        }

        public override IGameLauncher GameLauncher
        {
            get
            {
                if (m_glnGameLauncher == null)
                    m_glnGameLauncher = new WitcherLauncher(this, EnvironmentInfo);
                return m_glnGameLauncher;
            }
        }

        public override IToolLauncher GameToolLauncher
        {
            get
            {
                if (m_gtlToolLauncher == null)
                    m_gtlToolLauncher = new WitcherToolLauncher(this, EnvironmentInfo);
                return m_gtlToolLauncher;
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

        public override bool UsesPlugins
        {
            get
            {
                return false;
            }
        }

        public override IEnumerable<string> WritablePaths
        {
            get
            {
                return null;
            }
        }

        public override IActivePluginLogSerializer GetActivePluginLogSerializer(IPluginOrderLog p_polPluginOrderLog)
        {
            return null;
        }

        public override IGameSpecificValueInstaller GetGameSpecificValueInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
        {
            return null;
        }

        public override IGameSpecificValueInstaller GetGameSpecificValueUpgradeInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
        {
            return null;
        }

        public override IPluginDiscoverer GetPluginDiscoverer()
        {
            return null;
        }

        public override IPluginFactory GetPluginFactory()
        {
            return null;
        }

        public override IPluginOrderLogSerializer GetPluginOrderLogSerializer()
        {
            return null;
        }

        public override IPluginOrderValidator GetPluginOrderValidator()
        {
            return null;
        }

        public override IEnumerable<IUpdater> GetUpdaters()
        {
            return null;
        }

        protected override IGameModeDescriptor CreateGameModeDescriptor()
        {
            if (m_gmdGameModeInfo == null)
                m_gmdGameModeInfo = new WitcherGameModeDescriptor(EnvironmentInfo);
            return m_gmdGameModeInfo;
        }
    }
}
