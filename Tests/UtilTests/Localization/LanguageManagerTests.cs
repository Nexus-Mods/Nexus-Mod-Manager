using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.Util.Localization;
using NUnit.Framework;

namespace UtilTests.Localization
{
	[TestFixture]
	[NonParallelizable]
	public class LanguageManagerTests
	{
		private string _languagesDirectory;

		[SetUp]
		public void SetUp()
		{
			_languagesDirectory = Path.Combine(Path.GetTempPath(), "NMM-LanguageManagerTests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_languagesDirectory);
			LanguageManager.Initialize(LanguageManager.DefaultLanguageId, _languagesDirectory);
		}

		[TearDown]
		public void TearDown()
		{
			LanguageManager.Initialize(LanguageManager.DefaultLanguageId, _languagesDirectory);

			if (Directory.Exists(_languagesDirectory))
				Directory.Delete(_languagesDirectory, true);
		}

		[Test]
		public void DiscoverLanguagesAlwaysIncludesBuiltInEnglish()
		{
			var languages = LanguageManager.DiscoverLanguages(_languagesDirectory);

			Assert.AreEqual(1, languages.Count);
			Assert.AreEqual(LanguageManager.DefaultLanguageId, languages[0].Id);
			Assert.IsTrue(languages[0].IsBuiltIn);
		}

		[Test]
		public void DiscoverLanguagesReadsValidPackMetadata()
		{
			WritePack("Italiano.json", ValidItalianPackJson);

			var languages = LanguageManager.DiscoverLanguages(_languagesDirectory);
			var italian = languages.Single(language => language.Id == "it-IT");

			Assert.AreEqual("Italiano", italian.Name);
			Assert.AreEqual("it-IT", italian.Culture);
			Assert.AreEqual("NMM Community", italian.Author);
			Assert.IsFalse(italian.IsBuiltIn);
		}

		[Test]
		public void InitializeLoadsOnlySelectedPackAndFallsBackPerKey()
		{
			WritePack("Italiano.json", ValidItalianPackJson);

			LanguageManager.Initialize("it-IT", _languagesDirectory);

			Assert.AreEqual("it-IT", LanguageManager.CurrentLanguage.Id);
			Assert.AreEqual("Annulla", LanguageManager.Get("Common.Button.Cancel", "Cancel"));
			Assert.AreEqual("Refresh", LanguageManager.Get("Common.Action.Refresh", "Refresh"));
		}

		[Test]
		public void InitializeFallsBackToEnglishWhenSelectedPackIsMissing()
		{
			LanguageManager.Initialize("missing-language", _languagesDirectory);

			Assert.AreEqual(LanguageManager.DefaultLanguageId, LanguageManager.CurrentLanguage.Id);
			Assert.AreEqual("Cancel", LanguageManager.Get("Common.Button.Cancel", "Cancel"));
		}

		[Test]
		public void CorruptAndUnsupportedPacksAreIgnored()
		{
			WritePack("Broken.json", "{this is not valid json");
			WritePack("Future.json", "{\"formatVersion\":2,\"id\":\"xx-XX\",\"name\":\"Future\",\"strings\":{}}");

			var languages = LanguageManager.DiscoverLanguages(_languagesDirectory);

			Assert.AreEqual(1, languages.Count);
			Assert.AreEqual(LanguageManager.DefaultLanguageId, languages[0].Id);
		}

		[Test]
		public void ExternalPackCannotReplaceBuiltInEnglish()
		{
			WritePack("English.json", "{\"formatVersion\":1,\"id\":\"en-US\",\"name\":\"Modified English\",\"strings\":{\"Common.Button.Cancel\":\"Changed\"}}");

			LanguageManager.Initialize("en-US", _languagesDirectory);

			Assert.AreEqual(1, LanguageManager.AvailableLanguages.Count);
			Assert.IsTrue(LanguageManager.CurrentLanguage.IsBuiltIn);
			Assert.AreEqual("Cancel", LanguageManager.Get("Common.Button.Cancel", "Cancel"));
		}

		[Test]
		public void LocalizationKeysAreCaseSensitive()
		{
			WritePack("Italiano.json", ValidItalianPackJson);
			LanguageManager.Initialize("it-IT", _languagesDirectory);

			Assert.AreEqual("Cancel", LanguageManager.Get("common.button.cancel", "Cancel"));
		}

		[Test]
		public void HasTranslationsWithPrefixFindsOnlyMatchingPrefix()
		{
			WritePack(
				"Italiano.json",
				"{\"formatVersion\":1,\"id\":\"it-IT\",\"name\":\"Italiano\",\"strings\":{\"DevExpress.Grid.MenuColumnSortAscending\":\"Ordina crescente\"}}");
			LanguageManager.Initialize("it-IT", _languagesDirectory);

			Assert.IsTrue(LanguageManager.HasTranslationsWithPrefix("DevExpress.Grid."));
			Assert.IsFalse(LanguageManager.HasTranslationsWithPrefix("DevExpress.Editors."));
		}

		[Test]
		public void FormatRejectsTranslationWithMismatchedPlaceholders()
		{
			WritePack(
				"Italiano.json",
				"{\"formatVersion\":1,\"id\":\"it-IT\",\"name\":\"Italiano\",\"strings\":{\"Test.Format\":\"{0} file copiati\"}}");
			LanguageManager.Initialize("it-IT", _languagesDirectory);

			string value = LanguageManager.Format("Test.Format", "{0} files copied to {1}", 4, "C:\\Mods");

			Assert.AreEqual("4 files copied to C:\\Mods", value);
		}

		[Test]
		public void GetFormatRejectsTranslationWithMismatchedPlaceholders()
		{
			WritePack(
				"Italiano.json",
				"{\"formatVersion\":1,\"id\":\"it-IT\",\"name\":\"Italiano\",\"strings\":{\"Test.Format\":\"{0} file copiati\"}}");
			LanguageManager.Initialize("it-IT", _languagesDirectory);

			Assert.AreEqual("{0} files copied to {1}", LanguageManager.GetFormat("Test.Format", "{0} files copied to {1}"));
		}

		[Test]
		public void GetFormatRejectsMalformedTranslation()
		{
			WritePack(
				"Italiano.json",
				"{\"formatVersion\":1,\"id\":\"it-IT\",\"name\":\"Italiano\",\"strings\":{\"Test.Format\":\"Valore {0}}\"}}");
			LanguageManager.Initialize("it-IT", _languagesDirectory);

			Assert.AreEqual("Value {0}", LanguageManager.GetFormat("Test.Format", "Value {0}"));
		}

		[Test]
		public void EnsureLanguagesDirectoryCreatesMissingDirectory()
		{
			string nestedDirectory = Path.Combine(_languagesDirectory, "Nested", "Languages");

			Assert.IsTrue(LanguageManager.EnsureLanguagesDirectory(nestedDirectory));
			Assert.IsTrue(Directory.Exists(nestedDirectory));
		}

		private void WritePack(string fileName, string json)
		{
			File.WriteAllText(Path.Combine(_languagesDirectory, fileName), json, new UTF8Encoding(false));
		}

		private const string ValidItalianPackJson =
			"{\"formatVersion\":1,\"id\":\"it-IT\",\"name\":\"Italiano\",\"culture\":\"it-IT\",\"author\":\"NMM Community\",\"strings\":{\"Common.Button.Cancel\":\"Annulla\"}}";
	}
}
