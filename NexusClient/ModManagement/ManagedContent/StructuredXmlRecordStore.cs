using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Record operations consumed by the native game-value owner stack, with filesystem snapshots owned by its caller.</summary>
	public interface IStructuredRecordStore
	{
		/// <summary>Parses a durable native key without accepting file paths from that key.</summary>
		void ParseKey(string key, out string profile, out string identity);
		/// <summary>Recognizes only keys belonging to this record provider.</summary>
		bool IsSupportedKey(string key);
		/// <summary>Resolves an existing profile manifest location.</summary>
		string GetManifestPath(string profile);
		/// <summary>Checks identity and bounded record bytes.</summary>
		void ValidatePayload(string identity, byte[] value);
		/// <summary>Reads one current physical record as native payload bytes.</summary>
		bool TryReadValue(string path, string identity, out byte[] value);
		/// <summary>Writes one record after the caller snapshots the manifest.</summary>
		void WriteValue(string path, string identity, byte[] value);
		/// <summary>Removes one record after the caller snapshots the manifest.</summary>
		void RemoveValue(string path, string identity);
	}

	/// <summary>Bounded GUID-keyed XML attribute records under existing profile manifests; never creates missing game profiles.</summary>
	public sealed class StructuredXmlRecordStore : IStructuredRecordStore
	{
		private const int MaxPayloadBytes = 256 * 1024;
		private const int MaxMetadataBytes = 1024 * 1024;
		private const int MaxModules = 2048;
		private const int MaxProfiles = 256;
		private const int MaxPlannedValues = 8192;
		private const long MaxPlannedBytes = 16 * 1024 * 1024;
		private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
		private readonly StructuredXmlRecordRule _rule;
		private readonly string _root;

		/// <summary>Resolves the configured relative profile location under the existing user's LocalAppData directory.</summary>
		public StructuredXmlRecordStore(StructuredXmlRecordRule rule)
			: this(rule, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				(rule ?? throw new ArgumentNullException(nameof(rule))).ProfileRoot))
		{
		}

		/// <summary>Allows an explicit fixture root without changing the production LocalAppData location policy.</summary>
		internal StructuredXmlRecordStore(StructuredXmlRecordRule rule, string profilesRoot)
		{
			_rule = rule ?? throw new ArgumentNullException(nameof(rule));
			if (String.IsNullOrWhiteSpace(profilesRoot) || !Path.IsPathRooted(profilesRoot)) throw new ArgumentException("An absolute profile root is required.", nameof(profilesRoot));
			_root = Path.GetFullPath(profilesRoot);
		}

		/// <summary>Finds only the literal root metadata file; archive wrappers cannot silently expand the capability.</summary>
		public string MetadataPath(IMod mod)
		{
			string[] paths = (mod?.GetFileList() ?? new List<string>()).Where(path => String.Equals(path, _rule.MetadataFile, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
			if (paths.Length > 1) throw new InvalidDataException("The archive has duplicate structured metadata files.");
			return paths.FirstOrDefault();
		}

		/// <summary>Plans per-profile native keys and exact record bytes without changing files or ownership.</summary>
		public IReadOnlyList<BasicInstallGameSpecificValue> BuildPlan(IMod mod)
		{
			if (mod == null) throw new ArgumentNullException(nameof(mod));
			string source = MetadataPath(mod);
			if (source == null) return Array.AsReadOnly(new BasicInstallGameSpecificValue[0]);
			byte[] bytes = ReadMetadata(mod, source);
			JObject metadata;
			try
			{
				string decoded = _rule.MetadataEncoding == "Ascii" ? Encoding.ASCII.GetString(bytes) : Utf8.GetString(bytes);
				if (_rule.MetadataEncoding == "Utf8" && decoded.Length > 0 && decoded[0] == '\uFEFF') decoded = decoded.Substring(1);
				using (var text = new StringReader(decoded))
				using (var json = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
				{
					metadata = JObject.Load(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
					if (json.Read()) throw new InvalidDataException("Trailing structured record metadata is not supported.");
				}
			}
			catch (Exception error) when (error is JsonException || error is DecoderFallbackException)
			{
				throw new InvalidDataException("Structured record metadata could not be decoded.", error);
			}
			JToken raw = Property(metadata, _rule.MetadataArray);
			if (raw == null || raw.Type == JTokenType.Null) throw new InvalidDataException("Structured metadata is missing its configured record array.");
			JArray records = raw as JArray;
			if (records == null || records.Count > MaxModules) throw new InvalidDataException("Structured record metadata requires a bounded record array.");
			var payloads = new List<KeyValuePair<string, byte[]>>();
			var identities = new HashSet<string>(StringComparer.Ordinal);
			foreach (JToken token in records)
			{
				if (token.Type == JTokenType.Null) continue;
				JObject record = token as JObject;
				if (record == null) throw new InvalidDataException("Each structured record must be a metadata object.");
				XmlDocument document = new XmlDocument { XmlResolver = null };
				XmlElement node = document.CreateElement("node");
				node.SetAttribute("id", _rule.RecordId);
				foreach (StructuredXmlRecordField field in _rule.Fields)
				{
					string value = field.Value;
					foreach (string alias in field.Sources)
					{
						JToken found = Property(record, alias);
						if (found == null || found.Type == JTokenType.Null) continue;
						if (found.Type != JTokenType.String && found.Type != JTokenType.Integer) throw new InvalidDataException("XML field metadata must be a string or integer.");
						value = found.ToString(); break;
					}
					XmlElement attribute = document.CreateElement("attribute");
					attribute.SetAttribute("id", field.Id); attribute.SetAttribute("type", field.Type);
					attribute.SetAttribute("value", value ?? field.DefaultValue ?? String.Empty); node.AppendChild(attribute);
				}
				string id = NormalizeIdentity(ReadIdentity(node));
				if (!identities.Add(id)) throw new InvalidDataException("Structured metadata contains duplicate record identities.");
				payloads.Add(new KeyValuePair<string, byte[]>(id, Encode(node)));
			}
			var result = new List<BasicInstallGameSpecificValue>();
			try
			{
				if ((File.GetAttributes(_root) & FileAttributes.Directory) == 0) throw new InvalidDataException("The configured profile root is not a directory.");
			}
			catch (FileNotFoundException) { return result.AsReadOnly(); }
			catch (DirectoryNotFoundException) { return result.AsReadOnly(); }
			long plannedBytes = 0;
			DirectoryInfo[] profiles = new DirectoryInfo(_root).EnumerateDirectories().Where(profile =>
				!_rule.ExcludedProfiles.Contains(profile.Name, StringComparer.OrdinalIgnoreCase)).Take(MaxProfiles + 1)
				.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ThenBy(profile => profile.Name, StringComparer.Ordinal).ToArray();
			if (profiles.Length > MaxProfiles) throw new InvalidDataException("The structured profile set exceeds its bounded limit.");
			foreach (DirectoryInfo profile in profiles)
			{
				string path = GetManifestPath(profile.Name);
				if (!File.Exists(path)) continue;
				XmlNode children = Children(Load(path));
				foreach (KeyValuePair<string, byte[]> record in payloads)
				{
					XmlElement existing = FindRecord(children, record.Key);
					byte[] desired = existing == null ? record.Value : Encode(existing);
					plannedBytes += desired.Length;
					if (result.Count == MaxPlannedValues || plannedBytes > MaxPlannedBytes) throw new InvalidDataException("The structured effect plan exceeds its aggregate bounds.");
					result.Add(new BasicInstallGameSpecificValue(CreateKey(profile.Name, record.Key), desired));
				}
			}
			return result.AsReadOnly();
		}

		/// <summary>Bounds archive metadata reads before allocating the decoded document.</summary>
		private static byte[] ReadMetadata(IMod mod, string source)
		{
			using (FileStream input = mod.GetFileStream(source))
			{
				if (input == null || input.Length == 0 || input.Length > MaxMetadataBytes) throw new InvalidDataException("Structured record metadata exceeds its bounded input contract.");
				using (var output = new MemoryStream())
				{
					var buffer = new byte[8192]; int count;
					while ((count = input.Read(buffer, 0, Math.Min(buffer.Length, MaxMetadataBytes - (int)output.Length + 1))) > 0)
					{
						output.Write(buffer, 0, count);
						if (output.Length > MaxMetadataBytes) throw new InvalidDataException("Structured record metadata grew beyond its bounded input contract.");
					}
					return output.ToArray();
				}
			}
		}

		/// <summary>Reads a literal metadata property and rejects case aliases that would make identity selection ambiguous.</summary>
		private static JToken Property(JObject source, string name)
		{
			List<JProperty> matches = source.Properties().Where(property => String.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
			if (matches.Count > 1) throw new InvalidDataException("Metadata has ambiguous property casing: " + name);
			return matches.Count == 0 ? null : matches[0].Value;
		}

		/// <summary>Preserves a stable native namespace and canonical GUID spelling.</summary>
		private string CreateKey(string profile, string identity) => _rule.KeyPrefix + StructuredXmlRecordRule.Segment(profile) + "|" + NormalizeIdentity(identity);

		/// <summary>Accepts only this configured native namespace and a safe profile plus GUID.</summary>
		public void ParseKey(string key, out string profile, out string identity)
		{
			if (String.IsNullOrEmpty(key) || !key.StartsWith(_rule.KeyPrefix, StringComparison.Ordinal)) throw new ArgumentException("Unsupported structured record key.");
			string[] parts = key.Substring(_rule.KeyPrefix.Length).Split('|');
			if (parts.Length != 2) throw new ArgumentException("Malformed structured record key.");
			profile = StructuredXmlRecordRule.Segment(parts[0]); identity = NormalizeIdentity(parts[1]);
		}

		/// <summary>Identifies keys for native uninstall/upgrade without guessing another provider's namespace.</summary>
		public bool IsSupportedKey(string key)
		{
			try { string profile, id; ParseKey(key, out profile, out id); return true; }
			catch (Exception error) when (error is ArgumentException || error is InvalidDataException) { return false; }
		}

		/// <summary>Resolves only the configured existing profile/manifest and rejects observed linked descendants.</summary>
		public string GetManifestPath(string profile)
		{
			string directory = Path.Combine(_root, StructuredXmlRecordRule.Segment(profile));
			string path = Path.Combine(directory, _rule.ManifestFile);
			CheckPathType(directory, true);
			CheckPathType(path, false);
			LoadOrder.NativeLoadOrderService.RequireNoPendingManifest(path);
			return path;
		}

		/// <summary>Preserves missing-file handling while rejecting links, type conflicts and inaccessible path evidence.</summary>
		private static void CheckPathType(string path, bool directory)
		{
			try
			{
				FileAttributes attributes = File.GetAttributes(path);
				if ((attributes & FileAttributes.ReparsePoint) != 0 || ((attributes & FileAttributes.Directory) != 0) != directory)
					throw new InvalidDataException("Structured profile/manifest paths require direct directories and ordinary files.");
			}
			catch (FileNotFoundException) { }
			catch (DirectoryNotFoundException) { }
		}

		/// <summary>Validates a bounded exact-record payload against the identity carried by its native key.</summary>
		public void ValidatePayload(string identity, byte[] value)
		{
			XmlElement node = ParsePayload(value);
			if (NormalizeIdentity(ReadIdentity(node)) != NormalizeIdentity(identity)) throw new InvalidDataException("The structured payload identity differs from its native key.");
		}

		/// <summary>Reads only the selected record, preserving unknown attributes/children in its native payload.</summary>
		public bool TryReadValue(string path, string identity, out byte[] value)
		{
			XmlElement record = FindRecord(Children(Load(path)), identity);
			value = record == null ? null : Encode(record); return record != null;
		}

		/// <summary>Replaces or appends the selected record; the native caller must snapshot the complete file first.</summary>
		public void WriteValue(string path, string identity, byte[] value)
		{
			ValidatePayload(identity, value);
			XmlDocument document = Load(path); XmlNode children = Children(document); XmlElement existing = FindRecord(children, identity);
			XmlNode imported = document.ImportNode(ParsePayload(value), true);
			if (existing == null) children.AppendChild(imported); else children.ReplaceChild(imported, existing);
			Save(document, path);
		}

		/// <summary>Removes only the selected record, leaving unrelated records and game-owned configuration intact.</summary>
		public void RemoveValue(string path, string identity)
		{
			XmlDocument document = Load(path); XmlNode children = Children(document); XmlElement existing = FindRecord(children, identity);
			if (existing == null) return;
			children.RemoveChild(existing); Save(document, path);
		}

		/// <summary>Loads existing XML with bounded characters and no external entities or DTD execution.</summary>
		private static XmlDocument Load(string path)
		{
			var document = new XmlDocument { XmlResolver = null };
			using (XmlReader reader = XmlReader.Create(path, ReaderSettings())) document.Load(reader);
			return document;
		}

		/// <summary>Uses literal discriminator data rather than allowing JSON-supplied XPath expressions.</summary>
		private XmlNode Children(XmlDocument document)
		{
			List<XmlElement> containers = document.GetElementsByTagName("*").Cast<XmlNode>().OfType<XmlElement>()
				.Where(element => element.GetAttribute("id") == _rule.ContainerId).Take(2).ToList();
			if (containers.Count != 1) throw new InvalidDataException("The structured manifest requires one unambiguous configured container.");
			XmlElement[] children = containers[0].ChildNodes.OfType<XmlElement>().Where(element => element.Name == "children").Take(2).ToArray();
			if (children.Length != 1) throw new InvalidDataException("The structured container requires one children element.");
			return children[0];
		}

		/// <summary>Rejects duplicate matching identities instead of guessing which record can be replaced.</summary>
		private XmlElement FindRecord(XmlNode children, string identity)
		{
			string id = NormalizeIdentity(identity); XmlElement found = null; int count = 0;
			foreach (XmlElement record in children.ChildNodes.OfType<XmlElement>().Where(node => node.Name == "node" && node.GetAttribute("id") == _rule.RecordId))
			{
				if (++count > MaxModules) throw new InvalidDataException("The manifest record count exceeds the bounded limit.");
				string recordIdentity = ReadIdentity(record); Guid parsed;
				if (!Guid.TryParse(recordIdentity, out parsed)) throw new InvalidDataException("A manifest record has no valid GUID identity.");
				if (parsed.ToString("D").ToLowerInvariant() != id) continue;
				if (found != null) throw new InvalidDataException("The manifest contains duplicate selected record identities.");
				found = record;
			}
			return found;
		}

		/// <summary>Requires exactly one identity field while preserving all other XML fields.</summary>
		private string ReadIdentity(XmlElement node)
		{
			List<XmlElement> fields = node.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "attribute" && child.GetAttribute("id") == _rule.IdentityField).Take(2).ToList();
			if (fields.Count != 1) throw new InvalidDataException("A structured record requires one unambiguous identity field.");
			return fields[0].GetAttribute("value");
		}

		/// <summary>Parses only a bounded UTF-8 record of the configured literal type.</summary>
		private XmlElement ParsePayload(byte[] value)
		{
			if (value == null || value.Length == 0 || value.Length > MaxPayloadBytes) throw new InvalidDataException("Invalid structured record payload size.");
			var document = new XmlDocument { XmlResolver = null };
			using (var text = new StringReader(Utf8.GetString(value)))
			using (XmlReader reader = XmlReader.Create(text, ReaderSettings())) document.Load(reader);
			XmlElement node = document.DocumentElement;
			if (node == null || node.Name != "node" || node.GetAttribute("id") != _rule.RecordId) throw new InvalidDataException("The payload is not the configured XML record type.");
			NormalizeIdentity(ReadIdentity(node)); return node;
		}

		/// <summary>Retains the existing compact UTF-8 native payload representation.</summary>
		private static byte[] Encode(XmlElement node)
		{
			byte[] bytes = Utf8.GetBytes(node.OuterXml);
			if (bytes.Length == 0 || bytes.Length > MaxPayloadBytes) throw new InvalidDataException("Structured record payload exceeds the bounded limit.");
			return bytes;
		}

		/// <summary>Retains the established manifest serialization while native snapshots retain exact rollback bytes.</summary>
		private static void Save(XmlDocument document, string path)
		{
			using (XmlWriter writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true, IndentChars = "\t" })) document.Save(writer);
		}

		/// <summary>Bounds XML decoding and prohibits external resolution.</summary>
		private static XmlReaderSettings ReaderSettings() => new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 };
		/// <summary>Uses the existing canonical GUID spelling for durable native keys.</summary>
		private static string NormalizeIdentity(string identity)
		{
			Guid parsed;
			if (!Guid.TryParse(identity, out parsed)) throw new InvalidDataException("Structured record metadata requires a GUID identity.");
			return parsed.ToString("D").ToLowerInvariant();
		}
	}
}
