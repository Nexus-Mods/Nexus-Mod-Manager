using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChinhDo.Transactions;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.Mods;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>Uses native value-owner stacks and the enclosing file transaction for Witcher Collection settings.</summary>
	internal sealed class Witcher3CollectionValueInstaller : StructuredGameSpecificValueInstaller, IGameSpecificValueInstaller, IGameSpecificValueRestoreSupport
	{
		private readonly TxFileManager _files;
		private readonly HashSet<string> _snapshots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _previousEdits;
		private readonly ConfirmItemOverwriteDelegate _overwrite;

		/// <summary>Retains the same native history and overwrite-policy seam used by other structured games.</summary>
		internal Witcher3CollectionValueInstaller(IMod mod, IInstallLog log, TxFileManager files,
			ConfirmItemOverwriteDelegate overwrite, Witcher3CollectionRecordStore store, bool upgrade = false)
			: base(mod, log, files, overwrite, store)
		{
			_files = files;
			_overwrite = overwrite;
			_previousEdits = new HashSet<string>(upgrade ? log.GetInstalledGameSpecificValueEdits(mod) : Enumerable.Empty<string>(), StringComparer.Ordinal);
		}

		/// <summary>Names the affected settings file instead of displaying encoded internal record identities.</summary>
		public override bool ResolveGameSpecificValueEdit(string key)
		{
			string profile, identity; Store.ParseKey(key, out profile, out identity);
			string current = InstallLog.GetCurrentGameSpecificValueEditOwnerKey(key);
			if (String.IsNullOrEmpty(current) || StringComparer.OrdinalIgnoreCase.Equals(current, InstallLog.OriginalValuesKey) ||
				StringComparer.OrdinalIgnoreCase.Equals(current, InstallLog.GetModKey(Mod))) return true;
			if (_overwrite == null) return false;
			OverwriteResult result = _overwrite(LanguageManager.Format("Witcher3.Install.ReplaceSetting",
				"Another mod has already changed a setting in {0}. Use this mod's setting instead?", Path.GetFileName(profile)), false, false);
			return result == OverwriteResult.Yes || result == OverwriteResult.YesToAll;
		}

		/// <summary>Applies already reviewed changes, retaining the original value before the first owner takes over.</summary>
		public override bool ApplyResolvedGameSpecificValueEdit(string key, byte[] value)
		{
			string profile, identity;
			Store.ParseKey(key, out profile, out identity);
			Store.ValidatePayload(identity, value);
			string owner = InstallLog.GetModKey(Mod);
			string currentOwner = InstallLog.GetCurrentGameSpecificValueEditOwnerKey(key);
			bool sameOwner = !String.IsNullOrEmpty(owner) && StringComparer.OrdinalIgnoreCase.Equals(owner, currentOwner);
			if (_previousEdits.Contains(key) && !sameOwner)
			{
				InstallLog.ReplaceGameSpecificValueEdit(Mod, key, value);
				_previousEdits.Remove(key); return true;
			}
			string path = Store.GetManifestPath(profile);
			byte[] current;
			bool exists = Store.TryReadValue(path, identity, out current);
			if (String.IsNullOrEmpty(currentOwner)) InstallLog.LogOriginalGameSpecificValue(key, exists ? current : null);
			Snapshot(path);
			Store.WriteValue(path, identity, value);
			if (sameOwner) InstallLog.ReplaceGameSpecificValueEdit(Mod, key, value);
			else InstallLog.AddGameSpecificValueEdit(Mod, key, value);
			_previousEdits.Remove(key);
			return true;
		}

		/// <summary>Snapshots existing or absent files before any write; native rollback restores their complete original bytes.</summary>
		private void Snapshot(string path)
		{
			if (_snapshots.Add(path)) _files.Snapshot(path);
		}

		/// <summary>Restores the previous winning record even if its settings document was removed outside NMM.</summary>
		public new void UnEditGameSpecificValue(string key)
		{
			string owner = InstallLog.GetModKey(Mod);
			if (String.IsNullOrEmpty(owner) || !StringComparer.OrdinalIgnoreCase.Equals(owner, InstallLog.GetCurrentGameSpecificValueEditOwnerKey(key))) return;
			string profile, identity; Store.ParseKey(key, out profile, out identity);
			string path = Store.GetManifestPath(profile);
			byte[] previous = InstallLog.GetPreviousGameSpecificValue(key);
			Snapshot(path);
			if (previous == null) Store.RemoveValue(path, identity); else Store.WriteValue(path, identity, previous);
			InstallLog.RemoveGameSpecificValueEdit(Mod, key);
		}

		/// <summary>Restores retained records even when the original Collection created a previously absent settings file.</summary>
		public new bool TryRestoreGameSpecificValue(string key, byte[] value)
		{
			try
			{
				string profile, identity; Store.ParseKey(key, out profile, out identity);
				string path = Store.GetManifestPath(profile); Snapshot(path);
				if (value == null) Store.RemoveValue(path, identity); else Store.WriteValue(path, identity, value);
				return true;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException) { return false; }
		}

		/// <summary>Removes only old supported records omitted by a completed native upgrade.</summary>
		public override void FinalizeInstall()
		{
			foreach (string key in _previousEdits.Where(Store.IsSupportedKey).ToList())
			{
				if (StringComparer.OrdinalIgnoreCase.Equals(InstallLog.GetModKey(Mod), InstallLog.GetCurrentGameSpecificValueEditOwnerKey(key))) UnEditGameSpecificValue(key);
				else InstallLog.RemoveGameSpecificValueEdit(Mod, key);
			}
		}
	}
}
