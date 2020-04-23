namespace Nexus.Client.Games
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Windows.Forms;

    using Exceptions;

    /// <summary>
    /// A registry of all game modes whose mods can be managed by the application.
    /// </summary>
    public class GameModeRegistry
	{
		/// <summary>
		/// Searches for game mode factories in the specified path, and loads
		/// any factories that are found into a registry.
		/// </summary>
		/// <returns>A registry containing all of the discovered game mode factories.</returns>
		public static GameModeRegistry DiscoverSupportedGameModes(EnvironmentInfo environmentInfo)
		{
			Trace.TraceInformation("Discovering Game Mode Factories...");
			Trace.Indent();

            var appDirectory = Path.GetDirectoryName(Application.ExecutablePath);
			var gameModesPath = Path.Combine(appDirectory ?? string.Empty, "GameModes");

		    if (!Directory.Exists(gameModesPath))
            {
                Directory.CreateDirectory(gameModesPath);
            }

            Trace.TraceInformation("Looking in: {0}", gameModesPath);

		    var assemblies = Directory.GetFiles(gameModesPath, "*.dll");

            //If there are no assemblies detected then an exception must be thrown
            //to prevent a divide by zero exception further along
		    if (!assemblies.Any())
		    {
#if DEBUG
				throw new GameModeRegistryException(gameModesPath, "Compile the Game Modes directory in the solution.");
#else
				throw new GameModeRegistryException(gameModesPath);
#endif
            }

			var registry = new GameModeRegistry();

		    foreach (var assembly in assemblies)
			{
				Trace.TraceInformation("Checking: {0}", Path.GetFileName(assembly));
				Trace.Indent();

				var gameMode = Assembly.LoadFrom(assembly);

			    try
				{
					var types = gameMode.GetExportedTypes();

				    foreach (var type in types)
					{
					    if (!typeof(IGameModeFactory).IsAssignableFrom(type) || type.IsAbstract) continue;

					    Trace.TraceInformation("Initializing: {0}", type.FullName);
					    Trace.Indent();

					    var constructor = type.GetConstructor(new[] { typeof(IEnvironmentInfo) });

					    if (constructor == null)
					    {
					        Trace.TraceInformation("No constructor accepting one argument of type IEnvironmentInfo found.");
					        Trace.Unindent();

					        continue;
					    }

					    var gmfGameModeFactory = (IGameModeFactory)constructor.Invoke(new object[] { environmentInfo });
					    registry.RegisterGameMode(gmfGameModeFactory);

					    Trace.Unindent();
					}
				}
				catch (FileNotFoundException e)
				{
					Trace.TraceError($"Cannot load {assembly}: cannot find dependency {e.FileName}");
					// some dependencies were missing, so we couldn't load the assembly
					// given that these are plugins we don't have control over the dependencies:
					// we may not even know what they (we can get their name, but if it's a custom
					// dll not part of the client code base, we can't provide it even if we wanted to)
					// there's nothing we can do, so simply skip the assembly
				}

			    Trace.Unindent();
			}

		    Trace.Unindent();

			return registry;
		}

		/// <summary>
		/// Loads the factories for games that have been previously detected as installed.
		/// </summary>
		/// <param name="supportedGameModes">A registry containing the factories for all supported game modes.</param>
		/// <param name="environmentInfo">The application's environment info.</param>
		/// <returns>A registry containing all of the game mode factories for games that were previously detected as installed.</returns>
		public static GameModeRegistry LoadInstalledGameModes(GameModeRegistry supportedGameModes, EnvironmentInfo environmentInfo)
		{
			Trace.TraceInformation("Loading Game Mode Factories for Installed Games...");
			Trace.Indent();
			
			var installedGameModes = new GameModeRegistry();

            foreach (var gameId in environmentInfo.Settings.InstalledGames)
			{
				Trace.Write($"Loading {gameId}: ");

                if (supportedGameModes.IsRegistered(gameId))
				{
					Trace.WriteLine("Supported");
					installedGameModes.RegisterGameMode(supportedGameModes.GetGameMode(gameId));
				}
				else
                {
                    Trace.WriteLine("Not Supported");
                }
            }
			
			Trace.Unindent();
			return installedGameModes;
		}

		private readonly Dictionary<string, IGameModeFactory> _gameModeFactories = new Dictionary<string, IGameModeFactory>(StringComparer.OrdinalIgnoreCase);

        #region Properties

		/// <summary>
		/// Gets the list of registered game modes.
		/// </summary>
		/// <value>The list of registered game modes.</value>
		public IEnumerable<IGameModeDescriptor> RegisteredGameModes
		{
			get
			{
				foreach (var gameModeFactory in _gameModeFactories.Values)
                {
                    yield return gameModeFactory.GameModeDescriptor;
                }
            }
		}

		/// <summary>
		/// Gets the list of factories of the registered game modes.
		/// </summary>
		/// <value>The list of factories of the registered game modes.</value>
		public IEnumerable<IGameModeFactory> RegisteredGameModeFactories => _gameModeFactories.Values;

        #endregion

        /// <summary>
		/// Registers the specified game mode.
		/// </summary>
		/// <param name="gameModeFactory">The factory for the game mode to register.</param>
		public void RegisterGameMode(IGameModeFactory gameModeFactory)
		{
			if (_gameModeFactories.ContainsKey(gameModeFactory.GameModeDescriptor.ModeId))
			{
				var error = $"{_gameModeFactories[gameModeFactory.GameModeDescriptor.ModeId].GameModeDescriptor.Name} has the same Game Mode Id as {gameModeFactory.GameModeDescriptor.Name}. {_gameModeFactories[gameModeFactory.GameModeDescriptor.ModeId].GameModeDescriptor.Name} will be replaced in the registry.";
				Trace.TraceWarning(error);
			}

			_gameModeFactories[gameModeFactory.GameModeDescriptor.ModeId] = gameModeFactory;
		}

		/// <summary>
		/// Determines if the specified game mode is in the registry.
		/// </summary>
		/// <param name="gameModeId">The id of the game mode whose presence in the registry is to be determined.</param>
		/// <returns><c>true</c> if the specified game mode is in the registry;
		/// <c>false</c> otherwise.</returns>
		public bool IsRegistered(string gameModeId)
		{
			return _gameModeFactories.ContainsKey(gameModeId);
		}

		/// <summary>
		/// Gets the game mode factory registered for the given game mode id.
		/// </summary>
		/// <param name="gameModeId">The id of the game mode for which to retrieve a factory.</param>
		/// <returns>The game mode factory registered for the given game mode id,
		/// or <c>null</c> if no factory is registered for the given id.</returns>
		public IGameModeFactory GetGameMode(string gameModeId)
		{
            _gameModeFactories.TryGetValue(gameModeId, out var gmfFactory);
			return gmfFactory;
		}
	}
}
