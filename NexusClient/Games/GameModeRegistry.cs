using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Nexus.Client.Games
{
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
		public static GameModeRegistry DiscoverGameModes(EnvironmentInfo p_eifEnvironmentInfo)
		{
			Trace.TraceInformation("Discovering Game Mode Factories...");
			Trace.Indent();

			string strGameModesPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "GameModes");

			Trace.TraceInformation("Looking in: {0}", strGameModesPath);

			GameModeRegistry gmrRegistry = new GameModeRegistry();
			string[] strAssemblies = Directory.GetFiles(strGameModesPath, "*.dll");
			foreach (string strAssembly in strAssemblies)
			{
				Trace.TraceInformation("Checking: {0}", Path.GetFileName(strAssembly));
				Trace.Indent();

				Assembly asmGameMode = Assembly.LoadFrom(strAssembly);
				try
				{
					Type[] tpeTypes = asmGameMode.GetExportedTypes();
					foreach (Type tpeType in tpeTypes)
					{
						if (typeof(IGameModeFactory).IsAssignableFrom(tpeType) && !tpeType.IsAbstract)
						{
							Trace.TraceInformation("Initializing: {0}", tpeType.FullName);
							Trace.Indent();

							ConstructorInfo cifConstructor = tpeType.GetConstructor(new Type[] { typeof(IEnvironmentInfo) });
							if (cifConstructor == null)
							{
								Trace.TraceInformation("No constructor accepting one argument of type IEnvironmentInfo found.");
								Trace.Unindent();
								continue;
							}
							IGameModeFactory gmfGameModeFactory = (IGameModeFactory)cifConstructor.Invoke(new object[] { p_eifEnvironmentInfo });
							gmrRegistry.RegisterGameMode(gmfGameModeFactory);

							Trace.Unindent();
						}
					}
				}
				catch (FileNotFoundException e)
				{
					Trace.TraceError(String.Format("Cannot load {0}: cannot find dependency {1}", strAssembly, e.FileName));
					//some dependencies were missing, so we couldn't load the assembly
					// given that these are plugins we don't have control over the dependecies:
					// we may not even know what they (we can get their name, but if it's a custom
					// dll not part of the client code base, we can't provide it even if we wanted to)
					// there's nothing we can do, so simply skip the assembly
				}
				Trace.Unindent();
			}
			Trace.Unindent();

			return gmrRegistry;
		}

		private Dictionary<string, IGameModeFactory> m_dicGameModeFactories = new Dictionary<string, IGameModeFactory>(StringComparer.OrdinalIgnoreCase);

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_frgFormatRegistry">The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</param>
		public GameModeRegistry()
		{
		}

		#endregion

		/// <summary>
		/// Registers the specified game mode.
		/// </summary>
		/// <param name="p_gmfGameModeFactory">The factory for the game mode to register.</param>
		public void RegisterGameMode(IGameModeFactory p_gmfGameModeFactory)
		{
			if (m_dicGameModeFactories.ContainsKey(p_gmfGameModeFactory.GameModeDescriptor.ModeId))
			{
				string strError = String.Format("{0} has the same Game Mode Id as {1}. {0} will be replaced in the registry.", m_dicGameModeFactories[p_gmfGameModeFactory.GameModeDescriptor.ModeId].GameModeDescriptor.Name, p_gmfGameModeFactory.GameModeDescriptor.Name);
				Trace.TraceWarning(strError);
			}
			m_dicGameModeFactories[p_gmfGameModeFactory.GameModeDescriptor.ModeId] = p_gmfGameModeFactory;
		}

		/// <summary>
		/// Determines if the specified game mode is in the registry.
		/// </summary>
		/// <param name="p_strGameModeId">The id of the game mode whose presence in the registry is to be determined.</param>
		/// <returns><c>true</c> if the specified game mode is in the registry;
		/// <c>false</c> otherwise.</returns>
		public bool IsRegistered(string p_strGameModeId)
		{
			return m_dicGameModeFactories.ContainsKey(p_strGameModeId);
		}
	}
}
