using System;
using System.Collections.Generic;
using System.IO;
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
		public static List<IMod> FindExactManagedMods(ModManager modManager, CollectionsRetainedArtifact retained,
			CancellationToken cancellationToken)
		{
			if (modManager == null) throw new ArgumentNullException(nameof(modManager));
			if (retained == null) throw new ArgumentNullException(nameof(retained));
			var result = new List<IMod>();
			foreach (IMod mod in modManager.ManagedMods)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (mod == null || String.IsNullOrWhiteSpace(mod.ModArchivePath))
					continue;
				if (MatchesFile(mod.ModArchivePath, retained, cancellationToken))
					result.Add(mod);
			}
			return result;
		}

		public static bool MatchesFile(string path, CollectionsRetainedArtifact retained, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(path) || retained == null || !File.Exists(path))
				return false;
			string fullPath = Path.GetFullPath(path);
			var info = new FileInfo(fullPath);
			if (info.Length != retained.ByteLength)
				return false;

			string cachedHash;
			if (TryGetCachedHash(fullPath, info, out cachedHash))
				return StringComparer.Ordinal.Equals(cachedHash, retained.ContentHash.Value);

			string actualHash;
			using (FileStream input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
				HashBufferSize, FileOptions.SequentialScan))
			using (SHA256 sha = SHA256.Create())
			{
				byte[] buffer = new byte[HashBufferSize];
				int read;
				while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha.TransformBlock(buffer, 0, read, null, 0);
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				actualHash = ToHex(sha.Hash);
			}

			// Capture metadata after the read. If the archive changed while hashing, do not retain a reusable stamp.
			var after = new FileInfo(fullPath);
			if (after.Exists && after.Length == info.Length && after.LastWriteTimeUtc.Ticks == info.LastWriteTimeUtc.Ticks)
				RememberHash(fullPath, after, actualHash);

			return StringComparer.Ordinal.Equals(actualHash, retained.ContentHash.Value);
		}

		private static bool TryGetCachedHash(string fullPath, FileInfo info, out string hash)
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

		private static void RememberHash(string fullPath, FileInfo info, string hash)
		{
			lock (HashCacheLock)
				HashCache[fullPath] = new FileHashStamp(hash, info.Length, info.LastWriteTimeUtc.Ticks);
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
