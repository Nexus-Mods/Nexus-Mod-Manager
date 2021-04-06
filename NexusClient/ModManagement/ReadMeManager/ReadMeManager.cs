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
using SevenZip;


namespace Nexus.Client.ModManagement
{
	public class ReadMeManager
	{
		private static readonly Version CURRENT_VERSION = new Version("0.1.0.0");
		private static readonly String READMEMANAGER_FILE = "ReadMeManager.xml";
		private bool m_booReadMeXMLError = false;

		#region Fields

		private string m_strReadMePath = string.Empty;
		private bool m_booIsInitialized = false;
		private Dictionary<string, string> m_dicMovedArchiveFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private Dictionary<string, string[]> m_dicReadMeFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

		#endregion

		/// <summary>
		/// Reads the ReadMe manager version from the given file.
		/// </summary>
		/// <param name="p_strCategoryPath">The category file whose version is to be read.</param>
		/// <returns>The version of the specified category file, or a version of
		/// <c>0.0.0.0</c> if the file format is not recognized.</returns>
		public static Version ReadVersion(string p_strReadMePath)
		{
			string strReadMeFilePath = Path.Combine(p_strReadMePath, READMEMANAGER_FILE);
			if (!File.Exists(strReadMeFilePath))
				return new Version("0.0.0.0");

			XDocument docCategory = XDocument.Load(strReadMeFilePath);

			XElement xelCategory = docCategory.Element("categoryManager");
			if (xelCategory == null)
				return new Version("0.0.0.0");

			XAttribute xatVersion = xelCategory.Attribute("fileVersion");
			if (xatVersion == null)
				return new Version("0.0.0.0");

			return new Version(xatVersion.Value);
		}

		#region Properties

		/// <summary>
		/// The full path to the readme config file.
		/// </summary>
		protected string ReadMeFilePath
		{
			get
			{
				return Path.Combine(m_strReadMePath, READMEMANAGER_FILE);
			}
		}

		/// <summary>
		/// The path to the readme temp folder.
		/// </summary>
		protected string ReadMeTempPath
		{
			get
			{
				return Path.Combine(m_strReadMePath, "Temp");
			}
		}

		/// <summary>
		/// The full path to the ReadMe folder.
		/// </summary>
		public string ReadMeFolder
		{
			get
			{
				return m_strReadMePath;
			}
		}

		/// <summary>
		/// Whether the readme manager has been properly initialized.
		/// </summary>
		public bool IsInitialized
		{
			get
			{
				return m_booIsInitialized;
			}
		}

		/// <summary>
		/// Whether the readme manager XML is corrupt.
		/// </summary>
		public bool IsXMLCorrupt
		{
			get
			{
				return m_booReadMeXMLError;
			}
		}

		#endregion

		#region Costructor

		/// <summary>
		/// A simple constructor that initializes the ReadMeManager.
		/// </summary>
		/// <param name="p_strFilePath">The path where the ReadMe folder should be created.</param>
		public ReadMeManager(string p_strFilePath)
		{
			m_strReadMePath = Path.Combine(p_strFilePath, "ReadMe"); ;
			m_dicReadMeFiles.Clear();
			if (CheckReadMeFolder())
			{
				try
				{
					LoadReadMe();
				}
				catch
				{
					m_booReadMeXMLError = true;
					FileUtil.ForceDelete(Path.Combine(m_strReadMePath, "ReadMeManager.xml"));
				}
			}

		}

		#endregion

		#region ReadMe Management

		/// <summary>
		/// Verifies if the readme file exists in the archive and saves it to the ReadMe folder.
		/// </summary>
		/// <param name="p_strArchivePath">The path to the mod archive.</param>
		public bool VerifyReadMeFile(TxFileManager p_tfmFileManager, string p_strArchivePath)
		{
			try
			{
				Archive arcFile = new Archive(p_strArchivePath);
				List<string> lstFiles = GetFileList(arcFile, true);
				string strReadMePath = m_strReadMePath;
				string strFileName = String.Empty;
				string strReadMeFile = String.Empty;
				string strModFile = Path.GetFileName(p_strArchivePath);
				string strArchiveFile = Path.GetFileNameWithoutExtension(strModFile) + ".7z";
				byte[] bteData = null;

				PurgeTempFolder();

				for (int i = 0; i < lstFiles.Count; i++)
				{
					strFileName = lstFiles[i].ToString();
					if (Readme.IsValidReadme(strFileName))
					{
						bteData = arcFile.GetFileContents(lstFiles[i]);
						if (bteData.Length > 0)
						{
							strReadMeFile = Path.GetFileName(strFileName);
							strReadMePath = Path.Combine(ReadMeTempPath, strReadMeFile);
							p_tfmFileManager.WriteAllBytes(strReadMePath, bteData);

							break;
						}
					}
				}
				string[] strFilesToCompress = Directory.GetFiles(ReadMeTempPath, "*.*", SearchOption.AllDirectories);
				if (strFilesToCompress.Length > 0)
					if (CreateReadMeArchive(strArchiveFile, strFilesToCompress))
					{
						for (int i = 0; i < strFilesToCompress.Length; i++)
						{
							strFilesToCompress[i] = Path.GetFileName(strFilesToCompress[i]);
						}

						m_dicReadMeFiles.Add(Path.GetFileNameWithoutExtension(strArchiveFile), strFilesToCompress);
					}
			}
			catch
			{
				return false;
			}

			return true;
		}

		/// <summary>
		/// Creates the readme files archive for the current mod.
		/// </summary>
		/// <param name="p_strArchiveFile">The archive name.</param>
		/// <param name="p_strFilesToCompress">The list of files to compress.</param>
		protected bool CreateReadMeArchive(string p_strArchiveFile, string[] p_strFilesToCompress)
		{
			try
			{
				SevenZipCompressor szcCompressor = new SevenZipCompressor();
				szcCompressor.ArchiveFormat = OutArchiveFormat.SevenZip;
				szcCompressor.CompressionLevel = CompressionLevel.Normal;
				szcCompressor.CompressFiles(Path.Combine(m_strReadMePath, p_strArchiveFile), p_strFilesToCompress);

				foreach (string File in p_strFilesToCompress)
					FileUtil.ForceDelete(File);

				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Removes any file in the ReadMe/Temp folder.
		/// </summary>
		protected void PurgeTempFolder()
		{
			string strCurrentDirectory = Directory.GetCurrentDirectory();
			Directory.SetCurrentDirectory(ReadMeTempPath);
			foreach (string strFile in Directory.GetFiles(ReadMeTempPath))
				FileUtil.ForceDelete(strFile);
			Directory.SetCurrentDirectory(strCurrentDirectory);
		}

		/// <summary>
		/// Checks if the read me folder is already present, otherwise it tries to create it.
		/// </summary>
		public bool CheckReadMeFolder()
		{
			bool booCheck = false;
			string strCurrentDirectory = Directory.GetCurrentDirectory();
			try
			{
				Directory.SetCurrentDirectory(Directory.GetParent(m_strReadMePath).FullName);
				Directory.CreateDirectory("ReadMe");
				Directory.SetCurrentDirectory(m_strReadMePath);
				Directory.CreateDirectory("Temp");
				booCheck = true;
			}
			catch
			{
				booCheck = false;
			}
			finally
			{
				Directory.SetCurrentDirectory(strCurrentDirectory);
			}

			return booCheck;
		}

		/// <summary>
		/// Check if the readme setup file is already present.
		/// </summary>
		public void LoadReadMe()
		{
			string strReadMeManagerPath = ReadMeFilePath;
			if (File.Exists(strReadMeManagerPath))
			{
				XDocument docReadMe = XDocument.Load(strReadMeManagerPath);
				string strVersion = docReadMe.Element("readmeManager").Attribute("fileVersion").Value;
				if (!CURRENT_VERSION.ToString().Equals(strVersion))
					throw new Exception(String.Format("Invalid ReadMe Manager version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

				XElement xelReadMeList = docReadMe.Descendants("readmeList").FirstOrDefault();
				if (xelReadMeList != null)
				{
					foreach (XElement xelReadMe in xelReadMeList.Elements("modFile"))
					{
						string strModName = xelReadMe.Attribute("modName").Value;
						int intFiles = 0;
						string[] strFiles = new string[xelReadMe.Elements("readmeFile").Count()];
						foreach (XElement xelFile in xelReadMe.Elements("readmeFile"))
							strFiles[intFiles++] = xelFile.Attribute("readmeName").Value;

						m_dicReadMeFiles.Add(strModName, strFiles);
					}
				}

				m_booIsInitialized = true;
			}
		}

		/// <summary>
		/// Deletes the readme file.
		/// </summary>
		/// <param name="p_strFileName">The path where the ReadMe folder should be created.</param>
		public void DeleteReadMe(string p_strFileName)
		{
			string strFilePath = p_strFileName + ".7z";
			strFilePath = Path.Combine(m_strReadMePath, strFilePath);
			if (File.Exists(strFilePath))
				FileUtil.ForceDelete(strFilePath);
			m_dicReadMeFiles.Remove(p_strFileName);
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
			foreach (string strFile in p_arcArchive.GetFiles("", "*.txt|*.doc|*.docx|*.htm|*.html|*.rtf|*.pdf", p_booRecurse))
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
		/// Get the ReadMe file path if it exists.
		/// </summary>
		/// <param name="p_strFileName">The mod file name.</param>
		/// <param name="p_intReadmeFile">The index of the readme file to get.</param>
		public string GetModReadMe(string p_strModName, string p_strFileName)
		{
			string strReadMe = String.Empty;
			if (m_dicReadMeFiles.ContainsKey(p_strModName))
			{
				string strModReadMeFile = p_strModName + ".7z";
				strModReadMeFile = Path.Combine(m_strReadMePath, strModReadMeFile);
				if (File.Exists(strModReadMeFile))
				{
					PurgeTempFolder();
					Archive arcFile = new Archive(strModReadMeFile);
					byte[] bteData = arcFile.GetFileContents(p_strFileName);
					if ((bteData != null) && (bteData.Length > 0))
					{
						TxFileManager tfmFileManager = new TxFileManager();
						string strReadMeFile = Path.Combine(ReadMeTempPath, p_strFileName);
						tfmFileManager.WriteAllBytes(strReadMeFile, bteData);
						return strReadMeFile;
					}
				}
			}

			return strReadMe;
		}

		/// <summary>
		/// Check if there's a readme file for the given mod.
		/// </summary>
		/// <param name="p_strFileName">The mod file name.</param>
		public string[] CheckModReadMe(string p_strModName)
		{
			string strReadMe = String.Empty;
			if (m_dicReadMeFiles.ContainsKey(p_strModName))
			{
				return m_dicReadMeFiles[p_strModName];
			}

			return null;
		}

		/// <summary>
		/// Save the data to the category file.
		/// </summary>
		public void SaveReadMeConfig()
		{
			XDocument docReadMe = new XDocument();
			XElement xelRoot = new XElement("readmeManager", new XAttribute("fileVersion", CURRENT_VERSION));
			docReadMe.Add(xelRoot);

			XElement xelReadMeList = new XElement("readmeList");
			xelRoot.Add(xelReadMeList);
			xelReadMeList.Add(from ReadMe in m_dicReadMeFiles
							  select new XElement("modFile",
							  new XAttribute("modName", ReadMe.Key),
								  from FilePath in ReadMe.Value
								  select new XElement("readmeFile",
									  new XAttribute("readmeName", FilePath))));

			if (!Directory.Exists(ReadMeFilePath))
				CheckReadMeFolder();

			using (StreamWriter sw = new StreamWriter(ReadMeFilePath))
			{
				docReadMe.Save(sw);
			}
		}

		#endregion

	}
}
