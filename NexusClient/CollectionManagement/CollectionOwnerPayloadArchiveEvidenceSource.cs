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
		private const int ComparisonBufferSize = 1024 * 1024;

		private readonly Dictionary<string, IMod> _modsByKey;
		private readonly IGameMode _gameMode;
		private readonly Dictionary<string, CollectionInstalledModIdentity> _installedByKey;
		private readonly Dictionary<string, Dictionary<string, List<string>>> _archiveEntriesByOwner =
			new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
		private byte[] _archiveReadBuffer;
		private byte[] _payloadReadBuffer;

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
			cancellationToken.ThrowIfCancellationRequested();
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
			ContentIdentity matched;
			try
			{
				using (FileStream archiveEntry = mod.GetFileStream(matches[0]))
				using (FileStream deployed = new FileStream(payloadSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
					ComparisonBufferSize, FileOptions.SequentialScan))
					matched = MatchAndHash(archiveEntry, deployed, cancellationToken);
			}
			catch (Exception exception) when (!(exception is OperationCanceledException))
			{
				return null;
			}
			if (matched == null)
				return null;

			string proofIdentity = CollectionOwnerPayloadArchiveReconstruction.CreateProofIdentity(ownerKey, installed.InstallContext,
				canonicalTarget, matched.Hash, matched.ByteLength);
			return new CollectionOwnerPayloadReconstructionCandidate(ownerKind,
				CollectionOwnerPayloadReconstructionMappingKind.ExactArchiveEntry, true, false, ownerKey, canonicalTarget,
				proofIdentity, matched.Hash, matched.ByteLength, matched.Hash, matched.ByteLength);
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

		/// <summary>Proves exact byte equality and hashes the common payload once, stopping early when normal byte retention is required.</summary>
		private ContentIdentity MatchAndHash(FileStream archiveEntry, FileStream deployed, CancellationToken cancellationToken)
		{
			if (archiveEntry == null)
				return null;
			long expectedLength = archiveEntry.Length;
			if (expectedLength != deployed.Length)
				return null;
			if (_archiveReadBuffer == null)
				_archiveReadBuffer = new byte[ComparisonBufferSize];
			if (_payloadReadBuffer == null)
				_payloadReadBuffer = new byte[ComparisonBufferSize];

			using (SHA256 sha256 = SHA256.Create())
			{
				long length = 0;
				while (true)
				{
					int archiveRead = ReadBlock(archiveEntry, _archiveReadBuffer, cancellationToken);
					int payloadRead = ReadBlock(deployed, _payloadReadBuffer, cancellationToken);
					if (archiveRead != payloadRead)
						return null;
					if (archiveRead == 0)
						break;
					for (int index = 0; index < archiveRead; index++)
					{
						if (_archiveReadBuffer[index] != _payloadReadBuffer[index])
							return null;
					}
					// Both streams supplied the same complete block, so one digest proves their common content identity.
					sha256.TransformBlock(_archiveReadBuffer, 0, archiveRead, null, 0);
					length += archiveRead;
				}
				if (length != expectedLength)
					return null;
				cancellationToken.ThrowIfCancellationRequested();
				sha256.TransformFinalBlock(new byte[0], 0, 0);
				return new ContentIdentity(CollectionContentHash.FromSha256(BitConverter.ToString(sha256.Hash).Replace("-", String.Empty).ToLowerInvariant()), length);
			}
		}

		/// <summary>Fills one bounded comparison block so differing stream read sizes cannot change the equality decision.</summary>
		private static int ReadBlock(Stream source, byte[] buffer, CancellationToken cancellationToken)
		{
			int filled = 0;
			while (filled < buffer.Length)
			{
				cancellationToken.ThrowIfCancellationRequested();
				int read = source.Read(buffer, filled, buffer.Length - filled);
				if (read == 0)
					break;
				filled += read;
			}
			return filled;
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
