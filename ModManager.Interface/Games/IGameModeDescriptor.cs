
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
		/// Gets the theme to use for this game mode.
		/// </summary>
		/// <value>The theme to use for this game mode.</value>
		Theme ModeTheme { get; }

		#endregion
	}
}
