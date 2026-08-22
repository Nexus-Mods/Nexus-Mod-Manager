using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nexus.Client.Util.Localization
{
	/// <summary>
	/// Provides low-overhead, read-only access to the selected NMM UI language catalog.
	/// </summary>
	public static class LanguageManager
	{
		public const int CurrentPackFormatVersion = 1;
		public const string DefaultLanguageId = "en-US";
		public const string DefaultLanguageName = "English";
		public const string LanguagesDirectoryName = "Languages";

		private static readonly Regex FormatItemRegex = new Regex(@"(?<!\{)\{(?<index>\d+)(?:,[^}:]+)?(?::[^{}]*)?\}(?!\})", RegexOptions.CultureInvariant);
		private static readonly Dictionary<string, string> EmptyStrings = new Dictionary<string, string>(StringComparer.Ordinal);
		private static readonly LanguagePackInfo BuiltInEnglish = new LanguagePackInfo(
			CurrentPackFormatVersion,
			DefaultLanguageId,
			DefaultLanguageName,
			DefaultLanguageId,
			null,
			null,
			true);

		private static Dictionary<string, string> _strings = EmptyStrings;
		private static bool _hasTranslations;
		private static LanguagePackInfo _currentLanguage = BuiltInEnglish;
		private static IReadOnlyList<LanguagePackInfo> _availableLanguages = new ReadOnlyCollection<LanguagePackInfo>(new List<LanguagePackInfo> { BuiltInEnglish });
		private static bool _isInitialized;

		/// <summary>
		/// Gets whether the language manager has been initialized for this process.
		/// </summary>
		public static bool IsInitialized
		{
			get { return _isInitialized; }
		}

		/// <summary>
		/// Gets the active language. English is returned when no valid external pack is selected.
		/// </summary>
		public static LanguagePackInfo CurrentLanguage
		{
			get { return _currentLanguage; }
		}

		/// <summary>
		/// Gets the languages discovered during the last initialization.
		/// </summary>
		public static IReadOnlyList<LanguagePackInfo> AvailableLanguages
		{
			get { return _availableLanguages; }
		}

		/// <summary>
		/// Gets the default directory used for user-created language packs.
		/// </summary>
		public static string DefaultLanguagesDirectory
		{
			get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LanguagesDirectoryName); }
		}

		/// <summary>
		/// Initializes the language catalog. This is intended to run once during application startup,
		/// before UI creation. Reinitialization is supported for deterministic recovery/testing but is
		/// not used for live language switching.
		/// </summary>
		/// <param name="languageId">The persisted language identifier to load.</param>
		/// <param name="languagesDirectory">Optional language directory override.</param>
		public static void Initialize(string languageId, string languagesDirectory = null)
		{
			string directory = string.IsNullOrWhiteSpace(languagesDirectory)
				? DefaultLanguagesDirectory
				: languagesDirectory;

			EnsureLanguagesDirectory(directory);
			IReadOnlyList<LanguagePackInfo> availableLanguages = DiscoverLanguages(directory);
			LanguagePackInfo currentLanguage = BuiltInEnglish;
			Dictionary<string, string> strings = EmptyStrings;

			if (!string.IsNullOrWhiteSpace(languageId) &&
				!string.Equals(languageId, DefaultLanguageId, StringComparison.OrdinalIgnoreCase))
			{
				LanguagePackInfo selectedLanguage = availableLanguages.FirstOrDefault(
					language => string.Equals(language.Id, languageId, StringComparison.OrdinalIgnoreCase));

				if (selectedLanguage != null && !selectedLanguage.IsBuiltIn)
				{
					LanguagePack pack;
					if (LanguagePackLoader.TryLoad(selectedLanguage.FilePath, out pack))
					{
						if (string.Equals(pack.Id, selectedLanguage.Id, StringComparison.OrdinalIgnoreCase))
						{
							strings = pack.Strings ?? EmptyStrings;
							currentLanguage = selectedLanguage;
						}
						else
						{
							Trace.TraceWarning(
								"Ignoring language pack '{0}': its language id changed from '{1}' to '{2}' while loading.",
								selectedLanguage.FilePath,
								selectedLanguage.Id,
								pack.Id);
						}
					}
				}
			}

			_strings = strings;
			_hasTranslations = strings.Count != 0;
			_currentLanguage = currentLanguage;
			_availableLanguages = availableLanguages;
			_isInitialized = true;
		}

		/// <summary>
		/// Discovers valid JSON language packs. English is always present and cannot be replaced by an external pack.
		/// </summary>
		public static IReadOnlyList<LanguagePackInfo> DiscoverLanguages(string languagesDirectory = null)
		{
			string directory = string.IsNullOrWhiteSpace(languagesDirectory)
				? DefaultLanguagesDirectory
				: languagesDirectory;

			List<LanguagePackInfo> languages = new List<LanguagePackInfo> { BuiltInEnglish };
			HashSet<string> languageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DefaultLanguageId };

			try
			{
				if (!Directory.Exists(directory))
					return languages.AsReadOnly();

				string[] files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
				Array.Sort(files, StringComparer.OrdinalIgnoreCase);

				foreach (string filePath in files)
				{
					LanguagePackInfo info;
					if (!LanguagePackLoader.TryReadInfo(filePath, out info))
						continue;

					// English is built into NMM. An external English.json may be shipped as a
					// translator template, but it must never replace or duplicate the built-in entry.
					if (string.Equals(info.Id, DefaultLanguageId, StringComparison.OrdinalIgnoreCase))
						continue;

					if (!languageIds.Add(info.Id))
					{
						Trace.TraceWarning(
							"Ignoring language pack '{0}': language id '{1}' is already registered.",
							filePath,
							info.Id);
						continue;
					}

					languages.Add(info);
				}
			}
			catch (Exception ex)
			{
				Trace.TraceWarning(
					"Unable to enumerate NMM language packs in '{0}'. English will remain available. {1}",
					directory ?? string.Empty,
					ex.Message);
			}

			return languages.AsReadOnly();
		}

		/// <summary>
		/// Ensures the default or supplied language directory exists.
		/// </summary>
		public static bool EnsureLanguagesDirectory(string languagesDirectory = null)
		{
			string directory = string.IsNullOrWhiteSpace(languagesDirectory)
				? DefaultLanguagesDirectory
				: languagesDirectory;

			try
			{
				Directory.CreateDirectory(directory);
				return true;
			}
			catch (Exception ex)
			{
				Trace.TraceWarning(
					"Unable to create NMM language directory '{0}'. {1}",
					directory ?? string.Empty,
					ex.Message);
				return false;
			}
		}

		/// <summary>
		/// Gets whether the active external pack contains at least one translation under the supplied key prefix.
		/// Intended for one-time startup integration decisions, not UI hot paths.
		/// </summary>
		public static bool HasTranslationsWithPrefix(string keyPrefix)
		{
			if (!_hasTranslations || string.IsNullOrEmpty(keyPrefix))
				return false;

			foreach (string key in _strings.Keys)
			{
				if (key.StartsWith(keyPrefix, StringComparison.Ordinal))
					return true;
			}

			return false;
		}

		/// <summary>
		/// Returns a translated static UI string, or the supplied built-in English fallback.
		/// </summary>
		public static string Get(string key, string fallback)
		{
			if (!_hasTranslations || string.IsNullOrEmpty(key))
				return fallback;

			string value;
			return _strings.TryGetValue(key, out value) ? value : fallback;
		}

		/// <summary>
		/// Returns a translated composite-format string after validating its placeholders and syntax,
		/// or the supplied English fallback. Intended for formats cached once by UI controls.
		/// </summary>
		public static string GetFormat(string key, string fallback)
		{
			if (!_hasTranslations || string.IsNullOrEmpty(key))
				return fallback;

			string translated;
			if (!_strings.TryGetValue(key, out translated))
				return fallback;

			if (!HaveMatchingFormatItems(fallback, translated))
			{
				Trace.TraceWarning("Ignoring localization key '{0}' because its format placeholders do not match the English fallback.", key);
				return fallback;
			}

			if (!IsValidCompositeFormat(translated))
			{
				Trace.TraceWarning("Ignoring localization key '{0}' because its translated format string is invalid.", key);
				return fallback;
			}

			return translated;
		}

		/// <summary>
		/// Returns and formats a translated static UI string. Invalid translated format strings fall back to English.
		/// </summary>
		public static string Format(string key, string fallback, params object[] args)
		{
			string format = fallback;

			if (_hasTranslations && !string.IsNullOrEmpty(key))
			{
				string translated;
				if (_strings.TryGetValue(key, out translated))
				{
					if (HaveMatchingFormatItems(fallback, translated))
						format = translated;
					else
						Trace.TraceWarning("Ignoring localization key '{0}' because its format placeholders do not match the English fallback.", key);
				}
			}

			try
			{
				return string.Format(CultureInfo.CurrentCulture, format, args);
			}
			catch (FormatException)
			{
				if (!string.Equals(format, fallback, StringComparison.Ordinal))
				{
					Trace.TraceWarning("Ignoring localization key '{0}' because its translated format string is invalid.", key);
					return string.Format(CultureInfo.CurrentCulture, fallback, args);
				}

				throw;
			}
		}

		private static bool HaveMatchingFormatItems(string fallback, string translated)
		{
			if (fallback == null || translated == null)
				return fallback == translated;

			HashSet<int> fallbackIndexes = GetFormatItemIndexes(fallback);
			HashSet<int> translatedIndexes = GetFormatItemIndexes(translated);
			return fallbackIndexes.SetEquals(translatedIndexes);
		}

		private static bool IsValidCompositeFormat(string value)
		{
			if (value == null)
				return false;

			HashSet<int> indexes = GetFormatItemIndexes(value);
			int argumentCount = indexes.Count == 0 ? 0 : indexes.Max() + 1;
			object[] arguments = new object[argumentCount];

			try
			{
				string.Format(CultureInfo.InvariantCulture, value, arguments);
				return true;
			}
			catch (FormatException)
			{
				return false;
			}
		}

		private static HashSet<int> GetFormatItemIndexes(string value)
		{
			HashSet<int> indexes = new HashSet<int>();
			MatchCollection matches = FormatItemRegex.Matches(value);

			foreach (Match match in matches)
			{
				int index;
				if (int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out index))
					indexes.Add(index);
			}

			return indexes;
		}
	}
}
