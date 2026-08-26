namespace NexusClientTests
{
	using System;
	using Nexus.Client.ModRepositories;
	using NUnit.Framework;

	/// <summary>
	/// Verifies parsing and navigation behavior for manually entered Nexus Mods links.
	/// </summary>
	public class NexusModLinkParserTests
	{
		/// <summary>
		/// Ensures a current Nexus mod page exposes the game and mod identifier.
		/// </summary>
		[Test]
		public void ParsesCurrentModPage()
		{
			NexusModLink link;
			Assert.IsTrue(NexusModLinkParser.TryParse("https://www.nexusmods.com/skyrimspecialedition/mods/12345", out link));
			Assert.AreEqual("skyrimspecialedition", link.GameDomain);
			Assert.AreEqual("12345", link.ModId);
			Assert.IsNull(link.FileId);
		}

		/// <summary>
		/// Ensures a file-specific Nexus page exposes both mod and file identifiers.
		/// </summary>
		[Test]
		public void ParsesCurrentFilePage()
		{
			NexusModLink link;
			Assert.IsTrue(NexusModLinkParser.TryParse("https://www.nexusmods.com/fallout4/mods/100?tab=files&file_id=200", out link));
			Assert.AreEqual("100", link.ModId);
			Assert.AreEqual("200", link.FileId);
		}

		/// <summary>
		/// Ensures legacy game-subdomain links remain supported.
		/// </summary>
		[Test]
		public void ParsesLegacySubdomainLink()
		{
			NexusModLink link;
			Assert.IsTrue(NexusModLinkParser.TryParse("https://skyrim.nexusmods.com/mods/321", out link));
			Assert.AreEqual("skyrim", link.GameDomain);
			Assert.AreEqual("321", link.ModId);
		}

		/// <summary>
		/// Ensures NXM links expose the exact file identity.
		/// </summary>
		[Test]
		public void ParsesNxmFileLink()
		{
			NexusModLink link;
			Assert.IsTrue(NexusModLinkParser.TryParse("nxm://fallout4/mods/100/files/200", out link));
			Assert.AreEqual("fallout4", link.GameDomain);
			Assert.AreEqual("100", link.ModId);
			Assert.AreEqual("200", link.FileId);
		}

		/// <summary>
		/// Ensures a manually entered address without a scheme is normalized to HTTPS.
		/// </summary>
		[Test]
		public void NormalizesMissingScheme()
		{
			Uri uri;
			Assert.IsTrue(NexusModLinkParser.TryNormalizeWebsite("www.nexusmods.com/skyrim/mods/42", out uri));
			Assert.AreEqual("https", uri.Scheme);
			Assert.AreEqual("www.nexusmods.com", uri.Host);
		}

		/// <summary>
		/// Ensures a stored exact file URL is used without replacement.
		/// </summary>
		[Test]
		public void ExactStoredFileLinkWins()
		{
			var stored = new Uri("https://www.nexusmods.com/fallout4/mods/100?tab=files&file_id=201");
			Uri resolved = NexusModLinkParser.ResolveNavigationUri(stored, "fallout4", "100", "200");
			Assert.AreEqual(stored, resolved);
		}

		/// <summary>
		/// Ensures a generic stored mod page is promoted to the known exact file page.
		/// </summary>
		[Test]
		public void GenericStoredModPageUsesKnownFileId()
		{
			var stored = new Uri("https://www.nexusmods.com/fallout4/mods/100");
			Uri resolved = NexusModLinkParser.ResolveNavigationUri(stored, "fallout4", "100", "200");
			Assert.AreEqual("https://www.nexusmods.com/fallout4/mods/100?tab=files&file_id=200", resolved.ToString());
		}

		/// <summary>
		/// Ensures numeric IDs provide a navigation fallback when no website was stored.
		/// </summary>
		[Test]
		public void StoredIdsBuildNavigationFallback()
		{
			Uri resolved = NexusModLinkParser.ResolveNavigationUri(null, "fallout4", "100", "200");
			Assert.AreEqual("https://www.nexusmods.com/fallout4/mods/100?tab=files&file_id=200", resolved.ToString());
		}

		/// <summary>
		/// Ensures a file identifier is not attached to a stored page for a different mod.
		/// </summary>
		[Test]
		public void MismatchedStoredModPageIsNotPromoted()
		{
			var stored = new Uri("https://www.nexusmods.com/fallout4/mods/100");
			Assert.AreEqual(stored, NexusModLinkParser.ResolveNavigationUri(stored, "fallout4", "101", "200"));
		}

		/// <summary>
		/// Ensures invalid identifiers do not produce a fabricated Nexus address.
		/// </summary>
		[Test]
		public void InvalidIdsDoNotBuildNavigationFallback()
		{
			Assert.IsNull(NexusModLinkParser.ResolveNavigationUri(null, "fallout4", "0", "-1"));
		}

		/// <summary>
		/// Ensures the Latest-column main-page option strips a stored Nexus file target.
		/// </summary>
		[Test]
		public void MainModPageOptionUsesStoredNexusModPage()
		{
			var stored = new Uri("https://www.nexusmods.com/fallout4/mods/100?tab=files&file_id=201");
			Uri resolved = NexusModLinkParser.ResolveNavigationUri(stored, "fallout4", "100", "200", true);
			Assert.AreEqual("https://www.nexusmods.com/fallout4/mods/100", resolved.ToString());
		}

		/// <summary>
		/// Ensures the Latest-column main-page option builds a mod-page fallback without a file identifier.
		/// </summary>
		[Test]
		public void MainModPageOptionBuildsModPageFallback()
		{
			Uri resolved = NexusModLinkParser.ResolveNavigationUri(null, "fallout4", "100", "200", true);
			Assert.AreEqual("https://www.nexusmods.com/fallout4/mods/100", resolved.ToString());
		}

		/// <summary>
		/// Ensures non-Nexus websites are preserved as explicitly entered.
		/// </summary>
		[Test]
		public void NonNexusWebsiteIsPreserved()
		{
			var stored = new Uri("https://example.com/mod-page");
			Assert.AreEqual(stored, NexusModLinkParser.ResolveNavigationUri(stored, "fallout4", "100", "200"));
		}
	}
}
