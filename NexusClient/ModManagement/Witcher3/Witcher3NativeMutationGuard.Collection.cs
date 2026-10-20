using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement.Witcher3
{
	public sealed partial class Witcher3NativeMutationReview
	{
		private Dictionary<string, string> _collectionRemovalFiles;
		private Dictionary<string, byte[]> _collectionRemovalValues;

		/// <summary>Reviews the complete native footprint and exact fallback bytes before removing a Collection member.</summary>
		internal void AuthorizeCollectionUninstall(IMod mod, IVirtualModActivator virtualActivator)
		{
			AssertLease(_lease);
			string owner = _installLog.GetModKey(mod);
			if (String.IsNullOrWhiteSpace(owner) || _native.Issues.Count != 0) throw new InvalidDataException("Witcher removal requires complete native ownership.");
			InstallLogReadMod registration = _native.InstallLog.Mods.Single(item => String.Equals(item.ModKey, owner, StringComparison.OrdinalIgnoreCase));
			if (registration.InstallRoot != ModInstallRoot.Data || registration.HasInstallScript) throw new InvalidDataException("Unsupported Witcher native removal registration.");
			var targets = new HashSet<ModDeploymentTarget>(_native.InstallLog.Files.Where(item => item.OwnerKeys.Contains(owner, StringComparer.OrdinalIgnoreCase)).Select(item => item.Target));
			targets.UnionWith(_native.InstallLog.DeploymentTargets.Where(item => item.OwnerKeys.Contains(owner, StringComparer.OrdinalIgnoreCase)).Select(item => item.Target));
			targets.UnionWith(_native.VirtualState.Links.Where(item => String.Equals(item.OwnerKey, owner, StringComparison.OrdinalIgnoreCase)).Select(item => item.Target));
			_collectionRemovalFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (ModDeploymentTarget target in targets)
			{
				if (target.Root != ModDeploymentRoot.Data) throw new InvalidDataException("Witcher removal footprint uses an unexpected physical root.");
				string path = CollectionWitcher3Data.ValidateDestination(target.RelativePath);
				Witcher3CollectionRecordStore.RequireRegularAncestors(Path.Combine(_gameRoot, path));
				string fallback = ResolveRemovalFallback(target, owner, virtualActivator);
				_collectionRemovalFiles.Add(path, fallback == null ? null : HashRemovalPayload(fallback));
			}
			var records = ((DataDrivenWitcher3RemasteredGameMode)_gameMode).CreateCollectionRecordStore();
			_collectionRemovalValues = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			foreach (string key in _installLog.GetInstalledGameSpecificValueEdits(mod))
			{
				string profile, identity; records.ParseKey(key, out profile, out identity);
				byte[] expected;
				if (String.Equals(_installLog.GetCurrentGameSpecificValueEditOwnerKey(key), owner, StringComparison.OrdinalIgnoreCase)) expected = _installLog.GetPreviousGameSpecificValue(key);
				else records.TryReadValue(records.GetManifestPath(profile), identity, out expected);
				_collectionRemovalValues.Add(key, expected);
			}
			if (_native.InstallLog.IniEdits.Any(edit => edit.Values.Any(item => String.Equals(item.OwnerKey, owner, StringComparison.OrdinalIgnoreCase))))
				throw new InvalidDataException("Uncharacterized Witcher INI edits cannot be removed through the Collection adapter.");
			_reviewedUninstallOwnerKey = owner;
			_reviewedUninstallTargets = _collectionRemovalFiles.Keys.ToList().AsReadOnly();
			_completedRemovalVerified = false;
		}

		/// <summary>Resolves the same promoted or pure-Virtual winner that the native removal engine will restore.</summary>
		private string ResolveRemovalFallback(ModDeploymentTarget target, string owner, IVirtualModActivator virtualActivator)
		{
			NativeStateCaptureDeploymentTarget promoted = _native.DeploymentTargets.SingleOrDefault(item => item.Target.Equals(target));
			if (promoted != null)
			{
				NativeStateCaptureDeploymentOwner winner = promoted.Owners.LastOrDefault(item => !String.Equals(item.OwnerKey, owner, StringComparison.OrdinalIgnoreCase));
				if (winner != null && String.IsNullOrWhiteSpace(winner.PayloadSourcePath)) throw new InvalidDataException("Witcher promoted fallback is unresolved.");
				return winner?.PayloadSourcePath;
			}
			var links = _native.VirtualState.Links.Where(item => item.Target.Equals(target)).ToList();
			var remaining = links.Where(item => !String.Equals(item.OwnerKey, owner, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Active ? 0 : 1).ThenBy(item => item.Priority).ToList();
			if (remaining.Count > 0)
			{
				VirtualModReadLink next = remaining[0];
				if (remaining.Count > 1 && remaining[1].Active == next.Active && remaining[1].Priority == next.Priority) throw new InvalidDataException("Witcher Virtual fallback priority is ambiguous.");
				string source = String.IsNullOrWhiteSpace(next.ResolvedPayloadSourcePath) ? virtualActivator.GetVirtualSourceForOwner(target, next.OwnerKey) : next.ResolvedPayloadSourcePath;
				if (String.IsNullOrWhiteSpace(source)) throw new InvalidDataException("Witcher Virtual fallback payload is unresolved.");
				return source;
			}
			if (links.Count > 0)
			{
				NativeStateCaptureVirtualFallback original = _native.VirtualFallbacks.SingleOrDefault(item => item.Target.Equals(target));
				if (original == null || original.State == NativeStateCaptureVirtualFallbackState.Unavailable) throw new InvalidDataException("Witcher original-file fallback is unavailable.");
				return original.State == NativeStateCaptureVirtualFallbackState.Present ? original.PayloadSourcePath : null;
			}
			if (_native.InstallLog.Files.Where(item => item.Target.Equals(target)).SelectMany(item => item.OwnerKeys).Any(item => !String.Equals(item, owner, StringComparison.OrdinalIgnoreCase)))
				throw new InvalidDataException("Legacy Direct fallback requires native deployment promotion before removal.");
			return null;
		}

		/// <summary>Hashes exact fallback/live content without allocating large mod files.</summary>
		private static string HashRemovalPayload(string path)
		{
			Witcher3CollectionRecordStore.RequireRegularAncestors(path);
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (SHA256 sha = SHA256.Create()) return stream.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + BitConverter.ToString(sha.ComputeHash(stream));
		}

		/// <summary>Checks committed owner removal and independently reads back every expected restored payload/record.</summary>
		private bool VerifyCollectionUninstall(NativeStateCaptureSnapshot after)
		{
			if (_collectionRemovalFiles == null) return false;
			Witcher3NativeUninstallPostcondition.RequireOwnerAbsent(after, _reviewedUninstallOwnerKey);
			foreach (KeyValuePair<string, string> file in _collectionRemovalFiles)
			{
				string path = Path.Combine(_gameRoot, file.Key);
				if (file.Value == null)
				{
					if (File.Exists(path) || Directory.Exists(path)) throw new InvalidDataException("Removed Witcher payload remains on disk: " + file.Key);
				}
				else if (!StringComparer.Ordinal.Equals(file.Value, HashRemovalPayload(path))) throw new InvalidDataException("Witcher fallback bytes do not match the reviewed owner: " + file.Key);
			}
			var records = ((DataDrivenWitcher3RemasteredGameMode)_gameMode).CreateCollectionRecordStore();
			foreach (KeyValuePair<string, byte[]> entry in _collectionRemovalValues)
			{
				string profile, identity; records.ParseKey(entry.Key, out profile, out identity);
				byte[] actual; bool exists = records.TryReadValue(records.GetManifestPath(profile), identity, out actual);
				if ((entry.Value == null && exists) || (entry.Value != null && (!exists || !entry.Value.SequenceEqual(actual)))) throw new InvalidDataException("Witcher setting fallback does not match its reviewed native history.");
			}
			return true;
		}
	}
}
