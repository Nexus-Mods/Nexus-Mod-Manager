using Nexus.Client.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.Games.Witcher.Settings
{
    public class GeneralSettingsGroup : SettingsGroup
    {
        private IEnvironmentInfo p_eifEnvironmentInfo;
        private IGameMode witcherGameMode;

        public GeneralSettingsGroup(IEnvironmentInfo p_eifEnvironmentInfo, IGameMode witcherGameMode)
            : base(p_eifEnvironmentInfo)
        {
            this.p_eifEnvironmentInfo = p_eifEnvironmentInfo;
            this.witcherGameMode = witcherGameMode;
        }

        public override string Title
        {
            get
            {
                return witcherGameMode.Name;
            }
        }

        public override void Load()
        {
            
        }

        public override bool Save()
        {
            return true;
        }
    }
}
