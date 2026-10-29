using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>One observed native module; protected game records cannot be removed or moved by a draft.</summary>
	public sealed class NativeLoadOrderEntry
	{
		/// <summary>Captures XML-backed display metadata without conflating inclusion with physical activation.</summary>
		internal NativeLoadOrderEntry(string identity, string name, string folder, bool isProtected)
		{
			Identity = identity; Name = name; Folder = folder; IsProtected = isProtected;
		}
		public string Identity { get; }
		public string Name { get; }
		public string Folder { get; }
		public bool IsProtected { get; }
	}

	/// <summary>Bounded LSX observation preserving original bytes, unknown fields and game-owned campaign records.</summary>
	public sealed class LarianLoadOrderDocument
	{
		internal const int MaximumBytes = 8 * 1024 * 1024;
		internal readonly byte[] Bytes;
		internal readonly XmlDocument Xml;
		internal readonly XmlElement Mods;
		internal readonly XmlElement LegacyOrder;
		internal readonly Dictionary<string, XmlElement> Records;
		internal readonly bool Legacy;

		/// <summary>Parses a complete existing manifest; ambiguous structures and unsupported formats are never silently rewritten.</summary>
		internal LarianLoadOrderDocument(string path, byte[] bytes)
		{
			FilePath = Path.GetFullPath(path); Bytes = (byte[])bytes.Clone();
			if (bytes.Length == 0 || bytes.Length > MaximumBytes) throw new InvalidDataException("Invalid load-order file size.");
			Xml = Parse(bytes);
			XmlElement version = Xml.DocumentElement?.SelectSingleNode("version") as XmlElement;
			int major, minor;
			if (version == null || !Int32.TryParse(version.GetAttribute("major"), out major) || !Int32.TryParse(version.GetAttribute("minor"), out minor) ||
				major != 4 || minor < 0 || minor > 8) throw new InvalidDataException("Unsupported LSX version. The original file was not changed.");
			Legacy = minor < 7;
			Mods = Container(Xml, "Mods", true);
			LegacyOrder = Container(Xml, "ModOrder", Legacy);
			Records = new Dictionary<string, XmlElement>(StringComparer.OrdinalIgnoreCase);
			var entries = new List<NativeLoadOrderEntry>();
			foreach (XmlElement record in Children(Mods).ChildNodes.OfType<XmlElement>().Where(node => node.Name == "node" && node.GetAttribute("id") == "ModuleShortDesc"))
			{
				if (entries.Count >= 10000) throw new InvalidDataException("Too many module records.");
				string folder = Field(record, "Folder");
				bool gameRecord = new[] { "Gustav", "GustavDev", "GustavX", "Shared", "SharedDev" }.Contains(folder, StringComparer.OrdinalIgnoreCase);
				string rawId = Field(record, "UUID");
				string identity = gameRecord && String.IsNullOrEmpty(rawId) ? "game:" + folder : Identity(rawId);
				if (Records.ContainsKey(identity)) throw new InvalidDataException("Duplicate native module identity.");
				Records.Add(identity, record);
				entries.Add(new NativeLoadOrderEntry(identity, Field(record, "Name") ?? folder, folder, gameRecord));
			}
			if (!entries.Any(entry => entry.IsProtected && entry.Folder.StartsWith("Gustav", StringComparison.OrdinalIgnoreCase)))
				throw new InvalidDataException("The native campaign record is missing. Run the game or restore a known valid manifest before editing.");
			if (Legacy)
			{
				var ordered = new List<NativeLoadOrderEntry>();
				var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (XmlElement node in Children(LegacyOrder).ChildNodes.OfType<XmlElement>().Where(node => node.Name == "node" && node.GetAttribute("id") == "Module"))
				{
					string id = Identity(Field(node, "UUID"));
					if (!seen.Add(id) || !Records.ContainsKey(id)) throw new InvalidDataException("Legacy ModOrder has duplicate or unresolved UUIDs.");
					ordered.Add(entries.Single(entry => entry.Identity == id));
				}
				// The record list and legacy order must agree; choosing one silently could enable or drop a mod.
				if (entries.Where(entry => !entry.IsProtected).Any(entry => !seen.Contains(entry.Identity)))
					throw new InvalidDataException("Legacy Mods and ModOrder disagree. Repair the source before editing.");
				entries = entries.Where(entry => entry.IsProtected).Concat(ordered.Where(entry => !entry.IsProtected)).ToList();
			}
			Entries = Array.AsReadOnly(entries.ToArray());
			Sha256 = Hash(Bytes);
		}
		public string FilePath { get; }
		public string Sha256 { get; }
		public IReadOnlyList<NativeLoadOrderEntry> Entries { get; }
		public bool UsesLegacyModOrder => Legacy;

		/// <summary>Reads one bounded ordinary file without creating missing profiles or following a manifest link.</summary>
		public static LarianLoadOrderDocument Read(string path) => new LarianLoadOrderDocument(path, ReadBytes(path, MaximumBytes));

		/// <summary>Reads bounded bytes and rejects directories and observed links.</summary>
		internal static byte[] ReadBytes(string path, int maximum)
		{
			if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
				throw new InvalidDataException("An ordinary existing file is required.");
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				if (stream.Length == 0 || stream.Length > maximum) throw new InvalidDataException("File exceeds its bounded size.");
				byte[] bytes = new byte[stream.Length]; int read = 0, count;
				while (read < bytes.Length && (count = stream.Read(bytes, read, bytes.Length - read)) != 0) read += count;
				if (read != bytes.Length || stream.ReadByte() != -1) throw new IOException("The file changed during reading.");
				return bytes;
			}
		}

		/// <summary>Parses LSX without external entities, retaining element data and comments.</summary>
		internal static XmlDocument Parse(byte[] bytes)
		{
			var document = new XmlDocument { XmlResolver = null };
			using (var stream = new MemoryStream(bytes, false))
			using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes })) document.Load(reader);
			return document;
		}

		/// <summary>Resolves only containers directly beneath the unique ModuleSettings root.</summary>
		internal static XmlElement Container(XmlDocument document, string id, bool required)
		{
			XmlNodeList roots = document.SelectNodes("/save/region[@id='ModuleSettings']/node[@id='root']/children");
			if (roots.Count != 1) throw new InvalidDataException("The ModuleSettings root is missing or ambiguous.");
			XmlElement[] matches = roots[0].ChildNodes.OfType<XmlElement>().Where(node => node.Name == "node" && node.GetAttribute("id") == id).ToArray();
			if (matches.Length > 1 || (required && matches.Length != 1)) throw new InvalidDataException("A native module container is missing or ambiguous.");
			return matches.SingleOrDefault();
		}

		/// <summary>Requires exactly one children container instead of silently creating or choosing an ambiguous structure.</summary>
		internal static XmlElement Children(XmlElement node)
		{
			XmlElement[] children = node.ChildNodes.OfType<XmlElement>().Where(element => element.Name == "children").ToArray();
			if (children.Length != 1) throw new InvalidDataException("A native module container needs one children element.");
			return children[0];
		}

		/// <summary>Reads only a direct, unambiguous LSX field.</summary>
		internal static string Field(XmlElement node, string id)
		{
			XmlElement[] fields = node.ChildNodes.OfType<XmlElement>().Where(element => element.Name == "attribute" && element.GetAttribute("id") == id).ToArray();
			if (fields.Length > 1) throw new InvalidDataException("Duplicate LSX field: " + id);
			return fields.SingleOrDefault()?.GetAttribute("value");
		}

		/// <summary>Canonicalizes module UUIDs used by native and package records.</summary>
		internal static string Identity(string value)
		{
			Guid identity;
			if (!Guid.TryParse(value, out identity) || identity == Guid.Empty) throw new InvalidDataException("A native module requires a valid UUID.");
			return identity.ToString("D");
		}

		/// <summary>Produces exact-byte identities for review, publication and recovery.</summary>
		internal static string Hash(byte[] bytes)
		{
			using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", String.Empty);
		}

		/// <summary>Serializes a detached draft using the existing native XML writer style.</summary>
		internal static byte[] Serialize(XmlDocument document)
		{
			using (var stream = new MemoryStream())
			{
				using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, IndentChars = "\t" })) document.Save(writer);
				return stream.ToArray();
			}
		}
	}
}
