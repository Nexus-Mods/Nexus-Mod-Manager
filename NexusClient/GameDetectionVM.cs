using Nexus.Client.Games;

namespace Nexus.Client
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display the progress of game detection.
	/// </summary>
	public class GameDetectionVM
	{
		#region Properties

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		public IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the discoverer to use to find the game installation path.
		/// </summary>
		/// <value>The discoverer to use to find the game installation path.</value>
		public GameDiscoverer GameDetector { get; private set; }

		/// <summary>
		/// Gets the list of supported game modes.
		/// </summary>
		/// <value>The list of supported game modes.</value>
		public GameModeRegistry SupportedGameModes { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gdvGameDetector">The discoverer to use to find the game installation path.</param>
		/// <param name="p_gmrSupportedGameModes">The list of supported game modes.</param>
		public GameDetectionVM(IEnvironmentInfo p_eifEnvironmentInfo, GameDiscoverer p_gdvGameDetector, GameModeRegistry p_gmrSupportedGameModes)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameDetector = p_gdvGameDetector;
			SupportedGameModes = p_gmrSupportedGameModes;
		}

		#endregion

		/// <summary>
		/// Cancels the search for installed games.
		/// </summary>
		public void Cancel()
		{
			GameDetector.Cancel();
		}
	}
}
