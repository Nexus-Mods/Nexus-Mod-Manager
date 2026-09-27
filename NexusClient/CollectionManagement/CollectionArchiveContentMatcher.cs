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
			var info = new FileInfo(path);
			if (info.Length != retained.ByteLength)
				return false;
			using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (SHA256 sha = SHA256.Create())
			{
				byte[] buffer = new byte[64 * 1024];
				int read;
				while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha.TransformBlock(buffer, 0, read, null, 0);
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				return retained.ContentHash.Equals(CollectionContentHash.FromSha256(ToHex(sha.Hash)));
			}
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
