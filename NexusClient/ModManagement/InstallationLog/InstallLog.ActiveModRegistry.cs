using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.InstallationLog
{
	public partial class InstallLog
	{
		/// <summary>
		/// This tracks the mods that have been activated.
		/// </summary>
		private class ActiveModRegistry
		{
			private ThreadSafeObservableList<IMod> m_oclRegisteredMods = new ThreadSafeObservableList<IMod>();
			private Dictionary<IMod, string> m_dicModKeys = new Dictionary<IMod, string>(ModComparer.Filename);
			private readonly Dictionary<string, IMod> _modsByKey = new Dictionary<string, IMod>(StringComparer.Ordinal);
			private readonly HashSet<IMod> _hiddenMods = new HashSet<IMod>(ModComparer.Filename);

			#region Properties

			/// <summary>
			/// Gets the collection of registered mods, and their associated keys.
			/// </summary>
			/// <value>The collection of registered mods, and their associated keys.</value>
			public ICollection<KeyValuePair<IMod, string>> Registrations
			{
				get
				{
					return m_dicModKeys;
				}
			}

			/// <summary>
			/// Gets the collection of registered mods.
			/// </summary>
			/// <value>The collection of registered mods.</value>
			public ReadOnlyObservableList<IMod> RegisteredMods { get; private set; }
			#endregion

			#region Constructors

			/// <summary>
			/// The default constructor.
			/// </summary>
			public ActiveModRegistry()
			{
				RegisteredMods = new ReadOnlyObservableList<IMod>(m_oclRegisteredMods);
			}

			#endregion

			/// <summary>
			/// Empties the registry.
			/// </summary>
			public void Clear()
			{
				m_dicModKeys.Clear();
				_modsByKey.Clear();
				_hiddenMods.Clear();
				m_oclRegisteredMods.Clear();
			}

			/// <summary>
			/// Registers the given mod as being active, and associates it with the given key.
			/// </summary>
			/// <param name="p_modNewMod">The mod to register.</param>
			/// <param name="p_strModKey">The key with which to associate the mod.</param>
			public void RegisterMod(IMod p_modNewMod, string p_strModKey)
			{
				RegisterMod(p_modNewMod, p_strModKey, false);
			}

			/// <summary>
			/// Registers the given mod as being active, and associates it with the given key.
			/// </summary>
			/// <param name="p_modNewMod">The mod to register.</param>
			/// <param name="p_strModKey">The key with which to associate the mod.</param>
			/// <param name="p_booHiddenMod">Whether or not the mod should be included in the
			/// list of active mods.</param>
			public void RegisterMod(IMod p_modNewMod, string p_strModKey, bool p_booHiddenMod)
			{
				if (_modsByKey.ContainsKey(p_strModKey))
				{
					IMod modOld = _modsByKey[p_strModKey];
					DeregisterMod(modOld);
				}
				m_dicModKeys.Add(p_modNewMod, p_strModKey);
				_modsByKey.Add(p_strModKey, p_modNewMod);
				if (p_booHiddenMod) _hiddenMods.Add(p_modNewMod);
				if (!p_booHiddenMod)
					m_oclRegisteredMods.Add(p_modNewMod);
			}

			/// <summary>
			/// Deregisters the given mod.
			/// </summary>
			/// <param name="p_modMod">The mod to deregister.</param>
			public void DeregisterMod(IMod p_modMod)
			{
				string key;
				if (m_dicModKeys.TryGetValue(p_modMod, out key)) _modsByKey.Remove(key);
				_hiddenMods.Remove(p_modMod);
				m_dicModKeys.Remove(p_modMod);
				for (Int32 i = m_oclRegisteredMods.Count - 1; i >= 0; i--)
					if (ModComparer.Filename.Equals(m_oclRegisteredMods[i], p_modMod))
					{
						m_oclRegisteredMods.RemoveAt(i);
						return;
					}
			}

			/// <summary>
			/// Deregisters the specified mod.
			/// </summary>
			/// <param name="p_strModKey">The key of the mod to deregister.</param>
			public void DeregisterMod(string p_strModKey)
			{
				IMod modDeregister = GetMod(p_strModKey);
				if (modDeregister != null)
					DeregisterMod(GetMod(p_strModKey));
			}

			/// <summary>
			/// Determines if the given mod is registered.
			/// </summary>
			/// <param name="p_modMod">The mod for which it is to be determined if it is hidden.</param>
			/// <returns><c>true</c> if the given mod is hidden;
			/// <c>false</c> otherwise.</returns>
			public bool IsModRegistered(IMod p_modMod)
			{
				return m_dicModKeys.ContainsKey(p_modMod);
			}

			/// <summary>
			/// Determines if the given mod is hidden.
			/// </summary>
			/// <param name="p_modMod">The mod for which it is to be determined if it is registered.</param>
			/// <returns><c>true</c> if the given mod is registered;
			/// <c>false</c> otherwise.</returns>
			public bool IsModHidden(IMod p_modMod)
			{
				return !m_dicModKeys.ContainsKey(p_modMod) || _hiddenMods.Contains(p_modMod);
			}

			/// <summary>
			/// Determins if the given key exists in the registry.
			/// </summary>
			/// <param name="p_strKey">The key whose existence is to be determined.</param>
			/// <returns><c>true</c> if the key exists;
			/// <c>false</c> otherwise.</returns>
			public bool DoesKeyExist(string p_strKey)
			{
				return _modsByKey.ContainsKey(p_strKey);
			}

			/// <summary>
			/// Gets the key associated with the given mod.
			/// </summary>
			/// <param name="p_modMod">The mod whose key is to be retrieved.</param>
			/// <returns>The key associated with the given mod, or <c>null</c>
			/// if the given mod is not registered.</returns>
			public string GetKey(IMod p_modMod)
			{
				string strKey = null;
				m_dicModKeys.TryGetValue(p_modMod, out strKey);
				return strKey;
			}

			/// <summary>Captures an existing visible registration's position only when it is affected by rollback.</summary>
			public int GetVisibleIndex(IMod mod) { return m_oclRegisteredMods.IndexOf(mod); }

			/// <summary>Restores one affected registration without rebuilding the complete observable list.</summary>
			public void RestoreRegistration(IMod mod, string key, bool hidden, int position)
			{
				RegisterMod(mod, key, true);
				if (!hidden)
				{
					_hiddenMods.Remove(mod);
					m_oclRegisteredMods.Insert(Math.Max(0, Math.Min(position, m_oclRegisteredMods.Count)), mod);
				}
			}

			/// <summary>
			/// Gets the mod identified by the given key.
			/// </summary>
			/// <param name="p_strKey">The key of the mod to be retrieved.</param>
			/// <returns>The mod identified by the given key, or <c>null</c> if
			/// no mod is identified by the given key.</returns>
			public IMod GetMod(string p_strKey)
			{
				IMod mod;
				return p_strKey != null && _modsByKey.TryGetValue(p_strKey, out mod) ? mod : null;
			}
		}
	}
}
