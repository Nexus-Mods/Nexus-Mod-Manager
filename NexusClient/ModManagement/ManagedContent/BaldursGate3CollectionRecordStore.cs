using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Newtonsoft.Json.Linq;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Owns a BG3 Collection's ordered module settings through the existing native value history.</summary>
	internal sealed class BaldursGate3CollectionRecordStore : IStructuredRecordStore
	{
		internal const string Prefix = "bg3-collection-order-v1|";
		internal const string Identity = "collection-order";
		private readonly StructuredXmlRecordRule _rule;
		private readonly StructuredXmlRecordStore _records;
		private readonly string _root;

		/// <summary>Keeps legacy module keys delegated to the configured record provider.</summary>
		internal BaldursGate3CollectionRecordStore(StructuredXmlRecordRule rule)
			: this(rule, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), rule.ProfileRoot))
		{
		}

		/// <summary>Supports an isolated profile tree without changing the configured production root.</summary>
		internal BaldursGate3CollectionRecordStore(StructuredXmlRecordRule rule, string root)
		{
			_rule = rule;
			_root = root;
			_records = new StructuredXmlRecordStore(rule, root);
		}

		/// <summary>Builds one reversible ordered value for each existing game profile.</summary>
		internal IEnumerable<BasicInstallGameSpecificValue> BuildPlan(JArray order, ISet<string> selectedPackages = null, bool scopeToSelectedPackages = false)
		{
			string root = _root;
			if (!Directory.Exists(root)) yield break;
			foreach (string directory in Directory.GetDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
			{
				string profile = Path.GetFileName(directory);
				if (_rule.ExcludedProfiles.Contains(profile, StringComparer.OrdinalIgnoreCase)) continue;
				string path = GetManifestPath(profile);
				if (!File.Exists(path)) continue;
				XmlDocument document = Load(path);
				XmlElement mods = Container(document, "Mods");
				XmlNode children = mods.SelectSingleNode("children");
				if (children == null) children = mods.AppendChild(document.CreateElement("children"));
				var protectedIds = new HashSet<string>(children.ChildNodes.OfType<XmlElement>().Where(node => IsProtected(node))
					.Select(node => node.SelectSingleNode("attribute[@id='UUID']")?.Attributes?["value"]?.Value).Where(id => id != null), StringComparer.OrdinalIgnoreCase);
				var uuids = new HashSet<string>(order.OfType<JObject>().Where(entry => (bool?)entry["data"]?["isListed"] != true &&
					(!scopeToSelectedPackages || selectedPackages == null || selectedPackages.Contains(Path.GetFileName(((string)entry["id"] ?? String.Empty).Replace('/', '\\')))))
					.Select(entry => (string)entry["data"]?["uuid"]).Where(id => !protectedIds.Contains(id)), StringComparer.OrdinalIgnoreCase);
				foreach (XmlNode node in children.ChildNodes.Cast<XmlNode>().ToArray())
					if (uuids.Contains(node.SelectSingleNode("attribute[@id='UUID']")?.Attributes?["value"]?.Value)) children.RemoveChild(node);
				XmlElement legacyOrder = Container(document, "ModOrder");
				XmlNode orderChildren = legacyOrder?.SelectSingleNode("children");
				if (legacyOrder != null && orderChildren == null) orderChildren = legacyOrder.AppendChild(document.CreateElement("children"));
				if (orderChildren != null)
					foreach (XmlNode node in orderChildren.ChildNodes.Cast<XmlNode>().ToArray())
						if (uuids.Contains(node.SelectSingleNode("attribute[@id='UUID']")?.Attributes?["value"]?.Value)) orderChildren.RemoveChild(node);
				foreach (JObject entry in order.OfType<JObject>())
				{
					JObject data = (JObject)entry["data"];
					// As in Vortex, built-in/listed packages are not written as user modules.
					if (!(bool)entry["enabled"] || (bool?)data["isListed"] == true || protectedIds.Contains((string)data["uuid"]) ||
						(selectedPackages != null && !selectedPackages.Contains(Path.GetFileName(((string)entry["id"] ?? String.Empty).Replace('/', '\\'))))) continue;
					XmlElement module = document.CreateElement("node"); module.SetAttribute("id", "ModuleShortDesc");
					Attribute(document, module, "Folder", "LSString", (string)data["folder"]);
					Attribute(document, module, "MD5", "LSString", (string)data["md5"] ?? String.Empty);
					Attribute(document, module, "Name", "LSString", (string)data["name"]);
					Attribute(document, module, "PublishHandle", "uint64", "0");
					Attribute(document, module, "UUID", "guid", (string)data["uuid"]);
					Attribute(document, module, "Version64", "int64", data["version"].ToString());
					children.AppendChild(module);
					if (orderChildren != null)
					{
						XmlElement ordered = document.CreateElement("node"); ordered.SetAttribute("id", "Module");
						Attribute(document, ordered, "UUID", "FixedString", (string)data["uuid"]); orderChildren.AppendChild(ordered);
					}
				}
				// A curator order wins over previous positions; unrelated modules keep their relative order afterwards.
				foreach (XmlElement container in new[] { mods, legacyOrder }.Where(item => item != null))
				{
					XmlNode list = container.SelectSingleNode("children");
					XmlNode[] selected = list.ChildNodes.Cast<XmlNode>().Where(node => uuids.Contains(node.SelectSingleNode("attribute[@id='UUID']")?.Attributes?["value"]?.Value)).ToArray();
					foreach (XmlNode node in selected) list.RemoveChild(node);
					XmlNode anchor = list.ChildNodes.OfType<XmlElement>().FirstOrDefault(node => node.Name == "node" && !IsProtected(node) &&
						!protectedIds.Contains(node.SelectSingleNode("attribute[@id='UUID']")?.Attributes?["value"]?.Value));
					foreach (XmlNode node in selected) if (anchor == null) list.AppendChild(node); else list.InsertBefore(node, anchor);
				}
				yield return new BasicInstallGameSpecificValue(Prefix + profile + "|" + Identity, Capture(document));
			}
		}

		/// <summary>Recognizes the Collection namespace alongside existing individual-module keys.</summary>
		public bool IsSupportedKey(string key)
		{
			if (key == null || !key.StartsWith(Prefix, StringComparison.Ordinal)) return _records.IsSupportedKey(key);
			try { string profile, identity; ParseKey(key, out profile, out identity); return true; }
			catch (ArgumentException) { return false; }
		}

		/// <summary>Parses only safe profile names and the fixed aggregate identity.</summary>
		public void ParseKey(string key, out string profile, out string identity)
		{
			if (!key.StartsWith(Prefix, StringComparison.Ordinal)) { _records.ParseKey(key, out profile, out identity); return; }
			string[] parts = key.Substring(Prefix.Length).Split('|');
			if (parts.Length != 2 || parts[1] != Identity) throw new ArgumentException("Invalid BG3 Collection order key.");
			profile = StructuredXmlRecordRule.Segment(parts[0]); identity = Identity;
		}

		/// <summary>Uses the configured profile location and its existing native path validation.</summary>
		public string GetManifestPath(string profile) => _records.GetManifestPath(profile);

		/// <summary>Checks only the two BG3 module containers represented by an aggregate value.</summary>
		public void ValidatePayload(string identity, byte[] value)
		{
			if (identity != Identity) { _records.ValidatePayload(identity, value); return; }
			XmlDocument payload = LoadBytes(value);
			if (payload.DocumentElement?.Name != "bg3-order" || Container(payload, "Mods") == null ||
				payload.DocumentElement.ChildNodes.OfType<XmlElement>().Any(node => node.Name != "node" || (node.GetAttribute("id") != "Mods" && node.GetAttribute("id") != "ModOrder")))
				throw new InvalidDataException("Invalid BG3 Collection order payload.");
		}

		/// <summary>Captures only module settings; unrelated game settings remain outside this value.</summary>
		public bool TryReadValue(string path, string identity, out byte[] value)
		{
			if (identity != Identity) return _records.TryReadValue(path, identity, out value);
			value = Capture(Load(path)); return true;
		}

		/// <summary>Replaces module containers after the native installer snapshots the file.</summary>
		public void WriteValue(string path, string identity, byte[] value)
		{
			if (identity != Identity) { _records.WriteValue(path, identity, value); return; }
			ValidatePayload(identity, value);
			XmlDocument document = Load(path); XmlDocument payload = LoadBytes(value);
			XmlElement mods = Container(document, "Mods"); XmlNode parent = mods.ParentNode;
			foreach (string name in new[] { "Mods", "ModOrder" })
			{
				XmlElement current = Container(document, name); XmlElement desired = Container(payload, name);
				if (current != null) parent.RemoveChild(current);
				if (desired != null) parent.AppendChild(document.ImportNode(desired, true));
			}
			using (XmlWriter writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true, IndentChars = "\t" })) document.Save(writer);
		}

		/// <summary>Aggregate edits always retain an original value; ordinary record removal remains unchanged.</summary>
		public void RemoveValue(string path, string identity)
		{
			if (identity == Identity) throw new InvalidDataException("A BG3 Collection order requires its retained original value for removal.");
			_records.RemoveValue(path, identity);
		}

		/// <summary>Changes only declared curator members, retaining current game records and unrelated modules on replay/removal.</summary>
		internal static byte[] MergeManaged(byte[] current, byte[] desired, IEnumerable<string> managed)
		{
			var identities = new HashSet<string>(managed, StringComparer.OrdinalIgnoreCase);
			XmlDocument result = LoadBytes(current), source = LoadBytes(desired);
			XmlElement gameModules = Container(result, "Mods");
			if (gameModules == null) throw new InvalidDataException("The current native module container is missing.");
			identities.ExceptWith(gameModules.SelectNodes("children/node").OfType<XmlElement>().Where(IsProtected)
				.Select(node => node.SelectSingleNode("attribute[@id='UUID']")?.Attributes?["value"]?.Value));
			foreach (string name in new[] { "Mods", "ModOrder" })
			{
				XmlElement target = Container(result, name), incoming = Container(source, name);
				if ((target == null) != (incoming == null)) throw new InvalidDataException("Collection replay cannot migrate the native order format.");
				if (target == null) continue;
				XmlNode list = target.SelectSingleNode("children"), additions = incoming.SelectSingleNode("children");
				if (list == null || additions == null) throw new InvalidDataException("The native order container has no children.");
				foreach (XmlElement node in list.ChildNodes.OfType<XmlElement>().Where(node => !IsProtected(node) && identities.Contains(node.SelectSingleNode("attribute[@id='UUID']")?.Attributes?["value"]?.Value)).ToArray()) list.RemoveChild(node);
				foreach (XmlElement node in additions.ChildNodes.OfType<XmlElement>().Where(node => !IsProtected(node) && identities.Contains(node.SelectSingleNode("attribute[@id='UUID']")?.Attributes?["value"]?.Value)))
					list.AppendChild(result.ImportNode(node, true));
			}
			return Capture(result);
		}

		/// <summary>Identifies retained game-owned records independently of curator metadata.</summary>
		private static bool IsProtected(XmlElement node) => new[] { "Gustav", "GustavDev", "GustavX", "Shared", "SharedDev" }
			.Contains(node.SelectSingleNode("attribute[@id='Folder']")?.Attributes?["value"]?.Value, StringComparer.OrdinalIgnoreCase);

		/// <summary>Finds one literal module-settings container.</summary>
		private static XmlElement Container(XmlDocument document, string id) => document.SelectSingleNode("//node[@id='" + id + "']") as XmlElement;

		/// <summary>Serializes module containers in a stable compact native-value representation.</summary>
		private static byte[] Capture(XmlDocument document)
		{
			var payload = new XmlDocument(); payload.AppendChild(payload.CreateElement("bg3-order"));
			foreach (string name in new[] { "Mods", "ModOrder" })
			{
				XmlElement node = Container(document, name);
				if (node != null) payload.DocumentElement.AppendChild(payload.ImportNode(node, true));
			}
			return Encoding.UTF8.GetBytes(payload.OuterXml);
		}

		/// <summary>Loads game XML without external entity resolution.</summary>
		private static XmlDocument Load(string path)
		{
			var document = new XmlDocument { XmlResolver = null };
			using (XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
			if (Container(document, "Mods") == null) throw new InvalidDataException("BG3 modsettings.lsx has no Mods container.");
			return document;
		}

		/// <summary>Reads the retained XML fragment without allowing external entities.</summary>
		private static XmlDocument LoadBytes(byte[] value)
		{
			var document = new XmlDocument { XmlResolver = null };
			using (var stream = new MemoryStream(value, false))
			using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
			return document;
		}

		/// <summary>Writes literal BG3 LSX attributes using XML escaping.</summary>
		private static void Attribute(XmlDocument document, XmlElement node, string id, string type, string value)
		{
			XmlElement attribute = document.CreateElement("attribute"); attribute.SetAttribute("id", id);
			attribute.SetAttribute("type", type); attribute.SetAttribute("value", value); node.AppendChild(attribute);
		}
	}
}
