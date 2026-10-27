using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Ipc;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters;
using System.Threading;
using System.Windows.Forms;
using Nexus.Client;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Settings;
using Nexus.Client.Util;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies hidden startup forwarding and IPC channel cleanup without downloading or opening a real game session.</summary>
	[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
	public class NexusLinkHandoffTests
	{
		/// <summary>Preserves the full authenticated link and the canonical local endpoint name.</summary>
		[TestCase("nxm://witcher3/mods/12/files/34?key=test&expires=123&user_id=4", "Witcher3Remastered")]
		[TestCase("nxm://witcher3/collections/example/revisions/2", "Witcher3Remastered")]
		[TestCase("nxm://skyrim/mods/12/files/34", "Skyrim")]
		[TestCase("nxm://fallout4/mods/12/files/34", "Fallout4")]
		public void Forwarding_AcceptsLinkOnce_AndDisposesTheClient(string link, string mode)
		{
			string received = null;
			string endpoint = null;
			int deliveries = 0;
			int disposals = 0;
			IMessager client = InterfaceStub<IMessager>.Create((method, args) =>
			{
				if (method.Name == "AddMod") { received = (string)args[0]; deliveries++; }
				if (method.Name == "Dispose") disposals++;
				return null;
			});
			var intent = GameDiscoveryStartupIntent.Parse(new[] { link }, domain => mode.ToLowerInvariant());
			Assert.That(NexusLinkHandoff.TryForward(intent, new[] { mode }, id => { endpoint = id; return client; }), Is.True);
			Assert.That(endpoint, Is.EqualTo(mode));
			Assert.That(received, Is.EqualTo(link));
			Assert.That(deliveries, Is.EqualTo(1));
			Assert.That(disposals, Is.EqualTo(1));
		}

		/// <summary>Normal starts and explicit game selection never send a download request.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void NonNexusStartup_DoesNotProbeAnIpcEndpoint(bool explicitGame)
		{
			string[] arguments = explicitGame ? new[] { "-game", "Witcher3Remastered" } : new string[0];
			var intent = GameDiscoveryStartupIntent.Parse(arguments, value => value);
			Assert.That(NexusLinkHandoff.TryForward(intent, new string[0], id =>
			{
				Assert.Fail("Only incoming Nexus links use the hidden handoff.");
				return null;
			}), Is.False);
		}

		/// <summary>A missing or failed receiver leaves the startup link available for ordinary startup.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void UnavailableReceiver_PreservesThePendingLink(bool failDelivery)
		{
			var intent = GameDiscoveryStartupIntent.Parse(new[] { "nxm://witcher3/mods/1/files/2" }, value => "Witcher3Remastered");
			int disposals = 0;
			IMessager client = InterfaceStub<IMessager>.Create((method, args) =>
			{
				if (method.Name == "AddMod") throw new RemotingException("Receiver disconnected.");
				if (method.Name == "Dispose") disposals++;
				return null;
			});
			Assert.That(NexusLinkHandoff.TryForward(intent, null, id => failDelivery ? client : null), Is.False);
			Assert.That(intent.NxmLink.OriginalString, Is.EqualTo("nxm://witcher3/mods/1/files/2"));
			Assert.That(disposals, Is.EqualTo(failDelivery ? 1 : 0));
		}

		/// <summary>A cleanup error after acceptance cannot cause a second startup/download.</summary>
		[Test]
		public void AcceptedLink_RemainsAcceptedIfClientDisposalFails()
		{
			var intent = GameDiscoveryStartupIntent.Parse(new[] { "nxm://witcher3/mods/1/files/2" }, value => "Witcher3");
			IMessager client = InterfaceStub<IMessager>.Create((method, args) =>
			{
				if (method.Name == "Dispose") throw new RemotingException("Channel cleanup failed.");
				return null;
			});
			Assert.That(NexusLinkHandoff.TryForward(intent, null, id => client), Is.True);
		}

		/// <summary>A failed IPC probe must not prevent a later connection or an in-process listener from registering.</summary>
		[Test]
		public void MissingListener_ReleasesClientChannel_AndLaterConnectionSucceeds()
		{
			string mode = "Handoff" + Guid.NewGuid().ToString("N");
			IChannel[] before = ChannelServices.RegisteredChannels;
			Assert.That(MessagerClient.GetMessager(mode), Is.Null);
			Assert.That(ChannelServices.RegisteredChannels, Is.EquivalentTo(before));
			WithServer(mode, server =>
			{
				using (IMessager client = MessagerClient.GetMessager(mode))
				{
					Assert.That(client, Is.Not.Null);
					client.AddMod("test-link");
					Assert.That(server.LastLink, Is.EqualTo("test-link"));
				}
			});
			Assert.That(ChannelServices.RegisteredChannels, Is.EquivalentTo(before));
		}

		/// <summary>The real bootstrapper returns after IPC acknowledgement without creating a shell or requiring temporary paths.</summary>
		[Test]
		public void RunningInstance_BootstrapperForwardsBeforeCreatingAnyWindow()
		{
			string mode = "Handoff" + Guid.NewGuid().ToString("N");
			var installed = new SettingsList();
			installed.Add(mode);
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) => method.Name == "get_InstalledGames" ? installed : null);
			var environment = (Nexus.Client.EnvironmentInfo)FormatterServices.GetUninitializedObject(typeof(Nexus.Client.EnvironmentInfo));
			typeof(Nexus.Client.EnvironmentInfo).GetField("<Settings>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(environment, settings);
			string link = "nxm://" + mode.ToLowerInvariant() + "/mods/12/files/34?key=test";
			int windows = Application.OpenForms.Count;
			WithServer(mode, server =>
			{
				Assert.That(new Bootstrapper(environment).RunMainForm(new[] { link }), Is.True);
				Assert.That(server.LastLink, Is.EqualTo(link));
				Assert.That(server.Deliveries, Is.EqualTo(1));
				Assert.That(Application.OpenForms.Count, Is.EqualTo(windows));
			});
		}

		/// <summary>Repeated downloads retain canonical endpoints with missing or legacy-cased saved game IDs.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void FreshRunningGame_RepeatedLinksForwardWithoutDiscoveryWindow(bool legacySavedId)
		{
			string mode = "FreshHandoff" + Guid.NewGuid().ToString("N");
			var original = GameModeRuntimePolicies.All.ToArray();
			try
			{
				GameModeRuntimePolicies.Replace(new[] { new KeyValuePair<string, GameModeRuntimePolicy>(mode, new GameModeRuntimePolicy { SchemaVersion = 1 }) });
				var installed = new SettingsList();
				if (legacySavedId) installed.Add(mode.ToLowerInvariant());
				ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
				{
					if (method.Name == "Save") Assert.Fail("Forwarding must not modify game preferences.");
					if (method.Name == "get_InstalledGames") return installed;
					return null;
				});
				var environment = (Nexus.Client.EnvironmentInfo)FormatterServices.GetUninitializedObject(typeof(Nexus.Client.EnvironmentInfo));
				typeof(Nexus.Client.EnvironmentInfo).GetField("<Settings>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(environment, settings);
				int windows = Application.OpenForms.Count;
				WithServer(mode, server =>
				{
					for (int index = 1; index <= 2; index++)
					{
						string link = "nxm://" + mode.ToLowerInvariant() + "/mods/12/files/" + index + "?key=test";
						Assert.That(new Bootstrapper(environment).RunMainForm(new[] { "-trace", link }), Is.True);
						Assert.That(server.LastLink, Is.EqualTo(link));
						Assert.That(server.Deliveries, Is.EqualTo(index));
						Assert.That(Application.OpenForms.Count, Is.EqualTo(windows));
					}
				});
			}
			finally { GameModeRuntimePolicies.Replace(original); }
		}

		/// <summary>A listener marshal failure cleans up its channel so the same game can open a listener on retry.</summary>
		[Test]
		public void ListenerInitializationFailure_ReleasesChannelAndAllowsRetry()
		{
			string mode = "ListenerRetry" + Guid.NewGuid().ToString("N");
			IChannel[] before = ChannelServices.RegisteredChannels;
			var collision = new RecordingMessager();
			RemotingServices.Marshal(collision, mode + "Listener", typeof(IMessager));
			IMessager listener = null;
			try
			{
				IGameModeDescriptor descriptor = InterfaceStub<IGameModeDescriptor>.Create((method, args) => method.Name == "get_ModeId" ? mode : null);
				Assert.Throws<RemotingException>(() => MessagerServer.InitializeListener(null, descriptor, null, null));
				Assert.That(ChannelServices.RegisteredChannels, Is.EquivalentTo(before));
				RemotingServices.Disconnect(collision);
				listener = MessagerServer.InitializeListener(null, descriptor, null, null);
				Assert.That(listener, Is.Not.Null);
			}
			finally
			{
				listener?.Dispose();
				RemotingServices.Disconnect(collision);
			}
			Assert.That(ChannelServices.RegisteredChannels, Is.EquivalentTo(before));
		}

		/// <summary>Registers only an isolated test endpoint and reliably removes it afterward.</summary>
		private static void WithServer(string mode, Action<RecordingMessager> action)
		{
			// NUnit puts a MarshalByRef test context in the logical call context. Allow it only on this isolated test endpoint.
			var formatter = new BinaryServerFormatterSinkProvider { TypeFilterLevel = TypeFilterLevel.Full };
			var channel = new IpcServerChannel(new Hashtable
			{
				{ "name", mode + "Server" },
				{ "portName", CommonData.ModManagerName + "-" + mode + "IpcServer" }
			}, formatter);
			var server = new RecordingMessager();
			ChannelServices.RegisterChannel(channel, false);
			try
			{
				RemotingServices.Marshal(server, mode + "Listener", typeof(IMessager));
				action(server);
			}
			finally
			{
				RemotingServices.Disconnect(server);
				channel.StopListening(null);
				ChannelServices.UnregisterChannel(channel);
			}
		}

		/// <summary>Receives test messages without invoking any downloader or native game service.</summary>
		public sealed class RecordingMessager : MarshalByRefObject, IMessager
		{
			public string LastLink { get; private set; }
			public int Deliveries { get; private set; }
			/// <summary>Records acceptance of a link.</summary>
			public void AddMod(string link) { LastLink = link; Deliveries++; }
			/// <summary>Does not activate any real window.</summary>
			public void BringToFront() { }
			/// <summary>Acknowledges the existing IPC liveness probe.</summary>
			public void Post() { }
			/// <summary>Has no native services to release.</summary>
			public void Dispose() { }
			/// <summary>Keeps the test endpoint alive until its explicit removal.</summary>
			public override object InitializeLifetimeService() => null;
		}
	}
}
