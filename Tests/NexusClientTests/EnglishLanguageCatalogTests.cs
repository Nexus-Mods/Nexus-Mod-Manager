namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Runtime.Serialization.Json;
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
			@"LanguageManager\.(?:Get|GetFormat|Format)\s*\(\s*""(?<key>[^""]+)""",
			RegexOptions.CultureInvariant);

		private static readonly Regex MainFormKeyRegex = new Regex(
			@"\bL\s*\(\s*""(?<key>[^""]+)""",
			RegexOptions.CultureInvariant);

		private static readonly string[] LocalizedSourceDirectories =
		{
			"NexusClient",
			"NexusClient.Interface",
			"ModManager.Interface",
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
			HashSet<string> sourceKeys = CollectStaticSourceKeys(repositoryRoot);
			HashSet<string> catalogKeys = new HashSet<string>(pack.Strings.Keys, StringComparer.Ordinal);

			string[] missing = sourceKeys.Except(catalogKeys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
			string[] stale = catalogKeys
				.Where(key => !key.StartsWith("DevExpress.", StringComparison.Ordinal))
				.Except(sourceKeys, StringComparer.Ordinal)
				.OrderBy(key => key, StringComparer.Ordinal)
				.ToArray();

			Assert.That(missing, Is.Empty, "English.json is missing static localization keys: " + String.Join(", ", missing));
			Assert.That(stale, Is.Empty, "English.json contains stale NMM localization keys: " + String.Join(", ", stale));
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

		private static LanguagePack LoadEnglishTemplate(string repositoryRoot)
		{
			string path = Path.Combine(repositoryRoot, "NexusClient", "Languages", "English.json");
			Assert.That(File.Exists(path), Is.True, "English language template was not found at " + path);

			DataContractJsonSerializer serializer = new DataContractJsonSerializer(
				typeof(LanguagePack),
				new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

			using (FileStream stream = File.OpenRead(path))
				return (LanguagePack)serializer.ReadObject(stream);
		}

		private static HashSet<string> CollectStaticSourceKeys(string repositoryRoot)
		{
			HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);

			foreach (string directoryName in LocalizedSourceDirectories)
			{
				string directory = Path.Combine(repositoryRoot, directoryName);
				if (!Directory.Exists(directory))
					continue;

				foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
				{
					string source = File.ReadAllText(file);
					AddMatches(keys, LanguageManagerKeyRegex, source);

					if (Path.GetFileName(file).StartsWith("MainForm", StringComparison.Ordinal))
						AddMatches(keys, MainFormKeyRegex, source);
				}
			}

			return keys;
		}

		private static void AddMatches(ISet<string> keys, Regex regex, string source)
		{
			foreach (Match match in regex.Matches(source))
				keys.Add(match.Groups["key"].Value);
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
