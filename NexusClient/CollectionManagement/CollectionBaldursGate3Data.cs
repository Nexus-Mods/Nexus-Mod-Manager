using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Nexus.Client.Mods;
using Newtonsoft.Json.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Interprets the BG3 extension's deployment types and ordered package metadata.</summary>
	internal static class CollectionBaldursGate3Data
	{
		/// <summary>Scopes extension data to the declared BG3 game.</summary>
		internal static bool IsManifest(JObject root) => String.Equals((string)root["info"]?["domainName"], "baldursgate3", StringComparison.OrdinalIgnoreCase);

		/// <summary>Checks only metadata used to deploy the Collection's enabled packages.</summary>
		internal static JArray ReadLoadOrder(JObject root)
		{
			JToken token = root["loadOrder"];
			if (token == null) return new JArray();
			JArray order = token as JArray;
			if (order == null) throw new InvalidDataException("BG3 loadOrder must be an array.");
			var identities = new HashSet<Guid>();
			foreach (JObject entry in order.OfType<JObject>())
			{
				JObject data = entry["data"] as JObject;
				Guid uuid;
				if (data == null || !Guid.TryParse((string)data["uuid"], out uuid) || !identities.Add(uuid) ||
					entry["enabled"]?.Type != JTokenType.Boolean)
					throw new InvalidDataException("BG3 loadOrder requires unique package UUIDs and an enabled flag.");
				if ((bool)entry["enabled"] && (bool?)data["isListed"] != true &&
					(entry["id"]?.Type != JTokenType.String || data["folder"]?.Type != JTokenType.String || data["name"]?.Type != JTokenType.String ||
					(data["version"]?.Type != JTokenType.String && data["version"]?.Type != JTokenType.Integer)))
					throw new InvalidDataException("An enabled BG3 package is missing its folder, name or version.");
			}
			if (order.Any(entry => !(entry is JObject))) throw new InvalidDataException("BG3 loadOrder entries must be objects.");
			return order;
		}

		/// <summary>Maps declared secondary packages before native extension filtering, retaining non-loadable companions separately.</summary>
		internal static IEnumerable<KeyValuePair<string, string>> GetBasicInstallFiles(IMod mod, string type, string companionKey)
		{
			List<string> files = mod.GetFileList() ?? new List<string>();
			if (IsCompanionArchive(mod, files))
			{
				if (String.IsNullOrWhiteSpace(companionKey)) throw new InvalidDataException("BG3 companion content requires a stable artifact identity.");
				string key = new string(companionKey.Select(character => Char.IsLetterOrDigit(character) || character == '-' || character == '_' ? character : '_').ToArray());
				return files.Select(path => new KeyValuePair<string, string>(path,
					@"Data\NMM Collection Companions\" + key + "\\" + path.Replace('/', '\\'))).ToArray();
			}
			if (!IsSecondaryType(type)) return null;
			var recipe = new ModInstallationSimpleFileRecipe(files.Select(path => new ModInstallationSimpleFileMapping(path, path)));
			return MapFiles(recipe, type, files).Mappings.Select(mapping =>
				new KeyValuePair<string, string>(mapping.SourcePath, mapping.DestinationPath)).ToArray();
		}

		/// <summary>Recognizes standalone tools, unopened archive alternatives and exported module-settings documents; never activates them.</summary>
		private static bool IsCompanionArchive(IMod mod, IList<string> files)
		{
			if (files.Count == 0 || files.Any(path => Path.GetExtension(path).Equals(".pak", StringComparison.OrdinalIgnoreCase))) return false;
			// Standalone tools can contain Public/Data templates; retain their complete tree before looking for game anchors.
			int shallowestFile = files.Min(path => path.Replace('/', '\\').Split('\\').Length);
			if (files.Any(path => Path.GetExtension(path).Equals(".py", StringComparison.OrdinalIgnoreCase) &&
				path.Replace('/', '\\').Split('\\').Length == shallowestFile)) return true;
			bool hasNativeContent = files.Any(path => path.Replace('/', '\\').Split('\\').Any(part =>
				part.Equals("Data", StringComparison.OrdinalIgnoreCase) || part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
				part.Equals("Public", StringComparison.OrdinalIgnoreCase) || part.Equals("Generated", StringComparison.OrdinalIgnoreCase)));
			if (hasNativeContent) return false;
			List<string> payloads = files.Where(path => !Readme.IsDocumentationFile(path) && !ModInstallFileFilter.IsIgnored(path)).ToList();
			if (payloads.Count > 0 && payloads.All(path => new[] { ".zip", ".7z", ".rar" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
				return true;
			if (payloads.Count != 1 || !Path.GetExtension(payloads[0]).Equals(".lsx", StringComparison.OrdinalIgnoreCase)) return false;
			byte[] data = mod.GetFile(payloads[0]);
			if (data == null || data.Length > 16 * 1024 * 1024) return false;
			var document = new XmlDocument { XmlResolver = null };
			try
			{
				using (var stream = new MemoryStream(data, false))
				using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 }))
					document.Load(reader);
				return document.DocumentElement?.Name == "save" && document.SelectSingleNode("/save/region[@id='ModuleSettings']//node[@id='Mods']") != null;
			}
			catch (XmlException) { return false; }
		}

		/// <summary>Identifies curator types that deploy outside the user's pak directory.</summary>
		internal static bool IsSecondaryType(string type) => type == "bg3-loose" || type == "bg3-replacer" || type == "bg3-bg3se" || type == "dinput";

		/// <summary>Maps a Vortex package root onto BG3's existing Data/bin secondary deployment paths.</summary>
		internal static ModInstallationSimpleFileRecipe MapFiles(ModInstallationSimpleFileRecipe recipe, string type, IEnumerable<string> archiveFiles)
		{
			if (!IsSecondaryType(type)) return recipe;
			bool extenderInstaller = UsesExtenderInstaller(type, archiveFiles);
			var mappings = new List<ModInstallationSimpleFileMapping>();
			foreach (ModInstallationSimpleFileMapping mapping in recipe.Mappings)
			{
				string destination = mapping.DestinationPath.StartsWith(@"Data\NMM Collection Companions\", StringComparison.OrdinalIgnoreCase)
					? mapping.DestinationPath : MapDestination(mapping.DestinationPath, type, extenderInstaller);
				if (destination != null) mappings.Add(new ModInstallationSimpleFileMapping(mapping.SourcePath, destination));
			}
			return new ModInstallationSimpleFileRecipe(mappings, true);
		}

		/// <summary>Matches Vortex's automatic DLL-only BG3SE installer independently of a curator type override.</summary>
		internal static bool UsesExtenderInstaller(string type, IEnumerable<string> archiveFiles) => type == "bg3-bg3se" &&
			archiveFiles.Any(path => String.Equals(Path.GetFileName(path.Replace('/', '\\')), "DWrite.dll", StringComparison.OrdinalIgnoreCase));

		/// <summary>Prefixes native secondary roots once and preserves the selected package-relative path.</summary>
		internal static string MapDestination(string destination, string type, bool extenderInstaller)
		{
			if (!IsSecondaryType(type)) return destination;
			string path = destination.Replace('/', '\\');
			if (extenderInstaller)
			{
				if (!Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)) return null;
				path = Path.GetFileName(path);
			}
			string root = type == "bg3-loose" || type == "bg3-replacer" ? "Data" : "bin";
			string[] parts = path.Split('\\');
			int content = root == "Data" ? Array.FindIndex(parts, part => part.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
				part.Equals("Generated", StringComparison.OrdinalIgnoreCase) || part.Equals("Mods", StringComparison.OrdinalIgnoreCase) ||
				part.Equals("Localization", StringComparison.OrdinalIgnoreCase)) : -1;
			int rootIndex = Array.FindIndex(parts, part => part.Equals(root, StringComparison.OrdinalIgnoreCase));
			if (content >= 0) path = String.Join("\\", parts.Skip(content));
			else if (rootIndex >= 0) path = String.Join("\\", parts.Skip(rootIndex + 1));
			return root + "\\" + path;
		}
	}
}
