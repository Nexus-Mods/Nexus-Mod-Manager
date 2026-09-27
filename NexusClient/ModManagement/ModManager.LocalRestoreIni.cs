using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ChinhDo.Transactions;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Transactions;

namespace Nexus.Client.ModManagement
{
	/// <summary>One exact native INI owner/value record used by Local Collection restore.</summary>
	public sealed class ModIniRestoreOwner
	{
		public ModIniRestoreOwner(string ownerKey, bool originalValue, IMod mod, string value)
		{
			if (String.IsNullOrWhiteSpace(ownerKey)) throw new ArgumentException("An INI restore owner key is required.", nameof(ownerKey));
			if (originalValue && mod != null) throw new ArgumentException("An original INI restore owner cannot reference a managed mod.", nameof(mod));
			if (!originalValue && mod == null) throw new ArgumentNullException(nameof(mod));
			OwnerKey = ownerKey; OriginalValue = originalValue; Mod = mod; Value = value;
		}
		public string OwnerKey { get; }
		public bool OriginalValue { get; }
		public IMod Mod { get; }
		public string Value { get; }
	}

	/// <summary>One exact INI setting and its ordered fallback-to-winner owner history.</summary>
	public sealed class ModIniRestoreEntry
	{
		private readonly ReadOnlyCollection<ModIniRestoreOwner> _owners;
		public ModIniRestoreEntry(string file, string section, string key, IEnumerable<ModIniRestoreOwner> owners)
		{
			if (String.IsNullOrWhiteSpace(file)) throw new ArgumentException("An INI restore file is required.", nameof(file));
			File = file; Section = section ?? String.Empty; Key = key ?? String.Empty;
			List<ModIniRestoreOwner> copied = (owners ?? throw new ArgumentNullException(nameof(owners))).ToList();
			if (copied.Count == 0 || copied.Any(x => x == null)) throw new ArgumentException("An INI restore entry requires a non-empty owner history.", nameof(owners));
			_owners = new ReadOnlyCollection<ModIniRestoreOwner>(copied);
		}
		public string File { get; }
		public string Section { get; }
		public string Key { get; }
		public ReadOnlyCollection<ModIniRestoreOwner> Owners { get { return _owners; } }
	}

	/// <summary>Native exact INI owner/history restoration primitives used by Local Collection recovery.</summary>
	public partial class ModManager
	{
		/// <summary>Replaces the complete current NMM-managed INI state with one validated exact captured state.</summary>
		public void RestoreCapturedIniState(IReadOnlyList<ModIniRestoreEntry> entries)
		{
			if (entries == null) throw new ArgumentNullException(nameof(entries));
			IInstallLog installLog = InstallationLog ?? throw new InvalidOperationException("INI restoration requires the current InstallLog.");
			ValidateIniRestoreEntries(entries, installLog);
			InstallLogReadSnapshot current = installLog.GetCommittedStateSnapshot();
			if (current == null) throw new InvalidOperationException("The committed InstallLog snapshot is unavailable for INI restoration.");

			var desiredByKey = entries.ToDictionary(x => IniIdentity(x.File, x.Section, x.Key), StringComparer.OrdinalIgnoreCase);
			using (var transaction = new TransactionScope())
			{
				var fileManager = new TxFileManager();
				foreach (string file in current.IniEdits.Select(x => x.File).Concat(entries.Select(x => x.File))
					.Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
					fileManager.Snapshot(file);

				foreach (InstallLogReadIniEdit existing in current.IniEdits)
				{
					foreach (InstallLogReadStringValue owner in existing.Values)
					{
						IMod mod = ResolveIniOwnerMod(owner.OwnerKey, installLog);
						if (mod == null) throw new InvalidOperationException("A current INI owner cannot be resolved before exact restoration.");
						installLog.RemoveIniEdit(mod, existing.File, existing.Section, existing.Key);
					}
					if (!desiredByKey.ContainsKey(IniIdentity(existing.File, existing.Section, existing.Key)))
					{
						string fallback = existing.Values.Count > 0 &&
							existing.Values[0].OwnerKey.Equals(installLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase)
							? existing.Values[0].Value : null;
						IniMethods.WritePrivateProfileString(existing.Section, existing.Key, fallback, existing.File);
					}
				}

				foreach (ModIniRestoreEntry entry in entries)
				{
					foreach (ModIniRestoreOwner owner in entry.Owners)
					{
						if (owner.OriginalValue)
							installLog.LogOriginalIniValue(entry.File, entry.Section, entry.Key, owner.Value);
						else
							installLog.AddIniEdit(owner.Mod, entry.File, entry.Section, entry.Key, owner.Value);
					}
					IniMethods.WritePrivateProfileString(entry.Section, entry.Key, entry.Owners[entry.Owners.Count - 1].Value, entry.File);
				}

				VirtualModActivator.RestoreCapturedIniEditLog(entries, fileManager);
				transaction.Complete();
			}
		}

		private void ValidateIniRestoreEntries(IReadOnlyList<ModIniRestoreEntry> entries, IInstallLog installLog)
		{
			if (entries.Any(x => x == null)) throw new ArgumentException("INI restore state cannot contain null entries.", nameof(entries));
			if (entries.GroupBy(x => IniIdentity(x.File, x.Section, x.Key), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1))
				throw new InvalidDataException("INI restore state contains duplicate logical settings.");
			foreach (ModIniRestoreEntry entry in entries)
			{
				if (entry.Owners.Count(x => x.OriginalValue) > 1 || entry.Owners.Skip(1).Any(x => x.OriginalValue))
					throw new InvalidDataException("An original INI owner may appear only once at the bottom of an owner history.");
				if (entry.Owners.Select(x => x.OwnerKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entry.Owners.Count)
					throw new InvalidDataException("INI restore owner history contains duplicate owner keys.");
				foreach (ModIniRestoreOwner owner in entry.Owners)
				{
					if (owner.OriginalValue)
					{
						if (!owner.OwnerKey.Equals(installLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
							throw new InvalidDataException("The captured original INI owner was not remapped to the current InstallLog original key.");
						continue;
					}
					string liveKey = installLog.GetModKey(owner.Mod);
					if (!owner.OwnerKey.Equals(liveKey, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException("A captured INI owner no longer matches its current native mod registration.");
					if (owner.Value == null)
						throw new InvalidDataException("A managed INI restore value cannot be null because the native VMA replay log cannot represent it exactly.");
				}
			}
		}

		private IMod ResolveIniOwnerMod(string ownerKey, IInstallLog installLog)
		{
			if (ownerKey.Equals(installLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
				return InstallLog.OriginalValueMod;
			return ActiveMods.FirstOrDefault(x => ownerKey.Equals(installLog.GetModKey(x), StringComparison.OrdinalIgnoreCase));
		}

		private static string IniIdentity(string file, string section, string key)
		{
			return (file ?? String.Empty) + "\u001f" + (section ?? String.Empty) + "\u001f" + (key ?? String.Empty);
		}
	}
}
