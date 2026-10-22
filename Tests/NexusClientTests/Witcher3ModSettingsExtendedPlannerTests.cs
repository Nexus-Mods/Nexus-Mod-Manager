using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3ModSettingsExtendedPlannerTests
	{
		private string _root;
		private string _documents;
		private string _game;
		private string _settings;
		private CollectionTargetAuthority _authority;

		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-w3r-6b-planner-" + Guid.NewGuid().ToString("N"));
			_documents = Path.Combine(_root, "Documents");
			_game = Path.Combine(_root, "Game");
			_settings = Witcher3ModSettingsFileReader.GetFilePath(_documents);
			Directory.CreateDirectory(_game);
			string key = "nmm-w3r-6b-" + Guid.NewGuid().ToString("N");
			_authority = new CollectionTargetAuthority(CollectionTargetIdentity.FromCanonicalAuthority(key, key),
				"Witcher3", _game, key, key);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_root)) Directory.Delete(_root, true);
		}

		private Witcher3ModSettingsFileReadResult Observe(string content, Encoding encoding = null)
		{
			encoding = encoding ?? new UTF8Encoding(false, true);
			Directory.CreateDirectory(Path.GetDirectoryName(_settings));
			File.WriteAllBytes(_settings, encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray());
			return Witcher3ModSettingsFileReader.Read(_settings);
		}

		private Witcher3ModSettingsChangePreview Add(Witcher3ModSettingsFileReadResult read,
			Witcher3ModSettingsOrderEntry[] rows, params string[] allowed)
		{
			return Witcher3ModSettingsChangePlanner.PrepareWithNewFolders(read,
				Witcher3Edition.Legacy, rows, true, allowed);
		}

		[TestCase(Witcher3Edition.Legacy)]
		[TestCase(Witcher3Edition.Remastered)]
		public void Cleanup_RemovesMissingAndEmptySections_PreservesOtherBytesAndPriorities(Witcher3Edition edition)
		{
			string retained = "[modPresent]\nPriority=42\nEnabled=0\nVK=external\n" +
				"[Tool]\r\nPriority=9\r\nKeep=a=b\r\n[modPartial]\nPriority=42\n";
			var read = Observe("; keep header\r\n[modMissing]\r\nPriority=1\r\nVK=old\r\n" +
				retained + "[modEmpty]\r\nEnabled=1", new UnicodeEncoding(false, true, true));
			byte[] before = File.ReadAllBytes(_settings);
			var disk = new Witcher3ComponentDiskObservation(_game,
				new[] { @"Mods\MODPRESENT\content\present.bundle", @"Mods\modPartial\remaining.txt" },
				new[] { @"Mods\MODPRESENT", @"Mods\modPartial", @"Mods\modEmpty" }, true);
			var preview = Witcher3ModSettingsChangePlanner.PrepareRemoveMissingEntries(read, edition, disk);
			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			Assert.That(preview.Kind, Is.EqualTo(Witcher3ModSettingsChangeKind.RemoveMissingEntries));
			Assert.That(preview.ProposedText, Is.EqualTo("; keep header\r\n" + retained));
			Assert.That(preview.Changes.Select(change => change.FolderName), Is.EquivalentTo(new[] { "modMissing", "modEmpty" }));
			Assert.That(preview.TextEncoding, Is.EqualTo(read.Encoding));
			Assert.That(preview.ProposedByteLength, Is.EqualTo(2 + Encoding.Unicode.GetByteCount(preview.ProposedText)));
			Assert.That(File.ReadAllBytes(_settings), Is.EqualTo(before));
			var cleaned = Observe(preview.ProposedText);
			Assert.That(Witcher3ModSettingsChangePlanner.PrepareRemoveMissingEntries(cleaned, edition, disk).Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Unchanged));
		}

		[Test]
		public void Cleanup_CanRemoveEveryEntry_WithoutDeletingTheSettingsFile()
		{
			var read = Observe("[modGone]\r\nEnabled=1\r\nPriority=1\r\n");
			var preview = Witcher3ModSettingsChangePlanner.PrepareRemoveMissingEntries(read, Witcher3Edition.Remastered,
				new Witcher3ComponentDiskObservation(_game, new string[0], new string[0], true));
			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			Assert.That(preview.ProposedText, Is.Empty);
			Assert.That(File.Exists(_settings), Is.True);
		}

		[Test]
		public void Cleanup_RejectsIncompleteScanUnsupportedEditionAndAmbiguousSettings()
		{
			var read = Observe("[modGone]\nPriority=1\n");
			var incomplete = new Witcher3ComponentDiskObservation(_game, new string[0], new string[0], false);
			Assert.That(Witcher3ModSettingsChangePlanner.PrepareRemoveMissingEntries(read, Witcher3Edition.Remastered, incomplete).Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			var complete = new Witcher3ComponentDiskObservation(_game, new string[0], new string[0], true);
			Assert.That(Witcher3ModSettingsChangePlanner.PrepareRemoveMissingEntries(read, Witcher3Edition.Ambiguous, complete).Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			read = Observe("[modGone]\nEnabled=maybe\n");
			Assert.That(Witcher3ModSettingsChangePlanner.PrepareRemoveMissingEntries(read, Witcher3Edition.Remastered, complete).Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
		}

		[Test]
		public void SelectedPhysicalFoldersOnly_AreAppendedToExistingFile_WithoutChangingForeignContent()
		{
			var original = "; original\r\n[modManual]\r\nPriority=7\r\nVK=external-key\n" +
				"[ThirdParty]\nKeep=a=b\n" + "[modExisting]\r\nPriority=9";
			var read = Observe(original, new UnicodeEncoding(false, true, true));
			byte[] before = File.ReadAllBytes(_settings);
			var preview = Add(read, new[]
			{
				new Witcher3ModSettingsOrderEntry("modAddedA", false),
				new Witcher3ModSettingsOrderEntry("modManual"),
				new Witcher3ModSettingsOrderEntry("modExisting"),
				new Witcher3ModSettingsOrderEntry("modAddedB")
			}, "modAddedA", "modAddedB", "modIgnored");
			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			Assert.That(preview.Kind, Is.EqualTo(Witcher3ModSettingsChangeKind.ManagedOrder));
			Assert.That(preview.ProposedText, Does.Contain("VK=external-key\n[ThirdParty]\nKeep=a=b\n"));
			Assert.That(preview.ProposedText, Does.Contain("[modAddedA]\r\nEnabled=0\r\nPriority=1"));
			Assert.That(preview.ProposedText, Does.Contain("[modAddedB]\r\nEnabled=1\r\nPriority=4"));
			Assert.That(preview.ProposedText, Does.Not.Contain("modIgnored"));
			var parsed = Witcher3ModSettingsParser.Parse(preview.ProposedText);
			Assert.That(parsed.IsUnambiguous, Is.True);
			Assert.That(parsed.ModSections.Single(x => x.Name == "modManual").Priority, Is.EqualTo(2));
			Assert.That(parsed.ModSections.Single(x => x.Name == "modExisting").Priority, Is.EqualTo(3));
			Assert.That(preview.Changes.Select(x => x.FolderName), Is.EquivalentTo(new[]
			{
				"modAddedA", "modManual", "modExisting", "modAddedB"
			}));
			Assert.That(File.ReadAllBytes(_settings), Is.EqualTo(before));
		}

		[Test]
		public void NewFolders_RequireCompleteExistingPermutationAndExplicitAllowlist()
		{
			var read = Observe("[modA]\nPriority=1\n[modB]\nPriority=2\n");
			Assert.That(Add(read, new[] { new Witcher3ModSettingsOrderEntry("modA") }, "modA").Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			Assert.That(Add(read, new[] { new Witcher3ModSettingsOrderEntry("modA"),
				new Witcher3ModSettingsOrderEntry("modB"), new Witcher3ModSettingsOrderEntry("modC") }).Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			Assert.That(Add(read, new[] { new Witcher3ModSettingsOrderEntry("modA"),
				new Witcher3ModSettingsOrderEntry("modB"), new Witcher3ModSettingsOrderEntry("modC") },
				"modC", "MODC").Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			Assert.That(Add(read, new[] { new Witcher3ModSettingsOrderEntry("modA"),
				new Witcher3ModSettingsOrderEntry("modB"), new Witcher3ModSettingsOrderEntry("../modC") },
				"../modC").Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			Assert.That(Witcher3ModSettingsChangePlanner.Prepare(read, Witcher3Edition.Legacy,
				new[] { new Witcher3ModSettingsOrderEntry("modA"), new Witcher3ModSettingsOrderEntry("modB"),
					new Witcher3ModSettingsOrderEntry("modC") }, true).Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
		}

		[Test]
		public void ExplicitNewFolderCanBePreviewed_WithoutCreatingSettings()
		{
			var read = Observe("[Tool]\nKeep=1");
			var preview = Add(read, new[] { new Witcher3ModSettingsOrderEntry("modNew") }, "modNew");
			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			Assert.That(preview.ProposedText, Is.EqualTo("[Tool]\nKeep=1\n[modNew]\nEnabled=1\nPriority=1\n"));
			File.Delete(_settings);
			var missing = Witcher3ModSettingsFileReader.Read(_settings);
			Assert.That(Add(missing, new[] { new Witcher3ModSettingsOrderEntry("modNew") }, "modNew").Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			Assert.That(File.Exists(_settings), Is.False);
		}

		[Test]
		public void LargeExistingOrderWithMissingPriority_IsRewrittenDeterministically()
		{
			var source = new StringBuilder();
			var desired = new Witcher3ModSettingsOrderEntry[1500];
			for (int i = 0; i < desired.Length; i++)
			{
				string folder = "mod" + i.ToString("D4");
				source.Append('[').Append(folder).Append("]\n");
				desired[i] = new Witcher3ModSettingsOrderEntry(folder);
			}
			var read = Observe(source.ToString());
			var preview = Witcher3ModSettingsChangePlanner.Prepare(read, Witcher3Edition.Legacy,
				desired, true);
			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			var parsed = Witcher3ModSettingsParser.Parse(preview.ProposedText);
			Assert.That(parsed.IsUnambiguous, Is.True);
			Assert.That(parsed.ModSections.Count(), Is.EqualTo(desired.Length));
			Assert.That(parsed.ModSections.First().Priority, Is.EqualTo(1));
			Assert.That(parsed.ModSections.Last().Priority, Is.EqualTo(1500));
		}

		[Test]
		public void RestoreDefaults_RemovesOnlyKnownModOverrides_PreservesUnknownDataAndEncoding()
		{
			string original = "; preamble\r\n[modA]\r\nEnabled=0\r\nPriority=7\r\n" +
				"VK=vortex-value\r\nExtra=preserve\n[OtherTool]\nPriority=51\nEnabled=0\n" +
				"[modExternal]\r\nPriority = 9 \t\r\nEnabled=1\r\n";
			var encoding = new UnicodeEncoding(true, true, true);
			var read = Observe(original, encoding);
			byte[] before = File.ReadAllBytes(_settings);
			var preview = Witcher3ModSettingsChangePlanner.PrepareRestoreGameDefaults(read,
				Witcher3Edition.Legacy, true);
			Assert.That(preview.Kind, Is.EqualTo(Witcher3ModSettingsChangeKind.RestoreGameDefaults));
			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			Assert.That(preview.Changes.Count, Is.EqualTo(2));
			Assert.That(preview.Changes.All(x => !x.ProposedPriority.HasValue && !x.ProposedEnabled.HasValue), Is.True);
			Assert.That(preview.ProposedText, Does.Contain("[modA]\r\nVK=vortex-value\r\nExtra=preserve\n"));
			Assert.That(preview.ProposedText, Does.Contain("[OtherTool]\nPriority=51\nEnabled=0\n"));
			Assert.That(preview.ProposedText, Does.Contain("[modExternal]\r\n"));
			var parsed = Witcher3ModSettingsParser.Parse(preview.ProposedText);
			Assert.That(parsed.IsUnambiguous, Is.True);
			Assert.That(parsed.ModSections.All(x => !x.HasPriorityField && !x.HasEnabledField), Is.True);
			Assert.That(preview.ProposedByteLength, Is.EqualTo(
				encoding.GetPreamble().Length + encoding.GetByteCount(preview.ProposedText)));
			Assert.That(File.ReadAllBytes(_settings), Is.EqualTo(before));
		}

		[Test]
		public void RestoreDefaults_IsIdempotentAndBlockedForUnsupportedEditionOrAmbiguity()
		{
			var read = Observe("[modA]\nVK=external\n[Tool]\nPriority=12\n");
			Assert.That(Witcher3ModSettingsChangePlanner.PrepareRestoreGameDefaults(read,
				Witcher3Edition.Legacy, true).Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Unchanged));
			Assert.That(Witcher3ModSettingsChangePlanner.PrepareRestoreGameDefaults(read,
				Witcher3Edition.Ambiguous, true).Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			Assert.That(Witcher3ModSettingsChangePlanner.PrepareRestoreGameDefaults(read,
				Witcher3Edition.Legacy, false).Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			var malformed = Observe("[modA]\nPriority=1\n[modA]\nPriority=2\n");
			Assert.That(Witcher3ModSettingsChangePlanner.PrepareRestoreGameDefaults(malformed,
				Witcher3Edition.Legacy, true).Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
		}

		[Test]
		public void LifecyclePublish_DifferentGameRootAfterReview_RejectsBeforeFileMutation()
		{
			var read = Observe("[modA]\nPriority=2\n");
			var preview = Add(read, new[] { new Witcher3ModSettingsOrderEntry("modA"),
				new Witcher3ModSettingsOrderEntry("modB") }, "modB");
			byte[] original = File.ReadAllBytes(_settings);
			var lifecycle = new Witcher3ModSettingsLifecycleService("Witcher3", _documents,
				() => _game, _ => Witcher3Edition.Legacy,
				() => Tuple.Create(_authority, new GameStoragePathSet { GameId = "Witcher3",
					GameInstallPath = _game }),
				CollectionTargetMutationLeaseManager.Shared,
				(lease, authority, paths) => lease.MarkNativeAuthorityValidated(),
				(value, docs, edition, authority, lease, approved) =>
					Witcher3ModSettingsPublisher.PublishCore(value, docs, edition,
						authority, lease, approved, _ => Witcher3Edition.Legacy, null));
			string differentRoot = Path.Combine(_root, "ChangedInstallation");
			Directory.CreateDirectory(differentRoot);
			Assert.That(lifecycle.Publish(preview, true, differentRoot).Status,
				Is.EqualTo(Witcher3ModSettingsPublishStatus.StaleReview));
			Assert.That(File.ReadAllBytes(_settings), Is.EqualTo(original));
			Assert.That(File.Exists(_settings + ".nmm-w3r.pending"), Is.False);
			Assert.That(lifecycle.Publish(preview, true, _game).Status,
				Is.EqualTo(Witcher3ModSettingsPublishStatus.Published));
		}

		[Test]
		public void ExtendedPreviews_PublishViaExistingAtomicBackupAndRejectStaleReview()
		{
			var read = Observe("[modManual]\r\nPriority=2\r\nVK=external\r\n");
			var added = Add(read, new[] { new Witcher3ModSettingsOrderEntry("modNew"),
				new Witcher3ModSettingsOrderEntry("modManual") }, "modNew");
			byte[] before = File.ReadAllBytes(_settings);
			using (var lease = CollectionTargetMutationLeaseManager.Shared.Acquire(_authority))
			{
				lease.MarkNativeAuthorityValidated();
				var published = Witcher3ModSettingsPublisher.PublishCore(added, _documents,
					Witcher3Edition.Legacy, _authority, lease, true, _ => Witcher3Edition.Legacy, null);
				Assert.That(published.Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.Published));
				Assert.That(File.ReadAllBytes(published.BackupPath), Is.EqualTo(before));
				Assert.That(Witcher3ModSettingsFileReader.Read(_settings).ContentSha256,
					Is.EqualTo(added.ProposedSha256));
				Assert.That(Witcher3ModSettingsPublisher.PublishCore(added, _documents,
					Witcher3Edition.Legacy, _authority, lease, true, _ => Witcher3Edition.Legacy, null).Status,
					Is.EqualTo(Witcher3ModSettingsPublishStatus.StaleReview));

				var restore = Witcher3ModSettingsChangePlanner.PrepareRestoreGameDefaults(
					Witcher3ModSettingsFileReader.Read(_settings), Witcher3Edition.Legacy, true);
				Assert.That(restore.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
				var defaults = Witcher3ModSettingsPublisher.PublishCore(restore, _documents,
					Witcher3Edition.Legacy, _authority, lease, true, _ => Witcher3Edition.Legacy, null);
				Assert.That(defaults.Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.Published));
				Assert.That(Witcher3ModSettingsFileReader.Read(_settings).Document.ModSections.All(x =>
					!x.HasEnabledField && !x.HasPriorityField), Is.True);
				Assert.That(File.ReadAllBytes(defaults.BackupPath),
					Is.EqualTo(new UTF8Encoding(false, true).GetBytes(added.ProposedText)));
			}
		}
	}
}
