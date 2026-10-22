namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Runtime.Serialization.Json;
	using System.Text;
	using System.Text.RegularExpressions;

	using Nexus.Client.Util.Localization;

	using NUnit.Framework;

	/// <summary>
	/// Keeps the shipped English translator template synchronized with the static
	/// localization keys used by NMM source code.
	/// </summary>
	public class EnglishLanguageCatalogTests
	{
		private static readonly Regex LanguageManagerKeyRegex = new Regex(
			@"LanguageManager\.(?:Get|GetFormat|Format)\s*\(\s*""(?<key>[^""]+)""(?<dynamic>\s*\+)?",
			RegexOptions.CultureInvariant);

		private static readonly Regex LocalHelperKeyRegex = new Regex(
			@"\bL\s*\(\s*""(?<key>[^""]+)""(?<dynamic>\s*\+)?",
			RegexOptions.CultureInvariant);

		private static readonly Regex LocalHelperPrefixRegex = new Regex(
			@"\bstring\s+L\s*\([^)]*\)\s*(?:=>|\{\s*return)\s*LanguageManager\.Get\s*\(\s*""(?<prefix>[^""]+)""\s*\+\s*key\b",
			RegexOptions.CultureInvariant);

		private static readonly Regex FormatItemRegex = new Regex(
			@"(?<!\{)\{(?<index>\d+)(?:,[^}:]+)?(?::[^{}]*)?\}(?!\})",
			RegexOptions.CultureInvariant);

		private static readonly Regex CollectionKeyRegex = new Regex(
			@"""(?<key>Collections\.[A-Za-z0-9.]+)""",
			RegexOptions.CultureInvariant);

		private static readonly string[] LocalizedSourceDirectories =
		{
			"NexusClient",
			"NexusClient.Interface",
			"ModManager.Interface",
			"Script Types",
			"Util",
			"UI",
			"Game Modes",
			"Mods",
			"Commanding"
		};

		[Test]
		public void EnglishTemplateContainsEveryStaticNmmLocalizationKey()
		{
			string repositoryRoot = FindRepositoryRoot();
			LanguagePack pack = LoadEnglishTemplate(repositoryRoot);
			HashSet<string> sourceKeys = CollectStaticSourceKeys(repositoryRoot, pack.Strings.Keys);
			HashSet<string> catalogKeys = new HashSet<string>(pack.Strings.Keys, StringComparer.Ordinal);

			string[] missing = sourceKeys.Except(catalogKeys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
			Assert.That(missing, Is.Empty, "English.json is missing static localization keys: " + String.Join(", ", missing));
			// Retained translations and keys composed by other presentation helpers are valid catalog entries.
		}

		[Test]
		public void EnglishTemplateHasBuiltInEnglishMetadata()
		{
			LanguagePack pack = LoadEnglishTemplate(FindRepositoryRoot());

			Assert.That(pack.FormatVersion, Is.EqualTo(LanguageManager.CurrentPackFormatVersion));
			Assert.That(pack.Id, Is.EqualTo(LanguageManager.DefaultLanguageId));
			Assert.That(pack.Name, Is.EqualTo(LanguageManager.DefaultLanguageName));
			Assert.That(pack.Culture, Is.EqualTo(LanguageManager.DefaultLanguageId));
			Assert.That(pack.Strings, Is.Not.Null.And.Not.Empty);
		}

		[Test]
		public void ShippedTranslationsMatchEnglishTemplateKeys()
		{
			string repositoryRoot = FindRepositoryRoot();
			LanguagePack english = LoadEnglishTemplate(repositoryRoot);
			HashSet<string> englishKeys = new HashSet<string>(english.Strings.Keys, StringComparer.Ordinal);
			string languagesDirectory = Path.Combine(repositoryRoot, "NexusClient", "Languages");

			foreach (string path in Directory.EnumerateFiles(languagesDirectory, "*.json", SearchOption.TopDirectoryOnly)
				.Where(candidate => !Path.GetFileName(candidate).Equals("English.json", StringComparison.OrdinalIgnoreCase))
				.OrderBy(candidate => candidate, StringComparer.Ordinal))
			{
				LanguagePack pack = LoadLanguagePack(path);
				HashSet<string> translatedKeys = new HashSet<string>(pack.Strings.Keys, StringComparer.Ordinal);
				string[] missing = englishKeys.Except(translatedKeys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
				string[] extra = translatedKeys.Except(englishKeys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
				string fileName = Path.GetFileName(path);

				Assert.That(missing, Is.Empty, fileName + " is missing localization keys: " + String.Join(", ", missing));
				Assert.That(extra, Is.Empty, fileName + " contains keys not present in English.json: " + String.Join(", ", extra));
			}
		}

		/// <summary>
		/// Ensures translations preserve every composite-format argument, including repeated arguments in longer prompts.
		/// </summary>
		[Test]
		public void ShippedTranslationsPreserveEnglishFormatPlaceholders()
		{
			string repositoryRoot = FindRepositoryRoot();
			LanguagePack english = LoadEnglishTemplate(repositoryRoot);
			foreach (string path in Directory.EnumerateFiles(Path.Combine(repositoryRoot, "NexusClient", "Languages"), "*.json"))
			{
				LanguagePack translated = LoadLanguagePack(path);
				foreach (KeyValuePair<string, string> entry in english.Strings)
				{
					string value;
					if (!translated.Strings.TryGetValue(entry.Key, out value))
						continue; // Key coverage has a separate assertion with a complete missing-key report.
					string[] expected = FormatItemRegex.Matches(entry.Value).Cast<Match>()
						.Select(match => match.Value).OrderBy(item => item, StringComparer.Ordinal).ToArray();
					string[] actual = FormatItemRegex.Matches(value).Cast<Match>()
						.Select(match => match.Value).OrderBy(item => item, StringComparer.Ordinal).ToArray();
					Assert.That(actual, Is.EqualTo(expected), Path.GetFileName(path) + " changes placeholders for " + entry.Key);
				}
			}
		}

		private static LanguagePack LoadEnglishTemplate(string repositoryRoot)
		{
			string path = Path.Combine(repositoryRoot, "NexusClient", "Languages", "English.json");
			Assert.That(File.Exists(path), Is.True, "English language template was not found at " + path);
			return LoadLanguagePack(path);
		}

		private static LanguagePack LoadLanguagePack(string path)
		{
			DataContractJsonSerializer serializer = new DataContractJsonSerializer(
				typeof(LanguagePack),
				new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

			string json = File.ReadAllText(path);
			if (json.Length != 0 && json[0] == '\uFEFF') json = json.Substring(1);
			using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), false))
				return (LanguagePack)serializer.ReadObject(stream);
		}

		private static HashSet<string> CollectStaticSourceKeys(string repositoryRoot, IEnumerable<string> catalogKeys)
		{
			HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
			HashSet<string> dynamicPrefixes = new HashSet<string>(StringComparer.Ordinal);

			foreach (string directoryName in LocalizedSourceDirectories)
			{
				string directory = Path.Combine(repositoryRoot, directoryName);
				if (!Directory.Exists(directory))
					continue;

				foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
				{
					string source = File.ReadAllText(file);
					AddMatches(keys, dynamicPrefixes, LanguageManagerKeyRegex, source);

					Match helper = LocalHelperPrefixRegex.Match(source);
					AddMatches(keys, dynamicPrefixes, LocalHelperKeyRegex, source, helper.Success ? helper.Groups["prefix"].Value : String.Empty);
					// Collection messages also pass keys through presentation helpers and conditional expressions.
					AddMatches(keys, dynamicPrefixes, CollectionKeyRegex, source);
				}
			}

			// Enum-derived families are composed at runtime; a prefix is not itself a missing key.
			keys.UnionWith(catalogKeys.Where(key => dynamicPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))));
			return keys;
		}

		private static void AddMatches(ISet<string> keys, ISet<string> dynamicPrefixes, Regex regex, string source, string prefix = "")
		{
			foreach (Match match in regex.Matches(source))
			{
				string key = prefix + match.Groups["key"].Value;
				if (match.Groups["dynamic"].Success || key.EndsWith(".", StringComparison.Ordinal))
					dynamicPrefixes.Add(key);
				else keys.Add(key);
			}
		}

		private static string FindRepositoryRoot()
		{
			DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

			while (directory != null)
			{
				if (File.Exists(Path.Combine(directory.FullName, "NexusClient.sln")))
					return directory.FullName;

				directory = directory.Parent;
			}

			Assert.Fail("Unable to locate the NMM repository root from the test directory.");
			return null;
		}
	}
}
