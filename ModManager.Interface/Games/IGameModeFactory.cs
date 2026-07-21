using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.Games
{
	/// <summary>
	/// Describes the properties and methods of a game mode factory.
	/// </summary>
	/// <remarks>
	/// A game mode factory builds a game mode.
	/// </remarks>
	public interface IGameModeFactory
	{
		#region Properties

		/// <summary>
		/// Gets the descriptor of the game mode that this factory builds.
		/// </summary>
		/// <value>The descriptor of the game mode that this factory builds.</value>
		IGameModeDescriptor GameModeDescriptor { get; }

		#endregion

		/// <summary>
		/// Gets the path where mod files should be installed.
		/// </summary>
		/// <returns>The path where mod files should be installed, or
		/// <c>null</c> if the path could be be determined.</returns>
		string GetInstallationPath();

		/// <summary>
		/// Gets the path where mod files should be installed.
		/// </summary>
		/// <remarks>
		/// This method uses the given path to the installed game
		/// to determine the installaiton path for mods.
		/// </remarks>
		/// <returns>The path where mod files should be installed, or
		/// <c>null</c> if the path could be be determined.</returns>
		string GetInstallationPath(string p_strGameInstallPath);

		/// <summary>
		/// Gets the path to the game executable.
		/// </summary>
		/// <remarks>
		/// This method uses the given path to the installed game
		/// to determine the path to the game executable.
		/// </remarks>
		/// <returns>The path to the game executable, or
		/// <c>null</c> if the path could be be determined.</returns>
		string GetExecutablePath(string p_strGameInstallPath);

		/// <summary>
		/// Builds the game mode.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <param name="p_imsWarning">The resultant warning resultant from the creation of the game mode.
		/// <c>null</c> if there are no warnings.</param>
		/// <returns>The game mode.</returns>
		IGameMode BuildGameMode(FileUtil p_futFileUtility, out ViewMessage p_imsWarning);

		/// <summary>
		/// Performs the initial setup for the game mode.
		/// </summary>
		/// <param name="p_dlgShowView">The delegate to use to display a view.</param>
		/// <param name="p_dlgShowMessage">The delegate to use to display a message.</param>
		/// <returns><c>true</c> if the setup completed successfully;
		/// <c>false</c> otherwise.</returns>
		bool PerformInitialSetup(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage);

		/// <summary>
		/// Performs the initializtion for the game mode being created.
		/// </summary>
		/// <param name="p_dlgShowView">The delegate to use to display a view.</param>
		/// <param name="p_dlgShowMessage">The delegate to use to display a message.</param>
		/// <returns><c>true</c> if the setup completed successfully;
		/// <c>false</c> otherwise.</returns>
		bool PerformInitialization(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage);
	}
}
