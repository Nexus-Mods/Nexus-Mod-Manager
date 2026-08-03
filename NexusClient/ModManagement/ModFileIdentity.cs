using System;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Provides conservative comparisons for repository-backed mod-file identities.
	/// </summary>
	public static class ModFileIdentity
	{
		/// <summary>
		/// Determines whether the specified repository identifier can safely participate in file matching.
		/// </summary>
		/// <param name="identifier">The repository identifier to validate.</param>
		/// <returns><c>true</c> when the identifier is non-empty and is not a known sentinel value; otherwise, <c>false</c>.</returns>
		public static bool IsUsableRepositoryId(string identifier)
		{
			return !string.IsNullOrWhiteSpace(identifier)
				&& !identifier.Equals("0", StringComparison.OrdinalIgnoreCase)
				&& !identifier.Equals("-1", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Determines whether two metadata records identify the same repository mod file.
		/// </summary>
		/// <param name="leftModId">The first record's mod identifier.</param>
		/// <param name="leftFileId">The first record's file identifier.</param>
		/// <param name="rightModId">The second record's mod identifier.</param>
		/// <param name="rightFileId">The second record's file identifier.</param>
		/// <returns><c>true</c> when both records have usable identifiers and identify the same mod file; otherwise, <c>false</c>.</returns>
		public static bool IsSameRepositoryFile(string leftModId, string leftFileId, string rightModId, string rightFileId)
		{
			return IsUsableRepositoryId(leftModId)
				&& IsUsableRepositoryId(leftFileId)
				&& IsUsableRepositoryId(rightModId)
				&& IsUsableRepositoryId(rightFileId)
				&& leftModId.Equals(rightModId, StringComparison.OrdinalIgnoreCase)
				&& leftFileId.Equals(rightFileId, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Determines whether two non-empty file versions are different.
		/// </summary>
		/// <param name="leftVersion">The first file version.</param>
		/// <param name="rightVersion">The second file version.</param>
		/// <returns><c>true</c> when both versions are present and differ; otherwise, <c>false</c>.</returns>
		public static bool HasDifferentKnownVersion(string leftVersion, string rightVersion)
		{
			return !string.IsNullOrWhiteSpace(leftVersion)
				&& !string.IsNullOrWhiteSpace(rightVersion)
				&& !leftVersion.Trim().Equals(rightVersion.Trim(), StringComparison.OrdinalIgnoreCase);
		}
	}
}
