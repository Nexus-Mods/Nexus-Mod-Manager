using System;
using Nexus.Client.Games;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class GameDiscoveryStartupIntentTests
	{
		[Test]
		public void NoArguments_OpenShellWithoutRequestedGame()
		{
			var intent = GameDiscoveryStartupIntent.Parse(new string[0], value => value);
			Assert.That(intent.RequestedModeId, Is.Null);
			Assert.That(intent.NxmLink, Is.Null);
		}

		[Test]
		public void ExplicitGame_OverridesRememberedDefaultWithoutPersistingIt()
		{
			var intent = GameDiscoveryStartupIntent.Parse(new[] { "-trace", "-game", "SkyrimGOG" }, value => value);
			Assert.That(intent.RequestedModeId, Is.EqualTo("SkyrimGOG"));
			Assert.That(intent.NxmLink, Is.Null);
		}

		[Test]
		public void DanglingGameFlag_DoesNotThrowOrInventMode()
		{
			var intent = GameDiscoveryStartupIntent.Parse(new[] { "-game" }, value => value);
			Assert.That(intent.RequestedModeId, Is.Null);
		}

		[Test]
		public void NexusLink_IsRetainedForDeliveryAfterMatchingSessionOpens()
		{
			var intent = GameDiscoveryStartupIntent.Parse(new[] { "nxm://skyrimspecialedition/mods/123/files/456", "-game", "Other" },
				value => String.Equals(value, "skyrimspecialedition", StringComparison.OrdinalIgnoreCase) ? "SkyrimSE" : value);
			Assert.That(intent.RequestedModeId, Is.EqualTo("SkyrimSE"));
			Assert.That(intent.NxmLink, Is.Not.Null);
			Assert.That(intent.NxmLink.Host, Is.EqualTo("skyrimspecialedition"));
		}

		[Test]
		public void NonNexusUri_DoesNotStartDownloadOrGameSelection()
		{
			var intent = GameDiscoveryStartupIntent.Parse(new[] { "https://example.org/mods/10" }, value => value);
			Assert.That(intent.RequestedModeId, Is.Null);
			Assert.That(intent.NxmLink, Is.Null);
		}
	}
}
