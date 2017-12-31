using Nexus.Client.Mods;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.UI.Controls
{
    public class ModGetNewVersionEventArgs : ModEventArgs
    {
        /// <summary>
        /// Get a download link of the newest game mod file.
        /// </summary>
        public string DownloadLink { get; private set; }

        /// <summary>
        /// Generates a download link for the latest version of the selected mod.
        /// </summary>
        /// <param name="p_modMod">Mod to be updated.</param>
        /// <param name="gameMode">Currently active Game Mode.</param>
        public ModGetNewVersionEventArgs(IMod p_modMod,string gameMode)
            : base(p_modMod)
        {
            DownloadLink = $"nxm://{gameMode}/mods/{p_modMod.Id}/files/{p_modMod.DownloadId}";
        }
    }
}
