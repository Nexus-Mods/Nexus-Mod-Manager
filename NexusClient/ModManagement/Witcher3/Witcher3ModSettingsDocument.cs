using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Nexus.Client.ModManagement.Witcher3
{
	public enum Witcher3ModSettingsIssueKind
	{
		MalformedSectionHeader = 0,
		MalformedSetting = 1,
		SettingOutsideSection = 2,
		DuplicateSection = 3,
		DuplicateSetting = 4,
		InvalidPriority = 5,
		InvalidEnabled = 6,
		DuplicatePriority = 7,
		InputLimitExceeded = 8
	}

	public sealed class Witcher3ModSettingsIssue
	{
		internal Witcher3ModSettingsIssue(Witcher3ModSettingsIssueKind kind, int lineNumber, string section, string message)
		{
			Kind = kind;
			LineNumber = lineNumber;
			Section = section;
			Message = message;
		}

		public Witcher3ModSettingsIssueKind Kind { get; }
		public int LineNumber { get; }
		public string Section { get; }
		public string Message { get; }
	}

	/// <summary>Parsed metadata, not a replacement for its original text. Unknown keys are retained.</summary>
	public sealed class Witcher3ModSettingsValue
	{
		internal Witcher3ModSettingsValue(string key, string value, int lineNumber)
		{
			Key = key;
			Value = value;
			LineNumber = lineNumber;
		}

		public string Key { get; }
		public string Value { get; }
		public int LineNumber { get; }
	}

	public sealed class Witcher3ModSettingsSection
	{
		internal Witcher3ModSettingsSection(string name, int lineNumber,
			IEnumerable<Witcher3ModSettingsValue> values, bool uniqueName,
			bool? enabled, int? priority, string vortexKey)
		{
			Name = name;
			LineNumber = lineNumber;
			Values = new ReadOnlyCollection<Witcher3ModSettingsValue>(values.ToList());
			HasUniqueName = uniqueName;
			Enabled = enabled;
			Priority = priority;
			VortexKey = vortexKey;
			IsModFolder = IsModFolderName(name);
			HasEnabledField = Values.Any(x => KeyEquals(x.Key, "Enabled"));
			HasPriorityField = Values.Any(x => KeyEquals(x.Key, "Priority"));
		}

		public string Name { get; }
		public int LineNumber { get; }
		public ReadOnlyCollection<Witcher3ModSettingsValue> Values { get; }
		public bool HasUniqueName { get; }
		/// <summary>Only 0/1 is interpreted. Null means absent, invalid or duplicated; never means disabled.</summary>
		public bool? Enabled { get; }
		/// <summary>Null means absent, invalid or duplicated. Lower values have documented higher precedence in 4.x.</summary>
		public int? Priority { get; }
		public bool HasEnabledField { get; }
		public bool HasPriorityField { get; }
		/// <summary>Vortex-private association metadata; NEVER used to infer NMM native ownership.</summary>
		public string VortexKey { get; }
		/// <summary>Only well-formed mod* sections are eligible for the game-folder order view. Other sections remain in the document.</summary>
		public bool IsModFolder { get; }

		private static bool KeyEquals(string left, string right) =>
			String.Equals(left, right, StringComparison.OrdinalIgnoreCase);

		internal static bool IsModFolderName(string name)
		{
			if (String.IsNullOrWhiteSpace(name) || name.Length <= 3 ||
				!name.StartsWith("mod", StringComparison.OrdinalIgnoreCase) ||
				name == "." || name == ".." || name.EndsWith(".", StringComparison.Ordinal) ||
				name.EndsWith(" ", StringComparison.Ordinal)) return false;
			foreach (char c in name)
				if (Char.IsControl(c) || "\\/:*?\"<>|".IndexOf(c) >= 0) return false;
			return true;
		}
	}

	/// <summary>
	/// A lossless, read-only snapshot of the text supplied by the caller. OriginalText retains every
	/// comment, blank line, unknown field, separator, and line ending; parsed values are advisory.
	/// No serializer or writer is exposed by this type.
	/// </summary>
	public sealed class Witcher3ModSettingsDocument
	{
		internal Witcher3ModSettingsDocument(string originalText,
			IEnumerable<Witcher3ModSettingsSection> sections,
			IEnumerable<Witcher3ModSettingsIssue> issues, bool complete)
		{
			OriginalText = originalText;
			Sections = new ReadOnlyCollection<Witcher3ModSettingsSection>(sections.ToList());
			Issues = new ReadOnlyCollection<Witcher3ModSettingsIssue>(issues.ToList());
			IsComplete = complete;
		}

		public string OriginalText { get; }
		public ReadOnlyCollection<Witcher3ModSettingsSection> Sections { get; }
		public ReadOnlyCollection<Witcher3ModSettingsIssue> Issues { get; }
		public bool IsComplete { get; }
		/// <summary>False if any ambiguity is detected; a clean parse still does NOT prove in-game order semantics.</summary>
		public bool IsUnambiguous => IsComplete && Issues.Count == 0;
		public IEnumerable<Witcher3ModSettingsSection> ModSections => Sections.Where(x => x.IsModFolder);
	}

	/// <summary>
	/// Conservative INI-style interpretation of Witcher 3 mods.settings. No file I/O, writes, sorting,
	/// automatic fixes, default-enabled inference, Vortex-key ownership inference, or edition assumptions.
	/// </summary>
	public static class Witcher3ModSettingsParser
	{
		public const int MaxCharacters = 1024 * 1024;
		public const int MaxLines = 50000;
		private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

		private sealed class PendingSection
		{
			internal string Name;
			internal int Line;
			internal readonly List<Witcher3ModSettingsValue> Values = new List<Witcher3ModSettingsValue>();
		}

		public static Witcher3ModSettingsDocument Parse(string text)
		{
			if (text == null) throw new ArgumentNullException(nameof(text));
			var issues = new List<Witcher3ModSettingsIssue>();
			var sections = new List<PendingSection>();
			if (text.Length > MaxCharacters)
			{
				Issue(issues, Witcher3ModSettingsIssueKind.InputLimitExceeded, 0, null,
					"The settings text exceeds the read-only parser limit.");
				return new Witcher3ModSettingsDocument(text, new Witcher3ModSettingsSection[0], issues, false);
			}

			PendingSection current = null;
			int lineNumber = 0;
			int position = 0;
			while (position < text.Length)
			{
				if (++lineNumber > MaxLines)
				{
					Issue(issues, Witcher3ModSettingsIssueKind.InputLimitExceeded, lineNumber, null,
						"The settings file exceeds the read-only line count limit.");
					return new Witcher3ModSettingsDocument(text, new Witcher3ModSettingsSection[0], issues, false);
				}
				int end = position;
				while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
				string line = text.Substring(position, end - position).Trim();
				position = end < text.Length && text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n'
					? end + 2 : Math.Min(end + 1, text.Length);
				if (lineNumber == 1 && line.Length > 0 && line[0] == '\uFEFF') line = line.Substring(1).TrimStart();
				if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
				if (line[0] == '[')
				{
					int close = line.IndexOf(']');
					string tail = close < 0 ? null : line.Substring(close + 1).TrimStart();
					string name = close <= 1 ? null : line.Substring(1, close - 1).Trim();
					if (String.IsNullOrWhiteSpace(name) || tail == null ||
						(tail.Length > 0 && tail[0] != ';' && tail[0] != '#'))
					{
						Issue(issues, Witcher3ModSettingsIssueKind.MalformedSectionHeader, lineNumber, null,
							"Malformed section header; later lines are not assigned to a previous section.");
						current = null;
						continue;
					}
					current = new PendingSection { Name = name, Line = lineNumber };
					sections.Add(current);
					continue;
				}
				int equal = line.IndexOf('=');
				if (equal <= 0 || String.IsNullOrWhiteSpace(line.Substring(0, Math.Max(0, equal))))
				{
					Issue(issues, Witcher3ModSettingsIssueKind.MalformedSetting, lineNumber, current?.Name,
						"Unrecognized INI line or empty setting key; original text is retained.");
					continue;
				}
				if (current == null)
				{
					Issue(issues, Witcher3ModSettingsIssueKind.SettingOutsideSection, lineNumber, null,
						"A setting appears before a valid section header; it is not interpreted.");
					continue;
				}
				current.Values.Add(new Witcher3ModSettingsValue(line.Substring(0, equal).Trim(),
					line.Substring(equal + 1).Trim(), lineNumber));
			}

			var names = sections.GroupBy(x => x.Name, Names)
				.ToDictionary(x => x.Key, x => x.Count(), Names);
			var published = new List<Witcher3ModSettingsSection>();
			foreach (PendingSection section in sections)
			{
				if (names[section.Name] > 1)
					Issue(issues, Witcher3ModSettingsIssueKind.DuplicateSection, section.Line, section.Name,
						"The section name appears more than once (case-insensitive); its order cannot be resolved.");
				foreach (var duplicate in section.Values.GroupBy(x => x.Key, Names).Where(x => x.Count() > 1))
					Issue(issues, Witcher3ModSettingsIssueKind.DuplicateSetting, duplicate.First().LineNumber,
						section.Name, "A setting key is repeated; no value is selected for that key.");

				int? priority = null;
				bool? enabled = null;
				string vortexKey = null;
				var priorityValues = section.Values.Where(x => Names.Equals(x.Key, "Priority")).ToList();
				if (priorityValues.Count == 1)
				{
					int parsed;
					if (Int32.TryParse(priorityValues[0].Value, NumberStyles.AllowLeadingSign,
						CultureInfo.InvariantCulture, out parsed)) priority = parsed;
					else Issue(issues, Witcher3ModSettingsIssueKind.InvalidPriority,
						priorityValues[0].LineNumber, section.Name, "Priority must be an unambiguous signed integer.");
				}
				var enabledValues = section.Values.Where(x => Names.Equals(x.Key, "Enabled")).ToList();
				if (enabledValues.Count == 1)
				{
					if (enabledValues[0].Value == "1") enabled = true;
					else if (enabledValues[0].Value == "0") enabled = false;
					else Issue(issues, Witcher3ModSettingsIssueKind.InvalidEnabled,
							enabledValues[0].LineNumber, section.Name, "Enabled must be 0 or 1; no state is assumed.");
				}
				var vortexValues = section.Values.Where(x => Names.Equals(x.Key, "VK")).ToList();
				if (vortexValues.Count == 1) vortexKey = vortexValues[0].Value;
				published.Add(new Witcher3ModSettingsSection(section.Name, section.Line, section.Values,
					names[section.Name] == 1, enabled, priority, vortexKey));
			}
			foreach (var conflict in published.Where(x => x.IsModFolder && x.Priority.HasValue)
				.GroupBy(x => x.Priority.Value).Where(x => x.Count() > 1))
				foreach (Witcher3ModSettingsSection section in conflict)
					Issue(issues, Witcher3ModSettingsIssueKind.DuplicatePriority, section.LineNumber, section.Name,
						"Multiple mod folders claim the same Priority; the game winner is not inferred.");
			return new Witcher3ModSettingsDocument(text, published, issues, true);
		}

		private static void Issue(ICollection<Witcher3ModSettingsIssue> issues, Witcher3ModSettingsIssueKind kind,
			int line, string section, string message)
		{
			issues.Add(new Witcher3ModSettingsIssue(kind, line, section, message));
		}
	}
}
