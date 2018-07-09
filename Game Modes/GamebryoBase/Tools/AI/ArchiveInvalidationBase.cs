using System;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Games.Tools;
using System.Windows.Forms;

namespace Nexus.Client.Games.Gamebryo.Tools.AI
{
	/// <summary>
	/// UI.Controls ArchiveInvalidation.
	/// </summary>
	public abstract class ArchiveInvalidationBase : ITool
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
		protected GamebryoGameModeBase GameMode { get; private set; }

		#endregion

		/// <summary>
		/// Confirms that AI should be enabled.
		/// </summary>
		public Func<bool> ConfirmAiEnabling = delegate { return true; };

		/// <summary>
		/// Confirms that AI should be disabled.
		/// </summary>
		public Func<bool> ConfirmAiDisabling = delegate { return true; };

		#region Constructor

		/// <summary>
		/// Initialized the object with the given values.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		public ArchiveInvalidationBase(GamebryoGameModeBase p_gmdGameMode)
		{
			GameMode = p_gmdGameMode;
			LaunchCommand = new CheckedCommand("Archive Invalidation", "Toggles Archive Invalidation.", IsActive(), ToggleArchiveInvalidation);
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
		/// Toggles archive invalidation.
		/// </summary>
		public void ToggleArchiveInvalidation()
		{
			try
			{
				if (!File.Exists(GameMode.SettingsFiles.IniPath))
				{
					MessageBox.Show(String.Format("You have no {0} INI file. Please run {0} to initialize the file before turning on Archive Invalidation.", GameMode.Name), "Missing INI", MessageBoxButtons.OK, MessageBoxIcon.Information);
					return;
				}
				DisplayToolViewEventArgs teaArgs = new DisplayToolViewEventArgs(m_tvwToolView, true);
				DisplayToolView(this, teaArgs);
				if (Update())
					((CheckedCommand)LaunchCommand).IsChecked = IsActive();
				CloseToolView(this, teaArgs);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
		}

		/// <summary>
		/// Updates AI by toggling its status.
		/// </summary>
		/// <returns><c>true</c> if AI has been toggled;
		/// <c>false</c> otherwise.</returns>
		private bool Update()
		{
			string strFalloutPath = GameMode.SettingsFiles.IniPath;
			if (!File.Exists(strFalloutPath))
				throw new Exception(String.Format("You have no {0} INI file. Please run {0} to initialize the file.", GameMode.Name));
			if (!IsActive())
			{
				if (ConfirmAiEnabling())
				{
					ApplyAI();
					return true;
				}
			}
			else
			{
				if (ConfirmAiDisabling())
				{
					RemoveAI();
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Gets whether AI is enabled.
		/// </summary>
		/// <returns><c>true</c> if AI is enabled;
		/// <c>false</c> otherwise.</returns>
		public abstract bool IsActive();

		/// <summary>
		/// Enables AI.
		/// </summary>
		protected abstract void ApplyAI();

		/// <summary>
		/// Disables AI.
		/// </summary>
		protected abstract void RemoveAI();
	}
}
