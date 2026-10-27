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

		/// <summary>Launch switches preceding the download must not turn a protocol launch into an empty discovery shell.</summary>
		[TestCase("-trace")]
		[TestCase("/trace")]
		[TestCase("-legacy-startup")]
		public void NexusLink_AfterLaunchSwitchRetainsFullLinkAndGame(string launchSwitch)
		{
			const string link = "nxm://fallout4/mods/12/files/34?key=test&expires=123&user_id=4";
			var intent = GameDiscoveryStartupIntent.Parse(new[] { launchSwitch, "-game", "Other", link }, domain => "Fallout4");
			Assert.That(intent.RequestedModeId, Is.EqualTo("Fallout4"));
			Assert.That(intent.NxmLink.OriginalString, Is.EqualTo(link));
			Assert.That(intent.RequiresGameSelection, Is.False);
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
