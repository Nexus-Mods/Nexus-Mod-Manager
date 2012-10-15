
namespace Nexus.Client.Games
{
	/// <summary>
	/// The base class for game mode descriptors.
	/// </summary>
	/// <remarks>
	/// This implements functionality common to all game mode descriptors.
	/// </remarks>
	public abstract class GameModeDescriptorBase : IGameModeDescriptor
	{
		#region Properties

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public abstract string Name { get; }

		/// <summary>
		/// Gets the unique id of the game mode.
		/// </summary>
		/// <value>The unique id of the game mode.</value>
		public abstract string ModeId { get; }

		/// <summary>
		/// Gets the list of possible executable files for the game.
		/// </summary>
		/// <value>The list of possible executable files for the game.</value>
		public abstract string[] GameExecutables { get; }

		/// <summary>
		/// Gets the path to which mod files should be installed.
		/// </summary>
		/// <value>The path to which mod files should be installed.</value>
		public string InstallationPath
		{
			get
			{
				if (EnvironmentInfo.Settings.InstallationPaths.ContainsKey(ModeId))
					return (string)EnvironmentInfo.Settings.InstallationPaths[ModeId];
				return null;
			}
		}

		/// <summary>
		/// Gets the path to the game executable.
		/// </summary>
		/// <value>The path to the game executable.</value>
		public string ExecutablePath
		{
			get
			{
				if (EnvironmentInfo.Settings.ExecutablePaths.ContainsKey(ModeId))
					return (string)EnvironmentInfo.Settings.ExecutablePaths[ModeId];
				return null;
			}
		}

		/// <summary>
		/// Gets the list of critical plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin names, ordered by load order.</value>
		public abstract string[] OrderedCriticalPluginNames { get; }

		/// <summary>
		/// Gets the theme to use for this game mode.
		/// </summary>
		/// <value>The theme to use for this game mode.</value>
		public abstract Theme ModeTheme { get; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public GameModeDescriptorBase(IEnvironmentInfo p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
		}

		#endregion
	}
}
