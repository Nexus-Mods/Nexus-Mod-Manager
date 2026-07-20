namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Linq;

    using ChinhDo.Transactions;
    using Microsoft.Win32;

    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.Games;
    using Nexus.Client.ModManagement.InstallationLog;
    using Nexus.Client.Mods;
    using Nexus.Client.PluginManagement;
    using Nexus.Client.UI;
    using Nexus.Client.Util;
    using Nexus.Client.Util.Collections;

	// Phase 1 compatibility backend for virtual deployment; low-level link manipulation remains here during migration.
	public sealed class VirtualModDisableProgress
	{
		public VirtualModDisableProgress(string message, int current, int total)
		{
			Message = message;
			Current = current;
			Total = total;
		}

		public string Message { get; private set; }
		public int Current { get; private set; }
		public int Total { get; private set; }
	}

	public class VirtualModActivator : IVirtualModActivator
	{
		[DllImport("kernel32.dll")]
		static extern bool CreateSymbolicLink(string p_strLinkName, string p_strTargetPath, int dwFlags);

		[DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
		static extern bool CreateHardLink(string p_strLinkName, string p_strTargetPath, IntPtr lpSecurityAttributes);

		public event EventHandler ModActivationChanged;
		public event EventHandler VirtualStoreMutationEnded = delegate { };

		#region Static Properties

		private static readonly Version CURRENT_VERSION = new Version("0.3.0.0");
		private static readonly string ACTIVATOR_FILE = "VirtualModConfig.xml";
		private static readonly string ACTIVATOR_INIEDITS = "IniEdits.xml";
		private static readonly string ACTIVATOR_OVERWRITE = "_overwrites";
		public static readonly string ACTIVATOR_FOLDER = "VirtualInstall";
		public static readonly string ACTIVATOR_LINK_FOLDER = "NMMLink";
		public static readonly IMod DummyMod = new InstallLog.DummyMod("ORIGINAL_VALUE", string.Format("Dummy Mod: {0}", "ORIGINAL_VALUE"));

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

			XDocument docVirtual;

			try
			{
				using (var sr = new StreamReader(p_strVirtualActivatorConfigPath))
				{
					docVirtual = XDocument.Load(sr);
				}

				XElement xelVirtual = docVirtual.Element("virtualModActivator");
				if (xelVirtual == null)
					return new Version("0.0.0.0");

				XAttribute xatVersion = xelVirtual.Attribute("fileVersion");
				if (xatVersion == null)
					return new Version("0.0.0.0");

				return new Version(xatVersion.Value);
			}
			catch
			{
				return new Version("99.99.99.99");
			}
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
		private Dictionary<string, IVirtualModInfo> m_dicVirtualModInfoByFileName = new Dictionary<string, IVirtualModInfo>();
		private bool m_booVirtualModInfoLookupDirty = true;
		private int m_intVirtualModInfoLookupRevision = 0;
		private readonly object m_objVirtualModInfoLookupLock = new object();
		private static readonly AsyncLocal<ModInfoUpdateBatch> m_alcCurrentModInfoUpdateBatch = new AsyncLocal<ModInfoUpdateBatch>();
		private VirtualLinkIndex m_vliVirtualLinkIndex = new VirtualLinkIndex();
		private bool m_booVirtualLinkIndexDirty = true;
		private int m_intVirtualLinkIndexRevision = 0;
		private readonly object m_objVirtualLinkIndexLock = new object();
		private readonly AsyncLocal<int> m_alcVirtualLinkIndexMutationNesting = new AsyncLocal<int>();
		private int m_intVirtualStoreMutationCount = 0;
		private ThreadSafeObservableList<IVirtualModInfo> m_tslVirtualModInfo = new ThreadSafeObservableList<IVirtualModInfo>();
		private bool m_booInitialized = false;
		private bool m_booDisableLinkCreation = false;
		private bool m_booDisableIniLogging = false;
		private bool m_booForceHardLinks = false;
		private string m_strGameDataPath = string.Empty;
		private string m_strVirtualActivatorPath = string.Empty;
		private string m_strVirtualActivatorConfigPath = string.Empty;
		private string m_strVirtualActivatorIniEditsPath = string.Empty;
		private string m_strVirtualActivatorOverwritePath = string.Empty;
		private static readonly object m_objLock = new object();
		private readonly IVirtualModStore m_vmsVirtualModStore;
		private readonly IVirtualInstallReconciler m_vrcVirtualInstallReconciler = new VirtualInstallReconciler();

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
		public IGameMode GameMode { get; private set; }

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

		/// <summary>
		/// Gets the current game mode's VirtualInstall folder.
		/// </summary>
		/// <value>The current game mode's VirtualInstall folder.</value>
		public string VirtualFoder
		{
			get
			{
				if (EnvironmentInfo != null)
					if (!string.IsNullOrEmpty(EnvironmentInfo.Settings.VirtualFolder[GameMode.ModeId]))
					{
						string strVirtual = EnvironmentInfo.Settings.VirtualFolder[GameMode.ModeId];
						return Path.Combine(strVirtual, ACTIVATOR_FOLDER);
					}

				return string.Empty;
			}
		}

		/// <summary>
		/// Gets the current game mode's NMMLink folder.
		/// </summary>
		/// <value>The current game mode's NMMLink folder.</value>
		public string HDLinkFolder
		{
			get
			{
				if (EnvironmentInfo != null)
					if (!string.IsNullOrWhiteSpace(EnvironmentInfo.Settings.HDLinkFolder[GameMode.ModeId]))
					{
						string strLink = EnvironmentInfo.Settings.HDLinkFolder[GameMode.ModeId];
						if (!string.IsNullOrWhiteSpace(strLink))
							return Path.Combine(strLink, GameMode.ModeId, ACTIVATOR_LINK_FOLDER);
						else
							return string.Empty;
					}

				if (MultiHDMode == true)
					throw new ArgumentNullException("It seems the MultiHD mode is enabled but the program is unable to retrieve the Link folder.");
				else
					return string.Empty;
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

		/// <summary>
		/// Gets whether the MultiHD mode is currently enabled.
		/// </summary>
		/// <value>Whether the MultiHD mode is currently enabled.</value>
		public bool MultiHDMode
		{
			get
			{
				return m_booForceHardLinks;
			}
		}

		/// <summary>
		/// Gets whether the VirtualModActivator is initialized.
		/// </summary>
		/// <value>Whether the VirtualModActivator is initialized.</value>
		public bool Initialized
		{
			get
			{
				return m_booInitialized;
			}
		}

		/// <summary>
		/// Gets whether link creation is disabled.
		/// </summary>
		/// <value>Whether link creation is disabled.</value>
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

		/// <summary>
		/// Gets whether ini logging is disabled.
		/// </summary>
		/// <value>Whether ini logging is disabled.</value>
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

		public int ModCount
		{
			get
			{
				return m_tslVirtualModInfo.Count;
			}
		}

		public bool IsVirtualStoreMutationInProgress
		{
			get
			{
				return Interlocked.CompareExchange(ref m_intVirtualStoreMutationCount, 0, 0) > 0;
			}
		}

		protected string NewVirtualFolder;
		protected string NewLinkFolder;
		protected bool NewMultiHD;

		#endregion

		#region Constructors

		public VirtualModActivator(ModManager p_mmgModManager, IPluginManager p_pmgPluginManager, IGameMode p_gmdGameMode, IInstallLog p_ilgModInstallLog, IEnvironmentInfo p_eifEnvironmentInfo, string p_strModFolder)
		{
			AttachVirtualModInfoList(m_tslVirtualModInfo);
			AttachVirtualLinkList(m_tslVirtualModList);
			ModManager = p_mmgModManager;
			PluginManager = p_pmgPluginManager;
			GameMode = p_gmdGameMode;
			ModInstallLog = p_ilgModInstallLog;
			EnvironmentInfo = p_eifEnvironmentInfo;
			m_booForceHardLinks = EnvironmentInfo.Settings.MultiHDInstall[GameMode.ModeId];
			string strVirtualFolder = EnvironmentInfo.Settings.VirtualFolder[GameMode.ModeId];
			m_strVirtualActivatorPath = Path.Combine(strVirtualFolder, ACTIVATOR_FOLDER);
			m_strGameDataPath = GameMode.UsesPlugins ? GameMode.PluginDirectory : GameMode.InstallationPath;
			m_strVirtualActivatorConfigPath = Path.Combine(m_strVirtualActivatorPath, ACTIVATOR_FILE);
			m_strVirtualActivatorIniEditsPath = Path.Combine(m_strVirtualActivatorPath, ACTIVATOR_INIEDITS);
			m_strVirtualActivatorOverwritePath = Path.Combine(m_strVirtualActivatorPath, ACTIVATOR_OVERWRITE);
			if (!Directory.Exists(m_strVirtualActivatorPath))
				Directory.CreateDirectory(m_strVirtualActivatorPath);

			if (!string.IsNullOrEmpty(EnvironmentInfo.Settings.HDLinkFolder[GameMode.ModeId]))
			{
				string strHDLink = Path.Combine(Path.Combine(EnvironmentInfo.Settings.HDLinkFolder[GameMode.ModeId], GameMode.ModeId), ACTIVATOR_LINK_FOLDER);

				if (!Directory.Exists(strHDLink))
					Directory.CreateDirectory(strHDLink);
			}

			if (!Directory.Exists(m_strVirtualActivatorOverwritePath))
				Directory.CreateDirectory(m_strVirtualActivatorOverwritePath);

			m_vmsVirtualModStore = new VirtualModStoreFactory().Create(m_strVirtualActivatorConfigPath, CURRENT_VERSION, PerformVersionCheck, GetModFileVersionForLoadList);
		}

		#endregion

		#region Virtual Mod Activator

		#region List Management

		public void Initialize()
		{
			if (IsValid(m_strVirtualActivatorConfigPath))
				if (PerformVersionCheck())
				{
					SetCurrentList(LoadList(m_strVirtualActivatorConfigPath));
					m_booInitialized = true;
				}
		}

		private bool PerformVersionCheck()
		{
			return PerformVersionCheck(m_strVirtualActivatorConfigPath);
		}

		private bool PerformVersionCheck(string p_strFilePath)
		{
			Version FileVersion = ReadVersion(p_strFilePath);

			if (FileVersion.CompareTo(CURRENT_VERSION) == 0)
				return true;
			else if ((FileVersion == new Version("0.2.0.0") && (CURRENT_VERSION == new Version("0.3.0.0"))))
				return true;
			else
				return false;
		}

		private bool PerformVersionCheck(Version p_vrsVersion)
		{
			if (p_vrsVersion.CompareTo(CURRENT_VERSION) == 0)
				return true;
			else if ((p_vrsVersion == new Version("0.2.0.0") && (CURRENT_VERSION == new Version("0.3.0.0"))))
				return true;
			else
				return false;
		}

		public void Setup()
		{
			SaveList(false);
			SetCurrentList(LoadList(m_strVirtualActivatorConfigPath));
			m_booInitialized = true;
		}

		public string RequiresFixing()
		{
			return RequiresFixing(m_strVirtualActivatorConfigPath);
		}

		public string RequiresFixing(string p_strFilePath)
		{
			if (string.IsNullOrEmpty(p_strFilePath))
				return null;

			Version FileVersion = ReadVersion(p_strFilePath);

			if (!string.Equals(p_strFilePath, m_strVirtualActivatorConfigPath, StringComparison.OrdinalIgnoreCase))
				if (!(FileVersion.CompareTo(new Version("99.99.99.99")) < 0))
				{
					SaveModList(p_strFilePath);
					return null;
				}

			if (FileVersion.CompareTo(new Version("0.3.0.0")) < 0)
				return p_strFilePath;
			else
				return null;
		}

		public void Reset()
		{
			if (!string.IsNullOrEmpty(NewVirtualFolder))
			{
				m_strVirtualActivatorPath = Path.Combine(NewVirtualFolder, ACTIVATOR_FOLDER);
				m_strVirtualActivatorConfigPath = Path.Combine(m_strVirtualActivatorPath, ACTIVATOR_FILE);
				m_strVirtualActivatorIniEditsPath = Path.Combine(m_strVirtualActivatorPath, ACTIVATOR_INIEDITS);
				if (!Directory.Exists(m_strVirtualActivatorPath))
					Directory.CreateDirectory(m_strVirtualActivatorPath);
			}
			
			if (!string.IsNullOrEmpty(NewLinkFolder))
			{
				if (!string.IsNullOrEmpty(HDLinkFolder))
					FileUtil.ForceDelete(HDLinkFolder);

				string strHDLink = Path.Combine(NewLinkFolder, GameMode.ModeId, ACTIVATOR_LINK_FOLDER);

				if (!Directory.Exists(strHDLink))
					Directory.CreateDirectory(strHDLink);
			}
			else if (string.IsNullOrEmpty(NewLinkFolder) && !string.IsNullOrEmpty(HDLinkFolder))
			{
				if (Directory.Exists(HDLinkFolder))
					FileUtil.ForceDelete(HDLinkFolder);
			}

			m_booForceHardLinks = NewMultiHD;

			if (!string.IsNullOrWhiteSpace(NewVirtualFolder))
			{
				if (!string.Equals(EnvironmentInfo.Settings.VirtualFolder[GameMode.ModeId], NewVirtualFolder))
				{
					EnvironmentInfo.Settings.VirtualFolder[GameMode.ModeId] = NewVirtualFolder;
				}
			}
			if (!string.Equals(EnvironmentInfo.Settings.HDLinkFolder[GameMode.ModeId], NewLinkFolder))
			{
				EnvironmentInfo.Settings.HDLinkFolder[GameMode.ModeId] = NewLinkFolder;
			}
			if (NewMultiHD == !EnvironmentInfo.Settings.MultiHDInstall[GameMode.ModeId])
			{
				EnvironmentInfo.Settings.MultiHDInstall[GameMode.ModeId] = NewMultiHD;
			}

			SaveRegistry(GameMode.ModeId, NewVirtualFolder, NewLinkFolder, NewMultiHD);

			EnvironmentInfo.Settings.Save();
		}

		/// <summary>
		/// Save the mod list into the XML file.
		/// </summary>
		public bool SaveList()
		{
			return SaveList(false);
		}

		/// <summary>
		/// Save the mod list into the XML file.
		/// </summary>
		public bool SaveList(bool p_booModActivationChange)
		{
			if (!Directory.Exists(m_strVirtualActivatorPath))
				Directory.CreateDirectory(m_strVirtualActivatorPath);

			return SaveModList(p_booModActivationChange);
		}

		/// <summary>
		/// Save the mod list into the XML file.
		/// </summary>
		private bool SaveModList(bool p_booModActivationChange)
		{
			bool writtenTo = false;
			List<IVirtualModInfo> lstVirtualModInfo = new List<IVirtualModInfo>();

			foreach (VirtualModInfo mod in m_tslVirtualModInfo)
			{
				if (string.IsNullOrEmpty(mod.DownloadId))
				{
					IMod modMod = ModManager.ActiveMods.FirstOrDefault(x => x.Filename.Equals(mod.ModFileFullPath, StringComparison.OrdinalIgnoreCase));
					if (modMod != null)
						mod.DownloadId = modMod.DownloadId;

				}

				lstVirtualModInfo.Add(mod);
			}

			if (m_vmsVirtualModStore.IsReadyForWrite(m_strVirtualActivatorConfigPath))
			{
				using (BeginVirtualStoreMutation())
				{
					m_vmsVirtualModStore.Save(CURRENT_VERSION, m_strVirtualActivatorConfigPath, lstVirtualModInfo, m_tslVirtualModList);
					writtenTo = true;
				}
			}

			if (p_booModActivationChange)
				ModActivationChanged(null, new EventArgs());

			return writtenTo;
		}

		/// <summary>
		/// Save the mod list into the XML file.
		/// </summary>
		/// <param name="p_strPath">The Virtual Activator path.</param>
		public void SaveModList(string p_strPath)
		{
			if (!Directory.Exists(Path.GetDirectoryName(p_strPath)))
				Directory.CreateDirectory(Path.GetDirectoryName(p_strPath));
			m_vmsVirtualModStore.Copy(m_strVirtualActivatorConfigPath, p_strPath);
		}

		/// <summary>
		/// Save the mod list into the XML file.
		/// </summary>
		/// <param name="p_strPath">The Virtual Activator path.</param>
		/// <param name="p_lstVirtualModInfo">The IVirtualModInfo object list.</param>
		/// <param name="p_lstVirtualModLink">The IVirtualModLink object list.</param>
		public void SaveModList(string p_strPath, List<IVirtualModInfo> p_lstVirtualModInfo, List<IVirtualModLink> p_lstVirtualModLink)
		{
			if (string.Equals(Path.GetFullPath(p_strPath), m_strVirtualActivatorConfigPath, StringComparison.OrdinalIgnoreCase))
			{
				using (BeginVirtualStoreMutation())
				{
					m_vmsVirtualModStore.SaveWithModInfoMatching(CURRENT_VERSION, p_strPath, p_lstVirtualModInfo, p_lstVirtualModLink);
				}
				return;
			}

			m_vmsVirtualModStore.SaveWithModInfoMatching(CURRENT_VERSION, p_strPath, p_lstVirtualModInfo, p_lstVirtualModLink);
		}

		public void UpdateDownloadId(string p_strCurrentProfilePath, Dictionary<string, string> p_dctNewDownloadID)
		{

			bool booEdited = false;

			foreach (KeyValuePair<string, string> kvp in p_dctNewDownloadID)
			{
				IVirtualModInfo ModInfo = null;

				try
				{
					ModInfo = m_tslVirtualModInfo.Where(x => (x != null) && (!string.IsNullOrEmpty(x.ModFileName))).Where(x => x.ModFileName.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
				}
				catch { }

				if (ModInfo != null)
				{
					VirtualModInfo vmiModInfo = new VirtualModInfo(ModInfo);
					if (string.IsNullOrWhiteSpace(vmiModInfo.DownloadId))
						vmiModInfo.DownloadId = kvp.Value;
					else
						vmiModInfo.UpdatedDownloadId = kvp.Value;

					m_tslVirtualModInfo.Remove(ModInfo);
					m_tslVirtualModInfo.Add(vmiModInfo);

					List<IVirtualModLink> lstModLink = new List<IVirtualModLink>(m_tslVirtualModList.Where(x => (x != null) && (x.ModInfo == ModInfo)));

					foreach (IVirtualModLink ModLink in lstModLink)
					{
						VirtualModLink UpdateLink = new VirtualModLink(ModLink);
						UpdateLink.ModInfo = vmiModInfo;
						RemoveVirtualLink(ModLink);
						AddVirtualLink(UpdateLink);
					}

					if (!booEdited)
						booEdited = true;
				}
			}

			if (booEdited)
			{
				if (SaveList(false))
					if (!string.IsNullOrWhiteSpace(p_strCurrentProfilePath))
						SaveModList(p_strCurrentProfilePath);		
			}
		}

		private VirtualStoreMutationScope BeginVirtualStoreMutation()
		{
			Interlocked.Increment(ref m_intVirtualStoreMutationCount);
			return new VirtualStoreMutationScope(this);
		}

		private void EndVirtualStoreMutation()
		{
			if (Interlocked.Decrement(ref m_intVirtualStoreMutationCount) != 0)
				return;

			try
			{
				VirtualStoreMutationEnded(this, EventArgs.Empty);
			}
			catch (Exception e)
			{
				Trace.TraceWarning("Could not notify virtual store mutation completion: {0}", e.Message);
			}
		}

		private sealed class VirtualStoreMutationScope : IDisposable
		{
			private readonly VirtualModActivator m_vmaVirtualModActivator;
			private bool m_booDisposed;

			public VirtualStoreMutationScope(VirtualModActivator p_vmaVirtualModActivator)
			{
				m_vmaVirtualModActivator = p_vmaVirtualModActivator;
			}

			public void Dispose()
			{
				if (m_booDisposed)
					return;

				m_booDisposed = true;
				m_vmaVirtualModActivator.EndVirtualStoreMutation();
			}
		}

		public IDisposable BeginModInfoUpdateBatch()
		{
			ModInfoUpdateBatch mubPreviousBatch = m_alcCurrentModInfoUpdateBatch.Value;
			ModInfoUpdateBatch mubBatch = new ModInfoUpdateBatch(this, mubPreviousBatch);
			m_alcCurrentModInfoUpdateBatch.Value = mubBatch;
			return mubBatch;
		}

		public IDisposable BeginVirtualLinkUpdateBatch(int p_intExpectedAdditionalLinks)
		{
			EnsureVirtualLinkIndex();
			int intExpectedTotal = m_tslVirtualModList.Count + Math.Max(0, p_intExpectedAdditionalLinks);
			m_tslVirtualModList.EnsureCapacity(intExpectedTotal);
			if (p_intExpectedAdditionalLinks >= 256)
			{
				lock (m_objVirtualLinkIndexLock)
				{
					if (!m_booVirtualLinkIndexDirty)
						m_vliVirtualLinkIndex.EnsureCapacity(intExpectedTotal);
				}
			}

			m_alcVirtualLinkIndexMutationNesting.Value++;
			try
			{
				return new VirtualLinkUpdateBatch(this, m_tslVirtualModList.BeginUpdate());
			}
			catch
			{
				m_alcVirtualLinkIndexMutationNesting.Value--;
				throw;
			}
		}

		private sealed class VirtualLinkUpdateBatch : IDisposable
		{
			private VirtualModActivator m_vmaOwner;
			private IDisposable m_dspListUpdate;

			public VirtualLinkUpdateBatch(VirtualModActivator p_vmaOwner, IDisposable p_dspListUpdate)
			{
				m_vmaOwner = p_vmaOwner;
				m_dspListUpdate = p_dspListUpdate;
			}

			public void Dispose()
			{
				VirtualModActivator owner = m_vmaOwner;
				if (owner == null)
					return;

				m_vmaOwner = null;
				try
				{
					if (m_dspListUpdate != null)
						m_dspListUpdate.Dispose();
				}
				finally
				{
					m_dspListUpdate = null;
					owner.m_alcVirtualLinkIndexMutationNesting.Value--;
				}
			}
		}

		private void UpdateModInfoNowOrDefer(IMod p_modMod)
		{
			if (p_modMod == null)
				return;

			p_modMod.PlaceInModLoadOrder = p_modMod.NewPlaceInModLoadOrder; // just adding it in, shhhh

			ModInfoUpdateBatch mubBatch = m_alcCurrentModInfoUpdateBatch.Value;
			if (mubBatch != null && mubBatch.BelongsTo(this))
			{
				mubBatch.Defer(p_modMod);
				return;
			}

			UpdateModInfoNow(p_modMod);
		}

		private void UpdateModInfoNow(IMod p_modMod)
		{
			p_modMod.UpdateInfo(p_modMod, true);
		}

		internal sealed class ModInfoUpdateBatch : IDisposable
		{
			private readonly VirtualModActivator m_vmaVirtualModActivator;
			private readonly ModInfoUpdateBatch m_mubPreviousBatch;
			private readonly List<IMod> m_lstDeferredModInfoUpdates = new List<IMod>();
			private bool m_booDisposed;

			public ModInfoUpdateBatch(VirtualModActivator p_vmaVirtualModActivator, ModInfoUpdateBatch p_mubPreviousBatch)
			{
				m_vmaVirtualModActivator = p_vmaVirtualModActivator;
				m_mubPreviousBatch = p_mubPreviousBatch;
			}

			public bool BelongsTo(VirtualModActivator p_vmaVirtualModActivator)
			{
				return ReferenceEquals(m_vmaVirtualModActivator, p_vmaVirtualModActivator);
			}

			public void Defer(IMod p_modMod)
			{
				if (!ContainsDeferredModInfoUpdate(p_modMod))
					m_lstDeferredModInfoUpdates.Add(p_modMod);
			}

			public void Flush()
			{
				foreach (IMod mod in m_lstDeferredModInfoUpdates)
					m_vmaVirtualModActivator.UpdateModInfoNow(mod);

				m_lstDeferredModInfoUpdates.Clear();
			}

			public void Dispose()
			{
				if (m_booDisposed)
					return;

				m_booDisposed = true;
				if (ReferenceEquals(m_alcCurrentModInfoUpdateBatch.Value, this))
					m_alcCurrentModInfoUpdateBatch.Value = m_mubPreviousBatch;

				if (m_mubPreviousBatch != null && m_mubPreviousBatch.BelongsTo(m_vmaVirtualModActivator))
				{
					foreach (IMod mod in m_lstDeferredModInfoUpdates)
						m_mubPreviousBatch.Defer(mod);

					m_lstDeferredModInfoUpdates.Clear();
				}
				else
				{
					Flush();
				}
			}

			private bool ContainsDeferredModInfoUpdate(IMod p_modMod)
			{
				foreach (IMod mod in m_lstDeferredModInfoUpdates)
				{
					if (ReferenceEquals(mod, p_modMod))
						return true;
				}

				return false;
			}
		}

		private void AttachVirtualModInfoList(ThreadSafeObservableList<IVirtualModInfo> p_tslVirtualModInfo)
		{
			if (p_tslVirtualModInfo != null)
				p_tslVirtualModInfo.CollectionChanged += VirtualModInfoListChanged;

			MarkVirtualModInfoLookupDirty();
		}

		private void DetachVirtualModInfoList(ThreadSafeObservableList<IVirtualModInfo> p_tslVirtualModInfo)
		{
			if (p_tslVirtualModInfo != null)
				p_tslVirtualModInfo.CollectionChanged -= VirtualModInfoListChanged;
		}

		private void VirtualModInfoListChanged(object p_objSender, NotifyCollectionChangedEventArgs p_nccEventArgs)
		{
			MarkVirtualModInfoLookupDirty();
		}

		private void MarkVirtualModInfoLookupDirty()
		{
			lock (m_objVirtualModInfoLookupLock)
			{
				m_booVirtualModInfoLookupDirty = true;
				m_intVirtualModInfoLookupRevision++;
			}
		}

		private void RebuildVirtualModInfoLookup()
		{
			while (true)
			{
				int intRevision;
				lock (m_objVirtualModInfoLookupLock)
				{
					if (!m_booVirtualModInfoLookupDirty)
						return;

					intRevision = m_intVirtualModInfoLookupRevision;
				}

				List<IVirtualModInfo> lstVirtualModInfo = new List<IVirtualModInfo>(m_tslVirtualModInfo);
				Dictionary<string, IVirtualModInfo> dicVirtualModInfoByFileName = new Dictionary<string, IVirtualModInfo>();

				foreach (IVirtualModInfo modInfo in lstVirtualModInfo)
				{
					string strModFileName = GetVirtualModInfoLookupKey(modInfo.ModFileName);
					if (!dicVirtualModInfoByFileName.ContainsKey(strModFileName))
						dicVirtualModInfoByFileName.Add(strModFileName, modInfo);
				}

				lock (m_objVirtualModInfoLookupLock)
				{
					if (intRevision != m_intVirtualModInfoLookupRevision)
						continue;

					m_dicVirtualModInfoByFileName = dicVirtualModInfoByFileName;
					m_booVirtualModInfoLookupDirty = false;
					return;
				}
			}
		}

		private void EnsureVirtualModInfoLookup()
		{
			bool booDirty;
			lock (m_objVirtualModInfoLookupLock)
			{
				booDirty = m_booVirtualModInfoLookupDirty;
			}

			if (!booDirty)
				return;

			RebuildVirtualModInfoLookup();
		}

		private IVirtualModInfo FindVirtualModInfoByFileName(string p_strModFileName)
		{
			string strModFileName = GetVirtualModInfoLookupKey(p_strModFileName);

			while (true)
			{
				EnsureVirtualModInfoLookup();
				lock (m_objVirtualModInfoLookupLock)
				{
					if (m_booVirtualModInfoLookupDirty)
						continue;

					IVirtualModInfo modInfo;
					if (m_dicVirtualModInfoByFileName.TryGetValue(strModFileName, out modInfo))
						return modInfo;
				}

				return null;
			}
		}

		private static string GetVirtualModInfoLookupKey(string p_strModFileName)
		{
			return p_strModFileName.ToLowerInvariant();
		}

		private void AddVirtualModInfo(IVirtualModInfo p_vmiModInfo)
		{
			MarkVirtualModInfoLookupDirty();
			m_tslVirtualModInfo.Add(p_vmiModInfo);
			MarkVirtualModInfoLookupDirty();
		}

		private void AttachVirtualLinkList(ThreadSafeObservableList<IVirtualModLink> p_tslVirtualLinks)
		{
			if (p_tslVirtualLinks != null)
				p_tslVirtualLinks.CollectionChanged += VirtualLinkListChanged;

			MarkVirtualLinkIndexDirty();
		}

		private void DetachVirtualLinkList(ThreadSafeObservableList<IVirtualModLink> p_tslVirtualLinks)
		{
			if (p_tslVirtualLinks != null)
				p_tslVirtualLinks.CollectionChanged -= VirtualLinkListChanged;
		}

		private void VirtualLinkListChanged(object p_objSender, NotifyCollectionChangedEventArgs p_nccEventArgs)
		{
			if (m_alcVirtualLinkIndexMutationNesting.Value > 0)
				return;

			MarkVirtualLinkIndexDirty();
		}

		private void MarkVirtualLinkIndexDirty()
		{
			lock (m_objVirtualLinkIndexLock)
			{
				m_booVirtualLinkIndexDirty = true;
				m_intVirtualLinkIndexRevision++;
			}
		}

		private void RebuildVirtualLinkIndex()
		{
			while (true)
			{
				int intRevision;
				lock (m_objVirtualLinkIndexLock)
				{
					if (!m_booVirtualLinkIndexDirty)
						return;

					intRevision = m_intVirtualLinkIndexRevision;
				}

				List<IVirtualModLink> lstVirtualLinks = new List<IVirtualModLink>(m_tslVirtualModList);
				Dictionary<IVirtualModInfo, IMod> dicManagedModsByModInfo = BuildManagedModLookupForVirtualLinks(lstVirtualLinks);
				VirtualLinkIndex vliVirtualLinkIndex = new VirtualLinkIndex(lstVirtualLinks.Count);
				vliVirtualLinkIndex.Rebuild(lstVirtualLinks, x => GetVirtualLinkDeploymentPathKeys(x, dicManagedModsByModInfo));

				lock (m_objVirtualLinkIndexLock)
				{
					if (intRevision != m_intVirtualLinkIndexRevision)
						continue;

					m_vliVirtualLinkIndex = vliVirtualLinkIndex;
					m_booVirtualLinkIndexDirty = false;
					return;
				}
			}
		}

		private void EnsureVirtualLinkIndex()
		{
			bool booDirty;
			lock (m_objVirtualLinkIndexLock)
			{
				booDirty = m_booVirtualLinkIndexDirty;
			}

			if (!booDirty)
				return;

			RebuildVirtualLinkIndex();
		}

		private void CollectIndexedFileLinkMatches(
			string p_strVirtualPath,
			ModInstallRoot p_mirInstallRoot,
			int p_intCurrentPriority,
			ref List<IVirtualModLink> p_lstMatches,
			ref int p_intHighestPriority,
			ref IVirtualModLink p_vmlLowestPriorityLink)
		{
			string strTargetPathKey = GetDeploymentPathKey(p_strVirtualPath, p_mirInstallRoot);
			EnsureVirtualLinkIndex();

			lock (m_objVirtualLinkIndexLock)
			{
				CollectFileLinkMatches(
					m_vliVirtualLinkIndex.FindByVirtualPath(p_strVirtualPath),
					p_mirInstallRoot,
					true,
					p_intCurrentPriority,
					ref p_lstMatches,
					ref p_intHighestPriority,
					ref p_vmlLowestPriorityLink);

				if (!string.IsNullOrEmpty(strTargetPathKey))
				{
					CollectFileLinkMatches(
						m_vliVirtualLinkIndex.FindByDeploymentPath(strTargetPathKey),
						p_mirInstallRoot,
						false,
						p_intCurrentPriority,
						ref p_lstMatches,
						ref p_intHighestPriority,
						ref p_vmlLowestPriorityLink);
				}
			}
		}

		private IEnumerable<string> GetVirtualLinkDeploymentPathKeys(IVirtualModLink p_vmlLink, IDictionary<IVirtualModInfo, IMod> p_dicManagedModsByModInfo)
		{
			IMod modMod = null;
			if (p_vmlLink != null && p_vmlLink.ModInfo != null && p_dicManagedModsByModInfo != null)
				p_dicManagedModsByModInfo.TryGetValue(p_vmlLink.ModInfo, out modMod);

			foreach (string strDeploymentPathKey in GetVirtualLinkDeploymentPathKeys(p_vmlLink, modMod))
				yield return strDeploymentPathKey;
		}

		private IEnumerable<string> GetVirtualLinkDeploymentPathKeys(IVirtualModLink p_vmlLink, IMod p_modMod)
		{
			string strRawPathKey;
			string strAdjustedPathKey;
			GetVirtualLinkDeploymentPathKeys(p_vmlLink, p_modMod, out strRawPathKey, out strAdjustedPathKey);

			if (!string.IsNullOrEmpty(strRawPathKey))
				yield return strRawPathKey;
			if (!string.IsNullOrEmpty(strAdjustedPathKey))
				yield return strAdjustedPathKey;
		}

		private void GetVirtualLinkDeploymentPathKeys(IVirtualModLink p_vmlLink, IMod p_modMod, out string p_strRawPathKey, out string p_strAdjustedPathKey)
		{
			p_strRawPathKey = null;
			p_strAdjustedPathKey = null;
			if (p_vmlLink == null)
				return;

			p_strRawPathKey = GetDeploymentPathKey(p_vmlLink.VirtualModPath, p_vmlLink.InstallRoot);
			if (p_vmlLink.InstallRoot == ModInstallRoot.GameRoot || p_modMod == null)
				return;

			string strAdjustedPathKey = GetDeploymentPathKey(GetAdjustedVirtualPath(p_modMod, p_vmlLink.VirtualModPath, p_vmlLink.InstallRoot), p_vmlLink.InstallRoot);
			if (!string.IsNullOrEmpty(strAdjustedPathKey) && !strAdjustedPathKey.Equals(p_strRawPathKey, StringComparison.OrdinalIgnoreCase))
				p_strAdjustedPathKey = strAdjustedPathKey;
		}

		private Dictionary<IVirtualModInfo, IMod> BuildManagedModLookupForVirtualLinks(IEnumerable<IVirtualModLink> p_enmVirtualLinks)
		{
			Dictionary<IVirtualModInfo, IMod> dicManagedModsByModInfo = new Dictionary<IVirtualModInfo, IMod>(ReferenceEqualityComparer<IVirtualModInfo>.Instance);
			if (p_enmVirtualLinks == null)
				return dicManagedModsByModInfo;

			foreach (IVirtualModLink vmlLink in p_enmVirtualLinks)
			{
				if (vmlLink == null || vmlLink.ModInfo == null || dicManagedModsByModInfo.ContainsKey(vmlLink.ModInfo))
					continue;

				dicManagedModsByModInfo.Add(vmlLink.ModInfo, FindManagedMod(vmlLink.ModInfo));
			}

			return dicManagedModsByModInfo;
		}

		private string GetDeploymentPathKey(string p_strVirtualPath, ModInstallRoot p_mirInstallRoot)
		{
			if (string.IsNullOrWhiteSpace(p_strVirtualPath))
				return null;

			try
			{
				return Path.GetFullPath(Path.Combine(GetInstallRootPath(p_mirInstallRoot), p_strVirtualPath)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			}
			catch
			{
				return null;
			}
		}

		internal string GetDeployedFilePath(IVirtualModLink p_vmlLink)
		{
			return GetDeployedFilePath(p_vmlLink, p_vmlLink == null ? null : FindManagedMod(p_vmlLink.ModInfo));
		}

		internal Dictionary<IVirtualModLink, string> GetDeployedFilePaths(IEnumerable<IVirtualModLink> p_enmLinks)
		{
			List<IVirtualModLink> lstLinks = p_enmLinks == null
				? new List<IVirtualModLink>()
				: p_enmLinks.Where(x => x != null).ToList();
			Dictionary<IVirtualModInfo, IMod> dicManagedMods = BuildManagedModLookupForVirtualLinks(lstLinks);
			Dictionary<IVirtualModLink, string> dicPaths = new Dictionary<IVirtualModLink, string>(lstLinks.Count);

			foreach (IVirtualModLink vmlLink in lstLinks)
			{
				IMod modMod = null;
				if (vmlLink.ModInfo != null)
					dicManagedMods.TryGetValue(vmlLink.ModInfo, out modMod);
				dicPaths[vmlLink] = GetDeployedFilePath(vmlLink, modMod);
			}

			return dicPaths;
		}

		private string GetDeployedFilePath(IVirtualModLink p_vmlLink, IMod p_modMod)
		{
			if (p_vmlLink == null || string.IsNullOrWhiteSpace(p_vmlLink.VirtualModPath))
				return string.Empty;

			try
			{
				string strAdjustedPath = p_vmlLink.VirtualModPath;

				if (p_vmlLink.InstallRoot != ModInstallRoot.GameRoot)
				{
					if (p_modMod != null)
						strAdjustedPath = GetAdjustedVirtualPath(p_modMod, p_vmlLink.VirtualModPath, p_vmlLink.InstallRoot);
					else
						strAdjustedPath = GameMode.GetModFormatAdjustedPath(null, p_vmlLink.VirtualModPath, true);
				}

				if (string.IsNullOrWhiteSpace(strAdjustedPath))
					return string.Empty;

				string strInstallRoot = GetInstallRootPath(p_vmlLink.InstallRoot);
				if (p_vmlLink.InstallRoot == ModInstallRoot.Default &&
					p_modMod != null &&
					GameMode.HasSecondaryInstallPath &&
					GameMode.CheckSecondaryInstall(p_modMod, strAdjustedPath))
				{
					strInstallRoot = GameMode.SecondaryInstallationPath;
				}

				if (string.IsNullOrWhiteSpace(strInstallRoot))
					return string.Empty;

				return Path.GetFullPath(Path.Combine(strInstallRoot, strAdjustedPath));
			}
			catch
			{
				return string.Empty;
			}
		}

		private void AddVirtualLink(IVirtualModLink p_vmlLink)
		{
			AddVirtualLink(p_vmlLink, null);
		}

		private void AddVirtualLink(IVirtualModLink p_vmlLink, IMod p_modMod)
		{
			if (p_vmlLink == null)
				return;

			bool booOwnIndexMutationScope = m_alcVirtualLinkIndexMutationNesting.Value == 0;
			if (booOwnIndexMutationScope)
				EnsureVirtualLinkIndex();

			if (p_modMod == null && p_vmlLink.ModInfo != null)
				p_modMod = FindManagedMod(p_vmlLink.ModInfo);

			string strRawDeploymentPathKey;
			string strAdjustedDeploymentPathKey;
			GetVirtualLinkDeploymentPathKeys(p_vmlLink, p_modMod, out strRawDeploymentPathKey, out strAdjustedDeploymentPathKey);

			if (booOwnIndexMutationScope)
				m_alcVirtualLinkIndexMutationNesting.Value++;
			try
			{
				m_tslVirtualModList.Add(p_vmlLink);
			}
			finally
			{
				if (booOwnIndexMutationScope)
					m_alcVirtualLinkIndexMutationNesting.Value--;
			}

			lock (m_objVirtualLinkIndexLock)
			{
				if (!m_booVirtualLinkIndexDirty)
				{
					m_vliVirtualLinkIndex.Add(p_vmlLink, strRawDeploymentPathKey, strAdjustedDeploymentPathKey);
					m_intVirtualLinkIndexRevision++;
				}
			}
		}

		private void RemoveVirtualLink(IVirtualModLink p_vmlLink)
		{
			RemoveVirtualLink(p_vmlLink, null);
		}

		private void RemoveVirtualLink(IVirtualModLink p_vmlLink, IMod p_modMod)
		{
			if (p_vmlLink == null)
				return;

			bool booOwnIndexMutationScope = m_alcVirtualLinkIndexMutationNesting.Value == 0;
			if (booOwnIndexMutationScope)
				EnsureVirtualLinkIndex();

			if (p_modMod == null && p_vmlLink.ModInfo != null)
				p_modMod = FindManagedMod(p_vmlLink.ModInfo);

			string strRawDeploymentPathKey;
			string strAdjustedDeploymentPathKey;
			GetVirtualLinkDeploymentPathKeys(p_vmlLink, p_modMod, out strRawDeploymentPathKey, out strAdjustedDeploymentPathKey);
			bool booRemoved;

			if (booOwnIndexMutationScope)
				m_alcVirtualLinkIndexMutationNesting.Value++;
			try
			{
				booRemoved = m_tslVirtualModList.Remove(p_vmlLink);
			}
			finally
			{
				if (booOwnIndexMutationScope)
					m_alcVirtualLinkIndexMutationNesting.Value--;
			}

			if (!booRemoved)
				return;

			lock (m_objVirtualLinkIndexLock)
			{
				if (!m_booVirtualLinkIndexDirty)
				{
					m_vliVirtualLinkIndex.Remove(p_vmlLink, strRawDeploymentPathKey, strAdjustedDeploymentPathKey);
					m_intVirtualLinkIndexRevision++;
				}
			}
		}

		private void RemoveVirtualLinks(IEnumerable<IVirtualModLink> p_enmLinks)
		{
			List<IVirtualModLink> lstLinks = new List<IVirtualModLink>(p_enmLinks);
			foreach (IVirtualModLink vmlLink in lstLinks)
				RemoveVirtualLink(vmlLink);
		}

		private void ClearVirtualLinks()
		{
			m_alcVirtualLinkIndexMutationNesting.Value++;
			try
			{
				m_tslVirtualModList.Clear();
			}
			finally
			{
				m_alcVirtualLinkIndexMutationNesting.Value--;
			}

			lock (m_objVirtualLinkIndexLock)
			{
				m_vliVirtualLinkIndex.Clear();
				m_intVirtualLinkIndexRevision++;
				m_booVirtualLinkIndexDirty = false;
			}
		}

		public void SetCurrentList(IList<IVirtualModLink> p_ilvVirtualLinks)
		{
			DetachVirtualLinkList(m_tslVirtualModList);
			m_tslVirtualModList.Clear();
			m_tslVirtualModList = new ThreadSafeObservableList<IVirtualModLink>(p_ilvVirtualLinks);
			AttachVirtualLinkList(m_tslVirtualModList);
			RebuildVirtualLinkIndex();
		}

		/// <summary>
		/// Load the mod list from the XML file.
		/// </summary>
		/// <param name="p_strXMLFilePath">The XML file path.</param>
		public List<IVirtualModLink> LoadList(string p_strXMLFilePath)
		{
			VirtualModStoreData vmsData = m_vmsVirtualModStore.Load(p_strXMLFilePath, CURRENT_VERSION, PerformVersionCheck, GetModFileVersionForLoadList);

			DetachVirtualModInfoList(m_tslVirtualModInfo);
			m_tslVirtualModInfo.Clear();
			m_tslVirtualModInfo = new ThreadSafeObservableList<IVirtualModInfo>(vmsData.VirtualMods);
			AttachVirtualModInfoList(m_tslVirtualModInfo);
			return vmsData.VirtualLinks;
		}

		/// <summary>
		/// Load the mod list from the XML file.
		/// </summary>
		/// <param name="p_strProfilePath">The XML file path.</param>
		public bool LoadListOnDemand(string p_strProfilePath, out List<IVirtualModLink> p_lstVirtualLinks, out List<IVirtualModInfo> p_lstVirtualMods)
		{
			VirtualModStoreData vmsData;
			bool booLoaded = m_vmsVirtualModStore.TryLoad(p_strProfilePath, CURRENT_VERSION, PerformVersionCheck, GetModFileVersionForLoadListOnDemand, out vmsData);

			p_lstVirtualLinks = vmsData.VirtualLinks;
			p_lstVirtualMods = vmsData.VirtualMods;
			return booLoaded;
		}

		private string GetModFileVersionForLoadList(string p_strModFileName, string p_strDownloadId)
		{
			IMod mod = ModManager.GetModByFilename(p_strModFileName);
			return mod.HumanReadableVersion;
		}

		private string GetModFileVersionForLoadListOnDemand(string p_strModFileName, string p_strDownloadId)
		{
			IMod mod = ModManager.GetModByFilename(p_strModFileName);
			if ((mod != null) && (!string.IsNullOrEmpty(mod.HumanReadableVersion)))
				return mod.HumanReadableVersion;
			else if (!string.IsNullOrEmpty(p_strDownloadId))
			{
				mod = ModManager.GetModByDownloadID(p_strDownloadId);
				if ((mod != null) && (!string.IsNullOrEmpty(mod.HumanReadableVersion)))
					return mod.HumanReadableVersion;
				else
					return "0";
			}
			else
				return "0";
		}

		/// <summary>
		/// Load the mod list from the XML file.
		/// </summary>
		/// <param name="p_strXML">The XML file path.</param>
		/// <param name="p_strSavePath">The XML save path.</param>
		public List<IVirtualModLink> LoadImportedList(string p_strXML, string p_strSavePath)
		{
			List<string> lstAddedModInfo = new List<string>();
			List<IVirtualModLink> lstVirtualLinks = new List<IVirtualModLink>();
			List<IVirtualModInfo> lstVirtualMods = new List<IVirtualModInfo>();

			XDocument docVirtual = XDocument.Parse(p_strXML);
			string strVersion = docVirtual.Element("virtualModActivator").Attribute("fileVersion").Value;
			if (!PerformVersionCheck(new Version(strVersion)))
				throw new Exception(String.Format("Invalid Virtual Mod Activator version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

			bool booEditedList = false;

			try
			{
				XElement xelModList = docVirtual.Descendants("modList").FirstOrDefault();
				if ((xelModList != null) && xelModList.HasElements)
				{
					foreach (XElement xelMod in xelModList.Elements("modInfo"))
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
							if (!booEditedList)
								booEditedList = true;
							continue;
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
							IMod mod = ModManager.GetModByFilename(strModFileName);
							if ((mod != null) && (!string.IsNullOrEmpty(mod.HumanReadableVersion)))
								strFileVersion = mod.HumanReadableVersion;
							else if (!string.IsNullOrEmpty(strDownloadId))
							{
								mod = ModManager.GetModByDownloadID(strDownloadId);
								if ((mod != null) && (!string.IsNullOrEmpty(mod.HumanReadableVersion)))
									strFileVersion = mod.HumanReadableVersion;
								else
									strFileVersion = "0";
							}
							else
								strFileVersion = "0";
						}

						IMod modMod = ModManager.GetModByFilename(strModFileName);

						VirtualModInfo vmiMod = null;

						if (modMod != null)
							vmiMod = new VirtualModInfo(strModId, strDownloadId, strUpdatedDownloadId, strModName, strModFileName, strNewFileName, strModFilePath, strFileVersion);
						else
						{
							modMod = ModManager.GetModByDownloadID(strDownloadId);
							if (modMod != null)
							{
								if (!booEditedList)
									booEditedList = true;
								vmiMod = new VirtualModInfo(modMod.Id, modMod.DownloadId, strUpdatedDownloadId, modMod.ModName, modMod.Filename, strNewFileName, Path.GetDirectoryName(modMod.ModArchivePath), strFileVersion);
							}
							else
							{
								modMod = ModManager.GetModByDownloadID(strUpdatedDownloadId);
								if (modMod != null)
								{
									if (!booEditedList)
										booEditedList = true;
									vmiMod = new VirtualModInfo(modMod.Id, modMod.DownloadId, string.Empty, modMod.ModName, modMod.Filename, string.Empty, Path.GetDirectoryName(modMod.ModArchivePath), strFileVersion);
								}
							}
						}

						if (vmiMod == null)
							vmiMod = new VirtualModInfo(strModId, strDownloadId, strUpdatedDownloadId, strModName, strModFileName, strNewFileName, strModFilePath, strFileVersion);


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
				}

				if (booEditedList)
					SaveModList(Path.Combine(p_strSavePath, "modlist.xml"), lstVirtualMods, lstVirtualLinks);
			}
			catch
			{
				return null;
			}


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
				strPath = Path.Combine(GetInstallRootPath(ivlVirtualModLink.InstallRoot), ivlVirtualModLink.VirtualModPath);
			}

			return strPath;
		}

		private string GetInstallRootPath(ModInstallRoot p_mirInstallRoot)
		{
			return p_mirInstallRoot == ModInstallRoot.GameRoot ? GameMode.InstallationPath : m_strGameDataPath;
		}

		private string GetAdjustedVirtualPath(IMod p_modMod, string p_strBaseFilePath, ModInstallRoot p_mirInstallRoot)
		{
			if (p_mirInstallRoot == ModInstallRoot.GameRoot)
				return p_strBaseFilePath ?? string.Empty;

			return GameMode.GetModFormatAdjustedPath(p_modMod.Format, p_strBaseFilePath, p_modMod, true);
		}

		public int CheckFileLink(string p_strFilePath, out IMod p_modMod, out List<IVirtualModLink> p_lstFileLinks)
		{
			return CheckFileLink(p_strFilePath, ModInstallRoot.Default, out p_modMod, out p_lstFileLinks);
		}

		public int CheckFileLink(string p_strFilePath, ModInstallRoot p_mirInstallRoot, out IMod p_modMod, out List<IVirtualModLink> p_lstFileLinks)
		{
			return CheckFileLink(p_strFilePath, p_mirInstallRoot, -1, out p_modMod, out p_lstFileLinks);
		}

		private int CheckFileLink(string p_strFilePath, int p_intCurrentPriority, out IMod p_modMod)
		{
			List<IVirtualModLink> lstDummy;
			return CheckFileLink(p_strFilePath, ModInstallRoot.Default, p_intCurrentPriority, out p_modMod, out lstDummy);
		}

		private int CheckFileLink(string p_strFilePath, ModInstallRoot p_mirInstallRoot, int p_intCurrentPriority, out IMod p_modMod, out List<IVirtualModLink> p_lstFileLinks)
		{
			int intPriority = -1;
			IVirtualModLink vmlLowestPriorityLink = null;
			p_modMod = null;
			p_lstFileLinks = null;

			CollectIndexedFileLinkMatches(
				p_strFilePath,
				p_mirInstallRoot,
				p_intCurrentPriority,
				ref p_lstFileLinks,
				ref intPriority,
				ref vmlLowestPriorityLink);

			if (p_lstFileLinks != null && p_lstFileLinks.Count > 0)
			{
				if (vmlLowestPriorityLink != null)
					p_modMod = FindManagedMod(vmlLowestPriorityLink.ModInfo);
			}
			else if (File.Exists(p_strFilePath) && p_intCurrentPriority >= 0)
				intPriority = 0;
			else if (p_intCurrentPriority == -1)
			{
				string strLoosePath = Path.Combine(GetInstallRootPath(p_mirInstallRoot), p_strFilePath);
				if (File.Exists(strLoosePath))
					p_modMod = DummyMod;
			}

			return intPriority;
		}

		private static void CollectFileLinkMatches(
			VirtualLinkIndexBucket p_vlbBucket,
			ModInstallRoot p_mirInstallRoot,
			bool p_booFilterInstallRoot,
			int p_intCurrentPriority,
			ref List<IVirtualModLink> p_lstMatches,
			ref int p_intHighestPriority,
			ref IVirtualModLink p_vmlLowestPriorityLink)
		{
			if (p_vlbBucket == null)
				return;

			for (int i = 0; i < p_vlbBucket.Count; i++)
			{
				IVirtualModLink vmlLink = p_vlbBucket[i];
				if (vmlLink == null)
					continue;
				if (p_booFilterInstallRoot && vmlLink.InstallRoot != p_mirInstallRoot)
					continue;
				if (p_intCurrentPriority >= 0 && vmlLink.Priority == p_intCurrentPriority)
					continue;
				if (p_lstMatches != null && p_lstMatches.Contains(vmlLink))
					continue;

				if (p_lstMatches == null)
					p_lstMatches = new List<IVirtualModLink>(p_vlbBucket.Count);
				p_lstMatches.Add(vmlLink);

				if (vmlLink.Priority > p_intHighestPriority)
					p_intHighestPriority = vmlLink.Priority;
				if (p_vmlLowestPriorityLink == null || vmlLink.Priority < p_vmlLowestPriorityLink.Priority)
					p_vmlLowestPriorityLink = vmlLink;
			}
		}

		public bool PurgeLinks()
		{
			if (m_tslVirtualModInfo.Count > 0)
			{
				foreach (IVirtualModInfo modInfo in m_tslVirtualModInfo)
				{
					IMod modMod = ModManager.ManagedMods.FirstOrDefault(x => Path.GetFileName(x.Filename).ToLowerInvariant() == modInfo.ModFileName.ToLowerInvariant()
						|| (!string.IsNullOrEmpty(modInfo.DownloadId) && !string.IsNullOrEmpty(x.DownloadId) && modInfo.DownloadId.Equals(x.DownloadId)));
					DisableMod(modMod, true, null);
				}

				ClearVirtualLinks();
				SaveList(false);
			}
			return true;
		}

		public bool PurgeLinks(IList<IVirtualModLink> p_lstToPurge)
		{
			if (p_lstToPurge.Count > 0)
			{
				foreach (IVirtualModLink modLink in p_lstToPurge)
				{
					IMod modMod = ModManager.ManagedMods.FirstOrDefault(x => modLink.ModInfo.ModFileName.Equals(Path.GetFileName(x.Filename).ToString(), StringComparison.InvariantCultureIgnoreCase)
						|| (!string.IsNullOrEmpty(modLink.ModInfo.DownloadId) && !string.IsNullOrEmpty(x.DownloadId) && modLink.ModInfo.DownloadId.Equals(x.DownloadId)));
					RemoveFileLink(modLink, modMod, true);
				}

				List<IVirtualModInfo> lstModInfo = m_tslVirtualModList.Select(x => x.ModInfo).Distinct().ToList();
				List<IVirtualModInfo> lstMissing = m_tslVirtualModInfo.Except(lstModInfo, new VirtualModInfoEqualityComparer()).ToList();
				if ((lstMissing != null) && (lstMissing.Count > 0))
					m_tslVirtualModInfo.RemoveRange(lstMissing);

				SaveList(false);
			}
			return true;
		}

		public void PurgeLinkList()
		{
			List<IVirtualModInfo> lstModInfo = m_tslVirtualModList.Select(x => x.ModInfo).Distinct().ToList();
			List<IVirtualModInfo> lstMissing = m_tslVirtualModInfo.Except(lstModInfo, new VirtualModInfoEqualityComparer()).ToList();
			if ((lstMissing != null) && (lstMissing.Count > 0))
				m_tslVirtualModInfo.RemoveRange(lstMissing);

			SaveList(false);
		}

		public VirtualInstallReconciliationReport ValidateVirtualInstall()
		{
			RefreshVirtualInstallIndexes();
			return InspectVirtualInstallState();
		}

		public VirtualInstallReconciliationReport RepairVirtualInstallMetadata()
		{
			RefreshVirtualInstallIndexes();

			List<string> lstRepairs = new List<string>();
			List<IVirtualModLink> lstLinksToRemove = m_tslVirtualModList.Where(CanRemoveInactiveMetadataLink).ToList();
			if (lstLinksToRemove.Count > 0)
			{
				RemoveVirtualLinks(lstLinksToRemove);
				lstRepairs.Add(string.Format("Removed {0} inactive virtual-link metadata entries with missing source/path/mod metadata.", lstLinksToRemove.Count));
			}

			List<IVirtualModInfo> lstModInfo = m_tslVirtualModList.Select(x => x.ModInfo).Where(x => x != null).Distinct().ToList();
			List<IVirtualModInfo> lstMissing = m_tslVirtualModInfo.Except(lstModInfo, new VirtualModInfoEqualityComparer()).ToList();
			if ((lstMissing != null) && (lstMissing.Count > 0))
			{
				MarkVirtualModInfoLookupDirty();
				m_tslVirtualModInfo.RemoveRange(lstMissing);
				MarkVirtualModInfoLookupDirty();
				lstRepairs.Add(string.Format("Removed {0} unreferenced virtual-mod metadata entries.", lstMissing.Count));
			}

			if (lstRepairs.Count > 0)
			{
				RefreshVirtualInstallIndexes();
				SaveList(false);
			}

			VirtualInstallReconciliationReport report = InspectVirtualInstallState();
			foreach (string strRepair in lstRepairs)
				report.AddRepair(strRepair);

			return report;
		}

		private VirtualInstallReconciliationReport InspectVirtualInstallState()
		{
			return m_vrcVirtualInstallReconciler.Inspect(new List<IVirtualModInfo>(m_tslVirtualModInfo), new List<IVirtualModLink>(m_tslVirtualModList), GetVirtualInstallSourceRoots(), GetVirtualInstallGameDataRoots());
		}

		private void RefreshVirtualInstallIndexes()
		{
			MarkVirtualModInfoLookupDirty();
			MarkVirtualLinkIndexDirty();
			EnsureVirtualModInfoLookup();
			EnsureVirtualLinkIndex();
		}

		private bool CanRemoveInactiveMetadataLink(IVirtualModLink p_vmlLink)
		{
			if (p_vmlLink == null || p_vmlLink.Active)
				return false;

			if (p_vmlLink.ModInfo == null)
				return true;

			if (string.IsNullOrWhiteSpace(p_vmlLink.RealModPath) || string.IsNullOrWhiteSpace(p_vmlLink.VirtualModPath))
				return true;

			return !VirtualInstallSourceFileExists(p_vmlLink.RealModPath);
		}

		private bool VirtualInstallSourceFileExists(string p_strRealModPath)
		{
			foreach (string strSourceRoot in GetVirtualInstallSourceRoots())
			{
				try
				{
					if (File.Exists(Path.Combine(strSourceRoot, p_strRealModPath)))
						return true;
				}
				catch
				{
				}
			}

			return false;
		}

		private List<string> GetVirtualInstallGameDataRoots()
		{
			List<string> lstGameDataRoots = new List<string>();
			if (!string.IsNullOrWhiteSpace(m_strGameDataPath))
				lstGameDataRoots.Add(m_strGameDataPath);

			if (GameMode != null && !string.IsNullOrWhiteSpace(GameMode.InstallationPath) && !lstGameDataRoots.Contains(GameMode.InstallationPath, StringComparer.OrdinalIgnoreCase))
				lstGameDataRoots.Add(GameMode.InstallationPath);

			if (GameMode != null && GameMode.HasSecondaryInstallPath && !string.IsNullOrWhiteSpace(GameMode.SecondaryInstallationPath) && !lstGameDataRoots.Contains(GameMode.SecondaryInstallationPath, StringComparer.OrdinalIgnoreCase))
				lstGameDataRoots.Add(GameMode.SecondaryInstallationPath);

			return lstGameDataRoots;
		}

		private List<string> GetVirtualInstallSourceRoots()
		{
			List<string> lstSourceRoots = new List<string>();
			if (!string.IsNullOrWhiteSpace(m_strVirtualActivatorPath))
				lstSourceRoots.Add(m_strVirtualActivatorPath);

			if (MultiHDMode)
			{
				try
				{
					string strHDLinkFolder = HDLinkFolder;
					if (!string.IsNullOrWhiteSpace(strHDLinkFolder) && !lstSourceRoots.Contains(strHDLinkFolder, StringComparer.OrdinalIgnoreCase))
						lstSourceRoots.Add(strHDLinkFolder);
				}
				catch (Exception e)
				{
					Trace.TraceWarning("Unable to include the HD link folder in virtual install reconciliation: {0}", e.Message);
				}
			}

			return lstSourceRoots;
		}

		public void AddInactiveLink(IMod p_modMod, string p_strBaseFilePath, int p_intPriority)
		{
			AddInactiveLink(p_modMod, p_strBaseFilePath, p_intPriority, ModInstallRoot.Default);
		}

		public void AddInactiveLink(IMod p_modMod, string p_strBaseFilePath, int p_intPriority, ModInstallRoot p_mirInstallRoot)
		{
			IVirtualModInfo modInfo = FindVirtualModInfoByFileName(Path.GetFileName(p_modMod.Filename));
			if (modInfo == null)
			{
				VirtualModInfo vmiModInfo = new VirtualModInfo(p_modMod.Id, p_modMod.DownloadId, p_modMod.ModName, p_modMod.Filename, p_modMod.HumanReadableVersion);
				AddVirtualModInfo(vmiModInfo);
				modInfo = vmiModInfo;
			}
			string strRealFilePath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.Filename), p_strBaseFilePath);
			AddVirtualLink(new VirtualModLink(strRealFilePath, p_strBaseFilePath, p_intPriority, false, modInfo, p_mirInstallRoot), p_modMod);
		}

		public string AddFileLink(IMod p_modMod, string p_strBaseFilePath, bool p_booIsSwitching, bool p_booIsRestoring, int p_intPriority)
		{
			return AddFileLink(p_modMod, p_strBaseFilePath, p_booIsSwitching, p_booIsRestoring, p_intPriority, ModInstallRoot.Default);
		}

		public string AddFileLink(IMod p_modMod, string p_strBaseFilePath, bool p_booIsSwitching, bool p_booIsRestoring, int p_intPriority, ModInstallRoot p_mirInstallRoot)
		{
			if (p_booIsSwitching)
			{
				string strLoosePath = Path.Combine(GetInstallRootPath(p_mirInstallRoot), p_strBaseFilePath);
				if (File.Exists(strLoosePath))
					OverwriteLooseFile(p_strBaseFilePath, Path.GetFileName(p_modMod.Filename), p_mirInstallRoot);
			}

			return AddFileLink(p_modMod, p_strBaseFilePath, null, p_booIsSwitching, p_booIsRestoring, false, p_intPriority, p_mirInstallRoot);
		}

		private string GetRealFilePath(IMod p_modMod, string p_strBaseFilePath, string p_strRootPath)
		{
			string strRealFilePath = string.Empty;

			string strCheckPath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.Filename), p_strBaseFilePath);

			// First we check for the filename path
			string strActivatorFilePath = Path.Combine(p_strRootPath, strCheckPath);
			if (!File.Exists(strActivatorFilePath))
			{
				strCheckPath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.Filename), GameMode.GetModFormatAdjustedPath(p_modMod.Format, p_strBaseFilePath, p_modMod, false));
				strActivatorFilePath = Path.Combine(p_strRootPath, strCheckPath);
				if (!File.Exists(strActivatorFilePath))
				{
					strCheckPath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.Filename), GameMode.GetModFormatAdjustedPath(p_modMod.Format, p_strBaseFilePath, p_modMod, true));
					strActivatorFilePath = Path.Combine(p_strRootPath, strCheckPath);
					if (File.Exists(strActivatorFilePath))
						strRealFilePath = strCheckPath;
					else
					{
						strCheckPath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.Filename), GameMode.GetModFormatAdjustedPath(p_modMod.Format, p_strBaseFilePath, true));
						strActivatorFilePath = Path.Combine(p_strRootPath, strCheckPath);
						if (File.Exists(strActivatorFilePath))
							strRealFilePath = strCheckPath;
					}
				}
				else
					strRealFilePath = strCheckPath;
			}
			else
				strRealFilePath = strCheckPath;

			// If we didn't find the correct path and the downloadID is valid we try it
			if (string.IsNullOrEmpty(strRealFilePath) && !string.IsNullOrWhiteSpace(p_modMod.DownloadId))
			{
				strCheckPath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.DownloadId), p_strBaseFilePath);

				strActivatorFilePath = Path.Combine(p_strRootPath, strCheckPath);
				if (!File.Exists(strActivatorFilePath))
				{
					strCheckPath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.DownloadId), GameMode.GetModFormatAdjustedPath(p_modMod.Format, p_strBaseFilePath, p_modMod, false));
					strActivatorFilePath = Path.Combine(p_strRootPath, strCheckPath);
					if (!File.Exists(strActivatorFilePath))
					{
						strCheckPath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.DownloadId), GameMode.GetModFormatAdjustedPath(p_modMod.Format, p_strBaseFilePath, p_modMod, true));
						strActivatorFilePath = Path.Combine(p_strRootPath, strCheckPath);
						if (File.Exists(strActivatorFilePath))
							strRealFilePath = strCheckPath;
						else
						{
							strCheckPath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.DownloadId), GameMode.GetModFormatAdjustedPath(p_modMod.Format, p_strBaseFilePath, true));
							strActivatorFilePath = Path.Combine(p_strRootPath, strCheckPath);
							if (File.Exists(strActivatorFilePath))
								strRealFilePath = strCheckPath;
						}
					}
					else
						strRealFilePath = strCheckPath;
				}
				else
					strRealFilePath = strCheckPath;
			}

			if (string.IsNullOrEmpty(strRealFilePath))
				strRealFilePath = Path.Combine(Path.GetFileNameWithoutExtension(p_modMod.Filename), p_strBaseFilePath);

			return strRealFilePath;
		}

		private string GetRealFilePathFromSource(string p_strSourceFile)
		{
			string strRelativePath;
			if (TryGetPathRelativeToRoot(p_strSourceFile, m_strVirtualActivatorPath, out strRelativePath))
				return strRelativePath;

			if (MultiHDMode && !string.IsNullOrEmpty(HDLinkFolder) && TryGetPathRelativeToRoot(p_strSourceFile, HDLinkFolder, out strRelativePath))
				return strRelativePath;

			return null;
		}

		private static bool TryGetPathRelativeToRoot(string p_strFilePath, string p_strRootPath, out string p_strRelativePath)
		{
			p_strRelativePath = null;
			if (string.IsNullOrWhiteSpace(p_strFilePath) || string.IsNullOrWhiteSpace(p_strRootPath))
				return false;

			try
			{
				string strFullRootPath = Path.GetFullPath(p_strRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				string strFullFilePath = Path.GetFullPath(p_strFilePath);
				string strRootPrefix = strFullRootPath + Path.DirectorySeparatorChar;

				if (!strFullFilePath.StartsWith(strRootPrefix, StringComparison.OrdinalIgnoreCase))
					return false;

				p_strRelativePath = strFullFilePath.Substring(strRootPrefix.Length);
				return !string.IsNullOrWhiteSpace(p_strRelativePath);
			}
			catch
			{
				return false;
			}
		}

		public string AddFileLink(IMod p_modMod, string p_strBaseFilePath, string p_strSourceFile, bool p_booIsSwitching, bool p_booIsRestoring, bool p_booHandlePlugin, int p_intPriority)
		{
			return AddFileLink(p_modMod, p_strBaseFilePath, p_strSourceFile, p_booIsSwitching, p_booIsRestoring, p_booHandlePlugin, p_intPriority, ModInstallRoot.Default);
		}

		public string AddFileLink(IMod p_modMod, string p_strBaseFilePath, string p_strSourceFile, bool p_booIsSwitching, bool p_booIsRestoring, bool p_booHandlePlugin, int p_intPriority, ModInstallRoot p_mirInstallRoot)
		{
			string strSourceFile = p_strSourceFile;

			// When the installer provides the exact extracted source, derive the persisted
			// path directly instead of probing both VirtualInstall and NMMLink for every file.
			string strSourceRelativePath = GetRealFilePathFromSource(strSourceFile);
			string strRealFilePath = strSourceRelativePath;
			string strRealLinkFilePath = strSourceRelativePath;

			if (string.IsNullOrEmpty(strRealFilePath))
				strRealFilePath = GetRealFilePath(p_modMod, p_strBaseFilePath, m_strVirtualActivatorPath);

			if (MultiHDMode && !string.IsNullOrEmpty(HDLinkFolder) && string.IsNullOrEmpty(strRealLinkFilePath))
				strRealLinkFilePath = GetRealFilePath(p_modMod, p_strBaseFilePath, HDLinkFolder);

			string strAdjustedFilePath = GetAdjustedVirtualPath(p_modMod, p_strBaseFilePath, p_mirInstallRoot);

			if (string.IsNullOrEmpty(strAdjustedFilePath))
				return string.Empty;

			string strVirtualFileLink = string.Empty;

			if (p_mirInstallRoot == ModInstallRoot.Default && GameMode.HasSecondaryInstallPath && GameMode.CheckSecondaryInstall(p_modMod, strAdjustedFilePath))
				strVirtualFileLink = Path.Combine(GameMode.SecondaryInstallationPath, strAdjustedFilePath);
			else
				strVirtualFileLink = Path.Combine(GetInstallRootPath(p_mirInstallRoot), strAdjustedFilePath);

			// This is the path we're using if the mod was installed in the base VirtualInstall folder
			string strActivatorFilePath = string.IsNullOrWhiteSpace(strSourceFile) ? Path.Combine(m_strVirtualActivatorPath, strRealFilePath) : strSourceFile;

			// We're using this path if MultiHD Mode is active and the file type requires a hardlink
			string strLinkFilePath = string.Empty;
			if (MultiHDMode)
			{
				strLinkFilePath = string.IsNullOrWhiteSpace(strSourceFile) ? Path.Combine(HDLinkFolder, strRealLinkFilePath) : strSourceFile;
			}

			if (!Directory.Exists(Path.GetDirectoryName(strVirtualFileLink)))
				FileUtil.CreateDirectory(Path.GetDirectoryName(strVirtualFileLink));

			string strFileType = Path.GetExtension(strVirtualFileLink);
			if (!strFileType.StartsWith("."))
				strFileType = "." + strFileType;

			bool booHardLinkRequired = GameMode.HardlinkRequiredFilesType(strVirtualFileLink) ||
				strFileType.Equals(".exe", StringComparison.InvariantCultureIgnoreCase) ||
				strFileType.Equals(".jar", StringComparison.InvariantCultureIgnoreCase);

			if (File.Exists(strVirtualFileLink))
				FileUtil.ForceDelete(strVirtualFileLink);

			IVirtualModInfo modInfo = FindVirtualModInfoByFileName(Path.GetFileName(p_modMod.Filename));
			if (modInfo == null)
			{
				VirtualModInfo vmiModInfo = new VirtualModInfo(p_modMod.Id, p_modMod.DownloadId, p_modMod.ModName, p_modMod.Filename, p_modMod.HumanReadableVersion);
				AddVirtualModInfo(vmiModInfo);
				modInfo = vmiModInfo;
			}

			try
			{
				if (GameMode.RealFileRequired(strFileType))
				{
					File.Copy(strActivatorFilePath, strVirtualFileLink, true);

					if (File.Exists(strVirtualFileLink))
					{
						if (!p_booIsRestoring)
							AddVirtualLink(new VirtualModLink(strRealFilePath, p_strBaseFilePath, p_intPriority, true, modInfo, p_mirInstallRoot), p_modMod);
						else
							strVirtualFileLink = string.Empty;
					}
					else
						strVirtualFileLink = string.Empty;
				}
				else if (booHardLinkRequired)
				{
					if (MultiHDMode)
					{
						bool booSuccess = CreateHardLink(strVirtualFileLink, strLinkFilePath, IntPtr.Zero);
						if (!booSuccess)
							File.Copy(strLinkFilePath, strVirtualFileLink, true);

						if (booSuccess || File.Exists(strVirtualFileLink))
						{
							if (!p_booIsRestoring)
								AddVirtualLink(new VirtualModLink(strRealLinkFilePath, p_strBaseFilePath, p_intPriority, true, modInfo, p_mirInstallRoot), p_modMod);
							else
								strVirtualFileLink = string.Empty;
						}
						else
							strVirtualFileLink = string.Empty;
					}
					else
					{
						bool booSuccess = CreateHardLink(strVirtualFileLink, strActivatorFilePath, IntPtr.Zero);
						if (!booSuccess)
							File.Copy(strActivatorFilePath, strVirtualFileLink, true);

						if (booSuccess || File.Exists(strVirtualFileLink))
						{
							if (!p_booIsRestoring)
								AddVirtualLink(new VirtualModLink(strRealFilePath, p_strBaseFilePath, p_intPriority, true, modInfo, p_mirInstallRoot), p_modMod);
							else
								strVirtualFileLink = string.Empty;
						}
						else
							strVirtualFileLink = string.Empty;
					}
				}
				else if (!DisableLinkCreation)
				{
					if (!MultiHDMode && (CreateHardLink(strVirtualFileLink, strActivatorFilePath, IntPtr.Zero)))
					{
						if (!p_booIsRestoring)
							AddVirtualLink(new VirtualModLink(strRealFilePath, p_strBaseFilePath, p_intPriority, true, modInfo, p_mirInstallRoot), p_modMod);
						else
							strVirtualFileLink = string.Empty;
					}
					else if (CreateSymbolicLink(strVirtualFileLink, strActivatorFilePath, 0))
					{
						if (!p_booIsRestoring)
							AddVirtualLink(new VirtualModLink(strRealFilePath, p_strBaseFilePath, p_intPriority, true, modInfo, p_mirInstallRoot), p_modMod);
						else
							strVirtualFileLink = string.Empty;
					}
					else
						strVirtualFileLink = string.Empty;
				}
				else
					strVirtualFileLink = string.Empty;

			}
			catch
			{
				strVirtualFileLink = string.Empty;
			}

			if (p_booIsSwitching && (PluginManager != null) && !string.IsNullOrEmpty(strVirtualFileLink) && !p_booIsRestoring)
				if (PluginManager.IsActivatiblePluginFile(strVirtualFileLink))
				{
					PluginManager.AddPlugin(strVirtualFileLink);
					if (p_booHandlePlugin)
						PluginManager.ActivatePlugin(strVirtualFileLink);
				}

			UpdateModInfoNowOrDefer(p_modMod);
			return strVirtualFileLink;
		}

		public void RemoveFileLink(string p_strFilePath, IMod p_modMod)
		{
			string strPathCheck = p_strFilePath.Replace(m_strVirtualActivatorPath + Path.DirectorySeparatorChar.ToString(), string.Empty);
			IVirtualModLink ivlVirtualModLink = VirtualLinks.Find(x => x.VirtualModPath == strPathCheck);
			if (ivlVirtualModLink == null)
				ivlVirtualModLink = VirtualLinks.Find(x => x.RealModPath == strPathCheck);
			if (ivlVirtualModLink == null)
				ivlVirtualModLink = VirtualLinks.Find(x => Path.GetFullPath(x.RealModPath) == Path.GetFullPath(strPathCheck));
			RemoveFileLink(ivlVirtualModLink, p_modMod, false);
		}

		public void RemoveFileLink(IVirtualModLink p_ivlVirtualLink, IMod p_modMod)
		{
			RemoveFileLink(p_ivlVirtualLink, p_modMod, false);
		}

		public void PurgeFileLink(IVirtualModLink p_ivlVirtualLink, IMod p_modMod)
		{
			RemoveFileLink(p_ivlVirtualLink, p_modMod, true);
		}

		protected void RemoveFileLink(IVirtualModLink p_ivlVirtualLink, IMod p_modMod, bool p_booPurging)
		{
			IMod modCheck;

			if (p_ivlVirtualLink != null)
			{
				bool booActive = p_ivlVirtualLink.Active;
				int intCurrentPriority = p_ivlVirtualLink.Priority;
				List<IVirtualModLink> lstOverwrites;
				int intPriority = CheckFileLink(p_ivlVirtualLink.VirtualModPath, p_ivlVirtualLink.InstallRoot, intCurrentPriority, out modCheck, out lstOverwrites);
				string strInstallRootPath = GetInstallRootPath(p_ivlVirtualLink.InstallRoot);
				string strLinkPath = Path.Combine(strInstallRootPath, p_ivlVirtualLink.VirtualModPath);
				if ((!File.Exists(strLinkPath)) && (p_modMod != null))
					strLinkPath = Path.Combine(strInstallRootPath, GetAdjustedVirtualPath(p_modMod, p_ivlVirtualLink.VirtualModPath, p_ivlVirtualLink.InstallRoot));

				if (GameMode.HasSecondaryInstallPath)
					if (GameMode.CheckSecondaryUninstall(strLinkPath))
						return;

				if ((PluginManager != null) && ((intPriority < 0) || (modCheck == null)))
				{
					if (PluginManager.IsActivatiblePluginFile(strLinkPath) &&
						PluginManager.IsPluginRegistered(strLinkPath))
					{
						PluginManager.RemovePlugin(strLinkPath);
					}
				}

				string strPath;
				string strStop = strInstallRootPath;
				if ((p_ivlVirtualLink.InstallRoot != ModInstallRoot.GameRoot) && (p_modMod != null) && GameMode.HasSecondaryInstallPath && GameMode.CheckSecondaryInstall(p_modMod, strLinkPath))
				{
					strPath = Path.Combine(GameMode.SecondaryInstallationPath, GameMode.GetModFormatAdjustedPath(p_modMod.Format, p_ivlVirtualLink.VirtualModPath, p_modMod, true));
					strStop = GameMode.SecondaryInstallationPath;
				}
				else
					strPath = strLinkPath;

				if (p_ivlVirtualLink.Active)
					if (File.Exists(strPath))
						FileUtil.ForceDelete(strPath);

				RemoveVirtualLink(p_ivlVirtualLink, p_modMod);

				if ((intPriority >= 0) && !p_booPurging && (modCheck != null))
				{
					if (booActive)
						UpdateLinkListPriority(lstOverwrites, modCheck, false, true);
				}
				else
				{
					if (Directory.Exists(m_strVirtualActivatorOverwritePath))
					{
						string strOverwrite = Path.Combine(m_strVirtualActivatorOverwritePath, Path.GetFileNameWithoutExtension(p_ivlVirtualLink.ModInfo.ModFileName), p_ivlVirtualLink.VirtualModPath);
						if (File.Exists(strOverwrite))
						{
							try
							{
								File.Move(strOverwrite, strPath);
								TrimEmptyDirectories(Path.GetDirectoryName(strOverwrite), m_strVirtualActivatorOverwritePath);
							}
							catch { }
						}
					}
				}

				TrimEmptyDirectories(Path.GetDirectoryName(strPath), strStop);
			}
		}

		public void UpdateLinkPriority(IVirtualModLink p_ivlFileLink)
		{
			VirtualModLink vmlUpdated = new VirtualModLink(p_ivlFileLink);
			RemoveVirtualLink(p_ivlFileLink);

			vmlUpdated.Priority = 0;
			vmlUpdated.Active = true;
			AddVirtualLink(vmlUpdated);
		}

		private void UpdateLinkListPriority(List<IVirtualModLink> p_lstFileLinks, IMod p_modMod, bool p_booIncrement, bool p_booActivateFirst)
		{
			RemoveVirtualLinks(p_lstFileLinks);

			if (p_booActivateFirst)
			{
				VirtualModLink vmlFirst = new VirtualModLink(p_lstFileLinks.OrderBy(x => x.Priority).First());
				p_lstFileLinks.Remove(vmlFirst);

				if (vmlFirst.Priority > 0)
					vmlFirst.Priority--;
				vmlFirst.Active = true;
				AddVirtualLink(vmlFirst);
				AddFileLink(p_modMod, vmlFirst.VirtualModPath, false, true, vmlFirst.Priority, vmlFirst.InstallRoot);
			}

			if (p_lstFileLinks.Count > 0)
			{
				foreach (VirtualModLink vml in p_lstFileLinks.OrderBy(x => x.Priority))
				{
					if (p_booIncrement)
						vml.Priority++;
					else
						if (vml.Priority > 0)
						vml.Priority--;

					vml.Active = false;
					AddVirtualLink(vml);
				}
			}

			if (p_modMod != null)
			{
				p_modMod.PlaceInModLoadOrder = p_modMod.NewPlaceInModLoadOrder;
				p_modMod.UpdateInfo(p_modMod, true);
			}
		}

		public void UpdateLinkListPriority(List<IVirtualModLink> p_lstFileLinks)
		{
			UpdateLinkListPriority(p_lstFileLinks, null, true, false);
		}

		#endregion


		internal VirtualFileOwnerSwitchResult SwitchFileOwner(string p_strRelativePath, string p_strSelectedOwnerKey)
		{
			string strRelativePath = NormalizeVirtualFileManagerPath(p_strRelativePath);
			if (string.IsNullOrEmpty(strRelativePath))
				return VirtualFileOwnerSwitchResult.Failed("The selected file path is empty.");

			string strDeploymentRoot = FileManagerQueryService.GetDeploymentRoot(GameMode);
			string strSelectedDeployedPath;
			try
			{
				strSelectedDeployedPath = Path.GetFullPath(Path.Combine(strDeploymentRoot, strRelativePath));
			}
			catch (Exception ex)
			{
				return VirtualFileOwnerSwitchResult.Failed(ex);
			}

			List<IVirtualModLink> lstFileLinks = m_tslVirtualModList
				.Where(x => x != null && string.Equals(GetDeployedFilePath(x), strSelectedDeployedPath, StringComparison.OrdinalIgnoreCase))
				.ToList();

			if (lstFileLinks.Count == 0)
				return VirtualFileOwnerSwitchResult.Failed("The selected file is not tracked by virtual install metadata.");

			IVirtualModLink vmlCurrentOwner = lstFileLinks.OrderBy(x => x.Priority).FirstOrDefault(x => x.Active);
			if (vmlCurrentOwner == null)
				return VirtualFileOwnerSwitchResult.Failed("The selected file has no active NMM owner.");

			IVirtualModLink vmlSelectedOwner = lstFileLinks.FirstOrDefault(x => FileManagerQueryService.CreateOwnerKey(x.ModInfo).Equals(p_strSelectedOwnerKey ?? string.Empty, StringComparison.OrdinalIgnoreCase));
			if (vmlSelectedOwner == null)
				return VirtualFileOwnerSwitchResult.Failed("The selected owner is not a valid candidate for this file.");

			if (ReferenceEquals(vmlCurrentOwner, vmlSelectedOwner) || FileManagerQueryService.CreateOwnerKey(vmlCurrentOwner.ModInfo).Equals(FileManagerQueryService.CreateOwnerKey(vmlSelectedOwner.ModInfo), StringComparison.OrdinalIgnoreCase))
				return VirtualFileOwnerSwitchResult.Succeeded(strRelativePath, FileManagerQueryService.CreateOwnerKey(vmlSelectedOwner.ModInfo));

			IMod modSelected = FindManagedMod(vmlSelectedOwner.ModInfo);
			if (modSelected == null)
				return VirtualFileOwnerSwitchResult.Failed("The selected owner mod is no longer managed by NMM.");

			if (!VirtualOwnerSourceExists(modSelected, vmlSelectedOwner.VirtualModPath))
				return VirtualFileOwnerSwitchResult.Failed("The selected owner's staged source file is missing.");

			string strDeployedPath = GetDeployedFilePath(vmlSelectedOwner);
			if (string.IsNullOrWhiteSpace(strDeployedPath))
				return VirtualFileOwnerSwitchResult.Failed("The selected owner's deployment path could not be resolved.");
			string strBackupPath = null;
			bool booPluginWasActive = false;
			bool booPluginWasRegistered = false;

			if ((PluginManager != null) && PluginManager.IsActivatiblePluginFile(strDeployedPath))
			{
				booPluginWasActive = PluginManager.IsPluginActive(strDeployedPath);
				booPluginWasRegistered = PluginManager.IsPluginRegistered(strDeployedPath);
			}

			try
			{
				if (File.Exists(strDeployedPath))
				{
					strBackupPath = Path.Combine(Path.GetTempPath(), "NMM_FileManager_" + Guid.NewGuid().ToString("N") + Path.GetExtension(strDeployedPath));
					File.Copy(strDeployedPath, strBackupPath, true);
				}

				AddFileLink(modSelected, vmlSelectedOwner.VirtualModPath, null, true, true, false, vmlSelectedOwner.Priority, vmlSelectedOwner.InstallRoot);
				if (!File.Exists(strDeployedPath))
					throw new IOException("The selected owner file could not be deployed.");

				NormalizeFileOwnerPriorities(lstFileLinks, vmlSelectedOwner);
				SaveList(true);

				if ((PluginManager != null) && PluginManager.IsActivatiblePluginFile(strDeployedPath))
				{
					if (booPluginWasRegistered && !PluginManager.IsPluginRegistered(strDeployedPath))
						PluginManager.AddPlugin(strDeployedPath);
					if (booPluginWasActive && !PluginManager.IsPluginActive(strDeployedPath))
						PluginManager.ActivatePlugin(strDeployedPath);
				}

				return VirtualFileOwnerSwitchResult.Succeeded(strRelativePath, FileManagerQueryService.CreateOwnerKey(vmlSelectedOwner.ModInfo));
			}
			catch (Exception ex)
			{
				try
				{
					if (!string.IsNullOrEmpty(strBackupPath) && File.Exists(strBackupPath))
						File.Copy(strBackupPath, strDeployedPath, true);
				}
				catch { }

				return VirtualFileOwnerSwitchResult.Failed(ex);
			}
			finally
			{
				try
				{
					if (!string.IsNullOrEmpty(strBackupPath) && File.Exists(strBackupPath))
						File.Delete(strBackupPath);
				}
				catch { }
			}
		}

		private static string NormalizeVirtualFileManagerPath(string p_strPath)
		{
			if (string.IsNullOrWhiteSpace(p_strPath))
				return string.Empty;

			return p_strPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}

		private IMod FindManagedMod(IVirtualModInfo p_vmiModInfo)
		{
			if (p_vmiModInfo == null)
				return null;

			return ModManager.ManagedMods.FirstOrDefault(x => VirtualModInfoMatchesMod(p_vmiModInfo, x, Path.GetFileName(x.Filename)));
		}

		private bool VirtualOwnerSourceExists(IMod p_modMod, string p_strRelativePath)
		{
			string strRealFilePath = GetRealFilePath(p_modMod, p_strRelativePath, m_strVirtualActivatorPath);
			if (File.Exists(Path.Combine(m_strVirtualActivatorPath, strRealFilePath)))
				return true;

			if (MultiHDMode && !string.IsNullOrEmpty(HDLinkFolder))
			{
				string strRealLinkFilePath = GetRealFilePath(p_modMod, p_strRelativePath, HDLinkFolder);
				return File.Exists(Path.Combine(HDLinkFolder, strRealLinkFilePath));
			}

			return false;
		}

		private void NormalizeFileOwnerPriorities(List<IVirtualModLink> p_lstFileLinks, IVirtualModLink p_vmlSelectedOwner)
		{
			List<IVirtualModLink> lstOrderedLinks = p_lstFileLinks
				.OrderBy(x => ReferenceEquals(x, p_vmlSelectedOwner) ? 0 : 1)
				.ThenBy(x => x.Priority)
				.ThenBy(x => x.ModInfo == null ? string.Empty : x.ModInfo.ModName, StringComparer.OrdinalIgnoreCase)
				.Select(x => new VirtualModLink(x))
				.ToList<IVirtualModLink>();

			RemoveVirtualLinks(p_lstFileLinks);
			for (int i = 0; i < lstOrderedLinks.Count; i++)
			{
				lstOrderedLinks[i].Priority = i;
				lstOrderedLinks[i].Active = i == 0;
				AddVirtualLink(lstOrderedLinks[i]);
			}
		}

		#region Mod Management

		public void DisableMod(IMod p_modMod)
		{
			DisableMod(p_modMod, false, null);
		}

		public void DisableModFiles(IMod p_modMod)
		{
			DisableMod(p_modMod, true, null);
		}

		public bool DisableModWithProgress(IMod p_modMod, bool p_booPurging, Action<VirtualModDisableProgress> p_actProgress)
		{
			return DisableMod(p_modMod, p_booPurging, p_actProgress);
		}

		private bool DisableMod(IMod p_modMod, bool p_booPurging, Action<VirtualModDisableProgress> p_actProgress)
		{
			if (p_modMod == null)
				return false;

			string modFileName = Path.GetFileName(p_modMod.Filename);
			bool booHasVirtualModInfo = CheckIsModActive(p_modMod);
			ConcurrentQueue<IVirtualModLink> cqLinks = new ConcurrentQueue<IVirtualModLink>();

			if (m_tslVirtualModList.Count > 0)
			{
				Parallel.ForEach(m_tslVirtualModList, (fileLink) =>
				{
					if (VirtualModLinkMatchesMod(fileLink, p_modMod, modFileName))
						cqLinks.Enqueue(fileLink);
				});
			}

			if (!booHasVirtualModInfo && cqLinks.Count == 0)
				return false;

			ReportVirtualDisableProgress(p_actProgress, "Disabling deployed files...", 0, cqLinks.Count);

			int intProcessed = 0;
			using (BeginModInfoUpdateBatch())
			using (BeginVirtualLinkUpdateBatch(0))
			{
				foreach (IVirtualModLink Link in cqLinks)
				{
					RemoveFileLink(Link, p_modMod, p_booPurging);
					intProcessed++;
					ReportVirtualDisableProgress(p_actProgress, Link == null ? "Disabling deployed files..." : Link.VirtualModPath, intProcessed, cqLinks.Count);
				}
			}

			TxFileManager tfmFileManager = new TxFileManager();
			IIniInstaller iniIniInstaller = new IniInstaller(p_modMod, ModInstallLog, null, tfmFileManager, null);
			IList<IniEdit> lstIniEdits = ModInstallLog.GetInstalledIniEdits(p_modMod);
			foreach (IniEdit iniEdit in lstIniEdits)
				iniIniInstaller.UneditIni(iniEdit.File, iniEdit.Section, iniEdit.Key);

			RemoveIniEdits(p_modMod);

			m_tslVirtualModInfo.RemoveAll(x => VirtualModInfoMatchesMod(x, p_modMod, modFileName));

			if (!p_booPurging)
				SaveList(true);

			if (GameMode.RequiresModFileMerge)
			{
				List<IMod> ActiveMods;
				ActiveMods = ModManager.ActiveMods.Where(x => ActiveModList.Contains(Path.GetFileName(x.Filename), StringComparer.CurrentCultureIgnoreCase)).ToList();
				GameMode.ModFileMerge(ActiveMods, p_modMod, true);
			}

			return true;
		}

		private static void ReportVirtualDisableProgress(Action<VirtualModDisableProgress> p_actProgress, string p_strMessage, int p_intCurrent, int p_intTotal)
		{
			if (p_actProgress != null)
				p_actProgress(new VirtualModDisableProgress(p_strMessage, p_intCurrent, p_intTotal));
		}

		public void FinalizeModDeactivation(IMod p_modMod)
		{
			TxFileManager tfmFileManager = new TxFileManager();
			IIniInstaller iniIniInstaller = new IniInstaller(p_modMod, ModInstallLog, null, tfmFileManager, null);
			IList<IniEdit> lstIniEdits = ModInstallLog.GetInstalledIniEdits(p_modMod);
			foreach (IniEdit iniEdit in lstIniEdits)
				iniIniInstaller.UneditIni(iniEdit.File, iniEdit.Section, iniEdit.Key);

			RemoveIniEdits(p_modMod);

			string modFileName = Path.GetFileName(p_modMod.Filename);
			m_tslVirtualModInfo.RemoveAll(x => VirtualModInfoMatchesMod(x, p_modMod, modFileName));

			SaveList(true);

			if (GameMode.RequiresModFileMerge)
			{
				List<IMod> ActiveMods;
				ActiveMods = ModManager.ActiveMods.Where(x => ActiveModList.Contains(Path.GetFileName(x.Filename), StringComparer.CurrentCultureIgnoreCase)).ToList();
				GameMode.ModFileMerge(ActiveMods, p_modMod, true);
			}
		}

		public void EnableMod(IMod p_modMod)
		{
			ModInstallRoot installRoot = ModInstallLog == null ? ModInstallRoot.Default : ModInstallLog.GetModInstallRoot(p_modMod);
			string strVirtualFolderPath = Path.Combine(m_strVirtualActivatorPath, Path.GetFileNameWithoutExtension(p_modMod.Filename));
			string strLinkFolderPath = string.Empty;

			if (MultiHDMode)
				strLinkFolderPath = Path.Combine(HDLinkFolder, Path.GetFileNameWithoutExtension(p_modMod.Filename));

			m_booDisableIniLogging = true;

			if (Directory.Exists(strVirtualFolderPath) || (MultiHDMode && Directory.Exists(strLinkFolderPath)))
			{
				List<string> lstFiles = Directory.GetFiles(strVirtualFolderPath, "*", SearchOption.AllDirectories).ToList();
				if (MultiHDMode)
					lstFiles.AddRange(Directory.GetFiles(strLinkFolderPath, "*", SearchOption.AllDirectories));


				IModLinkInstaller ModLinkInstaller = GetModLinkInstaller();
				List<string> deployedPluginPaths = new List<string>();

				foreach (string File in lstFiles)
				{
					string strFile = File.Replace((strVirtualFolderPath + Path.DirectorySeparatorChar), string.Empty);
					if (Path.IsPathRooted(strFile))
						strFile = File.Replace((strLinkFolderPath + Path.DirectorySeparatorChar), string.Empty);

					string strFileLink = ModLinkInstaller.AddFileLink(p_modMod, strFile, File, false, false, installRoot);

					if (!string.IsNullOrEmpty(strFileLink) &&
						PluginManager != null &&
						PluginManager.IsActivatiblePluginFile(strFileLink))
					{
						deployedPluginPaths.Add(strFileLink);
					}
				}

				if (PluginManager != null && deployedPluginPaths.Count > 0)
					PluginManager.IntegrateDeployedPlugins(deployedPluginPaths);

				LoadIniEdits(p_modMod);
				SaveList(true);
			}
			m_booDisableIniLogging = false;
		}

		public void FinalizeModActivation(IMod p_modMod)
		{
			if (GameMode.RequiresModFileMerge)
			{
				List<IMod> ActiveMods;
				ActiveMods = ModManager.ActiveMods.Where(x => !x.Filename.Equals(p_modMod.Filename, StringComparison.CurrentCultureIgnoreCase) && ActiveModList.Contains(Path.GetFileName(x.Filename), StringComparer.CurrentCultureIgnoreCase)).ToList();
				GameMode.ModFileMerge(ActiveMods, p_modMod, false);
			}

			LoadIniEdits(p_modMod);
			SaveList(true);
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

			using (StreamWriter streamWriter = File.CreateText(m_strVirtualActivatorIniEditsPath))
			{
				docIniEdits.Save(streamWriter);
			}
		}

		public void LoadIniEdits(IMod p_modMod)
		{
			XDocument docIniEdits;

			if (File.Exists(m_strVirtualActivatorIniEditsPath))
			{
				docIniEdits = XDocument.Load(m_strVirtualActivatorIniEditsPath);

				string strVersion = docIniEdits.Element("virtualModActivator").Attribute("fileVersion").Value;
				if (!(CURRENT_VERSION.ToString() == strVersion))
					throw new Exception(string.Format("Invalid Ini Edits version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

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

		public void RestoreIniEdits()
		{
			XDocument docIniEdits;

			if (File.Exists(m_strVirtualActivatorIniEditsPath))
			{
				docIniEdits = XDocument.Load(m_strVirtualActivatorIniEditsPath);

				string strVersion = docIniEdits.Element("virtualModActivator").Attribute("fileVersion").Value;
				if (!(CURRENT_VERSION.ToString() == strVersion))
					throw new Exception(string.Format("Invalid Ini Edits version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

				try
				{
					XElement xelIniEdits = docIniEdits.Descendants("iniEdits").FirstOrDefault();
					if ((xelIniEdits != null) && xelIniEdits.HasElements)
					{
						List<XElement> xelEdits = xelIniEdits.Elements("iniEdit").ToList();

						if ((xelEdits != null) && (xelEdits.Count > 0))
						{
							TxFileManager tfmFileManager = new TxFileManager();
							IMod modLast = null;
							IMod modMod = null;
							IIniInstaller iniIniInstaller = null;

							foreach (XElement xelEdit in xelEdits)
							{
								string strIniFile = xelEdit.Attribute("iniFile").Value;
								string strIniSection = xelEdit.Element("iniSection").Value;
								string strIniKey = xelEdit.Element("iniKey").Value;
								string strIniValue = xelEdit.Element("iniValue").Value;
								string strModFile = xelEdit.Element("modFile").Value;

								if ((modLast != null) && (strModFile.ToLowerInvariant() == modLast.Filename.ToLowerInvariant()))
									modMod = modLast;
								else
								{
									modMod = ModManager.ManagedMods.Where(x => x.Filename.ToLowerInvariant() == strModFile.ToLowerInvariant()).FirstOrDefault();
									if (modMod != null)
										iniIniInstaller = new IniInstaller(modMod, ModInstallLog, null, tfmFileManager, null);
								}

								if (modMod != null)
								{
									iniIniInstaller.EditIni(strIniFile, strIniSection, strIniKey, strIniValue);
								}
							}
						}
					}
				}
				catch { }
			}
		}

		public void PurgeIniEdits()
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
						List<XElement> xelEdits = xelIniEdits.Elements("iniEdit").ToList();

						if ((xelEdits != null) && (xelEdits.Count > 0))
						{
							TxFileManager tfmFileManager = new TxFileManager();

							foreach (string strFilename in xelEdits.Select(x => x.Element("modFile").Value).Distinct())
							{
								IMod modMod = ModManager.ManagedMods.Where(x => x.Filename == strFilename).FirstOrDefault();
								if (modMod != null)
								{
									IIniInstaller iniIniInstaller = new IniInstaller(modMod, ModInstallLog, null, tfmFileManager, null);
									IList<IniEdit> lstIniEdits = ModInstallLog.GetInstalledIniEdits(modMod);
									foreach (IniEdit iniEdit in lstIniEdits)
										iniIniInstaller.UneditIni(iniEdit.File, iniEdit.Section, iniEdit.Key);
								}
							}
						}
					}

					FileUtil.ForceDelete(m_strVirtualActivatorIniEditsPath);
				}
				catch { }
			}
		}

		private void RemoveIniEdits(IMod p_modMod)
		{
			RemoveIniEdits(p_modMod, m_strVirtualActivatorIniEditsPath);
		}

		private void RemoveIniEdits(IMod p_modMod, string p_strPath)
		{
			XDocument docIniEdits;

			if (File.Exists(p_strPath))
			{
				docIniEdits = XDocument.Load(p_strPath);

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

					using (StreamWriter streamWriter = File.CreateText(p_strPath))
					{
						docIniEdits.Save(streamWriter);
					}
				}
				catch { }
			}
		}

		public void ImportIniEdits(string p_strIniXML)
		{
			XDocument docVirtual = XDocument.Parse(p_strIniXML);

			if (File.Exists(m_strVirtualActivatorIniEditsPath))
				FileUtil.ForceDelete(m_strVirtualActivatorIniEditsPath);

			using (StreamWriter streamWriter = File.CreateText(m_strVirtualActivatorIniEditsPath))
			{
				docVirtual.Save(streamWriter);
			}
		}

		#endregion

		public void SetNewFolders(string p_strVirtual, string p_strLink, bool? p_booMultiHD)
		{
			if (p_booMultiHD != null)
				NewMultiHD = (bool)p_booMultiHD;
			NewVirtualFolder = p_strVirtual;
			NewLinkFolder = p_strLink;
		}

		/// <summary>
		/// Check the integrity of the List of links.
		/// </summary>
		/// <param name="p_ivlVirtualLinks">The IVirtualModLink list.</param>
		/// <param name="p_lstMissingModInfo">The IVirtualModInfo list.</param>
		/// <param name="p_lstForced">Forced param.</param>
		public void CheckLinkListIntegrity(IList<IVirtualModLink> p_ivlVirtualLinks, out List<IVirtualModInfo> p_lstMissingModInfo, IList<string> p_lstForced)
		{
			p_lstMissingModInfo = new List<IVirtualModInfo>();

			if (p_ivlVirtualLinks != null)
			{
				foreach (IVirtualModLink ivlModLink in p_ivlVirtualLinks)
				{
					string strBaseFileCheck = Path.Combine(VirtualFoder, ivlModLink.RealModPath);

					if (!File.Exists(strBaseFileCheck) || (MultiHDMode && (string.IsNullOrEmpty(HDLinkFolder) || (!File.Exists(Path.Combine(HDLinkFolder, ivlModLink.RealModPath))))))
					{
						IVirtualModInfo vmiModInfo = ivlModLink.ModInfo;
						if (!p_lstMissingModInfo.Contains(vmiModInfo))
							p_lstMissingModInfo.Add(vmiModInfo);
					}
				}
			}
		}

		public IModLinkInstaller GetModLinkInstaller()
		{
			return new ModLinkInstaller(this);
		}

		public void PurgeMods(List<IMod> p_lstMods, string p_strPath)
		{
			List<IVirtualModLink> lstVirtualLinks = new List<IVirtualModLink>();
			List<IVirtualModInfo> lstVirtualMods = new List<IVirtualModInfo>();
			List<string> lstPlugins = new List<string>();
			string strProfilePath = Path.Combine(p_strPath, "modlist.xml");
			LoadListOnDemand(strProfilePath, out lstVirtualLinks, out lstVirtualMods);

			if (lstVirtualLinks.Count > 0 && lstVirtualMods.Count > 0)
			{
				foreach (IMod modMod in p_lstMods)
				{
					if (GameMode.UsesPlugins)
					{
						List<IVirtualModLink> ivlLinks = lstVirtualLinks.Where(x => x.ModInfo.ModFileName.ToLowerInvariant() == Path.GetFileName(modMod.Filename).ToLowerInvariant() && (Path.GetExtension(x.VirtualModPath).ToLowerInvariant() == ".esp" || Path.GetExtension(x.VirtualModPath).ToLowerInvariant() == ".esl" || Path.GetExtension(x.VirtualModPath).ToLowerInvariant() == ".esm")).ToList();
						if (ivlLinks != null)
						{
							foreach (IVirtualModLink Link in ivlLinks)
							{
								string strPlugin = Path.GetFileName(Link.VirtualModPath).ToLowerInvariant();
								List<IVirtualModLink> ivlPlugins = lstVirtualLinks.Where(x => Path.GetFileName(x.VirtualModPath).ToLowerInvariant() == strPlugin && x.ModInfo.ModFileName.ToLowerInvariant() != Path.GetFileName(modMod.Filename).ToLowerInvariant()).ToList();
								if ((ivlPlugins == null) || (ivlPlugins.Count == 0))
									lstPlugins.Add(strPlugin);
							}
						}

						//remove from ini
						string strPath = Path.Combine(p_strPath, ACTIVATOR_INIEDITS);
						RemoveIniEdits(modMod, strPath);
					}

					//remove from modlist
					lstVirtualLinks.RemoveAll(x => x.ModInfo.ModFileName.ToLowerInvariant() == Path.GetFileName(modMod.Filename).ToLowerInvariant());
					lstVirtualMods.RemoveAll(x => x.ModFileName.ToLowerInvariant() == Path.GetFileName(modMod.Filename).ToLowerInvariant());
				}

				if (GameMode.UsesPlugins && lstPlugins.Count > 0)
				{
					//remove loadorder
					string strLoadorderPath = Path.Combine(p_strPath, "loadorder.txt");
					File.WriteAllLines(strLoadorderPath, File.ReadLines(strLoadorderPath).Where(l => !lstPlugins.Contains(l.Substring(0, l.IndexOf('=')).ToLowerInvariant())).ToList());
				}

				//save list
				string strModListPath = Path.Combine(p_strPath, "modlist.xml");
				SaveModList(strModListPath, lstVirtualMods, lstVirtualLinks);
			}
		}

		private bool CheckIsModActive(IMod p_modMod)
		{
			if (m_tslVirtualModInfo == null || m_tslVirtualModInfo.Count == 0 || p_modMod == null)
				return false;

			string modFileName = Path.GetFileName(p_modMod.Filename);
			return m_tslVirtualModInfo.Any(x => VirtualModInfoMatchesMod(x, p_modMod, modFileName));
		}

		public bool CheckHasActiveLinks(IMod p_modMod)
		{
			if (p_modMod == null || m_tslVirtualModList == null || m_tslVirtualModList.Count == 0)
				return false;

			string strModFileName = Path.GetFileName(p_modMod.Filename);
			return m_tslVirtualModList.Any(x => x != null && x.Active && VirtualModLinkMatchesMod(x, p_modMod, strModFileName));
		}

		private static bool VirtualModInfoMatchesMod(IVirtualModInfo p_vmiModInfo, IMod p_modMod, string p_strModFileName)
		{
			if (p_vmiModInfo == null || p_modMod == null)
				return false;

			if (!string.IsNullOrEmpty(p_vmiModInfo.ModFileName) && !string.IsNullOrEmpty(p_strModFileName) && p_vmiModInfo.ModFileName.Equals(p_strModFileName, StringComparison.InvariantCultureIgnoreCase))
				return true;

			if (!string.IsNullOrEmpty(p_vmiModInfo.DownloadId) && !string.IsNullOrEmpty(p_modMod.DownloadId) && p_vmiModInfo.DownloadId.Equals(p_modMod.DownloadId, StringComparison.InvariantCultureIgnoreCase))
				return true;

			if (!string.IsNullOrEmpty(p_vmiModInfo.UpdatedDownloadId) && !string.IsNullOrEmpty(p_modMod.DownloadId) && p_vmiModInfo.UpdatedDownloadId.Equals(p_modMod.DownloadId, StringComparison.InvariantCultureIgnoreCase))
				return true;

			return false;
		}

		private static bool VirtualModLinkMatchesMod(IVirtualModLink p_vmlLink, IMod p_modMod, string p_strModFileName)
		{
			if (p_vmlLink == null)
				return false;

			return VirtualModInfoMatchesMod(p_vmlLink.ModInfo, p_modMod, p_strModFileName);
		}

		/// <summary>
		/// Deletes any empty directories found between the start path and the end directory.
		/// </summary>
		/// <param name="p_strStartPath">The path from which to start looking for empty directories.</param>
		/// <param name="p_strStopDirectory">The directory at which to stop looking.</param>
		protected void TrimEmptyDirectories(string p_strStartPath, string p_strStopDirectory)
		{
			string strEmptyDirectory = p_strStartPath;
			while (!string.IsNullOrEmpty(strEmptyDirectory) &&
				!strEmptyDirectory.Equals(p_strStopDirectory, StringComparison.OrdinalIgnoreCase))
			{
				if (!Directory.Exists(strEmptyDirectory) ||
					Directory.EnumerateFileSystemEntries(strEmptyDirectory).Any())
					break;

				for (int i = 0; i < 5 && Directory.Exists(strEmptyDirectory); i++)
					FileUtil.ForceDelete(strEmptyDirectory);

				strEmptyDirectory = Path.GetDirectoryName(strEmptyDirectory);
			}
		}

		public string GetCurrentFileOwner(string p_strPath)
		{
			string strOwner = string.Empty;

			if ((VirtualLinks != null) && (VirtualLinks.Count > 0))
			{
				string strFile = Path.GetFileName(p_strPath);
				IVirtualModLink vmlLink = VirtualLinks.Find(x => Path.GetFileName(x.VirtualModPath).Equals(strFile, StringComparison.CurrentCultureIgnoreCase) && (x.Active == true));
				if (vmlLink != null)
					strOwner = vmlLink.ModInfo.ModName;
			}

			return strOwner;
		}

		public bool OverwriteLooseFile(string p_strFilePath, string p_strModFileName)
		{
			return OverwriteLooseFile(p_strFilePath, p_strModFileName, ModInstallRoot.Default);
		}

		public bool OverwriteLooseFile(string p_strFilePath, string p_strModFileName, ModInstallRoot p_mirInstallRoot)
		{
			try
			{
				string strSource = Path.Combine(GetInstallRootPath(p_mirInstallRoot), p_strFilePath);
				string strDest = Path.Combine(m_strVirtualActivatorOverwritePath, Path.GetFileNameWithoutExtension(p_strModFileName), p_strFilePath);

				if (File.Exists(strSource))
				{
					string strDestFolder = Path.GetDirectoryName(strDest);
					if (!Directory.Exists(strDestFolder))
					{
						Directory.CreateDirectory(strDestFolder);
					}

					if (Directory.Exists(strDestFolder))
						File.Move(strSource, strDest);
				}

				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_rolModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask ActivatingMod(IMod p_modMod, bool p_booDisabling, ConfirmActionMethod p_camConfirm)
		{
			if (p_booDisabling)
				if (!CheckIsModActive(p_modMod))
					return null;

			ModInstallRoot installRoot = p_booDisabling || ModInstallLog == null ? ModInstallRoot.Default : ModInstallLog.GetModInstallRoot(p_modMod);
			LinkActivationTask latActivatingMod = new LinkActivationTask(PluginManager, this, new VirtualDeploymentService(this), p_modMod, p_booDisabling, p_camConfirm, installRoot);
			if (GameMode.LoadOrderManager != null)
				GameMode.LoadOrderManager.MonitorExternalTask(latActivatingMod);
			else
				latActivatingMod.Update(p_camConfirm);
			return latActivatingMod;
		}

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_lstFiles">The file list.</param>
		/// <param name="p_mprProfile">The profile.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask FixConfigFiles(List<string> p_lstFiles, IModProfile p_mprProfile, ConfirmActionMethod p_camConfirm)
		{
			VirtualConfigFixTask vcfConfigFixTask = new VirtualConfigFixTask(p_lstFiles, ModManager, this, p_mprProfile);
			vcfConfigFixTask.Update(p_camConfirm);
			return vcfConfigFixTask;
		}

		#endregion

		private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
			where T : class
		{
			public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

			public bool Equals(T x, T y)
			{
				return ReferenceEquals(x, y);
			}

			public int GetHashCode(T obj)
			{
				return obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
			}
		}
		#region VirtualModInfo EqualityComparer

		private class VirtualModInfoEqualityComparer : IEqualityComparer<IVirtualModInfo>
		{
			public bool Equals(IVirtualModInfo x, IVirtualModInfo y)
			{
				if (x == null || y == null)
					return false;
				if (ReferenceEquals(x, y))
					return true;
				return (x.ModFileName.Equals(y.ModFileName, StringComparison.InvariantCultureIgnoreCase));
			}

			public int GetHashCode(IVirtualModInfo obj)
			{
				return obj.ModFileName.GetHashCode();
			}
		}

		#endregion

		#region Registry

		/// <summary>
		/// Check the correct path on the Registry.
		/// </summary>
		/// <param name="strGameMode">The selected game mode.</param>
		/// <param name="strMods">The selected Mods path.</param>
		/// <param name="strInstallInfo">The selected Install Info path.</param>
		public void SaveRegistry(string p_strGameMode, string p_strVirtual, string p_strHDLink, bool p_booMultiHDInstall)
		{
			try
			{
				RegistryKey rkKey = null;
				string strNMMKey = @"SOFTWARE\NexusModManager\";
				string strGameKey = @"SOFTWARE\NexusModManager\" + p_strGameMode;

				if (RegistryUtil.CanReadKey(strNMMKey) && RegistryUtil.CanWriteKey(strNMMKey))
				{
					rkKey = Registry.LocalMachine.OpenSubKey(strNMMKey, true);
					if (rkKey == null)
						if (RegistryUtil.CanCreateKey(strNMMKey))
							Registry.LocalMachine.CreateSubKey(strNMMKey);
				}

				if (rkKey != null)
				{
					if (RegistryUtil.CanCreateKey(strGameKey))
						Registry.LocalMachine.CreateSubKey(strGameKey);

					if (RegistryUtil.CanReadKey(strGameKey) && RegistryUtil.CanWriteKey(strGameKey))
					{
						rkKey = Registry.LocalMachine.OpenSubKey(strGameKey, true);
						rkKey.SetValue("Virtual", p_strVirtual);
						rkKey.SetValue("HDLink", p_strHDLink);
						rkKey.SetValue("MultiHDInstall", p_booMultiHDInstall);
					}
				}
			}
			catch
			{
				return;
			}
		}

		#endregion
	}
}
