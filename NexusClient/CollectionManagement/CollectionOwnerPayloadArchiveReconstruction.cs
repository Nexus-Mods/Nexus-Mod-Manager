using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Creates and validates the immutable proof identity for the characterized exact archive-entry reconstruction path.</summary>
	internal static class CollectionOwnerPayloadArchiveReconstruction
	{
		internal static string CreateProofIdentity(string nativeSnapshotKey, ModInstallContext installContext,
			string archiveEntryPath, CollectionContentHash expectedHash, long expectedByteLength)
		{
			if (String.IsNullOrWhiteSpace(nativeSnapshotKey))
				throw new ArgumentException("A captured native snapshot key is required.", nameof(nativeSnapshotKey));
			if (installContext == null)
				throw new ArgumentNullException(nameof(installContext));
			if (expectedHash == null || expectedHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
				throw new ArgumentException("Archive-backed reconstruction requires an exact SHA-256 payload identity.", nameof(expectedHash));
			if (expectedByteLength < 0)
				throw new ArgumentOutOfRangeException(nameof(expectedByteLength));

			string canonicalEntry = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, archiveEntryPath).Path;
			string payload = "nmmce-basic-owner-archive-entry-v1\0" + nativeSnapshotKey.ToUpperInvariant() + "\0" +
				((int)installContext.Method).ToString(CultureInfo.InvariantCulture) + "\0" +
				((int)installContext.InstallRoot).ToString(CultureInfo.InvariantCulture) + "\0" +
				canonicalEntry.ToUpperInvariant() + "\0" + expectedHash.Value + "\0" +
				expectedByteLength.ToString(CultureInfo.InvariantCulture);
			using (SHA256 sha256 = SHA256.Create())
				return "basic-exact-archive-entry-v1:" + ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(payload)));
		}

		private static string ToHex(byte[] value)
		{
			var builder = new StringBuilder(value.Length * 2);
			foreach (byte item in value)
				builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
			return builder.ToString();
		}
	}
}
