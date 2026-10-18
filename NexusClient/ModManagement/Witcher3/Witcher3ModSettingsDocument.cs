using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement.ManagedContent;

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

		/// <summary>Preserves Witcher's existing safe mod-folder selection policy through its fixed codec format.</summary>
		internal static bool IsModFolderName(string name) => Witcher3ModSettingsParser.Format.IsComponentName(name);

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

	/// <summary>Preserves Witcher's fixed mods.settings grammar over the shared lossless INI codec.</summary>
	public static class Witcher3ModSettingsParser
	{
		public const int MaxCharacters = IniOrderParser.MaxCharacters;
		public const int MaxLines = IniOrderParser.MaxLines;
		internal static readonly IniOrderFormat Format = new IniOrderFormat("mod", "Priority", "Enabled");

		/// <summary>Interprets the existing Witcher keys while keeping Vortex metadata in the compatibility model.</summary>
		public static Witcher3ModSettingsDocument Parse(string text)
		{
			return FromSharedDocument(IniOrderParser.Parse(text, Format));
		}

		/// <summary>Retains Witcher's public interpretation when the shared bounded reader already parsed its fixed format.</summary>
		internal static Witcher3ModSettingsDocument FromSharedDocument(IniOrderDocument document)
		{
			return new Witcher3ModSettingsDocument(document.OriginalText,
				document.Sections.Select(section => new Witcher3ModSettingsSection(section.Name, section.LineNumber,
					section.Values.Select(value => new Witcher3ModSettingsValue(value.Key, value.Value, value.LineNumber)),
					section.HasUniqueName, section.Enabled, section.Priority, GetVortexKey(section))),
				document.Issues.Select(issue => new Witcher3ModSettingsIssue((Witcher3ModSettingsIssueKind)issue.Kind,
					issue.LineNumber, issue.Section, issue.Kind == IniOrderIssueKind.DuplicatePriority
						? "Multiple mod folders claim the same Priority; the game winner is not inferred." : issue.Message)), document.IsComplete);
		}

		/// <summary>Retains the unique VK association value without using it as native ownership evidence.</summary>
		private static string GetVortexKey(IniOrderSection section)
		{
			var values = section.Values.Where(value => String.Equals(value.Key, "VK", StringComparison.OrdinalIgnoreCase)).ToList();
			return values.Count == 1 ? values[0].Value : null;
		}
	}
}
