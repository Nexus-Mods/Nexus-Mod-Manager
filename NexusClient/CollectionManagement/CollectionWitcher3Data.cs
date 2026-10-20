using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using Nexus.Client.Games.ManagedContent;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Validated Witcher extension data from the exact retained collection.json; never writes game files.</summary>
	internal sealed class CollectionWitcher3Data
	{
		internal const int MaximumExpandedBytes = 64 * 1024 * 1024;
		internal readonly Dictionary<string, CollectionWitcher3OrderEntry> Order = new Dictionary<string, CollectionWitcher3OrderEntry>(StringComparer.OrdinalIgnoreCase);
		internal readonly Dictionary<string, byte[]> GeneratedFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
		internal readonly Dictionary<string, byte[]> Metadata = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Recognizes only the documented Witcher extension namespace.</summary>
		internal static bool IsWitcherManifest(JObject root)
		{
			JToken domain = (root["info"] as JObject)?["domainName"];
			return domain?.Type == JTokenType.String && String.Equals((string)domain, "witcher3", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Validates the extension completely, including embedded archive paths and expansion bounds.</summary>
		internal static CollectionWitcher3Data Parse(JObject root)
		{
			if (!IsWitcherManifest(root)) throw new InvalidDataException("Witcher Collection data requires the witcher3 game domain.");
			var result = new CollectionWitcher3Data();
			JToken orderToken = root["loadOrder"];
			if (orderToken != null && orderToken.Type != JTokenType.Null)
			{
				JArray entries = orderToken as JArray;
				if (entries == null || entries.Count > 4096) throw new InvalidDataException("Witcher loadOrder must be a bounded array.");
				foreach (JToken token in entries)
				{
					JObject entry = token as JObject;
					RequireFields(entry, new[] { "id", "name", "enabled", "modId", "locked", "data" }, "loadOrder entry");
					string id = RequireString(entry["id"], "loadOrder.id");
					RequireFolderName(id, "mod");
					if (entry["enabled"]?.Type != JTokenType.Boolean) throw new InvalidDataException("Witcher loadOrder.enabled must be boolean.");
					if (entry["locked"] != null && entry["locked"].Type != JTokenType.Boolean) throw new InvalidDataException("Witcher loadOrder.locked must be boolean.");
					foreach (string name in new[] { "name", "modId" })
						if (entry[name] != null && entry[name].Type != JTokenType.Null) RequireString(entry[name], "loadOrder." + name);
					JObject data = entry["data"] as JObject;
					RequireFields(data, new[] { "prefix" }, "loadOrder.data");
					int priority;
					if (data["prefix"]?.Type != JTokenType.Integer || !Int32.TryParse(data["prefix"].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out priority) || priority == Int32.MaxValue)
						throw new InvalidDataException("Witcher loadOrder.data.prefix must be a non-negative 32-bit priority.");
					if (result.Order.ContainsKey(id)) throw new InvalidDataException("Witcher loadOrder contains a duplicate folder: " + id);
					// Vortex prefixes are zero-based; its mods.settings serializer writes prefix + 1.
					result.Order.Add(id, new CollectionWitcher3OrderEntry(priority + 1, (bool)entry["enabled"], (string)entry["modId"], (string)entry["name"]));
				}
			}
			JToken mergedToken = root["mergedData"];
			if (mergedToken != null && mergedToken.Type != JTokenType.Null)
			{
				JObject merged = mergedToken as JObject;
				RequireFields(merged, new[] { "scriptMergedData", "menuModSettingsData" }, "mergedData");
				foreach (JProperty property in merged.Properties())
				{
					if (property.Value.Type == JTokenType.Null) continue;
					string hex = RequireString(property.Value, "mergedData." + property.Name);
					if (hex.Length % 2 != 0 || hex.Length > Nexus.Client.OnlineServices.NexusMods.Collections.NexusCollectionManifestNormalizer.MaxManifestBytes)
						throw new InvalidDataException("Witcher mergedData must contain a bounded hexadecimal ZIP archive.");
					byte[] bytes = new byte[hex.Length / 2];
					for (int i = 0; i < bytes.Length; i++)
						if (!Byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
							throw new InvalidDataException("Witcher mergedData contains invalid hexadecimal data.");
					result.ReadArchive(bytes, property.Name == "scriptMergedData");
				}
			}
			byte[] savedOrder;
			if (orderToken == null && result.Metadata.TryGetValue("mods.settings", out savedOrder))
			{
				string text; IniOrderTextEncoding encoding;
				if (!IniOrderByteCodec.TryDecode(savedOrder, out text, out encoding)) throw new InvalidDataException("Bundled Witcher mods.settings has an unsupported encoding.");
				IniOrderDocument order = IniOrderParser.Parse(text, new IniOrderFormat("mod", "Priority", "Enabled"));
				if (!order.IsUnambiguous) throw new InvalidDataException("Bundled Witcher mods.settings is ambiguous.");
				foreach (IniOrderSection section in order.ComponentSections)
				{
					if (!section.Priority.HasValue || !section.Enabled.HasValue) throw new InvalidDataException("Bundled Witcher order has incomplete priority or enabled values.");
					result.Order.Add(section.Name, new CollectionWitcher3OrderEntry(section.Priority.Value, section.Enabled.Value));
				}
			}
			return result;
		}

		/// <summary>Reads bounded, regular ZIP entries without extracting or executing archive content.</summary>
		private void ReadArchive(byte[] bytes, bool scripts)
		{
			using (var stream = new MemoryStream(bytes, false))
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, false))
			{
				if (zip.Entries.Count > 4096) throw new InvalidDataException("Witcher merge archive has too many entries.");
				long total = GeneratedFiles.Values.Sum(value => (long)value.Length) + Metadata.Values.Sum(value => (long)value.Length);
				var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (ZipArchiveEntry entry in zip.Entries)
				{
					string path = entry.FullName.Replace('/', '\\');
					bool directory = path.EndsWith("\\", StringComparison.Ordinal);
					string[] parts;
					if (!ManagedContentPath.TryParse(directory ? path.TrimEnd('\\') : path, out parts) || !seen.Add(path))
						throw new InvalidDataException("Witcher merge archive contains an unsafe or duplicate path.");
					int unixType = (entry.ExternalAttributes >> 16) & 0xf000;
					if ((unixType != 0 && unixType != 0x8000 && unixType != 0x4000) || (entry.ExternalAttributes & 0x400) != 0)
						throw new InvalidDataException("Witcher merge archive links and special files are not supported.");
					if (directory) { if (entry.Length != 0) throw new InvalidDataException("Witcher merge directory contains data."); continue; }
					if (entry.Length < 0 || entry.Length > MaximumExpandedBytes || (total += entry.Length) > MaximumExpandedBytes)
						throw new InvalidDataException("Witcher merge archive exceeds the expansion limit.");
					bool metadata = parts.Length == 1 && ((scripts && new[] { ".w3edition", "MergeInventory.xml", "mods.settings" }.Contains(path, StringComparer.OrdinalIgnoreCase)) ||
						(!scripts && path.Equals("vortex_menumod.cache", StringComparison.OrdinalIgnoreCase)));
					if (!metadata)
					{
						if (scripts && parts[0].StartsWith("mod", StringComparison.OrdinalIgnoreCase) && !parts[0].Equals("Mods", StringComparison.OrdinalIgnoreCase)) path = "Mods\\" + path;
						bool document = !scripts && parts.Length == 1 && Nexus.Client.ModManagement.Witcher3.Witcher3CollectionRecordStore.IsDocumentSettings(path.ToLowerInvariant()) && !path.Equals("mods.settings", StringComparison.OrdinalIgnoreCase);
						path = document ? path.ToLowerInvariant() : ValidateDestination(path);
						if (scripts && !path.StartsWith("Mods\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Script merge payload must be inside a mod folder.");
						if (!scripts && !document && !IsMatrixPath(path)) throw new InvalidDataException("Menu merge payload must target the Witcher configuration matrix or a known Documents settings file.");
					}
					byte[] content;
					using (Stream input = entry.Open())
					using (var output = new MemoryStream())
					{
						byte[] buffer = new byte[8192];
						int read;
						while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
						{
							if (output.Length + read > entry.Length) throw new InvalidDataException("Witcher merge archive entry exceeds its declared size.");
							output.Write(buffer, 0, read);
						}
						if (output.Length != entry.Length) throw new InvalidDataException("Witcher merge archive entry is truncated.");
						content = output.ToArray();
					}
					Dictionary<string, byte[]> destination = metadata ? Metadata : GeneratedFiles;
					if (destination.ContainsKey(path)) throw new InvalidDataException("Witcher merge archives contain conflicting outputs.");
					destination.Add(path, content);
				}
			}
		}

		/// <summary>Allows canonical Mods/DLC/bin files and passive root helper scripts; never game executable replacement.</summary>
		internal static string ValidateDestination(string path)
		{
			string[] parts;
			if (!ManagedContentPath.TryParse(path, out parts)) throw new InvalidDataException("Invalid Witcher Collection destination: " + path);
			if (parts.Length == 1)
			{
				if (IsRootHelper(path)) return parts[0];
				throw new InvalidDataException("Unsupported Witcher Collection root file: " + path);
			}
			if (parts[0].Equals("bin", StringComparison.OrdinalIgnoreCase))
			{
				if (parts.Last().Equals("witcher3.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A Collection cannot replace the game executable.");
				return String.Join("\\", parts);
			}
			bool mod = parts[0].Equals("Mods", StringComparison.OrdinalIgnoreCase);
			if ((!mod && !parts[0].Equals("DLC", StringComparison.OrdinalIgnoreCase)) || parts.Length < 3)
				throw new InvalidDataException("Witcher Collection destination is outside Mods, DLC or bin: " + path);
			RequireFolderName(parts[1], mod ? "mod" : "dlc");
			if ((mod && parts[1].Length > 63) || parts.Length - 3 > Nexus.Client.ModManagement.Witcher3.Witcher3ComponentDiskScan.DefaultMaximumDepth)
				throw new InvalidDataException("Witcher Collection component exceeds the game's folder length or native observation depth.");
			return String.Join("\\", parts);
		}

		/// <summary>Recognizes manually invoked helpers that native ownership copies/restores without executing.</summary>
		internal static bool IsRootHelper(string path)
		{
			return new[] { ".bat", ".cmd", ".ps1" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>Recognizes the game-local menu XML/list root that Vortex merges.</summary>
		internal static bool IsMatrixPath(string path)
		{
			return path.StartsWith(@"bin\config\r4game\user_config_matrix\pc\", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Rejects path aliases and INI-section injection in folder identities.</summary>
		internal static void RequireFolderName(string name, string prefix)
		{
			string[] parts;
			if (!ManagedContentPath.TryParse(name, out parts) || parts.Length != 1 || name.IndexOfAny(new[] { '[', ']' }) >= 0 ||
				name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid Witcher component folder: " + name);
		}

		/// <summary>Rejects uncharacterized extension properties rather than silently dropping behavior.</summary>
		private static void RequireFields(JObject value, IEnumerable<string> allowed, string label)
		{
			if (value == null || value.Properties().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal)))
				throw new InvalidDataException("Invalid or unsupported Witcher " + label + " fields.");
		}

		/// <summary>Reads bounded scalar strings without coercing JSON types.</summary>
		private static string RequireString(JToken token, string label)
		{
			if (token?.Type != JTokenType.String || String.IsNullOrEmpty((string)token)) throw new InvalidDataException("Witcher " + label + " must be a string.");
			return (string)token;
		}
	}

	/// <summary>Game priority and activation for one safe mod-folder identity.</summary>
	internal sealed class CollectionWitcher3OrderEntry
	{
		/// <summary>Captures the curator's explicit game priority and enabled state.</summary>
		internal CollectionWitcher3OrderEntry(int priority, bool enabled, string sourceModId = null, string displayName = null)
		{ Priority = priority; Enabled = enabled; SourceModId = sourceModId; DisplayName = displayName; }
		internal int Priority { get; }
		internal bool Enabled { get; }
		internal string SourceModId { get; }
		internal string DisplayName { get; }
	}
}
