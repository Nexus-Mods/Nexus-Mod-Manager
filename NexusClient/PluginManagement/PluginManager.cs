using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Plugins;
using Nexus.Client.UI;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.PluginManagement
{
	/// <summary>
	/// The class the encapsulates managing plugins.
	/// </summary>
	/// <remarks>
	/// The list of managed plugins needs to be centralized to ensure integrity; having multiple managers, each
	/// with a potentially different list of managed plugins, would be disastrous. As such, this
	/// object is a singleton to help enforce that policy.
	/// Note, however, that the singleton nature of the manager is not meant to provide global access to the object.
	/// As such, there is no static accessor to retrieve the singleton instance. Instead, the
	/// <see cref="Initialize"/> method returns the only instance that should be used.
	/// </remarks>
	public class PluginManager : IPluginManager
	{
		#region Singleton

		private static IPluginManager m_pmgCurrent = null;

		/// <summary>
		/// Initializes the singleton intances of the mod manager.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_mprManagedPluginRegistry">The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</param>
		/// <param name="p_aplPluginLog">The <see cref="ActivePluginLog"/> tracking plugin activations for the
		/// current game mode.</param>
		/// <param name="p_polOrderLog">The <see cref="IPluginOrderLog"/> tracking plugin order for the
		/// current game mode.</param>
		/// <param name="p_povOrderValidator">The object that validates plugin order.</param>
		/// <exception cref="InvalidOperationException">Thrown if the plugin manager has already
		/// been initialized.</exception>
		public static IPluginManager Initialize(IGameMode p_gmdGameMode, PluginRegistry p_mprManagedPluginRegistry, ActivePluginLog p_aplPluginLog, IPluginOrderLog p_polOrderLog, IPluginOrderValidator p_povOrderValidator)
		{
			return Initialize(p_gmdGameMode, p_mprManagedPluginRegistry, p_aplPluginLog, p_polOrderLog, p_povOrderValidator, false);
		}

		public static IPluginManager Initialize(IGameMode p_gmdGameMode, PluginRegistry p_mprManagedPluginRegistry, ActivePluginLog p_aplPluginLog, IPluginOrderLog p_polOrderLog, IPluginOrderValidator p_povOrderValidator, bool p_booPluginRestrictionsDisabled)
		{
			if (m_pmgCurrent != null)
			{
				throw new InvalidOperationException(
					"The Plugin Manager has already been initialized.");
			}

			m_pmgCurrent = new PluginManager(p_gmdGameMode, p_mprManagedPluginRegistry, p_aplPluginLog, p_polOrderLog, p_povOrderValidator,	p_booPluginRestrictionsDisabled);

			return m_pmgCurrent;
		}

		/// <summary>
		/// Releases the singleton plugin manager and detaches its snapshot invalidation handlers.
		/// </summary>
		public void Release()
		{
			UnhookSnapshotInvalidation();
			InvalidateCurrentSnapshot();
			m_pmgCurrent = null;
		}

		#endregion

		private readonly PluginSnapshotBuilder m_psbSnapshotBuilder = new PluginSnapshotBuilder();
		private volatile PluginSnapshot m_psnCurrentSnapshot;
		private bool m_booPluginRestrictionsDisabled;

		#region Properties

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.
		/// </summary>
		/// <value>The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</value>
		public PluginRegistry ManagedPluginRegistry { get; set; }

		/// <summary>
		/// Gets the <see cref="ActivePluginLog"/> tracking plugin activations for the current game mode.
		/// </summary>
		/// <value>The <see cref="ActivePluginLog"/> tracking plugin activations for the current game mode.</value>
		public ActivePluginLog ActivePluginLog { get; private set; }

		/// <summary>
		/// Gets the <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.
		/// </summary>
		/// <value>The <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.</value>
		public IPluginOrderLog PluginOrderLog { get; private set; }

		/// <summary>
		/// Gets the object that validates plugin order.
		/// </summary>
		/// <value>The object that validates plugin order.</value>
		protected IPluginOrderValidator OrderValidator { get; private set; }

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<Plugin> ManagedPlugins
		{
			get
			{
				return PluginOrderLog.OrderedPlugins;
			}
		}

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<Plugin> ActivePlugins
		{
			get
			{
				return ActivePluginLog.ActivePlugins;
			}
		}

		/// <summary>
		/// Gets the max allowed number of active plugins.
		/// </summary>
		/// <value>The max allowed number of active plugins (0 if there's no limit).</value>
		public Int32 MaxAllowedActivePluginsCount
		{
			get
			{
				return GameMode.MaxAllowedActivePluginsCount;
			}
		}

		/// <summary>
		/// Gets the current validated plugin snapshot, rebuilding it only after plugin state changes.
		/// </summary>
		public PluginSnapshot CurrentSnapshot
		{
			get
			{
				PluginSnapshot psnSnapshot = m_psnCurrentSnapshot;

				if (psnSnapshot != null)
					return psnSnapshot;

				psnSnapshot = BuildPluginSnapshot();
				m_psnCurrentSnapshot = psnSnapshot;
				return psnSnapshot;
			}
		}

		public bool PluginRestrictionsDisabled
		{
			get
			{
				return m_booPluginRestrictionsDisabled;
			}
		}

		#endregion

		private PluginManagementPolicy Policy
		{
			get
			{
				return GameMode.PluginManagementPolicy ?? new PluginManagementPolicy();
			}
		}

		private PluginSnapshot BuildPluginSnapshot()
		{
			return BuildPluginSnapshot(
				new List<Plugin>(ManagedPlugins),
				new HashSet<Plugin>(
					ActivePlugins.Where(x => x != null),
					PluginComparer.Filename));
		}

		private PluginSnapshot BuildPluginSnapshot(IList<Plugin> p_lstOrderedPlugins, ISet<Plugin> p_setActivePlugins)
		{
			return BuildPluginSnapshot(
				p_lstOrderedPlugins,
				p_setActivePlugins,
				PluginRestrictionsDisabled
					? PluginRestrictionMode.Disabled
					: PluginRestrictionMode.Enforced);
		}

		private PluginSnapshot BuildPluginSnapshot(IList<Plugin> p_lstOrderedPlugins, ISet<Plugin> p_setActivePlugins, PluginRestrictionMode p_prmRestrictionMode)
		{
			return m_psbSnapshotBuilder.Build(
				Policy,
				p_lstOrderedPlugins,
				p_setActivePlugins,
				p_prmRestrictionMode);
		}

		/// <summary>
		/// Hooks the plugin collections that invalidate the cached snapshot when their contents change.
		/// </summary>
		private void HookSnapshotInvalidation()
		{
			ManagedPlugins.CollectionChanged += PluginStateCollectionChanged;
			ActivePlugins.CollectionChanged += PluginStateCollectionChanged;
		}

		/// <summary>
		/// Unhooks the plugin collections used to invalidate the cached snapshot.
		/// </summary>
		private void UnhookSnapshotInvalidation()
		{
			ManagedPlugins.CollectionChanged -= PluginStateCollectionChanged;
			ActivePlugins.CollectionChanged -= PluginStateCollectionChanged;
		}

		/// <summary>
		/// Invalidates the cached snapshot after an ordered or active plugin collection change.
		/// </summary>
		/// <param name="p_objSender">The event sender.</param>
		/// <param name="p_nceArgs">The collection-change event arguments.</param>
		private void PluginStateCollectionChanged(object p_objSender, NotifyCollectionChangedEventArgs p_nceArgs)
		{
			InvalidateCurrentSnapshot();
		}

		/// <summary>
		/// Invalidates the cached plugin snapshot.
		/// </summary>
		private void InvalidateCurrentSnapshot()
		{
			m_psnCurrentSnapshot = null;
		}

		private List<Plugin> GetPolicyCorrectedOrder(
			IList<Plugin> p_lstPlugins)
		{
			return GetPolicyCorrectedOrder(
				p_lstPlugins,
				PluginRestrictionsDisabled);
		}

		private List<Plugin> GetPolicyCorrectedOrder(
			IList<Plugin> p_lstPlugins,
			bool p_booRestrictionsDisabled)
		{
			List<Plugin> plugins = p_lstPlugins == null
				? new List<Plugin>()
				: new List<Plugin>(
					p_lstPlugins.Where(x => x != null));

			foreach (Plugin plugin in ManagedPlugins)
			{
				if (plugin != null &&
					!plugins.Contains(plugin, PluginComparer.Filename))
				{
					plugins.Add(plugin);
				}
			}

			if (!p_booRestrictionsDisabled)
				return m_psbSnapshotBuilder.CorrectStable(Policy, plugins);

			return RestoreProtectedPluginPositions(plugins);
		}

		/// <summary>
		/// Restores the authoritative positions of critical and official unmanaged plugins while preserving the requested order of all other plugins.
		/// </summary>
		/// <param name="p_lstPlugins">The requested plugin order.</param>
		/// <returns>The normalized plugin order with protected plugins restored to their authoritative positions.</returns>
		private List<Plugin> RestoreProtectedPluginPositions(List<Plugin> p_lstPlugins)
		{
			List<Plugin> plugins = p_lstPlugins == null
				? new List<Plugin>()
				: new List<Plugin>(
					p_lstPlugins.Where(x => x != null));

			List<Plugin> protectedPlugins = plugins
				.Where(IsProtectedPlugin)
				.Distinct(PluginComparer.Filename)
				.ToList();

			if (protectedPlugins.Count == 0)
				return plugins;

			List<Plugin> currentOrder =
				new List<Plugin>(
					PluginOrderLog.OrderedPlugins.Where(x => x != null));

			/*
			 * Used only to determine the canonical position of newly discovered
			 * critical plugins. The order of normal plugins is not taken from
			 * this list.
			 */
			List<Plugin> strictReferenceOrder =
				m_psbSnapshotBuilder.CorrectStable(Policy, plugins);

			plugins.RemoveAll(IsProtectedPlugin);

			foreach (Plugin plugin in protectedPlugins.OrderBy(
						 x => GetProtectedPluginTargetIndex(
							 x,
							 currentOrder,
							 strictReferenceOrder)))
			{
				int targetIndex = GetProtectedPluginTargetIndex(
					plugin,
					currentOrder,
					strictReferenceOrder);

				targetIndex = Math.Max(
					0,
					Math.Min(targetIndex, plugins.Count));

				plugins.Insert(targetIndex, plugin);
			}

			return plugins;
		}

		private int GetProtectedPluginTargetIndex(
			Plugin p_plgPlugin,
			IList<Plugin> p_lstCurrentOrder,
			IList<Plugin> p_lstStrictReferenceOrder)
		{
			int currentIndex = FindPluginIndex(
				p_lstCurrentOrder,
				p_plgPlugin);

			int strictIndex = FindPluginIndex(
				p_lstStrictReferenceOrder,
				p_plgPlugin);

			/*
			 * Critical base plugins use their canonical strict position.
			 *
			 * Official unmanaged plugins, including entries loaded from .ccc,
			 * retain their current authoritative position.
			 */
			if (IsOrderedCriticalPlugin(p_plgPlugin))
			{
				return strictIndex >= 0
					? strictIndex
					: Math.Max(0, currentIndex);
			}

			return currentIndex >= 0
				? currentIndex
				: Math.Max(0, strictIndex);
		}

		/// <summary>
		/// Gets whether the specified plugin is protected from activation and ordering changes.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to inspect.</param>
		/// <returns><c>true</c> if the plugin is critical or officially unmanaged; otherwise, <c>false</c>.</returns>
		private bool IsProtectedPlugin(Plugin p_plgPlugin)
		{
			return p_plgPlugin != null &&
				   GameMode.IsCriticalPlugin(p_plgPlugin);
		}

		private bool IsOrderedCriticalPlugin(Plugin p_plgPlugin)
		{
			if (p_plgPlugin == null)
				return false;

			return (GameMode.OrderedCriticalPluginNames ??
					new string[0])
				.Contains(
					p_plgPlugin.Filename,
					StringComparer.OrdinalIgnoreCase);
		}

		private static int FindPluginIndex(
			IList<Plugin> p_lstPlugins,
			Plugin p_plgPlugin)
		{
			if (p_lstPlugins == null || p_plgPlugin == null)
				return -1;

			for (int i = 0; i < p_lstPlugins.Count; i++)
			{
				if (PluginComparer.Filename.Equals(
						p_lstPlugins[i],
						p_plgPlugin))
				{
					return i;
				}
			}

			return -1;
		}

		private bool TryApplyPluginState(IList<Plugin> p_lstOrderedPlugins, ISet<Plugin> p_setActivePlugins)
		{
			List<Plugin> correctedOrder = GetPolicyCorrectedOrder(p_lstOrderedPlugins);
			HashSet<Plugin> desiredActivePlugins = new HashSet<Plugin>(p_setActivePlugins == null ? new List<Plugin>() : p_setActivePlugins.Where(x => x != null), PluginComparer.Filename);

			foreach (Plugin protectedPlugin in correctedOrder.Where(IsProtectedPlugin))
			{
				desiredActivePlugins.Add(protectedPlugin);
			}

			PluginSnapshot snapshot = BuildPluginSnapshot(correctedOrder, desiredActivePlugins);

			if (snapshot.HasErrors)
			{
				TracePluginDiagnostics(snapshot);
				return false;
			}

			HashSet<Plugin> hstCurrentActivePlugins = new HashSet<Plugin>(ActivePlugins.Where(x => x != null), PluginComparer.Filename);
			bool booOrderChanged = !PluginOrdersEqual(PluginOrderLog.OrderedPlugins, correctedOrder);
			List<Plugin> lstPluginsToDeactivate = hstCurrentActivePlugins.Where(x => !desiredActivePlugins.Contains(x)).ToList();
			List<Plugin> lstPluginsToActivate = desiredActivePlugins.Where(x => !hstCurrentActivePlugins.Contains(x)).ToList();

			if (!booOrderChanged && lstPluginsToDeactivate.Count == 0 && lstPluginsToActivate.Count == 0)
			{
				m_psnCurrentSnapshot = snapshot;
				return true;
			}

			Transactions.TransactionScope tsTransaction = null;

			try
			{
				tsTransaction = new Transactions.TransactionScope();

				if (booOrderChanged)
					PluginOrderLog.SetPluginOrder(correctedOrder);

				if (lstPluginsToDeactivate.Count > 0)
					ActivePluginLog.DeactivatePlugins(lstPluginsToDeactivate);

				if (lstPluginsToActivate.Count > 0)
					ActivePluginLog.ActivatePlugins(lstPluginsToActivate);

				tsTransaction.Complete();
				m_psnCurrentSnapshot = snapshot;
				return true;
			}
			finally
			{
				if (tsTransaction != null)
					tsTransaction.Dispose();
			}
		}

		/// <summary>
		/// Determines whether two plugin sequences contain the same plugins in the same order.
		/// </summary>
		/// <param name="p_lstFirst">The first plugin sequence.</param>
		/// <param name="p_lstSecond">The second plugin sequence.</param>
		/// <returns><c>true</c> if both sequences have the same filename-based order; otherwise, <c>false</c>.</returns>
		private static bool PluginOrdersEqual(IList<Plugin> p_lstFirst, IList<Plugin> p_lstSecond)
		{
			if (ReferenceEquals(p_lstFirst, p_lstSecond))
				return true;

			if (p_lstFirst == null || p_lstSecond == null || p_lstFirst.Count != p_lstSecond.Count)
				return false;

			PluginComparer pcpComparer = PluginComparer.Filename;

			for (int intIndex = 0; intIndex < p_lstFirst.Count; intIndex++)
			{
				if (!pcpComparer.Equals(p_lstFirst[intIndex], p_lstSecond[intIndex]))
					return false;
			}

			return true;
		}

		private void TracePluginDiagnostics(PluginSnapshot snapshot)
		{
			foreach (PluginValidationDiagnostic diagnostic in snapshot.Diagnostics)
				if (diagnostic.Severity == PluginValidationSeverity.Error)
					Trace.TraceWarning("Plugin state rejected: {0} - {1}", diagnostic.Plugin == null ? String.Empty : diagnostic.Plugin.Filename, diagnostic.Message);
		}

		/// <summary>
		/// Attempts to enable or disable non-critical plugin sorting and dependency restrictions.
		/// </summary>
		/// <param name="p_booDisabled">Whether plugin restrictions should be disabled.</param>
		/// <param name="p_psnValidationSnapshot">The resulting snapshot, or the strict snapshot that prevented the transition.</param>
		/// <returns><c>true</c> if the requested mode was applied; otherwise, <c>false</c>.</returns>
		public bool TrySetPluginRestrictionsDisabled(bool p_booDisabled, out PluginSnapshot p_psnValidationSnapshot)
		{
			if (PluginRestrictionsDisabled == p_booDisabled)
			{
				p_psnValidationSnapshot = CurrentSnapshot;
				return true;
			}

			/*
			 * Entering unrestricted mode does not change plugin order or active
			 * state. It only changes how the current state is validated.
			 */
			if (p_booDisabled)
			{
				m_booPluginRestrictionsDisabled = true;
				InvalidateCurrentSnapshot();

				p_psnValidationSnapshot = CurrentSnapshot;
				return true;
			}

			/*
			 * Build the order that would be used under normal restrictions.
			 * Order-only violations can therefore be corrected automatically.
			 */
			List<Plugin> strictOrder = GetPolicyCorrectedOrder(
				new List<Plugin>(PluginOrderLog.OrderedPlugins),
				false);

			HashSet<Plugin> activePlugins = new HashSet<Plugin>(
				ActivePlugins.Where(x => x != null),
				PluginComparer.Filename);

			foreach (Plugin protectedPlugin in strictOrder.Where(IsProtectedPlugin))
				activePlugins.Add(protectedPlugin);

			/*
			 * Validate explicitly in strict mode before changing the current mode.
			 * Missing or inactive masters prevent the transition.
			 */
			PluginSnapshot strictSnapshot = BuildPluginSnapshot(
				strictOrder,
				activePlugins,
				PluginRestrictionMode.Enforced);

			if (strictSnapshot.HasErrors)
			{
				TracePluginDiagnostics(strictSnapshot);
				p_psnValidationSnapshot = strictSnapshot;
				return false;
			}

			/*
			 * Temporarily enter strict mode before applying the corrected state,
			 * because TryApplyPluginState uses the current restriction mode.
			 */
			m_booPluginRestrictionsDisabled = false;
			InvalidateCurrentSnapshot();

			if (!TryApplyPluginState(strictOrder, activePlugins))
			{
				/*
				 * Defensive rollback: restoring unrestricted mode is necessary
				 * only if applying the previously validated strict state fails.
				 */
				m_booPluginRestrictionsDisabled = true;
				InvalidateCurrentSnapshot();

				p_psnValidationSnapshot = strictSnapshot;
				return false;
			}

			p_psnValidationSnapshot = CurrentSnapshot;
			return true;
		}

		private Plugin ResolvePluginPath(string p_strPath)
		{
			if (String.IsNullOrWhiteSpace(p_strPath))
				return null;

			string strPath = p_strPath;
			if (!Path.IsPathRooted(strPath))
				strPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, strPath);

			Plugin plugin = ManagedPluginRegistry.GetPlugin(strPath);
			if (plugin == null && ManagedPluginRegistry.IsActivatiblePluginFile(strPath))
			{
				AddPlugin(strPath);
				plugin = ManagedPluginRegistry.GetPlugin(strPath);
			}
			return plugin;
		}

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_mprManagedPluginRegistry">The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</param>
		/// <param name="p_aplPluginLog">The <see cref="ActivePluginLog"/> tracking plugin activations for the
		/// current game mode.</param>
		/// <param name="p_polOrderLog">The <see cref="IPluginOrderLog"/> tracking plugin order for the
		/// current game mode.</param>
		/// <param name="p_povOrderValidator">The object that validates plugin order.</param>
		private PluginManager(IGameMode p_gmdGameMode, PluginRegistry p_mprManagedPluginRegistry, ActivePluginLog p_aplPluginLog, IPluginOrderLog p_polOrderLog, IPluginOrderValidator p_povOrderValidator, bool p_booPluginRestrictionsDisabled)
		{
			GameMode = p_gmdGameMode;
			ManagedPluginRegistry = p_mprManagedPluginRegistry;
			ActivePluginLog = p_aplPluginLog;
			PluginOrderLog = p_polOrderLog;
			OrderValidator = p_povOrderValidator;

			m_booPluginRestrictionsDisabled = p_booPluginRestrictionsDisabled;

			HookSnapshotInvalidation();

			HashSet<Plugin> activePlugins =	new HashSet<Plugin>(
					ActivePlugins.Where(x => x != null),
					PluginComparer.Filename);

			IEnumerable<string> protectedPluginNames = (GameMode.OrderedCriticalPluginNames ??
				 new string[0])
				.Concat(
					GameMode.OrderedOfficialUnmanagedPluginNames ??
					new string[0])
				.Distinct(StringComparer.OrdinalIgnoreCase);

			foreach (string strPlugin in protectedPluginNames)
			{
				Plugin plugin = ResolvePluginPath(strPlugin);

				if (plugin != null)
					activePlugins.Add(plugin);
			}

			TryApplyPluginState(
				new List<Plugin>(
					PluginOrderLog.OrderedPlugins),
				activePlugins);
		}

		#endregion

		#region Plugin Registration

		/// <summary>
		/// Adds the specified plugin to the list of managed plugins.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin to add.</param>
		/// <returns><c>true</c> if the specified plugin was added;
		/// <c>false</c> otherwise.</returns>
		public bool AddPlugin(string p_strPluginPath)
		{
			if (String.IsNullOrWhiteSpace(p_strPluginPath))
				return false;

			Transactions.TransactionScope tsTransaction = null;
			try
			{
				tsTransaction = new Transactions.TransactionScope();

				Plugin plgPlugin = ManagedPluginRegistry.GetPlugin(p_strPluginPath);
				bool booRegisteredNow = false;
				if (plgPlugin == null)
				{
					if (!ManagedPluginRegistry.RegisterPlugin(p_strPluginPath))
						return false;

					plgPlugin = ManagedPluginRegistry.GetPlugin(p_strPluginPath);
					if (plgPlugin == null)
						return false;

					booRegisteredNow = true;
				}

				List<Plugin> plugins = new List<Plugin>(PluginOrderLog.OrderedPlugins.Where(x => x != null));
				List<Plugin> matchingPlugins = plugins.Where(x => PluginComparer.Filename.Equals(x, plgPlugin)).ToList();
				bool booOrderNeedsRepair = matchingPlugins.Count != 1 || !ReferenceEquals(matchingPlugins[0], plgPlugin);

				if (booRegisteredNow || booOrderNeedsRepair)
				{
					plugins.RemoveAll(x => PluginComparer.Filename.Equals(x, plgPlugin));
					plugins.Add(plgPlugin);
					PluginOrderLog.SetPluginOrder(GetPolicyCorrectedOrder(plugins));
				}

				Plugin plgActiveMatch = ActivePlugins.FirstOrDefault(x => x != null && PluginComparer.Filename.Equals(x, plgPlugin));
				if (plgActiveMatch != null && !ReferenceEquals(plgActiveMatch, plgPlugin))
				{
					ActivePluginLog.DeactivatePlugin(plgActiveMatch);
					ActivePluginLog.ActivatePlugin(plgPlugin);
				}

				tsTransaction.Complete();
				return booRegisteredNow || booOrderNeedsRepair;
			}
			finally
			{
				if (tsTransaction != null)
					tsTransaction.Dispose();
			}
		}

		/// <summary>
		/// Registers all deployed plugins before applying the valid requested
		/// activation subset through the authoritative policy pipeline.
		/// </summary>
		public void IntegrateDeployedPlugins(IList<string> p_lstPluginPaths)
		{
			List<string> pluginPaths = (p_lstPluginPaths ?? new List<string>())
				.Where(x => !String.IsNullOrWhiteSpace(x) && IsActivatiblePluginFile(x))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (pluginPaths.Count == 0)
				return;

			List<Plugin> requestedPlugins = new List<Plugin>();

			// Register every deployed plugin before activation is evaluated. This
			// prevents activation success from depending on archive enumeration order.
			foreach (string pluginPath in pluginPaths)
			{
				AddPlugin(pluginPath);

				Plugin plugin = GetRegisteredPlugin(pluginPath);
				if (plugin != null &&
					!requestedPlugins.Any(x => PluginComparer.Filename.Equals(x, plugin)))
				{
					requestedPlugins.Add(plugin);
				}
			}

			if (requestedPlugins.Count == 0)
				return;

			List<Plugin> correctedOrder =
				GetPolicyCorrectedOrder(new List<Plugin>(PluginOrderLog.OrderedPlugins));

			HashSet<Plugin> currentActive =
				new HashSet<Plugin>(
					ActivePlugins.Where(x => x != null),
					PluginComparer.Filename);

			List<Plugin> requestedInactive = requestedPlugins
				.Where(x => !currentActive.Contains(x) && CanChangeActiveState(x))
				.ToList();

			if (requestedInactive.Count == 0)
				return;

			HashSet<Plugin> requestedActive =
				new HashSet<Plugin>(currentActive, PluginComparer.Filename);
			requestedActive.UnionWith(requestedInactive);

			PluginSnapshot requestedSnapshot =
				BuildPluginSnapshot(correctedOrder, requestedActive);

			if (!requestedSnapshot.HasErrors)
			{
				TryApplyPluginState(correctedOrder, requestedActive);
				return;
			}

			// Keep genuinely invalid plugins registered but inactive. Iterate in the
			// corrected order so newly deployed masters are considered before their
			// dependants, independently of archive file order.
			HashSet<Plugin> validActive =
				new HashSet<Plugin>(currentActive, PluginComparer.Filename);
			HashSet<Plugin> pending =
				new HashSet<Plugin>(requestedInactive, PluginComparer.Filename);

			bool stateExpanded;
			do
			{
				stateExpanded = false;

				foreach (Plugin candidate in
					correctedOrder.Where(x => pending.Contains(x)).ToList())
				{
					HashSet<Plugin> testActive =
						new HashSet<Plugin>(validActive, PluginComparer.Filename);
					testActive.Add(candidate);

					if (BuildPluginSnapshot(correctedOrder, testActive).HasErrors)
						continue;

					validActive.Add(candidate);
					pending.Remove(candidate);
					stateExpanded = true;
				}
			}
			while (stateExpanded);

			if (!validActive.SetEquals(currentActive))
				TryApplyPluginState(correctedOrder, validActive);

			if (pending.Count > 0)
			{
				Trace.TraceWarning(
					"Some deployed plugins remain inactive because their requested state is invalid: {0}",
					String.Join(
						", ",
						pending.Select(x => Path.GetFileName(x.Filename)).ToArray()));

				HashSet<Plugin> rejectedActive =
					new HashSet<Plugin>(validActive, PluginComparer.Filename);
				rejectedActive.UnionWith(pending);
				TracePluginDiagnostics(
					BuildPluginSnapshot(correctedOrder, rejectedActive));
			}
		}

		/// <summary>
		/// Removes the given plugin from the list of managed plugins.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to remove.</param>
		public void RemovePlugin(Plugin p_plgPlugin)
		{
			if (p_plgPlugin == null)
				return;

			Transactions.TransactionScope tsTransaction = null;
			try
			{
				tsTransaction = new Transactions.TransactionScope();

				// Registration lifecycle changes must not go through TryApplyPluginState().
				// That method preserves omitted managed plugins and validates the final state,
				// both of which are incorrect while physically adding or removing a plugin.
				ActivePluginLog.DeactivatePlugin(p_plgPlugin);
				PluginOrderLog.RemovePlugin(p_plgPlugin);
				ManagedPluginRegistry.UnregisterPlugin(p_plgPlugin);

				tsTransaction.Complete();
			}
			finally
			{
				if (tsTransaction != null)
					tsTransaction.Dispose();
			}
		}

		/// <summary>
		/// Removes the specified plugin from the list of managed plugins.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin to remove.</param>
		public void RemovePlugin(string p_strPluginPath)
		{
			RemovePlugin(GetRegisteredPlugin(p_strPluginPath));
		}

		/// <summary>
		/// Automatically sorts the managed plugins.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the sorting.</returns>
		public IBackgroundTask AutoPluginSorting(ConfirmActionMethod p_camConfirm)
		{
			AutoPluginSortingTask pstPluginSortingTask = new AutoPluginSortingTask(GameMode, ManagedPlugins, p_camConfirm);
			pstPluginSortingTask.Update(p_camConfirm);
			return pstPluginSortingTask;
		}

		/// <summary>
		/// Determines if the specified plugin is registered.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin whose registration status is to be determined.</param>
		/// <returns><c>true</c> if the specified plugin is registered;
		/// <c>false</c> otherwise.</returns>
		public bool IsPluginRegistered(string p_strPath)
		{
			return GetRegisteredPlugin(p_strPath) != null;
		}

		/// <summary>
		/// Gets the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path of the plugin to retrieve.</param>
		/// <returns>The specified plugin, or <c>null</c> if the plugin is not registered.</returns>
		public Plugin GetRegisteredPlugin(string p_strPath)
		{
			//TODO this check doesn't work for Gamebryo based games
			// GetFormatSpecificInstallPath() (or whatever it is called) should be
			// used instead of InstallationPath
			// but we can't use it because asking for the mod format here makes no
			// sense
			// as such, mods should pass in the full path, or at least a path relative to
			// InstallationPath
			// Really, I think GetFormatSpecificInstallPath() should be scrapped,
			// and mods should adjust for the current game mode, not the game mode for the
			// current mod format
			string strPath = p_strPath;
			if (!Path.IsPathRooted(p_strPath))
				strPath = Path.Combine(GameMode.PluginDirectory, GameMode.GetModFormatAdjustedPath(null, p_strPath, ModPathContext.VirtualStorage));
			return ManagedPluginRegistry.GetPlugin(strPath);
		}

		#endregion

		#region Plugin Activation/Deactivation

		/// <summary>
		/// Sets the activations status of the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin whose status is to be set.</param>
		/// <param name="p_booActive">Whether to activate the plugin, or deactivate it.</param>
		public void SetPluginActivation(string p_strPath, bool p_booActive)
		{
			if (p_booActive)
				ActivatePlugin(p_strPath);
			else
				DeactivatePlugin(p_strPath);
		}

		/// <summary>
		/// Activates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to activate.</param>
		public void ActivatePlugin(Plugin p_plgPlugin)
		{
			SetPluginActivation(new List<Plugin> { p_plgPlugin }, true);
		}

		/// <summary>
		/// Activates the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin to activate.</param>
		public void ActivatePlugin(string p_strPath)
		{
			ActivatePlugin(ResolvePluginPath(p_strPath));
		}

		/// <summary>
		/// Deactivates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to deactivate.</param>
		public void DeactivatePlugin(Plugin p_plgPlugin)
		{
			SetPluginActivation(new List<Plugin> { p_plgPlugin }, false);
		}

		/// <summary>
		/// Deactivates the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin to deactivate.</param>
		public void DeactivatePlugin(string p_strPath)
		{
			DeactivatePlugin(ResolvePluginPath(p_strPath));
		}

		public void SetPluginActivation(IList<Plugin> p_lstPlugins, bool p_booActive)
		{
			HashSet<Plugin> activePlugins = new HashSet<Plugin>(ActivePlugins.Where(x => x != null), PluginComparer.Filename);
			foreach (Plugin plugin in p_lstPlugins ?? new List<Plugin>())
			{
				if (plugin == null || !CanChangeActiveState(plugin))
					continue;
				if (p_booActive)
					activePlugins.Add(plugin);
				else
					activePlugins.Remove(plugin);
			}

			TryApplyPluginState(new List<Plugin>(PluginOrderLog.OrderedPlugins), activePlugins);
		}

		public void ApplyPluginState(IList<Plugin> p_lstOrderedPlugins, IList<Plugin> p_lstActivePlugins)
		{
			HashSet<Plugin> activePlugins = new HashSet<Plugin>(p_lstActivePlugins == null ? new List<Plugin>() : p_lstActivePlugins.Where(x => x != null), PluginComparer.Filename);
			if (!TryApplyPluginState(p_lstOrderedPlugins, activePlugins))
				throw new InvalidOperationException("The requested plugin state is invalid for the current game policy.");
		}

		/// <summary>
		/// Determines if the specified plugin is active.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin whose active status is to be determined.</param>
		/// <returns><c>true</c> if the specified plugin is active;
		/// <c>false</c> otherwise.</returns>
		public bool IsPluginActive(string p_strPath)
		{
			string strPath = p_strPath;
			if (!Path.IsPathRooted(p_strPath))
				strPath = Path.Combine(GameMode.PluginDirectory, GameMode.GetModFormatAdjustedPath(null, p_strPath, ModPathContext.VirtualStorage));
			return ActivePlugins.Contains(ManagedPluginRegistry.GetPlugin(strPath));
		}

		/// <summary>
		/// Determines if the active state of the given plugin can be changed.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin for which it is to be determined if the active state can be changed.</param>
		/// <returns><c>true</c> if the given plugin's active state can be changed;
		/// <c>false</c> otherwise.</returns>
		public bool CanChangeActiveState(Plugin p_plgPlugin)
		{
			return p_plgPlugin != null && !IsProtectedPlugin(p_plgPlugin);
		}

		public bool CanChangePluginOrder(Plugin p_plgPlugin)
		{
			return p_plgPlugin != null && !IsProtectedPlugin(p_plgPlugin);
		}

		#endregion

		#region Plugin Ordering

		/// <summary>
		/// Gets the load order of the specifid plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin whose load order is to be returned.</param>
		/// <returns>The index of the given plugin, or -1 if the plugin is not being managed.</returns>
		public Int32 GetPluginOrderIndex(Plugin p_plgPlugin)
		{
			return PluginOrderLog.OrderedPlugins.IndexOf(p_plgPlugin);
		}

		/// <summary>
		/// Sets the load order of the specifid plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin whose load order is to be set.</param>
		/// <param name="p_intNewIndex">The new load order index of the plugin.</param>
		public void SetPluginOrderIndex(Plugin p_plgPlugin, int p_intNewIndex)
		{
			if (!CanChangePluginOrder(p_plgPlugin))
				return;

			List<Plugin> plugins = new List<Plugin>(PluginOrderLog.OrderedPlugins);
			plugins.Remove(p_plgPlugin);
			p_intNewIndex = Math.Max(0, Math.Min(p_intNewIndex, plugins.Count));
			plugins.Insert(p_intNewIndex, p_plgPlugin);
			TryApplyPluginState(plugins, new HashSet<Plugin>(ActivePlugins.Where(x => x != null), PluginComparer.Filename));
		}

		/// <summary>
		/// Sets the order of the plugins to the given order.
		/// </summary>
		/// <remarks>
		/// If the given list does not include all registered plugins, then the plugins are ordered in a manner
		/// so as to not displace the positions of the plugins whose order was not specified.
		/// </remarks>
		/// <param name="p_lstOrderedPlugins">The list indicating the desired order of the plugins.</param>
		public void SetPluginOrder(IList<Plugin> p_lstOrderedPlugins)
		{
			TryApplyPluginState(p_lstOrderedPlugins, new HashSet<Plugin>(ActivePlugins.Where(x => x != null), PluginComparer.Filename));
		}

		/// <summary>
		/// Determines if the specified plugin order is valid.
		/// </summary>
		/// <param name="p_lstPlugins">The plugins whose order is to be validated.</param>
		/// <returns><c>true</c> if the given plugins are in a valid order;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateOrder(IList<Plugin> p_lstPlugins)
		{
			return !BuildPluginSnapshot(GetPolicyCorrectedOrder(p_lstPlugins), new HashSet<Plugin>(ActivePlugins.Where(x => x != null), PluginComparer.Filename)).HasErrors;
		}

		#endregion

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_hashMods">The hash of mods.</param>
		/// <param name="p_booEnable">Enable/Disable/Toggle.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask ManageMultiplePluginsTask(List<Plugin> p_lstPlugins, bool p_booEnable, ConfirmActionMethod p_camConfirm)
		{
			ManageMultiplePluginsTask mptManageMultiplePlugins = new ManageMultiplePluginsTask(p_lstPlugins, this, p_booEnable);
			mptManageMultiplePlugins.Update(p_camConfirm);
			return mptManageMultiplePlugins;
		}

		/// <summary>
		/// Determines if the specified file is a plugin that can be activated for the game mode.
		/// </summary>
		/// <param name="p_strPath">The path to the file for which it is to be determined if it is a plugin file.</param>
		/// <returns><c>true</c> if the specified file is a plugin file that can be activated in the game mode;
		/// <c>false</c> otherwise.</returns>
		public bool IsActivatiblePluginFile(string p_strPath)
		{
			string strPath = p_strPath;
			if (!Path.IsPathRooted(p_strPath))
				strPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, p_strPath);

			return ManagedPluginRegistry.IsActivatiblePluginFile(strPath);
		}

		/// <summary>
		/// Determines if the game mode can handle more active plugins.
		/// </summary>
		/// <returns><c>true</c> if it can;
		/// <c>false</c> otherwise.</returns>
		public bool CanActivatePlugins()
		{
			foreach (PluginAddressSpacePolicy addressSpace in Policy.AddressSpaces)
			{
				if (addressSpace == null || addressSpace.MaxCount <= 0 || addressSpace.AddressClass == PluginAddressClass.None)
					continue;
				int activePlugins = ActivePlugins.Count(x => x != null && x.Metadata.AddressClass == addressSpace.AddressClass);
				if (activePlugins >= addressSpace.MaxCount)
					return false;
			}
			return true;
		}

		public List<Plugin> GetOrphanedPlugins(string p_strMasterName)
		{
			List<Plugin> lstPlugins = new List<Plugin>();
			string strMasterName = Path.GetFileName(p_strMasterName);

			foreach (Plugin plugin in ManagedPlugins)
			{
				if (plugin.Masters.Contains(strMasterName, StringComparer.OrdinalIgnoreCase))
				{
					lstPlugins.Add(plugin);
				}
			}

			return lstPlugins;
		}

		/// <summary>
		/// Gets the plugin description.
		/// </summary>
		public string GetPluginDescription(string p_strPlugin)
		{
			return ManagedPluginRegistry.PluginFactory.GetUpdatedPluginInfo(p_strPlugin);
		}

		/// <summary>
		/// Applies the load order specified by the given list of registered plugins
		/// </summary>
		/// <param name="p_kvpRegisteredPlugins">The list of registered plugins.</param>
		/// <param name="p_booSortingOnly">Whether we just want to apply the sorting.</param>
		public IBackgroundTask ApplyLoadOrder(Dictionary<Plugin, string> p_kvpRegisteredPlugins, bool p_booSortingOnly)
		{
			ApplyLoadOrderTask altApplyLoadOrder = new ApplyLoadOrderTask(this, p_kvpRegisteredPlugins, p_booSortingOnly);
			if (GameMode.LoadOrderManager != null)
				GameMode.LoadOrderManager.MonitorExternalTask(altApplyLoadOrder);
			else
				altApplyLoadOrder.Update();

			return altApplyLoadOrder;
		}
	}
}
