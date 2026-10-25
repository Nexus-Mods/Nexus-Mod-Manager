using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

		/// <summary>Maps a Vortex package root onto BG3's existing Data/bin secondary deployment paths.</summary>
		internal static ModInstallationSimpleFileRecipe MapFiles(ModInstallationSimpleFileRecipe recipe, string type, IEnumerable<string> archiveFiles)
		{
			if (type != "bg3-loose" && type != "bg3-replacer" && type != "bg3-bg3se" && type != "dinput") return recipe;
			bool extenderInstaller = UsesExtenderInstaller(type, archiveFiles);
			var mappings = new List<ModInstallationSimpleFileMapping>();
			foreach (ModInstallationSimpleFileMapping mapping in recipe.Mappings)
			{
				string destination = MapDestination(mapping.DestinationPath, type, extenderInstaller);
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
			if (type != "bg3-loose" && type != "bg3-replacer" && type != "bg3-bg3se" && type != "dinput") return destination;
			string path = destination.Replace('/', '\\');
			if (extenderInstaller)
			{
				if (!Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)) return null;
				path = Path.GetFileName(path);
			}
			string root = type == "bg3-loose" || type == "bg3-replacer" ? "Data" : "bin";
			string[] parts = path.Split('\\');
			int content = root == "Data" ? Array.FindIndex(parts, part => part.Equals("Public", StringComparison.OrdinalIgnoreCase) || part.Equals("Generated", StringComparison.OrdinalIgnoreCase)) : -1;
			int rootIndex = Array.FindIndex(parts, part => part.Equals(root, StringComparison.OrdinalIgnoreCase));
			if (content >= 0) path = String.Join("\\", parts.Skip(content));
			else if (rootIndex >= 0) path = String.Join("\\", parts.Skip(rootIndex + 1));
			return root + "\\" + path;
		}
	}
}
