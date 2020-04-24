namespace Nexus.Client.Games
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
    using System.Linq;
    using Nexus.Client.BackgroundTasks;
	using Nexus.Client.Util;
	using Nexus.Client.Util.Collections;

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
			public IGameModeDescriptor GameMode { get; }

			/// <summary>
			/// Gets the install path of the game.
			/// </summary>
			/// <value>The install path of the game.</value>
			public string GameInstallPath { get; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="gameModeDescriptor">The game mode whose install info in described by the object.</param>
			/// <param name="gameInstallPath">The install path of the game.</param>
			public GameInstallData(IGameModeDescriptor gameModeDescriptor, string gameInstallPath)
			{
				GameMode = gameModeDescriptor;
				GameInstallPath = gameInstallPath;
			}

			#endregion
		}

		#region Events

		/// <summary>
		/// Raised when a possible installation path for a game mode has been found.
		/// </summary>
		public event EventHandler<GameModeDiscoveredEventArgs> PathFound = delegate { };

		/// <summary>
		/// Raised when a possible installation path for a game mode has been found.
		/// </summary>
		public event EventHandler<GameModeDiscoveredEventArgs> DisableButOk = delegate { };

		/// <summary>
		/// Raised when a game has been resolved.
		/// </summary>
		/// <remarks>
		/// A game is resolved when a path as been accepted, overridden, or the search
		/// has completed and the game has not been found.
		/// </remarks>
		public event EventHandler<GameModeDiscoveredEventArgs> GameResolved = delegate { };

		#endregion

		private readonly Dictionary<string, Queue<string>> _candidatePathsByGame = new Dictionary<string, Queue<string>>();
		private readonly Dictionary<string, List<string>> _foundPathsByGame = new Dictionary<string, List<string>>();
		private readonly Dictionary<string, IGameModeDescriptor> _gameModesById = new Dictionary<string, IGameModeDescriptor>();
		private readonly Dictionary<string, IGameModeDescriptor> _gameModesByFile = new Dictionary<string, IGameModeDescriptor>(StringComparer.OrdinalIgnoreCase);
		private readonly List<GameInstallData> _foundGameModes = new List<GameInstallData>();

		#region Properties

		/// <summary>
		/// Gets the list of discovered games.
		/// </summary>
		/// <remarks>
		/// This list may be incomplete until the task completes.
		/// </remarks>
		/// <value>The list of discovered games.</value>
		public IList<GameInstallData> DiscoveredGameModes => _foundGameModes;

        /// <summary>
		/// Gets the list of game modes for which the search has finished.
		/// </summary>
		/// <remarks>
		/// The search is finished for a game if an installation path has
		/// been accepted, overridden, or not found.
		/// </remarks>
		/// <value>The list of game modes for which the search has finished.</value>
		public IEnumerable<IGameModeDescriptor> ResolvedGameModes
		{
            get
            {
                return _gameModesById.Values.Where(gmdInfo => !_candidatePathsByGame.ContainsKey(gmdInfo.ModeId));
            }
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="PathFound"/> event.
		/// </summary>
		/// <param name="args">An <see cref="GameModeDiscoveredEventArgs"/> describing the task that was started.</param>
		protected virtual void OnPathFound(GameModeDiscoveredEventArgs args)
		{
			PathFound(this, args);
		}

		/// <summary>
		/// Raises the <see cref="PathFound"/> event.
		/// </summary>
		/// <param name="args">An <see cref="GameModeDiscoveredEventArgs"/> describing the task that was started.</param>
		protected virtual void OnDisableButOk(GameModeDiscoveredEventArgs args)
		{
			DisableButOk(this, args);
		}

		/// <summary>
		/// Raises the <see cref="PathFound"/> event.
		/// </summary>
		/// <param name="gameMode">The game mode which has been resolved.</param>
		/// <param name="foundPath">The installation path that was found.</param>
		protected void OnDisableButOk(IGameModeDescriptor gameMode, string foundPath)
		{
			OnDisableButOk(new GameModeDiscoveredEventArgs(gameMode, foundPath));
		}


		/// <summary>
		/// Raises the <see cref="PathFound"/> event.
		/// </summary>
		/// <param name="gameMode">The game mode for which a path was found.</param>
		/// <param name="foundPath">The installation path that was found.</param>
		protected void OnPathFound(IGameModeDescriptor gameMode, string foundPath)
		{
			OnPathFound(new GameModeDiscoveredEventArgs(gameMode, foundPath));
		}

		/// <summary>
		/// Raises the <see cref="GameResolved"/> event.
		/// </summary>
		/// <param name="args">An <see cref="GameModeDiscoveredEventArgs"/> describing the task that was started.</param>
		protected virtual void OnGameResolved(GameModeDiscoveredEventArgs args)
		{
			GameResolved(this, args);
		}

		/// <summary>
		/// Raises the <see cref="PathFound"/> event.
		/// </summary>
		/// <param name="gameMode">The game mode which has been resolved.</param>
		/// <param name="foundPath">The installation path that was found.</param>
		protected void OnGameResolved(IGameModeDescriptor gameMode, string foundPath)
		{
			_candidatePathsByGame.Remove(gameMode.ModeId);
			OnGameResolved(new GameModeDiscoveredEventArgs(gameMode, foundPath));
		}

		/// <summary>
		/// Raises the <see cref="FileSearcher.FileFound"/> event.
		/// </summary>
		/// <remarks>
		/// This determines the game mode whose file has been found, and raises the <see cref="PathFound"/>
		/// event as required.
		/// </remarks>
		/// <param name="args">The <see cref="EventArgs{String}"/> describing the event arguments.</param>
		protected override void OnFileFound(EventArgs<string> args)
		{
			base.OnFileFound(args);

			var fileName = Path.GetFileName(args.Argument);
			
            if (!string.IsNullOrEmpty(fileName) && _gameModesByFile.ContainsKey(fileName))
			{
				var gameMode = _gameModesByFile[fileName];
				var path = Path.GetDirectoryName(args.Argument);
				FoundCandidate(gameMode, path);
			}
		}

		/// <summary>
		/// Raises the <see cref="IBackgroundTask.TaskEnded"/> event.
		/// </summary>
		/// <remarks>
		/// This raises the <see cref="GameResolved"/> event for any games that were not found.
		/// </remarks>
		/// <param name="e">The <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		protected override void OnTaskEnded(TaskEndedEventArgs e)
		{
			base.OnTaskEnded(e);
			
            var modeIds = new string[_candidatePathsByGame.Count];
			_candidatePathsByGame.Keys.CopyTo(modeIds, 0);
			
            foreach (var modeId in modeIds)
            {
                if (_candidatePathsByGame[modeId].Count == 0)
                {
                    OnGameResolved(_gameModesById[modeId], null);
                }
            }
        }

		#endregion

		/// <summary>
		/// Searches for the games described by the given game mode descriptors.
		/// </summary>
		/// <param name="gameModesToFind">The game mode factory of the games to search for.</param>
		public void Find(IEnumerable<IGameModeFactory> gameModesToFind)
		{
			var filesToFind = new Set<string>();
			
            Trace.TraceInformation("Searching for the installation paths of:");
			Trace.Indent();
			
            foreach (var gameModeFactory in gameModesToFind)
			{
				Trace.TraceInformation("{0} ({1})", gameModeFactory.GameModeDescriptor.Name, gameModeFactory.GameModeDescriptor.ModeId);
				
                _candidatePathsByGame[gameModeFactory.GameModeDescriptor.ModeId] = new Queue<string>();
				_foundPathsByGame[gameModeFactory.GameModeDescriptor.ModeId] = new List<string>();
				_gameModesById[gameModeFactory.GameModeDescriptor.ModeId] = gameModeFactory.GameModeDescriptor;
				
                foreach (var strExecutable in gameModeFactory.GameModeDescriptor.GameExecutables)
				{
					_gameModesByFile[strExecutable] = gameModeFactory.GameModeDescriptor;
					filesToFind.Add(strExecutable);
				}

				Trace.Indent();
                Trace.TraceInformation("Checking Last Installation Path...");
				Trace.Indent();

				var installationPath = gameModeFactory.GameModeDescriptor.InstallationPath;
				
                Trace.TraceInformation("Returned: {0} (IsNull={1})", installationPath, string.IsNullOrEmpty(installationPath));
				Trace.Unindent();
				if (Verify(gameModeFactory.GameModeDescriptor.ModeId, installationPath))
					FoundCandidate(gameModeFactory.GameModeDescriptor, installationPath);

				Trace.TraceInformation("Asking Game Mode...");
				Trace.Indent();

				installationPath = gameModeFactory.GetInstallationPath();

				Trace.TraceInformation($"Returned: {installationPath} (IsNull={string.IsNullOrEmpty(installationPath)})");
				Trace.Unindent();
				
                if (Verify(gameModeFactory.GameModeDescriptor.ModeId, installationPath))
                {
                    FoundCandidate(gameModeFactory.GameModeDescriptor, installationPath);
                }

                Trace.Unindent();
			}

			Trace.TraceInformation("Starting search.");
			Trace.Unindent();
			
            Find(filesToFind.ToArray());
		}

		/// <summary>
		/// Adds the given path as an installation path candidate for the specified game.
		/// </summary>
		/// <param name="gameMode">The game mode for which to set an installation path candidate.</param>
		/// <param name="installationPath">The installation path candidate.</param>
		protected void FoundCandidate(IGameModeDescriptor gameMode, string installationPath)
		{
			if (!_candidatePathsByGame.ContainsKey(gameMode.ModeId))
            {
                return;
            }

            var strCleanPath = FileUtil.NormalizePath(installationPath);
			
            if (_foundPathsByGame[gameMode.ModeId].Contains(strCleanPath, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            _candidatePathsByGame[gameMode.ModeId].Enqueue(strCleanPath);
			_foundPathsByGame[gameMode.ModeId].Add(strCleanPath);
			
            if (_candidatePathsByGame[gameMode.ModeId].Count == 1)
            {
                OnPathFound(gameMode, installationPath);
            }
        }

		/// <summary>
		/// Accepts the current installation path that was found for the specified game mode
		/// as the correct installation path.
		/// </summary>
		/// <param name="gameModeId">The id of the game mode whose current installation path is to be accepted.</param>
		public void Accept(string gameModeId)
		{
			if (!_candidatePathsByGame.ContainsKey(gameModeId))
            {
                return;
            }

            var installPath = _candidatePathsByGame[gameModeId].Peek();
			_foundGameModes.Add(new GameInstallData(_gameModesById[gameModeId], installPath));
			Stop(gameModeId);
			OnGameResolved(_gameModesById[gameModeId], installPath);
			
            if (_candidatePathsByGame.Count == 0)
            {
                Status = TaskStatus.Complete;
            }
        }

		/// <summary>
		/// Overrides the installation path of the specified game mode with the given path.
		/// </summary>
		/// <param name="gameModeId">The id of the game mode whose installation path is to be overridden.</param>
		/// <param name="installPath">The path to use as the specified game mode's installation path.</param>
		public void Override(string gameModeId, string installPath)
		{
			if (!string.IsNullOrEmpty(installPath))
            {
                _foundGameModes.Add(new GameInstallData(_gameModesById[gameModeId], installPath));
            }

            Stop(gameModeId);
			OnGameResolved(_gameModesById[gameModeId], installPath);
			
            if (_candidatePathsByGame.Count == 0)
            {
                Status = TaskStatus.Complete;
            }
        }

		/// <summary>
		/// Cancels the search for the specified game mode.
		/// </summary>
		/// <param name="gameModeId">The id of the game mode for which to stop searching.</param>
		public void Cancel(string gameModeId)
		{
			Stop(gameModeId);
			OnGameResolved(_gameModesById[gameModeId], null);
			
            if (_candidatePathsByGame.Count == 0)
            {
                Status = TaskStatus.Complete;
            }
        }

		/// <summary>
		/// Cancels the search for the specified game mode.
		/// </summary>
		/// <param name="gameModeId">The id of the game mode for which to stop searching.</param>
		public void DisableButtonOk(string gameModeId)
		{
			OnDisableButOk(_gameModesById[gameModeId], null);
		}

		/// <summary>
		/// Rejects the current installation path of the specified game mode, and indicates
		/// the detector should continue searching.
		/// </summary>
		/// <param name="gameModeId">The id of the game mode whose current installation path is to be rejected.</param>
		public void Reject(string gameModeId)
		{
			if (!_candidatePathsByGame.ContainsKey(gameModeId) || (_candidatePathsByGame[gameModeId].Count == 0))
            {
                return;
            }

            _candidatePathsByGame[gameModeId].Dequeue();
			
            if (_candidatePathsByGame[gameModeId].Count > 0)
            {
                OnPathFound(_gameModesById[gameModeId], _candidatePathsByGame[gameModeId].Peek());
            }
            else if (Status == TaskStatus.Complete)
            {
                OnGameResolved(_gameModesById[gameModeId], null);
            }
        }

		/// <summary>
		/// Stops the search for the specified game mode.
		/// </summary>
		/// <param name="gameModeId">The id of the game mode to stop searching for.</param>
		private void Stop(string gameModeId)
		{
			if (_candidatePathsByGame.ContainsKey(gameModeId))
            {
                _candidatePathsByGame.Remove(gameModeId);
            }
        }

		/// <summary>
		/// Determines of the installation path for the specified game mode has been found.
		/// </summary>
		/// <remarks>
		/// This returns true if an installation path for the specified ame mode has been accepted, or if
		/// one one specified using <see cref="Override(string,string)"/>.
		/// </remarks>
		/// <param name="gameModeId">the id of the game mode for which it is to be determined if the installation path has been found.</param>
		/// <returns><c>true</c> if the installation path of the specified game mode has been found;
		/// <c>false</c> otherwise.</returns>
		public bool IsFound(string gameModeId)
		{
			var gidData = _foundGameModes.Find(d => d.GameMode.ModeId.Equals(gameModeId));
			return (gidData != null);
		}

		/// <summary>
		/// Determines if the detector has found any installation path candidates that have not
		/// been rejected or accepted.
		/// </summary>
		/// <param name="gameModeId">The id of the game mode for which it is to be determined if there are any installation path candidates.</param>
		/// <returns><c>true</c> if the detector has found any installation path candidates;
		/// <c>false</c> otherwise.</returns>
		public bool HasCandidates(string gameModeId)
		{
			return _candidatePathsByGame.ContainsKey(gameModeId) && (_candidatePathsByGame[gameModeId].Count > 0);
		}

		/// <summary>
		/// Gets the confirmed installation path of the specified game mode.
		/// </summary>
		/// <remarks>
		/// This will return <c>null</c> until an installation path has been accepted or overridden for the
		/// specified game mode.
		/// </remarks>
		/// <param name="gameModeId">The id of the game mode for which to return the confirmed installation path.</param>
		/// <returns>The confirmed installation path of the specified game mode.</returns>
		public string GetFinalPath(string gameModeId)
		{
			var gameInstallData = _foundGameModes.Find(d => d.GameMode.ModeId.Equals(gameModeId));
			
            return gameInstallData?.GameInstallPath;
        }

		/// <summary>
		/// Determines if the given installation path contains the specified game.
		/// </summary>
		/// <param name="gameModeId">The id of the game mode for which the path is to be verified.</param>
		/// <param name="installPath">The path to verify as being an installation path.</param>
		/// <returns><c>true</c> if the given path contains the specified game;
		/// <c>false</c> otherwise.</returns>
		public bool Verify(string gameModeId, string installPath)
		{
			if (string.IsNullOrEmpty(installPath))
            {
                return false;
            }

            var found = false;

            try
            {
                if (_gameModesById.Count > 0)
                {
                    if (_gameModesById[gameModeId].GameExecutables
                        .Any(exe => File.Exists(Path.Combine(installPath, exe))))
                    {
                        found = true;
                    }
                }
            }
            catch (Exception ex)
            {
				TraceUtil.TraceException(ex);
            }

			return found;
		}
	}
}
