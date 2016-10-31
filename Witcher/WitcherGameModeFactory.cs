using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Settings;
using System.Windows.Forms;

namespace Nexus.Client.Games.Witcher
{
    public class WitcherGameModeFactory : IGameModeFactory
    {
        private readonly IGameModeDescriptor m_gmdGameModeDescriptor = null;

        /// <summary>
		/// Gets the application's environement info.
		/// </summary>
		/// <value>The application's environement info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

        public IGameModeDescriptor GameModeDescriptor
        {
            get
            {
                return m_gmdGameModeDescriptor;
            }
        }

        protected WitcherGameMode InstantiateGameMode(FileUtil p_futFileUtility)
        {
            return new WitcherGameMode(EnvironmentInfo, p_futFileUtility);
        }

        public IGameMode BuildGameMode(FileUtil p_futFileUtility, out ViewMessage p_imsWarning)
        {
            if (EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] == null)
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] = new PerGameModeSettings<object>();
            if (!EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId].ContainsKey("AskAboutReadOnlySettingsFiles"))
            {
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["AskAboutReadOnlySettingsFiles"] = true;
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["UnReadOnlySettingsFiles"] = true;
                EnvironmentInfo.Settings.Save();
            }

            WitcherGameMode gmdGameMode = InstantiateGameMode(p_futFileUtility);
            p_imsWarning = null;

            return gmdGameMode;
        }

        public string GetExecutablePath(string p_strGameInstallPath)
        {
            return p_strGameInstallPath;
        }

        public string GetInstallationPath()
        {
            //TODO
            return null;
        }

        public string GetInstallationPath(string p_strGameInstallPath)
        {
            return p_strGameInstallPath;
        }

        public bool PerformInitialization(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
        {
            return true;
        }

        public bool PerformInitialSetup(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
        {
            if (EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] == null)
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] = new PerGameModeSettings<object>();

            WitcherSetupVM vmlSetup = new WitcherSetupVM(EnvironmentInfo, GameModeDescriptor);
            SetupForm frmSetup = new SetupForm(vmlSetup);
            if (((DialogResult)p_dlgShowView(frmSetup, true)) == DialogResult.Cancel)
                return false;
            return vmlSetup.Save();
        }

        public WitcherGameModeFactory(IEnvironmentInfo p_eifEnvironmentInfo)
        {
            EnvironmentInfo = p_eifEnvironmentInfo;
            m_gmdGameModeDescriptor = new WitcherGameModeDescriptor(p_eifEnvironmentInfo);
        }
    }
}
