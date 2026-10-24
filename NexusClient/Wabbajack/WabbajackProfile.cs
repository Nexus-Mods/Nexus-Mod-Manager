using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.OnlineServices.Wabbajack;

namespace Nexus.Client.Wabbajack
{
	/// <summary>The compiled MO2 profile's enabled mods, priority and plugin state.</summary>
	public sealed class WabbajackProfile
	{
		public string Name { get; private set; }
		public IReadOnlyList<string> Mods { get; private set; }
		public IReadOnlyList<string> Plugins { get; private set; }
		public IReadOnlyList<string> LoadOrder { get; private set; }
		public IReadOnlyDictionary<string, string> Files { get; private set; }
		public IReadOnlyDictionary<string, byte[]> FileContents { get; private set; }

		/// <summary>Returns the profiles actually compiled into the package.</summary>
		public static IReadOnlyList<WabbajackProfile> ReadAll(WabbajackPackage package, GameModeWabbajackDefinition configuration)
		{
			List<WabbajackProfile> profiles = new List<WabbajackProfile>();
			using (ZipArchive zip = ZipFile.OpenRead(package.FilePath))
				foreach (WabbajackDirective modlist in package.Directives.Where(item => item.To != null &&
					item.To.Replace('\\', '/').StartsWith("profiles/", StringComparison.OrdinalIgnoreCase) &&
					item.To.Replace('\\', '/').Split('/').Length == 3 && Path.GetFileName(item.To).Equals("modlist.txt", StringComparison.OrdinalIgnoreCase)))
				{
					string name = modlist.To.Replace('\\', '/').Split('/')[1];
					Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					Dictionary<string, byte[]> contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
					foreach (WabbajackDirective directive in package.Directives.Where(item => item.To != null &&
						item.To.Replace('\\', '/').StartsWith("profiles/" + name + "/", StringComparison.OrdinalIgnoreCase)))
					{
						string leaf = directive.To.Replace('\\', '/').Substring(("profiles/" + name + "/").Length);
						if (leaf.Contains("/") || !(leaf.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ||
							new[] { "modlist.txt", "plugins.txt", "loadorder.txt" }.Contains(leaf, StringComparer.OrdinalIgnoreCase))) continue;
						if (WabbajackCollectionConverter.Kind(directive.Type) != "InlineFile")
							throw new NotSupportedException("This Wabbajack profile requires a non-embedded configuration file: " + directive.To);
						ZipArchiveEntry entry = zip.GetEntry(directive.SourceDataId);
						using (Stream input = entry.Open())
						using (MemoryStream data = new MemoryStream())
						{
							input.CopyTo(data); byte[] bytes = data.ToArray(); contents.Add(leaf, bytes);
							using (StreamReader reader = new StreamReader(new MemoryStream(bytes, false))) files.Add(leaf, reader.ReadToEnd());
						}
					}
					string pluginText, orderText;
					files.TryGetValue("plugins.txt", out pluginText);
					files.TryGetValue("loadorder.txt", out orderText);
					profiles.Add(new WabbajackProfile {
						Name = name, Files = files, FileContents = contents,
						Mods = Lines(files["modlist.txt"]).Where(line => line.StartsWith("+", StringComparison.Ordinal)).Select(line => line.Substring(1)).Reverse().ToList(),
						Plugins = Lines(pluginText).Where(line => !configuration.PluginFileUsesActiveMarkers || line.StartsWith("*", StringComparison.Ordinal))
							.Select(line => line.TrimStart('*')).ToList(),
						LoadOrder = Lines(orderText ?? pluginText).Select(line => line.TrimStart('*')).ToList() });
				}
			return profiles;
		}

		/// <summary>Ignores the comments and empty lines used by MO2's profile text files.</summary>
		private static IEnumerable<string> Lines(string text)
		{
			return (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal));
		}

		/// <summary>Displays the author's profile name in the profile selector.</summary>
		public override string ToString() { return Name; }
	}
}
