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
	/// <summary>One exact native game-specific owner/value record used by Local Collection restore.</summary>
	public sealed class ModGameSpecificValueRestoreOwner
	{
		private readonly byte[] _value;

		public ModGameSpecificValueRestoreOwner(string ownerKey, bool originalValue, IMod mod, byte[] value)
		{
			if (String.IsNullOrWhiteSpace(ownerKey))
				throw new ArgumentException("A game-specific restore owner key is required.", nameof(ownerKey));
			if (originalValue && mod != null)
				throw new ArgumentException("An original game-specific restore owner cannot reference a managed mod.", nameof(mod));
			if (!originalValue && mod == null)
				throw new ArgumentNullException(nameof(mod));
			OwnerKey = ownerKey;
			OriginalValue = originalValue;
			Mod = mod;
			_value = value == null ? null : (byte[])value.Clone();
		}

		public string OwnerKey { get; }
		public bool OriginalValue { get; }
		public IMod Mod { get; }
		public byte[] Value { get { return _value == null ? null : (byte[])_value.Clone(); } }
		internal byte[] UnsafeValue { get { return _value; } }
	}

	/// <summary>One exact game-specific key and its ordered fallback-to-winner native owner history.</summary>
	public sealed class ModGameSpecificValueRestoreEntry
	{
		private readonly ReadOnlyCollection<ModGameSpecificValueRestoreOwner> _owners;

		public ModGameSpecificValueRestoreEntry(string key, IEnumerable<ModGameSpecificValueRestoreOwner> owners)
		{
			if (String.IsNullOrWhiteSpace(key))
				throw new ArgumentException("A game-specific restore key is required.", nameof(key));
			Key = key;
			List<ModGameSpecificValueRestoreOwner> copied = (owners ?? throw new ArgumentNullException(nameof(owners))).ToList();
			if (copied.Count == 0 || copied.Any(x => x == null))
				throw new ArgumentException("A game-specific restore entry requires a non-empty owner history.", nameof(owners));
			_owners = new ReadOnlyCollection<ModGameSpecificValueRestoreOwner>(copied);
		}

		public string Key { get; }
		public ReadOnlyCollection<ModGameSpecificValueRestoreOwner> Owners { get { return _owners; } }
	}

	/// <summary>Native exact game-specific owner/history restoration primitives used by Local Collection recovery.</summary>
	public partial class ModManager
	{
		/// <summary>Attempts to read the exact current physical bytes through the active game-mode restore adapter.</summary>
		public bool TryCaptureCurrentGameSpecificValue(string key, out byte[] value)
		{
			if (String.IsNullOrWhiteSpace(key))
				throw new ArgumentException("A game-specific key is required.", nameof(key));
			IInstallLog installLog = InstallationLog ?? throw new InvalidOperationException("Game-specific capture requires the current InstallLog.");
			IMod contextMod = installLog.GetCurrentGameSpecificValueEditOwner(key) ?? InstallLog.OriginalValueMod;
			var fileManager = new TxFileManager();
			IGameSpecificValueRestoreSupport support = CreateGameSpecificValueRestoreSupport(contextMod, installLog, fileManager);
			if (support == null)
			{
				value = null;
				return false;
			}
			return support.TryReadGameSpecificValue(key, out value);
		}

		/// <summary>Replaces the complete current NMM-managed game-specific state with one validated exact captured state.</summary>
		public void RestoreCapturedGameSpecificValueState(IReadOnlyList<ModGameSpecificValueRestoreEntry> entries)
		{
			if (entries == null)
				throw new ArgumentNullException(nameof(entries));
			IInstallLog installLog = InstallationLog ?? throw new InvalidOperationException("Game-specific restoration requires the current InstallLog.");
			ValidateGameSpecificRestoreEntries(entries, installLog);
			InstallLogReadSnapshot current = installLog.GetCommittedStateSnapshot();
			if (current == null)
				throw new InvalidOperationException("The committed InstallLog snapshot is unavailable for game-specific restoration.");

			var desiredByKey = entries.ToDictionary(x => x.Key, StringComparer.Ordinal);
			using (var transaction = new TransactionScope())
			{
				var fileManager = new TxFileManager();
				foreach (InstallLogReadGameValue existing in current.GameValues)
				{
					IMod contextMod = ResolveGameSpecificOwnerMod(existing.Values.Count == 0 ? null : existing.Values[existing.Values.Count - 1].OwnerKey, installLog)
						?? InstallLog.OriginalValueMod;
					if (!desiredByKey.ContainsKey(existing.Key))
					{
						InstallLogReadBinaryValue original = existing.Values.FirstOrDefault(x =>
							x.OwnerKey.Equals(installLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase));
						if (original == null || original.Value == null)
							throw new NotSupportedException("The current game-specific value has no exact original fallback that the active adapter can restore.");
						IGameSpecificValueRestoreSupport support = RequireGameSpecificValueRestoreSupport(contextMod, installLog, fileManager, existing.Key);
						if (!support.TryRestoreGameSpecificValue(existing.Key, original.Value))
							throw new InvalidOperationException("The active game-specific adapter could not restore the original fallback bytes.");
					}

					foreach (InstallLogReadBinaryValue owner in existing.Values)
					{
						IMod mod = ResolveGameSpecificOwnerMod(owner.OwnerKey, installLog);
						if (mod == null)
							throw new InvalidOperationException("A current game-specific owner cannot be resolved before exact restoration.");
						installLog.RemoveGameSpecificValueEdit(mod, existing.Key);
					}
				}

				foreach (ModGameSpecificValueRestoreEntry entry in entries)
				{
					foreach (ModGameSpecificValueRestoreOwner owner in entry.Owners)
					{
						if (owner.OriginalValue)
							installLog.LogOriginalGameSpecificValue(entry.Key, owner.UnsafeValue);
						else
							installLog.AddGameSpecificValueEdit(owner.Mod, entry.Key, owner.UnsafeValue);
					}

					ModGameSpecificValueRestoreOwner winner = entry.Owners[entry.Owners.Count - 1];
					if (winner.UnsafeValue == null)
						throw new NotSupportedException("The active game-specific restore adapter cannot represent an absent final binary value.");
					IMod contextMod = winner.OriginalValue ? InstallLog.OriginalValueMod : winner.Mod;
					IGameSpecificValueRestoreSupport support = RequireGameSpecificValueRestoreSupport(contextMod, installLog, fileManager, entry.Key);
					if (!support.TryRestoreGameSpecificValue(entry.Key, winner.UnsafeValue))
						throw new InvalidOperationException("The active game-specific adapter could not restore the captured winner bytes.");
				}

				transaction.Complete();
			}
		}

		private void ValidateGameSpecificRestoreEntries(IReadOnlyList<ModGameSpecificValueRestoreEntry> entries, IInstallLog installLog)
		{
			if (entries.Any(x => x == null))
				throw new ArgumentException("Game-specific restore state cannot contain null entries.", nameof(entries));
			if (entries.GroupBy(x => x.Key, StringComparer.Ordinal).Any(x => x.Count() != 1))
				throw new InvalidDataException("Game-specific restore state contains duplicate keys.");
			foreach (ModGameSpecificValueRestoreEntry entry in entries)
			{
				if (entry.Owners.Count(x => x.OriginalValue) > 1 || entry.Owners.Skip(1).Any(x => x.OriginalValue))
					throw new InvalidDataException("An original game-specific owner may appear only once at the bottom of an owner history.");
				if (entry.Owners.Select(x => x.OwnerKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entry.Owners.Count)
					throw new InvalidDataException("Game-specific restore owner history contains duplicate owner keys.");
				foreach (ModGameSpecificValueRestoreOwner owner in entry.Owners)
				{
					if (owner.OriginalValue)
					{
						if (!owner.OwnerKey.Equals(installLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
							throw new InvalidDataException("The captured original game-specific owner was not remapped to the current InstallLog original key.");
						continue;
					}
					string liveKey = installLog.GetModKey(owner.Mod);
					if (!owner.OwnerKey.Equals(liveKey, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException("A captured game-specific owner no longer matches its current native mod registration.");
					if (owner.UnsafeValue == null)
						throw new InvalidDataException("A managed game-specific restore value cannot be null.");
				}
			}
		}

		private IMod ResolveGameSpecificOwnerMod(string ownerKey, IInstallLog installLog)
		{
			if (String.IsNullOrWhiteSpace(ownerKey))
				return null;
			if (ownerKey.Equals(installLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
				return InstallLog.OriginalValueMod;
			return ActiveMods.FirstOrDefault(x => ownerKey.Equals(installLog.GetModKey(x), StringComparison.OrdinalIgnoreCase));
		}

		private IGameSpecificValueRestoreSupport RequireGameSpecificValueRestoreSupport(IMod contextMod, IInstallLog installLog,
			TxFileManager fileManager, string key)
		{
			IGameSpecificValueRestoreSupport support = CreateGameSpecificValueRestoreSupport(contextMod, installLog, fileManager);
			if (support == null)
				throw new NotSupportedException("The current game mode does not expose an exact Local Collection restore adapter for game-specific key '" + key + "'.");
			return support;
		}

		private IGameSpecificValueRestoreSupport CreateGameSpecificValueRestoreSupport(IMod contextMod, IInstallLog installLog,
			TxFileManager fileManager)
		{
			IGameSpecificValueInstaller installer = GameMode.GetGameSpecificValueInstaller(contextMod ?? InstallLog.OriginalValueMod,
				installLog, fileManager, m_futFileUtility, null);
			return installer as IGameSpecificValueRestoreSupport;
		}
	}
}
