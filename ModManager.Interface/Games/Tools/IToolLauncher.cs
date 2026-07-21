using System.Collections.Generic;

namespace Nexus.Client.Games.Tools
{
	/// <summary>
	/// Describes the properties and methods of a game mode's tool launcher.
	/// </summary>
	/// <remarks>
	/// A tool launcher exposes tool that the client can launch. Tools add functionality on a per game mode basis.
	/// </remarks>
	public interface IToolLauncher
	{
		#region Properties

		/// <summary>
		/// Gets the tools associated with the game mode.
		/// </summary>
		/// <value>The tools associated with the game mode.</value>
		IEnumerable<ITool> Tools { get; }

		#endregion
	}
}
