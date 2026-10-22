using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3ModSettingsPublisherTests
	{
		private string _documents;
		private string _file;
		private string _gameRoot;
		private CollectionTargetAuthority _authority;

		[SetUp]
		public void SetUp()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-w3r-publish-" + Guid.NewGuid().ToString("N"));
			_documents = Path.Combine(root, "Documents");
			_gameRoot = Path.Combine(root, "Game");
			_file = Witcher3ModSettingsFileReader.GetFilePath(_documents);
			Directory.CreateDirectory(_gameRoot);
			string physicalKey = "w3r-publication-fixture-" + Guid.NewGuid().ToString("N");
			string storage = "storage-" + Guid.NewGuid().ToString("N");
			_authority = new CollectionTargetAuthority(
				CollectionTargetIdentity.FromCanonicalAuthority(physicalKey, storage),
				"Witcher3", _gameRoot, physicalKey, storage);
		}

		[TearDown]
		public void TearDown()
		{
			string root = Directory.GetParent(_documents).FullName;
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}

		private CollectionTargetMutationLease Lease(bool validated = true)
		{
			var lease = CollectionTargetMutationLeaseManager.Shared.Acquire(_authority);
			if (validated) lease.MarkNativeAuthorityValidated();
			return lease;
		}

		private Witcher3ModSettingsChangePreview Preview(Encoding encoding = null)
		{
			encoding = encoding ?? new UTF8Encoding(false, true);
			Directory.CreateDirectory(Path.GetDirectoryName(_file));
			File.WriteAllBytes(_file, encoding.GetPreamble().Concat(encoding.GetBytes(
				"; keep\r\n[modFirst]\r\nPriority=1\r\nVK=foreign\r\n[modSecond]\nPriority=2\nEnabled=0\n")).ToArray());
			return Witcher3ModSettingsChangePlanner.Prepare(Witcher3ModSettingsFileReader.Read(_file),
				Witcher3Edition.Legacy, new[] { new Witcher3ModSettingsOrderEntry("modSecond", true),
					new Witcher3ModSettingsOrderEntry("modFirst") }, true);
		}

		private Witcher3ModSettingsPublishResult Publish(Witcher3ModSettingsChangePreview preview,
			CollectionTargetMutationLease lease, Action<Witcher3ModSettingsPublicationStep> checkpoint = null,
			Witcher3Edition expected = Witcher3Edition.Legacy,
			Func<string, Witcher3Edition> live = null, bool approved = true)
		{
			return Witcher3ModSettingsPublisher.PublishCore(preview, _documents, expected, _authority,
				lease, approved, live ?? (_ => Witcher3Edition.Legacy), checkpoint);
		}

		[Test]
		public void VerifiedPublication_RetainsExactBackupAndDoesNotCreateASecondInstallOwner()
		{
			var preview = Preview();
			byte[] before = File.ReadAllBytes(_file);
			using (var lease = Lease())
			{
				var result = Publish(preview, lease);
				Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.Published));
				Assert.That(result.BackupPath, Is.Not.Null);
				Assert.That(File.ReadAllBytes(result.BackupPath), Is.EqualTo(before));
				Assert.That(File.ReadAllText(_file), Is.EqualTo(preview.ProposedText));
				Assert.That(Witcher3ModSettingsPublisher.InspectRecovery(_documents).Status,
					Is.EqualTo(Witcher3ModSettingsRecoveryStatus.None));
				Assert.That(Publish(preview, lease).Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.StaleReview));
			}
		}

		[TestCase("utf8bom")]
		[TestCase("utf16le")]
		[TestCase("utf16be")]
		public void PublishedBytesMatchTheReviewedHashAndEncoding(string format)
		{
			Encoding encoding = format == "utf8bom" ? (Encoding)new UTF8Encoding(true, true)
				: new UnicodeEncoding(format == "utf16be", true, true);
			var preview = Preview(encoding);
			byte[] before = File.ReadAllBytes(_file);
			using (var lease = Lease())
			{
				var result = Publish(preview, lease);
				Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.Published));
				Assert.That(File.ReadAllBytes(_file), Is.EqualTo(
					encoding.GetPreamble().Concat(encoding.GetBytes(preview.ProposedText)).ToArray()));
				Assert.That(File.ReadAllBytes(result.BackupPath), Is.EqualTo(before));
				Assert.That(Witcher3ModSettingsFileReader.Read(_file).ContentSha256,
					Is.EqualTo(preview.ProposedSha256));
			}
		}

		[Test]
		public void ChangedBeforePublication_BlocksWithoutCreatingMarkerOrBackup()
		{
			var preview = Preview();
			File.AppendAllText(_file, "; third party edit\n");
			byte[] outside = File.ReadAllBytes(_file);
			using (var lease = Lease())
			{
				Assert.That(Publish(preview, lease).Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.StaleReview));
				Assert.That(Witcher3ModSettingsPublisher.InspectRecovery(_documents).Status,
					Is.EqualTo(Witcher3ModSettingsRecoveryStatus.None));
				Assert.That(File.ReadAllBytes(_file), Is.EqualTo(outside));
				Assert.That(Directory.GetFiles(Path.GetDirectoryName(_file), "*.backup"), Is.Empty);
			}
		}

		[TestCase((int)Witcher3ModSettingsPublicationStep.AfterMarker)]
		[TestCase((int)Witcher3ModSettingsPublicationStep.AfterBackup)]
		[TestCase((int)Witcher3ModSettingsPublicationStep.AfterTemporary)]
		public void InterruptedBeforeReplacement_RemainsRecoverableWithoutTouchingOriginal(int interruptedStep)
		{
			var interrupted = (Witcher3ModSettingsPublicationStep)interruptedStep;
			var preview = Preview();
			byte[] before = File.ReadAllBytes(_file);
			using (var lease = Lease())
			{
				var outcome = Publish(preview, lease, step =>
				{
					if (step == interrupted) throw new InvalidOperationException("simulated interruption");
				});
				Assert.That(outcome.Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.RecoveryRequired));
				Assert.That(File.ReadAllBytes(_file), Is.EqualTo(before));
				Assert.That(Witcher3ModSettingsPublisher.InspectRecovery(_documents).Status,
					Is.EqualTo(Witcher3ModSettingsRecoveryStatus.OriginalIntact));
				Assert.That(Publish(preview, lease).Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.RecoveryRequired));
				Assert.That(Witcher3ModSettingsPublisher.AcknowledgeRecovery(_documents, _authority, lease).Status,
					Is.EqualTo(Witcher3ModSettingsRecoveryStatus.None));
				Assert.That(Witcher3ModSettingsPublisher.InspectRecovery(_documents).Status,
					Is.EqualTo(Witcher3ModSettingsRecoveryStatus.None));
			}
		}

		[Test]
		public void InterruptedAfterReplacement_RecognizesCommittedPostimageAndKeepsBackup()
		{
			var preview = Preview();
			byte[] before = File.ReadAllBytes(_file);
			using (var lease = Lease())
			{
				var outcome = Publish(preview, lease, step =>
				{
					if (step == Witcher3ModSettingsPublicationStep.AfterReplacement)
						throw new InvalidOperationException("simulated crash after atomic replace");
				});
				Assert.That(outcome.Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.RecoveryRequired));
				var pending = Witcher3ModSettingsPublisher.InspectRecovery(_documents);
				Assert.That(pending.Status, Is.EqualTo(Witcher3ModSettingsRecoveryStatus.PublishedIntact));
				Assert.That(File.ReadAllBytes(pending.BackupPath), Is.EqualTo(before));
				Assert.That(Witcher3ModSettingsPublisher.AcknowledgeRecovery(_documents, _authority, lease).Status,
					Is.EqualTo(Witcher3ModSettingsRecoveryStatus.None));
				Assert.That(File.ReadAllBytes(_file), Is.EqualTo(
					new UTF8Encoding(false, true).GetBytes(preview.ProposedText)));
				Assert.That(File.ReadAllBytes(pending.BackupPath), Is.EqualTo(before));
			}
		}

		[Test]
		public void OutsideWriterBetweenBackupAndReplace_IsDetectedAndNeverOverwritten()
		{
			var preview = Preview();
			byte[] foreign = Encoding.UTF8.GetBytes("[modFirst]\nPriority=99\n; external editor\n");
			using (var lease = Lease())
			{
				var outcome = Publish(preview, lease, step =>
				{
					if (step == Witcher3ModSettingsPublicationStep.BeforeFinalPreimageCheck)
						File.WriteAllBytes(_file, foreign);
				});
				Assert.That(outcome.Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.RecoveryRequired));
				Assert.That(File.ReadAllBytes(_file), Is.EqualTo(foreign));
				Assert.That(Witcher3ModSettingsPublisher.InspectRecovery(_documents).Status,
					Is.EqualTo(Witcher3ModSettingsRecoveryStatus.Conflict));
				Assert.That(Witcher3ModSettingsPublisher.AcknowledgeRecovery(_documents, _authority, lease).Status,
					Is.EqualTo(Witcher3ModSettingsRecoveryStatus.Conflict));
				Assert.That(Publish(preview, lease).Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.RecoveryRequired));
			}
		}

		[Test]
		public void MissingBackupAfterSwappedFile_NeverProvesRecoverableCommit()
		{
			var preview = Preview();
			using (var lease = Lease())
			{
				Publish(preview, lease, step =>
				{
					if (step == Witcher3ModSettingsPublicationStep.AfterReplacement)
						throw new InvalidOperationException("interrupted");
				});
				var pending = Witcher3ModSettingsPublisher.InspectRecovery(_documents);
				Assert.That(pending.Status, Is.EqualTo(Witcher3ModSettingsRecoveryStatus.PublishedIntact));
				File.Delete(pending.BackupPath);
				Assert.That(Witcher3ModSettingsPublisher.InspectRecovery(_documents).Status,
					Is.EqualTo(Witcher3ModSettingsRecoveryStatus.Conflict));
			}
		}

		[Test]
		public void CorruptMarker_DoesNotAuthorizeCleanupOrReplacement()
		{
			var preview = Preview();
			using (var lease = Lease())
			{
				File.WriteAllText(_file + ".nmm-w3r.pending", "not a valid record");
				Assert.That(Witcher3ModSettingsPublisher.InspectRecovery(_documents).Status,
					Is.EqualTo(Witcher3ModSettingsRecoveryStatus.InvalidMarker));
				Assert.That(Publish(preview, lease).Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.RecoveryRequired));
				Assert.That(Witcher3ModSettingsPublisher.AcknowledgeRecovery(_documents, _authority, lease).Status,
					Is.EqualTo(Witcher3ModSettingsRecoveryStatus.InvalidMarker));
			}
		}

		[Test]
		public void ApprovalEditionAndLeaseGates_PreventDiskMutation()
		{
			var preview = Preview();
			byte[] original = File.ReadAllBytes(_file);
			using (var lease = Lease())
			{
				Assert.That(Publish(preview, lease, approved: false).Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.Blocked));
				Assert.Throws<InvalidOperationException>(() => Publish(preview, lease, expected: Witcher3Edition.Remastered));
				Assert.That(Publish(preview, lease, live: _ => Witcher3Edition.Remastered).Status,
					Is.EqualTo(Witcher3ModSettingsPublishStatus.Blocked));
				Assert.That(File.ReadAllBytes(_file), Is.EqualTo(original));
			}
			using (var lease = Lease(validated: false))
				Assert.Throws<InvalidOperationException>(() => Publish(preview, lease));
			Assert.That(Witcher3ModSettingsPublisher.InspectRecovery(_documents).Status,
				Is.EqualTo(Witcher3ModSettingsRecoveryStatus.None));
		}

		[Test]
		public void LiveExecutableProbe_BlocksAnUnknownGameVersion()
		{
			var preview = Preview();
			using (var lease = Lease())
			{
				// The fixture's game root contains no executable: production's live probe must block.
				Assert.That(Witcher3ModSettingsPublisher.Publish(preview, _documents,
					Witcher3Edition.Legacy, _authority, lease, true).Status,
					Is.EqualTo(Witcher3ModSettingsPublishStatus.Blocked));
					Assert.That(Witcher3ModSettingsPublisher.InspectRecovery(_documents).Status,
						Is.EqualTo(Witcher3ModSettingsRecoveryStatus.None));
			}
		}

		[Test]
		public void DifferentDocumentsLocation_NeverPublishesTheReviewedFile()
		{
			var preview = Preview();
			using (var lease = Lease())
			{
				var result = Witcher3ModSettingsPublisher.PublishCore(preview,
					Path.Combine(_documents, "other"), Witcher3Edition.Legacy,
					_authority, lease, true, _ => Witcher3Edition.Legacy, null);
				Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsPublishStatus.Blocked));
			}
		}
	}
}
