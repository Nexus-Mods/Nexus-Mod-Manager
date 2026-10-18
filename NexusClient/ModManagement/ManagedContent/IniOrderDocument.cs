using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Nexus.Client.Games.ManagedContent;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Literal section identity and numeric order/enabled keys for a bounded INI order codec.</summary>
	public sealed class IniOrderFormat
	{
		/// <summary>Validates format data; it contains no game path, edition policy or file-write capability.</summary>
		public IniOrderFormat(string componentPrefix, string priorityKey, string enabledKey)
		{
			string[] parts;
			if (!ManagedContentPath.TryParse(componentPrefix, out parts) || parts.Length != 1)
				throw new ArgumentException("A safe literal component prefix is required.", nameof(componentPrefix));
			if (!IsKey(priorityKey) || !IsKey(enabledKey) || String.Equals(priorityKey, enabledKey, StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Priority and enabled keys must be distinct literal identifiers of at most 64 characters.");
			ComponentPrefix = componentPrefix;
			PriorityKey = priorityKey;
			EnabledKey = enabledKey;
		}

		public string ComponentPrefix { get; }
		public string PriorityKey { get; }
		public string EnabledKey { get; }

		/// <summary>Preserves the characterized folder-section grammar with an explicit, game-independent prefix.</summary>
		public bool IsComponentName(string name)
		{
			if (String.IsNullOrWhiteSpace(name) || name.Length <= ComponentPrefix.Length ||
				!name.StartsWith(ComponentPrefix, StringComparison.OrdinalIgnoreCase) ||
				name == "." || name == ".." || name.EndsWith(".", StringComparison.Ordinal) ||
				name.EndsWith(" ", StringComparison.Ordinal)) return false;
			foreach (char c in name)
				if (Char.IsControl(c) || "\\/:*?\"<>|".IndexOf(c) >= 0) return false;
			return true;
		}

		/// <summary>Accepts literal INI identifiers without separators, whitespace, comments or executable patterns.</summary>
		private static bool IsKey(string key)
		{
			return !String.IsNullOrEmpty(key) && key.Length <= 64 && key.All(c =>
				(c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.');
		}
	}

	/// <summary>Ambiguities and bounds encountered while interpreting the supplied order document.</summary>
	public enum IniOrderIssueKind
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

	/// <summary>One immutable parse diagnostic, including its original text position.</summary>
	public sealed class IniOrderIssue
	{
		/// <summary>Captures a diagnostic without altering or discarding source text.</summary>
		internal IniOrderIssue(IniOrderIssueKind kind, int lineNumber, string section, string message)
		{
			Kind = kind;
			LineNumber = lineNumber;
			Section = section;
			Message = message;
		}

		public IniOrderIssueKind Kind { get; }
		public int LineNumber { get; }
		public string Section { get; }
		public string Message { get; }
	}

	/// <summary>Parsed metadata, not a replacement for its original text. Unknown keys are retained.</summary>
	public sealed class IniOrderValue
	{
		/// <summary>Captures a key/value and original line number.</summary>
		internal IniOrderValue(string key, string value, int lineNumber)
		{
			Key = key;
			Value = value;
			LineNumber = lineNumber;
		}

		public string Key { get; }
		public string Value { get; }
		public int LineNumber { get; }
	}

	/// <summary>One section and its interpreted numeric keys; unknown values remain available verbatim.</summary>
	public sealed class IniOrderSection
	{
		/// <summary>Freezes parsed values and derives only the configured component/key metadata.</summary>
		internal IniOrderSection(string name, int lineNumber,
			IEnumerable<IniOrderValue> values, bool uniqueName,
			bool? enabled, int? priority, IniOrderFormat format)
		{
			Name = name;
			LineNumber = lineNumber;
			Values = new ReadOnlyCollection<IniOrderValue>(values.ToList());
			HasUniqueName = uniqueName;
			Enabled = enabled;
			Priority = priority;
			IsComponent = format.IsComponentName(name);
			HasEnabledField = Values.Any(x => KeyEquals(x.Key, format.EnabledKey));
			HasPriorityField = Values.Any(x => KeyEquals(x.Key, format.PriorityKey));
		}

		public string Name { get; }
		public int LineNumber { get; }
		public ReadOnlyCollection<IniOrderValue> Values { get; }
		public bool HasUniqueName { get; }
		/// <summary>Only 0/1 is interpreted. Null means absent, invalid or duplicated; never means disabled.</summary>
		public bool? Enabled { get; }
		/// <summary>Null means absent, invalid or duplicated. Precedence is game policy, not inferred by the document codec.</summary>
		public int? Priority { get; }
		public bool HasEnabledField { get; }
		public bool HasPriorityField { get; }
		/// <summary>Only safe sections matching the configured prefix are component-order candidates.</summary>
		public bool IsComponent { get; }

		/// <summary>Compares INI keys using the characterized case-insensitive grammar.</summary>
		private static bool KeyEquals(string left, string right) =>
			String.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	}

	/// <summary>
	/// A lossless, read-only snapshot of the text supplied by the caller. OriginalText retains every
	/// comment, blank line, unknown field, separator, and line ending; parsed values are advisory.
	/// No serializer or writer is exposed by this type.
	/// </summary>
	public sealed class IniOrderDocument
	{
		/// <summary>Freezes the complete source text and detached interpretation.</summary>
		internal IniOrderDocument(string originalText,
			IEnumerable<IniOrderSection> sections,
			IEnumerable<IniOrderIssue> issues, bool complete, IniOrderFormat format)
		{
			Format = format;
			OriginalText = originalText;
			Sections = new ReadOnlyCollection<IniOrderSection>(sections.ToList());
			Issues = new ReadOnlyCollection<IniOrderIssue>(issues.ToList());
			IsComplete = complete;
		}

		public IniOrderFormat Format { get; }
		public string OriginalText { get; }
		public ReadOnlyCollection<IniOrderSection> Sections { get; }
		public ReadOnlyCollection<IniOrderIssue> Issues { get; }
		public bool IsComplete { get; }
		/// <summary>False if any ambiguity is detected; a clean parse does not establish game-effective order semantics.</summary>
		public bool IsUnambiguous => IsComplete && Issues.All(issue => issue.Kind == IniOrderIssueKind.DuplicatePriority);
		public IEnumerable<IniOrderSection> ComponentSections => Sections.Where(x => x.IsComponent);
	}

	/// <summary>
	/// Conservative, lossless INI order interpretation with literal configured keys and component prefixes.
	/// No filesystem access, write authorization, default-enabled inference or game-specific precedence assumptions.
	/// </summary>
	public static class IniOrderParser
	{
		public const int MaxCharacters = 1024 * 1024;
		public const int MaxLines = 50000;
		private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

		/// <summary>Accumulates values for one section while parsing.</summary>
		private sealed class PendingSection
		{
			internal string Name;
			internal int Line;
			internal readonly List<IniOrderValue> Values = new List<IniOrderValue>();
		}

		/// <summary>Interprets bounded source text without normalizing or rewriting it.</summary>
		public static IniOrderDocument Parse(string text, IniOrderFormat format)
		{
			if (text == null) throw new ArgumentNullException(nameof(text));
			if (format == null) throw new ArgumentNullException(nameof(format));
			var issues = new List<IniOrderIssue>();
			var sections = new List<PendingSection>();
			if (text.Length > MaxCharacters)
			{
				Issue(issues, IniOrderIssueKind.InputLimitExceeded, 0, null,
					"The settings text exceeds the read-only parser limit.");
				return new IniOrderDocument(text, new IniOrderSection[0], issues, false, format);
			}

			PendingSection current = null;
			int lineNumber = 0;
			int position = 0;
			while (position < text.Length)
			{
				if (++lineNumber > MaxLines)
				{
					Issue(issues, IniOrderIssueKind.InputLimitExceeded, lineNumber, null,
						"The settings file exceeds the read-only line count limit.");
					return new IniOrderDocument(text, new IniOrderSection[0], issues, false, format);
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
						Issue(issues, IniOrderIssueKind.MalformedSectionHeader, lineNumber, null,
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
					Issue(issues, IniOrderIssueKind.MalformedSetting, lineNumber, current?.Name,
						"Unrecognized INI line or empty setting key; original text is retained.");
					continue;
				}
				if (current == null)
				{
					Issue(issues, IniOrderIssueKind.SettingOutsideSection, lineNumber, null,
						"A setting appears before a valid section header; it is not interpreted.");
					continue;
				}
				current.Values.Add(new IniOrderValue(line.Substring(0, equal).Trim(),
					line.Substring(equal + 1).Trim(), lineNumber));
			}

			var names = sections.GroupBy(x => x.Name, Names)
				.ToDictionary(x => x.Key, x => x.Count(), Names);
			var published = new List<IniOrderSection>();
			foreach (PendingSection section in sections)
			{
				if (names[section.Name] > 1)
					Issue(issues, IniOrderIssueKind.DuplicateSection, section.Line, section.Name,
						"The section name appears more than once (case-insensitive); its order cannot be resolved.");
				foreach (var duplicate in section.Values.GroupBy(x => x.Key, Names).Where(x => x.Count() > 1))
					Issue(issues, IniOrderIssueKind.DuplicateSetting, duplicate.First().LineNumber,
						section.Name, "A setting key is repeated; no value is selected for that key.");

				int? priority = null;
				bool? enabled = null;
				var priorityValues = section.Values.Where(x => Names.Equals(x.Key, format.PriorityKey)).ToList();
				if (priorityValues.Count == 1)
				{
					int parsed;
					if (Int32.TryParse(priorityValues[0].Value, NumberStyles.AllowLeadingSign,
						CultureInfo.InvariantCulture, out parsed)) priority = parsed;
					else Issue(issues, IniOrderIssueKind.InvalidPriority,
						priorityValues[0].LineNumber, section.Name, format.PriorityKey + " must be an unambiguous signed integer.");
				}
				var enabledValues = section.Values.Where(x => Names.Equals(x.Key, format.EnabledKey)).ToList();
				if (enabledValues.Count == 1)
				{
					if (enabledValues[0].Value == "1") enabled = true;
					else if (enabledValues[0].Value == "0") enabled = false;
					else Issue(issues, IniOrderIssueKind.InvalidEnabled,
							enabledValues[0].LineNumber, section.Name, format.EnabledKey + " must be 0 or 1; no state is assumed.");
				}
				published.Add(new IniOrderSection(section.Name, section.Line, section.Values,
					names[section.Name] == 1, enabled, priority, format));
			}
			foreach (var conflict in published.Where(x => x.IsComponent && x.Priority.HasValue)
				.GroupBy(x => x.Priority.Value).Where(x => x.Count() > 1))
				foreach (IniOrderSection section in conflict)
					Issue(issues, IniOrderIssueKind.DuplicatePriority, section.LineNumber, section.Name,
						"Multiple component sections claim the same " + format.PriorityKey + "; the game winner is not inferred.");
			return new IniOrderDocument(text, published, issues, true, format);
		}

		/// <summary>Appends a diagnostic at an original line/section location.</summary>
		private static void Issue(ICollection<IniOrderIssue> issues, IniOrderIssueKind kind,
			int line, string section, string message)
		{
			issues.Add(new IniOrderIssue(kind, line, section, message));
		}
	}
}
