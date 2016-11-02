using Nexus.Client.Games.Settings;
using Nexus.Client.Settings;
using Nexus.Client.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.Games.Witcher.Settings
{
    public class GeneralSettingsGroup : SettingsGroup
    {
        private string m_strInstallationPath = null;
        private string m_strCustomCommand = null;
        private string m_strCustomCommandArguments = null;

        /// <summary>
		/// Gets or sets the path to which mod files should be installed.
		/// </summary>
		/// <value>The path to which mod files should be installed.</value>
		public string InstallationPath
        {
            get
            {
                return m_strInstallationPath;
            }
            set
            {
                SetPropertyIfChanged(ref m_strInstallationPath, value, () => InstallationPath);
            }
        }

        /// <summary>
		/// Gets or sets the custom launch command.
		/// </summary>
		/// <value>The custom launch command.</value>
		public string CustomLaunchCommand
        {
            get
            {
                return m_strCustomCommand;
            }
            set
            {
                SetPropertyIfChanged(ref m_strCustomCommand, value, () => CustomLaunchCommand);
            }
        }

        /// <summary>
        /// Gets or set the custom launch command arguments.
        /// </summary>
        /// <value>The custom launch command arguments.</value>
        public string CustomLaunchCommandArguments
        {
            get
            {
                return m_strCustomCommandArguments;
            }
            set
            {
                SetPropertyIfChanged(ref m_strCustomCommandArguments, value, () => CustomLaunchCommandArguments);
            }
        }

        private IEnvironmentInfo p_eifEnvironmentInfo;
        protected IGameMode GameMode { get; private set; }

        public GeneralSettingsGroup(IEnvironmentInfo p_eifEnvironmentInfo, IGameMode p_gmdGameMode)
            : base(p_eifEnvironmentInfo)
        {
            this.p_eifEnvironmentInfo = p_eifEnvironmentInfo;
            this.GameMode = p_gmdGameMode;
            RequiredDirectoriesVM = new RequiredDirectoriesControlVM(p_eifEnvironmentInfo, p_gmdGameMode);
        }

        public override string Title
        {
            get
            {
                return GameMode.Name;
            }
        }

        public RequiredDirectoriesControlVM RequiredDirectoriesVM { get; private set; }

        public override void Load()
        {
            string strValue = null;
            bool booRetrieved = false;
            if (EnvironmentInfo.Settings.DelayedSettings.ContainsKey(GameMode.ModeId))
                booRetrieved = EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId].TryGetValue(String.Format("InstallationPaths~{0}", GameMode.ModeId), out strValue);
            if (!booRetrieved)
                EnvironmentInfo.Settings.InstallationPaths.TryGetValue(GameMode.ModeId, out strValue);
            InstallationPath = strValue;

            strValue = null;
            EnvironmentInfo.Settings.CustomLaunchCommands.TryGetValue(GameMode.ModeId, out strValue);
            CustomLaunchCommand = strValue;

            strValue = null;
            EnvironmentInfo.Settings.CustomLaunchCommandArguments.TryGetValue(GameMode.ModeId, out strValue);
            CustomLaunchCommandArguments = strValue;

            RequiredDirectoriesVM.LoadSettings();
        }

        public override bool Save()
        {
            if (!RequiredDirectoriesVM.ValidateSettings())
                return false;
            RequiredDirectoriesVM.SaveSettings(true);

            if (!String.Equals(EnvironmentInfo.Settings.InstallationPaths[GameMode.ModeId], InstallationPath.Trim(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)))
                EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId].Add(String.Format("InstallationPaths~{0}", GameMode.ModeId), InstallationPath.Trim(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId] = FileUtil.StripInvalidPathChars(CustomLaunchCommand);
            EnvironmentInfo.Settings.CustomLaunchCommandArguments[GameMode.ModeId] = CustomLaunchCommandArguments;
            EnvironmentInfo.Settings.Save();
            return true;
        }
    }
}
