using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3ModSettingsChangePlannerTests
	{
		private string _directory;
		private string _settingsPath;

		[SetUp]
		public void SetUp()
		{
			_directory = Path.Combine(Path.GetTempPath(), "nmm-w3r-change-preview-" + Guid.NewGuid().ToString("N"));
			_settingsPath = Witcher3ModSettingsFileReader.GetFilePath(_directory);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
		}

		private Witcher3ModSettingsFileReadResult Observe(string text, Encoding encoding = null)
		{
			encoding = encoding ?? new UTF8Encoding(false, true);
			Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
			File.WriteAllBytes(_settingsPath, encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray());
			return Witcher3ModSettingsFileReader.Read(_settingsPath);
		}

		private static Witcher3ModSettingsChangePreview Preview(Witcher3ModSettingsFileReadResult read,
			params Witcher3ModSettingsOrderEntry[] entries)
		{
			return Witcher3ModSettingsChangePlanner.Prepare(read, Witcher3Edition.Legacy, entries, true);
		}

		[Test]
		public void MixedNewlinesAndVortexKeys_OnlyRequestedValuesChange_WithoutFileWrites()
		{
			string original = "; unchanged preamble\r\n[modSword] ; third party\r\nPriority =  11 \t\r\n"
				+ "Enabled=1\r\nVK=private-vortex-link\r\nOtherKey=a=b\n\n"
				+ "[OtherTool]\r\nPriority=11\r\nKeep=exact\r\n"
				+ "[modArmor]\nPriority=22\nEnabled=0";
			var read = Observe(original);
			byte[] before = File.ReadAllBytes(_settingsPath);

			var preview = Preview(read, new Witcher3ModSettingsOrderEntry("modArmor", true),
				new Witcher3ModSettingsOrderEntry("MODSWORD"));

			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			Assert.That(preview.AuthorizesFileWrite, Is.False);
			Assert.That(preview.ProposedText, Does.Contain("Priority =  2 \t\r\nEnabled=1\r\nVK=private-vortex-link"));
			Assert.That(preview.ProposedText, Does.Contain("[OtherTool]\r\nPriority=11\r\nKeep=exact\r\n"));
			Assert.That(preview.ProposedText, Does.Contain("[modArmor]\nPriority=1\nEnabled=1"));
			Assert.That(preview.Changes.Select(x => x.FolderName), Is.EquivalentTo(new[] { "modArmor", "modSword" }));
			Assert.That(File.ReadAllBytes(_settingsPath), Is.EqualTo(before));
			Assert.That(Witcher3ModSettingsParser.Parse(preview.ProposedText).IsUnambiguous, Is.True);
		}

		[Test]
		public void MissingPriorityAndExplicitEnabled_AreInsertedWithoutNormalizingOtherLines()
		{
			var read = Observe("[modFirst]\nEnabled=0\n[modSecond]\r\nCustom=keep\r\n");
			var preview = Preview(read, new Witcher3ModSettingsOrderEntry("modSecond"),
				new Witcher3ModSettingsOrderEntry("modFirst", true));

			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			Assert.That(preview.ProposedText, Is.EqualTo(
				"[modFirst]\nEnabled=1\nPriority=2\n[modSecond]\r\nCustom=keep\r\nPriority=1\r\n"));
			var parsed = Witcher3ModSettingsParser.Parse(preview.ProposedText);
			Assert.That(parsed.ModSections.Select(x => x.Priority), Is.EqualTo(new int?[] { 2, 1 }));
			Assert.That(parsed.ModSections.Last().Enabled, Is.Null);
		}

		[Test]
		public void MissingPriorityAtEndOfFileWithoutNewline_KeepsTheNoFinalNewlineConvention()
		{
			var read = Observe("[modLast]");
			var preview = Preview(read, new Witcher3ModSettingsOrderEntry("modLast", false));
			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.PreviewAvailable));
			Assert.That(preview.ProposedText, Is.EqualTo("[modLast]\r\nPriority=1\r\nEnabled=0"));
			Assert.That(File.ReadAllText(_settingsPath), Is.EqualTo("[modLast]"));
		}

		[Test]
		public void AlreadyMatchingSemanticOrder_LeavesNonCanonicalNumericFormattingUntouched()
		{
			string original = "[modFirst]\r\nPriority = +001\r\nEnabled=1\r\n"
				+ "[modSecond]\r\nPriority=0002\r\n";
			var read = Observe(original);
			var preview = Preview(read, new Witcher3ModSettingsOrderEntry("modFirst"),
				new Witcher3ModSettingsOrderEntry("modSecond"));

			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Unchanged));
			Assert.That(preview.Changes, Is.Empty);
			Assert.That(preview.ProposedText, Is.EqualTo(original));
			Assert.That(preview.ProposedSha256, Is.EqualTo(read.ContentSha256));
		}

		[TestCase("utf8bom")]
		[TestCase("utf16le")]
		[TestCase("utf16be")]
		public void ObservedEncodingAndBom_AreRetainedInProposedByteIdentity(string format)
		{
			Encoding encoding = format == "utf8bom" ? (Encoding)new UTF8Encoding(true, true)
				: new UnicodeEncoding(format == "utf16be", true, true);
			var read = Observe("[modCafe]\r\nPriority=5\r\nEnabled=1\r\n", encoding);
			var preview = Preview(read, new Witcher3ModSettingsOrderEntry("modCafe", false));
			byte[] expected = encoding.GetPreamble().Concat(encoding.GetBytes(preview.ProposedText)).ToArray();
			using (var sha = SHA256.Create())
				Assert.That(preview.ProposedSha256, Is.EqualTo(
					BitConverter.ToString(sha.ComputeHash(expected)).Replace("-", String.Empty)));
			Assert.That(preview.TextEncoding, Is.EqualTo(read.Encoding));
			Assert.That(preview.ProposedByteLength, Is.EqualTo(expected.Length));
			Assert.That(preview.ProposedText, Does.Contain("Enabled=0"));
		}

		[Test]
		public void MultipleConfiguredFolders_MustBeExplicitlyIncludedEvenIfExternallyManaged()
		{
			var read = Observe("[modNmm]\nPriority=1\n[modManual]\nPriority=2\n");
			var preview = Preview(read, new Witcher3ModSettingsOrderEntry("modNmm"));
			Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			Assert.That(preview.ProposedText, Is.Null);
			Assert.That(preview.Changes, Is.Empty);
		}

		[Test]
		public void DuplicateOrInventedRequestedComponent_IsNeverMappedToExistingSection()
		{
			var read = Observe("[modFirst]\nPriority=1\n[modSecond]\nPriority=2\n");
			Assert.That(Preview(read, new Witcher3ModSettingsOrderEntry("modFirst"),
				new Witcher3ModSettingsOrderEntry("MODFIRST")).Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			Assert.That(Preview(read, new Witcher3ModSettingsOrderEntry("modFirst"),
				new Witcher3ModSettingsOrderEntry("modInvented")).Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			Assert.That(Preview(read, new Witcher3ModSettingsOrderEntry("modFirst"),
				new Witcher3ModSettingsOrderEntry("mod/escape")).Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
		}

		[Test]
		public void AmbiguousCurrentConfiguration_MustBeFixedSeparatelyRatherThanPartiallyRewritten()
		{
			var read = Observe("[modFirst]\nPriority=1\n[modSecond]\nPriority=1\n");
			Assert.That(read.Document.IsUnambiguous, Is.False);
			Assert.That(Preview(read, new Witcher3ModSettingsOrderEntry("modSecond"),
				new Witcher3ModSettingsOrderEntry("modFirst")).Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
		}

		[Test]
		public void MissingFileOrNoExplicitConsent_CannotCreateSettings()
		{
			var missing = Witcher3ModSettingsFileReader.Read(_settingsPath);
			Assert.That(Preview(missing, new Witcher3ModSettingsOrderEntry("modFirst")).Status,
				Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			Assert.That(Directory.Exists(_directory), Is.False);
			var loaded = Observe("[modFirst]\nPriority=1\n");
			var noConsent = Witcher3ModSettingsChangePlanner.Prepare(loaded,
				Witcher3Edition.Legacy, new[] { new Witcher3ModSettingsOrderEntry("modFirst") }, false);
			Assert.That(noConsent.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			Assert.That(noConsent.AuthorizesFileWrite, Is.False);
		}

		[Test]
		public void RemasteredAndUnknownEdition_DoNotBorrowLegacyOrderSemantics()
		{
			var loaded = Observe("[modFirst]\nPriority=1\n");
			foreach (var edition in new[] { Witcher3Edition.Remastered, Witcher3Edition.Ambiguous })
			{
				var preview = Witcher3ModSettingsChangePlanner.Prepare(loaded, edition,
					new[] { new Witcher3ModSettingsOrderEntry("modFirst") }, true);
				Assert.That(preview.Status, Is.EqualTo(Witcher3ModSettingsChangePlanStatus.Blocked));
			}
		}
	}
}
