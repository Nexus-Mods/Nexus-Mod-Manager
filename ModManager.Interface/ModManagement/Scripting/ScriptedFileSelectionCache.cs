using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Nexus.Client.Games;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Persists and loads the ordered file operations selected by a scripted installer for later replay.
	/// </summary>
	public class ScriptedFileSelectionCache : IScriptedFileSelectionCache
	{
		private const string ReplayVersion = "2";
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

		/// <summary>
		/// Gets whether this cache records every replayable file operation, including generated payloads.
		/// </summary>
		public bool HasCompleteReplay
		{
			get
			{
				if (!File.Exists(m_strFilePath))
					return false;

				try
				{
					XElement root = XDocument.Load(m_strFilePath).Descendants("FileList").FirstOrDefault();
					XAttribute version = root == null ? null : root.Attribute("ReplayVersion");
					return version != null && String.Equals(version.Value, ReplayVersion, StringComparison.Ordinal);
				}
				catch
				{
					return false;
				}
			}
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
			EnsureDocumentForWrite();
			m_xelRoot.Add(CreateFileElement(p_strFrom, p_strTo));
			m_docLog.Save(m_strFilePath);
		}

		/// <summary>
		/// Records a generated payload in a sidecar file and appends it to the ordered replay plan.
		/// </summary>
		public void RecordGeneratedFile(string p_strTo, byte[] p_bteData)
		{
			if (p_bteData == null)
				throw new ArgumentNullException(nameof(p_bteData));

			EnsureDocumentForWrite();
			string payloadDirectory = GetPayloadDirectoryPath(m_strFilePath);
			if (!Directory.Exists(payloadDirectory))
				Directory.CreateDirectory(payloadDirectory);

			string payloadName = Guid.NewGuid().ToString("N") + ".bin";
			string payloadPath = Path.Combine(payloadDirectory, payloadName);
			File.WriteAllBytes(payloadPath, p_bteData);
			string payloadHash = ComputeHash(p_bteData);

			m_xelRoot.Add(new XElement("GeneratedFile",
				new XAttribute("FileTo", p_strTo ?? String.Empty),
				new XAttribute("Payload", payloadName),
				new XAttribute("Length", p_bteData.LongLength.ToString(CultureInfo.InvariantCulture)),
				new XAttribute("Sha256", payloadHash)));
			m_docLog.Save(m_strFilePath);
		}

		/// <summary>
		/// Records a scripted basic-install request in the ordered replay plan.
		/// </summary>
		public void RecordBasicInstall()
		{
			EnsureDocumentForWrite();
			m_xelRoot.Add(new XElement("BasicInstall"));
			m_docLog.Save(m_strFilePath);
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
						XAttribute from = xelModFile.Attribute("FileFrom");
						XAttribute to = xelModFile.Attribute("FileTo");
						if (from == null || to == null)
							return null;
						string strFileFrom = from.Value;
						string strFileTo = to.Value;
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
		/// Loads and validates the complete ordered replay plan without loading generated payload bytes into memory.
		/// </summary>
		public IReadOnlyList<ScriptedReplayOperation> LoadReplayOperations()
		{
			if (!File.Exists(m_strFilePath))
				return null;

			XDocument document = XDocument.Load(m_strFilePath);
			XElement root = document.Descendants("FileList").FirstOrDefault();
			if (root == null || !root.HasElements)
				return null;

			var operations = new List<ScriptedReplayOperation>();
			foreach (XElement element in root.Elements())
			{
				if (element.Name.LocalName.Equals("File", StringComparison.OrdinalIgnoreCase))
				{
					string source = GetAttributeValue(element, "FileFrom");
					if (String.IsNullOrWhiteSpace(source))
						continue;
					operations.Add(new ScriptedReplayOperation(
						ScriptedReplayOperationKind.ArchiveFile,
						source,
						GetAttributeValue(element, "FileTo"),
						null,
						0,
						null));
					continue;
				}

				if (element.Name.LocalName.Equals("BasicInstall", StringComparison.OrdinalIgnoreCase))
				{
					operations.Add(new ScriptedReplayOperation(
						ScriptedReplayOperationKind.BasicInstall, null, null, null, 0, null));
					continue;
				}

				if (!element.Name.LocalName.Equals("GeneratedFile", StringComparison.OrdinalIgnoreCase))
					continue;

				string payloadName = GetAttributeValue(element, "Payload");
				string payloadPath = ResolvePayloadPath(payloadName);
				long payloadLength;
				if (!Int64.TryParse(GetAttributeValue(element, "Length"), NumberStyles.None, CultureInfo.InvariantCulture, out payloadLength) || payloadLength < 0)
					throw new InvalidDataException(String.Format("Scripted replay payload '{0}' has an invalid length.", payloadName));
				string payloadHash = GetAttributeValue(element, "Sha256");
				ValidatePayload(payloadPath, payloadLength, payloadHash);
				operations.Add(new ScriptedReplayOperation(
					ScriptedReplayOperationKind.GeneratedFile,
					null,
					GetAttributeValue(element, "FileTo"),
					payloadPath,
					payloadLength,
					payloadHash));
			}

			return operations.Count == 0 ? null : operations;
		}

		/// <summary>
		/// Copies one scripted replay XML and its generated-payload sidecar directory.
		/// </summary>
		public static void CopyArtifacts(string p_strSourceCachePath, string p_strDestinationCachePath)
		{
			if (String.IsNullOrWhiteSpace(p_strSourceCachePath) || String.IsNullOrWhiteSpace(p_strDestinationCachePath))
				return;
			if (!File.Exists(p_strSourceCachePath))
				return;
			if (Path.GetFullPath(p_strSourceCachePath).Equals(Path.GetFullPath(p_strDestinationCachePath), StringComparison.OrdinalIgnoreCase))
				return;

			string destinationDirectory = Path.GetDirectoryName(p_strDestinationCachePath);
			if (!String.IsNullOrWhiteSpace(destinationDirectory) && !Directory.Exists(destinationDirectory))
				Directory.CreateDirectory(destinationDirectory);
			File.Copy(p_strSourceCachePath, p_strDestinationCachePath, true);

			string sourcePayloadDirectory = GetPayloadDirectoryPath(p_strSourceCachePath);
			string destinationPayloadDirectory = GetPayloadDirectoryPath(p_strDestinationCachePath);
			if (Directory.Exists(destinationPayloadDirectory))
				DeleteDirectoryTree(destinationPayloadDirectory);
			if (Directory.Exists(sourcePayloadDirectory))
				CopyDirectory(sourcePayloadDirectory, destinationPayloadDirectory);
		}

		/// <summary>
		/// Deletes one scripted replay XML and its generated-payload sidecar directory.
		/// </summary>
		public static void DeleteArtifacts(string p_strCachePath)
		{
			if (String.IsNullOrWhiteSpace(p_strCachePath))
				return;
			DeleteFile(p_strCachePath);
			string payloadDirectory = GetPayloadDirectoryPath(p_strCachePath);
			if (Directory.Exists(payloadDirectory))
				DeleteDirectoryTree(payloadDirectory);
		}

		/// <summary>
		/// Returns the sidecar directory containing generated replay payloads for a cache file.
		/// </summary>
		public static string GetPayloadDirectoryPath(string p_strCachePath)
		{
			return Path.Combine(Path.GetDirectoryName(p_strCachePath) ?? String.Empty, Path.GetFileNameWithoutExtension(p_strCachePath) + ".payload");
		}

		/// <summary>
		/// Loads or creates the replay document used for incremental scripted-operation persistence.
		/// </summary>
		private void EnsureDocumentForWrite()
		{
			string directory = Path.GetDirectoryName(m_strFilePath);
			if (!String.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
				Directory.CreateDirectory(directory);

			if (m_xelRoot != null)
				return;

			if (File.Exists(m_strFilePath))
			{
				m_docLog = XDocument.Load(m_strFilePath);
				m_xelRoot = m_docLog.Descendants("FileList").FirstOrDefault();
				if (m_xelRoot == null)
					throw new InvalidDataException("The scripted replay cache does not contain a FileList root element.");
				return;
			}

			m_docLog = new XDocument();
			m_xelRoot = new XElement("FileList",
				new XAttribute("ModName", m_strModName ?? String.Empty),
				new XAttribute("ModVersion", m_strModVersion ?? String.Empty),
				new XAttribute("ReplayVersion", ReplayVersion));
			m_docLog.Add(m_xelRoot);
		}

		/// <summary>
		/// Resolves and validates one generated-payload sidecar path.
		/// </summary>
		private string ResolvePayloadPath(string p_strPayloadName)
		{
			if (String.IsNullOrWhiteSpace(p_strPayloadName) || Path.IsPathRooted(p_strPayloadName) || p_strPayloadName.IndexOf(Path.DirectorySeparatorChar) >= 0 || p_strPayloadName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
				throw new InvalidDataException("The scripted replay payload path is invalid.");

			string payloadDirectory = Path.GetFullPath(GetPayloadDirectoryPath(m_strFilePath));
			string payloadPath = Path.GetFullPath(Path.Combine(payloadDirectory, p_strPayloadName));
			string prefix = payloadDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (!payloadPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("The scripted replay payload path escapes its sidecar directory.");
			return payloadPath;
		}

		/// <summary>
		/// Validates the length and integrity hash of one generated replay payload.
		/// </summary>
		private static void ValidatePayload(string p_strPayloadPath, long p_lngExpectedLength, string p_strExpectedHash)
		{
			if (!File.Exists(p_strPayloadPath))
				throw new FileNotFoundException("A generated scripted replay payload is missing.", p_strPayloadPath);

			var info = new FileInfo(p_strPayloadPath);
			if (info.Length != p_lngExpectedLength)
				throw new InvalidDataException(String.Format("Generated scripted replay payload '{0}' has an unexpected length.", p_strPayloadPath));
			if (String.IsNullOrWhiteSpace(p_strExpectedHash))
				throw new InvalidDataException(String.Format("Generated scripted replay payload '{0}' does not contain an integrity hash.", p_strPayloadPath));

			using (FileStream stream = File.OpenRead(p_strPayloadPath))
			using (SHA256 sha = SHA256.Create())
			{
				string actualHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", String.Empty);
				if (!actualHash.Equals(p_strExpectedHash, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException(String.Format("Generated scripted replay payload '{0}' failed its integrity check.", p_strPayloadPath));
			}
		}

		/// <summary>
		/// Computes the SHA-256 hash stored for a generated replay payload.
		/// </summary>
		private static string ComputeHash(byte[] p_bteData)
		{
			using (SHA256 sha = SHA256.Create())
				return BitConverter.ToString(sha.ComputeHash(p_bteData)).Replace("-", String.Empty);
		}

		/// <summary>
		/// Reads an XML attribute without throwing when it is absent.
		/// </summary>
		private static string GetAttributeValue(XElement p_xelElement, string p_strName)
		{
			XAttribute attribute = p_xelElement.Attribute(p_strName);
			return attribute == null ? String.Empty : attribute.Value;
		}

		/// <summary>
		/// Creates one archive-file replay entry.
		/// </summary>
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

		/// <summary>
		/// Copies a generated-payload sidecar directory recursively.
		/// </summary>
		private static void CopyDirectory(string p_strSourceDirectory, string p_strDestinationDirectory)
		{
			Directory.CreateDirectory(p_strDestinationDirectory);
			foreach (string file in Directory.GetFiles(p_strSourceDirectory))
				File.Copy(file, Path.Combine(p_strDestinationDirectory, Path.GetFileName(file)), true);
			foreach (string directory in Directory.GetDirectories(p_strSourceDirectory))
				CopyDirectory(directory, Path.Combine(p_strDestinationDirectory, Path.GetFileName(directory)));
		}

		/// <summary>
		/// Deletes a replay artifact after clearing the read-only attribute.
		/// </summary>
		private static void DeleteFile(string p_strPath)
		{
			if (!File.Exists(p_strPath))
				return;
			File.SetAttributes(p_strPath, FileAttributes.Normal);
			File.Delete(p_strPath);
		}

		/// <summary>
		/// Deletes a replay sidecar tree after clearing read-only attributes.
		/// </summary>
		private static void DeleteDirectoryTree(string p_strDirectory)
		{
			foreach (string file in Directory.GetFiles(p_strDirectory, "*", SearchOption.AllDirectories))
				File.SetAttributes(file, FileAttributes.Normal);
			foreach (string directory in Directory.GetDirectories(p_strDirectory, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
				File.SetAttributes(directory, FileAttributes.Directory);
			File.SetAttributes(p_strDirectory, FileAttributes.Directory);
			Directory.Delete(p_strDirectory, true);
		}

		#endregion
	}
}
