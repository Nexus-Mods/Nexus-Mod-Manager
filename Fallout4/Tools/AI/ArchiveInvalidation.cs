﻿using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games.Gamebryo.Tools.AI;
using Nexus.Client.Util;
using Nexus.Client.Games.Tools;
using Nexus.Client.Commands;
using Nexus.Client.Games.Gamebryo;

namespace Nexus.Client.Games.Fallout4.Tools.AI
{
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
		protected Fallout4GameMode GameMode { get; private set; }

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
		public ArchiveInvalidation(Fallout4GameMode p_gmdGameMode)
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
                string strPluginsPath = GameMode.PluginDirectory;
                foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("Fallout4 - *.ba2"))
                    fi.LastWriteTime = new DateTime(2008, 10, 1);
                foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("DLCRobot - *.ba2"))
                    fi.LastWriteTime = new DateTime(2008, 10, 2);
                foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("DLCworkshop01 - *.ba2"))
                    fi.LastWriteTime = new DateTime(2008, 10, 3);
                foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("DLCCoast - *.ba2"))
                    fi.LastWriteTime = new DateTime(2008, 10, 4);
                foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("DLCworkshop02 - *.ba2"))
                    fi.LastWriteTime = new DateTime(2008, 10, 5);
                foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("DLCworkshop03 - *.ba2"))
                    fi.LastWriteTime = new DateTime(2008, 10, 6);
                foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("DLCNukaWorld - *.ba2"))
                    fi.LastWriteTime = new DateTime(2008, 10, 7);
            }
        }
	}
}
