using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nexus.Client.Games.ManagedContent
{
	/// <summary>Shared Windows-relative path and stable component naming rules; never accesses disk.</summary>
	public static class ManagedContentPath
	{
		/// <summary>Accepts only unambiguous relative file paths, rejecting Windows aliases and traversal.</summary>
		public static bool TryParse(string path, out string[] parts)
		{
			parts = null;
			if (String.IsNullOrWhiteSpace(path) || path.Length > 4096) return false;
			string normalized = path.Replace('/', '\\');
			if (normalized[0] == '\\' || normalized[normalized.Length - 1] == '\\') return false;
			string[] segments = normalized.Split('\\');
			foreach (string segment in segments)
			{
				if (segment.Length == 0 || segment.Length > 255 || segment == "." || segment == ".." ||
					segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal)) return false;
				foreach (char c in segment)
					if (c < ' ' || "<>:\"|?*".IndexOf(c) >= 0) return false;
				string name = segment.Split('.')[0].ToUpperInvariant();
				if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(name) ||
					(name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) ||
					name.StartsWith("LPT", StringComparison.Ordinal)) && name[3] >= '1' && name[3] <= '9')) return false;
			}
			parts = segments;
			return true;
		}

		/// <summary>Derives a bounded display stem plus a stable SHA-256 suffix, preserving the existing Witcher naming contract.</summary>
		public static string GenerateComponentName(string identity, string suggestedName, string prefix, int maximumLength)
		{
			if (String.IsNullOrWhiteSpace(identity) || String.IsNullOrWhiteSpace(suggestedName)) return null;
			var stem = new StringBuilder();
			foreach (char c in suggestedName)
			{
				if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-') stem.Append(c);
				else if (stem.Length > 0 && stem[stem.Length - 1] != '_') stem.Append('_');
			}
			string label = stem.ToString().Trim('_', '-');
			if (label.Length == 0) label = "Package";
			int available = maximumLength - prefix.Length - 11;
			if (available < 1) throw new ArgumentException("Component name limit must leave room for a stem and hash.");
			using (SHA256 sha = SHA256.Create())
			{
				string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", String.Empty).Substring(0, 10);
				return prefix + label.Substring(0, Math.Min(label.Length, available)).TrimEnd('_', '-') + "_" + hash;
			}
		}
	}
}
