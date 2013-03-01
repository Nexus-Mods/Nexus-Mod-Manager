using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using Nexus.Client.Games;
using ChinhDo.Transactions;


namespace Nexus.Client.ModManagement
{
	public partial class ReadMeManager
	{
		#region Fields

		private string m_strReadMePath = null;
		private Dictionary<string, string> m_dicMovedArchiveFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		#endregion

		#region Costructor

		/// <summary>
		/// A simple constructor that initializes the ReadMeManager.
		/// </summary>
		public ReadMeManager(string p_strFilePath)
		{
			m_strReadMePath = Path.Combine(p_strFilePath, "ReadMe");
			CheckReadMeFolder(m_strReadMePath);
		}

		#endregion

		#region ReadMe Management

		/// <summary>
		/// Verifies if the readme file.
		/// </summary>
		public bool VerifyReadMeFile(TxFileManager p_tfmFileManager, string strArchivePath, string strModFolderPath, string strModName)
		{
			Archive arcFile = new Archive(strArchivePath);
			List<string> lstFiles = GetFileList(arcFile, true);
			string strReadMePath = m_strReadMePath;
			string p_strFileName = null;
			byte[] p_bteData = null;
			for (int i = 0; i < lstFiles.Count; i++)
			{
				p_strFileName = lstFiles[i].ToString();
				if (p_strFileName.ToLower().Contains("readme"))
				{
					CheckReadMeFolder(strReadMePath);

					p_bteData = arcFile.GetFileContents(lstFiles[i]);
					strReadMePath = Path.Combine(strReadMePath, strModName + ".txt");
					p_tfmFileManager.WriteAllBytes(strReadMePath, p_bteData);
				}
			}
			return true;
		}

		/// <summary>
		/// Verifies the readme folder.
		/// </summary>
		public bool CheckReadMeFolder(string p_strReadMeFolder)
		{
			if (!Directory.Exists(p_strReadMeFolder))
				Directory.CreateDirectory(Path.GetDirectoryName(p_strReadMeFolder));

			return true;
		}

		/// <summary>
		/// Deletes the readme file.
		/// </summary>
		public void DeleteReadMe(string p_strFileName)
		{
			string strPath = Path.Combine(m_strReadMePath, p_strFileName + ".txt");

			if (File.Exists(strPath))
				FileUtil.ForceDelete(strPath);
		}

		/// <summary>
		/// Retrieves the list of all files in the specified folder.
		/// </summary>
		/// <param name="p_arcArchive">The archive whose file is to be retrieved.</param>
		/// <param name="p_booRecurse">Whether to return files that are in subdirectories of the given directory.</param>
		/// <returns>The list of all files in the specified folder.</returns>
		private List<string> GetFileList(Archive p_arcArchive, bool p_booRecurse)
		{
			List<string> lstFiles = new List<string>();
			foreach (string strFile in p_arcArchive.GetFiles("", p_booRecurse))
				if (!m_dicMovedArchiveFiles.ContainsValue(strFile))
					if (!strFile.StartsWith("fomod", StringComparison.OrdinalIgnoreCase))
						lstFiles.Add(strFile);
			string strPathPrefix = "" ?? "";
			strPathPrefix = strPathPrefix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			strPathPrefix = strPathPrefix.Trim(Path.DirectorySeparatorChar);
			if (strPathPrefix.Length > 0)
				strPathPrefix += Path.DirectorySeparatorChar;
			foreach (string strFile in m_dicMovedArchiveFiles.Keys)
				if (strFile.StartsWith(strPathPrefix, StringComparison.OrdinalIgnoreCase) && !strFile.StartsWith("fomod", StringComparison.OrdinalIgnoreCase))
					lstFiles.Add(strFile);
			return lstFiles;
		}

		/// <summary>
		/// Checks the ReadMe file path if exists.
		/// </summary>
		public string GetModReadMe(string p_strPath)
		{
			string strModReadMeFile = Path.Combine(m_strReadMePath, p_strPath + ".txt");
			if (File.Exists(strModReadMeFile))
				return strModReadMeFile;
			else
				return string.Empty;
		}

		#endregion

	}
}
