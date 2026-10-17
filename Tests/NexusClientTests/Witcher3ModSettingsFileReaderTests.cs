using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3ModSettingsFileReaderTests
	{
		private string root;
		private string file;

		[SetUp]
		public void SetUp()
		{
			root = Path.Combine(Path.GetTempPath(), "nmm-w3r-config-" + Guid.NewGuid().ToString("N"));
			file = Witcher3ModSettingsFileReader.GetFilePath(root);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}

		private void Write(byte[] bytes)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(file));
			File.WriteAllBytes(file, bytes);
		}

		[Test]
		public void MissingFile_DoesNotCreateDirectoriesOrAssumeDisabledMods()
		{
			var result = Witcher3ModSettingsFileReader.Read(file);
			Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsReadStatus.Missing));
			Assert.That(result.Document, Is.Null);
			Assert.That(Directory.Exists(root), Is.False);
		}

		[Test]
		public void Utf8WithoutBom_RemainsUnmodifiedAndIncludesByteHash()
		{
			byte[] bytes = new UTF8Encoding(false).GetBytes("[modCafe]\r\nEnabled=1\r\nPriority=7\r\n");
			Write(bytes);
			var result = Witcher3ModSettingsFileReader.Read(file);
			Assert.That(result.IsLoaded, Is.True);
			Assert.That(result.Encoding, Is.EqualTo(Witcher3ModSettingsTextEncoding.Utf8));
			Assert.That(result.Document.ModSections.Single().Priority, Is.EqualTo(7));
			Assert.That(result.ByteLength, Is.EqualTo(bytes.Length));
			using (var hash = SHA256.Create())
				Assert.That(result.ContentSha256, Is.EqualTo(
					BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", String.Empty)));
			Assert.That(File.ReadAllBytes(file), Is.EqualTo(bytes));
		}

		[Test]
		public void Utf8Bom_PreservesEncodingIdentityAndDoesNotStripOriginalBytes()
		{
			byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF }
				.Concat(Encoding.UTF8.GetBytes("[modA]\nEnabled=0\n")).ToArray();
			Write(bytes);
			var result = Witcher3ModSettingsFileReader.Read(file);
			Assert.That(result.IsLoaded, Is.True);
			Assert.That(result.Encoding, Is.EqualTo(Witcher3ModSettingsTextEncoding.Utf8Bom));
			Assert.That(result.Document.ModSections.Single().Enabled, Is.False);
			Assert.That(File.ReadAllBytes(file), Is.EqualTo(bytes));
		}

		[TestCase(false, Witcher3ModSettingsTextEncoding.Utf16LittleEndianBom)]
		[TestCase(true, Witcher3ModSettingsTextEncoding.Utf16BigEndianBom)]
		public void Utf16WithBom_IsDecodedWithoutChangingFile(bool bigEndian, Witcher3ModSettingsTextEncoding expected)
		{
			var encoding = new UnicodeEncoding(bigEndian, true);
			byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes("[modA]\nPriority=99\n")).ToArray();
			Write(bytes);
			var result = Witcher3ModSettingsFileReader.Read(file);
			Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsReadStatus.Loaded));
			Assert.That(result.Encoding, Is.EqualTo(expected));
			Assert.That(result.Document.ModSections.Single().Priority, Is.EqualTo(99));
			Assert.That(File.ReadAllBytes(file), Is.EqualTo(bytes));
		}

		[Test]
		public void InvalidUtf8_ReturnsUninterpretableResultRatherThanReplacementCharacters()
		{
			Write(new byte[] { (byte)'[', (byte)'m', (byte)'o', (byte)'d', (byte)'A', (byte)']', 10, 0xFF });
			var result = Witcher3ModSettingsFileReader.Read(file);
			Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsReadStatus.UnsupportedEncoding));
			Assert.That(result.Document, Is.Null);
		}

		[Test]
		public void BomlessUtf16_IsRejectedInsteadOfCreatingMisleadingEntries()
		{
			Write(new UnicodeEncoding(false, false).GetBytes("[modA]\nEnabled=0\n"));
			var result = Witcher3ModSettingsFileReader.Read(file);
			Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsReadStatus.UnsupportedEncoding));
		}

		[Test]
		public void Utf32Bom_IsExplicitlyRejected()
		{
			Write(new byte[] { 0xFF, 0xFE, 0, 0, 0x5B, 0, 0, 0 });
			var result = Witcher3ModSettingsFileReader.Read(file);
			Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsReadStatus.UnsupportedEncoding));
		}

		[Test]
		public void BoundedRead_RefusesOversizedFileAndLeavesItUntouched()
		{
			byte[] bytes = Encoding.UTF8.GetBytes("[modA]\nPriority=1\n");
			Write(bytes);
			var result = Witcher3ModSettingsFileReader.Read(file, maxBytes: 8);
			Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsReadStatus.TooLarge));
			Assert.That(result.Document, Is.Null);
			Assert.That(File.ReadAllBytes(file), Is.EqualTo(bytes));
		}

		[Test]
		public void LoadedButInvalidPriority_ReturnsDocumentWithDiagnosticsNotCorrections()
		{
			Write(Encoding.UTF8.GetBytes("[modA]\nPriority=bad\n"));
			var result = Witcher3ModSettingsFileReader.Read(file);
			Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsReadStatus.Loaded));
			Assert.That(result.Document.IsUnambiguous, Is.False);
			Assert.That(result.Document.ModSections.Single().Priority, Is.Null);
		}

		[Test]
		public void FileTooManyCharacters_ReturnsParseRejectedWithoutPartiallyTrustedOrder()
		{
			Write(Encoding.UTF8.GetBytes("[modA]\nPriority=1\n" +
				new string('x', Witcher3ModSettingsParser.MaxCharacters)));
			var result = Witcher3ModSettingsFileReader.Read(file);
			Assert.That(result.Status, Is.EqualTo(Witcher3ModSettingsReadStatus.ParseRejected));
			Assert.That(result.Document.IsComplete, Is.False);
			Assert.That(result.Document.Sections, Is.Empty);
		}

		[Test]
		public void RelativePathAndZeroLimit_AreRejectedBeforeAnyAccess()
		{
			Assert.Throws<ArgumentException>(() => Witcher3ModSettingsFileReader.Read("mods.settings"));
			Assert.Throws<ArgumentOutOfRangeException>(() => Witcher3ModSettingsFileReader.Read(file, 0));
			Assert.Throws<ArgumentException>(() => Witcher3ModSettingsFileReader.GetFilePath("relative"));
		}
	}
}
