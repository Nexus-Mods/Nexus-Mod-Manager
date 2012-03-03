using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.Util.Collections;
using Nexus.Client.Util;
using System.IO;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.Games
{
	public class GameDiscoverer : FileSearcher
	{
		public class GameInstallData
		{
			public IGameModeDescriptor GameMode { get; private set; }
			public string InstallationPath { get; private set; }

			public GameInstallData(IGameModeDescriptor p_gmdGameMode, string p_strInstallationPath)
			{
				GameMode = p_gmdGameMode;
				InstallationPath = p_strInstallationPath;
			}
		}

		#region Events

		public event EventHandler<GameModeDiscoveredEventArgs> PathFound = delegate { };

		#endregion

		private Dictionary<string, Queue<string>> m_dicFoundPathsByGame = new Dictionary<string, Queue<string>>();
		private Dictionary<string, IGameModeDescriptor> m_dicGameModesById = new Dictionary<string, IGameModeDescriptor>();
		private Dictionary<string, IGameModeDescriptor> m_dicGameModesByFile = new Dictionary<string, IGameModeDescriptor>(StringComparer.OrdinalIgnoreCase);
		private List<GameInstallData> m_lstFoundGameModes = new List<GameInstallData>();

		#region Properties

		public IEnumerable<GameInstallData> DiscoveredGameModes
		{
			get
			{
				return m_lstFoundGameModes;
			}
		}

		#endregion

		#region Constructor

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

		#endregion

		public void Find(IEnumerable<IGameModeDescriptor> p_lstGameModesToFind)
		{
			Set<string> lstFilesToFind = new Set<string>();
			foreach (IGameModeDescriptor gmdGameMode in p_lstGameModesToFind)
			{
				m_dicFoundPathsByGame[gmdGameMode.ModeId] = new Queue<string>();
				m_dicGameModesById[gmdGameMode.ModeId] = gmdGameMode;
				foreach (string strExecutable in gmdGameMode.GameExecutables)
				{
					m_dicGameModesByFile[strExecutable] = gmdGameMode;
					lstFilesToFind.Add(strExecutable);
				}
			}
			Find(lstFilesToFind.ToArray());
		}

		protected override void OnFileFound(EventArgs<string> e)
		{
			base.OnFileFound(e);
			string strFileName = Path.GetFileName(e.Argument);
			IGameModeDescriptor gmdGameMode = m_dicGameModesByFile[strFileName];
			if (!m_dicFoundPathsByGame.ContainsKey(gmdGameMode.ModeId))
				return;
			string strPath = Path.GetDirectoryName(e.Argument);
			m_dicFoundPathsByGame[gmdGameMode.ModeId].Enqueue(strPath);
			if (m_dicFoundPathsByGame[gmdGameMode.ModeId].Count == 1)
				OnPathFound(gmdGameMode, strPath);
		}

		public void Accept(string p_strGameModeId)
		{
			if (!m_dicFoundPathsByGame.ContainsKey(p_strGameModeId))
				return;
			m_lstFoundGameModes.Add(new GameInstallData(m_dicGameModesById[p_strGameModeId], m_dicFoundPathsByGame[p_strGameModeId].Peek()));
			Stop(p_strGameModeId);
			if (m_dicFoundPathsByGame.Count == 0)
				Status = TaskStatus.Complete;
		}

		public void Reject(string p_strGameModeId)
		{
			if (!m_dicFoundPathsByGame.ContainsKey(p_strGameModeId) || (m_dicFoundPathsByGame[p_strGameModeId].Count == 0))
				return;
			m_dicFoundPathsByGame[p_strGameModeId].Dequeue();
			if (m_dicFoundPathsByGame[p_strGameModeId].Count > 0)
				OnPathFound(m_dicGameModesById[p_strGameModeId], m_dicFoundPathsByGame[p_strGameModeId].Peek());
		}

		private void Stop(string p_strGameModeId)
		{
			if (m_dicFoundPathsByGame.ContainsKey(p_strGameModeId))
				m_dicFoundPathsByGame.Remove(p_strGameModeId);
		}

		public bool IsFound(string p_strGameModeId)
		{
			return m_dicFoundPathsByGame.ContainsKey(p_strGameModeId) && (m_dicFoundPathsByGame[p_strGameModeId].Count > 0);
		}
	}
}
