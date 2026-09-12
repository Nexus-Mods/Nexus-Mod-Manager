using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.ModManagement;
using Nexus.Client.Settings;

namespace Nexus.Client.Games.Settings
{
	/// <summary>
	/// This class encapsulates the common data and the operations presented by UI
	/// elements that display the setup for a game mode.
	/// </summary>
	public class VirtualDirectoriesSetupVM
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
		public VirtualDirectoriesControlVM VirtualDirectoriesControlVM { get; private set; }

		/// <summary>
		/// Gets whether the setup is complete.
		/// </summary>
		/// <value>Whether the setup is complete.</value>
		public bool IsSetupComplete { get; private set; }

		/// <summary>
		/// Gets whether storage paths changed during the last save.
		/// </summary>
		public bool StoragePathsChanged { get; private set; }

		/// <summary>
		/// Gets whether the per-game install method changed during the last save.
		/// </summary>
		public bool InstallMethodChanged { get; private set; }

		/// <summary>
		/// Gets or sets the preferred install method for this game.
		/// </summary>
		public ModInstallMethod PreferredInstallMethod { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameModeInfo">The descriptor for the game mode being set up.</param>
		public VirtualDirectoriesSetupVM(IEnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo, IVirtualModActivator p_ivaVirtualActivator)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameModeDescriptor = p_gmdGameModeInfo;
			PreferredInstallMethod = p_eifEnvironmentInfo.Settings.GetPreferredInstallMethod(p_gmdGameModeInfo.ModeId);
			VirtualDirectoriesControlVM = new VirtualDirectoriesControlVM(p_eifEnvironmentInfo, p_gmdGameModeInfo, p_ivaVirtualActivator);
		}

		#endregion

		/// <summary>
		/// Save the changes that the setup has performed.
		/// </summary>
		/// <returns><c>true</c> if the changes were saved;
		/// <c>false</c> otherwise.</returns>
		public bool Save()
		{
			StoragePathsChanged = false;
			InstallMethodChanged = false;
			if (!VirtualDirectoriesControlVM.ValidateSettings())
				return false;

			StoragePathsChanged = VirtualDirectoriesControlVM.SaveSettings();
			ModInstallMethod currentMethod = EnvironmentInfo.Settings.GetPreferredInstallMethod(GameModeDescriptor.ModeId);
			if (currentMethod != PreferredInstallMethod)
			{
				EnvironmentInfo.Settings.SetPreferredInstallMethod(GameModeDescriptor.ModeId, PreferredInstallMethod);
				EnvironmentInfo.Settings.Save();
				InstallMethodChanged = true;
			}

			IsSetupComplete = true;
			return StoragePathsChanged || InstallMethodChanged;
		}
	}
}

