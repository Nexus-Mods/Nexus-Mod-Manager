using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Compares mutable native managed archives with one already sealed immutable retained artifact.</summary>
	internal static class CollectionArchiveContentMatcher
	{
		private const int HashBufferSize = 1024 * 1024;
		private static readonly object HashCacheLock = new object();
		private static readonly Dictionary<string, FileHashStamp> HashCache =
			new Dictionary<string, FileHashStamp>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, FileHashStamp> TrustedHashCache =
			new Dictionary<string, FileHashStamp>(StringComparer.OrdinalIgnoreCase);
		public static List<IMod> FindExactManagedMods(ModManager modManager, CollectionsRetainedArtifact retained,
			CancellationToken cancellationToken)
		{
			if (modManager == null) throw new ArgumentNullException(nameof(modManager));
			if (retained == null) throw new ArgumentNullException(nameof(retained));
			var result = new List<IMod>();
			foreach (IMod mod in modManager.ManagedMods)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string archivePath = GetManagedArchivePath(mod);
				if (MatchesFile(archivePath, retained, cancellationToken))
					result.Add(mod);
			}
			return result;
		}

		/// <summary>Returns the physical archive path represented by one managed mod.</summary>
		internal static string GetManagedArchivePath(IMod mod)
		{
			if (mod == null)
				return null;
			return !String.IsNullOrWhiteSpace(mod.ModArchivePath) ? mod.ModArchivePath : mod.Filename;
		}

		/// <summary>Finds exact archive bytes already present in the current game's native mod library.</summary>
		internal static List<string> FindExactLibraryArchiveFiles(ModManager modManager, long byteLength,
			CollectionContentHash contentHash, CancellationToken cancellationToken, bool preparedOnly = false)
		{
			if (modManager == null) throw new ArgumentNullException(nameof(modManager));
			if (contentHash == null || contentHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
				throw new ArgumentException("An exact SHA-256 content identity is required.", nameof(contentHash));
			if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));

			string root = Path.GetFullPath(modManager.CurrentGameModeModDirectory);
			if (!Directory.Exists(root))
				return new List<string>();

			var excludedRoots = new List<string>();
			var environment = modManager.GameMode == null ? null : modManager.GameMode.GameModeEnvironmentInfo;
			if (environment != null)
			{
				AddExcludedRoot(excludedRoots, environment.ModCacheDirectory);
				AddExcludedRoot(excludedRoots, environment.ModDownloadCacheDirectory);
				AddExcludedRoot(excludedRoots, environment.ModReadMeDirectory);
				AddExcludedRoot(excludedRoots, environment.CategoryDirectory);
			}
			AddExcludedRoot(excludedRoots, Path.Combine(root, VirtualModActivator.ACTIVATOR_FOLDER));
			AddExcludedRoot(excludedRoots, Path.Combine(root, VirtualModActivator.ACTIVATOR_LINK_FOLDER));
			AddExcludedRoot(excludedRoots, Path.Combine(root, ProfileManager.PROFILE_FOLDER));

			bool recurse = modManager.EnvironmentInfo != null && modManager.EnvironmentInfo.Settings.ScanSubfoldersForMods;
			var matches = new List<string>();
			foreach (string file in EnumerateLibraryFiles(root, recurse, excludedRoots, cancellationToken))
			{
				cancellationToken.ThrowIfCancellationRequested();
				FileInfo info;
				try { info = new FileInfo(file); }
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
				{
					continue;
				}
				if (!info.Exists || info.Length != byteLength)
					continue;
				if (preparedOnly ? MatchesPreparedFile(file, byteLength, contentHash) : MatchesFile(file, byteLength, contentHash, cancellationToken))
					matches.Add(Path.GetFullPath(file));
			}
			return matches.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
		}

		public static bool MatchesFile(string path, CollectionsRetainedArtifact retained, CancellationToken cancellationToken)
		{
			if (retained == null)
				return false;
			return MatchesFile(path, retained.ByteLength, retained.ContentHash, cancellationToken);
		}

		/// <summary>Matches an unchanged preparation proof without reading archive bytes or starting a new checksum pass.</summary>
		internal static bool MatchesPreparedFile(string path, long byteLength, CollectionContentHash contentHash)
		{
			if (String.IsNullOrWhiteSpace(path) || contentHash == null || contentHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
				return false;
			var info = new FileInfo(Path.GetFullPath(path));
			if (!info.Exists || info.Length != byteLength) return false;
			lock (HashCacheLock)
			{
				FileHashStamp stamp;
				if (!HashCache.TryGetValue(info.FullName, out stamp) || stamp.ByteLength != info.Length ||
					stamp.LastWriteUtcTicks != info.LastWriteTimeUtc.Ticks)
					TrustedHashCache.TryGetValue(info.FullName, out stamp);
				return stamp != null && stamp.ByteLength == info.Length && stamp.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks &&
					StringComparer.Ordinal.Equals(stamp.HashValue, contentHash.Value);
			}
		}

		internal static bool MatchesFile(string path, long byteLength, CollectionContentHash contentHash,
			CancellationToken cancellationToken, bool nativeChildVerification = false)
		{
			if (String.IsNullOrWhiteSpace(path) || contentHash == null ||
				contentHash.Algorithm != CollectionContentHashAlgorithm.Sha256 || !File.Exists(path))
				return false;
			string fullPath = Path.GetFullPath(path);
			var info = new FileInfo(fullPath);
			if (info.Length != byteLength)
				return false;

			string cachedHash;
			if (TryGetObservedHash(fullPath, info, out cachedHash))
				return StringComparer.Ordinal.Equals(cachedHash, contentHash.Value);
			if (CollectionsRetainedArtifactStore.CanReuseSavedArchiveHashes)
				lock (HashCacheLock)
				{
					FileHashStamp stamp;
					if (TrustedHashCache.TryGetValue(fullPath, out stamp) && stamp.ByteLength == info.Length &&
						stamp.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
						return StringComparer.Ordinal.Equals(stamp.HashValue, contentHash.Value);
				}

			long performanceStarted = CollectionPerformanceMetrics.StartTiming();
			CollectionPreparationProgress.ReportArchiveVerification(fullPath, false);
			CollectionPreparationProgress.ReportArchiveHashing(false);
			string actualHash;
			using (FileStream input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
				HashBufferSize, FileOptions.SequentialScan))
			using (SHA256 sha = SHA256.Create())
			{
				byte[] buffer = new byte[HashBufferSize];
				long completed = 0;
				int read;
				while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha.TransformBlock(buffer, 0, read, null, 0);
					completed += read;
					CollectionPreparationProgress.ReportBytes(completed, info.Length);
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				actualHash = ToHex(sha.Hash);
			}
			if (nativeChildVerification)
				CollectionPerformanceMetrics.RecordNativeChildArchiveHash(performanceStarted, info.Length);
			else
				CollectionPerformanceMetrics.RecordRetainedArtifactVerificationHash(performanceStarted, info.Length);

			// Capture metadata after the read. If the archive changed while hashing, do not retain a reusable stamp.
			var after = new FileInfo(fullPath);
			if (after.Exists && after.Length == info.Length && after.LastWriteTimeUtc.Ticks == info.LastWriteTimeUtc.Ticks)
				RememberObservedHash(fullPath, after, actualHash);

			return StringComparer.Ordinal.Equals(actualHash, contentHash.Value);
		}

		private static IEnumerable<string> EnumerateLibraryFiles(string root, bool recurse, IList<string> excludedRoots,
			CancellationToken cancellationToken)
		{
			var pending = new Stack<string>();
			pending.Push(root);
			while (pending.Count != 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string directory = pending.Pop();
				string[] files;
				string[] directories;
				try
				{
					files = Directory.GetFiles(directory);
					directories = recurse ? Directory.GetDirectories(directory) : new string[0];
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
					ex is ArgumentException || ex is NotSupportedException)
				{
					continue;
				}

				foreach (string file in files)
					yield return file;
				for (int index = directories.Length - 1; index >= 0; index--)
				{
					string child = Path.GetFullPath(directories[index]);
					if (!IsExcludedPath(child, excludedRoots))
						pending.Push(child);
				}
			}
		}

		private static void AddExcludedRoot(ICollection<string> roots, string path)
		{
			if (roots == null || String.IsNullOrWhiteSpace(path))
				return;
			try { roots.Add(Path.GetFullPath(path)); }
			catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { }
		}

		private static bool IsExcludedPath(string path, IEnumerable<string> excludedRoots)
		{
			foreach (string excluded in excludedRoots)
			{
				if (StringComparer.OrdinalIgnoreCase.Equals(path, excluded))
					return true;
				string rooted = excluded.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
					Path.DirectorySeparatorChar;
				if (path.StartsWith(rooted, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}

		/// <summary>Reuses a checksum observed during this process while the archive's size and write stamp are unchanged.</summary>
		internal static bool TryGetObservedHash(string fullPath, FileInfo info, out string hash)
		{
			lock (HashCacheLock)
			{
				FileHashStamp stamp;
				if (HashCache.TryGetValue(fullPath, out stamp) && stamp.ByteLength == info.Length &&
					stamp.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
				{
					hash = stamp.HashValue;
					return true;
				}
			}
			hash = null;
			return false;
		}

		/// <summary>Shares an actually calculated checksum with subsequent read-only archive matching and preparation.</summary>
		internal static void RememberObservedHash(string fullPath, FileInfo info, string hash)
		{
			lock (HashCacheLock)
			{
				HashCache[fullPath] = new FileHashStamp(hash, info.Length, info.LastWriteTimeUtc.Ticks);
			}
		}

		/// <summary>Shares an unchanged saved archive proof only with calls that explicitly opted into saved-hash reuse.</summary>
		internal static void RememberTrustedHash(string fullPath, FileInfo info, string hash)
		{
			lock (HashCacheLock) TrustedHashCache[fullPath] = new FileHashStamp(hash, info.Length, info.LastWriteTimeUtc.Ticks);
		}

		/// <summary>Discards an opt-in proof when strict verification observes different bytes.</summary>
		internal static void ForgetTrustedHash(string fullPath)
		{
			lock (HashCacheLock) TrustedHashCache.Remove(fullPath);
		}

		private sealed class FileHashStamp
		{
			public FileHashStamp(string hashValue, long byteLength, long lastWriteUtcTicks)
			{
				HashValue = hashValue;
				ByteLength = byteLength;
				LastWriteUtcTicks = lastWriteUtcTicks;
			}

			public string HashValue { get; }
			public long ByteLength { get; }
			public long LastWriteUtcTicks { get; }
		}

		private static string ToHex(byte[] bytes)
		{
			var chars = new char[bytes.Length * 2];
			const string alphabet = "0123456789abcdef";
			for (int index = 0; index < bytes.Length; index++)
			{
				chars[index * 2] = alphabet[bytes[index] >> 4];
				chars[(index * 2) + 1] = alphabet[bytes[index] & 0x0f];
			}
			return new string(chars);
		}
	}
}
