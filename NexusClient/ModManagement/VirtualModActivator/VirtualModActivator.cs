using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ChinhDo.Transactions;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	public class VirtualModActivator : IVirtualModActivator
	{
		[DllImport("kernel32.dll")]
		static extern bool CreateSymbolicLink(string p_strLinkName, string p_strTargetPath, int dwFlags);

		[DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
		static extern bool CreateHardLink(string p_strLinkName, string p_strTargetPath, IntPtr lpSecurityAttributes);

		public event EventHandler ModActivationChanged;

		#region Static Properties

		private static readonly Version CURRENT_VERSION = new Version("0.2.0.0");
		private static readonly string ACTIVATOR_FILE = "VirtualModConfig.xml";
		private static readonly string ACTIVATOR_INIEDITS = "IniEdits.xml";
		public static readonly string ACTIVATOR_FOLDER = "Virtual";
		public static readonly string ACTIVATOR_LINK_FOLDER = "NMMLink";


		/// <summary>
		/// Reads the virtual mod activator version from the given config file.
		/// </summary>
		/// /// <param name="p_strVirtualActivatorConfigPath">The config file whose version is to be read.</param>
		/// <returns>The version of the specified config file, or a version of
		/// <c>0.0.0.0</c> if the file format is not recognized.</returns>
		public static Version ReadVersion(string p_strVirtualActivatorConfigPath)
		{
			if (!File.Exists(p_strVirtualActivatorConfigPath))
				return new Version("0.0.0.0");

			XDocument docVirtual = XDocument.Load(p_strVirtualActivatorConfigPath);

			XElement xelVirtual = docVirtual.Element("virtualModActivator");
			if (xelVirtual == null)
				return new Version("0.0.0.0");

			XAttribute xatVersion = xelVirtual.Attribute("fileVersion");
			if (xatVersion == null)
				return new Version("0.0.0.0");

			return new Version(xatVersion.Value);
		}

		/// <summary>
		/// Determines if the config file is valid.
		/// </summary>
		/// <returns><c>true</c> if the config file is valid;
		/// <c>false</c> otherwise.</returns>
		protected static bool IsValid(string p_strVirtualActivatorConfigPath)
		{
			if (!File.Exists(p_strVirtualActivatorConfigPath))
				return false;
			try
			{
				XDocument docVirtual = XDocument.Load(p_strVirtualActivatorConfigPath);
			}
			catch (Exception e)
			{
				Trace.TraceError("Invalid Virtual Mod Activator File ({0}):", p_strVirtualActivatorConfigPath);
				Trace.Indent();
				TraceUtil.TraceException(e);
				Trace.Unindent();
				return false;
			}
			return true;
		}
		#endregion

		private ThreadSafeObservableList<IVirtualModLink> m_tslVirtualModList = new ThreadSafeObservableList<IVirtualModLink>();
		private ThreadSafeObservableList<IVirtualModInfo> m_tslVirtualModInfo = new ThreadSafeObservableList<IVirtualModInfo>();
		private bool m_booInitialized = false;
		private bool m_booDisableLinkCreation = false;
		private bool m_booDisableIniLogging = false;
		private string m_strGameDataPath = String.Empty;
		private string m_strVirtualActivatorPath = String.Empty;
		private string m_strVirtualActivatorConfigPath = String.Empty;
		private string m_strVirtualActivatorIniEditsPath = String.Empty;

		#region Properties

		/// <summary>
		/// Gets the mod manager to use to manage mods.
		/// </summary>
		/// <value>The mod manager to use to manage mods.</value>
		protected ModManager ModManager { get; private set; }

		/// <summary>
		/// Gets the plugin manager to use to manage plugins.
		/// </summary>
		/// <value>The plugin manager to use to manage plugins.</value>
		protected IPluginManager PluginManager { get; private set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the install log that tracks mod install info
		/// for the current game mode.
		/// </summary>
		/// <value>The install log that tracks mod install info
		/// for the current game mode.</value>
		protected IInstallLog ModInstallLog { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		public string VirtualFoder
		{
			get
			{
				if (EnvironmentInfo != null)
					if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.VirtualFolder[GameMode.ModeId]))
						return Path.Combine(EnvironmentInfo.Settings.VirtualFolder[GameMode.ModeId], ACTIVATOR_FOLDER);

				return String.Empty;
			}
		}

		public string HDLinkFolder
		{
			get
			{
				if (EnvironmentInfo != null)
					if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.HDLinkFolder[GameMode.ModeId]))
						return Path.Combine(Path.Combine(EnvironmentInfo.Settings.HDLinkFolder[GameMode.ModeId], GameMode.ModeId), ACTIVATOR_LINK_FOLDER);

				return String.Empty;
			}
		}

		public string VirtualInstallFoder
		{
			get
			{
				if (EnvironmentInfo != null)
					if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.VirtualFolder[GameMode.ModeId]))
						return Path.Combine(Path.Combine(EnvironmentInfo.Settings.VirtualFolder[GameMode.ModeId], ACTIVATOR_FOLDER), "Install");

				return String.Empty;
			}
		}

		public string HDLinkInstallFoder
		{
			get
			{
				if (EnvironmentInfo != null)
					if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.VirtualFolder[GameMode.ModeId]))
						return Path.Combine(EnvironmentInfo.Settings.VirtualFolder[GameMode.ModeId], Path.Combine(ACTIVATOR_LINK_FOLDER, "Install"));

				return String.Empty;
			}
		}

		/// <summary>
		/// Gets the current support version of the virtual mod activator.
		/// </summary>
		/// <value>The current support version of the virtual mod activator.</value>
		public static Version CurrentVersion
		{
			get
			{
				return CURRENT_VERSION;
			}
		}

		public bool ForceHardLinks 
		{
			get
			{
				return EnvironmentInfo.Settings.ForceHardLinks;
			}
		}

		public bool Initialized
		{
			get
			{
				return m_booInitialized;
			}
		}

		public bool DisableLinkCreation
		{
			get
			{
				return m_booDisableLinkCreation;
			}
			set
			{
				m_booDisableLinkCreation = value;
			}
		}

		public bool DisableIniLogging
		{
			get
			{
				return m_booDisableIniLogging;
			}
		}

		public IEnumerable<string> ActiveModList
		{
			get
			{
				return VirtualMods.Select(x => x.ModFileName.ToLowerInvariant());
			}
		}

		public string VirtualPath
		{
			get
			{
				return m_strVirtualActivatorPath;
			}
		}

		public ThreadSafeObservableList<IVirtualModLink> VirtualLinks
		{
			get
			{
				return m_tslVirtualModList;
			}
		}

		public ThreadSafeObservableList<IVirtualModInfo> VirtualMods
		{
			get
			{
				return m_tslVirtualModInfo;
			}
		}

		public Int32 ModCount
		{
			get
			{
				return m_tslVirtualModInfo.Count;
			}
		}

		#endregion

		#region Constructors

		public VirtualModActivator(ModManager p_mmgModManager, IPluginManager p_pmgPluginManager, IGameMode p_gmdGameMode, IInstallLog p_ilgModInstallLog, IEnvironmentInfo p_eifEnvironmentInfo, string p_strModFolder)
		{
			ModManager = p_mmgModManager;
			PluginManager = p_pmgPluginManager;
			GameMode = p_gmdGameMode;
			ModInstallLog = p_ilgModInstallLog;
			EnvironmentInfo = p_eifEnvironmentInfo;
			m_strVirtualActivatorPath = Path.Combine(p_strModFolder, ACTIVATOR_FOLDER);
			m_strGameDataPath = GameMode.PluginDirectory;
			m_strVirtualActivatorConfigPath = Path.Combine(m_strVirtualActivatorPath, ACTIVATOR_FILE);
			m_strVirtualActivatorIniEditsPath = Path.Combine(m_strVirtualActivatorPath, ACTIVATOR_INIEDITS);
		}

		#endregion

		#region Virtual Mod Activator

		#region List Management

		public void Initialize()
		{
			if (IsValid(m_strVirtualActivatorConfigPath))
				if (ReadVersion(m_strVirtualActivatorConfigPath) == CURRENT_VERSION)
				{
					SetCurrentList(LoadList(m_strVirtualActivatorConfigPath));
					m_booInitialized = true;
				}
		}

		public void Setup()
		{
			SaveList();
			SetCurrentList(LoadList(m_strVirtualActivatorConfigPath));
			m_booInitialized = true;
		}

		public void SaveList()
		{
			if (!Directory.Exists(m_strVirtualActivatorPath))
				Directory.CreateDirectory(m_strVirtualActivatorPath);
			SaveModList();
		}

		private void SaveModList()
		{
			SaveModList(m_strVirtualActivatorConfigPath);
		}

		public void SaveModList(string p_strPath)
		{
			XDocument docVirtual = new XDocument();
			XElement xelRoot = new XElement("virtualModActivator", new XAttribute("fileVersion", CURRENT_VERSION));
			docVirtual.Add(xelRoot);
			
			XElement xelModList = new XElement("modList");
			xelRoot.Add(xelModList);
			xelModList.Add(from mod in m_tslVirtualModInfo
						   select new XElement("modInfo",
							   new XAttribute("modId", mod.ModId),
							   new XAttribute("modName", mod.ModName),
							   new XAttribute("modFileName", mod.ModFileName),
							   new XAttribute("modFilePath", mod.ModFilePath),
							   from link in m_tslVirtualModList.Where(x => x.ModInfo == mod)
							   select new XElement("fileLink",
								   new XAttribute("realPath", link.RealModPath),
								   new XAttribute("virtualPath", link.VirtualModPath),
								   new XElement("linkPriority",
									   new XText(link.Priority.ToString())),
								   new XElement("isActive",
									   new XText(link.Active.ToString())))));

			//XElement xelLinkList = new XElement("linkList");
			//xelRoot.Add(xelLinkList);
			//xelLinkList.Add(from vml in m_tslVirtualModList
			//				select new XElement("link",
			//					new XAttribute("realPath", vml.RealModPath),
			//					new XAttribute("virtualPath", vml.VirtualModPath),
			//					new XElement("modName",
			//						new XText(vml.ModName)),
			//					new XElement("modFileName",
			//						new XText(vml.ModFileName)),
			//					new XElement("modId",
			//						new XText(vml.ModId)),
			//					new XElement("linkPriority",
			//						new XText(vml.Priority.ToString())),
			//					new XElement("isActive",
			//						new XText(vml.Active.ToString()))));

			docVirtual.Save(p_strPath);
		}

		public void SetCurrentList(IList<IVirtualModLink> p_ilvVirtualLinks)
		{
			m_tslVirtualModList.Clear();
			m_tslVirtualModList = new ThreadSafeObservableList<IVirtualModLink>(p_ilvVirtualLinks);
		}

		public List<IVirtualModLink> LoadList(string p_strXMLFilePath)
		{
			List<IVirtualModLink> lstVirtualLinks = new List<IVirtualModLink>();
			List<IVirtualModInfo> lstVirtualMods = new List<IVirtualModInfo>();

			if (File.Exists(p_strXMLFilePath))
			{
				XDocument docVirtual = XDocument.Load(p_strXMLFilePath);
				string strVersion = docVirtual.Element("virtualModActivator").Attribute("fileVersion").Value;
				if (!CURRENT_VERSION.ToString().Equals(strVersion))
					throw new Exception(String.Format("Invalid Virtual Mod Activator version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

				try
				{
					XElement xelModList = docVirtual.Descendants("modList").FirstOrDefault();
					if ((xelModList != null) && xelModList.HasElements)
					{
						foreach (XElement xelMod in xelModList.Elements("modInfo"))
						{
							string strModId = xelMod.Attribute("modId").Value;
							string strModName = xelMod.Attribute("modName").Value;
							string strModFileName = xelMod.Attribute("modFileName").Value;
							string strModFilePath = xelMod.Attribute("modFilePath").Value;

							VirtualModInfo vmiMod = new VirtualModInfo(strModId, strModName, strModFileName, strModFilePath);
							lstVirtualMods.Add(vmiMod);

							foreach (XElement xelLink in xelMod.Elements("fileLink"))
							{
								string strRealPath = xelLink.Attribute("realPath").Value;
								string strVirtualPath = xelLink.Attribute("virtualPath").Value;
								Int32 intPriority = 0;
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

								lstVirtualLinks.Add(new VirtualModLink(strRealPath, strVirtualPath, intPriority, booActive, vmiMod));
							}
						}
					}
				}
				catch { }
			}
			m_tslVirtualModInfo.Clear();
			m_tslVirtualModInfo = new ThreadSafeObservableList<IVirtualModInfo>(lstVirtualMods);
			return lstVirtualLinks;
		}

		public bool LoadListOnDemand(string p_strProfilePath, out List<IVirtualModLink> p_lstVirtualLinks, out List<IVirtualModInfo> p_lstVirtualMods)
		{
			p_lstVirtualLinks = new List<IVirtualModLink>();
			p_lstVirtualMods = new List<IVirtualModInfo>();

			if (File.Exists(p_strProfilePath))
			{
				XDocument docVirtual = XDocument.Load(p_strProfilePath);
				string strVersion = docVirtual.Element("virtualModActivator").Attribute("fileVersion").Value;
				if (!CURRENT_VERSION.ToString().Equals(strVersion))
					throw new Exception(String.Format("Invalid Virtual Mod Activator version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

				try
				{
					XElement xelModList = docVirtual.Descendants("modList").FirstOrDefault();
					if ((xelModList != null) && xelModList.HasElements)
					{
						foreach (XElement xelMod in xelModList.Elements("modInfo"))
						{
							string strModId = xelMod.Attribute("modId").Value;
							string strModName = xelMod.Attribute("modName").Value;
							string strModFileName = xelMod.Attribute("modFileName").Value;
							string strModFilePath = xelMod.Attribute("modFilePath").Value;

							VirtualModInfo vmiMod = new VirtualModInfo(strModId, strModName, strModFileName, strModFilePath);
							p_lstVirtualMods.Add(vmiMod);

							foreach (XElement xelLink in xelMod.Elements("fileLink"))
							{
								string strRealPath = xelLink.Attribute("realPath").Value;
								string strVirtualPath = xelLink.Attribute("virtualPath").Value;
								Int32 intPriority = 0;
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

								p_lstVirtualLinks.Add(new VirtualModLink(strRealPath, strVirtualPath, intPriority, booActive, vmiMod));
							}
						}

						return true;
					}
				}
				catch { }
			}

			return false;
		}

		public List<IVirtualModLink> LoadImportedList(string p_strXML)
		{
			List<IVirtualModLink> lstVirtualLinks = new List<IVirtualModLink>();
			List<IVirtualModInfo> lstVirtualMods = new List<IVirtualModInfo>();

			XDocument docVirtual = XDocument.Parse(p_strXML);
			string strVersion = docVirtual.Element("virtualModActivator").Attribute("fileVersion").Value;
			if (!CURRENT_VERSION.ToString().Equals(strVersion))
				throw new Exception(String.Format("Invalid Virtual Mod Activator version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

			try
			{
				XElement xelModList = docVirtual.Descendants("modList").FirstOrDefault();
				if ((xelModList != null) && xelModList.HasElements)
				{
					foreach (XElement xelMod in xelModList.Elements("modInfo"))
					{
						string strModId = xelMod.Attribute("modId").Value;
						string strModName = xelMod.Attribute("modName").Value;
						string strModFileName = xelMod.Attribute("modFileName").Value;
						string strModFilePath = xelMod.Attribute("modFilePath").Value;

						VirtualModInfo vmiMod = new VirtualModInfo(strModId, strModName, strModFileName, strModFilePath);
						lstVirtualMods.Add(vmiMod);

						foreach (XElement xelLink in xelMod.Elements("fileLink"))
						{
							string strRealPath = xelLink.Attribute("realPath").Value;
							string strVirtualPath = xelLink.Attribute("virtualPath").Value;
							Int32 intPriority = 0;
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

							lstVirtualLinks.Add(new VirtualModLink(strRealPath, strVirtualPath, intPriority, booActive, vmiMod));
						}
					}
				}
			}
			catch { }

			return lstVirtualLinks;
		}

		#endregion

		#region Link Management

		public string CheckVirtualLink(string p_strFilePath)
		{
			string strPath = p_strFilePath;
			IVirtualModLink ivlVirtualModLink = VirtualLinks.Find(x => Path.Combine(m_strVirtualActivatorPath, x.RealModPath) == p_strFilePath);
			if (ivlVirtualModLink != null)
			{ 
				strPath = Path.Combine(m_strGameDataPath, ivlVirtualModLink.VirtualModPath);
			}

			return strPath;
		}

		public Int32 CheckFileLink(string p_strFilePath, out IMod p_modMod, out List<IVirtualModLink> p_lstFileLinks)
		{
			return CheckFileLink(p_strFilePath, -1, out p_modMod, out p_lstFileLinks);
		}

		private Int32 CheckFileLink(string p_strFilePath, Int32 p_intCurrentPriority, out IMod p_modMod)
		{
			List<IVirtualModLink> lstDummy;
			return CheckFileLink(p_strFilePath, p_intCurrentPriority, out p_modMod, out lstDummy);
		}

		private Int32 CheckFileLink(string p_strFilePath, Int32 p_intCurrentPriority, out IMod p_modMod, out List<IVirtualModLink> p_lstFileLinks)
		{
			Int32 intPriority = -1;
			p_modMod = null;

			List<IVirtualModLink> lstVirtualModLink = new List<IVirtualModLink>();
			if (p_intCurrentPriority >= 0)
				lstVirtualModLink = VirtualLinks.Where(x => (x.VirtualModPath.ToLowerInvariant() == p_strFilePath.ToLowerInvariant()) && (x.Priority != p_intCurrentPriority)).ToList();
			else
				lstVirtualModLink = VirtualLinks.Where(x => x.VirtualModPath.ToLowerInvariant() == p_strFilePath.ToLowerInvariant()).ToList();

			if ((lstVirtualModLink != null) && (lstVirtualModLink.Count > 0))
			{
				IVirtualModLink ivlModLink = lstVirtualModLink.OrderByDescending(x => x.Priority).FirstOrDefault();
				if (ivlModLink != null)
					intPriority = ivlModLink.Priority;
				ivlModLink = lstVirtualModLink.OrderBy(x => x.Priority).FirstOrDefault();
				if (ivlModLink != null)
					p_modMod = ModManager.ManagedMods.FirstOrDefault(x => String.Equals(Path.GetFileName(x.Filename), ivlModLink.ModInfo.ModFileName, StringComparison.InvariantCultureIgnoreCase));
			}
			else if (File.Exists(p_strFilePath) && (p_intCurrentPriority >= 0))
				intPriority = 0;

			p_lstFileLinks = lstVirtualModLink;

			return intPriority;
		}
		
		public bool PurgeLinks()
		{
			if (m_tslVirtualModInfo.Count > 0)
			{
				foreach (IVirtualModInfo modInfo in m_tslVirtualModInfo)
				{
					IMod modMod = ModManager.ManagedMods.FirstOrDefault(x => String.Equals(Path.GetFileName(x.Filename), modInfo.ModFileName, StringComparison.InvariantCultureIgnoreCase));
					DisableMod(modMod, true);
				}

				VirtualLinks.Clear();
				SaveList();
			}
			return true;
		}

		public void AddInactiveLink(IMod p_modMod, string p_strBaseFilePath, Int32 p_intPriority)
		{
			IVirtualModInfo modInfo = m_tslVirtualModInfo.Where(x => String.Equals(x.ModFileName, Path.GetFileName(p_modMod.Filename), StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
			if (modInfo == null)
			{
				VirtualModInfo vmiModInfo = new VirtualModInfo(p_modMod.Id, p_modMod.ModName, p_modMod.Filename);
				m_tslVirtualModInfo.Add(vmiModInfo);
				modInfo = vmiModInfo;
			}
			string strRealFilePath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.Filename), p_strBaseFilePath);
			m_tslVirtualModList.Add(new VirtualModLink(strRealFilePath, p_strBaseFilePath, p_intPriority, false, modInfo));
		}

		public string AddFileLink(IMod p_modMod, string p_strBaseFilePath, bool p_booIsSwitching, bool p_booIsRestoring, Int32 p_intPriority)
		{
			string strRealFilePath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.Filename), p_strBaseFilePath);
			string strAdjustedFilePath = GameMode.GetModFormatAdjustedPath(p_modMod.Format, p_strBaseFilePath, false);
			string strVirtualFileLink = String.Empty;

			if (GameMode.HasSecondaryInstallPath && GameMode.CheckSecondaryInstall(p_modMod))
				strVirtualFileLink = Path.Combine(GameMode.SecondaryInstallationPath, strAdjustedFilePath);
			else
				strVirtualFileLink = Path.Combine(m_strGameDataPath, p_strBaseFilePath);

			string strActivatorFilePath = Path.Combine(m_strVirtualActivatorPath, strRealFilePath);

			if (!Directory.Exists(Path.GetDirectoryName(strVirtualFileLink)))
				FileUtil.CreateDirectory(Path.GetDirectoryName(strVirtualFileLink));

			string strFileType = Path.GetExtension(strVirtualFileLink);
			if (!strFileType.StartsWith("."))
				strFileType = "." + strFileType;

			if (File.Exists(strVirtualFileLink))
				FileUtil.ForceDelete(strVirtualFileLink);

			IVirtualModInfo modInfo = m_tslVirtualModInfo.Where(x => String.Equals(x.ModFileName, Path.GetFileName(p_modMod.Filename), StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
			if (modInfo == null)
			{
				VirtualModInfo vmiModInfo = new VirtualModInfo(p_modMod.Id, p_modMod.ModName, p_modMod.Filename);
				m_tslVirtualModInfo.Add(vmiModInfo);
				modInfo = vmiModInfo;
			}

			if (strFileType.Equals(".esp", StringComparison.InvariantCultureIgnoreCase) || strFileType.Equals(".esm", StringComparison.InvariantCultureIgnoreCase) || strFileType.Equals(".exe", StringComparison.InvariantCultureIgnoreCase))
			{
				if (CreateHardLink(strVirtualFileLink, strActivatorFilePath, IntPtr.Zero))
					if (!p_booIsRestoring)
						m_tslVirtualModList.Add(new VirtualModLink(strRealFilePath, p_strBaseFilePath, p_intPriority, true, modInfo));
					else
						strVirtualFileLink = String.Empty;
			}
			else if (!DisableLinkCreation)
			{
				if (ForceHardLinks && (CreateHardLink(strVirtualFileLink, strActivatorFilePath, IntPtr.Zero)))
				{
					if (!p_booIsRestoring)
						m_tslVirtualModList.Add(new VirtualModLink(strRealFilePath, p_strBaseFilePath, p_intPriority, true, modInfo));
					else
						strVirtualFileLink = String.Empty;
				}
				else if (CreateSymbolicLink(strVirtualFileLink, strActivatorFilePath, 0))
				{
					if (!p_booIsRestoring)
						m_tslVirtualModList.Add(new VirtualModLink(strRealFilePath, p_strBaseFilePath, p_intPriority, true, modInfo));
					else
						strVirtualFileLink = String.Empty;
				}
			}
			else
				strVirtualFileLink = String.Empty;

			if (!p_booIsSwitching && (PluginManager != null) && !String.IsNullOrEmpty(strVirtualFileLink) && !p_booIsRestoring)
				if (PluginManager.IsActivatiblePluginFile(strVirtualFileLink))
					PluginManager.AddPlugin(strVirtualFileLink);

			return strVirtualFileLink;
		}

		public void RemoveFileLink(string p_strFilePath, IMod p_modMod)
		{
			string strPathCheck = p_strFilePath.Replace(m_strVirtualActivatorPath + Path.DirectorySeparatorChar.ToString(), String.Empty);
			IVirtualModLink ivlVirtualModLink = VirtualLinks.Find(x => x.VirtualModPath == strPathCheck);
			if (ivlVirtualModLink == null)
				ivlVirtualModLink = VirtualLinks.Find(x => x.RealModPath == strPathCheck);
			RemoveFileLink(ivlVirtualModLink, p_modMod);
		}

		public void RemoveFileLink(IVirtualModLink p_ivlVirtualLink, IMod p_modMod)
		{
			IMod modCheck = null;
			
			if (p_ivlVirtualLink != null)
			{
				Int32 intPriority = CheckFileLink(p_ivlVirtualLink.VirtualModPath, p_ivlVirtualLink.Priority, out modCheck);

				if ((PluginManager != null) && (intPriority < 0))
				{
					string strLinkPath = Path.Combine(m_strGameDataPath, p_ivlVirtualLink.VirtualModPath);
					if (PluginManager.IsActivatiblePluginFile(strLinkPath))
					{
						PluginManager.DeactivatePlugin(strLinkPath);
						PluginManager.RemovePlugin(strLinkPath);
					}
				}

				string strPath = string.Empty;
				string strStop = m_strGameDataPath;
				if ((p_modMod != null) && (GameMode.HasSecondaryInstallPath && GameMode.CheckSecondaryInstall(p_modMod)))
				{
					strPath = Path.Combine(GameMode.SecondaryInstallationPath, GameMode.GetModFormatAdjustedPath(p_modMod.Format, p_ivlVirtualLink.VirtualModPath, false));
					strStop = GameMode.SecondaryInstallationPath;
				}
				else
					strPath = Path.Combine(m_strGameDataPath, p_ivlVirtualLink.VirtualModPath);

				if (File.Exists(strPath))
					FileUtil.ForceDelete(strPath);
				VirtualLinks.Remove(p_ivlVirtualLink);

				if (intPriority >= 0)
					AddFileLink(modCheck, p_ivlVirtualLink.VirtualModPath, false, true, p_ivlVirtualLink.Priority);

				TrimEmptyDirectories(Path.GetDirectoryName(strPath), strStop);
			}
		}

		public void UpdateLinkPriority(List<IVirtualModLink> lstFileLinks)
		{
			m_tslVirtualModList.RemoveRange(lstFileLinks);

			foreach (VirtualModLink vml in lstFileLinks)
			{
				vml.Priority++;
				vml.Active = false;
				m_tslVirtualModList.Add(vml);
			}
		}

		#endregion

		#region Mod Management

		public void DisableMod(IMod p_modMod)
		{
			DisableMod(p_modMod, false);
		}

		private void DisableMod(IMod p_modMod, bool p_booPurging)
		{
			List<IVirtualModLink> ivlLinks = m_tslVirtualModList.Where(x => String.Equals(x.ModInfo.ModFileName , Path.Combine(p_modMod.Filename), StringComparison.InvariantCultureIgnoreCase)).ToList();
			if (ivlLinks.Count > 0)
			{
				foreach (IVirtualModLink Link in ivlLinks)
					RemoveFileLink(Link, p_modMod);

				TxFileManager tfmFileManager = new TxFileManager();
				IIniInstaller iniIniInstaller = new IniInstaller(p_modMod, ModInstallLog, null, tfmFileManager, null);
				IList<IniEdit> lstIniEdits = ModInstallLog.GetInstalledIniEdits(p_modMod);
				foreach (IniEdit iniEdit in lstIniEdits)
					iniIniInstaller.UneditIni(iniEdit.File, iniEdit.Section, iniEdit.Key);

				RemoveIniEdits(p_modMod);

				m_tslVirtualModInfo.RemoveAll(x => String.Equals(x.ModFileName, Path.GetFileName(p_modMod.Filename), StringComparison.InvariantCultureIgnoreCase));

				if (!p_booPurging)
					SaveList();
			}
			if (!p_booPurging)
				if (this.ModActivationChanged != null)
					this.ModActivationChanged(this, new EventArgs());
		}

		public void EnableMod(IMod p_modMod)
		{
			string strModFolderPath = Path.Combine(m_strVirtualActivatorPath, Path.GetFileNameWithoutExtension(p_modMod.Filename));
			m_booDisableIniLogging = true;

			if (Directory.Exists(strModFolderPath))
			{
				string[] strFiles = Directory.GetFiles(strModFolderPath, "*", SearchOption.AllDirectories);

				foreach (string File in strFiles)
				{
					string strFile = File.Replace((strModFolderPath + Path.DirectorySeparatorChar), String.Empty);
					IModLinkInstaller ModLinkInstaller = GetModLinkInstaller();

					string strFileLink = ModLinkInstaller.AddFileLink(p_modMod, strFile, false);
					
					if (!string.IsNullOrEmpty(strFileLink))
						if (PluginManager != null)
							if (PluginManager.IsActivatiblePluginFile(strFileLink))
							{
								PluginManager.AddPlugin(strFileLink);
								PluginManager.ActivatePlugin(strFileLink);
							}
				}
				LoadIniEdits(p_modMod);
				SaveList();

				if (this.ModActivationChanged != null)
					this.ModActivationChanged(p_modMod, new EventArgs());
			}
			m_booDisableIniLogging = false;
		}

		public void LogIniEdits(IMod p_modMod, string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue)
		{
			XDocument docIniEdits;

			if (!File.Exists(m_strVirtualActivatorIniEditsPath))
			{
				docIniEdits = new XDocument();
				XElement xelRoot = new XElement("virtualModActivator", new XAttribute("fileVersion", CURRENT_VERSION));
				docIniEdits.Add(xelRoot);
			}
			else
				docIniEdits = XDocument.Load(m_strVirtualActivatorIniEditsPath);

			XElement xelIniEdits = docIniEdits.Descendants("iniEdits").FirstOrDefault();
			xelIniEdits.Add(new XElement("iniEdit",
								new XAttribute("modFile", p_modMod.Filename),
								new XElement("iniFile",
									new XText(p_strSettingsFileName)),
								new XElement("iniSection",
									new XText(p_strSection)),
								new XElement("iniKey",
									new XText(p_strKey)),
								new XElement("iniValue",
									new XText(p_strValue))));

			docIniEdits.Save(m_strVirtualActivatorIniEditsPath);
		}

		private void LoadIniEdits(IMod p_modMod)
		{
			XDocument docIniEdits;

			if (File.Exists(m_strVirtualActivatorIniEditsPath))
			{
				docIniEdits = XDocument.Load(m_strVirtualActivatorIniEditsPath);

				string strVersion = docIniEdits.Element("virtualModActivator").Attribute("fileVersion").Value;
				if (!CURRENT_VERSION.ToString().Equals(strVersion))
					throw new Exception(String.Format("Invalid Ini Edits version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

				try
				{
					XElement xelIniEdits = docIniEdits.Descendants("iniEdits").FirstOrDefault();
					if ((xelIniEdits != null) && xelIniEdits.HasElements)
					{
						List<XElement> xelEdits = xelIniEdits.Elements("iniEdit").Where(x => x.Attribute("modFile").Value == p_modMod.Filename).ToList();

						if ((xelEdits != null) && (xelEdits.Count > 0))
						{
							TxFileManager tfmFileManager = new TxFileManager();
							IIniInstaller iniIniInstaller = new IniInstaller(p_modMod, ModInstallLog, null, tfmFileManager, null);
							
							foreach (XElement xelEdit in xelEdits)
							{
								string strIniFile = xelEdit.Attribute("iniFile").Value;
								string strIniSection = xelEdit.Element("iniSection").Value;
								string strIniKey = xelEdit.Element("iniKey").Value;
								string strIniValue = xelEdit.Element("iniValue").Value;

								iniIniInstaller.EditIni(strIniFile, strIniSection, strIniKey, strIniValue);
							}
						}
					}
				}
				catch { }
			}
		}

		private void RemoveIniEdits(IMod p_modMod)
		{
			XDocument docIniEdits;

			if (File.Exists(m_strVirtualActivatorIniEditsPath))
			{
				docIniEdits = XDocument.Load(m_strVirtualActivatorIniEditsPath);

				try
				{
					XElement xelIniEdits = docIniEdits.Descendants("iniEdits").FirstOrDefault();
					if ((xelIniEdits != null) && xelIniEdits.HasElements)
					{
						List<XElement> xelEdits = xelIniEdits.Elements("iniEdit").Where(x => x.Attribute("modFile").Value == p_modMod.Filename).ToList();

						if ((xelEdits != null) && (xelEdits.Count > 0))
							foreach (XElement xelEdit in xelEdits)
								xelEdit.Remove();
					}

					docIniEdits.Save(m_strVirtualActivatorIniEditsPath);
				}
				catch { }
			}
		}

		#endregion

		public Dictionary<string, string> CheckLinkListIntegrity(IList<IVirtualModLink> p_ivlVirtualLinks)
		{
			Dictionary<string, string> dicNotFound = new Dictionary<string,string>();

			foreach (IVirtualModLink ivlModLink in p_ivlVirtualLinks)
			{
				if (!File.Exists(Path.Combine(m_strVirtualActivatorPath, ivlModLink.RealModPath)))
				{
					if (!dicNotFound.ContainsKey(ivlModLink.ModInfo.ModFileName))
						dicNotFound.Add(ivlModLink.ModInfo.ModFileName, ivlModLink.ModInfo.ModName);
				}
			}

			return dicNotFound;
		}

		public IModLinkInstaller GetModLinkInstaller()
		{
			return new ModLinkInstaller(this);
		}

		/// <summary>
		/// Deletes any empty directories found between the start path and the end directory.
		/// </summary>
		/// <param name="p_strStartPath">The path from which to start looking for empty directories.</param>
		/// <param name="p_strStopDirectory">The directory at which to stop looking.</param>
		protected void TrimEmptyDirectories(string p_strStartPath, string p_strStopDirectory)
		{
			string strEmptyDirectory = p_strStartPath;
			while (true)
			{
				if (Directory.Exists(strEmptyDirectory) &&
					(Directory.GetFiles(strEmptyDirectory).Length + Directory.GetDirectories(strEmptyDirectory).Length == 0) &&
					!strEmptyDirectory.Equals(p_strStopDirectory, StringComparison.OrdinalIgnoreCase))
				{
					for (Int32 i = 0; i < 5 && Directory.Exists(strEmptyDirectory); i++)
						FileUtil.ForceDelete(strEmptyDirectory);
				}
				else
					break;
				strEmptyDirectory = Path.GetDirectoryName(strEmptyDirectory);
			}
		}

		#endregion
	}
}
