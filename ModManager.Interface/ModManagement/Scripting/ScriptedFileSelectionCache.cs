using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Nexus.Client.Games;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Persists and loads the file mappings selected by a scripted installer for later replay.
	/// </summary>
	/// <remarks>
	/// This cache intentionally stores only archive source and installation destination mappings.
	/// It is used to replay the selected files without rerunning the scripted installer and is not
	/// an installation transaction log or a serialized scripted installation plan.
	/// </remarks>
	public class ScriptedFileSelectionCache : IScriptedFileSelectionCache
	{
		private readonly string m_strFilePath;
		private readonly string m_strModName;
		private readonly string m_strModVersion;
		private XDocument m_docLog = new XDocument();
		private XElement m_xelRoot;

		#region Properties

		/// <summary>
		/// Gets the path of the scripted file-selection cache.
		/// </summary>
		/// <value>The full cache file path.</value>
		public string FilePath
		{
			get { return m_strFilePath; }
		}

		/// <summary>
		/// Gets whether the scripted file-selection cache currently exists.
		/// </summary>
		/// <value><c>true</c> if the cache exists; otherwise, <c>false</c>.</value>
		public bool Exists
		{
			get { return File.Exists(m_strFilePath); }
		}

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a cache for the default scripted-installation log location of the specified mod.
		/// </summary>
		/// <param name="p_modMod">The mod whose scripted file selections are stored.</param>
		/// <param name="p_gmdGameMode">The game mode that provides the install-info directory.</param>
		public ScriptedFileSelectionCache(IMod p_modMod, IGameMode p_gmdGameMode)
			: this(GetDefaultFilePath(p_modMod, p_gmdGameMode), p_modMod.ModName, p_modMod.HumanReadableVersion)
		{
		}

		/// <summary>
		/// Initializes a cache reader for an explicit scripted file-selection cache path.
		/// </summary>
		/// <param name="p_strFilePath">The full path of the cache to read.</param>
		public ScriptedFileSelectionCache(string p_strFilePath)
			: this(p_strFilePath, String.Empty, String.Empty)
		{
		}

		/// <summary>
		/// Initializes a scripted file-selection cache with its persistence metadata.
		/// </summary>
		/// <param name="p_strFilePath">The full path of the cache file.</param>
		/// <param name="p_strModName">The mod name written to a newly created cache.</param>
		/// <param name="p_strModVersion">The mod version written to a newly created cache.</param>
		private ScriptedFileSelectionCache(string p_strFilePath, string p_strModName, string p_strModVersion)
		{
			m_strFilePath = p_strFilePath;
			m_strModName = p_strModName;
			m_strModVersion = p_strModVersion;
		}

		#endregion

		#region Cache Management

		/// <summary>
		/// Records a source and destination mapping selected by the scripted installer.
		/// </summary>
		/// <param name="p_strFrom">The source path inside the mod archive.</param>
		/// <param name="p_strTo">The destination path selected by the installer.</param>
		public void RecordSelection(string p_strFrom, string p_strTo)
		{
			if (m_docLog == null)
				m_docLog = new XDocument();

			string strDirectory = Path.GetDirectoryName(m_strFilePath);
			if (!Directory.Exists(strDirectory))
				Directory.CreateDirectory(strDirectory);

			if (Directory.Exists(strDirectory))
			{
				// Preserve the legacy writer lifecycle: a new document root is created only when
				// the cache file does not yet exist for the current scripted installer instance.
				if (!File.Exists(m_strFilePath))
				{
					m_xelRoot = new XElement("FileList", new XAttribute("ModName", m_strModName ?? String.Empty), new XAttribute("ModVersion", m_strModVersion ?? String.Empty));
					m_docLog.Add(m_xelRoot);
					XElement xelFiles = CreateFileElement(p_strFrom, p_strTo);
					m_xelRoot.Add(xelFiles);
				}
				else
				{
					XElement xelFiles = CreateFileElement(p_strFrom, p_strTo);
					m_xelRoot.Add(xelFiles);
				}

				m_docLog.Save(m_strFilePath);
			}
		}

		/// <summary>
		/// Loads the ordered source and destination mappings stored in the cache.
		/// </summary>
		/// <returns>The cached mappings, or <c>null</c> when no usable mappings are available.</returns>
		public List<KeyValuePair<string, string>> LoadSelections()
		{
			if (!File.Exists(m_strFilePath))
				return null;

			XDocument docScripted = XDocument.Load(m_strFilePath);
			List<KeyValuePair<string, string>> lstFiles = new List<KeyValuePair<string, string>>();

			try
			{
				XElement xelFileList = docScripted.Descendants("FileList").FirstOrDefault();
				if ((xelFileList != null) && xelFileList.HasElements)
				{
					foreach (XElement xelModFile in xelFileList.Elements("File"))
					{
						string strFileFrom = xelModFile.Attribute("FileFrom").Value;
						string strFileTo = xelModFile.Attribute("FileTo").Value;
						if (!String.IsNullOrWhiteSpace(strFileFrom))
							lstFiles.Add(new KeyValuePair<string, string>(strFileFrom, strFileTo));
					}

					if (lstFiles.Count > 0)
						return lstFiles;
				}
			}
			catch (Exception e)
			{
				// Preserve the legacy malformed-cache behavior used by ModInstaller.
				if (String.IsNullOrEmpty(e.Message) && (lstFiles.Count > 0))
					return lstFiles;
			}

			return null;
		}

		/// <summary>
		/// Creates an XML element representing a single scripted file-selection mapping.
		/// </summary>
		/// <param name="p_strFrom">The source path inside the mod archive.</param>
		/// <param name="p_strTo">The destination path selected by the installer.</param>
		/// <returns>The XML element containing the source and destination mapping.</returns>
		private static XElement CreateFileElement(string p_strFrom, string p_strTo)
		{
			return new XElement("File", new XAttribute("FileFrom", p_strFrom ?? String.Empty), new XAttribute("FileTo", p_strTo ?? String.Empty));
		}

		/// <summary>
		/// Builds the default scripted file-selection cache path for the specified mod and game mode.
		/// </summary>
		/// <param name="p_modMod">The mod whose cache path is required.</param>
		/// <param name="p_gmdGameMode">The game mode that provides the install-info directory.</param>
		/// <returns>The full default scripted file-selection cache path.</returns>
		private static string GetDefaultFilePath(IMod p_modMod, IGameMode p_gmdGameMode)
		{
			return Path.Combine(Path.Combine(p_gmdGameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted"), Path.GetFileNameWithoutExtension(p_modMod.Filename)) + ".xml";
		}

		#endregion
	}
}
