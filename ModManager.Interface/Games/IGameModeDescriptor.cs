
namespace Nexus.Client.Games
{
	/// <summary>
	/// Describes the properties and methods of a game mode descriptor.
	/// </summary>
	/// <remarks>
	/// A game mode descriptor provides the basic info about a game mode. It provides
	/// the information that differentiates one game mode from another, such as the name and id.
	/// </remarks>
	public interface IGameModeDescriptor
	{
		#region Properties

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		string Name { get; }

		/// <summary>
		/// Gets the unique id of the game mode.
		/// </summary>
		/// <value>The unique id of the game mode.</value>
		string ModeId { get; }

		/// <summary>
		/// Gets the list of possible executable files for the game.
		/// </summary>
		/// <value>The list of possible executable files for the game.</value>
		string[] GameExecutables { get; }

		/// <summary>
		/// Gets the path to which mod files should be installed.
		/// </summary>
		/// <value>The path to which mod files should be installed.</value>
		string InstallationPath { get; }

		/// <summary>
		/// Gets the path to the game executable.
		/// </summary>
		/// <value>The path to the game executable.</value>
		string ExecutablePath { get; }

		/// <summary>
		/// Gets the list of critical plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin names, ordered by load order.</value>
		string[] OrderedCriticalPluginNames { get; }

		/// <summary>
		/// Gets the theme to use for this game mode.
		/// </summary>
		/// <value>The theme to use for this game mode.</value>
		Theme ModeTheme { get; }

		/// <summary>
		/// Gets the custom message for missing critical files.
		/// </summary>
		/// <value>The custom message for missing critical files.</value>
		string CriticalFilesErrorMessage { get; }

		/// <summary>
		/// Gets the directory where the game plugins are installed.
		/// </summary>
		/// <value>The directory where the game plugins are installed.</value>
		string PluginDirectory { get; }

		#endregion
	}
}
