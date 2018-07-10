using System.Collections.Generic;
using Nexus.Client.Games.Tools;

namespace Nexus.Client.Games.DarkSouls.Tools
{
    /// <summary>
	/// Exposes the tools for DarkSouls.
    /// </summary>
    public class DarkSoulsToolLauncher : IToolLauncher
    {
        private List<ITool> m_lstTools = new List<ITool>();

        #region Properties

        #region IToolLauncher Members

        /// <summary>
        /// Gets the tools associated with the game mode.
        /// </summary>
        /// <value>The tools associated with the game mode.</value>
        public IEnumerable<ITool> Tools
        {
            get
            {
                return m_lstTools;
            }
        }

        #endregion

        /// <summary>
        /// Gets the game mode currently being managed.
        /// </summary>
        /// <value>The game mode currently being managed.</value>
        protected IGameMode GameMode { get; private set; }

        /// <summary>
        /// Gets the application's envrionment info.
        /// </summary>
        /// <value>The application's envrionment info.</value>
        protected IEnvironmentInfo EnvironmentInfo { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given dependencies.
        /// </summary>
        /// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public DarkSoulsToolLauncher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
        {
            GameMode = p_gmdGameMode;
            EnvironmentInfo = p_eifEnvironmentInfo;
            SetupTools();
        }

        #endregion

        /// <summary>
        /// Initializes the game tools.
        /// </summary>
        protected void SetupTools()
        {
            m_lstTools.Clear();
        }
    }
}
