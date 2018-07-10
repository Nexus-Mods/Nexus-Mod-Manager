using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games.Gamebryo.Tools.BSA;
using Nexus.Client.ModManagement.Scripting;

namespace Nexus.Client.Games.Gamebryo.ModManagement.Scripting
{
	/// <summary>
	/// Encapsulates the management of BSA files.
	/// </summary>
	public class BsaManager : IDisposable
	{
		private static readonly Dictionary<string, BSAArchive> m_dicBSAs = new Dictionary<string, BSAArchive>();

		#region Properties

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		public GamebryoGameModeBase GameMode { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor the initializes the object with the given values.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		public BsaManager(GamebryoGameModeBase p_gmdGameMode)
		{
			GameMode = p_gmdGameMode;
		}

		#endregion

		/// <summary>
		/// Gets the specified file from the specified BSA.
		/// </summary>
		/// <param name="p_strBsa">The BSA from which to extract the specified file.</param>
		/// <param name="p_strFile">The files to extract form the specified BSA.</param>
		/// <returns>The data of the specified file.</returns>
		/// <exception cref="IllegalFilePathException">Thrown if <paramref name="p_strBsa"/>
		/// contains illegal characters or refers to a file outside of the Data directory, or
		/// if <paramref name="p_strFile"/> refers to an unsafe location.</exception>
		/// <exception cref="BSAArchive.BSALoadException">Thrown if the specified BSA cannot be loaded.</exception>
		public byte[] GetDataFileFromBSA(string p_strBsa, string p_strFile)
		{
			if (Path.GetDirectoryName(p_strBsa).Length > 0)
				throw new IllegalFilePathException(p_strBsa);
			string strLoweredBsa = p_strBsa.ToLowerInvariant();
			if (!m_dicBSAs.ContainsKey(strLoweredBsa))
				m_dicBSAs[strLoweredBsa] = new BSAArchive(Path.Combine(GameMode.PluginDirectory, strLoweredBsa));
			return m_dicBSAs[strLoweredBsa].GetFile(p_strFile);
		}

		/// <summary>
		/// Retrieves the list of files in the specified BSA.
		/// </summary>
		/// <param name="p_strBsa">The BSA whose file listing is requested.</param>
		/// <returns>The list of files contained in the specified BSA.</returns>
		/// <exception cref="IllegalFilePathException">Thrown if <paramref name="p_strBsa"/>
		/// contains illegal characters or refers to a file outside of the Data directory.</exception>
		/// <exception cref="BSAArchive.BSALoadException">Thrown if the specified BSA cannot be loaded.</exception>
		public string[] GetBSAFileList(string p_strBsa)
		{
			if (Path.GetDirectoryName(p_strBsa).Length > 0)
				throw new IllegalFilePathException(p_strBsa);
			if (!m_dicBSAs.ContainsKey(p_strBsa))
				m_dicBSAs[p_strBsa] = new BSAArchive(Path.Combine(GameMode.PluginDirectory, p_strBsa));
			return (string[])m_dicBSAs[p_strBsa].FileNames.Clone();
		}

		#region IDisposable Members

		/// <summary>
		/// Disposes the BSA manager.
		/// </summary>
		/// <remarks>
		/// This method ensures that all BSAs have been released.
		/// </remarks>
		public void Dispose()
		{
			foreach (BSAArchive bsaBSA in m_dicBSAs.Values)
				bsaBSA.Dispose();
			m_dicBSAs.Clear();
		}

		#endregion
	}
}
