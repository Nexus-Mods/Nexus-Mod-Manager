using System;
using System.Globalization;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Canonical exact identity for Vortex manual/browse/direct archives whose retained source supplies MD5 and byte length.</summary>
	internal static class CollectionExternalArtifactIdentity
	{
		public const string Scheme = "vortex-external-file";

		public static string Format(string md5, long byteLength)
		{
			string normalizedMd5;
			if (!TryNormalizeMd5(md5, out normalizedMd5))
				throw new ArgumentException("A 32-character hexadecimal MD5 digest is required.", nameof(md5));
			if (byteLength <= 0)
				throw new ArgumentOutOfRangeException(nameof(byteLength));
			return normalizedMd5 + "/" + byteLength.ToString(CultureInfo.InvariantCulture);
		}

		public static bool TryParse(CollectionArtifactReference artifact, out string md5, out long byteLength)
		{
			md5 = null;
			byteLength = 0;
			return artifact != null && StringComparer.Ordinal.Equals(artifact.Scheme, Scheme) &&
				TryParseStableId(artifact.StableId, out md5, out byteLength);
		}

		public static bool TryParseStableId(string stableId, out string md5, out long byteLength)
		{
			md5 = null;
			byteLength = 0;
			if (String.IsNullOrWhiteSpace(stableId))
				return false;
			string[] parts = stableId.Split('/');
			return parts.Length == 2 && TryNormalizeMd5(parts[0], out md5) &&
				Int64.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out byteLength) && byteLength > 0;
		}

		public static bool TryNormalizeMd5(string value, out string normalized)
		{
			normalized = null;
			if (String.IsNullOrWhiteSpace(value) || value.Length != 32 || !StringComparer.Ordinal.Equals(value, value.Trim()))
				return false;
			for (int index = 0; index < value.Length; index++)
			{
				char c = value[index];
				if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
					return false;
			}
			normalized = value.ToLowerInvariant();
			return true;
		}

		public static bool MatchesByteLength(CollectionArtifactReference artifact, long byteLength)
		{
			string md5;
			long expected;
			return !TryParse(artifact, out md5, out expected) || expected == byteLength;
		}
	}
}
