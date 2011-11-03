using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Nexus.Client.Games;

namespace Nexus.Client
{
	/// <summary>
	/// Chooses the current game mode.
	/// </summary>
	/// <remarks>
	/// This selector checks the following to select the current game mode:
	/// -command line
	/// -remembered game mode in settings
	/// -ask the user
	/// </remarks>
	public class GameModeSelector
	{
		private Dictionary<string, IGameModeFactory> m_dicGameModeFactories = new Dictionary<string, IGameModeFactory>(StringComparer.OrdinalIgnoreCase);

		#region Properties

		/// <summary>
		/// Gets or sets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public GameModeSelector(IEnvironmentInfo p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			DiscoverGameModes();
		}

		#endregion

		/// <summary>
		/// Finds the game mode assemblies available for selection.
		/// </summary>
		protected void DiscoverGameModes()
		{
			Trace.TraceInformation("Discovering Game Mode Factories...");
			Trace.Indent();

			string strGameModesPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "GameModes");

			Trace.TraceInformation("Looking in: {0}", strGameModesPath);

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
							IGameModeFactory gmfGameModeFactory = (IGameModeFactory)cifConstructor.Invoke(new object[] { EnvironmentInfo });
							if (m_dicGameModeFactories.ContainsKey(gmfGameModeFactory.GameModeDescriptor.ModeId))
							{
								string strError = String.Format("{0} has the same Game Mode Id as {1}. {1} will not be available.", m_dicGameModeFactories[gmfGameModeFactory.GameModeDescriptor.ModeId].GameModeDescriptor.Name, gmfGameModeFactory.GameModeDescriptor.Name);
								Trace.TraceWarning(strError);
								MessageBox.Show(strError, "Duplicate Game Mode Ids", MessageBoxButtons.OK, MessageBoxIcon.Warning);
							}
							else
								m_dicGameModeFactories[gmfGameModeFactory.GameModeDescriptor.ModeId] = gmfGameModeFactory;

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
		}

		/// <summary>
		/// Selectes the current game mode.
		/// </summary>
		/// <param name="p_strArgs">The command line arguments.</param>
		/// <param name="p_booChangeGameMode">Whether the user has requested a game mode change.</param>
		/// <returns>The <see cref="IGameModeFactory"/> that can build the game mode selected by the user.</returns>
		public IGameModeFactory SelectGameMode(string[] p_strArgs, bool p_booChangeGameMode)
		{
			Trace.Write("Determining Game Mode: ");

			string strSelectedGame = EnvironmentInfo.Settings.RememberGameMode ? EnvironmentInfo.Settings.RememberedGameMode : null;
			if (!p_booChangeGameMode)
			{
				for (Int32 i = 0; i < p_strArgs.Length; i++)
				{
					string strArg = p_strArgs[i];
					if (strArg.StartsWith("-"))
					{
						switch (strArg)
						{
							case "-game":
								Trace.Write("(From Command line: " + p_strArgs[i + 1] + ") ");
								if (!m_dicGameModeFactories.ContainsKey(p_strArgs[i + 1]))
									MessageBox.Show(String.Format("Unrecognized -game Parameter: {0}", p_strArgs[i + 1]), "Unrecognized Game Mode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
								else
									strSelectedGame = p_strArgs[i + 1];
								break;
						}
					}
				}
			}

			if (p_booChangeGameMode || String.IsNullOrEmpty(strSelectedGame))
			{
				Trace.Write("(From Selection Form) ");
				List<IGameModeDescriptor> lstGameModeInfos = new List<IGameModeDescriptor>();
				foreach (IGameModeFactory gmfFactory in m_dicGameModeFactories.Values)
					lstGameModeInfos.Add(gmfFactory.GameModeDescriptor);
				GameModeSelectionForm msfSelector = new GameModeSelectionForm(lstGameModeInfos, EnvironmentInfo.Settings);
				msfSelector.ShowDialog();
				strSelectedGame = msfSelector.SelectedGameModeId;
			}
			Trace.WriteLine(strSelectedGame);
			if (!m_dicGameModeFactories.ContainsKey(strSelectedGame))
			{
				string strError = String.Format("Unrecognized Game Mode: {0}", p_strArgs[1]);
				Trace.TraceError(strError);
				MessageBox.Show(strError, "Unrecognized Game Mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return null;
			}

			return m_dicGameModeFactories[strSelectedGame];
		}
	}
}
