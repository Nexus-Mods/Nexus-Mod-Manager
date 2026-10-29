using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ChinhDo.Transactions;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Coordinates native module edits with reversible order preferences in the same owner stack/transaction.</summary>
	internal sealed class LarianOrderGameValueInstaller : IGameSpecificValueInstaller, IGameSpecificValueInstallDecisionSupport, IGameSpecificValueRestoreSupport
	{
		private readonly IMod _mod;
		private readonly IInstallLog _log;
		private readonly TxFileManager _files;
		private readonly StructuredXmlRecordRule _rule;
		private readonly BaldursGate3CollectionRecordStore _store;
		private readonly StructuredGameSpecificValueInstaller _inner;
		private readonly bool _upgrade;
		private readonly HashSet<string> _profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _oldPreferences;
		private readonly HashSet<string> _oldAggregates;
		private readonly HashSet<string> _suppliedPreferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _legacyAggregateProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string[]> _ownedMembers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Uses the retained native installer for XML values; preference keys are additive and versioned.</summary>
		internal LarianOrderGameValueInstaller(IMod mod, IInstallLog log, TxFileManager files, ConfirmItemOverwriteDelegate confirmation, StructuredXmlRecordRule rule, bool upgrade)
		{
			_mod = mod; _log = log; _files = files; _rule = rule; _upgrade = upgrade;
			_store = new BaldursGate3CollectionRecordStore(rule);
			_inner = upgrade ? (StructuredGameSpecificValueInstaller)new StructuredGameSpecificValueUpgradeInstaller(mod, log, files, confirmation, _store)
				: new StructuredGameSpecificValueInstaller(mod, log, files, confirmation, _store);
			_oldPreferences = new HashSet<string>(log.GetInstalledGameSpecificValueEdits(mod).Where(key => key.StartsWith(LarianOrderPreference.Prefix, StringComparison.Ordinal)), StringComparer.Ordinal);
			_oldAggregates = new HashSet<string>(log.GetInstalledGameSpecificValueEdits(mod).Where(key => key.StartsWith(BaldursGate3CollectionRecordStore.Prefix, StringComparison.Ordinal)), StringComparer.Ordinal);
			foreach (var value in log.GetCommittedStateSnapshot().GameValues.Where(value => _oldPreferences.Contains(value.Key)))
			{
				LarianOrderPreference.TryProfile(value.Key, out string profile);
				var owner = value.Values.SingleOrDefault(item => item.OwnerKey == log.GetModKey(mod));
				LarianOrderPreference preference = LarianOrderPreference.Decode(owner?.Value);
				if (preference != null) _ownedMembers[profile] = preference.Managed;
			}
		}

		/// <summary>Curator preference replacement is part of the approved Collection operation and always takes precedence.</summary>
		public bool ResolveGameSpecificValueEdit(string key)
		{
			string profile;
			if (LarianOrderPreference.TryProfile(key, out profile)) return true;
			if (key != null && key.StartsWith(BaldursGate3CollectionRecordStore.Prefix, StringComparison.Ordinal))
			{ _store.ParseKey(key, out profile, out string identity); return true; }
			return _inner.ResolveGameSpecificValueEdit(key);
		}

		/// <summary>Applies one edit through the established native overwrite decision boundary.</summary>
		public bool EditGameSpecificValue(string key, byte[] value) => ResolveGameSpecificValueEdit(key) && ApplyResolvedGameSpecificValueEdit(key, value);

		/// <summary>Stores curator preferences as owner values; original/manual preferences remain available on removal.</summary>
		public bool ApplyResolvedGameSpecificValueEdit(string key, byte[] value)
		{
			string profile;
			if (!LarianOrderPreference.TryProfile(key, out profile))
			{
				_store.ParseKey(key, out profile, out string identity); _profiles.Add(profile);
				if (identity == BaldursGate3CollectionRecordStore.Identity)
				{
					LarianOrderPreference preference = _suppliedPreferences.Contains(profile) ? LarianOrderPreference.Decode(LarianOrderPreference.Read(_log, profile)) : null;
					if (preference == null) _legacyAggregateProfiles.Add(profile);
					if (preference != null)
					{
						_store.TryReadValue(_store.GetManifestPath(profile), identity, out byte[] current);
						value = BaldursGate3CollectionRecordStore.MergeManaged(current, value, preference.Managed);
					}
				}
				bool applied = _inner.ApplyResolvedGameSpecificValueEdit(key, value);
				if (applied && identity == BaldursGate3CollectionRecordStore.Identity)
				{
					_oldAggregates.Remove(key);
					// The generic upgrade adapter preserves shadow positions. A newly supplied curator recipe deliberately wins.
					if (_log.GetCurrentGameSpecificValueEditOwnerKey(LarianOrderPreference.Key(profile)) == _log.GetModKey(_mod) &&
						_log.GetCurrentGameSpecificValueEditOwnerKey(key) != _log.GetModKey(_mod))
						return new StructuredGameSpecificValueInstaller(_mod, _log, _files, null, _store).ApplyResolvedGameSpecificValueEdit(key, value);
				}
				return applied;
			}
			_store.GetManifestPath(profile); LarianOrderPreference.Decode(value);
			string owner = _log.GetCurrentGameSpecificValueEditOwnerKey(key);
			if (owner == null)
			{
				LarianLoadOrderDocument original = LarianLoadOrderDocument.Read(_store.GetManifestPath(profile));
				_log.LogOriginalGameSpecificValue(key, new LarianOrderPreference { Source = "Manual", Order = original.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).ToArray(), Excluded = new string[0] }.Encode());
			}
			if (owner == _log.GetModKey(_mod)) _log.ReplaceGameSpecificValueEdit(_mod, key, value);
			else _log.AddGameSpecificValueEdit(_mod, key, value);
			_profiles.Add(profile); _suppliedPreferences.Add(profile); _oldPreferences.Remove(key); return true;
		}

		/// <summary>Restores the previous preference owner before final XML reconciliation; no second ownership ledger is used.</summary>
		public void UnEditGameSpecificValue(string key)
		{
			string profile;
			if (LarianOrderPreference.TryProfile(key, out profile))
			{
				_store.GetManifestPath(profile); _log.RemoveGameSpecificValueEdit(_mod, key); _profiles.Add(profile); return;
			}
			_store.ParseKey(key, out profile, out string identity); _profiles.Add(profile);
			byte[] current = null;
			string path = _store.GetManifestPath(profile);
			bool preserve = identity == BaldursGate3CollectionRecordStore.Identity && _ownedMembers.ContainsKey(profile) &&
				_log.GetCurrentGameSpecificValueEditOwnerKey(key) == _log.GetModKey(_mod) && File.Exists(path);
			if (preserve) _store.TryReadValue(path, identity, out current);
			_inner.UnEditGameSpecificValue(key);
			if (preserve)
			{
				_store.TryReadValue(path, identity, out byte[] restored);
				_store.WriteValue(path, identity, BaldursGate3CollectionRecordStore.MergeManaged(current, restored, _ownedMembers[profile]));
			}
		}

		/// <summary>Reapplies preferences after a complete install, upgrade or removal and logs the actual resulting native values.</summary>
		public void FinalizeInstall()
		{
			if (_upgrade) foreach (string key in _oldPreferences.ToArray()) UnEditGameSpecificValue(key);
			if (_upgrade) foreach (string key in _oldAggregates.ToArray()) UnEditGameSpecificValue(key);
			foreach (string key in _log.GetInstalledGameSpecificValueEdits(_mod).Where(_store.IsSupportedKey))
			{
				_store.ParseKey(key, out string profile, out string identity); _profiles.Add(profile);
			}
			_inner.FinalizeInstall();
			foreach (string profile in _profiles) Reconcile(profile, _rule, _log, _files, !_legacyAggregateProfiles.Contains(profile));
		}

		/// <summary>Reads native preference values from the authoritative transaction-aware owner history.</summary>
		public bool TryReadGameSpecificValue(string key, out byte[] value)
		{
			if (!LarianOrderPreference.TryProfile(key, out string profile)) return _inner.TryReadGameSpecificValue(key, out value);
			return ((ICurrentGameSpecificValueReader)_log).TryReadCurrentGameSpecificValue(key, out value);
		}

		/// <summary>Validates a retained preference; the exact restore coordinator replaces its native owner history itself.</summary>
		public bool TryRestoreGameSpecificValue(string key, byte[] value)
		{
			if (!LarianOrderPreference.TryProfile(key, out string profile)) return _inner.TryRestoreGameSpecificValue(key, value);
			_store.GetManifestPath(profile); LarianOrderPreference.Decode(value); return true;
		}

		/// <summary>Applies a current preference inside an existing native transaction without inspecting packages mid-install.</summary>
		internal static void Reconcile(string profile, StructuredXmlRecordRule rule, IInstallLog log, TxFileManager files, bool applyPreference = true)
		{
			// Retained recipes without an explicit policy effect keep their exact whole-aggregate contract.
			LarianOrderPreference preference = applyPreference ? LarianOrderPreference.Decode(LarianOrderPreference.Read(log, profile)) : null;
			var store = new BaldursGate3CollectionRecordStore(rule);
			string path = store.GetManifestPath(profile);
			if (!File.Exists(path)) return;
			byte[] before = LarianLoadOrderDocument.ReadBytes(path, LarianLoadOrderDocument.MaximumBytes);
			byte[] after = LarianOrderPreference.Apply(before, path, preference);
			var original = new LarianLoadOrderDocument(path, before);
			var result = new LarianLoadOrderDocument(path, after);
			if (!before.SequenceEqual(after)) files.WriteAllBytes(path, after);
			foreach (string id in original.Records.Keys.Union(result.Records.Keys).Where(id => !id.StartsWith("game:", StringComparison.Ordinal)))
			{
				string key = rule.KeyPrefix + profile + "|" + id;
				IMod owner = log.GetCurrentGameSpecificValueEditOwner(key);
				if (owner == null || log.GetModKey(owner) == log.OriginalValuesKey) continue;
				XmlElement record; log.ReplaceGameSpecificValueEdit(owner, key, result.Records.TryGetValue(id, out record) ? Encoding.UTF8.GetBytes(record.OuterXml) : null);
			}
			string aggregate = BaldursGate3CollectionRecordStore.Prefix + profile + "|" + BaldursGate3CollectionRecordStore.Identity;
			IMod aggregateOwner = log.GetCurrentGameSpecificValueEditOwner(aggregate);
			if (aggregateOwner != null && log.GetModKey(aggregateOwner) != log.OriginalValuesKey)
			{
				store.TryReadValue(path, BaldursGate3CollectionRecordStore.Identity, out byte[] payload);
				log.ReplaceGameSpecificValueEdit(aggregateOwner, aggregate, payload);
			}
		}
	}
}
