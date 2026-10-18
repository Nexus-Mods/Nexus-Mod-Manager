using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3RemasteredHealthReaderTests
	{
		private string _root;
		private string _game;
		private string _documents;
		private string _dx12;
		private string _settings;

		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-w3r-health-" + Guid.NewGuid().ToString("N"));
			_game = Path.Combine(_root, "Game");
			_documents = Path.Combine(_root, "Documents");
			_dx12 = Path.Combine(_game, "bin", "x64_dx12", "witcher3.exe");
			_settings = Path.Combine(_documents, "The Witcher 3", "dx12user.settings");
			Directory.CreateDirectory(_game);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_root)) Directory.Delete(_root, true);
		}

		private Witcher3RemasteredHealthObservation Inspect(Witcher3Edition edition = Witcher3Edition.Remastered)
		{
			return Witcher3RemasteredHealthReader.Read(_game, _documents, edition);
		}

		private void Executable()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_dx12));
			File.WriteAllBytes(_dx12, new byte[] { 0x4D, 0x5A });
		}

		private void Settings(string text, Encoding encoding = null)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_settings));
			File.WriteAllText(_settings, text, encoding ?? new UTF8Encoding(false, true));
		}

		private static bool Has(Witcher3RemasteredHealthObservation report,
			Witcher3RemasteredHealthIssueCode code)
		{
			return report.Issues.Any(issue => issue.Code == code);
		}

		[Test]
		public void BeforeFirstLaunch_MissingSettingsAndExecutableAreObservedWithoutCreatingAnything()
		{
			var report = Inspect();
			Assert.That(report.Dx12ExecutableObserved, Is.False);
			Assert.That(report.SettingsStatus, Is.EqualTo(Witcher3RemasteredSettingsReadStatus.Missing));
			Assert.That(report.GlobalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.NotConfirmed));
			Assert.That(report.LocalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.NotConfirmed));
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.Dx12ExecutableMissing), Is.True);
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.SettingsMissing), Is.True);
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.GlobalModsDisabled), Is.False);
			Assert.That(Directory.Exists(_documents), Is.False);
		}

		[Test]
		public void ExplicitlyEnabledSwitches_AreObservedButNeverClaimGameEffectiveModLoading()
		{
			Executable();
			Settings("[Graphics]\r\nEnabled=0\r\n[ContentManager/Mods]\r\nEnabled=true\r\nEnabledLocal=1\r\n");
			byte[] before = File.ReadAllBytes(_settings);
			var report = Inspect();
			Assert.That(report.Edition, Is.EqualTo(Witcher3Edition.Remastered));
			Assert.That(report.Dx12ExecutableObserved, Is.True);
			Assert.That(report.SettingsStatus, Is.EqualTo(Witcher3RemasteredSettingsReadStatus.Loaded));
			Assert.That(report.GlobalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Enabled));
			Assert.That(report.LocalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Enabled));
			Assert.That(report.Issues, Is.Empty);
			Assert.That(File.ReadAllBytes(_settings), Is.EqualTo(before));
		}

		[Test]
		public void ExplicitGlobalDisable_ReportsOnlyGlobalSwitchAsDisabled()
		{
			Executable();
			Settings("[ContentManager/Mods]\r\nEnabled = 0\r\nEnabledLocal = true\r\n");
			var report = Inspect();
			Assert.That(report.GlobalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Disabled));
			Assert.That(report.LocalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Enabled));
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.GlobalModsDisabled), Is.True);
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.LocalModsDisabled), Is.False);
		}

		[Test]
		public void ExplicitLocalDisable_ReportsOnlyLocalSwitchAsDisabled()
		{
			Executable();
			Settings("[ContentManager/Mods]\r\nEnabled=1\r\nEnabledLocal=FALSE\r\n");
			var report = Inspect();
			Assert.That(report.GlobalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Enabled));
			Assert.That(report.LocalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Disabled));
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.LocalModsDisabled), Is.True);
		}

		[Test]
		public void MissingOrUnrecognizedKeys_AreNotGuessedAsDisabled()
		{
			Executable();
			Settings("; comment\r\n[OtherSection]\r\nEnabledLocal=0\r\n[ContentManager/Mods]\r\nEnabled=maybe\r\n");
			var report = Inspect();
			Assert.That(report.SettingsStatus, Is.EqualTo(Witcher3RemasteredSettingsReadStatus.Loaded));
			Assert.That(report.GlobalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.NotConfirmed));
			Assert.That(report.LocalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.NotConfirmed));
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.GlobalModsNotConfirmed), Is.True);
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.LocalModsNotConfirmed), Is.True);
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.LocalModsDisabled), Is.False);
		}

		[Test]
		public void DuplicateKeys_AreAmbiguousEvenWhenOneValueLooksDisabled()
		{
			Settings("[ContentManager/Mods]\nEnabled=1\nEnabled=0\nEnabledLocal=1\n");
			var report = Inspect();
			Assert.That(report.SettingsStatus, Is.EqualTo(Witcher3RemasteredSettingsReadStatus.Ambiguous));
			Assert.That(report.GlobalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Ambiguous));
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.SettingsAmbiguous), Is.True);
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.GlobalModsDisabled), Is.False);
		}

		[Test]
		public void DuplicateContentManagerSections_AreAmbiguous()
		{
			Settings("[ContentManager/Mods]\nEnabled=0\n[Graphics]\nTest=1\n[CONTENTMANAGER/MODS]\nEnabledLocal=1\n");
			var report = Inspect();
			Assert.That(report.SettingsStatus, Is.EqualTo(Witcher3RemasteredSettingsReadStatus.Ambiguous));
			Assert.That(report.LocalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Ambiguous));
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.GlobalModsDisabled), Is.False);
		}

		[Test]
		public void BomEncodingsAndCaseInsensitiveSection_AreReadWithoutRewriting()
		{
			Executable();
			foreach (Encoding encoding in new Encoding[] {
				new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true),
				new UnicodeEncoding(true, true, true) })
			{
				Settings("[ contentmanager/mods ]\r\nENABLED = false\r\nenabledlocal = 1\r\n", encoding);
				byte[] original = File.ReadAllBytes(_settings);
				var report = Inspect();
				Assert.That(report.SettingsStatus, Is.EqualTo(Witcher3RemasteredSettingsReadStatus.Loaded));
				Assert.That(report.GlobalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Disabled));
				Assert.That(report.LocalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Enabled));
				Assert.That(File.ReadAllBytes(_settings), Is.EqualTo(original));
			}
		}

		[Test]
		public void MalformedEncoding_CannotInventSwitchValues()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_settings));
			File.WriteAllBytes(_settings, new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0x28 });
			var report = Inspect();
			Assert.That(report.SettingsStatus, Is.EqualTo(Witcher3RemasteredSettingsReadStatus.UnsupportedEncoding));
			Assert.That(report.GlobalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.NotConfirmed));
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.SettingsUnsupportedEncoding), Is.True);
		}

		[Test]
		public void OversizedConfiguration_DoesNotReadOrInterpretBeyondLimit()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_settings));
			using (var file = new FileStream(_settings, FileMode.CreateNew))
				file.SetLength(Witcher3RemasteredHealthReader.MaximumSettingsBytes + 1L);
			var report = Inspect();
			Assert.That(report.SettingsStatus, Is.EqualTo(Witcher3RemasteredSettingsReadStatus.TooLarge));
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.SettingsTooLarge), Is.True);
		}

		[Test]
		public void ExternalEdit_IsObservedOnNextReadWithoutChangingFileOrModeState()
		{
			Executable();
			Settings("[ContentManager/Mods]\nEnabled=1\nEnabledLocal=1\n");
			Assert.That(Inspect().Issues, Is.Empty);
			Settings("[ContentManager/Mods]\nEnabled=1\nEnabledLocal=0\n");
			byte[] next = File.ReadAllBytes(_settings);
			Assert.That(Has(Inspect(), Witcher3RemasteredHealthIssueCode.LocalModsDisabled), Is.True);
			Assert.That(File.ReadAllBytes(_settings), Is.EqualTo(next));
		}

		[Test]
		public void AmbiguousEdition_StaysWarningEvenWhenDx12AndBothSwitchesExist()
		{
			Executable();
			Settings("[ContentManager/Mods]\nEnabled=1\nEnabledLocal=1\n");
			var report = Inspect(Witcher3Edition.Ambiguous);
			Assert.That(Has(report, Witcher3RemasteredHealthIssueCode.EditionUnconfirmed), Is.True);
			Assert.That(report.GlobalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Enabled));
			Assert.That(report.Issues.Any(x => x.Severity == Witcher3RemasteredHealthSeverity.Warning), Is.True);
		}

		[Test]
		public void RejectsRelativeRootsInsteadOfAccidentallyReadingWorkingDirectory()
		{
			Assert.Throws<ArgumentException>(() => Witcher3RemasteredHealthReader.Read("relative", _documents, Witcher3Edition.Remastered));
			Assert.Throws<ArgumentException>(() => Witcher3RemasteredHealthReader.Read(_game, "relative", Witcher3Edition.Remastered));
		}
	}
}
