using Nexus.Client.Games.Settings;

namespace Nexus.Client.Games
{
	/// <summary>
	/// Describes the properties and methods of a setup view model.
	/// </summary>
	/// <remarks>
	/// A setup view model encapsulates the data and the operations presented by UI
	/// elements that display a game mode setup view.
	/// </remarks>
	public interface ISetupVM
	{
		#region Properties

		/// <summary>
		/// Gets the descriptor of the current game mode.
		/// </summary>
		/// <value>The descriptor of the current game mode.</value>
		IGameModeDescriptor GameModeDescriptor { get; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for diaplying a required directories
		/// UI view.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for diaplying a required directories
		/// UI view.</value>
		SetupDirectoriesControlVM SetupDirectoriesControlVM { get; }

		/// <summary>
		/// Gets whether the setup is complete.
		/// </summary>
		/// <value>Whether the setup is complete.</value>
		bool IsSetupComplete { get; }

		#endregion

		/// <summary>
		/// Save the changes that the setup has performed.
		/// </summary>
		/// <returns><c>true</c> if the changes were saved;
		/// <c>false</c> otherwise.</returns>
		bool Save();
	}
}
