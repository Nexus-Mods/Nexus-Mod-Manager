using System;
using System.Collections.Generic;
using System.Text;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>One immutable source line and its exact original terminator.</summary>
	public sealed class IniOrderSourceLine
	{
		/// <summary>Captures a line split by the shared lossless renderer.</summary>
		internal IniOrderSourceLine(string text, string ending)
		{
			Text = text;
			Ending = ending;
		}

		public string Text { get; }
		public string Ending { get; }
	}

	/// <summary>Pure text mechanics for reviewed INI edits; game policy and publication remain with the caller.</summary>
	public static class IniOrderTextEdits
	{
		/// <summary>Replaces a single setting value while retaining its key, equals spacing and trailing whitespace.</summary>
		public static string ReplaceValue(string originalLine, string value)
		{
			if (originalLine == null) throw new ArgumentNullException(nameof(originalLine));
			if (value == null) throw new ArgumentNullException(nameof(value));
			int equals = originalLine.IndexOf('=');
			if (equals <= 0 || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
				throw new ArgumentException("A setting line and a single-line replacement value are required.");
			int start = equals + 1;
			while (start < originalLine.Length && Char.IsWhiteSpace(originalLine[start])) start++;
			int end = originalLine.Length;
			while (end > start && Char.IsWhiteSpace(originalLine[end - 1])) end--;
			return originalLine.Substring(0, start) + value + originalLine.Substring(end);
		}

		/// <summary>Splits source text without losing mixed CRLF/LF/CR endings or a missing final newline.</summary>
		public static List<IniOrderSourceLine> SplitLines(string text)
		{
			if (text == null) throw new ArgumentNullException(nameof(text));
			var lines = new List<IniOrderSourceLine>();
			int position = 0;
			while (position < text.Length)
			{
				int end = position;
				while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
				int suffix = end < text.Length && text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 :
					end < text.Length ? 1 : 0;
				lines.Add(new IniOrderSourceLine(text.Substring(position, end - position), text.Substring(end, suffix)));
				position = end + suffix;
			}
			return lines;
		}

		/// <summary>Renders caller-reviewed exact line edits while preserving all other source text.</summary>
		public static string Render(IList<IniOrderSourceLine> lines, IDictionary<int, string> replacements,
			IDictionary<int, List<string>> additions, ISet<int> removals = null)
		{
			if (lines == null) throw new ArgumentNullException(nameof(lines));
			if (replacements == null) throw new ArgumentNullException(nameof(replacements));
			if (additions == null) throw new ArgumentNullException(nameof(additions));
			var result = new StringBuilder();
			for (int i = 0; i < lines.Count; i++)
			{
				int lineNumber = i + 1;
				if (removals != null && removals.Contains(lineNumber)) continue;
				string replacement;
				result.Append(replacements.TryGetValue(lineNumber, out replacement) ? replacement : lines[i].Text);
				List<string> pending;
				if (!additions.TryGetValue(lineNumber, out pending))
				{
					result.Append(lines[i].Ending);
					continue;
				}

				string newline = PickNewline(lines, i);
				result.Append(lines[i].Ending.Length == 0 ? newline : lines[i].Ending);
				for (int j = 0; j < pending.Count; j++)
				{
					result.Append(pending[j]);
					if (j < pending.Count - 1 || lines[i].Ending.Length > 0) result.Append(newline);
				}
			}
			return result.ToString();
		}

		/// <summary>Selects the nearest original newline, falling back to CRLF only when none exists.</summary>
		public static string PickNewline(IList<IniOrderSourceLine> lines, int afterIndex)
		{
			if (lines == null) throw new ArgumentNullException(nameof(lines));
			for (int i = afterIndex; i >= 0; i--)
				if (lines[i].Ending.Length > 0) return lines[i].Ending;
			for (int i = afterIndex + 1; i < lines.Count; i++)
				if (lines[i].Ending.Length > 0) return lines[i].Ending;
			return "\r\n";
		}

	}
}
