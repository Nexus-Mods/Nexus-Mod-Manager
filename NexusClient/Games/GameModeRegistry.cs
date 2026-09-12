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
    using Nexus.Client.Games.DataDriven;

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

            var appDirectory = Path.GetDirectoryName(Application.ExecutablePath) ?? string.Empty;
			var gameModesPath = Path.Combine(appDirectory, "GameModes");
            var definitionsPath = Path.Combine(gameModesPath, "Definitions");

		    if (!Directory.Exists(gameModesPath))
            {
                Directory.CreateDirectory(gameModesPath);
            }

            Trace.TraceInformation("Looking in: {0}", gameModesPath);

		    var assemblies = Directory.GetFiles(gameModesPath, "*.dll")
                .Where(IsPotentialGameModeAssembly)
                .ToArray();
            bool hasDefinitions = Directory.Exists(definitionsPath) && Directory.EnumerateFiles(definitionsPath, "*.json", SearchOption.AllDirectories).Any();

            //If there are no assemblies detected then an exception must be thrown
            //to prevent a divide by zero exception further along
		    if (!assemblies.Any() && !hasDefinitions)
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

                try
                {
                    var gameMode = Assembly.LoadFrom(assembly);
                    var types = gameMode.GetExportedTypes();

                    foreach (var type in types)
                    {
                        if (!typeof(IGameModeFactory).IsAssignableFrom(type) || type.IsAbstract) continue;

                        Trace.TraceInformation("Initializing: {0}", type.FullName);
                        Trace.Indent();
                        try
                        {
                            var constructor = type.GetConstructor(new[] { typeof(IEnvironmentInfo) });
                            if (constructor == null)
                            {
                                Trace.TraceInformation("No constructor accepting one argument of type IEnvironmentInfo found.");
                                continue;
                            }

                            var gmfGameModeFactory = (IGameModeFactory)constructor.Invoke(new object[] { environmentInfo });
                            registry.RegisterGameMode(gmfGameModeFactory);
                        }
                        finally
                        {
                            Trace.Unindent();
                        }
                    }
                }
                catch (Exception e)
                {
                    TraceAssemblyLoadFailure(assembly, e);
                }
                finally
                {
                    Trace.Unindent();
                }
			}

            registry.RegisterDataDrivenGameModes(environmentInfo, definitionsPath);

		    Trace.Unindent();

			return registry;
		}

        /// <summary>
        /// Determines whether an assembly can contain a Game Mode factory without loading it into the execution context.
        /// </summary>
        private static bool IsPotentialGameModeAssembly(string assembly)
        {
            var fileName = Path.GetFileName(assembly);
            if (String.IsNullOrEmpty(fileName))
                return false;

            if (IsScriptTypeAssembly(fileName))
            {
                Trace.TraceInformation("Ignoring Script Type assembly during Game Mode discovery: {0}", fileName);
                return false;
            }

            try
            {
                var reflectionAssembly = Assembly.ReflectionOnlyLoadFrom(assembly);
                var contractAssemblyName = typeof(IGameModeFactory).Assembly.GetName().Name;
                if (reflectionAssembly.GetReferencedAssemblies().Any(reference => String.Equals(reference.Name, contractAssemblyName, StringComparison.OrdinalIgnoreCase)))
                    return true;

                Trace.TraceInformation("Ignoring non-Game-Mode assembly: {0}", fileName);
            }
            catch (Exception e)
            {
                Trace.TraceWarning("Ignoring unreadable assembly {0}: {1}: {2}", fileName, e.GetType().Name, e.Message);
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified file is a generic or game-specific built-in Script Type assembly.
        /// </summary>
        private static bool IsScriptTypeAssembly(string fileName)
        {
            string[] scriptTypeAssemblies = { "CSharpScript.dll", "ModScript.dll", "XmlScript.dll" };
            foreach (var scriptTypeAssembly in scriptTypeAssemblies)
                if (fileName.Equals(scriptTypeAssembly, StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith("." + scriptTypeAssembly, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        /// <summary>
        /// Traces an assembly discovery failure without aborting the remaining plugin discovery.
        /// </summary>
        private static void TraceAssemblyLoadFailure(string assembly, Exception exception)
        {
            Trace.TraceError("Cannot load {0}: {1}: {2}", assembly, exception.GetType().Name, exception.Message);
            var reflectionTypeLoadException = exception as ReflectionTypeLoadException;
            if (reflectionTypeLoadException == null)
                return;

            foreach (var loaderException in reflectionTypeLoadException.LoaderExceptions.Where(item => item != null))
                Trace.TraceError("    Loader exception: {0}: {1}", loaderException.GetType().Name, loaderException.Message);
        }

        private void RegisterDataDrivenGameModes(EnvironmentInfo environmentInfo, string definitionsPath)
        {
            var result = new GameModeDefinitionLoader().LoadFromDirectory(definitionsPath);
            foreach (var issue in result.Issues)
                Trace.WriteLine("GameMode definition " + issue);

            foreach (var definition in result.Definitions)
            {
                if (definition.LegacyFallback && IsRegistered(definition.ModeId))
                {
                    Trace.TraceInformation("Skipping data-driven definition for {0}; legacy fallback is active.", definition.ModeId);
                    continue;
                }

                if (IsRegistered(definition.ModeId))
                    Trace.TraceInformation("Data-driven definition for {0} replaces the legacy factory.", definition.ModeId);

                RegisterGameMode(new DataDrivenGameModeFactory(environmentInfo, definition));
            }
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
