namespace NexusClientTests
{
	using System;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModRepositories;
	using NUnit.Framework;

	/// <summary>
	/// Verifies conservative matching of repository-backed mod files.
	/// </summary>
	public class ModFileIdentityTests
	{
		/// <summary>
		/// Ensures separate files published under the same Nexus mod are not treated as upgrades of one another.
		/// </summary>
		[Test]
		public void DifferentFileIdsUnderSameModDoNotMatch()
		{
			Assert.IsFalse(ModFileIdentity.IsSameRepositoryFile("100", "200", "100", "201"));
		}

		/// <summary>
		/// Ensures matching usable mod and file identifiers represent the same repository file.
		/// </summary>
		[Test]
		public void MatchingModAndFileIdsMatch()
		{
			Assert.IsTrue(ModFileIdentity.IsSameRepositoryFile("100", "200", "100", "200"));
		}

		/// <summary>
		/// Ensures missing and sentinel file identifiers cannot trigger upgrade matching.
		/// </summary>
		[TestCase(null)]
		[TestCase("")]
		[TestCase("0")]
		[TestCase("-1")]
		public void InvalidFileIdsDoNotMatch(string fileId)
		{
			Assert.IsFalse(ModFileIdentity.IsSameRepositoryFile("100", fileId, "100", fileId));
		}

		/// <summary>
		/// Ensures matching file identifiers under different Nexus mods are not treated as the same file.
		/// </summary>
		[Test]
		public void MatchingFileIdsUnderDifferentModsDoNotMatch()
		{
			Assert.IsFalse(ModFileIdentity.IsSameRepositoryFile("100", "200", "101", "200"));
		}

		/// <summary>
		/// Ensures sentinel mod identifiers cannot participate in upgrade matching.
		/// </summary>
		[TestCase("0")]
		[TestCase("-1")]
		[TestCase("")]
		public void InvalidModIdsDoNotMatch(string modId)
		{
			Assert.IsFalse(ModFileIdentity.IsSameRepositoryFile(modId, "200", modId, "200"));
		}

		/// <summary>
		/// Ensures combined repository metadata uses the selected Nexus file identity and version rather than the parent mod version.
		/// </summary>
		[Test]
		public void CombinedMetadataUsesFileIdentityAndVersion()
		{
			var modInfo = new ModInfo
			{
				Id = "100",
				ModName = "Parent Mod",
				FileName = "old-file.zip",
				HumanReadableVersion = "9.0",
				LastKnownVersion = "9.0"
			};
			var fileInfo = new ModFileInfo("200", "specific-file.zip", "Specific File", "1.2");

			var combinedInfo = AutoTagger.CombineInfo(modInfo, fileInfo);

			Assert.AreEqual("200", combinedInfo.DownloadId);
			Assert.AreEqual("specific-file.zip", combinedInfo.FileName);
			Assert.AreEqual("1.2", combinedInfo.HumanReadableVersion);
			Assert.AreEqual("1.2", combinedInfo.LastKnownVersion);
		}

		/// <summary>
		/// Ensures an unversioned Nexus file does not inherit the parent mod version.
		/// </summary>
		[Test]
		public void CombinedUnversionedFileDoesNotUseParentVersion()
		{
			var modInfo = new ModInfo
			{
				Id = "100",
				HumanReadableVersion = "9.0",
				LastKnownVersion = "9.0",
				MachineVersion = new Version(9, 0)
			};
			var fileInfo = new ModFileInfo("200", "specific-file.zip", "Specific File", null);

			var combinedInfo = AutoTagger.CombineInfo(modInfo, fileInfo);

			Assert.IsNull(combinedInfo.HumanReadableVersion);
			Assert.IsNull(combinedInfo.LastKnownVersion);
			Assert.IsNull(combinedInfo.MachineVersion);
		}

		/// <summary>
		/// Ensures an upgrade prompt requires two present and genuinely different versions.
		/// </summary>
		[TestCase("1.0", "1.1", true)]
		[TestCase("1.0", "1.0", false)]
		[TestCase(" 1.0 ", "1.0", false)]
		[TestCase(null, "1.1", false)]
		[TestCase("1.0", "", false)]
		public void DifferentVersionDetectionIsConservative(string currentVersion, string candidateVersion, bool expected)
		{
			Assert.AreEqual(expected, ModFileIdentity.HasDifferentKnownVersion(currentVersion, candidateVersion));
		}
	}
}
