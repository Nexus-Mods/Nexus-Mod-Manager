using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>Lossless folder settings and individually owned Witcher menu records for native Collection recipes.</summary>
	internal sealed class Witcher3CollectionRecordStore : IStructuredRecordStore
	{
		internal const string Prefix = "witcher3-record-v1|";
		private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
		private readonly string _gameRoot;
		private readonly string _settingsPath;

		/// <summary>Binds records to configured game/Documents locations, never paths supplied by a Collection.</summary>
		internal Witcher3CollectionRecordStore(string gameRoot, string settingsPath)
		{
			_gameRoot = Path.GetFullPath(gameRoot);
			_settingsPath = Path.GetFullPath(settingsPath);
		}

		/// <summary>Encodes literal record identities into a durable native ownership key.</summary>
		internal static string Key(string file, params string[] identity)
		{
			file = file.Replace('/', '\\').ToLowerInvariant();
			identity = (string[])identity.Clone();
			if (IsDocumentSettings(file) && identity.Length == 3 && identity[0] == "ini")
			{
				// Windows paths and INI keys are case-insensitive; one logical value must have one native owner stack.
				identity[1] = identity[1].ToLowerInvariant();
				identity[2] = file == "mods.settings" && identity[2].Equals("Priority", StringComparison.OrdinalIgnoreCase) ? "Priority" :
					file == "mods.settings" && identity[2].Equals("Enabled", StringComparison.OrdinalIgnoreCase) ? "Enabled" : identity[2].ToLowerInvariant();
			}
			return Prefix + Encode(file) + "|" + Encode(String.Join("|", identity.Select(Encode)));
		}

		/// <summary>Encodes one identity segment without path or separator ambiguity.</summary>
		private static string Encode(string value) { return Convert.ToBase64String(Utf8.GetBytes(value)); }

		/// <summary>Decodes one bounded identity segment.</summary>
		private static string Decode(string value)
		{
			if (value.Length > 16384) throw new InvalidDataException("Witcher record identity is too long.");
			try { return Utf8.GetString(Convert.FromBase64String(value)); }
			catch (Exception ex) when (ex is FormatException || ex is DecoderFallbackException) { throw new InvalidDataException("Invalid Witcher record identity.", ex); }
		}

		/// <summary>Parses only this provider's canonical key grammar.</summary>
		public void ParseKey(string key, out string profile, out string identity)
		{
			if (key == null || !key.StartsWith(Prefix, StringComparison.Ordinal)) throw new InvalidDataException("Unknown Witcher record key.");
			string[] parts = key.Substring(Prefix.Length).Split('|');
			if (parts.Length != 2) throw new InvalidDataException("Invalid Witcher record key.");
			profile = Decode(parts[0]); identity = Decode(parts[1]);
			ValidateProfile(profile);
			string[] fields = Fields(identity);
			if (IsDocumentSettings(profile) && (fields.Length != 3 || fields[0] != "ini")) throw new InvalidDataException("Invalid Witcher settings record.");
			if (profile == "mods.settings" && (fields[2] != "Priority" && fields[2] != "Enabled"))
				throw new InvalidDataException("Invalid Witcher order record.");
			if (profile == "mods.settings") CollectionWitcher3Data.RequireFolderName(fields[1], "mod");
			if (IsDocumentSettings(profile) && (String.IsNullOrEmpty(fields[1]) || fields[1].IndexOfAny(new[] { '[', ']', '\r', '\n', '\0' }) >= 0 ||
				String.IsNullOrEmpty(fields[2]) || fields[2].IndexOfAny(new[] { '=', '\r', '\n', '\0' }) >= 0)) throw new InvalidDataException("Invalid Witcher INI section or key.");
			if (!IsDocumentSettings(profile) && !((fields.Length == 3 && fields[0] == "attribute") || (fields.Length == 4 && fields[0] == "variable") ||
				(fields.Length == 2 && fields[0] == "line"))) throw new InvalidDataException("Invalid Witcher menu record.");
			if (fields[0] == "variable" && fields[2] != "VisibleVars" && fields[2] != "HiddenVars") throw new InvalidDataException("Unsupported Witcher variable container.");
			if (!IsDocumentSettings(profile) && (fields[0] == "line") != Path.GetExtension(profile).Equals(".txt", StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("Witcher menu record type does not match its file.");
			if (!String.Equals(key, Key(profile, fields), StringComparison.Ordinal)) throw new InvalidDataException("Noncanonical Witcher record key.");
		}

		/// <summary>Recognizes supported native record keys without throwing on other providers.</summary>
		public bool IsSupportedKey(string key)
		{
			try { string file, identity; ParseKey(key, out file, out identity); return true; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException) { return false; }
		}

		/// <summary>Resolves only mods.settings or safe files in the game's menu configuration matrix.</summary>
		public string GetManifestPath(string profile)
		{
			ValidateProfile(profile);
			string result;
			if (profile == "mods.settings") result = _settingsPath;
			else if (IsDocumentSettings(profile)) result = Path.Combine(Path.GetDirectoryName(_settingsPath), profile);
			else
			{
				result = Path.Combine(_gameRoot, profile);
			}
			RequireRegularAncestors(result);
			return result;
		}

		/// <summary>Checks the literal record namespace without reading physical paths during capability detection.</summary>
		private static void ValidateProfile(string profile)
		{
			if (IsDocumentSettings(profile)) return;
			string safe = CollectionWitcher3Data.ValidateDestination(profile);
			if (!CollectionWitcher3Data.IsMatrixPath(safe) || (!Path.GetExtension(safe).Equals(".xml", StringComparison.OrdinalIgnoreCase) && !Path.GetExtension(safe).Equals(".txt", StringComparison.OrdinalIgnoreCase)))
				throw new InvalidDataException("Unsupported Witcher menu record file.");
		}

		/// <summary>Rejects links at every existing ancestor before reading or writing a native record.</summary>
		internal static void RequireRegularAncestors(string path)
		{
			for (string cursor = Path.GetFullPath(path); !String.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
			{
				try { if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Witcher record path traverses a reparse point."); }
				catch (FileNotFoundException) { }
				catch (DirectoryNotFoundException) { }
			}
		}

		/// <summary>Checks bounded scalar/variable payloads before their native ownership history is changed.</summary>
		public void ValidatePayload(string identity, byte[] value)
		{
			if (value == null || value.Length > 1024 * 1024) throw new InvalidDataException("Witcher record payload exceeds its bound.");
			string[] fields = Fields(identity);
			string text = Utf8.GetString(value);
			if (fields[0] == "variable")
			{
				XElement variable = ReadXml(value).Root;
				if (variable == null || variable.Name != "Var" || (string)variable.Attribute("id") != fields[3]) throw new InvalidDataException("Witcher menu variable identity changed.");
			}
			else if (text.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new InvalidDataException("Witcher scalar record contains multiple lines.");
		}

		/// <summary>Decodes a record's semantic identity fields.</summary>
		private static string[] Fields(string identity) { return identity.Split('|').Select(Decode).ToArray(); }

		/// <summary>Reads current logical bytes for independent native verification and original-value backups.</summary>
		public bool TryReadValue(string path, string identity, out byte[] value)
		{
			value = null;
			RequireRegularAncestors(path);
			if (!File.Exists(path)) return false;
			string[] fields = Fields(identity);
			string text = null;
			if (fields[0] == "ini")
			{
				IniOrderTextEncoding encoding;
				IniOrderDocument document = ReadOrder(path, out encoding);
				IniOrderSection section = document.Sections.SingleOrDefault(item => String.Equals(item.Name, fields[1], StringComparison.OrdinalIgnoreCase));
				text = section?.Values.SingleOrDefault(item => String.Equals(item.Key, fields[2], StringComparison.OrdinalIgnoreCase))?.Value;
			}
			else if (fields[0] == "line") text = ReadText(path).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Contains(fields[1], StringComparer.Ordinal) ? fields[1] : null;
			else
			{
				XElement group = FindGroup(ReadXml(File.ReadAllBytes(path)), fields[1]);
				if (fields[0] == "attribute") text = (string)group?.Attribute(fields[2]);
				else
				{
					XElement variable = group?.Element(fields[2])?.Elements("Var").SingleOrDefault(item => (string)item.Attribute("id") == fields[3]);
					text = variable?.ToString(SaveOptions.DisableFormatting);
				}
			}
			if (text == null) return false;
			value = Utf8.GetBytes(text); return true;
		}

		/// <summary>Applies one reviewed record; the caller owns transaction snapshots and native history.</summary>
		public void WriteValue(string path, string identity, byte[] value) { ValidatePayload(identity, value); Change(path, identity, value); }

		/// <summary>Removes only one native-owned record while retaining unrelated settings and menu variables.</summary>
		public void RemoveValue(string path, string identity) { if (File.Exists(path)) Change(path, identity, null); }

		/// <summary>Changes the requested logical record without replacing an entire user's settings or menu document.</summary>
		private void Change(string path, string identity, byte[] value)
		{
			RequireRegularAncestors(path);
			string[] fields = Fields(identity);
			if (fields[0] == "ini") { ChangeOrder(path, fields, value == null ? null : Utf8.GetString(value)); return; }
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			if (fields[0] == "line")
			{
				IniOrderTextEncoding encoding = IniOrderTextEncoding.Utf8;
				string text = File.Exists(path) ? ReadText(path, out encoding) : String.Empty;
				var lines = IniOrderTextEdits.SplitLines(text);
				var removals = new HashSet<int>(lines.Select((line, index) => new { line, index }).Where(item => item.line.Text == fields[1]).Select(item => item.index + 1));
				if (value == null) text = IniOrderTextEdits.Render(lines, new Dictionary<int, string>(), new Dictionary<int, List<string>>(), removals);
				else if (removals.Count == 0) text += (text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal) && !text.EndsWith("\r", StringComparison.Ordinal) ? "\r\n" : String.Empty) + fields[1] + "\r\n";
				File.WriteAllBytes(path, IniOrderByteCodec.Encode(text, encoding)); return;
			}
			XDocument xml = File.Exists(path) ? ReadXml(File.ReadAllBytes(path)) : new XDocument(new XElement("UserConfig"));
			XElement group = FindGroup(xml, fields[1]);
			if (group == null)
			{
				if (value == null) return;
				group = new XElement("Group", new XAttribute("id", fields[1])); xml.Root.Add(group);
			}
			if (fields[0] == "attribute") group.SetAttributeValue(fields[2], value == null ? null : Utf8.GetString(value));
			else
			{
				XElement container = group.Element(fields[2]);
				if (container == null) { if (value == null) return; container = new XElement(fields[2]); group.Add(container); }
				XElement old = container.Elements("Var").SingleOrDefault(item => (string)item.Attribute("id") == fields[3]);
				if (value == null) old?.Remove();
				else { XElement replacement = ReadXml(value).Root; if (old == null) container.Add(replacement); else old.ReplaceWith(replacement); }
			}
			using (var writer = XmlWriter.Create(path, new XmlWriterSettings { Encoding = new UnicodeEncoding(false, true), Indent = true })) xml.Save(writer);
		}

		/// <summary>Performs a bounded, lossless INI key update with the existing shared order codec.</summary>
		private static void ChangeOrder(string path, string[] fields, string value)
		{
			IniOrderTextEncoding encoding;
			IniOrderDocument document = ReadOrder(path, out encoding);
			List<IniOrderSourceLine> lines = IniOrderTextEdits.SplitLines(document.OriginalText);
			IniOrderSection section = document.Sections.SingleOrDefault(item => String.Equals(item.Name, fields[1], StringComparison.OrdinalIgnoreCase));
			var replacements = new Dictionary<int, string>(); var additions = new Dictionary<int, List<string>>(); var removals = new HashSet<int>();
			string output;
			if (section == null)
			{
				if (value == null) return;
				string newline = IniOrderTextEdits.PickNewline(lines, lines.Count - 1);
				output = document.OriginalText + (document.OriginalText.Length > 0 && !document.OriginalText.EndsWith("\n", StringComparison.Ordinal) && !document.OriginalText.EndsWith("\r", StringComparison.Ordinal) ? newline : String.Empty) + "[" + fields[1] + "]" + newline + fields[2] + "=" + value + newline;
			}
			else
			{
				IniOrderValue old = section.Values.SingleOrDefault(item => String.Equals(item.Key, fields[2], StringComparison.OrdinalIgnoreCase));
				if (old != null) { if (value == null) removals.Add(old.LineNumber); else replacements.Add(old.LineNumber, IniOrderTextEdits.ReplaceValue(lines[old.LineNumber - 1].Text, value)); }
				else if (value != null) additions.Add(section.LineNumber, new List<string> { fields[2] + "=" + value });
				output = IniOrderTextEdits.Render(lines, replacements, additions, removals);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, IniOrderByteCodec.Encode(output, encoding));
		}

		/// <summary>Reads bounded order text without guessing an encoding or accepting ambiguous sections.</summary>
		private static IniOrderDocument ReadOrder(string path, out IniOrderTextEncoding encoding)
		{
			string text = String.Empty; encoding = IniOrderTextEncoding.Utf8;
			if (File.Exists(path))
			{
				if (new FileInfo(path).Length > 4 * 1024 * 1024 || !IniOrderByteCodec.TryDecode(File.ReadAllBytes(path), out text, out encoding)) throw new InvalidDataException("Witcher mods.settings has an unsupported encoding or size.");
			}
			bool ordering = Path.GetFileName(path).Equals("mods.settings", StringComparison.OrdinalIgnoreCase);
			IniOrderDocument result = IniOrderParser.Parse(text, ordering ? new IniOrderFormat("mod", "Priority", "Enabled") : SettingsFormat());
			if (!result.IsUnambiguous) throw new InvalidDataException("Witcher settings contain ambiguous sections or values; resolve these before Collection installation.");
			return result;
		}

		/// <summary>Reads bounded text input.</summary>
		private static string ReadText(string path)
		{
			IniOrderTextEncoding encoding;
			return ReadText(path, out encoding);
		}

		/// <summary>Retains the original encoding/BOM of a bounded menu list.</summary>
		private static string ReadText(string path, out IniOrderTextEncoding encoding)
		{
			if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Witcher menu list is too large.");
			string text;
			if (!IniOrderByteCodec.TryDecode(File.ReadAllBytes(path), out text, out encoding)) throw new InvalidDataException("Witcher menu list encoding is unsupported.");
			return text;
		}

		/// <summary>Parses bounded XML with external entities and DTDs disabled.</summary>
		internal static XDocument ReadXml(byte[] bytes)
		{
			string text; IniOrderTextEncoding encoding;
			if (bytes == null || bytes.Length > 16 * 1024 * 1024 || !IniOrderByteCodec.TryDecode(bytes, out text, out encoding))
				throw new InvalidDataException("Witcher menu XML has an unsupported encoding or size.");
			// Several published menus contain UTF-8 bytes with a UTF-16 declaration. Decode
			// physical bytes strictly before parsing; DTDs and external entities remain disabled.
			using (var source = new StringReader(text))
			using (var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 })) return XDocument.Load(reader);
		}

		/// <summary>Matches Vortex's shared game-menu list; custom mod menus retain their complete ordered XML.</summary>
		internal static bool RequiresMenuMerge(string path)
		{
			return Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
				new[] { "audio", "display", "gameplay", "gamma", "graphics", "graphicsdx11", "hdr", "hidden", "hud", "input", "localization" }
				.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>Preserves a complete custom menu, including presets and repeated separators, with a correct UTF-16 encoding.</summary>
		internal static byte[] PrepareMenuFile(byte[] bytes)
		{
			XDocument xml = ReadXml(bytes);
			if (xml.Root?.Name != "UserConfig") throw new InvalidDataException("Witcher menu XML must have a UserConfig root.");
			using (var output = new MemoryStream())
			{
				using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UnicodeEncoding(false, true), Indent = true, CloseOutput = false })) xml.Save(writer);
				return output.ToArray();
			}
		}

		/// <summary>Finds one unambiguous native menu group.</summary>
		private static XElement FindGroup(XDocument xml, string id)
		{
			if (xml.Root?.Name != "UserConfig") throw new InvalidDataException("Witcher menu XML must have a UserConfig root.");
			return xml.Root.Elements("Group").SingleOrDefault(item => (string)item.Attribute("id") == id);
		}

		/// <summary>Expands XML/list input into individually reversible native game-value effects.</summary>
		internal static IEnumerable<KeyValuePair<string, byte[]>> PlanMenu(string path, byte[] bytes)
		{
			if (Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
			{
				string text; IniOrderTextEncoding encoding;
				if (!IniOrderByteCodec.TryDecode(bytes, out text, out encoding)) throw new InvalidDataException("Unsupported Witcher menu list encoding.");
				foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal))
					yield return new KeyValuePair<string, byte[]>(Key(path, "line", line), Utf8.GetBytes(line));
				yield break;
			}
			XDocument xml = ReadXml(bytes);
			if (xml.Root?.Name != "UserConfig" || xml.Root.HasAttributes || xml.Root.Elements().Any(element => element.Name != "Group")) throw new InvalidDataException("Unsupported Witcher menu XML structure.");
			foreach (XElement group in xml.Root.Elements("Group"))
			{
				string id = (string)group.Attribute("id");
				if (String.IsNullOrEmpty(id)) throw new InvalidDataException("Witcher menu group has no id.");
				foreach (XAttribute attribute in group.Attributes().Where(item => item.Name != "id"))
					yield return new KeyValuePair<string, byte[]>(Key(path, "attribute", id, attribute.Name.ToString()), Utf8.GetBytes(attribute.Value));
				foreach (XElement container in group.Elements())
				{
					if ((container.Name != "VisibleVars" && container.Name != "HiddenVars") || container.Elements().Any(element => element.Name != "Var")) throw new InvalidDataException("Unsupported Witcher menu variable container.");
					foreach (XElement variable in container.Elements("Var"))
					{
						string variableId = (string)variable.Attribute("id");
						if (String.IsNullOrEmpty(variableId)) throw new InvalidDataException("Witcher menu variable has no id.");
						yield return new KeyValuePair<string, byte[]>(Key(path, "variable", id, container.Name.ToString(), variableId), Utf8.GetBytes(variable.ToString(SaveOptions.DisableFormatting)));
					}
				}
			}
		}

		/// <summary>Whitelists the game's literal Documents settings names; Collection data never supplies arbitrary paths.</summary>
		internal static bool IsDocumentSettings(string name)
		{
			return new[] { "mods.settings", "input.settings", "user.settings", "dx12user.settings" }.Contains(name, StringComparer.Ordinal);
		}

		/// <summary>Turns a Vortex settings fragment into individually reversible native INI records.</summary>
		internal static IEnumerable<KeyValuePair<string, byte[]>> PlanSettings(string name, byte[] bytes)
		{
			if (!IsDocumentSettings(name) || name == "mods.settings") throw new InvalidDataException("Unsupported Witcher Documents fragment.");
			string text; IniOrderTextEncoding encoding;
			if (!IniOrderByteCodec.TryDecode(bytes, out text, out encoding)) throw new InvalidDataException("Unsupported Witcher settings fragment encoding.");
			IniOrderDocument document = IniOrderParser.Parse(text, SettingsFormat());
			if (!document.IsUnambiguous) throw new InvalidDataException("Witcher settings fragment contains ambiguous values.");
			foreach (IniOrderSection section in document.Sections)
				foreach (IniOrderValue value in section.Values)
					yield return new KeyValuePair<string, byte[]>(Key(name, "ini", section.Name, value.Key), Utf8.GetBytes(value.Value));
		}

		/// <summary>Uses the lossless INI grammar without interpreting arbitrary settings named Enabled or Priority as folder state.</summary>
		private static IniOrderFormat SettingsFormat()
		{
			return new IniOrderFormat("__nmm_no_component__", "__nmm_unused_priority__", "__nmm_unused_enabled__");
		}
	}
}
