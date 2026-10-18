using System;
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
	public class Witcher3LoadOrderExtendedEditorTests
	{
		private string _root;
		private string _game;
		private string _file;

		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-w3r-6b-editor-" + Guid.NewGuid().ToString("N"));
			_game = Path.Combine(_root, "game");
			_file = Witcher3ModSettingsFileReader.GetFilePath(Path.Combine(_root, "documents"));
			Directory.CreateDirectory(_game);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_root)) Directory.Delete(_root, true);
		}

		private Witcher3ModSettingsFileReadResult Observe(string content)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_file));
			File.WriteAllText(_file, content, new UTF8Encoding(false, true));
			return Witcher3ModSettingsFileReader.Read(_file);
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
			bool complete = true, params string[] present)
		{
			return Witcher3LoadOrderProjector.Project(Witcher3NativeComponentIndexer.Build(NativeCapture(), _game,
				new Witcher3ComponentDiskObservation(_game, new string[0],
					present.Select(x => @"Mods\" + x), complete)), read, Witcher3Edition.Legacy);
		}

		private Witcher3LoadOrderEditorSession Session(Witcher3ModSettingsFileReadResult read,
			bool complete = true, params string[] present)
		{
			return new Witcher3LoadOrderEditorSession("Witcher3", Witcher3Edition.Legacy,
				false, read, Projection(read, complete, present));
		}

		private const string Existing = "[modA]\r\nPriority=1\r\n[modManual]\r\nPriority=2\r\nVK=external\r\n";

		[Test]
		public void AddAndRemoveDraftAddition_DoesNotAdoptManualOrModifyGameUntilPublication()
		{
			var editor = Session(Observe(Existing), true, "modA", "modManual", "modExternal", "modOther");
			byte[] before = File.ReadAllBytes(_file);
			Assert.That(editor.CanAddToOrder("modExternal"), Is.True);
			Assert.That(editor.AddToOrder("modExternal"), Is.True);
			Assert.That(editor.Find("modExternal").IsConfigured, Is.False);
			Assert.That(editor.Find("modExternal").IsNewInDraft, Is.True);
			Assert.That(editor.SetGameEnabled("modExternal", false), Is.True);
			Assert.That(editor.Move("modExternal", -1), Is.True);
			Assert.That(editor.IsDirty, Is.True);
			var preview = editor.PrepareChange();
			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			Assert.That(preview.ProposedText, Does.Contain("[modExternal]\r\nEnabled=0\r\nPriority=2"));
			Assert.That(preview.ProposedText, Does.Contain("[modManual]\r\nPriority=3\r\nVK=external"));
			Assert.That(preview.ProposedText, Does.Not.Contain("[modOther]"));
			Assert.That(File.ReadAllBytes(_file), Is.EqualTo(before));
			Assert.That(editor.RemoveDraftAddition("modExternal"), Is.True);
			Assert.That(editor.IsDirty, Is.False);
			Assert.That(editor.Find("modExternal").DraftPosition, Is.Null);
			Assert.That(editor.SetGameEnabled("modExternal", true), Is.False);
			Assert.That(editor.AddToOrder("modExternal"), Is.True);
			editor.ResetDraft();
			Assert.That(editor.IsDirty, Is.False);
			Assert.That(editor.Rows.Select(x => x.FolderName), Is.EqualTo(
					new[] { "modA", "modManual", "modExternal", "modOther" }));
		}

		[Test]
		public void UnsafeOrUnconfirmedInventory_CannotEnlistUnconfiguredFolder()
		{
			var read = Observe(Existing);
			var incomplete = Session(read, false, "modA", "modExternal");
			Assert.That(incomplete.CanAddToOrder("modExternal"), Is.False);
			Assert.That(incomplete.AddToOrder("modExternal"), Is.False);
			var complete = Session(read, true, "modA", "modExternal");
			Assert.That(complete.AddToOrder("modExternal"), Is.True);
			Assert.That(complete.AddedFoldersStillObserved(Projection(read, true, "modA")), Is.False);
			Assert.That(complete.AddedFoldersStillObserved(Projection(read, false, "modA", "modExternal")), Is.False);
			Assert.That(complete.AddedFoldersStillObserved(Projection(read, true, "modA", "modExternal")), Is.True);
			Assert.That(complete.RemoveDraftAddition("modA"), Is.False);
		}

		[Test]
		public void RestoreDefaultsIsExplicitSeparateReview_AndNeverAffectsTheDraftOrGameUntilPublish()
		{
			var read = Observe(Existing + "[ThirdParty]\nKeep=still here\n");
			var editor = Session(read, true, "modA", "modManual", "modExternal");
			byte[] before = File.ReadAllBytes(_file);
			Assert.That(editor.CanRestoreGameDefaults, Is.True);
			var reset = editor.PrepareRestoreGameDefaults();
			Assert.That(reset.Kind, Is.EqualTo(Witcher3ModSettingsChangeKind.RestoreGameDefaults));
			Assert.That(reset.ProposedText, Does.Contain("[ThirdParty]\nKeep=still here\n"));
			Assert.That(reset.ProposedText, Does.Contain("VK=external"));
			Assert.That(editor.AddToOrder("modExternal"), Is.True);
			Assert.That(editor.PrepareRestoreGameDefaults().Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			Assert.That(File.ReadAllBytes(_file), Is.EqualTo(before));
		}

		[Test]
		public void AbsentSettings_LeavesLegacyDefaultUnchangedAndDoesNotCreateFile()
		{
			var read = Witcher3ModSettingsFileReader.Read(_file);
			var editor = Session(read, true, "modExternal");
			Assert.That(editor.CanEdit, Is.False);
			Assert.That(editor.CanRestoreGameDefaults, Is.False);
			Assert.That(editor.AddToOrder("modExternal"), Is.False);
			Assert.That(File.Exists(_file), Is.False);
		}
	}
}
