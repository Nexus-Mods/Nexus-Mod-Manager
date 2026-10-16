using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.IO;
using System.ComponentModel;
using ChinhDo.Transactions;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement
{
	public partial class VirtualModActivator
	{
		private readonly object _persistenceLock = new object ();
		private int _unobservableRecords;
		private IList<IVirtualModLink> _nativeReloadLinks;
		private readonly VirtualModStoreDelta _storeChanges = new VirtualModStoreDelta();
		private readonly HashSet<IVirtualModInfo> _observedMods = new HashSet<IVirtualModInfo>(ReferenceEqualityComparer<IVirtualModInfo>.Instance);
		private readonly HashSet<IVirtualModLink> _observedLinks = new HashSet<IVirtualModLink>(ReferenceEqualityComparer<IVirtualModLink>.Instance);
		/// <summary>Restores legacy XML into SQLite through the caller's native restore transaction.</summary>
		internal void RestoreStoreSnapshot(string sourceXml, string destinationXml, TxFileManager fileManager)
		{
			var xml = new XmlVirtualModStore();
			VirtualModStoreData data = xml.LoadForImport(sourceXml, CURRENT_VERSION, PerformVersionCheck, GetModFileVersionForLoadList);
			string temporary = Path.Combine(Path.GetTempPath(), FileUtil.CreateTempToken() + ".sqlite");
			try
			{
				var store = new SQLiteVirtualModStore(destinationXml, temporary, CURRENT_VERSION, PerformVersionCheck, xml);
				store.Initialize();
				store.SaveStoreData(CURRENT_VERSION, data);
				XmlVirtualModStore.ValidateImport(CURRENT_VERSION, data, store.Load(destinationXml, CURRENT_VERSION, PerformVersionCheck, GetModFileVersionForLoadList));
				string database = SQLiteVirtualModStore.GetDefaultDatabasePath(destinationXml);
				if (!Directory.Exists(Path.GetDirectoryName(database)))
					fileManager.CreateDirectory(Path.GetDirectoryName(database));
				fileManager.Copy(temporary, database, true);
				if (!File.Exists(database + ".authority"))
					fileManager.WriteAllText(database + ".authority", "nmm-ce.native-sqlite-authority/1");
			}
			finally
			{
				if (File.Exists(temporary))
					File.Delete(temporary);
			}
		}

		/// <summary>Attaches persisted-property notifications without changing existing metadata interfaces.</summary>
		private void ObserveMod(IVirtualModInfo info, bool attach)
		{
			VirtualModInfo mutable = info as VirtualModInfo;
			if (attach)
			{
				if (!_observedMods.Add(info))
					return;
				if (mutable != null)
					mutable.PersistenceChanged += ModPersistenceChanged;
				else
				{
					_unobservableRecords++;
					_storeChanges.RequiresSnapshot = true;
				}
			}
			else if (_observedMods.Remove(info))
			{
				if (mutable != null)
					mutable.PersistenceChanged -= ModPersistenceChanged;
				else
					_unobservableRecords--;
			}
		}

		/// <summary>Attaches change notifications for priority, active state, paths and owner metadata.</summary>
		private void ObserveLink(IVirtualModLink link, bool attach)
		{
			VirtualModLink mutable = link as VirtualModLink;
			if (attach)
			{
				if (!_observedLinks.Add(link))
					return;
				if (mutable != null)
					mutable.PersistenceChanged += LinkPersistenceChanged;
				else
				{
					_unobservableRecords++;
					_storeChanges.RequiresSnapshot = true;
				}
			}
			else if (_observedLinks.Remove(link))
			{
				if (mutable != null)
					mutable.PersistenceChanged -= LinkPersistenceChanged;
				else
					_unobservableRecords--;
			}
		}

		/// <summary>Records an in-place registration edit.</summary>
		private void ModPersistenceChanged(object sender, EventArgs args)
		{
			lock (_persistenceLock)
				if (_observedMods.Contains((IVirtualModInfo)sender))
					_storeChanges.Mods.Add((IVirtualModInfo)sender);
		}

		/// <summary>Records an in-place link edit.</summary>
		private void LinkPersistenceChanged(object sender, EventArgs args)
		{
			lock (_persistenceLock)
				if (_observedLinks.Contains((IVirtualModLink)sender))
				{
					// An explicit reassignment can merge two mod groups; preserve the source list's link ordering.
					PropertyChangedEventArgs property = args as PropertyChangedEventArgs;
					if (property != null && property.PropertyName == nameof(VirtualModLink.ModInfo)) _storeChanges.RequiresSnapshot = true;
					_storeChanges.Links.Add((IVirtualModLink)sender);
				}
		}

		/// <summary>Tracks registration mutations even while the observable list batches UI updates.</summary>
		private void VirtualModInfoMutationOccurred(object sender, NotifyCollectionChangedEventArgs args)
		{
			TrackStoreCollectionChange(args, true);
		}

		/// <summary>Tracks individual link mutations without turning a batched UI reset into a whole-store save.</summary>
		private void VirtualLinkMutationOccurred(object sender, NotifyCollectionChangedEventArgs args)
		{
			TrackStoreCollectionChange(args, false);
		}

		/// <summary>Tracks collection deltas, retaining bulk persistence only for explicit reset/reorder operations.</summary>
		private void TrackStoreCollectionChange(NotifyCollectionChangedEventArgs args, bool mods)
		{
			lock (_persistenceLock)
			{
				if (args.Action == NotifyCollectionChangedAction.Reset || args.Action == NotifyCollectionChangedAction.Move)
				{
					if (args.Action == NotifyCollectionChangedAction.Reset)
					{
						foreach (IVirtualModInfo mod in _observedMods.ToArray())
							ObserveMod(mod, false);
						foreach (IVirtualModLink link in _observedLinks.ToArray())
							ObserveLink(link, false);
						_unobservableRecords = 0;
						foreach (IVirtualModInfo mod in m_tslVirtualModInfo)
							ObserveMod(mod, true);
						foreach (IVirtualModLink link in m_tslVirtualModList)
							ObserveLink(link, true);
					}

					_storeChanges.RequiresSnapshot = true;
					return;
				}

				if (args.Action == NotifyCollectionChangedAction.Replace || (args.Action == NotifyCollectionChangedAction.Add && args.NewStartingIndex >= 0 && args.NewStartingIndex + args.NewItems.Count != (mods ? m_tslVirtualModInfo.Count : m_tslVirtualModList.Count)))
					_storeChanges.RequiresSnapshot = true;
				foreach (object item in args.OldItems ?? new object[0])
				{
					if (mods)
					{
						var mod = (IVirtualModInfo)item;
						ObserveMod(mod, false);
						_storeChanges.Mods.Remove(mod);
						_storeChanges.AppendedMods.Remove(mod);
						_storeChanges.RemovedMods.Add(mod);
					}
					else
					{
						var link = (IVirtualModLink)item;
						ObserveLink(link, false);
						_storeChanges.Links.Remove(link);
						_storeChanges.AppendedLinks.Remove(link);
						_storeChanges.RemovedLinks.Add(link);
					}
				}

				foreach (object item in args.NewItems ?? new object[0])
				{
					if (mods)
					{
						var mod = (IVirtualModInfo)item;
						ObserveMod(mod, true);
						_storeChanges.RemovedMods.Remove(mod);
						_storeChanges.Mods.Add(mod);
						_storeChanges.AppendedMods[mod] = ++_storeChanges.Order;
					}
					else
					{
						var link = (IVirtualModLink)item;
						ObserveLink(link, true);
						_storeChanges.RemovedLinks.Remove(link);
						_storeChanges.Links.Add(link);
						_storeChanges.AppendedLinks[link] = ++_storeChanges.Order;
					}
				}
			}
		}

		/// <summary>Persists the current mutation, avoiding global collection enumeration on the ordinary SQLite path.</summary>
		private void PersistStoreChanges()
		{
			lock (_persistenceLock)
			{
				SQLiteVirtualModStore sqlite = m_vmsVirtualModStore as SQLiteVirtualModStore;
				IEnumerable<IVirtualModInfo> affected = sqlite == null || _storeChanges.RequiresSnapshot
					? (IEnumerable<IVirtualModInfo>)m_tslVirtualModInfo
					: _storeChanges.Mods.Concat(_storeChanges.Links.Where(x => x.ModInfo != null).Select(x => x.ModInfo))
						.Distinct(NativeReferenceComparer<IVirtualModInfo>.Instance).ToArray();
				foreach (IVirtualModInfo mod in affected)
					if (String.IsNullOrEmpty(mod.DownloadId))
					{
						var managed = ModManager.ActiveMods.FirstOrDefault(x => String.Equals(x.Filename, mod.ModFileFullPath, StringComparison.OrdinalIgnoreCase));
						if (managed != null && mod is VirtualModInfo)
							((VirtualModInfo)mod).DownloadId = managed.DownloadId;
					}

				if (sqlite != null && !_storeChanges.RequiresSnapshot)
					sqlite.SaveDelta(CURRENT_VERSION, _storeChanges);
				else
					m_vmsVirtualModStore.Save(CURRENT_VERSION, m_strVirtualActivatorConfigPath, m_tslVirtualModInfo.ToList(), m_tslVirtualModList);
				_storeChanges.Clear();
				_storeChanges.RequiresSnapshot = _unobservableRecords != 0;
			}
		}
	}
}
