using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using System.Diagnostics;

namespace Nexus.Client.Games
{
	/// <summary>
	/// Searches for installed games.
	/// </summary>
	public class GameDiscoverer : FileSearcher
	{
		/// <summary>
		/// Describes the installation path of a game.
		/// </summary>
		public class GameInstallData
		{
			#region Properties

			/// <summary>
			/// Gets the game mode whose install info in described by the object.
			/// </summary>
			/// <value>The game mode whose install info in described by the object.</value>
			public IGameModeDescriptor GameMode { get; private set; }

			/// <summary>
			/// Gets the installation path of the game mode.
			/// </summary>
			/// <value>The installation path of the game mode.</value>
			public string InstallationPath { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_gmdGameMode">The game mode whose install info in described by the object.</param>
			/// <param name="p_strInstallationPath">The installation path of the game mode.</param>
			public GameInstallData(IGameModeDescriptor p_gmdGameMode, string p_strInstallationPath)
			{
				GameMode = p_gmdGameMode;
				InstallationPath = p_strInstallationPath;
			}

			#endregion
		}

		#region Events

		/// <summary>
		/// Raised when a possible installation path for a game mode has been found.
		/// </summary>
		public event EventHandler<GameModeDiscoveredEventArgs> PathFound = delegate { };

		#endregion

		private Dictionary<string, Queue<string>> m_dicCandidatePathsByGame = new Dictionary<string, Queue<string>>();
		private Dictionary<string, List<string>> m_dicFoundPathsByGame = new Dictionary<string, List<string>>();
		private Dictionary<string, IGameModeDescriptor> m_dicGameModesById = new Dictionary<string, IGameModeDescriptor>();
		private Dictionary<string, IGameModeDescriptor> m_dicGameModesByFile = new Dictionary<string, IGameModeDescriptor>(StringComparer.OrdinalIgnoreCase);
		private List<GameInstallData> m_lstFoundGameModes = new List<GameInstallData>();

		#region Properties

		/// <summary>
		/// Gets the list of discovered games.
		/// </summary>
		/// <remarks>
		/// This list may be incomplete until the task completes.
		/// </remarks>
		/// <value>The list of discovered games.</value>
		public IEnumerable<GameInstallData> DiscoveredGameModes
		{
			get
			{
				return m_lstFoundGameModes;
			}
		}

		#endregion

		#region Constructor

		/// <summary>
		/// The default constructor.
		/// </summary>
		public GameDiscoverer()
		{
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="PathFound"/> event.
		/// </summary>
		/// <param name="e">An <see cref="GameModeDiscoveredEventArgs"/> describing the task that was started.</param>
		protected virtual void OnPathFound(GameModeDiscoveredEventArgs e)
		{
			PathFound(this, e);
		}

		/// <summary>
		/// Raises the <see cref="PathFound"/> event.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode for which a path was found.</param>
		/// <param name="p_strFoundPath">The installaiton path that was found.</param>
		protected void OnPathFound(IGameModeDescriptor p_gmdGameMode, string p_strFoundPath)
		{
			OnPathFound(new GameModeDiscoveredEventArgs(p_gmdGameMode, p_strFoundPath));
		}

		/// <summary>
		/// Raises the <see cref="FileSearcher.FileFound"/> event.
		/// </summary>
		/// <remarks>
		/// This determines the game mode whose file has been found, and raises the <see cref="PathFound"/>
		/// event as required.
		/// </remarks>
		/// <param name="e">The <see cref="EventArgs{String}"/> describing the event arguments.</param>
		protected override void OnFileFound(EventArgs<string> e)
		{
			base.OnFileFound(e);
			string strFileName = Path.GetFileName(e.Argument);
			IGameModeDescriptor gmdGameMode = m_dicGameModesByFile[strFileName];
			string strPath = Path.GetDirectoryName(e.Argument);
			FoundCandidate(gmdGameMode, strPath);
		}

		#endregion

		/// <summary>
		/// Searchs for the games described by the given game mode descriptors.
		/// </summary>
		/// <param name="p_lstGameModesToFind">The game mode factorie of the games to search for.</param>
		public void Find(IEnumerable<IGameModeFactory> p_lstGameModesToFind)
		{
			Set<string> lstFilesToFind = new Set<string>();
			Trace.TraceInformation("Searching for the installation paths of:");
			Trace.Indent();
			foreach (IGameModeFactory gmfFactory in p_lstGameModesToFind)
			{
				Trace.TraceInformation("{0} ({1})", gmfFactory.GameModeDescriptor.Name, gmfFactory.GameModeDescriptor.ModeId);
				m_dicCandidatePathsByGame[gmfFactory.GameModeDescriptor.ModeId] = new Queue<string>();
				m_dicFoundPathsByGame[gmfFactory.GameModeDescriptor.ModeId] = new List<string>();
				m_dicGameModesById[gmfFactory.GameModeDescriptor.ModeId] = gmfFactory.GameModeDescriptor;
				foreach (string strExecutable in gmfFactory.GameModeDescriptor.GameExecutables)
				{
					m_dicGameModesByFile[strExecutable] = gmfFactory.GameModeDescriptor;
					lstFilesToFind.Add(strExecutable);
				}
				Trace.Indent();
				Trace.TraceInformation("Asking Game Mode...");
				string strPath = gmfFactory.GetInstallationPath();
				if (Verify(gmfFactory.GameModeDescriptor.ModeId, strPath))
					FoundCandidate(gmfFactory.GameModeDescriptor, strPath);
				Trace.Unindent();
			}
			Trace.TraceInformation("Starting search.");
			Trace.Unindent();
			Find(lstFilesToFind.ToArray());
		}

		/// <summary>
		/// Adds the given path as an installation path candidate for the specified game.
		/// </summary>
		/// <param name="gmdGameMode">The game mode for which to set an installation path candidate.</param>
		/// <param name="p_strInstallationPath">The installation path candidate.</param>
		protected void FoundCandidate(IGameModeDescriptor gmdGameMode, string p_strInstallationPath)
		{
			if (!m_dicCandidatePathsByGame.ContainsKey(gmdGameMode.ModeId))
				return;
			string strCleanPath = FileUtil.NormalizePath(p_strInstallationPath);
			if (m_dicFoundPathsByGame[gmdGameMode.ModeId].Contains(strCleanPath, StringComparer.OrdinalIgnoreCase))
				return;
			m_dicCandidatePathsByGame[gmdGameMode.ModeId].Enqueue(strCleanPath);
			m_dicFoundPathsByGame[gmdGameMode.ModeId].Add(strCleanPath);
			if (m_dicCandidatePathsByGame[gmdGameMode.ModeId].Count == 1)
				OnPathFound(gmdGameMode, p_strInstallationPath);
		}

		/// <summary>
		/// Accepts the current installation path that was found for the specified game mode
		/// as the correct installation path.
		/// </summary>
		/// <param name="p_strGameModeId">The id of the game mode whose current installation path is to be accepted.</param>
		public void Accept(string p_strGameModeId)
		{
			if (!m_dicCandidatePathsByGame.ContainsKey(p_strGameModeId))
				return;
			m_lstFoundGameModes.Add(new GameInstallData(m_dicGameModesById[p_strGameModeId], m_dicCandidatePathsByGame[p_strGameModeId].Peek()));
			Stop(p_strGameModeId);
			if (m_dicCandidatePathsByGame.Count == 0)
				Status = TaskStatus.Complete;
		}

		/// <summary>
		/// Overrides the installation path of the specified game mode with the given path.
		/// </summary>
		/// <param name="p_strGameModeId">The id of the game mode whose installation path is to be overridden.</param>
		/// <param name="p_strInstallPath">The path to use as the specified game mode's installation path.</param>
		public void Override(string p_strGameModeId, string p_strInstallPath)
		{
			m_lstFoundGameModes.Add(new GameInstallData(m_dicGameModesById[p_strGameModeId], p_strInstallPath));
			Stop(p_strGameModeId);
			if (m_dicCandidatePathsByGame.Count == 0)
				Status = TaskStatus.Complete;
		}

		/// <summary>
		/// Rejects the current installation path of the specified game mode, and indicates
		/// the detector should continue searching.
		/// </summary>
		/// <param name="p_strGameModeId">The id of the game mode whose current installation path is to be rejected.</param>
		public void Reject(string p_strGameModeId)
		{
			if (!m_dicCandidatePathsByGame.ContainsKey(p_strGameModeId) || (m_dicCandidatePathsByGame[p_strGameModeId].Count == 0))
				return;
			m_dicCandidatePathsByGame[p_strGameModeId].Dequeue();
			if (m_dicCandidatePathsByGame[p_strGameModeId].Count > 0)
				OnPathFound(m_dicGameModesById[p_strGameModeId], m_dicCandidatePathsByGame[p_strGameModeId].Peek());
		}

		/// <summary>
		/// Stops the search for the specified game mode.
		/// </summary>
		/// <param name="p_strGameModeId">The id of the game mode to stop searching for.</param>
		private void Stop(string p_strGameModeId)
		{
			if (m_dicCandidatePathsByGame.ContainsKey(p_strGameModeId))
				m_dicCandidatePathsByGame.Remove(p_strGameModeId);
		}

		/// <summary>
		/// Determines of the installation path for the specified game mode has been found.
		/// </summary>
		/// <remarks>
		/// This returns true if an installation path for the specified ame mode has been accepted, or if
		/// one one specified useing <see cref="Override(string,string)"/>.
		/// </remarks>
		/// <param name="p_strGameModeId">the id of the game mode for which it is to be determined if the installation path has been found.</param>
		/// <returns><c>true</c> if the installation path of the specified game mode has been found;
		/// <c>false</c> otherwise.</returns>
		public bool IsFound(string p_strGameModeId)
		{
			GameInstallData gidData = m_lstFoundGameModes.Find(d => d.GameMode.ModeId.Equals(p_strGameModeId));
			return (gidData != null);
		}

		/// <summary>
		/// Determines if the detector has found any installation path candidates that have not
		/// been rejected or accepted.
		/// </summary>
		/// <param name="p_strGameModeId">The id of the game mode for which it is to be determined if there are any installation path candidates.</param>
		/// <returns><c>true</c> if the detector has found any installation path candidates;
		/// <c>false</c> otherwise.</returns>
		public bool HasCandidates(string p_strGameModeId)
		{
			return m_dicCandidatePathsByGame.ContainsKey(p_strGameModeId) && (m_dicCandidatePathsByGame[p_strGameModeId].Count > 0);
		}

		/// <summary>
		/// Gets the confirmed installation path of the specified game mode.
		/// </summary>
		/// <remarks>
		/// This will return <c>null</c> until an installation path has been accepted or overridden for the
		/// specified game mode.
		/// </remarks>
		/// <param name="p_strGameModeId">The id of the game mode for which to return the confirmed installation path.</param>
		/// <returns>The confirmed installation path of the specified game mode.</returns>
		public string GetFinalPath(string p_strGameModeId)
		{
			GameInstallData gidData = m_lstFoundGameModes.Find(d => d.GameMode.ModeId.Equals(p_strGameModeId));
			if (gidData != null)
				return gidData.InstallationPath;
			return null;
		}

		/// <summary>
		/// Determines if the given installation path contains the specified game.
		/// </summary>
		/// <param name="p_strGameModeId">The id of the game mode for which the path is to be verified.</param>
		/// <param name="p_strInstallPath">The path to verify as being an installation path.</param>
		/// <returns><c>true</c> if the given path contains the speficied game;
		/// <c>false</c> otherwise.</returns>
		public bool Verify(string p_strGameModeId, string p_strInstallPath)
		{
			if (String.IsNullOrEmpty(p_strInstallPath))
				return false;

			bool booFound = false;
			foreach (string strExe in m_dicGameModesById[p_strGameModeId].GameExecutables)
				if (File.Exists(Path.Combine(p_strInstallPath, strExe)))
				{
					booFound = true;
					break;
				}
			return booFound;
		}
	}
}
