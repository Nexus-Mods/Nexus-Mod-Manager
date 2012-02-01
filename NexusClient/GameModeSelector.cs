using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Nexus.Client.Games;

namespace Nexus.Client
{
	/// <summary>
	/// Chooses the current game mode.
	/// </summary>
	/// <remarks>
	/// This selector checks the following to select the current game mode:
	/// -command line
	/// -remembered game mode in settings
	/// -ask the user
	/// </remarks>
	public class GameModeSelector
	{
		#region Properties

		/// <summary>
		/// Gets or sets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public GameModeSelector(IEnvironmentInfo p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
		}

		#endregion

		/// <summary>
		/// Selectes the current game mode.
		/// </summary>
		/// <param name="p_strArgs">The command line arguments.</param>
		/// <param name="p_booChangeGameMode">Whether the user has requested a game mode change.</param>
		/// <returns>The <see cref="IGameModeFactory"/> that can build the game mode selected by the user.</returns>
		public IGameModeFactory SelectGameMode(string[] p_strArgs, bool p_booChangeGameMode)
		{
			/*Trace.Write("Determining Game Mode: ");

			string strSelectedGame = EnvironmentInfo.Settings.RememberGameMode ? EnvironmentInfo.Settings.RememberedGameMode : null;
			if (!p_booChangeGameMode)
			{
				for (Int32 i = 0; i < p_strArgs.Length; i++)
				{
					string strArg = p_strArgs[i];
					if (strArg.StartsWith("-"))
					{
						switch (strArg)
						{
							case "-game":
								Trace.Write("(From Command line: " + p_strArgs[i + 1] + ") ");
								if (!m_dicGameModeFactories.ContainsKey(p_strArgs[i + 1]))
									MessageBox.Show(String.Format("Unrecognized -game Parameter: {0}", p_strArgs[i + 1]), "Unrecognized Game Mode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
								else
									strSelectedGame = p_strArgs[i + 1];
								break;
						}
					}
				}
			}

			if (p_booChangeGameMode || String.IsNullOrEmpty(strSelectedGame))
			{
				Trace.Write("(From Selection Form) ");
				List<IGameModeDescriptor> lstGameModeInfos = new List<IGameModeDescriptor>();
				foreach (IGameModeFactory gmfFactory in m_dicGameModeFactories.Values)
					lstGameModeInfos.Add(gmfFactory.GameModeDescriptor);
				GameModeSelectionForm msfSelector = new GameModeSelectionForm(lstGameModeInfos, EnvironmentInfo.Settings);
				msfSelector.ShowDialog();
				strSelectedGame = msfSelector.SelectedGameModeId;
			}
			Trace.WriteLine(strSelectedGame);
			if (!m_dicGameModeFactories.ContainsKey(strSelectedGame))
			{
				string strError = String.Format("Unrecognized Game Mode: {0}", p_strArgs[1]);
				Trace.TraceError(strError);
				MessageBox.Show(strError, "Unrecognized Game Mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return null;
			}

			return m_dicGameModeFactories[strSelectedGame];*/
			return null;
		}
	}
}
