using System;
using System.Reflection;
using System.Runtime.Serialization;
using Nexus.Client;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.UI;
using Nexus.Client.Settings;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies shared Witcher catalog routing without saving real user settings, starting IPC or downloading files.</summary>
	[TestFixture]
	public class Witcher3DownloadModeTests
	{
		private string _preference;
		private string _skyrimPreference;
		private string _rememberedMode;
		private int _saves;
		private bool _rejectSave;
		private ISettings _settings;

		/// <summary>Creates in-memory user settings using the existing interface proxy fixture.</summary>
		[SetUp]
		public void SetUp()
		{
			_preference = null;
			_skyrimPreference = "SkyrimSE";
			_rememberedMode = "Witcher3";
			_saves = 0;
			_rejectSave = false;
			_settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Witcher3DownloadOverride": return _preference;
					case "set_Witcher3DownloadOverride": _preference = (string)args[0]; return null;
					case "get_SkyrimSEDownloadOverride": return _skyrimPreference;
					case "set_SkyrimSEDownloadOverride": _skyrimPreference = (string)args[0]; return null;
					case "get_RememberedGameMode": return _rememberedMode;
					case "Save":
						if (_rejectSave) throw new InvalidOperationException("Simulated settings save failure.");
						_saves++; return null;
					default: return null;
				}
			});
		}

		/// <summary>Exercises the actual bootstrap mapping consumed by both startup paths.</summary>
		private string RequestedMode(string domain)
		{
			var environment = (Nexus.Client.EnvironmentInfo)FormatterServices.GetUninitializedObject(typeof(Nexus.Client.EnvironmentInfo));
			SetField(environment, "<Settings>k__BackingField", _settings);
			var bootstrapper = new Bootstrapper(environment);
			return (string)typeof(Bootstrapper).GetMethod("DetermineRequestedGameMode", BindingFlags.Instance | BindingFlags.NonPublic)
				.Invoke(bootstrapper, new object[] { domain });
		}

		/// <summary>Builds only the view-model dependencies relevant to the edition selector.</summary>
		private ModManagerVM ViewModel(string modeId)
		{
			IGameMode mode = InterfaceStub<IGameMode>.Create((method, args) => method.Name == "get_ModeId" ? modeId : null);
			var manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
			SetField(manager, "<GameMode>k__BackingField", mode);
			var viewModel = (ModManagerVM)FormatterServices.GetUninitializedObject(typeof(ModManagerVM));
			SetField(viewModel, "<ModManager>k__BackingField", manager);
			SetField(viewModel, "<Settings>k__BackingField", _settings);
			return viewModel;
		}

		/// <summary>Assigns one backing field without starting native application services.</summary>
		private static void SetField(object target, string name, object value) =>
			target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

		/// <summary>Saved download destinations override the startup game for ordinary and collection links.</summary>
		[TestCase("Witcher3Remastered", "Witcher3")]
		[TestCase("Witcher3", "Witcher3Remastered")]
		[TestCase("witcher3remastered", "SkyrimSE")]
		public void IncomingWitcherLinks_UseSavedEdition_WithoutChangingCatalogOrStartupPreference(string preference, string remembered)
		{
			_preference = preference;
			_rememberedMode = remembered;
			string expected = NexusGameDomain.ResolveWitcher3DownloadMode(preference, remembered);
			foreach (string link in new[] { "nxm://witcher3/mods/123/files/456", "nxm://witcher3/collections/test/revisions/1" })
			{
				var intent = GameDiscoveryStartupIntent.Parse(new[] { link }, RequestedMode);
				Assert.That(intent.RequestedModeId, Is.EqualTo(expected));
				Assert.That(intent.NxmLink.Host, Is.EqualTo("witcher3"));
			}
			Assert.That(_rememberedMode, Is.EqualTo(remembered));
			Assert.That(_saves, Is.Zero);
		}

		/// <summary>Missing and invalid preferences retain safe, edition-specific fallback behavior.</summary>
		[TestCase(null, "Witcher3Remastered", "Witcher3Remastered")]
		[TestCase("", "Witcher3", "Witcher3")]
		[TestCase("invalid", "witcher3remastered", "Witcher3Remastered")]
		[TestCase("SkyrimSE", "Fallout4", "Witcher3")]
		public void MissingOrInvalidPreference_UsesRememberedWitcherEditionThenLegacyDefault(string preference, string remembered, string expected)
		{
			_preference = preference;
			_rememberedMode = remembered;
			Assert.That(RequestedMode("WITCHER3"), Is.EqualTo(expected));
			Assert.That(_saves, Is.Zero);
		}

		/// <summary>The button exposes both editions and persists only the download choice.</summary>
		[TestCase("Witcher3", "Witcher3Remastered")]
		[TestCase("Witcher3Remastered", "Witcher3")]
		public void ToolbarToggle_SavesOtherEdition_AndNextLinkUsesIt(string current, string next)
		{
			_preference = current;
			var viewModel = ViewModel(current);
			int changed = 0;
			viewModel.DownloadModeChanged += (sender, args) => changed++;
			Assert.That(viewModel.HasDownloadModeChoice, Is.True);
			Assert.That(viewModel.ToggleDownloadMode(), Is.True);
			Assert.That(_preference, Is.EqualTo(next));
			Assert.That(_saves, Is.EqualTo(1));
			Assert.That(changed, Is.EqualTo(1));
			Assert.That(RequestedMode("witcher3"), Is.EqualTo(next));
			Assert.That(ViewModel(current).DownloadModeLabel, Does.Contain(next == "Witcher3Remastered" ? "Remastered" : "Witcher 3"));
			Assert.That(viewModel.ModManager.GameMode.ModeId, Is.EqualTo(current));
			Assert.That(_rememberedMode, Is.EqualTo("Witcher3"));
			Assert.That(_skyrimPreference, Is.EqualTo("SkyrimSE"));
		}

		/// <summary>An unsaved new preference cannot leave misleading toolbar state after an error.</summary>
		[Test]
		public void SaveFailure_PreservesPreviousDestination_WithoutAnnouncingChange()
		{
			_preference = "Witcher3Remastered";
			_rejectSave = true;
			var viewModel = ViewModel("Witcher3Remastered");
			int changed = 0;
			viewModel.DownloadModeChanged += (sender, args) => changed++;
			Assert.Throws<InvalidOperationException>(() => viewModel.ToggleDownloadMode());
			Assert.That(_preference, Is.EqualTo("Witcher3Remastered"));
			Assert.That(changed, Is.Zero);
		}

		/// <summary>Witcher preferences do not affect other catalog aliases or the Skyrim selector.</summary>
		[Test]
		public void OtherGames_KeepTheirRoutingAndExistingSkyrimToggle()
		{
			_preference = "Witcher3Remastered";
			_skyrimPreference = "SkyrimGOG";
			Assert.That(RequestedMode("skyrimspecialedition"), Is.EqualTo("SkyrimGOG"));
			Assert.That(RequestedMode("newvegas"), Is.EqualTo("FalloutNV"));
			Assert.That(RequestedMode("fallout4"), Is.EqualTo("fallout4"));
			var skyrim = ViewModel("SkyrimGOG");
			Assert.That(skyrim.HasDownloadModeChoice, Is.True);
			Assert.That(skyrim.ToggleDownloadMode(), Is.True);
			Assert.That(_skyrimPreference, Is.EqualTo("SkyrimSE"));
			Assert.That(_preference, Is.EqualTo("Witcher3Remastered"));
			Assert.That(ViewModel("Fallout4").HasDownloadModeChoice, Is.False);
		}

		/// <summary>An explicit command-line game remains independent of the Nexus download preference.</summary>
		[Test]
		public void ExplicitGameArgument_DoesNotUseDownloadPreference()
		{
			_preference = "Witcher3Remastered";
			var intent = GameDiscoveryStartupIntent.Parse(new[] { "-game", "Witcher3" }, RequestedMode);
			Assert.That(intent.RequestedModeId, Is.EqualTo("Witcher3"));
			Assert.That(intent.NxmLink, Is.Null);
		}
	}
}
