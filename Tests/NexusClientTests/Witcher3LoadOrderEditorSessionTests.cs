using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3LoadOrderEditorSessionTests
	{
		private string _root;
		private string _game;
		private string _documents;
		private string _settings;

		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-w3r-order-view-" + Guid.NewGuid().ToString("N"));
			_game = Path.Combine(_root, "game");
			_documents = Path.Combine(_root, "documents");
			_settings = Witcher3ModSettingsFileReader.GetFilePath(_documents);
			Directory.CreateDirectory(_game);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_root)) Directory.Delete(_root, true);
		}

		private Witcher3ModSettingsFileReadResult Observe(string text)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_settings));
			File.WriteAllText(_settings, text, new UTF8Encoding(false, true));
			return Witcher3ModSettingsFileReader.Read(_settings);
		}

		private NativeStateCaptureSnapshot NativeCapture()
		{
			var install = new InstallLogReadSnapshot("original-values", 7,
				new InstallLogReadMod[0], new InstallLogReadFile[0], new InstallLogReadIniEdit[0],
				new InstallLogReadGameValue[0], new InstallLogReadDeploymentTarget[0]);
			return new NativeStateCaptureSnapshot(install, new VirtualModReadSnapshot(new VirtualModReadLink[0]),
				new[] { new NativeStateCaptureRoot(ModDeploymentRoot.Data, _game),
					new NativeStateCaptureRoot(ModDeploymentRoot.GameRoot, _game) },
				new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable,
				new NativeStateCaptureReplayReference[0], new NativeStateCapturePlugin[0],
				NativeStateCaptureCoverage.NotApplicable, new NativeStateCaptureIssue[0]);
		}

		private Witcher3LoadOrderProjection Projection(Witcher3ModSettingsFileReadResult read,
			Witcher3Edition edition = Witcher3Edition.Legacy)
		{
			var disk = new Witcher3ComponentDiskObservation(_game, new string[0],
				new[] { @"Mods\modA", @"Mods\modB", @"Mods\modManual", @"Mods\modUnconfigured" }, true);
			return Witcher3LoadOrderProjector.Project(
				Witcher3NativeComponentIndexer.Build(NativeCapture(), _game, disk), read, edition);
		}

		private Witcher3LoadOrderEditorSession Session(Witcher3ModSettingsFileReadResult read,
			string mode = "Witcher3", Witcher3Edition edition = Witcher3Edition.Legacy, bool pending = false)
		{
			return new Witcher3LoadOrderEditorSession(mode, edition, pending, read, Projection(read, edition));
		}

		private const string ThreeSections =
			"; preserve comments\r\n[modA]\r\nPriority=20\r\nEnabled=1\r\n" +
			"[modManual]\r\nPriority=30\r\nVK=external\r\n" +
			"[SomeOtherTool]\r\nKeep=unchanged\r\n" +
			"[modB]\r\nPriority=10\r\nEnabled=0\r\n";

		[Test]
		public void LoadedDraft_ContainsAllConfiguredFoldersAndNeverAdoptsUnconfiguredFolder()
		{
			var read = Observe(ThreeSections);
			var editor = Session(read);
			Assert.That(editor.CanEdit, Is.True);
			Assert.That(editor.Rows.Select(x => x.FolderName), Is.EqualTo(new[]
			{
				"modB", "modA", "modManual", "modUnconfigured"
			}));
			Assert.That(editor.ConfiguredCount, Is.EqualTo(3));
			Assert.That(editor.UnconfiguredCount, Is.EqualTo(1));
			Assert.That(editor.Rows.Last().DraftPosition, Is.Null);
			Assert.That(editor.Move("modUnconfigured", -1), Is.False);
			Assert.That(editor.SetGameEnabled("modUnconfigured", false), Is.False);
			Assert.That(editor.IsDirty, Is.False);
		}

		[Test]
		public void ReorderingManualComponent_PreservesVkAndOtherSections_WithoutTouchingDisk()
		{
			var read = Observe(ThreeSections);
			var editor = Session(read);
			byte[] before = File.ReadAllBytes(_settings);
			Assert.That(editor.Move("modManual", -1), Is.True);
			Assert.That(editor.Move("modManual", -1), Is.True);
			Assert.That(editor.SetGameEnabled("modManual", false), Is.True);
			Assert.That(editor.IsDirty, Is.True);
			var preview = editor.PrepareChange();
			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			Assert.That(preview.Changes.Select(x => x.FolderName), Is.EquivalentTo(new[] { "modManual", "modA", "modB" }));
			Assert.That(preview.ProposedText, Does.Contain("[modManual]\r\nPriority=1\r\nVK=external\r\nEnabled=0"));
			Assert.That(preview.ProposedText, Does.Contain("[SomeOtherTool]\r\nKeep=unchanged"));
			Assert.That(preview.ProposedText, Does.Not.Contain("[modUnconfigured]"));
			Assert.That(File.ReadAllBytes(_settings), Is.EqualTo(before));
		}

		[Test]
		public void ResetDraftRestoresOrderingAndUnknownEnabledInsteadOfWriting()
		{
			var editor = Session(Observe(ThreeSections));
			Assert.That(editor.Move("modManual", -1), Is.True);
			Assert.That(editor.SetGameEnabled("modManual", true), Is.True);
			editor.ResetDraft();
			Assert.That(editor.IsDirty, Is.False);
			Assert.That(editor.Find("modManual").DraftEnabled, Is.Null);
			Assert.That(editor.Rows.Select(x => x.FolderName), Is.EqualTo(new[]
			{
				"modB", "modA", "modManual", "modUnconfigured"
			}));
			Assert.That(File.ReadAllText(_settings), Is.EqualTo(ThreeSections));
		}

		[Test]
		public void MissingFileIsDefaultReadOnly_NoSettingsCreated()
		{
			var read = Witcher3ModSettingsFileReader.Read(_settings);
			var editor = Session(read);
			Assert.That(editor.CanEdit, Is.False);
			Assert.That(editor.ConfiguredCount, Is.Zero);
			Assert.That(editor.UnconfiguredCount, Is.EqualTo(4));
			Assert.That(editor.Move("modA", 1), Is.False);
			Assert.That(editor.SetGameEnabled("modB", false), Is.False);
			Assert.That(editor.IsDirty, Is.False);
			Assert.That(File.Exists(_settings), Is.False);
		}

		[TestCase("Witcher3Remastered", Witcher3Edition.Ambiguous, false)]
		[TestCase("Witcher3Remastered", Witcher3Edition.Legacy, false)]
		[TestCase("Witcher3", Witcher3Edition.Remastered, false)]
		[TestCase("Witcher3", Witcher3Edition.Ambiguous, false)]
		[TestCase("Witcher3", Witcher3Edition.Legacy, true)]
		public void UnknownEditionAndPendingRecovery_DisableAllWrites(string mode,
			Witcher3Edition edition, bool pending)
		{
			var editor = Session(Observe(ThreeSections), mode, edition, pending);
			Assert.That(editor.CanEdit, Is.False);
			Assert.That(editor.Move("modA", -1), Is.False);
			Assert.That(editor.SetGameEnabled("modA", false), Is.False);
			Assert.That(editor.IsDirty, Is.False);
		}

		[Test]
		public void ExternalEditInvalidatesReadSnapshot_NoFallbackToTimestamp()
		{
			var editor = Session(Observe(ThreeSections));
			Assert.That(editor.MatchesObservedFile(Witcher3ModSettingsFileReader.Read(_settings)), Is.True);
			File.AppendAllText(_settings, "; edited externally\r\n");
			Assert.That(editor.MatchesObservedFile(Witcher3ModSettingsFileReader.Read(_settings)), Is.False);
		}

		[Test]
		public void AmbiguousSettingsAreVisibleButNeverEditable()
		{
			var read = Observe("[modA]\r\nPriority=1\r\n[modA]\r\nPriority=2\r\n");
			var editor = Session(read);
			Assert.That(editor.CanEdit, Is.False);
			Assert.That(editor.Move("modA", -1), Is.False);
		}

		[Test]
		public void RemasteredViewIncludesCurrentDx12AndLocalModHealthWithoutWritingFiles()
		{
			Observe(ThreeSections);
			string executable = Path.Combine(_game, "bin", "x64_dx12", "witcher3.exe");
			Directory.CreateDirectory(Path.GetDirectoryName(executable));
			File.WriteAllBytes(executable, new byte[] { 0x4D, 0x5A });
			string dx12Settings = Path.Combine(_documents, "The Witcher 3", "dx12user.settings");
			File.WriteAllText(dx12Settings, "[ContentManager/Mods]\r\nEnabled=1\r\nEnabledLocal=0\r\n");
			var lifecycle = new Witcher3ModSettingsLifecycleService("Witcher3Remastered", _documents,
				() => _game, _ => Witcher3Edition.Remastered,
				() => { throw new AssertionException("Health inspection must not acquire native mutation authority."); },
				CollectionTargetMutationLeaseManager.Shared,
				(lease, authority, paths) => { throw new AssertionException("Read-only health must not reload ownership."); },
				(preview, docs, edition, authority, lease, approved) =>
					{ throw new AssertionException("Read-only health must not publish settings."); });
			var view = new Witcher3LoadOrderViewService(lifecycle, () => _game,
				() => NativeCapture(),
				root => new Witcher3ComponentDiskObservation(root, new string[0],
					new[] { @"Mods\modA", @"Mods\modB" }, true));
			var result = view.Read();
			Assert.That(result.RemasteredHealth, Is.Not.Null);
			Assert.That(result.RemasteredComponents, Is.Not.Null);
			Assert.That(result.RemasteredComponents.ModFolderCount, Is.GreaterThanOrEqualTo(2));
			Assert.That(result.RemasteredHealth.Dx12ExecutableObserved, Is.True);
			Assert.That(result.RemasteredHealth.SettingsStatus, Is.EqualTo(Witcher3RemasteredSettingsReadStatus.Loaded));
			Assert.That(result.RemasteredHealth.LocalMods, Is.EqualTo(Witcher3RemasteredModSwitchState.Disabled));
			Assert.That(result.Editor.CanEdit, Is.True);
			Assert.That(result.Editor.IsDirty, Is.False);
			Assert.That(File.ReadAllText(dx12Settings), Does.Contain("EnabledLocal=0"));
		}

		[Test]
		public void ViewServiceCapturesOncePerRefresh_WithoutCallingNativeMutation()
		{
			Observe(ThreeSections);
			int reads = 0;
			var lifecycle = new Witcher3ModSettingsLifecycleService("Witcher3", _documents,
				() => _game, _ => Witcher3Edition.Legacy,
				() => { throw new AssertionException("The read view cannot acquire native mutation authority."); },
				CollectionTargetMutationLeaseManager.Shared,
				(lease, authority, paths) => { throw new AssertionException("No validation reload for a read-only grid."); },
				(preview, docs, edition, authority, lease, approved) =>
					{ throw new AssertionException("The view must not publish game settings."); });
			var view = new Witcher3LoadOrderViewService(lifecycle, () => _game,
				() => { reads++; return NativeCapture(); },
				root => new Witcher3ComponentDiskObservation(root, new string[0],
					new[] { @"Mods\modA", @"Mods\modB", @"Mods\modManual" }, true));
			Witcher3LoadOrderViewSnapshot first = view.Read();
			Assert.That(first.Editor.CanEdit, Is.True);
			Assert.That(first.RemasteredHealth, Is.Null);
			Assert.That(first.RemasteredComponents, Is.Null);
			Assert.That(first.Editor.Rows.Select(x => x.FolderName), Is.EqualTo(new[] { "modB", "modA", "modManual" }));
			Assert.That(reads, Is.EqualTo(1));
			File.AppendAllText(_settings, "; externally changed\n");
			Witcher3LoadOrderViewSnapshot second = view.Read();
			Assert.That(reads, Is.EqualTo(2));
			Assert.That(second.Editor.MatchesObservedFile(first.Lifecycle.Settings), Is.False);
		}
	}
}
