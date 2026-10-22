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
	public class Witcher3ModSettingsLifecycleTests
	{
		private string _root;
		private string _documents;
		private string _gameRoot;
		private string _file;
		private CollectionTargetAuthority _authority;
		private Witcher3Edition _observedEdition;
		private int _targetResolves;
		private int _validations;
		private bool _rejectNativeValidation;
		private Action<Witcher3ModSettingsPublicationStep> _checkpoint;

		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-w3r-lifecycle-" + Guid.NewGuid().ToString("N"));
			_documents = Path.Combine(_root, "Documents");
			_gameRoot = Path.Combine(_root, "Game");
			_file = Witcher3ModSettingsFileReader.GetFilePath(_documents);
			Directory.CreateDirectory(_gameRoot);
			var physical = "w3r-lifecycle-" + Guid.NewGuid().ToString("N");
			var storage = "storage-" + Guid.NewGuid().ToString("N");
			_authority = new CollectionTargetAuthority(
				CollectionTargetIdentity.FromCanonicalAuthority(physical, storage),
				"Witcher3", _gameRoot, physical, storage);
			_observedEdition = Witcher3Edition.Legacy;
			_targetResolves = 0;
			_validations = 0;
			_rejectNativeValidation = false;
			_checkpoint = null;
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_root)) Directory.Delete(_root, true);
		}

		private Witcher3ModSettingsLifecycleService Service(string modeId = "Witcher3")
		{
			return new Witcher3ModSettingsLifecycleService(modeId, _documents,
				() => _gameRoot, _ => _observedEdition,
				() =>
				{
					_targetResolves++;
					return Tuple.Create(_authority, new GameStoragePathSet { GameId = modeId,
						GameInstallPath = _gameRoot });
				},
				CollectionTargetMutationLeaseManager.Shared,
				(lease, authority, paths) =>
				{
					_validations++;
					Assert.That(authority, Is.SameAs(_authority));
					Assert.That(paths.GameId, Is.EqualTo(modeId));
					Assert.That(lease.HasCrossProcessReservation, Is.True);
					Assert.That(lease.RequiresRecovery, Is.False);
					if (_rejectNativeValidation)
						throw new InvalidOperationException("simulated native reload failure");
					lease.MarkNativeAuthorityValidated();
				},
				(preview, docs, edition, authority, lease, approved) =>
					Witcher3ModSettingsPublisher.PublishCore(preview, docs, edition,
						authority, lease, approved, _ => _observedEdition, _checkpoint));
		}

		private Witcher3ModSettingsChangePreview Preview()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_file));
			File.WriteAllText(_file, "[modFirst]\r\nEnabled=1\r\nPriority=1\r\n[modSecond]\r\nEnabled=0\r\nPriority=2\r\n",
				new UTF8Encoding(false, true));
			return Witcher3ModSettingsChangePlanner.Prepare(Witcher3ModSettingsFileReader.Read(_file),
				Witcher3Edition.Legacy, new[] { new Witcher3ModSettingsOrderEntry("modSecond", true),
					new Witcher3ModSettingsOrderEntry("modFirst") }, true);
		}

		[TestCase(false)]
		[TestCase(true)]
		public void Cleanup_RechecksRestoredFoldersBeforePublication_AndRetainsOriginalBackup(bool restoreFolder)
		{
			Preview();
			var read = Witcher3ModSettingsFileReader.Read(_file);
			byte[] before = File.ReadAllBytes(_file);
			var preview = Witcher3ModSettingsChangePlanner.PrepareRemoveMissingEntries(read, Witcher3Edition.Legacy,
				Witcher3ComponentDiskScan.Scan(_gameRoot));
			if (restoreFolder)
			{
				string folder = Path.Combine(_gameRoot, "Mods", "modFirst", "content");
				Directory.CreateDirectory(folder);
				File.WriteAllText(Path.Combine(folder, "restored.bundle"), "restored");
			}
			var lifecycle = Service();
			Assert.That(lifecycle.Publish(preview, false, _gameRoot).Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.Blocked));
			var result = lifecycle.Publish(preview, true, _gameRoot);
			Assert.That(_validations, Is.EqualTo(1));
			Assert.That(result.Status, Is.EqualTo(restoreFolder ? Witcher3ModSettingsPublishStatus.StaleReview : Witcher3ModSettingsPublishStatus.Published));
			if (restoreFolder)
				Assert.That(File.ReadAllBytes(_file), Is.EqualTo(before));
			else
			{
				Assert.That(File.ReadAllText(_file), Is.Empty);
				Assert.That(File.ReadAllBytes(result.BackupPath), Is.EqualTo(before));
				Assert.That(File.Exists(_file), Is.True);
			}
		}

		[Test]
		public void Cleanup_RejectsExternalSettingsEditsAfterReview()
		{
			Preview();
			var preview = Witcher3ModSettingsChangePlanner.PrepareRemoveMissingEntries(
				Witcher3ModSettingsFileReader.Read(_file), Witcher3Edition.Legacy, Witcher3ComponentDiskScan.Scan(_gameRoot));
			File.AppendAllText(_file, "; edited elsewhere\n");
			byte[] before = File.ReadAllBytes(_file);
			Assert.That(Service().Publish(preview, true, _gameRoot).Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.StaleReview));
			Assert.That(File.ReadAllBytes(_file), Is.EqualTo(before));
		}

		[Test]
		public void MissingSettings_StartReadOnlyAndNeverCreatesDocumentsOrNativeAuthority()
		{
			var lifecycle = Service();
			Assert.That(lifecycle.Current, Is.Null);
			var first = lifecycle.Refresh();
			Assert.That(first.Settings.Status, Is.EqualTo(Witcher3ModSettingsReadStatus.Missing));
			Assert.That(first.Recovery.Status, Is.EqualTo(Witcher3ModSettingsRecoveryStatus.None));
			Assert.That(first.CanPreviewLegacyManagedOrder, Is.False);
			Assert.That(lifecycle.Current, Is.SameAs(first));
			Assert.That(Directory.Exists(_documents), Is.False);
			Assert.That(_targetResolves, Is.Zero);
			Assert.That(_validations, Is.Zero);
		}

		[Test]
		public void RefreshReobservesExternalEditsAndEditionTransitionWithoutWritingAnything()
		{
			Preview();
			var lifecycle = Service();
			var first = lifecycle.Refresh();
			Assert.That(first.CanPreviewLegacyManagedOrder, Is.True);
			File.AppendAllText(_file, "; external edit\n");
			_observedEdition = Witcher3Edition.Remastered;
			var second = lifecycle.Refresh();
			Assert.That(second.Settings.ContentSha256, Is.Not.EqualTo(first.Settings.ContentSha256));
			Assert.That(second.Edition, Is.EqualTo(Witcher3Edition.Remastered));
			Assert.That(second.CanPreviewLegacyManagedOrder, Is.False);
			Assert.That(first.Edition, Is.EqualTo(Witcher3Edition.Legacy));
			Assert.That(_targetResolves, Is.Zero);
		}

		[Test]
		public void PublishExplicitLegacyApproval_ValidatesAuthorityAndRefreshesCommittedFile()
		{
			var preview = Preview();
			var lifecycle = Service();
			Assert.That(lifecycle.Refresh().Settings.ContentSha256, Is.EqualTo(preview.ObservedSha256));
			Assert.That(lifecycle.Publish(preview, false).Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.Blocked));
			Assert.That(_targetResolves, Is.Zero);
			var result = lifecycle.Publish(preview, true);
			Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.Published));
			Assert.That(_targetResolves, Is.EqualTo(1));
			Assert.That(_validations, Is.EqualTo(1));
			Assert.That(lifecycle.Current.Settings.ContentSha256, Is.EqualTo(preview.ProposedSha256));
			Assert.That(lifecycle.Current.HasPendingPublication, Is.False);
			Assert.That(File.Exists(result.BackupPath), Is.True);
		}

		[Test]
		public void PublishStaleReview_PreservesExternalBytesAndRefreshesObservation()
		{
			var preview = Preview();
			var lifecycle = Service();
			lifecycle.Refresh();
			File.AppendAllText(_file, "; editor changed settings\n");
			byte[] external = File.ReadAllBytes(_file);
			Assert.That(lifecycle.Publish(preview, true).Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.StaleReview));
			Assert.That(File.ReadAllBytes(_file), Is.EqualTo(external));
			Assert.That(lifecycle.Current.Settings.ContentSha256, Is.Not.EqualTo(preview.ObservedSha256));
			Assert.That(lifecycle.Current.HasPendingPublication, Is.False);
		}

		[Test]
		public void NativeReloadFailure_CannotPublishAndDoesNotLeaveASettingsMarker()
		{
			var preview = Preview();
			var lifecycle = Service();
			byte[] original = File.ReadAllBytes(_file);
			_rejectNativeValidation = true;
			Assert.Throws<InvalidOperationException>(() => lifecycle.Publish(preview, true));
			Assert.That(_targetResolves, Is.EqualTo(1));
			Assert.That(_validations, Is.EqualTo(1));
			Assert.That(File.ReadAllBytes(_file), Is.EqualTo(original));
			Assert.That(lifecycle.Current.HasPendingPublication, Is.False);
			Assert.That(File.Exists(_file + ".nmm-w3r.pending"), Is.False);
		}

		[Test]
		public void AResolvedDifferentGameAuthority_IsRejectedBeforeLeaseAcquisition()
		{
			var preview = Preview();
			var lifecycle = Service();
			var foreignKey = "foreign-" + Guid.NewGuid().ToString("N");
			_authority = new CollectionTargetAuthority(
				CollectionTargetIdentity.FromCanonicalAuthority(foreignKey, foreignKey),
				"Witcher3Remastered", _gameRoot, foreignKey, foreignKey);
			Assert.Throws<InvalidOperationException>(() => lifecycle.Publish(preview, true));
			Assert.That(_validations, Is.Zero);
			Assert.That(lifecycle.Current.HasPendingPublication, Is.False);
			Assert.That(Witcher3ModSettingsFileReader.Read(_file).ContentSha256,
				Is.EqualTo(preview.ObservedSha256));
		}

		[Test]
		public void RestartObservesPendingPostimageWithoutAutoAcknowledgement_ThenExplicitlyAcknowledges()
		{
			var preview = Preview();
			byte[] before = File.ReadAllBytes(_file);
			var lifecycle = Service();
			_checkpoint = step =>
			{
				if (step == Witcher3ModSettingsPublicationStep.AfterReplacement)
					throw new InvalidOperationException("injected post-commit interruption");
			};
			Assert.That(lifecycle.Publish(preview, true).Status,
				Is.EqualTo(Witcher3ModSettingsPublishStatus.RecoveryRequired));
			var restarted = Service();
			var status = restarted.Refresh();
			Assert.That(status.Recovery.Status, Is.EqualTo(Witcher3ModSettingsRecoveryStatus.PublishedIntact));
			Assert.That(status.HasPendingPublication, Is.True);
			Assert.That(status.CanPreviewLegacyManagedOrder, Is.False);
			Assert.That(restarted.Refresh().HasPendingPublication, Is.True);
			Assert.That(restarted.AcknowledgePendingPublication(false).Status,
				Is.EqualTo(Witcher3ModSettingsRecoveryStatus.PublishedIntact));
			Assert.That(restarted.Current.HasPendingPublication, Is.True);
			Assert.That(_targetResolves, Is.EqualTo(1)); // publication, never read-only startup
			var acknowledged = restarted.AcknowledgePendingPublication(true);
			Assert.That(acknowledged.Status, Is.EqualTo(Witcher3ModSettingsRecoveryStatus.None));
			Assert.That(restarted.Current.HasPendingPublication, Is.False);
			Assert.That(restarted.Current.Settings.ContentSha256, Is.EqualTo(preview.ProposedSha256));
			Assert.That(File.ReadAllBytes(acknowledged.BackupPath), Is.EqualTo(before));
			Assert.That(_targetResolves, Is.EqualTo(2));
		}

		[Test]
		public void InvalidMarker_IsNeverAutomaticallyAcknowledgedOrDeleted()
		{
			Preview();
			var marker = _file + ".nmm-w3r.pending";
			File.WriteAllText(marker, "not a supported marker");
			var lifecycle = Service();
			Assert.That(lifecycle.Refresh().Recovery.Status,
				Is.EqualTo(Witcher3ModSettingsRecoveryStatus.InvalidMarker));
			Assert.That(lifecycle.Current.CanPreviewLegacyManagedOrder, Is.False);
			Assert.That(lifecycle.AcknowledgePendingPublication(true).Status,
				Is.EqualTo(Witcher3ModSettingsRecoveryStatus.InvalidMarker));
			Assert.That(File.Exists(marker), Is.True);
		}

		[Test]
		public void RemasteredMode_RejectsLegacyNativeAuthorityWithoutWriting()
		{
			var preview = Preview();
			var lifecycle = Service("Witcher3Remastered");
			Assert.That(lifecycle.Refresh().CanPreviewLegacyManagedOrder, Is.False);
			byte[] before = File.ReadAllBytes(_file);
			Assert.Throws<InvalidOperationException>(() => lifecycle.Publish(preview, true));
			Assert.Throws<InvalidOperationException>(() => lifecycle.AcknowledgePendingPublication(true));
			Assert.That(File.ReadAllBytes(_file), Is.EqualTo(before));
			Assert.That(_targetResolves, Is.EqualTo(2));
		}

		[Test]
		public void LiveEditionGateDuringPublish_RemainsAuthoritativeOverOldPreview()
		{
			var preview = Preview();
			var lifecycle = Service();
			Assert.That(lifecycle.Refresh().CanPreviewLegacyManagedOrder, Is.True);
			_observedEdition = Witcher3Edition.Remastered;
			Assert.That(lifecycle.Publish(preview, true).Status,
				Is.EqualTo(Witcher3ModSettingsPublishStatus.Blocked));
			Assert.That(lifecycle.Current.Edition, Is.EqualTo(Witcher3Edition.Remastered));
			Assert.That(lifecycle.Current.Settings.ContentSha256, Is.EqualTo(preview.ObservedSha256));
			Assert.That(lifecycle.Current.HasPendingPublication, Is.False);
		}
	}
}
