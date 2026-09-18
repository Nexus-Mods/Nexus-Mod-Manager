namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Forms;
    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.DownloadMonitoring;
    using Nexus.Client.Games;
    using Nexus.Client.ModActivationMonitoring;
    using Nexus.Client.ModAuthoring;
    using Nexus.Client.ModManagement.InstallationLog;
    using Nexus.Client.ModManagement.Operations;
    using Nexus.Client.ModManagement.Scripting;
    using Nexus.Client.ModRepositories;
    using Nexus.Client.Mods;
    using Nexus.Client.Mods.Formats.FOMod;
    using Nexus.Client.PluginManagement;
    using Nexus.Client.SSO;
    using Nexus.Client.Settings;
    using Nexus.Client.UI;
    using Nexus.Client.Util;
    using Nexus.Client.Util.Collections;
    using Nexus.Client.Util.Localization;

    /// <summary>
    /// The class the encapsulates managing mods.
    /// </summary>
    /// <remarks>
    /// The list of managed mods needs to be centralized to ensure integrity; having multiple mod managers, each
    /// with a potentially different list of managed mods, would be disastrous. As such, this
    /// object is a singleton to help enforce that policy.
    /// Note, however, that the singleton nature of the manager is not meant to provide global access to the object.
    /// As such, there is no static accessor to retrieve the singleton instance. Instead, the
    /// <see cref="Initialize"/> method returns the only instance that should be used.
    /// </remarks>
    public partial class ModManager
	{
		#region Singleton

		private static ModManager m_mmgCurrent = null;

		/// <summary>
		/// Initializes the singleton intances of the mod manager.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_mrpModRepository">The mod repository from which to get mods and mod metadata.</param>
		/// <param name="p_dmrMonitor">The download monitor to use to track task progress.</param>
		/// <param name="p_mamMonitor">The mod activation monitor to use to track task progress.</param>
		/// <param name="p_frgFormatRegistry">The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</param>
		/// <param name="p_mrgModRegistry">The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_ilgInstallLog">The install log tracking mod activations for the current game mode.</param>
		/// <param name="p_pmgPluginManager">The plugin manager to use to work with plugins.</param>
		/// <returns>The initialized mod manager.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the mod manager has already
		/// been initialized.</exception>
		public static ModManager Initialize(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IModRepository p_mrpModRepository, DownloadMonitor p_dmrMonitor, ModActivationMonitor p_mamMonitor, IModFormatRegistry p_frgFormatRegistry, ModRegistry p_mrgModRegistry, IModCacheManager p_mcmModCacheManager, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgInstallLog, IPluginManager p_pmgPluginManager)
		{
			if (m_mmgCurrent != null)
				throw new InvalidOperationException("The Mod Manager has already been initialized.");
			m_mmgCurrent = new ModManager(p_gmdGameMode, p_eifEnvironmentInfo, p_mrpModRepository, p_dmrMonitor, p_mamMonitor, p_frgFormatRegistry, p_mrgModRegistry, p_mcmModCacheManager, p_futFileUtility, p_scxUIContext, p_ilgInstallLog, p_pmgPluginManager);
			return m_mmgCurrent;
		}

		/// <summary>
		/// This disposes of the singleton object, allowing it to be re-initialized.
		/// </summary>
		public void Release()
		{
			DetachSortOrderIdentityTracking();
			ModAdditionQueue.Dispose();
			ModAdditionQueue = null;
			m_mmgCurrent = null;
		}

		#endregion

		private ModActivator m_macModActivator = null;
		private VirtualModActivator m_vmaVirtualModActivator = null;
		private IModDeploymentManager m_mdmDeploymentManager = null;
		private ReadMeManager m_rmmReadMeManager = null;
		private readonly FileUtil m_futFileUtility;
		private readonly SynchronizationContext m_scxUIContext;
		private readonly IPluginManager m_pmgPluginManager;
		private IProfileManager m_ipmProfileManager;
		private readonly HashSet<IMod> m_setSortOrderTrackedManagedMods = new HashSet<IMod>(ModReferenceEqualityComparer.Instance);

		#region events

		public event EventHandler<EventArgs<IBackgroundTask>> UpdateCheckStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> UpdateCategoriesCheckStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> AutomaticDownloadStarted = delegate { };

		#endregion

		#region Properties

		/// <summary>
		/// The loginform Task.
		/// </summary>
		public AuthenticationFormTask LoginTask;

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		public IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		public IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the mod repository from which to get mods and mod metadata.
		/// </summary>
		/// <value>The mod repository from which to get mods and mod metadata.</value>
		public IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the mod auto updater.
		/// </summary>
		/// <value>The mod auto updater.</value>
		public AutoUpdater AutoUpdater { get; private set; }

		/// <summary>
		/// Gets the mod auto updater.
		/// </summary>
		/// <value>The mod auto updater.</value>
		protected ModActivator Activator
		{
			get
			{
				return m_macModActivator ?? (m_macModActivator = new ModActivator(InstallationLog, InstallerFactory));
			}
		}

		/// <summary>
		/// Gets the download monitor to use to display status.
		/// </summary>
		/// <value>The download monitor to use to display status.</value>
		protected DownloadMonitor DownloadMonitor { get; private set; }

		/// <summary>
		/// Gets the mod activation monitor to use to display status.
		/// </summary>
		/// <value>The mod activation monitor to use to display status.</value>
		public ModActivationMonitor ModActivationMonitor { get; private set; }

		/// <summary>
		/// Gets the <see cref="ModInstallerFactory"/> to use to create
		/// <see cref="ModInstaller"/>s.
		/// </summary>
		/// <value>The <see cref="ModInstallerFactory"/> to use to create
		/// <see cref="ModInstaller"/>s.</value>
		protected ModInstallerFactory InstallerFactory { get; private set; }

		/// <summary>
		/// Gets the install log tracking mod activations for the current game mode.
		/// </summary>
		/// <value>The install log tracking mod activations for the current game mode.</value>
		public IInstallLog InstallationLog { get; private set; }

		/// <summary>
		/// Gets the <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.
		/// </summary>
		/// <value>The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</value>
		protected IModFormatRegistry FormatRegistry { get; private set; }

		/// <summary>
		/// Gets the <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.
		/// </summary>
		/// <value>The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</value>
		protected ModRegistry ManagedModRegistry { get; private set; }

		/// <summary>
		/// Gets the cache manager for the current game mode.
		/// </summary>
		protected IModCacheManager ModCacheManager { get; private set; }

		/// <summary>
		/// Gets the Sort-assignment service bound to the current game storage, or <c>null</c> if its supplemental store is unavailable.
		/// </summary>
		public ModSortOrderService SortOrderService { get; private set; }

		/// <summary>
		/// Gets the <see cref="AddModQueue"/> that contains the list
		/// of <see cref="IMod"/>s to be added to the mod manager.
		/// </summary>
		/// <value>The <see cref="AddModQueue"/> that contains the list
		/// of <see cref="IMod"/>s to be added to the mod manager.</value>
		protected AddModQueue ModAdditionQueue { get; private set; }

		/// <summary>
		/// Gets the current Virtual Mod Activator.
		/// </summary>
		/// <value>The current Virtual Mod Activator.</value>
		public VirtualModActivator VirtualModActivator
		{
			get
			{
				return m_vmaVirtualModActivator;
			}
		}

		/// <summary>
		/// Gets the method-neutral deployment coordinator.
		/// </summary>
		public IModDeploymentManager DeploymentManager
		{
			get
			{
				return m_mdmDeploymentManager;
			}
		}

		/// <summary>
		/// Gets the newest available information about the managed mods.
		/// </summary>
		/// <value>The newest available information about the managed mods.</value>
		public ReadOnlyObservableList<AutoUpdater.UpdateInfo> NewestModInfo
		{
			get
			{
				return AutoUpdater.NewestModInfo;
			}
		}

		/// <summary>
		/// Gets the list of supported mod formats.
		/// </summary>
		/// <value>The list of supported mod formats.</value>
		public ICollection<IModFormat> ModFormats
		{
			get
			{
				return FormatRegistry.Formats;
			}
		}

		/// <summary>
		/// Gets the newest available information about the managed mods.
		/// </summary>
		/// <value>The newest available information about the managed mods.</value>
		public ReadMeManager ReadMeManager
		{
			get
			{
				return m_rmmReadMeManager;
			}
		}

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<IMod> ManagedMods
		{
			get
			{
				return ManagedModRegistry.RegisteredMods;
			}
		}

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<IMod> ActiveMods
		{
			get
			{
				return InstallationLog.ActiveMods;
			}
		}

		/// <summary>
		/// Gets whether the repository is in offline mode.
		/// </summary>
		/// <value>Whether the repository is in offline mode.</value>
		public bool RepositoryOfflineMode
		{
			get
			{
				return ModRepository.IsOffline;
			}
		}

		/// <summary>
		/// Gets the current game mode Mod directory.
		/// </summary>
		/// <value>The current game mode Mod directory.</value>
		public string CurrentGameModeModDirectory
		{
			get
			{
				return GameMode.GameModeEnvironmentInfo.ModDirectory;
			}
		}

		/// <summary>
		/// Gets the current game mode default categories.
		/// </summary>
		/// <value>The current game mode default categories.</value>
		public string CurrentGameModeDefaultCategories
		{
			get
			{
				return GameMode.GameDefaultCategories;
			}
		}

		/// <summary>
		/// Gets whether the required tool for the current game mode is missing.
		/// </summary>
		/// <value>Whether the required tool for the current game mode is missing.</value>
		public string RequiredToolErrorMessage
		{
			get
			{
				if (GameMode.OrderedRequiredToolFileNames != null)
					if (String.IsNullOrEmpty(GameMode.InstallationPath))
						return GameMode.RequiredToolErrorMessage;
				return String.Empty;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_mrpModRepository">The mod repository from which to get mods and mod metadata.</param>
		/// <param name="p_dmrMonitor">The download monitor to use to track task progress.</param>
		/// <param name="p_mamMonitor">The mod activation monitor to use to track task progress.</param>
		/// <param name="p_frgFormatRegistry">The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</param>
		/// <param name="p_mdrManagedModRegistry">The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_ilgInstallLog">The install log tracking mod activations for the current game mode.</param>
		/// <param name="p_pmgPluginManager">The plugin manager to use to work with plugins.</param>
		private ModManager(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IModRepository p_mrpModRepository, DownloadMonitor p_dmrMonitor, ModActivationMonitor p_mamMonitor, IModFormatRegistry p_frgFormatRegistry, ModRegistry p_mdrManagedModRegistry, IModCacheManager p_mcmModCacheManager, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgInstallLog, IPluginManager p_pmgPluginManager)
		{
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			m_rmmReadMeManager = new ReadMeManager(EnvironmentInfo.Settings.ModFolder[GameMode.ModeId]);
			ModRepository = p_mrpModRepository;
			FormatRegistry = p_frgFormatRegistry;
			ManagedModRegistry = p_mdrManagedModRegistry;
			ModCacheManager = p_mcmModCacheManager;
			InstallationLog = p_ilgInstallLog;
			m_futFileUtility = p_futFileUtility;
			m_scxUIContext = p_scxUIContext;
			m_pmgPluginManager = p_pmgPluginManager;
			m_vmaVirtualModActivator = new VirtualModActivator(this, p_pmgPluginManager, p_gmdGameMode, p_ilgInstallLog, p_eifEnvironmentInfo, EnvironmentInfo.Settings.ModFolder[GameMode.ModeId]);
			m_vmaVirtualModActivator.Initialize();
			m_mdmDeploymentManager = new ModDeploymentManager(p_ilgInstallLog, m_vmaVirtualModActivator, p_gmdGameMode);
			InstallerFactory = new ModInstallerFactory(p_gmdGameMode, p_eifEnvironmentInfo, p_futFileUtility, p_scxUIContext, p_ilgInstallLog, p_pmgPluginManager, m_vmaVirtualModActivator, m_mdmDeploymentManager);
			DownloadMonitor = p_dmrMonitor;
			ModActivationMonitor = p_mamMonitor;
			InitializeSortOrderService();
			ModAdditionQueue = new AddModQueue(p_eifEnvironmentInfo, this);
			AutoUpdater = new AutoUpdater(p_mrpModRepository, p_mdrManagedModRegistry, p_eifEnvironmentInfo);
			LoginTask = new AuthenticationFormTask(this);
		}

		/// <summary>
		/// Initializes the current-archive inventory first, then resolves passive startup assignments against that complete inventory.
		/// </summary>
		private void InitializeSortOrderService()
		{
			try
			{
				var store = new ModSortOrderStore(ModCacheManager.ModCacheDirectory, GameMode.GameModeEnvironmentInfo.ModDirectory);
				if (!store.IsUsable)
				{
					Trace.TraceWarning("Mod Sort assignments are unavailable because the supplemental SQLite store could not be initialized.");
					return;
				}
				SortOrderService = new ModSortOrderService(store);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Unable to initialize Mod Sort assignments: {0}", ex);
				SortOrderService = null;
				return;
			}

			var managedMods = GetManagedModsSnapshot();
			var bindingProtectors = GetSortOrderBindingProtectors();
			SortOrderService.RebuildCurrentArchiveInventory(managedMods, bindingProtectors);

			foreach (var mod in managedMods)
			{
				ResolveStartupSortOrder(mod, managedMods);
			}
			RebuildSortOrderManagedIdentityTracking(managedMods);

			foreach (var mod in bindingProtectors)
			{
				ResolveStartupSortOrder(mod, managedMods);
			}

			ManagedModRegistry.RegisteredMods.CollectionChanged += ManagedMods_SortOrderCollectionChanged;
			InstallationLog.ActiveMods.CollectionChanged += ActiveMods_SortOrderCollectionChanged;
		}

		/// <summary>
		/// Resolves one passive-discovery assignment without allowing a Sort failure to prevent application startup.
		/// </summary>
		private void ResolveStartupSortOrder(IMod mod, IEnumerable<IMod> managedMods)
		{
			try
			{
				SortOrderService.Resolve(mod, ModSortOrderAssignmentContext.StartupOrDiscovery, managedMods);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Unable to resolve startup Mod Sort assignment for {0}: {1}", mod?.ModArchivePath, ex);
			}
		}

		/// <summary>
		/// Maintains current managed-archive bindings incrementally; Reset performs the only full inventory reconstruction.
		/// </summary>
		private void ManagedMods_SortOrderCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (SortOrderService == null)
			{
				return;
			}

			if (e.Action == NotifyCollectionChangedAction.Reset)
			{
				RebuildSortOrderCurrentArchiveState();
				return;
			}

			var newMods = new List<IMod>();
			if (e.NewItems != null)
			{
				foreach (var item in e.NewItems)
				{
					if (item is IMod mod)
					{
						newMods.Add(mod);
						TrackSortOrderManagedIdentity(mod);
						SortOrderService.TrackManagedMod(mod);
					}
				}
			}

			if (e.OldItems != null)
			{
				foreach (var item in e.OldItems)
				{
					if (item is IMod mod && !ContainsReference(newMods, mod))
					{
						UntrackSortOrderManagedIdentity(mod);
						SortOrderService.UntrackManagedMod(mod);
					}
				}
			}

			foreach (var mod in newMods)
			{
				ResolveStartupSortOrder(mod, ManagedModRegistry.RegisteredMods);
			}
		}

		/// <summary>
		/// Tracks active placeholders separately so they may protect bindings without ever becoming MIN donors.
		/// </summary>
		private void ActiveMods_SortOrderCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (SortOrderService == null)
			{
				return;
			}

			if (e.Action == NotifyCollectionChangedAction.Reset)
			{
				RefreshSortOrderBindingProtectors();
				return;
			}

			var newProtectors = new List<IMod>();
			if (e.NewItems != null)
			{
				foreach (var item in e.NewItems)
				{
					if (item is IMod mod && IsSortOrderBindingProtector(mod))
					{
						newProtectors.Add(mod);
						SortOrderService.TrackBindingProtector(mod);
					}
				}
			}

			if (e.OldItems != null)
			{
				foreach (var item in e.OldItems)
				{
					if (item is IMod mod && IsSortOrderBindingProtector(mod) && !ContainsReference(newProtectors, mod))
					{
						SortOrderService.UntrackBindingProtector(mod);
					}
				}
			}

			foreach (var mod in newProtectors)
			{
				ResolveStartupSortOrder(mod, ManagedModRegistry.RegisteredMods);
			}
		}

		/// <summary>
		/// Reconciles newly available repository identity for pending and explicit assignments; only pending Add rows may inherit.
		/// </summary>
		private void ManagedMod_SortOrderIdentityChanged(object sender, PropertyChangedEventArgs e)
		{
			if (SortOrderService == null || (e.PropertyName != nameof(IMod.Id) && e.PropertyName != nameof(IMod.DownloadId)))
			{
				return;
			}

			var mod = sender as IMod;
			if (mod == null)
			{
				return;
			}

			try
			{
				SortOrderService.ReconcileIdentity(mod);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Unable to reconcile Mod Sort identity for {0}: {1}", mod.ModArchivePath, ex);
			}
		}

		/// <summary>
		/// Rebuilds the two-phase current archive inventory after a managed-registry Reset.
		/// </summary>
		private void RebuildSortOrderCurrentArchiveState()
		{
			var managedMods = GetManagedModsSnapshot();
			var bindingProtectors = GetSortOrderBindingProtectors();
			SortOrderService.RebuildCurrentArchiveInventory(managedMods, bindingProtectors);
			RebuildSortOrderManagedIdentityTracking(managedMods);

			foreach (var mod in managedMods)
			{
				ResolveStartupSortOrder(mod, managedMods);
			}
			foreach (var mod in bindingProtectors)
			{
				ResolveStartupSortOrder(mod, managedMods);
			}
		}

		/// <summary>
		/// Rebuilds the live Sort inventory after the active install-log collection changes wholesale.
		/// </summary>
		private void RefreshSortOrderBindingProtectors()
		{
			if (SortOrderService == null)
			{
				return;
			}

			var managedMods = GetManagedModsSnapshot();
			var bindingProtectors = GetSortOrderBindingProtectors();
			SortOrderService.RebuildCurrentArchiveInventory(managedMods, bindingProtectors);
			foreach (var mod in bindingProtectors)
			{
				ResolveStartupSortOrder(mod, managedMods);
			}
		}

		/// <summary>
		/// Moves Sort ActiveMods tracking from a replaced install log to the current one and refreshes binding protection.
		/// </summary>
		private void RebindSortOrderActiveModsTracking(IInstallLog previousInstallLog)
		{
			if (SortOrderService == null)
			{
				return;
			}

			if (previousInstallLog?.ActiveMods != null)
			{
				previousInstallLog.ActiveMods.CollectionChanged -= ActiveMods_SortOrderCollectionChanged;
			}
			if (InstallationLog?.ActiveMods != null)
			{
				InstallationLog.ActiveMods.CollectionChanged += ActiveMods_SortOrderCollectionChanged;
			}
			RefreshSortOrderBindingProtectors();
		}

		/// <summary>
		/// Captures the current managed collection for deterministic two-phase startup/rebuild processing.
		/// </summary>
		private List<IMod> GetManagedModsSnapshot()
		{
			var managedMods = new List<IMod>();
			foreach (IMod mod in ManagedModRegistry.RegisteredMods)
			{
				managedMods.Add(mod);
			}
			return managedMods;
		}

		/// <summary>
		/// Captures active missing-archive placeholders that may keep an installed archive binding alive.
		/// </summary>
		private List<IMod> GetSortOrderBindingProtectors()
		{
			var protectors = new List<IMod>();
			foreach (IMod mod in InstallationLog.ActiveMods)
			{
				if (IsSortOrderBindingProtector(mod))
				{
					protectors.Add(mod);
				}
			}
			return protectors;
		}

		/// <summary>
		/// Determines whether an active mod is a usable missing-archive placeholder for Sort binding lifetime.
		/// </summary>
		private static bool IsSortOrderBindingProtector(IMod mod)
		{
			return mod is InstallLog.DummyMod && !string.IsNullOrWhiteSpace(mod.ModArchivePath) && Path.IsPathRooted(mod.ModArchivePath);
		}

		/// <summary>
		/// Subscribes to identity changes once for one managed mod object.
		/// </summary>
		private void TrackSortOrderManagedIdentity(IMod mod)
		{
			if (mod == null || !m_setSortOrderTrackedManagedMods.Add(mod))
			{
				return;
			}
			mod.PropertyChanged += ManagedMod_SortOrderIdentityChanged;
		}

		/// <summary>
		/// Removes identity tracking for the exact managed mod object.
		/// </summary>
		private void UntrackSortOrderManagedIdentity(IMod mod)
		{
			if (mod == null || !m_setSortOrderTrackedManagedMods.Remove(mod))
			{
				return;
			}
			mod.PropertyChanged -= ManagedMod_SortOrderIdentityChanged;
		}

		/// <summary>
		/// Replaces managed-mod identity subscriptions in one linear pass for startup and registry Reset handling.
		/// </summary>
		private void RebuildSortOrderManagedIdentityTracking(IEnumerable<IMod> managedMods)
		{
			ClearSortOrderManagedIdentityTracking();
			if (managedMods == null)
			{
				return;
			}
			foreach (var mod in managedMods)
			{
				TrackSortOrderManagedIdentity(mod);
			}
		}

		/// <summary>
		/// Removes all managed-mod identity subscriptions without repeated set lookups.
		/// </summary>
		private void ClearSortOrderManagedIdentityTracking()
		{
			foreach (var mod in m_setSortOrderTrackedManagedMods)
			{
				mod.PropertyChanged -= ManagedMod_SortOrderIdentityChanged;
			}
			m_setSortOrderTrackedManagedMods.Clear();
		}

		/// <summary>
		/// Determines whether a list contains the exact mod object reference.
		/// </summary>
		private static bool ContainsReference(IEnumerable<IMod> mods, IMod target)
		{
			foreach (var mod in mods)
			{
				if (ReferenceEquals(mod, target))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Compares managed mods strictly by object identity for subscription ownership tracking.
		/// </summary>
		private sealed class ModReferenceEqualityComparer : IEqualityComparer<IMod>
		{
			public static readonly ModReferenceEqualityComparer Instance = new ModReferenceEqualityComparer();

			/// <summary>
			/// Returns whether both values are the same managed-mod object.
			/// </summary>
			public bool Equals(IMod left, IMod right)
			{
				return ReferenceEquals(left, right);
			}

			/// <summary>
			/// Returns an identity-based hash code that is unaffected by IMod equality overrides.
			/// </summary>
			public int GetHashCode(IMod mod)
			{
				return mod == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(mod);
			}
		}

		/// <summary>
		/// Removes Sort identity and collection tracking subscriptions owned by this manager instance.
		/// </summary>
		private void DetachSortOrderIdentityTracking()
		{
			if (ManagedModRegistry == null)
			{
				return;
			}

			ManagedModRegistry.RegisteredMods.CollectionChanged -= ManagedMods_SortOrderCollectionChanged;
			if (InstallationLog?.ActiveMods != null)
			{
				InstallationLog.ActiveMods.CollectionChanged -= ActiveMods_SortOrderCollectionChanged;
			}
			ClearSortOrderManagedIdentityTracking();
		}

		#endregion

		#region Login Management

		/// <summary>
		/// Logins the user into the current mod repository.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		/// <returns><c>true</c> if the user was successfully logged in;
		/// <c>false</c> otherwise</returns>
		public bool Login()
		{
			if (LoginTask.LoggedOut)
				LoginTask.Update();
			else if (LoginTask.LoggingIn)
				MessageBox.Show(LanguageManager.Get("Mods.Login.WaitForAttempt", "Wait for the login attempt."), LanguageManager.Get("Mods.Login.InProgressTitle", "Login in progress..."), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return LoginTask.LoggedIn;
		}

		public void Logout()
		{
			LoginTask.Reset();
		}

		/// <summary>
		/// Resets generated cache data for the given mod without changing its installed state.
		/// </summary>
		/// <param name="p_modMod">The mod whose generated cache should be reset.</param>
		public void ResetModCache(IMod p_modMod)
		{
			if (p_modMod == null)
			{
				throw new ArgumentNullException(nameof(p_modMod));
			}

			var resettableMod = p_modMod as IModCacheResettable;
			if (resettableMod == null)
			{
				throw new NotSupportedException("The selected mod format does not support resetting generated cache data.");
			}

			resettableMod.ResetCache(ModCacheManager);
		}
		#endregion

		#region Mod Addition

		/// <summary>
		/// Installs the specified mod.
		/// </summary>
		/// <param name="p_strPath">The path to the mod to install.</param>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTask AddMod(string p_strPath, ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			return AddMod(p_strPath, p_cocConfirmOverwrite, null);
		}

		/// <summary>
		/// Installs the specified mod with an optional explicit category assignment.
		/// </summary>
		/// <param name="p_strPath">The path to the mod to install.</param>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <param name="p_intCategoryOverrideId">The explicit category ID, or <c>null</c> to keep normal Nexus category resolution.</param>
		/// <returns>A background task allowing the caller to track the operation.</returns>
		public IBackgroundTask AddMod(string p_strPath, ConfirmOverwriteCallback p_cocConfirmOverwrite, Int32? p_intCategoryOverrideId)
		{
			Uri uriPath = new Uri(p_strPath);
			if (uriPath.Scheme.ToLowerInvariant().ToString() == "nxm")
			{
				if (!ModRepository.IsOffline)
				{
					return ModAdditionQueue.AddMod(uriPath, p_cocConfirmOverwrite, p_intCategoryOverrideId);
				}
				else
				{
					Login();
					return AsyncAddMod(uriPath, p_cocConfirmOverwrite, p_intCategoryOverrideId);
				}
			}
			else
			{
				return ModAdditionQueue.AddMod(uriPath, p_cocConfirmOverwrite, p_intCategoryOverrideId);
			}
		}

		/// <summary>
		/// Loads the list of mods that are queued to be added to the mod manager.
		/// </summary>
		public void LoadQueuedMods()
		{
			ModAdditionQueue.LoadQueuedMods();
		}

		#endregion

		#region Mod Removal

		/// <summary>
		/// Deletes the given mod.
		/// </summary>
		/// <remarks>
		/// The mod is deactivated, unregistered, and then deleted.
		/// </remarks>
		/// <param name="p_modMod">The mod to delete.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation,
		/// or <c>null</c> if no long-running operation needs to be done.</returns>
		public IBackgroundTaskSet DeleteMod(IMod p_modMod, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			ModDeleter mddDeleter = InstallerFactory.CreateDelete(p_modMod, p_rolActiveMods);
			mddDeleter.TaskSetCompleted += new EventHandler<TaskSetCompletedEventArgs>(Deactivator_TaskSetCompleted);
			mddDeleter.Install();
			return mddDeleter;
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskSetCompleted"/> event of the mod deletion
		/// mod deativator.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the event arguments.</param>
		private void Deactivator_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			if (e.Success)
				ManagedModRegistry.UnregisterMod((IMod)e.ReturnValue);
		}

		#endregion

		public void ReinitializeInstallLog(string p_strInstallLogPath)
		{
			var previousInstallLog = InstallationLog;
			var replacementInstallLog = previousInstallLog.ReInitialize(p_strInstallLogPath);
			InstallationLog = replacementInstallLog;
			RebindSortOrderActiveModsTracking(previousInstallLog);
			m_vmaVirtualModActivator.ReinitializeInstallLog(InstallationLog);
			m_mdmDeploymentManager = new ModDeploymentManager(InstallationLog, m_vmaVirtualModActivator, GameMode);
			InstallerFactory = new ModInstallerFactory(GameMode, EnvironmentInfo, m_futFileUtility, m_scxUIContext, InstallationLog, m_pmgPluginManager, m_vmaVirtualModActivator, m_mdmDeploymentManager);
			if (m_ipmProfileManager != null)
				InstallerFactory.SetProfileManager(m_ipmProfileManager);
			m_macModActivator = null;
		}

		#region Mod Activation/Deactivation

		/// <summary>
		/// Activates the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to activate.</param>
		/// <param name="p_dlgUpgradeConfirmationDelegate">The delegate that is called to confirm whether an upgrade install should be performed.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <param name="p_rolActiveMods">The list or Active mods.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet ActivateMod(IMod p_modMod, ConfirmModUpgradeDelegate p_dlgUpgradeConfirmationDelegate, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			return ActivateMod(p_modMod, p_dlgUpgradeConfirmationDelegate, p_dlgOverwriteConfirmationDelegate,
				p_rolActiveMods, CapturePreferredInstallContext(ModInstallRoot.Default));
		}

		/// <summary>
		/// Activates the given mod using an explicitly captured install context.
		/// </summary>
		public IBackgroundTaskSet ActivateMod(IMod p_modMod, ConfirmModUpgradeDelegate p_dlgUpgradeConfirmationDelegate,
			ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods,
			ModInstallContext p_micInstallContext)
		{
			return ActivateMod(p_modMod, p_dlgUpgradeConfirmationDelegate, p_dlgOverwriteConfirmationDelegate,
				p_rolActiveMods, p_micInstallContext, false);
		}

		/// <summary>
		/// Activates the given mod while preserving an explicit install context if an upgrade is selected.
		/// </summary>
		public IBackgroundTaskSet ActivateMod(IMod p_modMod, ConfirmModUpgradeDelegate p_dlgUpgradeConfirmationDelegate,
			ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods,
			ModInstallContext p_micInstallContext, bool p_booExplicitMethodOverride)
		{
			if (p_micInstallContext == null)
				throw new ArgumentNullException(nameof(p_micInstallContext));
			if (InstallationLog.ActiveMods.Contains(p_modMod))
				return null;

			IBackgroundTaskSet operation = Activator.Activate(p_modMod, p_dlgUpgradeConfirmationDelegate, p_dlgOverwriteConfirmationDelegate,
				p_rolActiveMods, false, p_micInstallContext, p_booExplicitMethodOverride);
			return AttachManualOperationIdentity(operation, p_micInstallContext);
		}

		public IBackgroundTaskSet ActivateModInGameRoot(IMod p_modMod, ConfirmModUpgradeDelegate p_dlgUpgradeConfirmationDelegate, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			return ActivateMod(p_modMod, p_dlgUpgradeConfirmationDelegate, p_dlgOverwriteConfirmationDelegate,
				p_rolActiveMods, CapturePreferredInstallContext(ModInstallRoot.GameRoot));
		}

		/// <summary>
		/// Reinstalls the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to reinstall.</param>
		/// <param name="p_dlgUpgradeConfirmationDelegate">The delegate that is called to confirm whether an upgrade install should be performed.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <param name="p_rolActiveMods">The list or Active mods.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet ReinstallMod(IMod p_modMod, ConfirmModUpgradeDelegate p_dlgUpgradeConfirmationDelegate, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			ModInstallContext installContext = InstallationLog.ActiveMods.Contains(p_modMod)
				? CaptureInstalledContext(p_modMod)
				: CapturePreferredInstallContext(ModInstallRoot.Default);
			return ReinstallMod(p_modMod, p_dlgUpgradeConfirmationDelegate, p_dlgOverwriteConfirmationDelegate,
				p_rolActiveMods, installContext);
		}

		/// <summary>
		/// Reinstalls the given mod using an explicitly captured method/root.
		/// </summary>
		public IBackgroundTaskSet ReinstallMod(IMod p_modMod, ConfirmModUpgradeDelegate p_dlgUpgradeConfirmationDelegate,
			ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods,
			ModInstallContext p_micInstallContext)
		{
			if (p_micInstallContext == null)
				throw new ArgumentNullException(nameof(p_micInstallContext));

			IBackgroundTaskSet operation = Activator.Activate(p_modMod, p_dlgUpgradeConfirmationDelegate, p_dlgOverwriteConfirmationDelegate,
				p_rolActiveMods, true, p_micInstallContext);
			return AttachManualOperationIdentity(operation, p_micInstallContext);
		}

		/// <summary>
		/// Captures the currently installed method/root for a mod.
		/// </summary>
		public ModInstallContext CaptureInstalledContext(IMod p_modMod)
		{
			if (p_modMod == null)
				throw new ArgumentNullException(nameof(p_modMod));

			return new ModInstallContext(InstallationLog.GetModInstallMethod(p_modMod), InstallationLog.GetModInstallRoot(p_modMod));
		}

		/// <summary>
		/// Captures the per-game preferred method for a new operation and combines it with the requested root.
		/// </summary>
		public ModInstallContext CapturePreferredInstallContext(ModInstallRoot p_mirInstallRoot)
		{
			return new ModInstallContext(EnvironmentInfo.Settings.GetPreferredInstallMethod(GameMode.ModeId), p_mirInstallRoot);
		}

	/// <summary>
	/// Forces an upgrade from one mod to another.
	/// </summary>
	/// <remarks>
	/// No checks as to whether the two mods are actually related are performed. The new mod is reactivated
	/// as if it were the old mod, and the old mod is replaced by the new mod.
	/// </remarks>
	/// <param name="p_modOldMod">The mod from which to upgrade.</param>
	/// <param name="p_modNewMod">The mod to which to upgrade.</param>
	/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
	/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
	public IBackgroundTaskSet ForceUpgrade(IMod p_modOldMod, IMod p_modNewMod, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return Activator.ForceUpgrade(p_modOldMod, p_modNewMod, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// Reactivates the given mod.
		/// </summary>
		/// <remarks>
		/// A reactivation is an upgrade of a mod to itself. It re-runs the activation,
		/// without changing the installed precedence of its files and installed values.
		/// </remarks>
		/// <param name="p_modMod">The mod to reactivate.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet ReactivateMod(IMod p_modMod, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			if (!InstallationLog.ActiveMods.Contains(p_modMod))
				throw new InvalidOperationException(String.Format("Cannot reactivate the given mod, {0}. It is not active.", p_modMod.ModName));
			ModActivator marActivator = new ModActivator(InstallationLog, InstallerFactory);
			return marActivator.Reactivate(p_modMod, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// deactivates the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to deactivate.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		/// <returns>An unstarted background task set to submit through <see cref="ModActivationMonitor"/>, or <c>null</c> when no managed state exists.</returns>
		public IBackgroundTaskSet DeactivateMod(IMod p_modMod, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			bool booIsInstallLogActive = InstallationLog.ActiveMods.Contains(p_modMod);
			bool booHasManagedFiles = DeploymentManager != null && DeploymentManager.HasManagedFiles(p_modMod);
			if (!booIsInstallLogActive && !booHasManagedFiles)
				return null;

			ModInstallContext installContext = booIsInstallLogActive
				? CaptureInstalledContext(p_modMod)
				: new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Default);
			return AttachManualOperationIdentity(InstallerFactory.CreateUninstaller(p_modMod, p_rolActiveMods), installContext);
		}

		/// <summary>
		/// Attaches a new manual-operation identity to a native task before it reaches the shared submission seam.
		/// </summary>
		private IBackgroundTaskSet AttachManualOperationIdentity(IBackgroundTaskSet p_btsOperation, ModInstallContext p_micFallbackContext)
		{
			if (p_btsOperation == null)
				return null;

			ModInstallerBase nativeOperation = p_btsOperation as ModInstallerBase;
			if (nativeOperation == null)
				throw new InvalidOperationException("Only native mod installer task sets can receive a mod-operation identity.");

			ModInstallContext installContext = p_micFallbackContext;
			ModInstaller installer = p_btsOperation as ModInstaller;
			if (installer != null)
				installContext = installer.OperationInstallContext;
			if (installContext == null)
				throw new InvalidOperationException("A native mod operation requires an immutable install context before submission.");

			ModOperationFingerprint fingerprint = new ModOperationFingerprint(BuildManualOperationTargetFingerprint(), installContext, null);
			nativeOperation.AssignOperationIdentity(ModOperationIdentity.CreateNew(ModOperationOrigin.Manual, fingerprint));
			return p_btsOperation;
		}

		/// <summary>
		/// Builds the descriptive target token used by C3 operation identity. C4 will provide canonical shared-target authority.
		/// </summary>
		private string BuildManualOperationTargetFingerprint()
		{
			string modeId = GameMode == null ? String.Empty : GameMode.ModeId ?? String.Empty;
			string gamePath = GameMode == null ? String.Empty : GameMode.InstallationPath ?? String.Empty;
			string installInfoPath = GameMode == null || GameMode.GameModeEnvironmentInfo == null
				? String.Empty
				: GameMode.GameModeEnvironmentInfo.InstallInfoDirectory ?? String.Empty;

			return String.Format("nmm-target-v1|mode={0}|game={1}|installInfo={2}", modeId, gamePath, installInfoPath);
		}

		#endregion

		#region Mod Tagging

		/// <summary>
		/// Gets the tagger to use to tag mods with metadata.
		/// </summary>
		/// <returns>The tagger to use to tag mods with metadata.</returns>
		public AutoTagger GetModTagger()
		{
			return new AutoTagger(ModRepository);
		}

		#endregion

		#region Mod Updating

		/// <summary>
		/// Toggles the endorsement for the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to endorse/unendorse.</param>
		public void ToggleModEndorsement(IMod p_modMod)
		{
			AutoUpdater.ToggleModEndorsement(p_modMod);
		}

		/// <summary>
		/// Switches the mod category.
		/// </summary>
		/// <param name="p_modMod">The mod.</param>
		/// <param name="p_intCategoryId">The new category id.</param>
		public void SwitchModCategory(IMod p_modMod, Int32 p_intCategoryId)
		{
			AutoUpdater.SwitchModCategory(p_modMod, p_intCategoryId);
		}

		/// <summary>
		/// Remaps repository and explicit custom category assignments according to the supplied old-to-new ID map.
		/// </summary>
		/// <param name="p_dctCategoryRemaps">The old-to-new category ID mappings.</param>
		internal void RemapCategoryAssignments(IDictionary<Int32, Int32> p_dctCategoryRemaps)
		{
			if (p_dctCategoryRemaps == null || p_dctCategoryRemaps.Count == 0)
				return;

			Dictionary<IMod, Int32> originalRepositoryAssignments = new Dictionary<IMod, Int32>();
			Dictionary<IMod, Int32> originalCustomAssignments = new Dictionary<IMod, Int32>();
			Dictionary<IMod, Int32> replacementRepositoryAssignments = new Dictionary<IMod, Int32>();
			Dictionary<IMod, Int32> replacementCustomAssignments = new Dictionary<IMod, Int32>();
			foreach (IMod mod in ManagedMods)
			{
				Int32 newRepositoryCategoryId;
				Int32 newCustomCategoryId;
				bool remapRepositoryCategory = p_dctCategoryRemaps.TryGetValue(mod.CategoryId, out newRepositoryCategoryId) &&
					newRepositoryCategoryId < CategoryManager.FIRST_CUSTOM_CATEGORY_ID;
				bool remapCustomCategory = p_dctCategoryRemaps.TryGetValue(mod.CustomCategoryId, out newCustomCategoryId);
				if (!remapRepositoryCategory && !remapCustomCategory)
					continue;

				originalRepositoryAssignments[mod] = mod.CategoryId;
				originalCustomAssignments[mod] = mod.CustomCategoryId;
				replacementRepositoryAssignments[mod] = remapRepositoryCategory ? newRepositoryCategoryId : mod.CategoryId;
				replacementCustomAssignments[mod] = remapCustomCategory ? newCustomCategoryId : mod.CustomCategoryId;
			}

			List<IMod> remappedMods = new List<IMod>();
			try
			{
				foreach (IMod mod in replacementRepositoryAssignments.Keys)
				{
					remappedMods.Add(mod);
					ApplyCategoryAssignments(mod, replacementRepositoryAssignments[mod], replacementCustomAssignments[mod]);
				}
			}
			catch
			{
				foreach (IMod mod in remappedMods)
				{
					try
					{
						ApplyCategoryAssignments(mod, originalRepositoryAssignments[mod], originalCustomAssignments[mod]);
					}
					catch (Exception rollbackException)
					{
						System.Diagnostics.Trace.TraceError("Unable to roll back category assignments for {0}: {1}", mod.ModName, rollbackException);
					}
				}
				throw;
			}
		}

		/// <summary>
		/// Applies repository and custom category IDs to a mod as one metadata update.
		/// </summary>
		/// <param name="p_modMod">The mod whose category assignments are being updated.</param>
		/// <param name="p_intRepositoryCategoryId">The repository category ID.</param>
		/// <param name="p_intCustomCategoryId">The explicit custom category ID, or <c>-1</c> to use the repository category.</param>
		private static void ApplyCategoryAssignments(IMod p_modMod, Int32 p_intRepositoryCategoryId, Int32 p_intCustomCategoryId)
		{
			ModInfo updatedModInfo = new ModInfo(p_modMod)
			{
				CategoryId = p_intRepositoryCategoryId,
				CustomCategoryId = p_intCustomCategoryId,
				ForceCustomCategoryId = true
			};
			p_modMod.UpdateInfo(updatedModInfo, false);
		}

		/// <summary>
		/// Starts a category update using the current repository.
		/// </summary>
		/// <param name="p_cmCategoryManager">The category manager to update.</param>
		/// <param name="p_pmProfileManager">The current profile manager.</param>
		/// <param name="p_camConfirm">The delegate used to confirm updater actions.</param>
		/// <param name="p_booResetCategoryAssignmentsAfterUpdate">Whether every mod must be reassigned to its Nexus category after the update.</param>
		/// <returns>The running category update task.</returns>
		public IBackgroundTask UpdateCategories(CategoryManager p_cmCategoryManager, IProfileManager p_pmProfileManager, ConfirmActionMethod p_camConfirm, bool p_booResetCategoryAssignmentsAfterUpdate)
		{
			if (ModRepository.UserStatus != null)
			{
				CategoriesUpdateCheckTask cutCategoriesUpdateCheck = new CategoriesUpdateCheckTask(this, p_cmCategoryManager, ModRepository, p_booResetCategoryAssignmentsAfterUpdate);
				cutCategoriesUpdateCheck.Update(p_camConfirm);
				return cutCategoriesUpdateCheck;
			}
			else
				throw new Exception("Login required");
		}

		#region asyncUpdateCategories

		/// <summary>
		/// Starts a category update after an asynchronous repository login.
		/// </summary>
		/// <param name="p_cmCategoryManager">The category manager to update.</param>
		/// <param name="p_pmProfileManager">The current profile manager.</param>
		/// <param name="p_camConfirm">The delegate used to confirm updater actions.</param>
		/// <param name="p_booResetCategoryAssignmentsAfterUpdate">Whether every mod must be reassigned to its Nexus category after the update.</param>
		public void AsyncUpdateCategories(CategoryManager p_cmCategoryManager, IProfileManager p_pmProfileManager, ConfirmActionMethod p_camConfirm, bool p_booResetCategoryAssignmentsAfterUpdate)
		{
			CategoriesUpdateCheckTask cutCategoriesUpdateCheck = new CategoriesUpdateCheckTask(this, p_cmCategoryManager, ModRepository, p_booResetCategoryAssignmentsAfterUpdate);
			AsyncUpdateCategoriesTask(cutCategoriesUpdateCheck, p_camConfirm);
		}

		public async Task AsyncUpdateCategoriesTask(CategoriesUpdateCheckTask p_cutCategoriesUpdateCheck, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					p_cutCategoriesUpdateCheck.Update(p_camConfirm);
					UpdateCategoriesCheckStarted(this, new EventArgs<IBackgroundTask>(p_cutCategoriesUpdateCheck));
					break;
				}
				else
					intRetry++;
			}
		}

		#endregion

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_rolModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask ActivateMultipleMods(List<IMod> p_lstModList, bool p_booAllowCancel, ConfirmActionMethod p_camConfirm, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			ModInstallContext installContext = CapturePreferredInstallContext(ModInstallRoot.Default);
			ActivateMultipleModsTask ammActivateAllMods = new ActivateMultipleModsTask(p_lstModList, InstallationLog, InstallerFactory,
				p_camConfirm, p_dlgOverwriteConfirmationDelegate, installContext);
			ammActivateAllMods.Update(p_camConfirm);
			return ammActivateAllMods;
		}

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_rolModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask ProfileActivateMultipleMods(List<IMod> p_lstModList, bool p_booAllowCancel, ConfirmActionMethod p_camConfirm, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			var profileContext = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Default);
			ActivateMultipleModsTask ammActivateAllMods = new ActivateMultipleModsTask(p_lstModList, InstallationLog, InstallerFactory,
				p_camConfirm, p_dlgOverwriteConfirmationDelegate, profileContext);
			ammActivateAllMods.Update(p_camConfirm);
			return ammActivateAllMods;
		}

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="modList">The list of mods we need to update.</param>
		/// <param name="confirm">The delegate to call to confirm an action.</param>
		/// <param name="overrideCategorySetup">Whether to force a global update.</param>
		/// <param name="missingDownloadId">Whether to just look for missing download IDs.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask UpdateMods(List<IMod> modList, IProfileManager profileManager, ConfirmActionMethod confirm, string period, bool overrideCategorySetup, bool? missingDownloadId)
        {
            if (ModRepository.UserStatus != null)
			{
				var modUpdateCheck = new ModUpdateCheckTask(AutoUpdater, profileManager, ModRepository, modList, period, overrideCategorySetup, missingDownloadId, EnvironmentInfo.Settings.OverrideLocalModNames);
				modUpdateCheck.Update(confirm);

                return modUpdateCheck;
			}

            throw new Exception("Login required");
        }

		/// <summary>
		/// Disables multiple mods.
		/// </summary>
		/// <param name="p_rolModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask DisableMultipleMods(List<IMod> p_rolModList, ConfirmActionMethod p_camConfirm)
		{
			DisableMultipleModsTask dmmDisableAllMods = new DisableMultipleModsTask(p_rolModList, VirtualModActivator);
			dmmDisableAllMods.Update(p_camConfirm);
			return dmmDisableAllMods;
		}

		/// <summary>
		/// Disables multiple mods.
		/// </summary>
		/// <param name="p_rolModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask DeleteMultipleMods(ReadOnlyObservableList<IMod> p_rolModList, ConfirmActionMethod p_camConfirm)
		{
			DeleteMultipleModsTask dmmDeleteAllMods = new DeleteMultipleModsTask(p_rolModList, VirtualModActivator, ManagedModRegistry, this, InstallationLog.ActiveMods, InstallerFactory);
			dmmDeleteAllMods.Update(p_camConfirm);
			return dmmDeleteAllMods;
		}

		/// <summary>
		/// Uninstalls multiple mods.
		/// </summary>
		/// <param name="p_rolModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask DeactivateMultipleMods(ReadOnlyObservableList<IMod> p_rolModList, bool p_booFilesOnly, ConfirmActionMethod p_camConfirm)
		{
			DeactivateMultipleModsTask dmmDeactivateAllMods = new DeactivateMultipleModsTask(p_rolModList, this.InstallationLog, this.InstallerFactory, this.VirtualModActivator, GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, p_booFilesOnly);
			dmmDeactivateAllMods.Update(p_camConfirm);
			return dmmDeactivateAllMods;
		}

		/// <summary>
		/// Perform initial setup steps for the profile switch,
		/// installing and disabling mods when required.
		/// </summary>
		public IBackgroundTask ProfileSwitchSetup(ReadOnlyObservableList<IMod> modsToDeactivate, List<ProfileDeploymentInstallRequest> modsToInstall, IProfileManager profileManager,
			IModProfile profileToInstall, IModProfile profileToSwitch, bool p_booFilesOnly, ConfirmActionMethod p_camConfirm, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			ProfileSwitchSetupTask profileSwitchSetup = new ProfileSwitchSetupTask(modsToDeactivate, modsToInstall, profileManager, profileToInstall, profileToSwitch, InstallationLog, InstallerFactory, VirtualModActivator, DeploymentManager,
				GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, p_booFilesOnly, p_camConfirm, p_dlgOverwriteConfirmationDelegate);
			profileSwitchSetup.Update(p_camConfirm);
			return profileSwitchSetup;
		}

		#region asyncTag

		public async void AsyncTagMod(ModManagement.UI.ModManagerVM p_ModManagerVM, ModManagement.UI.ModTaggerVM p_ModTaggerVM, EventHandler<EventArgs<ModManagement.UI.ModTaggerVM>> p_TaggingMod)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					p_TaggingMod(p_ModManagerVM, new EventArgs<ModManagement.UI.ModTaggerVM>(p_ModTaggerVM));
					break;
				}
				else
					intRetry++;
			}
		}

		#endregion

		#region asyncAddMod

		/// <summary>
		/// Starts an asynchronous add-mod operation using normal Nexus category resolution.
		/// </summary>
		public IBackgroundTask AsyncAddMod(Uri p_uriPath, ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			return AsyncAddMod(p_uriPath, p_cocConfirmOverwrite, null);
		}

		/// <summary>
		/// Starts an asynchronous add-mod operation with an optional explicit category assignment.
		/// </summary>
		public IBackgroundTask AsyncAddMod(Uri p_uriPath, ConfirmOverwriteCallback p_cocConfirmOverwrite, Int32? p_intCategoryOverrideId)
		{
			IBackgroundTask tskAddModTask = ModAdditionQueue.AddMod(p_uriPath, p_cocConfirmOverwrite, p_intCategoryOverrideId);
			AsyncAddModTask(tskAddModTask);
			return tskAddModTask;
		}

		public async void AsyncAddModTask(IBackgroundTask p_tskAddModTask)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					if (p_tskAddModTask.Status == BackgroundTasks.TaskStatus.Paused)
						p_tskAddModTask.Resume();
					break;
				}
				else
				{
					intRetry++;
				}
			}
		} 
		#endregion

		#region asyncUpdate

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_lstModList">The list of mods we need to update.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <param name="p_booOverrideCategorySetup">Whether to force a global update.</param>
		/// <param name="p_booMissingDownloadId">Whether to just look for missing download IDs.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public void AsyncUpdateMods(List<IMod> p_lstModList, IProfileManager p_pmProfileManager, ConfirmActionMethod p_camConfirm, bool p_booOverrideCategorySetup, bool? p_booMissingDownloadId)
		{
			ModUpdateCheckTask mutModUpdateCheck = new ModUpdateCheckTask(AutoUpdater, p_pmProfileManager, ModRepository, p_lstModList, string.Empty, p_booOverrideCategorySetup, p_booMissingDownloadId, EnvironmentInfo.Settings.OverrideLocalModNames);
			AsyncUpdateModsTask(mutModUpdateCheck, p_camConfirm);
		}

		public async Task AsyncUpdateModsTask(ModUpdateCheckTask p_mutModUpdateCheck, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					p_mutModUpdateCheck.Update(p_camConfirm);
					UpdateCheckStarted(this, new EventArgs<IBackgroundTask>(p_mutModUpdateCheck));
					break;
				}
				else
					intRetry++;
			}
		}

		#endregion

		#region asyncEndorse

		/// <summary>
		/// Async toggle of the endorsement for the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to endorse/unendorse.</param>
		public async void AsyncEndorseMod(IMod p_modMod)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					AutoUpdater.ToggleModEndorsement(p_modMod);
					break;
				}
				else
					intRetry++;
			}
		}

		#endregion


		#region Automatic Download

		/// <summary>
		/// Starts the Automatic Download.
		/// </summary>
		/// <param name="p_strPath">The path to the mod to install.</param>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTask AutomaticDownload(List<string> p_strMissingMods, ProfileManager p_pmProfileManager, ConfirmActionMethod p_camConfirm, ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			AutomaticDownloadTask amtProfileAdder = new AutomaticDownloadTask(p_strMissingMods, this, p_pmProfileManager, p_cocConfirmOverwrite);
			amtProfileAdder.Update(p_camConfirm);
			return amtProfileAdder;
		}

		/// <summary>
		/// Starts the Automatic Download.
		/// </summary>
		/// <param name="p_strPath">The path to the mod to install.</param>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public void AsyncAutomaticDownload(List<string> p_strMissingMods, ProfileManager p_pmProfileManager, ConfirmActionMethod p_camConfirm, ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			AutomaticDownloadTask adtAutomaticDownload = new AutomaticDownloadTask(p_strMissingMods, this, p_pmProfileManager, p_cocConfirmOverwrite);
			AsyncAutomaticDownloadTask(adtAutomaticDownload, p_camConfirm);
		}

		public async void AsyncAutomaticDownloadTask(AutomaticDownloadTask p_pstAutomaticDownloadTask, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					p_pstAutomaticDownloadTask.Update(p_camConfirm);
					AutomaticDownloadStarted(this, new EventArgs<IBackgroundTask>(p_pstAutomaticDownloadTask));
					break;
				}
				else
				{
					intRetry++;
				}
			}
		}

		#endregion

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_lstModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask SetupReadMeManager(List<IMod> p_lstModList, ConfirmActionMethod p_camConfirm)
		{
			ReadMeSetupTask rmsReadMeManagerSetup = new ReadMeSetupTask(ReadMeManager, p_lstModList);
			rmsReadMeManagerSetup.Update(p_camConfirm);
			return rmsReadMeManagerSetup;
		}

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_hashMods">The hash of mods.</param>
		/// <param name="p_booEnable">Enable/Disable/Toggle.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask ToggleUpdateWarningTask(HashSet<IMod> p_hashMods, bool? p_booEnable, ConfirmActionMethod p_camConfirm)
		{
			ToggleUpdateWarningTask tuwToggleWarning = new ToggleUpdateWarningTask(p_hashMods, p_booEnable, p_camConfirm);
			tuwToggleWarning.Update(p_camConfirm);
			return tuwToggleWarning;
		}

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_hashMods">The hash of mods.</param>
		/// <param name="p_booEnable">Enable/Disable/Toggle.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask ToggleUpdateChecksTask(HashSet<IMod> p_hashMods, bool? p_booEnable, ConfirmActionMethod p_camConfirm)
		{
			ToggleModUpdateChecksTask tucToggleUpdateChecks = new ToggleModUpdateChecksTask(p_hashMods, p_booEnable, p_camConfirm);
			tucToggleUpdateChecks.Update(p_camConfirm);
			return tucToggleUpdateChecks;
		}

		#endregion

		#region Mod Search

		/// <summary>
		/// Returns the mod registered with the given file name.
		/// </summary>
		/// <param name="p_strFilename">The path of the mod to return</param>
		/// <returns>The mod registered with the given file name, or
		/// <c>null</c> if there is no registered mod with the given file name.</returns>
		public IMod GetModByFilename(string p_strFilename)
		{
			return ManagedModRegistry.GetModByFilename(p_strFilename);
		}

		/// <summary>
		/// Returns the mod registered with the given downloadId.
		/// </summary>
		/// <param name="p_strDownloadID">The path of the mod to return</param>
		/// <returns>The mod registered with the given downloadId, or
		/// <c>null</c> if there is no registered mod with the given downloadId.</returns>
		public IMod GetModByDownloadID(string p_strDownloadID)
		{
			return ManagedModRegistry.GetModByDownloadID(p_strDownloadID);
		}

		#endregion

		/// <summary>
		/// Sets the current profile manager.
		/// </summary>
		public void SetProfileManager(IProfileManager p_ipmProfileManager)
		{
			m_ipmProfileManager = p_ipmProfileManager;
			InstallerFactory.SetProfileManager(p_ipmProfileManager);
		}

		/// <summary>
		/// Purges all the scripted installers log files inside the InstallInfo\Scripted folder.
		/// </summary>
		public void PurgeXMLInstalledFile()
		{
			string strInstallFilesPath = Path.Combine(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted"));
			if (Directory.Exists(strInstallFilesPath))
			{
				foreach (string file in Directory.GetFiles(strInstallFilesPath, "*.xml", SearchOption.TopDirectoryOnly))
					ScriptedFileSelectionCache.DeleteArtifacts(file);
			}
		}

		public bool IsMatchingVersion(string p_strLocalVersion, string p_strProfileVersion)
		{
			Regex rgxClean = new Regex(@"([v(ver)]\.?)|((\.0)+$)", RegexOptions.IgnoreCase);
			string strThisVersion = rgxClean.Replace(p_strProfileVersion ?? "", "");
			string strThatVersion = rgxClean.Replace(p_strLocalVersion ?? "", "");
			if (String.IsNullOrEmpty(strThisVersion) || string.IsNullOrEmpty(strThatVersion))
				return true;
			else
				return String.Equals(strThisVersion, strThatVersion, StringComparison.OrdinalIgnoreCase);
		}
	}
}
