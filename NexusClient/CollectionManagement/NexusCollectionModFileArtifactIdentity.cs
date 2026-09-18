using System;
using System.Globalization;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Parses the canonical Nexus mod-file artifact identity used by Collection acquisition.
	/// </summary>
	internal static class NexusCollectionModFileArtifactIdentity
	{
		public const string Scheme = "nexus-mod-file";

		/// <summary>
		/// Parses a normalized Collection artifact into its Nexus game, mod and file identity.
		/// </summary>
		public static bool TryParse(CollectionArtifactReference artifact, out string gameDomain, out long modId, out long fileId)
		{
			gameDomain = null;
			modId = 0;
			fileId = 0;
			return artifact != null &&
				StringComparer.Ordinal.Equals(artifact.Scheme, Scheme) &&
				TryParseStableId(artifact.StableId, out gameDomain, out modId, out fileId);
		}

		/// <summary>
		/// Parses the stable Nexus artifact token emitted by the Collection normalizer.
		/// </summary>
		public static bool TryParseStableId(string stableId, out string gameDomain, out long modId, out long fileId)
		{
			gameDomain = null;
			modId = 0;
			fileId = 0;
			if (String.IsNullOrWhiteSpace(stableId))
				return false;

			string[] parts = stableId.Split('/');
			if (parts.Length != 3 || String.IsNullOrWhiteSpace(parts[0]))
				return false;
			if (!Int64.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out modId) || modId <= 0)
				return false;
			if (!Int64.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out fileId) || fileId <= 0)
				return false;

			gameDomain = parts[0].Trim().ToLowerInvariant();
			return gameDomain.Length > 0;
		}

		/// <summary>
		/// Creates the canonical stable artifact token for one Nexus mod file.
		/// </summary>
		public static string Format(string gameDomain, long modId, long fileId)
		{
			if (String.IsNullOrWhiteSpace(gameDomain))
				throw new ArgumentException("A Nexus game domain is required.", nameof(gameDomain));
			if (modId <= 0)
				throw new ArgumentOutOfRangeException(nameof(modId));
			if (fileId <= 0)
				throw new ArgumentOutOfRangeException(nameof(fileId));

			return gameDomain.Trim().ToLowerInvariant() + "/" +
				modId.ToString(CultureInfo.InvariantCulture) + "/" +
				fileId.ToString(CultureInfo.InvariantCulture);
		}
	}
}
