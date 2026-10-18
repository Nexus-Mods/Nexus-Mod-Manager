using System;
using System.IO;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3ExternalScriptMergerInspectorTests
	{
		private string _temporaryRoot;

		[SetUp]
		public void SetUp()
		{
			_temporaryRoot = Path.Combine(Path.GetTempPath(), "nmm-w3r-merger-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_temporaryRoot);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, true);
		}

		private static Witcher3ScriptMergerToolObservation Assess(string product,
			string version = "", string company = "", string description = "",
			string fileName = "WitcherScriptMerger.exe", string fileVersion = "")
		{
			return Witcher3ExternalScriptMergerInspector.Assess(fileName,
				new Witcher3ScriptMergerVersionMetadata(product, description, company,
					version, fileVersion));
		}

		[Test]
		public void GenericExecutableNameAndOriginalVersion_DoNotIdentifyClassicOrApproveRemastered()
		{
			var report = Assess("WitcherScriptMerger", "0.6.5");
			Assert.That(report.Status, Is.EqualTo(Witcher3ScriptMergerReadStatus.Observed));
			Assert.That(report.Family, Is.EqualTo(Witcher3ScriptMergerFamily.Unknown));
			Assert.That(report.Compatibility, Is.EqualTo(Witcher3ScriptMergerCompatibility.Unconfirmed));
		}

		[Test]
		public void ExplicitOriginalClassicFamily_ReceivesLegacyWarning()
		{
			var report = Assess("Witcher Script Merger (CLASSIC)", "0.6.5", "AnotherSymbiote");
			Assert.That(report.Family, Is.EqualTo(Witcher3ScriptMergerFamily.Classic));
			Assert.That(report.Compatibility, Is.EqualTo(Witcher3ScriptMergerCompatibility.LegacyReleaseWarning));
		}

		[Test]
		public void RemasteredFamilyVersionOneOrLater_OnlyClaimsSupport()
		{
			var report = Assess("Script Merger - Remastered", "1.5.1");
			Assert.That(report.Family, Is.EqualTo(Witcher3ScriptMergerFamily.Remastered));
			Assert.That(report.Compatibility, Is.EqualTo(Witcher3ScriptMergerCompatibility.RemasteredSupportClaimed));
			Assert.That(report.VersionLabel, Is.EqualTo("1.5.1"));
		}

		[Test]
		public void RemasteredLabelWithMissingOrUndocumentedVersion_IsUnconfirmed()
		{
			Assert.That(Assess("Script Merger - Remastered").Compatibility,
				Is.EqualTo(Witcher3ScriptMergerCompatibility.Unconfirmed));
			Assert.That(Assess("Script Merger - Remastered", "0.7.0").Compatibility,
				Is.EqualTo(Witcher3ScriptMergerCompatibility.Unconfirmed));
		}

		[TestCase("0.9.7", Witcher3ScriptMergerCompatibility.LegacyReleaseWarning)]
		[TestCase("v0.9.8-rc.3", Witcher3ScriptMergerCompatibility.LegacyReleaseWarning)]
		[TestCase("v0.9.8-rc.4", Witcher3ScriptMergerCompatibility.RemasteredSupportWithGogCaveat)]
		[TestCase("v0.9.8-rc.5", Witcher3ScriptMergerCompatibility.RemasteredSupportClaimed)]
		[TestCase("1.1.0", Witcher3ScriptMergerCompatibility.RemasteredSupportClaimed)]
		[TestCase("0.9.8", Witcher3ScriptMergerCompatibility.Unconfirmed)]
		[TestCase("0.9.8.0", Witcher3ScriptMergerCompatibility.Unconfirmed)]
		[TestCase("unversioned", Witcher3ScriptMergerCompatibility.Unconfirmed)]
		public void FreshAutomatedRespectsDocumentedRemasteredReleaseBoundary(string version,
			Witcher3ScriptMergerCompatibility expected)
		{
			var report = Assess("Script Merger - Fresh and Automated Edition", version);
			Assert.That(report.Family, Is.EqualTo(Witcher3ScriptMergerFamily.FreshAutomated));
			Assert.That(report.Compatibility, Is.EqualTo(expected));
		}

		[Test]
		public void FileVersionMaySupplyKnownFamilyVersionWhenProductVersionIsAbsent()
		{
			var report = Assess("SM-FAE", "", "", "", fileVersion: "1.1.0.0");
			Assert.That(report.Compatibility, Is.EqualTo(Witcher3ScriptMergerCompatibility.RemasteredSupportClaimed));
		}

		[Test]
		public void ContradictoryClassicAndRemasteredMarkers_AreNotResolvedByVersionAlone()
		{
			var report = Assess("Script Merger Classic Remastered", "1.5.1");
			Assert.That(report.Family, Is.EqualTo(Witcher3ScriptMergerFamily.Unknown));
			Assert.That(report.Compatibility, Is.EqualTo(Witcher3ScriptMergerCompatibility.Unconfirmed));
		}

		[Test]
		public void DescriptionMentioningRemasteredButDenyingCompatibility_IsNotPositiveEvidence()
		{
			var report = Assess("WitcherScriptMerger", "1.5.1", description: "Not compatible with Remastered");
			Assert.That(report.Family, Is.EqualTo(Witcher3ScriptMergerFamily.Unknown));
			Assert.That(report.Compatibility, Is.EqualTo(Witcher3ScriptMergerCompatibility.Unconfirmed));
		}

		[Test]
		public void RenamedUnrecognizedExecutableCannotClaimToBeMerger()
		{
			var report = Assess("Script Merger - Remastered", "1.5.1", fileName: "notepad.exe");
			Assert.That(report.Status, Is.EqualTo(Witcher3ScriptMergerReadStatus.UnexpectedExecutable));
			Assert.That(report.Compatibility, Is.EqualTo(Witcher3ScriptMergerCompatibility.Unconfirmed));
		}

		[Test]
		public void VersionMetadataIsBoundedAndControlCharactersRemoved()
		{
			var metadata = new Witcher3ScriptMergerVersionMetadata(
				"Script Merger\nRemastered\r\t" + new string('a', 400), "description\nwith control",
				"company", "1.5.1\n", "1.5.1");
			Assert.That(metadata.ProductName, Does.Not.Contain("\n"));
			Assert.That(metadata.ProductName.Length, Is.LessThanOrEqualTo(128));
			Assert.That(metadata.FileDescription, Does.Not.Contain("\n"));
			Assert.That(metadata.ProductVersion, Is.EqualTo("1.5.1"));
		}

		[Test]
		public void MissingSelectedFile_DoesNotCreateAnyFileOrClaimNoToolIsInstalled()
		{
			string path = Path.Combine(_temporaryRoot, "WitcherScriptMerger.exe");
			var report = Witcher3ExternalScriptMergerInspector.Inspect(path);
			Assert.That(report.Status, Is.EqualTo(Witcher3ScriptMergerReadStatus.Missing));
			Assert.That(report.Compatibility, Is.EqualTo(Witcher3ScriptMergerCompatibility.Unconfirmed));
			Assert.That(File.Exists(path), Is.False);
		}

		[Test]
		public void UnexpectedExePathIsRejectedWithoutInspectingOrModifyingFileBytes()
		{
			string path = Path.Combine(_temporaryRoot, "someOtherTool.exe");
			byte[] bytes = { 0x4d, 0x5a, 1, 2, 3 };
			File.WriteAllBytes(path, bytes);
			var report = Witcher3ExternalScriptMergerInspector.Inspect(path);
			Assert.That(report.Status, Is.EqualTo(Witcher3ScriptMergerReadStatus.UnexpectedExecutable));
			Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
		}

		[Test]
		public void SelectedNonPeExeIsReadOnlyAndNeverCertifiesRemastered()
		{
			string path = Path.Combine(_temporaryRoot, "WitcherScriptMerger.exe");
			byte[] bytes = { 0x4d, 0x5a, 2, 4, 6 };
			File.WriteAllBytes(path, bytes);
			var report = Witcher3ExternalScriptMergerInspector.Inspect(path);
			Assert.That(report.Status, Is.AnyOf(Witcher3ScriptMergerReadStatus.Observed,
				Witcher3ScriptMergerReadStatus.ReadFailed));
			Assert.That(report.Compatibility, Is.EqualTo(Witcher3ScriptMergerCompatibility.Unconfirmed));
			Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
		}

		[Test]
		public void RelativeFilePathCannotResolveToAnotherExecutableSilently()
		{
			Assert.Throws<ArgumentException>(() =>
				Witcher3ExternalScriptMergerInspector.Inspect("WitcherScriptMerger.exe"));
		}

		[Test]
		public void NullMetadataAndMissingExecutableNameCannotBeUsedAsCompatibilityProof()
		{
			Assert.Throws<ArgumentNullException>(() =>
				Witcher3ExternalScriptMergerInspector.Assess("WitcherScriptMerger.exe", null));
			Assert.Throws<ArgumentException>(() =>
				Witcher3ExternalScriptMergerInspector.Assess(" ",
					new Witcher3ScriptMergerVersionMetadata("Remastered", "", "", "1.5.1", "")));
		}
	}
}
