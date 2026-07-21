using System.Collections.Generic;
using Nexus.Client.Games;

namespace Nexus.Client.Settings.UI
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display application settings.
	/// </summary>
	public class SettingsFormVM
	{
		#region Properties

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		public IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		public IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the settings group views to display in the form.
		/// </summary>
		/// <value>The settings group views to display in the form.</value>
		public IEnumerable<ISettingsGroupView> SettingsGroups { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_enmSettingsGroups">The settings group views to display in the form.</param>
		public SettingsFormVM(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IEnumerable<ISettingsGroupView> p_enmSettingsGroups)
		{
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			SettingsGroups = p_enmSettingsGroups;
		}

		#endregion

		/// <summary>
		/// Saves the settings.
		/// </summary>
		/// <returns><c>true</c> if the settings were successfully saved;
		/// <c>false</c> otherwise.</returns>
		public bool Save()
		{
			foreach (ISettingsGroupView sgvView in SettingsGroups)
			{
				if (!sgvView.SettingsGroup.Save())
					return false;
			}
			return true;
		}
	}
}
