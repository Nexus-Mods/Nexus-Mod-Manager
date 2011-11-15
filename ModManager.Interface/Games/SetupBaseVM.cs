using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.Games.Settings;

namespace Nexus.Client.Games
{
	/// <summary>
	/// This class encapsulates the common data and the operations presented by UI
	/// elements that display the setup for a game mode.
	/// </summary>
	public class SetupBaseVM : ISetupVM
	{
		#region Properties

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		public IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the descriptor of the current game mode.
		/// </summary>
		/// <value>The descriptor of the current game mode.</value>
		public IGameModeDescriptor GameModeDescriptor { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for diaplying a required directories
		/// UI view.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for diaplying a required directories
		/// UI view.</value>
		public RequiredDirectoriesControlVM RequiredDirectoriesControlVM { get; private set; }

		/// <summary>
		/// Gets whether the setup is complete.
		/// </summary>
		/// <value>Whether the setup is complete.</value>
		public bool IsSetupComplete { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameModeInfo">The descriptor for the game mode being set up.</param>
		public SetupBaseVM(IEnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameModeDescriptor = p_gmdGameModeInfo;
			RequiredDirectoriesControlVM = new RequiredDirectoriesControlVM(p_eifEnvironmentInfo, p_gmdGameModeInfo);
		}

		#endregion
		
		/// <summary>
		/// Save the changes that the setup has performed.
		/// </summary>
		/// <returns><c>true</c> if the changes were saved;
		/// <c>false</c> otherwise.</returns>
		public bool Save()
		{
			if (RequiredDirectoriesControlVM.ValidateSettings())
			{
				RequiredDirectoriesControlVM.SaveSettings();
				EnvironmentInfo.Settings.Save();
				IsSetupComplete = true;
				return true;
			}
			return false;
		}
	}
}
