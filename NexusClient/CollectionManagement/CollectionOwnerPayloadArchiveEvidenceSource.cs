using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	internal interface ICollectionOwnerPayloadArchiveEvidenceSource
	{
		CollectionOwnerPayloadReconstructionCandidate CreateCandidate(NativeStateCaptureDeploymentOwnerKind ownerKind,
			string ownerKey, ModDeploymentTarget target, string payloadSourcePath, CancellationToken cancellationToken);
	}

	/// <summary>Builds exact Step 2 reconstruction evidence from the current registered mod archive and deployed owner bytes.</summary>
	internal sealed class CollectionOwnerPayloadArchiveEvidenceSource : ICollectionOwnerPayloadArchiveEvidenceSource
	{
		private readonly Dictionary<string, IMod> _modsByKey;
		private readonly IGameMode _gameMode;
		private readonly Dictionary<string, CollectionInstalledModIdentity> _installedByKey;
		private readonly Dictionary<string, Dictionary<string, List<string>>> _archiveEntriesByOwner =
			new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

		internal CollectionOwnerPayloadArchiveEvidenceSource(IInstallLog installLog, CollectionInstalledIdentitySnapshot installedIdentities, IGameMode gameMode)
		{
			if (installLog == null)
				throw new ArgumentNullException(nameof(installLog));
			if (installedIdentities == null)
				throw new ArgumentNullException(nameof(installedIdentities));
			_gameMode = gameMode ?? throw new ArgumentNullException(nameof(gameMode));

			_installedByKey = installedIdentities.Mods.ToDictionary(x => x.NativeSnapshotKey, x => x, StringComparer.OrdinalIgnoreCase);
			_modsByKey = new Dictionary<string, IMod>(StringComparer.OrdinalIgnoreCase);
			foreach (IMod mod in installLog.ActiveMods)
			{
				string key = installLog.GetModKey(mod);
				if (!String.IsNullOrWhiteSpace(key) && !_modsByKey.ContainsKey(key))
					_modsByKey.Add(key, mod);
			}
		}

		public CollectionOwnerPayloadReconstructionCandidate CreateCandidate(
			NativeStateCaptureDeploymentOwnerKind ownerKind, string ownerKey, ModDeploymentTarget target,
			string payloadSourcePath, CancellationToken cancellationToken)
		{
			if (ownerKind != NativeStateCaptureDeploymentOwnerKind.Direct &&
				ownerKind != NativeStateCaptureDeploymentOwnerKind.Virtual)
				return null;
			// Step 2B intentionally starts with ordinary unscripted/default-root file installs only.
			// Game-specific special-install and merge paths remain byte-retained until they have their own
			// characterized reconstruction descriptors.
			if (_gameMode.RequiresSpecialFileInstallation || _gameMode.RequiresModFileMerge)
				return null;
			if (String.IsNullOrWhiteSpace(ownerKey) || target == null || String.IsNullOrWhiteSpace(payloadSourcePath) || !File.Exists(payloadSourcePath))
				return null;

			CollectionInstalledModIdentity installed;
			IMod mod;
			if (!_installedByKey.TryGetValue(ownerKey, out installed) || !_modsByKey.TryGetValue(ownerKey, out mod) ||
				installed.HasInstallScript || installed.InstallContext.InstallRoot != ModInstallRoot.Default ||
				installed.Archive == null || !installed.Archive.ArchiveCurrentlyAvailable ||
				String.IsNullOrWhiteSpace(installed.Archive.LiveArchivePath) || !File.Exists(installed.Archive.LiveArchivePath))
			{
				return new CollectionOwnerPayloadReconstructionCandidate(ownerKind,
					CollectionOwnerPayloadReconstructionMappingKind.ExactArchiveEntry, false, false, ownerKey,
					target.RelativePath, "archive-unavailable", null, null, null, null);
			}

			string canonicalTarget;
			try
			{
				canonicalTarget = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, target.RelativePath).Path;
			}
			catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException)
			{
				return null;
			}

			Dictionary<string, List<string>> entries = GetArchiveEntries(ownerKey, mod);
			List<string> matches;
			if (!entries.TryGetValue(canonicalTarget, out matches) || matches.Count == 0)
				return null;
			if (matches.Count != 1)
			{
				return new CollectionOwnerPayloadReconstructionCandidate(ownerKind,
					CollectionOwnerPayloadReconstructionMappingKind.ExactArchiveEntry, true, true, ownerKey,
					canonicalTarget, "ambiguous-archive-entry", null, null, null, null);
			}

			cancellationToken.ThrowIfCancellationRequested();
			ContentIdentity expected;
			try
			{
				using (FileStream archiveEntry = mod.GetFileStream(matches[0]))
					expected = Hash(archiveEntry, cancellationToken);
			}
			catch (Exception exception) when (!(exception is OperationCanceledException))
			{
				return null;
			}

			ContentIdentity observed;
			try
			{
				using (FileStream deployed = new FileStream(payloadSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
					observed = Hash(deployed, cancellationToken);
			}
			catch (Exception exception) when (exception is FileNotFoundException || exception is DirectoryNotFoundException ||
				exception is IOException || exception is UnauthorizedAccessException)
			{
				return null;
			}

			string proofIdentity = CollectionOwnerPayloadArchiveReconstruction.CreateProofIdentity(ownerKey, installed.InstallContext,
				canonicalTarget, expected.Hash, expected.ByteLength);
			return new CollectionOwnerPayloadReconstructionCandidate(ownerKind,
				CollectionOwnerPayloadReconstructionMappingKind.ExactArchiveEntry, true, false, ownerKey, canonicalTarget,
				proofIdentity, expected.Hash, expected.ByteLength, observed.Hash, observed.ByteLength);
		}

		private Dictionary<string, List<string>> GetArchiveEntries(string ownerKey, IMod mod)
		{
			Dictionary<string, List<string>> cached;
			if (_archiveEntriesByOwner.TryGetValue(ownerKey, out cached))
				return cached;

			cached = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			IEnumerable<string> files;
			try
			{
				files = mod.GetFileList() ?? new List<string>();
			}
			catch
			{
				files = new List<string>();
			}
			foreach (string file in files)
			{
				string canonical;
				try
				{
					canonical = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, file).Path;
				}
				catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException)
				{
					continue;
				}
				List<string> list;
				if (!cached.TryGetValue(canonical, out list))
				{
					list = new List<string>();
					cached.Add(canonical, list);
				}
				list.Add(file);
			}
			_archiveEntriesByOwner.Add(ownerKey, cached);
			return cached;
		}

		private static ContentIdentity Hash(Stream source, CancellationToken cancellationToken)
		{
			using (SHA256 sha256 = SHA256.Create())
			{
				byte[] buffer = new byte[1024 * 1024];
				long length = 0;
				int read;
				while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha256.TransformBlock(buffer, 0, read, null, 0);
					length += read;
				}
				sha256.TransformFinalBlock(new byte[0], 0, 0);
				return new ContentIdentity(CollectionContentHash.FromSha256(BitConverter.ToString(sha256.Hash).Replace("-", String.Empty).ToLowerInvariant()), length);
			}
		}

		private sealed class ContentIdentity
		{
			internal ContentIdentity(CollectionContentHash hash, long byteLength)
			{
				Hash = hash;
				ByteLength = byteLength;
			}
			internal CollectionContentHash Hash { get; }
			internal long ByteLength { get; }
		}
	}
}
