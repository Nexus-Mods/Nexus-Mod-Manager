using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.Games.Witcher
{
    public class WitcherSetupVM : SetupBaseVM
    {
        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
        /// <param name="p_gmdGameModeInfo">The descriptor for the game mode being set up.</param>
        public WitcherSetupVM(IEnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo)
			:base(p_eifEnvironmentInfo, p_gmdGameModeInfo)
		{
        }

        #endregion
    }
}
