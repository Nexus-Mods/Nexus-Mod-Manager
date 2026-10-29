using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Threading;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.Mods;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Plans an explicitly installed LSX order archive through reversible native values, never ordinary PAK-folder deployment.</summary>
	internal static class LarianLoadOrderArchivePlanner
	{
		/// <summary>Recognizes order-only archives by content type, allowing descriptive LSX filenames and wrapper folders.</summary>
		internal static string Source(IMod mod)
		{
			return mod == null ? null : Source(mod.GetFileList());
		}

		/// <summary>Recognizes metadata-only archives without confusing loose game files or mixed PAK installers with order imports.</summary>
		internal static string Source(IEnumerable<string> paths)
		{
			if (paths == null) return null;
			string[] files = paths.ToArray();
			if (files.Any(path => !new[] { ".lsx", ".txt", ".md", ".html", ".htm", ".png", ".jpg", ".jpeg", ".json" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))) return null;
			if (files.Any(path => String.Equals(Path.GetExtension(path), ".pak", StringComparison.OrdinalIgnoreCase))) return null;
			string[] orders = files.Where(path => String.Equals(Path.GetExtension(path), ".lsx", StringComparison.OrdinalIgnoreCase) &&
				(String.Equals(Path.GetFileName(path), "modsettings.lsx", StringComparison.OrdinalIgnoreCase) ||
				 Path.GetFileNameWithoutExtension(path).IndexOf("loadorder", StringComparison.OrdinalIgnoreCase) >= 0 ||
				 !path.Replace('\\', '/').Split('/').Any(part => new[] { "Data", "Public", "Mods", "bin", "Localization" }.Contains(part, StringComparer.OrdinalIgnoreCase)))).Take(2).ToArray();
			if (orders.Length > 1) throw new InvalidDataException(LanguageManager.Get("LoadOrder.Install.AmbiguousOrderArchive", "The archive contains multiple LSX files. Use an archive containing one load order."));
			return orders.SingleOrDefault();
		}

		/// <summary>Validates the exported order before any native mutation, retaining each target profile's game records and format.</summary>
		internal static IReadOnlyList<BasicInstallGameSpecificValue> Build(IMod mod, StructuredXmlRecordRule rule, string profilesRoot = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string source = Source(mod);
			if (source == null) return new BasicInstallGameSpecificValue[0];
			byte[] bytes;
			using (FileStream stream = mod.GetFileStream(source))
			{
				if (stream == null || stream.Length < 1 || stream.Length > LarianLoadOrderDocument.MaximumBytes) throw new InvalidDataException("The exported load order is empty or oversized.");
				using (var copy = new MemoryStream())
				{
					byte[] buffer = new byte[8192]; int count;
					while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
					{
						cancellationToken.ThrowIfCancellationRequested();
						if (copy.Length + count > LarianLoadOrderDocument.MaximumBytes) throw new InvalidDataException("The exported load order grew beyond its limit.");
						copy.Write(buffer, 0, count);
					}
					bytes = copy.ToArray();
				}
			}
			var imported = new LarianLoadOrderDocument(Path.Combine(Path.GetTempPath(), "imported-modsettings.lsx"), bytes);
			NativeLoadOrderEntry[] entries = imported.Entries.Where(entry => !entry.IsProtected).ToArray();
			if (entries.Length == 0) throw new InvalidDataException(LanguageManager.Get("LoadOrder.Install.EmptyOrderArchive", "The exported load order contains no custom module entries."));
			foreach (NativeLoadOrderEntry entry in entries)
			{
				string version = LarianLoadOrderDocument.Field(imported.Records[entry.Identity], "Version64");
				if (version != null && !UInt64.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out ulong ignored))
					throw new InvalidDataException(LanguageManager.Format("LoadOrder.Install.InvalidOrderVersion", "The exported load order contains an invalid packed module version: {0}", entry.Name));
			}
			string root = profilesRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), rule.ProfileRoot);
			var store = new BaldursGate3CollectionRecordStore(rule, root);
			var values = new List<BasicInstallGameSpecificValue>();
			long plannedBytes = 0;
			if (!Directory.Exists(root)) throw new InvalidDataException(LanguageManager.Get("LoadOrder.Install.MissingProfileRoot", "Start the game once to create a game profile before installing a load order."));
			string[] profiles = Directory.EnumerateDirectories(root).Take(257).ToArray();
			if (profiles.Length > 256) throw new InvalidDataException("The game profile set exceeds its supported limit.");
			foreach (string directory in profiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				string profile = Path.GetFileName(directory);
				if (rule.ExcludedProfiles.Contains(profile, StringComparer.OrdinalIgnoreCase)) continue;
				string path = store.GetManifestPath(profile);
				if (!File.Exists(path)) continue;
				LarianLoadOrderDocument current = LarianLoadOrderDocument.Read(path);
				if (current.Legacy != imported.Legacy) throw new InvalidDataException(LanguageManager.Get("LoadOrder.Install.OrderFormatMismatch", "The exported order and target profile use different native formats. Export an order matching the current game profile before installing it."));
				var protectedIds = new HashSet<string>(current.Entries.Where(entry => entry.IsProtected).Select(entry => entry.Identity));
				XmlDocument desired = (XmlDocument)current.Xml.CloneNode(true);
				XmlElement mods = LarianLoadOrderDocument.Children(LarianLoadOrderDocument.Container(desired, "Mods", true));
				foreach (XmlElement node in mods.ChildNodes.OfType<XmlElement>().Where(node => node.Name == "node" && node.GetAttribute("id") == "ModuleShortDesc").ToArray())
				{
					string rawId = LarianLoadOrderDocument.Field(node, "UUID");
					string id = String.IsNullOrEmpty(rawId) ? "game:" + LarianLoadOrderDocument.Field(node, "Folder") : LarianLoadOrderDocument.Identity(rawId);
					if (!protectedIds.Contains(id)) mods.RemoveChild(node);
				}
				foreach (NativeLoadOrderEntry entry in entries)
				{
					if (protectedIds.Contains(entry.Identity)) throw new InvalidDataException("The imported order attempts to replace a game-owned module.");
					mods.AppendChild(desired.ImportNode(imported.Records[entry.Identity], true));
				}
				if (current.UsesLegacyModOrder)
				{
					XmlElement order = LarianLoadOrderDocument.Children(LarianLoadOrderDocument.Container(desired, "ModOrder", true));
					foreach (XmlElement node in order.ChildNodes.OfType<XmlElement>().Where(node => node.Name == "node" && node.GetAttribute("id") == "Module").ToArray()) order.RemoveChild(node);
					foreach (NativeLoadOrderEntry entry in current.Entries.Where(entry => entry.IsProtected && !entry.Identity.StartsWith("game:", StringComparison.Ordinal)).Concat(entries))
					{
						XmlElement node = desired.CreateElement("node"); node.SetAttribute("id", "Module");
						XmlElement id = desired.CreateElement("attribute"); id.SetAttribute("id", "UUID"); id.SetAttribute("type", "FixedString"); id.SetAttribute("value", entry.Identity);
						node.AppendChild(id); order.AppendChild(node);
					}
				}
				new LarianLoadOrderDocument(path, LarianLoadOrderDocument.Serialize(desired));
				string[] included = entries.Select(entry => entry.Identity).ToArray();
				string[] previous = current.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).ToArray();
				var preference = new LarianOrderPreference { Source = "Manual", Order = included, Excluded = previous.Except(included).ToArray(), Managed = previous.Union(included).ToArray() };
				byte[] policy = preference.Encode(), aggregate = BaldursGate3CollectionRecordStore.Capture(desired);
				plannedBytes += policy.Length + aggregate.Length;
				if (plannedBytes > 16 * 1024 * 1024) throw new InvalidDataException("The load-order installation plan exceeds its aggregate limit.");
				values.Add(new BasicInstallGameSpecificValue(LarianOrderPreference.Key(profile), policy));
				values.Add(new BasicInstallGameSpecificValue(BaldursGate3CollectionRecordStore.Prefix + profile + "|" + BaldursGate3CollectionRecordStore.Identity, aggregate));
			}
			if (values.Count == 0) throw new InvalidDataException(LanguageManager.Get("LoadOrder.Install.MissingOrderProfile", "No existing game profile can receive this load order. Start the game once, then retry."));
			return values.AsReadOnly();
		}
	}
}
