using System;
using System.Linq;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3ModSettingsParserTests
	{
		[Test]
		public void ValidModSections_ReadOrderAndEnabledWithoutAssumingTheGameAppliedThem()
		{
			var result = Witcher3ModSettingsParser.Parse(
				"[modB]\r\nEnabled=0\r\nPriority=12\r\n[modA]\r\nPriority=2\r\nEnabled=1\r\n");
			Assert.That(result.IsUnambiguous, Is.True);
			Assert.That(result.ModSections.Select(x => x.Name), Is.EqualTo(new[] { "modB", "modA" }));
			Assert.That(result.ModSections.Select(x => x.Priority), Is.EqualTo(new int?[] { 12, 2 }));
			Assert.That(result.ModSections.Select(x => x.Enabled), Is.EqualTo(new bool?[] { false, true }));
		}

		[Test]
		public void MixedNewlinesCommentsUnknownKeysAndVortexMetadata_ArePreservedExactlyAsText()
		{
			string text = "\uFEFF; preamble\r\n[modExample] ; comment\nEnabled=1\rPriority=7\r\n"
				+ "VK=vortex-only\r\nThirdPartyOption=a=b\n\n[OtherTool]\r\nCustom=keep\r\n";
			var result = Witcher3ModSettingsParser.Parse(text);
			Assert.That(result.IsUnambiguous, Is.True);
			Assert.That(result.OriginalText, Is.EqualTo(text));
			Assert.That(result.Sections.Count, Is.EqualTo(2));
			Assert.That(result.Sections[0].VortexKey, Is.EqualTo("vortex-only"));
			Assert.That(result.Sections[0].Values.Single(x => x.Key == "ThirdPartyOption").Value,
				Is.EqualTo("a=b"));
			Assert.That(result.Sections[1].IsModFolder, Is.False);
			Assert.That(result.Sections[1].Values.Single().Value, Is.EqualTo("keep"));
		}

		[Test]
		public void MissingEnabledOrPriority_StaysNullInsteadOfBeingInvented()
		{
			var result = Witcher3ModSettingsParser.Parse("[modManual]\nNonStandard=1\n");
			var entry = result.ModSections.Single();
			Assert.That(entry.Enabled, Is.Null);
			Assert.That(entry.Priority, Is.Null);
			Assert.That(entry.HasEnabledField, Is.False);
			Assert.That(entry.HasPriorityField, Is.False);
			Assert.That(result.IsUnambiguous, Is.True);
		}

		[Test]
		public void DuplicateSectionCaseAliases_AreNotSilentlyMergedOrAssignedAnOrder()
		{
			var result = Witcher3ModSettingsParser.Parse(
				"[modSword]\nPriority=1\n[MODSWORD]\nPriority=3\n");
			Assert.That(result.Sections.Count, Is.EqualTo(2));
			Assert.That(result.Sections.All(x => !x.HasUniqueName), Is.True);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ModSettingsIssueKind.DuplicateSection));
			Assert.That(result.IsUnambiguous, Is.False);
		}

		[Test]
		public void DuplicatePriority_ReportsBothComponentsButKeepsRawValues()
		{
			var result = Witcher3ModSettingsParser.Parse(
				"[modA]\nPriority=3\n[modB]\nPriority=3\n[Other]\nPriority=3\n");
			Assert.That(result.Issues.Count(x => x.Kind == Witcher3ModSettingsIssueKind.DuplicatePriority),
				Is.EqualTo(2));
			Assert.That(result.ModSections.Select(x => x.Priority), Is.EqualTo(new int?[] { 3, 3 }));
		}

		[Test]
		public void DuplicateKeyCaseAliases_KeepDataButNeverGuessActiveValue()
		{
			var result = Witcher3ModSettingsParser.Parse(
				"[modA]\nPriority=2\npriority=5\nEnabled=1\nENABLED=0\nVK=a\nvk=b\n");
			var entry = result.ModSections.Single();
			Assert.That(entry.Values.Count, Is.EqualTo(6));
			Assert.That(entry.Priority, Is.Null);
			Assert.That(entry.Enabled, Is.Null);
			Assert.That(entry.VortexKey, Is.Null);
			Assert.That(result.Issues.Count(x => x.Kind == Witcher3ModSettingsIssueKind.DuplicateSetting),
				Is.EqualTo(3));
		}

		[TestCase("1.5", Witcher3ModSettingsIssueKind.InvalidPriority)]
		[TestCase("2147483648", Witcher3ModSettingsIssueKind.InvalidPriority)]
		[TestCase("true", Witcher3ModSettingsIssueKind.InvalidPriority)]
		public void MalformedPriority_IsNotUsedAsOrder(string priority, Witcher3ModSettingsIssueKind issue)
		{
			var result = Witcher3ModSettingsParser.Parse("[modA]\nPriority=" + priority + "\n");
			Assert.That(result.ModSections.Single().Priority, Is.Null);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(issue));
		}

		[TestCase("false")]
		[TestCase("True")]
		[TestCase("2")]
		[TestCase("1 ; comment")]
		public void NonCanonicalEnabled_IsUnknownRatherThanAssumed(string enabled)
		{
			var result = Witcher3ModSettingsParser.Parse("[modA]\nEnabled=" + enabled);
			Assert.That(result.ModSections.Single().Enabled, Is.Null);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ModSettingsIssueKind.InvalidEnabled));
		}

		[Test]
		public void SignedIntegerPriorities_AreRetainedWithoutWritingCanonicalReplacement()
		{
			var result = Witcher3ModSettingsParser.Parse("[modA]\nPriority=-10\n[modB]\nPriority=+20\n");
			Assert.That(result.ModSections.Select(x => x.Priority), Is.EqualTo(new int?[] { -10, 20 }));
			Assert.That(result.IsUnambiguous, Is.True);
		}

		[Test]
		public void SuspiciousFolderNamesAndDlcSections_AreRetainedButNotExposedAsModFolders()
		{
			var result = Witcher3ModSettingsParser.Parse(
				"[dlcQuest]\nPriority=1\n[mod..]\nPriority=2\n[mod/a]\nPriority=3\n[modOk]\nPriority=4\n");
			Assert.That(result.Sections.Count, Is.EqualTo(4));
			Assert.That(result.ModSections.Select(x => x.Name), Is.EqualTo(new[] { "modOk" }));
		}

		[Test]
		public void MalformedHeader_DoesNotAttachFollowingSettingToPriorSection()
		{
			var result = Witcher3ModSettingsParser.Parse(
				"[modA]\nPriority=1\n[modBroken\nPriority=8\n[modB]\nPriority=2\n");
			Assert.That(result.ModSections.Count(), Is.EqualTo(2));
			Assert.That(result.ModSections.First().Priority, Is.EqualTo(1));
			Assert.That(result.ModSections.Last().Priority, Is.EqualTo(2));
			Assert.That(result.Issues.Select(x => x.Kind),
				Does.Contain(Witcher3ModSettingsIssueKind.MalformedSectionHeader));
			Assert.That(result.Issues.Select(x => x.Kind),
				Does.Contain(Witcher3ModSettingsIssueKind.SettingOutsideSection));
		}

		[Test]
		public void GlobalSettingsAndMalformedLines_AreOnlyRecordedNotInterpreted()
		{
			string text = "Priority=200\n[modA]\nInvalidLine\nEnabled=1\n";
			var result = Witcher3ModSettingsParser.Parse(text);
			Assert.That(result.OriginalText, Is.EqualTo(text));
			Assert.That(result.ModSections.Single().Enabled, Is.True);
			Assert.That(result.ModSections.Single().Priority, Is.Null);
			Assert.That(result.Issues.Select(x => x.Kind),
				Does.Contain(Witcher3ModSettingsIssueKind.SettingOutsideSection));
			Assert.That(result.Issues.Select(x => x.Kind),
				Does.Contain(Witcher3ModSettingsIssueKind.MalformedSetting));
		}

		[Test]
		public void EmptyOrCommentOnlyFile_MeansNoOverridesNotDisabledMods()
		{
			var empty = Witcher3ModSettingsParser.Parse(String.Empty);
			var comments = Witcher3ModSettingsParser.Parse("; comment\n# more\r\n");
			Assert.That(empty.Sections, Is.Empty);
			Assert.That(empty.IsUnambiguous, Is.True);
			Assert.That(comments.Sections, Is.Empty);
			Assert.That(comments.IsUnambiguous, Is.True);
		}

		[Test]
		public void OversizeText_IsRejectedWithoutExposingPartiallyInterpretedSections()
		{
			var text = "[modA]\nPriority=1\n" + new string('x', Witcher3ModSettingsParser.MaxCharacters);
			var result = Witcher3ModSettingsParser.Parse(text);
			Assert.That(result.IsComplete, Is.False);
			Assert.That(result.Sections, Is.Empty);
			Assert.That(result.Issues.Select(x => x.Kind),
				Does.Contain(Witcher3ModSettingsIssueKind.InputLimitExceeded));
		}

		[Test]
		public void TooManyLines_IsRejectedWithoutPartiallyTrustedEntries()
		{
			var result = Witcher3ModSettingsParser.Parse("[modA]\n" +
				String.Concat(Enumerable.Repeat("; comment\n", Witcher3ModSettingsParser.MaxLines)));
			Assert.That(result.IsComplete, Is.False);
			Assert.That(result.Sections, Is.Empty);
			Assert.That(result.Issues.Select(x => x.Kind),
				Does.Contain(Witcher3ModSettingsIssueKind.InputLimitExceeded));
		}
	}
}
