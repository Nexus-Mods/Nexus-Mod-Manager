namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Nexus.Client.Mods;
	using Nexus.Client.Mods.Formats.FOMod;

	/// <summary>
	/// Identifies the lifecycle context in which a Sort assignment is being resolved.
	/// </summary>
	public enum ModSortOrderAssignmentContext
	{
		StartupOrDiscovery,
		AddOrDownload,
		IdentityResolvedForPendingAdd,
		ExplicitEdit
	}

	/// <summary>
	/// Describes a resolved Sort assignment change.
	/// </summary>
	public sealed class ModSortOrderChangedEventArgs : EventArgs
	{
		/// <summary>
		/// Initializes a Sort assignment change notification.
		/// </summary>
		public ModSortOrderChangedEventArgs(string archivePath, int? sortNumber)
		{
			ArchivePath = archivePath;
			SortNumber = sortNumber;
		}

		public string ArchivePath { get; }
		public int? SortNumber { get; }
	}

	/// <summary>
	/// Resolves and caches durable user-controlled Sort assignments for the active game storage.
	/// </summary>
	public sealed class ModSortOrderService
	{
		private readonly object _syncRoot = new object();
		private readonly ModSortOrderStore _store;
		private readonly Dictionary<long, ModSortOrderRecord> _recordsById = new Dictionary<long, ModSortOrderRecord>();
		private readonly Dictionary<string, List<ModSortOrderRecord>> _recordsByLocator = new Dictionary<string, List<ModSortOrderRecord>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, List<ModSortOrderRecord>> _recordsByRepositoryFile = new Dictionary<string, List<ModSortOrderRecord>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, ModSortOrderRecord> _resolvedByLocator = new Dictionary<string, ModSortOrderRecord>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, CurrentArchiveState> _currentArchivesByLocator = new Dictionary<string, CurrentArchiveState>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, HashSet<string>> _currentLocatorsByRepositoryFile = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, HashSet<string>> _currentManagedLocatorsByModId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Tracks the current archive objects that may own a live binding. Managed archives may donate MIN values; placeholders only protect bindings.
		/// </summary>
		private sealed class CurrentArchiveState
		{
			public CurrentArchiveState(string locator)
			{
				Locator = locator;
			}

			public string Locator { get; }
			public List<IMod> ManagedMods { get; } = new List<IMod>();
			public List<IMod> BindingProtectors { get; } = new List<IMod>();
			public string ModId { get; set; }
			public string DownloadId { get; set; }
			public bool IdentityConflict { get; set; }
			public string IndexedRepositoryKey { get; set; }
			public string IndexedManagedModId { get; set; }
			public bool HasManagedArchive => ManagedMods.Count > 0;
			public bool ProtectsBinding => HasManagedArchive || BindingProtectors.Count > 0;
		}

		/// <summary>
		/// Initializes the service and loads durable assignment rows into memory once.
		/// </summary>
		public ModSortOrderService(ModSortOrderStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
			foreach (var record in _store.LoadAll())
			{
				AddRecord(record);
			}
		}

		/// <summary>
		/// Raised after a durable write succeeds or an existing durable row becomes the active in-memory assignment.
		/// </summary>
		public event EventHandler<ModSortOrderChangedEventArgs> AssignmentChanged = delegate { };

		/// <summary>
		/// Rebuilds the current-archive inventory before startup assignment resolution or after a collection Reset.
		/// </summary>
		public void RebuildCurrentArchiveInventory(IEnumerable<IMod> managedMods, IEnumerable<IMod> bindingProtectors)
		{
			lock (_syncRoot)
			{
				_currentArchivesByLocator.Clear();
				_currentLocatorsByRepositoryFile.Clear();
				_currentManagedLocatorsByModId.Clear();
				if (managedMods != null)
				{
					foreach (var mod in managedMods)
					{
						TrackCurrentArchive(mod, true);
					}
				}
				if (bindingProtectors != null)
				{
					foreach (var mod in bindingProtectors)
					{
						TrackCurrentArchive(mod, false);
					}
				}

				foreach (var locator in _resolvedByLocator.Keys.Where(x => !_currentArchivesByLocator.ContainsKey(x)).ToList())
				{
					_resolvedByLocator.Remove(locator);
				}
			}
		}

		/// <summary>
		/// Adds or refreshes one current managed archive. Managed archives are eligible MIN donors.
		/// </summary>
		public void TrackManagedMod(IMod mod)
		{
			if (mod == null)
			{
				return;
			}
			lock (_syncRoot)
			{
				TrackCurrentArchive(mod, true);
			}
		}

		/// <summary>
		/// Removes one managed archive from the live inventory while retaining its durable historical assignment.
		/// </summary>
		public void UntrackManagedMod(IMod mod)
		{
			if (mod == null)
			{
				return;
			}
			lock (_syncRoot)
			{
				UntrackCurrentArchive(mod, true);
			}
		}

		/// <summary>
		/// Adds an active placeholder that protects an installed archive binding but never donates a MIN value.
		/// </summary>
		public void TrackBindingProtector(IMod mod)
		{
			if (mod == null)
			{
				return;
			}
			lock (_syncRoot)
			{
				TrackCurrentArchive(mod, false);
			}
		}

		/// <summary>
		/// Removes an active placeholder from the live inventory without deleting historical metadata.
		/// </summary>
		public void UntrackBindingProtector(IMod mod)
		{
			if (mod == null)
			{
				return;
			}
			lock (_syncRoot)
			{
				UntrackCurrentArchive(mod, false);
			}
		}

		/// <summary>
		/// Reconciles newly available repository identity for any current assignment without allowing passive rows to inherit.
		/// </summary>
		public int? ReconcileIdentity(IMod mod)
		{
			if (mod == null)
			{
				throw new ArgumentNullException(nameof(mod));
			}

			ModSortOrderRecord resolved;
			string locator;
			lock (_syncRoot)
			{
				locator = GetLocator(mod);
				var current = GetResolvedRecord(locator);
				if (current == null)
				{
					resolved = ResolveStartupOrDiscovery(mod, locator, null);
				}
				else if (HasIdentityConflict(current, mod))
				{
					// Do not merge a known different repository file into this durable assignment.
					resolved = current;
				}
				else if (current.AssignmentState == ModSortOrderAssignmentState.PendingAddIdentity)
				{
					resolved = ResolvePendingAddIdentity(mod, locator, null);
				}
				else if (IsExplicit(current))
				{
					resolved = ReconcileExplicitIdentity(current, mod, locator);
				}
				else
				{
					resolved = AttachIdentityIfNeeded(current, mod, locator);
				}

				Bind(locator, resolved);
				RefreshTrackedArchiveIdentity(locator);
			}

			AssignmentChanged(this, new ModSortOrderChangedEventArgs(locator, resolved.SortNumber));
			return resolved.SortNumber;
		}

		/// <summary>
		/// Resolves an assignment for a lifecycle boundary without performing UI-time I/O.
		/// </summary>
		public int? Resolve(IMod mod, ModSortOrderAssignmentContext context, IEnumerable<IMod> managedMods = null)
		{
			if (mod == null)
			{
				throw new ArgumentNullException(nameof(mod));
			}
			if (context == ModSortOrderAssignmentContext.ExplicitEdit)
			{
				throw new ArgumentException("Explicit edits must use SetSortNumber().", nameof(context));
			}

			ModSortOrderRecord resolved;
			string locator;
			lock (_syncRoot)
			{
				locator = GetLocator(mod);
				switch (context)
				{
					case ModSortOrderAssignmentContext.StartupOrDiscovery:
						resolved = ResolveStartupOrDiscovery(mod, locator, managedMods);
						break;
					case ModSortOrderAssignmentContext.AddOrDownload:
						resolved = ResolveAddOrDownload(mod, locator, managedMods);
						break;
					case ModSortOrderAssignmentContext.IdentityResolvedForPendingAdd:
						resolved = ResolvePendingAddIdentity(mod, locator, managedMods);
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(context));
				}
				Bind(locator, resolved);
				RefreshTrackedArchiveIdentity(locator);
			}

			AssignmentChanged(this, new ModSortOrderChangedEventArgs(locator, resolved.SortNumber));
			return resolved.SortNumber;
		}

		/// <summary>
		/// Resolves a confirmed Add/download using repository identity supplied by the originating download context when available.
		/// </summary>
		public int? ResolveAddOrDownload(IMod mod, IEnumerable<IMod> managedMods, string repositoryModId, string repositoryDownloadId)
		{
			if (mod == null)
			{
				throw new ArgumentNullException(nameof(mod));
			}

			ModSortOrderRecord resolved;
			string locator;
			lock (_syncRoot)
			{
				locator = GetLocator(mod);
				resolved = ResolveAddOrDownload(mod, locator, managedMods, repositoryModId, repositoryDownloadId);
				Bind(locator, resolved);
				RefreshTrackedArchiveIdentity(locator);
			}

			AssignmentChanged(this, new ModSortOrderChangedEventArgs(locator, resolved.SortNumber));
			return resolved.SortNumber;
		}

		/// <summary>
		/// Durably sets or clears one mod's Sort value and updates memory only after the commit succeeds.
		/// </summary>
		public void SetSortNumber(IMod mod, int? sortNumber)
		{
			if (mod == null)
			{
				throw new ArgumentNullException(nameof(mod));
			}

			ModSortOrderRecord saved;
			string locator;
			lock (_syncRoot)
			{
				locator = GetLocator(mod);
				var current = GetRecordForExplicitEdit(mod, locator);
				var state = sortNumber.HasValue ? ModSortOrderAssignmentState.ExplicitNumeric : ModSortOrderAssignmentState.ExplicitBlank;
				var edited = new ModSortOrderRecord(
					current?.AssignmentId ?? 0,
					locator,
					PreferUsableId(current?.ModId, mod.Id),
					PreferUsableId(current?.DownloadId, mod.DownloadId),
					sortNumber,
					state,
					DateTime.UtcNow);
				saved = PersistRecord(edited);
				Bind(locator, saved);
				RefreshTrackedArchiveIdentity(locator);
			}

			AssignmentChanged(this, new ModSortOrderChangedEventArgs(locator, saved.SortNumber));
		}

		/// <summary>
		/// Gets the already-resolved Sort value from memory only.
		/// </summary>
		public int? GetSortNumber(IMod mod)
		{
			if (mod == null)
			{
				return null;
			}

			var locator = GetLocator(mod);
			lock (_syncRoot)
			{
				return _resolvedByLocator.TryGetValue(locator, out var record) ? record.SortNumber : (int?)null;
			}
		}

		/// <summary>
		/// Tries to get an already-resolved Sort value from memory only.
		/// </summary>
		public bool TryGetSortNumber(IMod mod, out int? sortNumber)
		{
			sortNumber = null;
			if (mod == null)
			{
				return false;
			}

			var locator = GetLocator(mod);
			lock (_syncRoot)
			{
				if (!_resolvedByLocator.TryGetValue(locator, out var record))
				{
					return false;
				}
				sortNumber = record.SortNumber;
				return true;
			}
		}

		/// <summary>
		/// Gets whether the currently bound row is waiting for identity from an explicit Add/download lifecycle.
		/// </summary>
		public bool IsPendingAddIdentity(IMod mod)
		{
			if (mod == null)
			{
				return false;
			}

			var locator = GetLocator(mod);
			lock (_syncRoot)
			{
				return _resolvedByLocator.TryGetValue(locator, out var record) && record.AssignmentState == ModSortOrderAssignmentState.PendingAddIdentity;
			}
		}

		/// <summary>
		/// Resolves startup or passive discovery without ever inheriting from sibling mods.
		/// </summary>
		private ModSortOrderRecord ResolveStartupOrDiscovery(IMod mod, string locator, IEnumerable<IMod> managedMods)
		{
			var current = GetCompatibleResolvedRecord(mod, locator);
			if (IsExplicit(current))
			{
				return ReconcileExplicitIdentity(current, mod, locator);
			}

			if (HasExactRepositoryIdentity(mod))
			{
				var exactAtLocator = FindExactAtLocator(mod, locator);
				if (exactAtLocator != null)
				{
					if (current != null && current.AssignmentId != exactAtLocator.AssignmentId)
					{
						var rebound = CreateRecord(exactAtLocator.AssignmentId, locator, mod, exactAtLocator.SortNumber, exactAtLocator.AssignmentState);
						return PersistRecord(rebound, current.AssignmentId);
					}
					return exactAtLocator;
				}

				var exactHistorical = FindUniqueExactRepositoryRecord(mod, locator, current?.AssignmentId, managedMods);
				if (exactHistorical != null)
				{
					var rebound = CreateRecord(exactHistorical.AssignmentId, locator, mod, exactHistorical.SortNumber, exactHistorical.AssignmentState);
					return PersistRecord(rebound, current?.AssignmentId);
				}

				var partialPathRecord = FindUniquePathRecord(mod, locator);
				if (partialPathRecord != null)
				{
					return AttachIdentityIfNeeded(partialPathRecord, mod, locator);
				}

				if (current != null && current.AssignmentState == ModSortOrderAssignmentState.InheritedNumeric)
				{
					return AttachIdentityIfNeeded(current, mod, locator);
				}

				if (IsTransient(current))
				{
					return AttachIdentityIfNeeded(current, mod, locator);
				}

				return CreateRecord(0, locator, mod, null, ModSortOrderAssignmentState.BaselineBlank);
			}

			if (current != null)
			{
				return AttachIdentityIfNeeded(current, mod, locator);
			}

			var pathRecord = FindUniquePathRecord(mod, locator);
			if (pathRecord != null)
			{
				return AttachIdentityIfNeeded(pathRecord, mod, locator);
			}

			return CreateRecord(0, locator, mod, null, ModSortOrderAssignmentState.BaselineBlank);
		}

		/// <summary>
		/// Resolves a genuinely new add or download, restoring an exact file before applying sibling inheritance.
		/// </summary>
		private ModSortOrderRecord ResolveAddOrDownload(IMod mod, string locator, IEnumerable<IMod> managedMods, string repositoryModId = null, string repositoryDownloadId = null)
		{
			var modId = GetEffectiveModId(mod, repositoryModId);
			var downloadId = GetEffectiveDownloadId(mod, repositoryModId, repositoryDownloadId);
			var current = GetCompatibleResolvedRecord(locator, modId, downloadId);
			if (IsExplicit(current))
			{
				return ReconcileExplicitIdentity(current, locator, modId, downloadId, managedMods);
			}

			if (HasExactRepositoryIdentity(modId, downloadId))
			{
				var exactAtLocator = FindExactAtLocator(modId, downloadId, locator);
				if (exactAtLocator != null)
				{
					if (current != null && current.AssignmentId != exactAtLocator.AssignmentId)
					{
						var rebound = CreateRecord(exactAtLocator.AssignmentId, locator, modId, downloadId, exactAtLocator.SortNumber, exactAtLocator.AssignmentState);
						return PersistRecord(rebound, current.AssignmentId);
					}
					return exactAtLocator;
				}

				var exactHistorical = FindUniqueExactRepositoryRecord(modId, downloadId, locator, current?.AssignmentId, managedMods);
				if (exactHistorical != null)
				{
					var rebound = CreateRecord(exactHistorical.AssignmentId, locator, modId, downloadId, exactHistorical.SortNumber, exactHistorical.AssignmentState);
					return PersistRecord(rebound, current?.AssignmentId);
				}
			}

			if (current != null && current.AssignmentState == ModSortOrderAssignmentState.InheritedNumeric)
			{
				return AttachIdentityIfNeeded(current, locator, modId, downloadId);
			}

			if (!ModFileIdentity.IsUsableRepositoryId(modId))
			{
				var localRecord = FindUniquePathRecord(modId, locator);
				if (localRecord != null)
				{
					return AttachIdentityIfNeeded(localRecord, locator, modId, downloadId);
				}

				if (current != null && current.AssignmentState == ModSortOrderAssignmentState.PendingAddIdentity)
				{
					return current;
				}

				return PersistRecord(CreateRecord(0, locator, modId, downloadId, null, ModSortOrderAssignmentState.PendingAddIdentity));
			}

			var inheritedValue = GetMinimumSiblingValue(modId, locator, managedMods);
			var hasExactIdentity = HasExactRepositoryIdentity(modId, downloadId);
			var state = !hasExactIdentity
				? ModSortOrderAssignmentState.PendingAddIdentity
				: (inheritedValue.HasValue ? ModSortOrderAssignmentState.InheritedNumeric : ModSortOrderAssignmentState.BaselineBlank);
			if (IsTransient(current))
			{
				var updated = CreateRecord(current.AssignmentId, locator, modId, downloadId, inheritedValue, state);
				return PersistRecord(updated);
			}
			return PersistRecord(CreateRecord(0, locator, modId, downloadId, inheritedValue, state));
		}

		/// <summary>
		/// Finalizes one pending Add/download row when enough repository identity becomes available.
		/// </summary>
		private ModSortOrderRecord ResolvePendingAddIdentity(IMod mod, string locator, IEnumerable<IMod> managedMods)
		{
			var current = GetCompatibleResolvedRecord(mod, locator);
			if (current == null)
			{
				return ResolveStartupOrDiscovery(mod, locator, managedMods);
			}

			if (IsExplicit(current))
			{
				return ReconcileExplicitIdentity(current, mod, locator);
			}
			if (current.AssignmentState != ModSortOrderAssignmentState.PendingAddIdentity)
			{
				return AttachIdentityIfNeeded(current, mod, locator);
			}
			if (!ModFileIdentity.IsUsableRepositoryId(mod.Id))
			{
				return AttachIdentityIfNeeded(current, mod, locator);
			}

			if (HasExactRepositoryIdentity(mod))
			{
				var exactHistorical = FindUniqueExactRepositoryRecord(mod, locator, current.AssignmentId, managedMods);
				if (exactHistorical != null)
				{
					var rebound = CreateRecord(exactHistorical.AssignmentId, locator, mod, exactHistorical.SortNumber, exactHistorical.AssignmentState);
					return PersistRecord(rebound, current.AssignmentId);
				}
			}

			var inheritedValue = GetMinimumSiblingValue(mod, locator, managedMods);
			var state = HasExactRepositoryIdentity(mod)
				? (inheritedValue.HasValue ? ModSortOrderAssignmentState.InheritedNumeric : ModSortOrderAssignmentState.BaselineBlank)
				: ModSortOrderAssignmentState.PendingAddIdentity;
			return PersistRecord(CreateRecord(current.AssignmentId, locator, mod, inheritedValue, state));
		}

		/// <summary>
		/// Selects the durable row that an explicit edit should modify without letting a stale path overwrite another repository file.
		/// </summary>
		private ModSortOrderRecord GetRecordForExplicitEdit(IMod mod, string locator)
		{
			var current = GetCompatibleResolvedRecord(mod, locator);
			if (current != null)
			{
				return IsExplicit(current) ? ReconcileExplicitIdentity(current, mod, locator) : AttachIdentityIfNeeded(current, mod, locator);
			}

			if (HasExactRepositoryIdentity(mod))
			{
				var exactAtLocator = FindExactAtLocator(mod, locator);
				if (exactAtLocator != null)
				{
					return exactAtLocator;
				}

				var exactHistorical = FindUniqueExactRepositoryRecord(mod, locator, null);
				if (exactHistorical != null)
				{
					return exactHistorical;
				}
				return null;
			}

			return FindUniquePathRecord(mod, locator);
		}

		/// <summary>
		/// Preserves an explicit user value while consolidating a newly resolved exact repository identity.
		/// </summary>
		private ModSortOrderRecord ReconcileExplicitIdentity(ModSortOrderRecord current, IMod mod, string locator)
		{
			return ReconcileExplicitIdentity(current, locator, mod.Id, mod.DownloadId, null);
		}

		/// <summary>
		/// Preserves an explicit user value while reconciling a supplied repository identity.
		/// </summary>
		private ModSortOrderRecord ReconcileExplicitIdentity(ModSortOrderRecord current, string locator, string modId, string downloadId, IEnumerable<IMod> managedMods)
		{
			if (current == null || !HasExactRepositoryIdentity(modId, downloadId) || IsSameRepositoryFile(current, modId, downloadId))
			{
				return AttachIdentityIfNeeded(current, locator, modId, downloadId);
			}

			var exactHistorical = FindUniqueExactRepositoryRecord(modId, downloadId, locator, current.AssignmentId, managedMods);
			if (exactHistorical == null)
			{
				return AttachIdentityIfNeeded(current, locator, modId, downloadId);
			}

			var replacement = CreateRecord(exactHistorical.AssignmentId, locator, modId, downloadId, current.SortNumber, current.AssignmentState);
			return PersistRecord(replacement, current.AssignmentId);
		}

		/// <summary>
		/// Adds newly available non-conflicting repository identity to an existing row without changing its Sort value.
		/// </summary>
		private ModSortOrderRecord AttachIdentityIfNeeded(ModSortOrderRecord record, IMod mod, string locator)
		{
			return AttachIdentityIfNeeded(record, locator, mod.Id, mod.DownloadId);
		}

		/// <summary>
		/// Adds newly available non-conflicting repository identity supplied by a lifecycle boundary.
		/// </summary>
		private ModSortOrderRecord AttachIdentityIfNeeded(ModSortOrderRecord record, string locator, string modId, string downloadId)
		{
			if (record == null)
			{
				return null;
			}
			if (HasIdentityConflict(record, modId, downloadId))
			{
				return record;
			}

			var resolvedModId = PreferUsableId(record.ModId, modId);
			var resolvedDownloadId = PreferUsableId(record.DownloadId, downloadId);
			if (string.Equals(record.ArchivePath, locator, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(record.ModId, resolvedModId, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(record.DownloadId, resolvedDownloadId, StringComparison.OrdinalIgnoreCase))
			{
				return record;
			}

			var updated = new ModSortOrderRecord(record.AssignmentId, locator, resolvedModId, resolvedDownloadId, record.SortNumber, record.AssignmentState, record.UpdatedUtc);
			return PersistRecord(updated);
		}

		/// <summary>
		/// Finds a currently bound row only when its known repository identity does not conflict with the mod.
		/// </summary>
		private ModSortOrderRecord GetCompatibleResolvedRecord(IMod mod, string locator)
		{
			return GetCompatibleResolvedRecord(locator, mod.Id, mod.DownloadId);
		}

		/// <summary>
		/// Finds a currently bound row only when its known repository identity does not conflict with supplied IDs.
		/// </summary>
		private ModSortOrderRecord GetCompatibleResolvedRecord(string locator, string modId, string downloadId)
		{
			var current = GetResolvedRecord(locator);
			return current != null && !HasIdentityConflict(current, modId, downloadId) ? current : null;
		}

		/// <summary>
		/// Gets the current in-memory binding for a locator.
		/// </summary>
		private ModSortOrderRecord GetResolvedRecord(string locator)
		{
			return _resolvedByLocator.TryGetValue(locator, out var record) ? record : null;
		}

		/// <summary>
		/// Finds an exact repository-file row already stored at the current locator.
		/// </summary>
		private ModSortOrderRecord FindExactAtLocator(IMod mod, string locator)
		{
			return FindExactAtLocator(mod.Id, mod.DownloadId, locator);
		}

		/// <summary>
		/// Finds an exact supplied repository-file identity already stored at the current locator.
		/// </summary>
		private ModSortOrderRecord FindExactAtLocator(string modId, string downloadId, string locator)
		{
			if (!_recordsByLocator.TryGetValue(locator, out var records))
			{
				return null;
			}

			var matches = records.Where(x => IsSameRepositoryFile(x, modId, downloadId)).ToList();
			return matches.Count == 1 ? matches[0] : null;
		}

		/// <summary>
		/// Finds a unique historical repository-file row that can be rebound without stealing a currently bound copy.
		/// </summary>
		private ModSortOrderRecord FindUniqueExactRepositoryRecord(IMod mod, string targetLocator, long? excludedAssignmentId, IEnumerable<IMod> managedMods = null)
		{
			return FindUniqueExactRepositoryRecord(mod.Id, mod.DownloadId, targetLocator, excludedAssignmentId, managedMods);
		}

		/// <summary>
		/// Finds a unique historical repository-file row without rebinding an identity that has multiple current copies.
		/// </summary>
		private ModSortOrderRecord FindUniqueExactRepositoryRecord(string modId, string downloadId, string targetLocator, long? excludedAssignmentId, IEnumerable<IMod> managedMods)
		{
			if (!HasExactRepositoryIdentity(modId, downloadId))
			{
				return null;
			}

			var key = GetRepositoryKey(modId, downloadId);
			if (!_recordsByRepositoryFile.TryGetValue(key, out var records))
			{
				return null;
			}

			if (HasOtherCurrentRepositoryCopy(modId, downloadId, targetLocator, managedMods))
			{
				return null;
			}

			var candidates = records.Where(x => !excludedAssignmentId.HasValue || x.AssignmentId != excludedAssignmentId.Value).ToList();
			if (candidates.Count != 1)
			{
				return null;
			}

			var candidate = candidates[0];
			foreach (var binding in _resolvedByLocator)
			{
				if (binding.Value.AssignmentId == candidate.AssignmentId && !string.Equals(binding.Key, targetLocator, StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}
			}
			return candidate;
		}

		/// <summary>
		/// Determines whether another current managed archive or binding protector has the supplied exact repository identity.
		/// </summary>
		private bool HasOtherCurrentRepositoryCopy(string modId, string downloadId, string targetLocator, IEnumerable<IMod> managedMods)
		{
			if (!HasExactRepositoryIdentity(modId, downloadId) ||
				!_currentLocatorsByRepositoryFile.TryGetValue(GetRepositoryKey(modId, downloadId), out var locators))
			{
				return false;
			}

			return locators.Any(x => !string.Equals(x, targetLocator, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Finds one unambiguous path-based record for a file without a reliable repository-file identity.
		/// </summary>
		private ModSortOrderRecord FindUniquePathRecord(IMod mod, string locator)
		{
			return FindUniquePathRecord(mod.Id, locator);
		}

		/// <summary>
		/// Finds one unambiguous path-based record using the supplied repository page identity when available.
		/// </summary>
		private ModSortOrderRecord FindUniquePathRecord(string modId, string locator)
		{
			if (!_recordsByLocator.TryGetValue(locator, out var records))
			{
				return null;
			}

			var hasModId = ModFileIdentity.IsUsableRepositoryId(modId);
			if (!hasModId)
			{
				var pathOnly = records.Where(x => !ModFileIdentity.IsUsableRepositoryId(x.ModId) && !ModFileIdentity.IsUsableRepositoryId(x.DownloadId)).ToList();
				if (pathOnly.Count == 1)
				{
					return pathOnly[0];
				}
				return pathOnly.Count == 0 && records.Count == 1 ? records[0] : null;
			}

			var candidates = records.Where(x =>
				!HasExactRepositoryIdentity(x) &&
				ModFileIdentity.IsUsableRepositoryId(x.ModId) &&
				string.Equals(x.ModId, modId, StringComparison.OrdinalIgnoreCase) &&
				!ModFileIdentity.IsUsableRepositoryId(x.DownloadId)).ToList();
			return candidates.Count == 1 ? candidates[0] : null;
		}

		/// <summary>
		/// Computes the minimum non-blank Sort value from currently present managed, already-resolved same-ModId siblings.
		/// </summary>
		private int? GetMinimumSiblingValue(IMod mod, string locator, IEnumerable<IMod> managedMods)
		{
			return GetMinimumSiblingValue(mod.Id, locator, managedMods);
		}

		/// <summary>
		/// Computes the minimum non-blank Sort value for a supplied repository page identity.
		/// </summary>
		private int? GetMinimumSiblingValue(string modId, string locator, IEnumerable<IMod> managedMods)
		{
			if (!ModFileIdentity.IsUsableRepositoryId(modId) ||
				!_currentManagedLocatorsByModId.TryGetValue(GetRepositoryIdKey(modId), out var siblingLocators))
			{
				return null;
			}

			int? minimum = null;
			foreach (var siblingLocator in siblingLocators)
			{
				if (string.Equals(siblingLocator, locator, StringComparison.OrdinalIgnoreCase) ||
					!_resolvedByLocator.TryGetValue(siblingLocator, out var siblingRecord) ||
					!siblingRecord.SortNumber.HasValue)
				{
					continue;
				}

				if (!minimum.HasValue || siblingRecord.SortNumber.Value < minimum.Value)
				{
					minimum = siblingRecord.SortNumber.Value;
				}
			}
			return minimum;
		}

		/// <summary>
		/// Tracks one current archive object by reference and refreshes its effective trusted identity.
		/// </summary>
		private void TrackCurrentArchive(IMod mod, bool managed)
		{
			if (mod == null)
			{
				return;
			}

			var locator = GetLocator(mod);
			if (!_currentArchivesByLocator.TryGetValue(locator, out var state))
			{
				state = new CurrentArchiveState(locator);
				_currentArchivesByLocator[locator] = state;
			}

			var list = managed ? state.ManagedMods : state.BindingProtectors;
			if (!ContainsReference(list, mod))
			{
				list.Add(mod);
			}
			RefreshCurrentArchiveIdentity(state);
		}

		/// <summary>
		/// Removes one current archive object by reference and drops only the live binding when the locator is no longer represented.
		/// </summary>
		private void UntrackCurrentArchive(IMod mod, bool managed)
		{
			var locator = GetLocator(mod);
			if (!_currentArchivesByLocator.TryGetValue(locator, out var state))
			{
				return;
			}

			RemoveReference(managed ? state.ManagedMods : state.BindingProtectors, mod);
			if (!state.ProtectsBinding)
			{
				RemoveCurrentArchiveIndexes(state);
				_currentArchivesByLocator.Remove(locator);
				_resolvedByLocator.Remove(locator);
				return;
			}
			RefreshCurrentArchiveIdentity(state);
		}

		/// <summary>
		/// Refreshes identity for a currently tracked locator after a successful durable assignment change.
		/// </summary>
		private void RefreshTrackedArchiveIdentity(string locator)
		{
			if (_currentArchivesByLocator.TryGetValue(locator, out var state))
			{
				RefreshCurrentArchiveIdentity(state);
			}
		}

		/// <summary>
		/// Resolves current identity from live metadata plus compatible saved/bound records without performing I/O.
		/// </summary>
		private void RefreshCurrentArchiveIdentity(CurrentArchiveState state)
		{
			RemoveCurrentArchiveIndexes(state);
			state.ModId = null;
			state.DownloadId = null;
			state.IdentityConflict = false;

			foreach (var mod in state.ManagedMods)
			{
				MergeCurrentIdentity(state, mod.Id, mod.DownloadId);
			}
			foreach (var mod in state.BindingProtectors)
			{
				MergeCurrentIdentity(state, mod.Id, mod.DownloadId);
			}

			if (_resolvedByLocator.TryGetValue(state.Locator, out var boundRecord))
			{
				if (HasIdentityConflict(boundRecord, state.ModId, state.DownloadId))
				{
					state.IdentityConflict = true;
				}
				else
				{
					MergeCurrentIdentity(state, boundRecord.ModId, boundRecord.DownloadId);
				}
			}

			if (!state.IdentityConflict && _recordsByLocator.TryGetValue(state.Locator, out var records))
			{
				var compatible = records.Where(x => !HasIdentityConflict(x, state.ModId, state.DownloadId)).ToList();
				if (!ModFileIdentity.IsUsableRepositoryId(state.ModId))
				{
					var modIds = compatible.Select(x => x.ModId).Where(ModFileIdentity.IsUsableRepositoryId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					if (modIds.Count == 1)
					{
						state.ModId = modIds[0];
					}
				}

				if (!ModFileIdentity.IsUsableRepositoryId(state.DownloadId) && ModFileIdentity.IsUsableRepositoryId(state.ModId))
				{
					var downloadIds = compatible
						.Where(x => !ModFileIdentity.IsUsableRepositoryId(x.ModId) || string.Equals(x.ModId, state.ModId, StringComparison.OrdinalIgnoreCase))
						.Select(x => x.DownloadId)
						.Where(ModFileIdentity.IsUsableRepositoryId)
						.Distinct(StringComparer.OrdinalIgnoreCase)
						.ToList();
					if (downloadIds.Count == 1)
					{
						state.DownloadId = downloadIds[0];
					}
				}
			}

			IndexCurrentArchive(state);
		}

		/// <summary>
		/// Adds the current locator to exact-file and MIN-donor indexes after its effective identity is stable.
		/// </summary>
		private void IndexCurrentArchive(CurrentArchiveState state)
		{
			if (state.IdentityConflict)
			{
				return;
			}

			if (state.ProtectsBinding && HasExactRepositoryIdentity(state.ModId, state.DownloadId))
			{
				state.IndexedRepositoryKey = GetRepositoryKey(state.ModId, state.DownloadId);
				AddCurrentLocator(_currentLocatorsByRepositoryFile, state.IndexedRepositoryKey, state.Locator);
			}

			if (state.HasManagedArchive && ModFileIdentity.IsUsableRepositoryId(state.ModId))
			{
				state.IndexedManagedModId = GetRepositoryIdKey(state.ModId);
				AddCurrentLocator(_currentManagedLocatorsByModId, state.IndexedManagedModId, state.Locator);
			}
		}

		/// <summary>
		/// Removes the locator's previous identity-index entries before recalculating its effective identity.
		/// </summary>
		private void RemoveCurrentArchiveIndexes(CurrentArchiveState state)
		{
			if (!string.IsNullOrEmpty(state.IndexedRepositoryKey))
			{
				RemoveCurrentLocator(_currentLocatorsByRepositoryFile, state.IndexedRepositoryKey, state.Locator);
				state.IndexedRepositoryKey = null;
			}

			if (!string.IsNullOrEmpty(state.IndexedManagedModId))
			{
				RemoveCurrentLocator(_currentManagedLocatorsByModId, state.IndexedManagedModId, state.Locator);
				state.IndexedManagedModId = null;
			}
		}

		/// <summary>
		/// Adds one locator to a current multi-value identity index.
		/// </summary>
		private static void AddCurrentLocator(IDictionary<string, HashSet<string>> index, string key, string locator)
		{
			if (!index.TryGetValue(key, out var locators))
			{
				locators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				index[key] = locators;
			}
			locators.Add(locator);
		}

		/// <summary>
		/// Removes one locator from a current multi-value identity index.
		/// </summary>
		private static void RemoveCurrentLocator(IDictionary<string, HashSet<string>> index, string key, string locator)
		{
			if (!index.TryGetValue(key, out var locators))
			{
				return;
			}

			locators.Remove(locator);
			if (locators.Count == 0)
			{
				index.Remove(key);
			}
		}

		/// <summary>
		/// Merges one trusted identity source, marking conflicts rather than silently combining unrelated repository files.
		/// </summary>
		private static void MergeCurrentIdentity(CurrentArchiveState state, string modId, string downloadId)
		{
			if (ModFileIdentity.IsUsableRepositoryId(modId))
			{
				if (ModFileIdentity.IsUsableRepositoryId(state.ModId) && !string.Equals(state.ModId, modId, StringComparison.OrdinalIgnoreCase))
				{
					state.IdentityConflict = true;
				}
				else
				{
					state.ModId = modId;
				}
			}

			if (ModFileIdentity.IsUsableRepositoryId(downloadId))
			{
				if (ModFileIdentity.IsUsableRepositoryId(state.DownloadId) && !string.Equals(state.DownloadId, downloadId, StringComparison.OrdinalIgnoreCase))
				{
					state.IdentityConflict = true;
				}
				else
				{
					state.DownloadId = downloadId;
				}
			}
		}

		/// <summary>
		/// Determines whether a list already contains the exact object reference.
		/// </summary>
		private static bool ContainsReference(IEnumerable<IMod> mods, IMod target)
		{
			return mods.Any(x => ReferenceEquals(x, target));
		}

		/// <summary>
		/// Removes the exact object reference without relying on mod filename equality.
		/// </summary>
		private static void RemoveReference(IList<IMod> mods, IMod target)
		{
			for (var index = mods.Count - 1; index >= 0; index--)
			{
				if (ReferenceEquals(mods[index], target))
				{
					mods.RemoveAt(index);
				}
			}
		}

		/// <summary>
		/// Persists one row, then updates all in-memory indexes only after the durable commit succeeds.
		/// </summary>
		private ModSortOrderRecord PersistRecord(ModSortOrderRecord record, long? obsoleteAssignmentId = null)
		{
			var saved = _store.Save(record, obsoleteAssignmentId);
			if (record.AssignmentId > 0)
			{
				RemoveRecord(record.AssignmentId);
			}
			if (obsoleteAssignmentId.HasValue && obsoleteAssignmentId.Value > 0 && obsoleteAssignmentId.Value != saved.AssignmentId)
			{
				RemoveRecord(obsoleteAssignmentId.Value);
			}
			AddRecord(saved);
			return saved;
		}

		/// <summary>
		/// Adds one durable row to the in-memory historical indexes.
		/// </summary>
		private void AddRecord(ModSortOrderRecord record)
		{
			_recordsById[record.AssignmentId] = record;
			AddIndexedRecord(_recordsByLocator, record.ArchivePath, record);
			if (HasExactRepositoryIdentity(record))
			{
				AddIndexedRecord(_recordsByRepositoryFile, GetRepositoryKey(record.ModId, record.DownloadId), record);
			}
		}

		/// <summary>
		/// Removes one durable row from the in-memory historical and active indexes.
		/// </summary>
		private void RemoveRecord(long assignmentId)
		{
			if (!_recordsById.TryGetValue(assignmentId, out var record))
			{
				return;
			}

			_recordsById.Remove(assignmentId);
			RemoveIndexedRecord(_recordsByLocator, record.ArchivePath, assignmentId);
			if (HasExactRepositoryIdentity(record))
			{
				RemoveIndexedRecord(_recordsByRepositoryFile, GetRepositoryKey(record.ModId, record.DownloadId), assignmentId);
			}

			foreach (var locator in _resolvedByLocator.Where(x => x.Value.AssignmentId == assignmentId).Select(x => x.Key).ToList())
			{
				_resolvedByLocator.Remove(locator);
			}
		}

		/// <summary>
		/// Adds a record to one multi-value in-memory index.
		/// </summary>
		private static void AddIndexedRecord(IDictionary<string, List<ModSortOrderRecord>> index, string key, ModSortOrderRecord record)
		{
			if (!index.TryGetValue(key, out var records))
			{
				records = new List<ModSortOrderRecord>();
				index[key] = records;
			}
			records.Add(record);
		}

		/// <summary>
		/// Removes a record from one multi-value in-memory index.
		/// </summary>
		private static void RemoveIndexedRecord(IDictionary<string, List<ModSortOrderRecord>> index, string key, long assignmentId)
		{
			if (!index.TryGetValue(key, out var records))
			{
				return;
			}
			records.RemoveAll(x => x.AssignmentId == assignmentId);
			if (records.Count == 0)
			{
				index.Remove(key);
			}
		}

		/// <summary>
		/// Binds a durable row to the currently present archive locator.
		/// </summary>
		private void Bind(string locator, ModSortOrderRecord record)
		{
			if (record == null)
			{
				_resolvedByLocator.Remove(locator);
				return;
			}
			_resolvedByLocator[locator] = record;
		}

		/// <summary>
		/// Creates a row using only repository identifiers that are safe for identity matching.
		/// </summary>
		private static ModSortOrderRecord CreateRecord(long assignmentId, string locator, IMod mod, int? sortNumber, ModSortOrderAssignmentState state)
		{
			return CreateRecord(assignmentId, locator, mod.Id, mod.DownloadId, sortNumber, state);
		}

		/// <summary>
		/// Creates a row from supplied repository identifiers that are safe for identity matching.
		/// </summary>
		private static ModSortOrderRecord CreateRecord(long assignmentId, string locator, string modId, string downloadId, int? sortNumber, ModSortOrderAssignmentState state)
		{
			return new ModSortOrderRecord(
				assignmentId,
				locator,
				ModFileIdentity.IsUsableRepositoryId(modId) ? modId : null,
				ModFileIdentity.IsUsableRepositoryId(downloadId) ? downloadId : null,
				sortNumber,
				state,
				DateTime.UtcNow);
		}

		/// <summary>
		/// Gets the normalized archive locator without performing file-system I/O.
		/// </summary>
		private string GetLocator(IMod mod)
		{
			var path = !string.IsNullOrWhiteSpace(mod.ModArchivePath) ? mod.ModArchivePath : mod.Filename;
			return _store.GetArchiveLocator(path);
		}

		/// <summary>
		/// Determines whether a row and mod identify different known repository files.
		/// </summary>
		private static bool HasIdentityConflict(ModSortOrderRecord record, IMod mod)
		{
			return HasIdentityConflict(record, mod.Id, mod.DownloadId);
		}

		/// <summary>
		/// Determines whether a row and supplied repository identifiers describe different known files.
		/// </summary>
		private static bool HasIdentityConflict(ModSortOrderRecord record, string modId, string downloadId)
		{
			if (HasExactRepositoryIdentity(record) && HasExactRepositoryIdentity(modId, downloadId))
			{
				return !IsSameRepositoryFile(record, modId, downloadId);
			}
			if (ModFileIdentity.IsUsableRepositoryId(record.ModId) && ModFileIdentity.IsUsableRepositoryId(modId) &&
				!string.Equals(record.ModId, modId, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (ModFileIdentity.IsUsableRepositoryId(record.DownloadId) && ModFileIdentity.IsUsableRepositoryId(downloadId) &&
				!string.Equals(record.DownloadId, downloadId, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Determines whether a mod has a reliable repository-file identity.
		/// </summary>
		private static bool HasExactRepositoryIdentity(IMod mod)
		{
			return HasExactRepositoryIdentity(mod.Id, mod.DownloadId);
		}

		/// <summary>
		/// Determines whether supplied repository identifiers form a reliable repository-file identity.
		/// </summary>
		private static bool HasExactRepositoryIdentity(string modId, string downloadId)
		{
			return ModFileIdentity.IsUsableRepositoryId(modId) && ModFileIdentity.IsUsableRepositoryId(downloadId);
		}

		/// <summary>
		/// Determines whether a persisted row has a reliable repository-file identity.
		/// </summary>
		private static bool HasExactRepositoryIdentity(ModSortOrderRecord record)
		{
			return ModFileIdentity.IsUsableRepositoryId(record.ModId) && ModFileIdentity.IsUsableRepositoryId(record.DownloadId);
		}

		/// <summary>
		/// Determines whether a persisted row identifies the supplied repository file.
		/// </summary>
		private static bool IsSameRepositoryFile(ModSortOrderRecord record, IMod mod)
		{
			return IsSameRepositoryFile(record, mod.Id, mod.DownloadId);
		}

		/// <summary>
		/// Determines whether a persisted row identifies the supplied repository file IDs.
		/// </summary>
		private static bool IsSameRepositoryFile(ModSortOrderRecord record, string modId, string downloadId)
		{
			return ModFileIdentity.IsSameRepositoryFile(record.ModId, record.DownloadId, modId, downloadId);
		}

		/// <summary>
		/// Determines whether a row contains a user decision that later identity resolution must preserve.
		/// </summary>
		private static bool IsExplicit(ModSortOrderRecord record)
		{
			return record != null && (record.AssignmentState == ModSortOrderAssignmentState.ExplicitBlank || record.AssignmentState == ModSortOrderAssignmentState.ExplicitNumeric);
		}

		/// <summary>
		/// Determines whether a current row may be replaced by a stronger exact-identity result.
		/// </summary>
		private static bool IsTransient(ModSortOrderRecord record)
		{
			return record != null && (record.AssignmentState == ModSortOrderAssignmentState.BaselineBlank || record.AssignmentState == ModSortOrderAssignmentState.PendingAddIdentity);
		}

		/// <summary>
		/// Selects the repository page identity, preferring an explicit trusted Add/download value.
		/// </summary>
		private static string GetEffectiveModId(IMod mod, string repositoryModId)
		{
			return ModFileIdentity.IsUsableRepositoryId(repositoryModId)
				? repositoryModId
				: (ModFileIdentity.IsUsableRepositoryId(mod.Id) ? mod.Id : null);
		}

		/// <summary>
		/// Selects the repository file identity without pairing a trusted page ID with a stale file ID from another page.
		/// </summary>
		private static string GetEffectiveDownloadId(IMod mod, string repositoryModId, string repositoryDownloadId)
		{
			if (ModFileIdentity.IsUsableRepositoryId(repositoryDownloadId))
			{
				return repositoryDownloadId;
			}

			if (ModFileIdentity.IsUsableRepositoryId(repositoryModId) &&
				(!ModFileIdentity.IsUsableRepositoryId(mod.Id) || !string.Equals(repositoryModId, mod.Id, StringComparison.OrdinalIgnoreCase)))
			{
				return null;
			}

			return ModFileIdentity.IsUsableRepositoryId(mod.DownloadId) ? mod.DownloadId : null;
		}

		/// <summary>
		/// Keeps an existing usable identifier, otherwise adopts a newly available usable identifier.
		/// </summary>
		private static string PreferUsableId(string current, string candidate)
		{
			if (ModFileIdentity.IsUsableRepositoryId(current))
			{
				return current;
			}
			return ModFileIdentity.IsUsableRepositoryId(candidate) ? candidate : null;
		}

		/// <summary>
		/// Builds the case-insensitive in-memory key for an exact repository file.
		/// </summary>
		private static string GetRepositoryKey(string modId, string downloadId)
		{
			return GetRepositoryIdKey(modId) + "\u001f" + GetRepositoryIdKey(downloadId);
		}

		/// <summary>
		/// Normalizes one repository identifier for case-insensitive in-memory identity indexes.
		/// </summary>
		private static string GetRepositoryIdKey(string repositoryId)
		{
			return (repositoryId ?? string.Empty).Trim();
		}
	}
}
