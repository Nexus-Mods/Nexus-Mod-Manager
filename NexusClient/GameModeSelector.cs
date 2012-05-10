using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Nexus.Client.Games;
using System.Text;

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

		/// <summary>
		/// Gets or sets the game modes factories for supported games.
		/// </summary>
		/// <value>The game modes factories for supported games.</value>
		protected GameModeRegistry SupportedGameModes { get; set; }

		/// <summary>
		/// Gets or sets the game modes factories for installed games.
		/// </summary>
		/// <value>The game modes factories for installed games.</value>
		protected GameModeRegistry InstalledGameModes { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmrSupportedGameModes">The games modes supported by the mod manager.</param>
		/// <param name="p_gmrInstalledGameModes">The game modes factories for installed games.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public GameModeSelector(GameModeRegistry p_gmrSupportedGameModes, GameModeRegistry p_gmrInstalledGameModes, IEnvironmentInfo p_eifEnvironmentInfo)
		{
			SupportedGameModes = p_gmrSupportedGameModes;
			InstalledGameModes = p_gmrInstalledGameModes;
			EnvironmentInfo = p_eifEnvironmentInfo;
		}

		#endregion

		/// <summary>
		/// Selectes the current game mode.
		/// </summary>
		/// <param name="p_strRequestedGameMode">The id of the game mode we want to select.</param>
		/// <param name="p_booChangeDefaultGameMode">Whether the users ahs requested a change to the default game mode.</param>
		/// <returns>The <see cref="IGameModeFactory"/> that can build the game mode selected by the user.</returns>
		public IGameModeFactory SelectGameMode(string p_strRequestedGameMode, bool p_booChangeDefaultGameMode)
		{
			Trace.Write("Determining Game Mode: ");

			string strSelectedGame = EnvironmentInfo.Settings.RememberGameMode ? EnvironmentInfo.Settings.RememberedGameMode : null;
			if (!String.IsNullOrEmpty(p_strRequestedGameMode))
			{
				Trace.Write("(Requested Mode: " + p_strRequestedGameMode + ") ");
				strSelectedGame = p_strRequestedGameMode;
			}

			if (p_booChangeDefaultGameMode || String.IsNullOrEmpty(strSelectedGame))
			{
				Trace.Write("(From Selection Form) ");
				List<IGameModeDescriptor> lstGameModeInfos = new List<IGameModeDescriptor>();
				foreach (IGameModeDescriptor gmdGameMode in InstalledGameModes.RegisteredGameModes)
					lstGameModeInfos.Add(gmdGameMode);
				GameModeSelectionForm msfSelector = new GameModeSelectionForm(lstGameModeInfos, EnvironmentInfo.Settings);
				msfSelector.ShowDialog();
                if (msfSelector.DialogResult == DialogResult.OK)
                    strSelectedGame = msfSelector.SelectedGameModeId;
                else
                    return null;
			}
			Trace.WriteLine(strSelectedGame);
			if (!InstalledGameModes.IsRegistered(strSelectedGame))
			{
				StringBuilder stbError = new StringBuilder();
				if (!SupportedGameModes.IsRegistered(strSelectedGame))
					stbError.AppendFormat("Unrecognized Game Mode: {0}", strSelectedGame);
				else
				{
					stbError.AppendFormat("{0} is not set up to work with {1}", EnvironmentInfo.Settings.ModManagerName, SupportedGameModes.GetGameMode(strSelectedGame).GameModeDescriptor.Name).AppendLine();
					stbError.AppendFormat("If {0} is installed, rescan for installed games from the Change Game toolbar item.", SupportedGameModes.GetGameMode(strSelectedGame).GameModeDescriptor.Name).AppendLine();
				}
				Trace.TraceError(stbError.ToString());
				MessageBox.Show(stbError.ToString(), "Unrecognized Game Mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return null;
			}

			return InstalledGameModes.GetGameMode(strSelectedGame);
		}
	}
}
