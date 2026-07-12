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
			XDocument docVirtual = BuildXmlDocument(fileVersion, virtualModInfo, mod => GetVirtualLinksForMod(dicVirtualLinksByModInfo, mod));

			SaveXmlDocumentSafely(docVirtual, filePath);
		}

		public void SaveWithModInfoMatching(Version fileVersion, string filePath, IList<IVirtualModInfo> virtualModInfo, IEnumerable<IVirtualModLink> virtualModLink)
		{
			XDocument docVirtual = BuildXmlDocument(fileVersion, virtualModInfo, mod => virtualModLink.Where(x => CheckModInfo(x.ModInfo, mod) == true));

			SaveXmlDocumentSafely(docVirtual, filePath);
		}

		public VirtualModStoreData Load(string filePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion)
		{
			VirtualModStoreData data;
			Load(filePath, currentVersion, isValidVersion, getMissingFileVersion, false, out data);
			return data;
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

		private static XDocument BuildXmlDocument<TModInfo>(Version fileVersion, IEnumerable<TModInfo> virtualModInfo, Func<TModInfo, IEnumerable<IVirtualModLink>> getLinks) where TModInfo : IVirtualModInfo
		{
			XDocument docVirtual = new XDocument();
			XElement xelRoot = new XElement("virtualModActivator", new XAttribute("fileVersion", fileVersion));
			docVirtual.Add(xelRoot);

			XElement xelModList = new XElement("modList");
			xelRoot.Add(xelModList);
			xelModList.Add(from mod in virtualModInfo
						   select new XElement("modInfo",
							   new XAttribute("modId", mod.ModId ?? string.Empty),
							   new XAttribute("downloadId", mod.DownloadId ?? string.Empty),
							   new XAttribute("updatedDownloadId", mod.UpdatedDownloadId ?? string.Empty),
							   new XAttribute("modName", mod.ModName),
							   new XAttribute("modFileName", mod.ModFileName),
							   new XAttribute("modNewFileName", mod.NewFileName ?? string.Empty),
							   new XAttribute("modFilePath", mod.ModFilePath),
							   new XAttribute("FileVersion", mod.FileVersion ?? string.Empty),
							   from link in getLinks(mod)
							   select new XElement("fileLink",
								   new XAttribute("realPath", link.RealModPath),
								   new XAttribute("virtualPath", link.VirtualModPath),
								   new XElement("linkPriority",
									   new XText(link.Priority.ToString())),
								   new XElement("isActive",
									   new XText(link.Active.ToString())))));

			return docVirtual;
		}

		private static bool Load(string filePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion, bool reportSuccess, out VirtualModStoreData data)
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
							LoadMod(xelMod, lstAddedModInfo, lstVirtualMods, lstVirtualLinks, getMissingFileVersion);
						}

						if (reportSuccess)
							return true;
					}
				}
				catch { }
			}

			return false;
		}

		private static void LoadMod(XElement xelMod, List<string> lstAddedModInfo, List<IVirtualModInfo> lstVirtualMods, List<IVirtualModLink> lstVirtualLinks, Func<string, string, string> getMissingFileVersion)
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
				return;

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

				lstVirtualLinks.Add(new VirtualModLink(strRealPath, strVirtualPath, intPriority, booActive, vmiMod));
			}
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

		private static bool CheckModInfo(IVirtualModInfo p_vmiA, IVirtualModInfo p_vmiB)
		{
			if (!string.IsNullOrEmpty(p_vmiA.DownloadId) && !string.IsNullOrEmpty(p_vmiB.DownloadId))
				if (p_vmiA.DownloadId.Equals(p_vmiB.DownloadId, StringComparison.OrdinalIgnoreCase))
					return true;

			if (!string.IsNullOrEmpty(p_vmiA.ModFileName) && !string.IsNullOrEmpty(p_vmiB.ModFileName))
				if (p_vmiA.ModFileName.Equals(p_vmiB.ModFileName, StringComparison.OrdinalIgnoreCase))
					return true;

			return false;
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

		private static void SaveXmlDocumentSafely(XDocument document, string filePath)
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
