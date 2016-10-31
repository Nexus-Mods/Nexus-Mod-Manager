using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.Games.Witcher.Settings
{
    public class GeneralSettingsGroup
    {
        private IEnvironmentInfo p_eifEnvironmentInfo;
        private WitcherGameMode witcherGameMode;

        public GeneralSettingsGroup(IEnvironmentInfo p_eifEnvironmentInfo, WitcherGameMode witcherGameMode)
        {
            this.p_eifEnvironmentInfo = p_eifEnvironmentInfo;
            this.witcherGameMode = witcherGameMode;
        }
    }
}
