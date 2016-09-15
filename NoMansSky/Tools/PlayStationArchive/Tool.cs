using Nexus.Client.Commands;
using Nexus.Client.Games.Tools;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Nexus.Client.Games.NoMansSky.Tools.PlayStationArchive
{
    /// <summary>
    /// Contains methods for extraction of No Man's Sky PSARC files
    /// </summary>
    public class PSARCTool : ITool
    {
        #region Events

        public event EventHandler<DisplayToolViewEventArgs> DisplayToolView = delegate { };

        public event EventHandler<DisplayToolViewEventArgs> CloseToolView = delegate { };

        #endregion

        private IToolView m_tvwToolView = null;

        #region Properties

        public Command LaunchCommand { get; private set; }

        protected NoMansSkyGameMode GameMode { get; private set; }

        #endregion

        #region Constructor

        public PSARCTool(NoMansSkyGameMode p_gmdGameMode)
        {
            GameMode = p_gmdGameMode;
            LaunchCommand = new Command("Extract all pak files", "Cleans the GAMEDATA directory and extracts all pak files", UnpackGameData);
        }

        #endregion

        /// <summary>
        /// Sets the view to use for this tool.
        /// </summary>
        /// <param name="p_tvwToolView">The view to use for this tool.</param>
        public void SetToolView(IToolView p_tvwToolView) => m_tvwToolView = p_tvwToolView;

        public void UnpackGameData()
        {
            
        }
    }
}
