using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using ChinhDo.Transactions;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>
	/// Applies configured record values through NMM's native game-specific-value ownership stack.
	/// </summary>
	public class StructuredGameSpecificValueInstaller : IGameSpecificValueInstaller,
		IGameSpecificValueInstallDecisionSupport, IGameSpecificValueRestoreSupport
	{
		private readonly IStructuredRecordStore _store;
		private readonly IMod _mod;
		private readonly IInstallLog _installLog;
		private readonly TxFileManager _fileManager;
		private readonly ConfirmItemOverwriteDelegate _overwriteConfirmation;
		private readonly HashSet<string> _snapshotted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Uses the existing native InstallLog and caller-owned transaction for one configured record store.</summary>
		public StructuredGameSpecificValueInstaller(IMod mod, IInstallLog installLog, TxFileManager fileManager,
			ConfirmItemOverwriteDelegate overwriteConfirmation, IStructuredRecordStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
			_mod = mod ?? throw new ArgumentNullException(nameof(mod));
			_installLog = installLog ?? throw new ArgumentNullException(nameof(installLog));
			_fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
			_overwriteConfirmation = overwriteConfirmation;
		}

		/// <summary>Resolves native overwrite policy before applying the exact reviewed value.</summary>
		public virtual bool EditGameSpecificValue(string key, byte[] value)
		{
			if (!ResolveGameSpecificValueEdit(key))
				return false;
			return ApplyResolvedGameSpecificValueEdit(key, value);
		}

		/// <summary>Uses native owner stacks and the existing overwrite confirmation delegate.</summary>
		public virtual bool ResolveGameSpecificValueEdit(string key)
		{
			string profileName;
			string uuid;
			Store.ParseKey(key, out profileName, out uuid);
			string currentOwner = _installLog.GetCurrentGameSpecificValueEditOwnerKey(key);
			string installingOwner = _installLog.GetModKey(_mod);
			if (String.IsNullOrEmpty(currentOwner) ||
				StringComparer.OrdinalIgnoreCase.Equals(currentOwner, _installLog.OriginalValuesKey) ||
				StringComparer.OrdinalIgnoreCase.Equals(currentOwner, installingOwner))
			{
				return true;
			}

			if (_overwriteConfirmation == null)
				return false;

			string message = LanguageManager.Format("ManagedContent.Structured.Overwrite",
				"Record '{0}' in profile '{1}' is already managed by another mod. Overwrite the managed value?", uuid, profileName);
			OverwriteResult result = _overwriteConfirmation(message, false, false);
			return result == OverwriteResult.Yes || result == OverwriteResult.YesToAll;
		}

		/// <summary>Snapshots before mutation and logs original/installed bytes through native game-value history.</summary>
		public virtual bool ApplyResolvedGameSpecificValueEdit(string key, byte[] value)
		{
			string profileName;
			string uuid;
			Store.ParseKey(key, out profileName, out uuid);
			Store.ValidatePayload(uuid, value);
			string manifestPath = Store.GetManifestPath(profileName);
			if (!Exists(manifestPath))
				throw new InvalidOperationException("The reviewed structured profile manifest no longer exists: " + profileName);

			byte[] current;
			bool exists = Store.TryReadValue(manifestPath, uuid, out current);
			string currentOwner = _installLog.GetCurrentGameSpecificValueEditOwnerKey(key);
			string installingOwner = _installLog.GetModKey(_mod);
			bool sameOwner = !String.IsNullOrEmpty(currentOwner) && StringComparer.OrdinalIgnoreCase.Equals(currentOwner, installingOwner);
			bool originalOwner = !String.IsNullOrEmpty(currentOwner) && StringComparer.OrdinalIgnoreCase.Equals(currentOwner, _installLog.OriginalValuesKey);

			// A reviewed Collection operation may not silently absorb a physical change that happened after review.
			// The reviewed BG3 order intentionally replaces the prior module order, whose bytes are retained below.
			bool replacesOrder = Store is BaldursGate3CollectionRecordStore && uuid == BaldursGate3CollectionRecordStore.Identity;
			if (exists && !BytesEqual(current, value) && !sameOwner && !replacesOrder)
				throw new InvalidOperationException("The reviewed structured record changed before native execution.");
			if (!exists && !String.IsNullOrEmpty(currentOwner) && !sameOwner && !originalOwner)
				throw new InvalidOperationException("InstallLog owns a structured record that is no longer present in the profile manifest.");

			if (String.IsNullOrEmpty(currentOwner))
				_installLog.LogOriginalGameSpecificValue(key, exists ? current : null);

			if (!exists || !BytesEqual(current, value))
			{
				Snapshot(manifestPath);
				Store.WriteValue(manifestPath, uuid, value);
			}

			if (sameOwner)
				_installLog.ReplaceGameSpecificValueEdit(_mod, key, value);
			else
				_installLog.AddGameSpecificValueEdit(_mod, key, value);
			return true;
		}

		/// <summary>Restores only the winning native owner, preserving shadowed owners and prior values.</summary>
		public void UnEditGameSpecificValue(string key)
		{
			string profileName;
			string uuid;
			Store.ParseKey(key, out profileName, out uuid);
			string installingOwner = _installLog.GetModKey(_mod);
			string currentOwner = _installLog.GetCurrentGameSpecificValueEditOwnerKey(key);
			if (String.IsNullOrEmpty(installingOwner) || !StringComparer.OrdinalIgnoreCase.Equals(installingOwner, currentOwner))
				return;

			string manifestPath = Store.GetManifestPath(profileName);
			if (Exists(manifestPath))
			{
				Snapshot(manifestPath);
				byte[] previous = _installLog.GetPreviousGameSpecificValue(key);
				if (previous == null)
					Store.RemoveValue(manifestPath, uuid);
				else
					Store.WriteValue(manifestPath, uuid, previous);
			}

			_installLog.RemoveGameSpecificValueEdit(_mod, key);
		}

		/// <summary>Reads current physical evidence for the existing restore/Collection verifier.</summary>
		public bool TryReadGameSpecificValue(string key, out byte[] value)
		{
			value = null;
			try
			{
				string profileName;
				string uuid;
				Store.ParseKey(key, out profileName, out uuid);
				string manifestPath = Store.GetManifestPath(profileName);
				return Exists(manifestPath) && Store.TryReadValue(manifestPath, uuid, out value);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException || ex is IOException ||
				ex is UnauthorizedAccessException || ex is XmlException)
			{
				return false;
			}
		}

		/// <summary>Restores retained bytes using the native transaction snapshot, without creating missing profiles.</summary>
		public bool TryRestoreGameSpecificValue(string key, byte[] value)
		{
			try
			{
				string profileName;
				string uuid;
				Store.ParseKey(key, out profileName, out uuid);
				string manifestPath = Store.GetManifestPath(profileName);
				if (!Exists(manifestPath))
					return false;
				Snapshot(manifestPath);
				if (value == null)
					Store.RemoveValue(manifestPath, uuid);
				else
					Store.WriteValue(manifestPath, uuid, value);
				return true;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException || ex is IOException ||
				ex is UnauthorizedAccessException || ex is XmlException)
			{
				return false;
			}
		}

		/// <summary>Completes a normal value installation without additional side effects.</summary>
		public virtual void FinalizeInstall()
		{
		}

		/// <summary>Validates retained upgrade payloads even when another native owner currently wins.</summary>
		protected void ValidatePayloadFromKey(string key, byte[] value)
		{
			string profile, identity;
			Store.ParseKey(key, out profile, out identity);
			Store.ValidatePayload(identity, value);
		}

		/// <summary>Gets the same immutable provider for execution, rollback, upgrade and removal.</summary>
		protected IStructuredRecordStore Store => _store;
		protected IInstallLog InstallLog { get { return _installLog; } }
		protected IMod Mod { get { return _mod; } }

		/// <summary>Does not mistake access failures or a directory for an absent game file.</summary>
		private static bool Exists(string path)
		{
			try
			{
				if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
					throw new InvalidDataException("A structured manifest must be an ordinary existing file.");
				return true;
			}
			catch (FileNotFoundException) { return false; }
			catch (DirectoryNotFoundException) { return false; }
		}

		/// <summary>Snapshots each complete manifest once in the caller-owned file transaction.</summary>
		private void Snapshot(string manifestPath)
		{
			if (_snapshotted.Contains(manifestPath)) return;
			_fileManager.Snapshot(manifestPath);
			_snapshotted.Add(manifestPath);
		}

		/// <summary>Compares exact record payload bytes.</summary>
		private static bool BytesEqual(byte[] left, byte[] right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Length != right.Length) return false;
			for (int index = 0; index < left.Length; index++)
				if (left[index] != right[index]) return false;
			return true;
		}
	}

	/// <summary>Upgrade variant that removes previously owned configured record entries omitted by the new reviewed recipe.</summary>
	public sealed class StructuredGameSpecificValueUpgradeInstaller : StructuredGameSpecificValueInstaller
	{
		private readonly HashSet<string> _originallyInstalledEdits;

		/// <summary>Captures the previous native edit set for same-mod upgrade cleanup.</summary>
		public StructuredGameSpecificValueUpgradeInstaller(IMod mod, IInstallLog installLog, TxFileManager fileManager,
			ConfirmItemOverwriteDelegate overwriteConfirmation, IStructuredRecordStore store)
			: base(mod, installLog, fileManager, overwriteConfirmation, store)
		{
			_originallyInstalledEdits = new HashSet<string>(installLog.GetInstalledGameSpecificValueEdits(mod), StringComparer.Ordinal);
		}

		/// <summary>Preserves previously installed same-mod edit ownership during upgrades.</summary>
		public override bool ResolveGameSpecificValueEdit(string key)
		{
			if (WasPreviouslyInstalledByThisMod(key))
				return true;
			return base.ResolveGameSpecificValueEdit(key);
		}

		/// <summary>Updates a shadowed native value without rewriting another winning owner.</summary>
		public override bool ApplyResolvedGameSpecificValueEdit(string key, byte[] value)
		{
			if (WasPreviouslyInstalledByThisMod(key))
			{
				string installingOwner = InstallLog.GetModKey(Mod);
				string currentOwner = InstallLog.GetCurrentGameSpecificValueEditOwnerKey(key);
				if (!StringComparer.OrdinalIgnoreCase.Equals(installingOwner, currentOwner))
				{
					ValidatePayloadFromKey(key, value);
					InstallLog.ReplaceGameSpecificValueEdit(Mod, key, value);
					_originallyInstalledEdits.Remove(key);
					return true;
				}
			}

			bool result = base.ApplyResolvedGameSpecificValueEdit(key, value);
			if (result)
				_originallyInstalledEdits.Remove(key);
			return result;
		}

		/// <summary>Inspects native history rather than physical file presence.</summary>
		private bool WasPreviouslyInstalledByThisMod(string key)
		{
			string installingOwner = InstallLog.GetModKey(Mod);
			if (String.IsNullOrEmpty(installingOwner))
				return false;
			return InstallLog.GetGameSpecificValueEditInstallers(key)
				.Any(installer => installer != null && StringComparer.OrdinalIgnoreCase.Equals(InstallLog.GetModKey(installer), installingOwner));
		}

		/// <summary>Removes old supported keys omitted by the successful upgraded recipe.</summary>
		public override void FinalizeInstall()
		{
			string installingOwner = InstallLog.GetModKey(Mod);
			foreach (string key in _originallyInstalledEdits.Where(Store.IsSupportedKey).ToList())
			{
				string currentOwner = InstallLog.GetCurrentGameSpecificValueEditOwnerKey(key);
				if (!String.IsNullOrEmpty(installingOwner) && StringComparer.OrdinalIgnoreCase.Equals(installingOwner, currentOwner))
					UnEditGameSpecificValue(key);
				else
					InstallLog.RemoveGameSpecificValueEdit(Mod, key);
			}
		}
	}

}
