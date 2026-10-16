namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Runtime.CompilerServices;
	using System.Threading;
	using System.Xml.Linq;

	using Nexus.Client.Util;

	internal sealed class XmlVirtualModStore : IVirtualModStore
	{
		public bool IsReadyForWrite(string filePath)
		{
			int intRepeat = 0;
			bool booLocked = false;

			while (!FileUtil.IsFileReady(filePath))
			{
				WaitBeforeFileReadyRetry(100);
				if (intRepeat++ > 50)
				{
					Trace.TraceWarning("Could not get access to \"{0}\".", filePath);
					booLocked = true;
					break;
				}
			}

			return !booLocked;
		}

		public void Save(Version fileVersion, string filePath, IList<IVirtualModInfo> virtualModInfo, IEnumerable<IVirtualModLink> virtualModLink)
		{
			Dictionary<IVirtualModInfo, List<IVirtualModLink>> dicVirtualLinksByModInfo = BuildVirtualLinksByModInfo(virtualModLink);
			XStreamingElement docVirtual = BuildXmlDocument(fileVersion, virtualModInfo, mod => GetVirtualLinksForMod(dicVirtualLinksByModInfo, mod));

			SaveXmlDocumentSafely(docVirtual, filePath);
		}

		public void SaveWithModInfoMatching(Version fileVersion, string filePath, IList<IVirtualModInfo> virtualModInfo, IEnumerable<IVirtualModLink> virtualModLink)
		{
			MatchingLinkIndex links = new MatchingLinkIndex(virtualModLink);
			XStreamingElement docVirtual = BuildXmlDocument(fileVersion, virtualModInfo, links.GetLinks);

			SaveXmlDocumentSafely(docVirtual, filePath);
		}

		public VirtualModStoreData Load(string filePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion)
		{
			VirtualModStoreData data;
			Load(filePath, currentVersion, isValidVersion, getMissingFileVersion, false, out data);
			return data;
		}

		/// <summary>Requires a complete legacy parse before migration or backup restore can replace native state.</summary>
		internal VirtualModStoreData LoadForImport(string path, Version version, Func<Version, bool> validVersion, Func<string, string, string> missingVersion)
		{
			if (!File.Exists(path)) throw new FileNotFoundException("The virtual mod snapshot is missing.", path);
			try
			{
				VirtualModStoreData data;
				Load(path, version, validVersion, missingVersion, false, out data, true);
				return data;
			}
			catch (Exception exception)
			{
				throw new InvalidDataException("The legacy virtual mod snapshot could not be read completely. The original file has been preserved.", exception);
			}
		}

		/// <summary>Checks migration values and owner ordering using the same legacy serializer used by profiles.</summary>
		internal static void ValidateImport(Version version, VirtualModStoreData expected, VirtualModStoreData actual)
		{
			Dictionary<IVirtualModInfo, List<IVirtualModLink>> left = BuildVirtualLinksByModInfo(expected.VirtualLinks);
			Dictionary<IVirtualModInfo, List<IVirtualModLink>> right = BuildVirtualLinksByModInfo(actual.VirtualLinks);
			string before = BuildXmlDocument(version, expected.VirtualMods, mod => GetVirtualLinksForMod(left, mod)).ToString(SaveOptions.DisableFormatting);
			string after = BuildXmlDocument(version, actual.VirtualMods, mod => GetVirtualLinksForMod(right, mod)).ToString(SaveOptions.DisableFormatting);
			if (!String.Equals(before, after, StringComparison.Ordinal))
				throw new InvalidDataException("Virtual mod migration did not preserve the legacy registrations and ordered links.");
		}

		public bool TryLoad(string filePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion, out VirtualModStoreData data)
		{
			return Load(filePath, currentVersion, isValidVersion, getMissingFileVersion, true, out data);
		}

		public void Copy(string sourcePath, string destinationPath)
		{
			if (IsReadyForWrite(destinationPath))
				CopyFileSafely(sourcePath, destinationPath);
		}

		/// <summary>Streams the existing XML format without retaining an element tree for all installed links.</summary>
		private static XStreamingElement BuildXmlDocument<TModInfo>(Version fileVersion, IEnumerable<TModInfo> virtualModInfo, Func<TModInfo, IEnumerable<IVirtualModLink>> getLinks) where TModInfo : IVirtualModInfo
		{
			return new XStreamingElement("virtualModActivator", new XAttribute("fileVersion", fileVersion),
				new XStreamingElement("modList", from mod in virtualModInfo
					select new XStreamingElement("modInfo",
						new XAttribute("modId", mod.ModId ?? string.Empty),
						new XAttribute("downloadId", mod.DownloadId ?? string.Empty),
						new XAttribute("updatedDownloadId", mod.UpdatedDownloadId ?? string.Empty),
						new XAttribute("modName", mod.ModName),
						new XAttribute("modFileName", mod.ModFileName),
						new XAttribute("modNewFileName", mod.NewFileName ?? string.Empty),
						new XAttribute("modFilePath", mod.ModFilePath),
						new XAttribute("FileVersion", mod.FileVersion ?? string.Empty),
						from link in getLinks(mod)
						select new XStreamingElement("fileLink",
							new XAttribute("realPath", link.RealModPath),
							new XAttribute("virtualPath", link.VirtualModPath),
							new XAttribute("installRoot", NormalizeInstallRoot(link.InstallRoot)),
							new XStreamingElement("linkPriority", link.Priority.ToString()),
							new XStreamingElement("isActive", link.Active.ToString())))));
		}

		private static bool Load(string filePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion, bool reportSuccess, out VirtualModStoreData data, bool requireComplete = false)
		{
			List<string> lstAddedModInfo = new List<string>();
			List<IVirtualModLink> lstVirtualLinks = new List<IVirtualModLink>();
			List<IVirtualModInfo> lstVirtualMods = new List<IVirtualModInfo>();

			data = new VirtualModStoreData(lstVirtualMods, lstVirtualLinks);

			if (File.Exists(filePath))
			{
				XDocument docVirtual;
				using (var sr = new StreamReader(filePath))
				{
					docVirtual = XDocument.Load(sr);
				}

				string strVersion = docVirtual.Element("virtualModActivator").Attribute("fileVersion").Value;
				if (!isValidVersion(new Version(strVersion)))
					throw new Exception(string.Format("Invalid Virtual Mod Activator version: {0} Expecting {1}", strVersion, currentVersion));

				try
				{
					XElement xelModList = docVirtual.Descendants("modList").FirstOrDefault();
					if ((xelModList != null) && xelModList.HasElements)
					{
						foreach (XElement xelMod in xelModList.Elements("modInfo"))
						{
							LoadMod(xelMod, lstAddedModInfo, lstVirtualMods, lstVirtualLinks, getMissingFileVersion, requireComplete);
						}

						if (reportSuccess)
							return true;
					}
				}
				catch { if (requireComplete) throw; }
			}

			return false;
		}

		private static void LoadMod(XElement xelMod, List<string> lstAddedModInfo, List<IVirtualModInfo> lstVirtualMods, List<IVirtualModLink> lstVirtualLinks, Func<string, string, string> getMissingFileVersion, bool keepEmptyMods = false)
		{
			string strModId = xelMod.Attribute("modId").Value;
			string strDownloadId = string.Empty;
			string strUpdatedDownloadId = string.Empty;
			string strNewFileName = string.Empty;
			string strFileVersion = string.Empty;

			try
			{
				strDownloadId = xelMod.Attribute("downloadId").Value;
			}
			catch { }

			try
			{
				strUpdatedDownloadId = xelMod.Attribute("updatedDownloadId").Value;
			}
			catch { }

			string strModName = xelMod.Attribute("modName").Value;
			string strModFileName = xelMod.Attribute("modFileName").Value;

			if (lstAddedModInfo.Contains(strModFileName, StringComparer.InvariantCultureIgnoreCase))
			{
				if (keepEmptyMods) throw new InvalidDataException("The legacy virtual mod snapshot contains duplicate archive filenames: " + strModFileName);
				return;
			}

			try
			{
				strNewFileName = xelMod.Attribute("modNewFileName").Value;
			}
			catch { }

			string strModFilePath = xelMod.Attribute("modFilePath").Value;

			try
			{
				strFileVersion = xelMod.Attribute("FileVersion").Value;
			}
			catch
			{
				strFileVersion = getMissingFileVersion(strModFileName, strDownloadId);
			}

			VirtualModInfo vmiMod = new VirtualModInfo(strModId, strDownloadId, strUpdatedDownloadId, strModName, strModFileName, strNewFileName, strModFilePath, strFileVersion);

			bool booNoFileLink = true;

			foreach (XElement xelLink in xelMod.Elements("fileLink"))
			{
				string strRealPath = xelLink.Attribute("realPath").Value;
				string strVirtualPath = xelLink.Attribute("virtualPath").Value;
				int intPriority = 0;
				try
				{
					intPriority = Convert.ToInt32(xelLink.Element("linkPriority").Value);
				}
				catch { }
				bool booActive = false;
				try
				{
					booActive = Convert.ToBoolean(xelLink.Element("isActive").Value);
				}
				catch { }

				if (booNoFileLink)
				{
					booNoFileLink = false;
					lstVirtualMods.Add(vmiMod);
					lstAddedModInfo.Add(strModFileName);
				}

				ModInstallRoot installRoot = ModInstallRoot.Data;
				XAttribute installRootAttribute = xelLink.Attribute("installRoot");
				if (installRootAttribute != null && !String.IsNullOrWhiteSpace(installRootAttribute.Value))
				{
					ModInstallRoot parsedRoot;
					if (!Enum.TryParse(installRootAttribute.Value, true, out parsedRoot))
						throw new InvalidDataException(String.Format("Invalid Virtual link install root '{0}'.", installRootAttribute.Value));
					installRoot = NormalizeInstallRoot(parsedRoot);
				}

				lstVirtualLinks.Add(new VirtualModLink(strRealPath, strVirtualPath, intPriority, booActive, vmiMod, installRoot));
			}
			if (keepEmptyMods && booNoFileLink)
			{
				lstVirtualMods.Add(vmiMod);
				lstAddedModInfo.Add(strModFileName);
			}
		}

		private static ModInstallRoot NormalizeInstallRoot(ModInstallRoot installRoot)
		{
			if (installRoot == ModInstallRoot.Data)
				return ModInstallRoot.Data;
			if (installRoot == ModInstallRoot.GameRoot)
				return ModInstallRoot.GameRoot;
			throw new InvalidDataException(String.Format("Unsupported Virtual link install root '{0}'.", installRoot));
		}

		private static Dictionary<IVirtualModInfo, List<IVirtualModLink>> BuildVirtualLinksByModInfo(IEnumerable<IVirtualModLink> virtualModLink)
		{
			Dictionary<IVirtualModInfo, List<IVirtualModLink>> dicVirtualLinksByModInfo = new Dictionary<IVirtualModInfo, List<IVirtualModLink>>(VirtualModInfoReferenceComparer.Instance);

			foreach (IVirtualModLink link in virtualModLink)
			{
				if (link.ModInfo == null)
					continue;

				List<IVirtualModLink> lstVirtualLinks;
				if (!dicVirtualLinksByModInfo.TryGetValue(link.ModInfo, out lstVirtualLinks))
				{
					lstVirtualLinks = new List<IVirtualModLink>();
					dicVirtualLinksByModInfo.Add(link.ModInfo, lstVirtualLinks);
				}

				lstVirtualLinks.Add(link);
			}

			return dicVirtualLinksByModInfo;
		}

		private static IEnumerable<IVirtualModLink> GetVirtualLinksForMod(Dictionary<IVirtualModInfo, List<IVirtualModLink>> p_dicVirtualLinksByModInfo, IVirtualModInfo p_vmiModInfo)
		{
			List<IVirtualModLink> lstVirtualLinks;
			if (p_dicVirtualLinksByModInfo.TryGetValue(p_vmiModInfo, out lstVirtualLinks))
				return lstVirtualLinks;

			return Enumerable.Empty<IVirtualModLink>();
		}

		/// <summary>Indexes the legacy download-ID-or-filename match without rescanning every link for every mod.</summary>
		internal sealed class MatchingLinkIndex
		{
			private readonly List<IVirtualModLink> _links;
			private readonly Dictionary<string, List<int>> _byDownloadId = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
			private readonly Dictionary<string, List<int>> _byFileName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

			/// <summary>Captures input positions so OR matches preserve link order and duplicate input occurrences.</summary>
			internal MatchingLinkIndex(IEnumerable<IVirtualModLink> links)
			{
				_links = links.ToList();
				for (int index = 0; index < _links.Count; index++)
				{
					Add(_byDownloadId, _links[index].ModInfo.DownloadId, index);
					Add(_byFileName, _links[index].ModInfo.ModFileName, index);
				}
			}

			/// <summary>Merges both matching identity lists, emitting each input position exactly once.</summary>
			internal IEnumerable<IVirtualModLink> GetLinks(IVirtualModInfo mod)
			{
				List<int> byId = null;
				List<int> byName = null;
				if (!String.IsNullOrEmpty(mod.DownloadId)) _byDownloadId.TryGetValue(mod.DownloadId, out byId);
				if (!String.IsNullOrEmpty(mod.ModFileName)) _byFileName.TryGetValue(mod.ModFileName, out byName);
				int idCount = byId == null ? 0 : byId.Count;
				int nameCount = byName == null ? 0 : byName.Count;
				int idIndex = 0;
				int nameIndex = 0;
				while (idIndex < idCount || nameIndex < nameCount)
				{
					int next = Math.Min(idIndex < idCount ? byId[idIndex] : Int32.MaxValue,
						nameIndex < nameCount ? byName[nameIndex] : Int32.MaxValue);
					if (idIndex < idCount && byId[idIndex] == next) idIndex++;
					if (nameIndex < nameCount && byName[nameIndex] == next) nameIndex++;
					yield return _links[next];
				}
			}

			/// <summary>Indexes only identities that the legacy matching predicate considers usable.</summary>
			private static void Add(Dictionary<string, List<int>> index, string key, int position)
			{
				if (String.IsNullOrEmpty(key)) return;
				List<int> positions;
				if (!index.TryGetValue(key, out positions))
				{
					positions = new List<int>();
					index.Add(key, positions);
				}
				positions.Add(position);
			}
		}

		private sealed class VirtualModInfoReferenceComparer : IEqualityComparer<IVirtualModInfo>
		{
			public static readonly VirtualModInfoReferenceComparer Instance = new VirtualModInfoReferenceComparer();

			private VirtualModInfoReferenceComparer()
			{
			}

			public bool Equals(IVirtualModInfo p_vmiA, IVirtualModInfo p_vmiB)
			{
				return ReferenceEquals(p_vmiA, p_vmiB);
			}

			public int GetHashCode(IVirtualModInfo p_vmiModInfo)
			{
				return RuntimeHelpers.GetHashCode(p_vmiModInfo);
			}
		}

		private static void WaitBeforeFileReadyRetry(int milliseconds)
		{
			if (milliseconds <= 0)
				return;

			using (ManualResetEventSlim retryWait = new ManualResetEventSlim(false))
			{
				retryWait.Wait(milliseconds);
			}
		}

		private static void SaveXmlDocumentSafely(XStreamingElement document, string filePath)
		{
			string tempFilePath = GetTempFilePath(filePath);

			try
			{
				using (StreamWriter streamWriter = File.CreateText(tempFilePath))
				{
					document.Save(streamWriter);
				}

				ReplaceFileWithTempFile(tempFilePath, filePath);
				tempFilePath = null;
			}
			finally
			{
				DeleteTempFile(tempFilePath);
			}
		}

		private static void CopyFileSafely(string sourcePath, string destinationPath)
		{
			string tempFilePath = GetTempFilePath(destinationPath);

			try
			{
				using (FileStream inputFile = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				using (FileStream outputFile = new FileStream(tempFilePath, FileMode.CreateNew))
				{
					byte[] buffer = new byte[0x10000];
					int bytes;

					while ((bytes = inputFile.Read(buffer, 0, buffer.Length)) > 0)
					{
						outputFile.Write(buffer, 0, bytes);
					}
				}

				ReplaceFileWithTempFile(tempFilePath, destinationPath);
				tempFilePath = null;
			}
			finally
			{
				DeleteTempFile(tempFilePath);
			}
		}

		private static string GetTempFilePath(string filePath)
		{
			string fullPath = Path.GetFullPath(filePath);
			string directory = Path.GetDirectoryName(fullPath);

			return Path.Combine(directory, Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
		}

		private static void ReplaceFileWithTempFile(string tempFilePath, string filePath)
		{
			if (File.Exists(filePath))
			{
				File.Replace(tempFilePath, filePath, null, true);
				return;
			}

			File.Move(tempFilePath, filePath);
		}

		private static void DeleteTempFile(string tempFilePath)
		{
			if (string.IsNullOrEmpty(tempFilePath))
				return;

			try
			{
				if (File.Exists(tempFilePath))
					File.Delete(tempFilePath);
			}
			catch (Exception e)
			{
				Trace.TraceWarning("Could not delete temporary virtual mod list file \"{0}\": {1}", tempFilePath, e.Message);
			}
		}
	}
}
