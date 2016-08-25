using Nexus.Client.Commands;
using Nexus.Client.Games.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.Games.NoMansSky.Tools.ShaderCache
{
    public class Cleaner : ITool
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

        public Cleaner(NoMansSkyGameMode p_gmdGameMode)
        {
            GameMode = p_gmdGameMode;
            LaunchCommand = new Command("Clean shader cache", "Deletes the shader cache folder", Clean);
        }

        #endregion

        /// <summary>
        /// Sets the view to use for this tool.
        /// </summary>
        /// <param name="p_tvwToolView">The view to use for this tool.</param>
        public void SetToolView(IToolView p_tvwToolView) => m_tvwToolView = p_tvwToolView;

        public void Clean()
        {
            if (Directory.Exists(Path.Combine(GameMode.InstallationPath, "SHADERCACHE")))
                Directory.Delete(Path.Combine(GameMode.InstallationPath, "SHADERCACHE"));
        }
    }
}
