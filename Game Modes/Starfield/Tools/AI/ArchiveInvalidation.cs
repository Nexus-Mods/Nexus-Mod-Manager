namespace Nexus.Client.Games.Starfield.Tools.AI
{
    using System;
	using System.Diagnostics;
	using System.IO;
	using System.Windows.Forms;

	using Nexus.Client.Util;
    using Nexus.Client.Games.Tools;
    using Nexus.Client.Commands;
	
    /// <summary>
	/// Controls ArchiveInvalidation.
	/// </summary>
	public class ArchiveInvalidation : ITool
	{
		#region Events

		/// <summary>
		/// Notifies listeners that the tool wants a view displayed.
		/// </summary>
		public event EventHandler<DisplayToolViewEventArgs> DisplayToolView = delegate { };

		/// <summary>
		/// Notifies listeners that the tool wants a view closed.
		/// </summary>
		public event EventHandler<DisplayToolViewEventArgs> CloseToolView = delegate { };

		#endregion

		private IToolView m_tvwToolView = null;

		#region Properties

		/// <summary>
		/// Gets the command to launch the tool.
		/// </summary>
		/// <value>The command to launch the tool.</value>
		public Command LaunchCommand { get; private set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		protected StarfieldGameMode GameMode { get; private set; }

		#endregion

		/// <summary>
		/// Confirms that AI should be reset.
		/// </summary>
		public Func<bool> ConfirmAiReset = delegate { return true; };

		#region Constructor

		/// <summary>
		/// Initialized the object with the given values.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		public ArchiveInvalidation(StarfieldGameMode p_gmdGameMode)
		{
			GameMode = p_gmdGameMode;
			LaunchCommand = new Command("Reset Archive Invalidation", "Resets Archive Invalidation.", ResetArchiveInvalidation);
		}

		#endregion

		/// <summary>
		/// Sets the view to use for this tool.
		/// </summary>
		/// <param name="p_tvwToolView">The view to use for this tool.</param>
		public void SetToolView(IToolView p_tvwToolView)
		{
			m_tvwToolView = p_tvwToolView;
		}

		/// <summary>
		/// Resets archive invalidation.
		/// </summary>
		public void ResetArchiveInvalidation()
		{
			DisplayToolViewEventArgs teaArgs = new DisplayToolViewEventArgs(m_tvwToolView, true);
			DisplayToolView(this, teaArgs);
			ApplyAI();
			CloseToolView(this, teaArgs);
		}

        /// <summary>
        /// Enables AI.
        /// </summary>
        protected void ApplyAI()
        {
            if (ConfirmAiReset())
            {
                string pluginsPath = GameMode.PluginDirectory;

                try
                {
					GameMode.RequiresExternalConfig(out pluginsPath);

				}
                catch (Exception ex)
                {
                    Trace.TraceError("ApplyAI - Could not set LastWriteTime.");
                    TraceUtil.TraceException(ex);

                    MessageBox.Show(
                        "Could not apply Archive Invalidation, at least one file could not be modified.\n" +
                        "Please try again, or check trace log for more info.\n\n" +
                        ex.Message,
                        "Archive Invalidation failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
				}
            }
        }
	}
}
