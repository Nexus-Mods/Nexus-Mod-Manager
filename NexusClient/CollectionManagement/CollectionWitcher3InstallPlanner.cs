using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Nexus.Client.Games.ManagedContent;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.ModManagement.Witcher3;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Translates Witcher package types, curator folder order and embedded merges into native reviewed effects.</summary>
	internal static class CollectionWitcher3InstallPlanner
	{
		/// <summary>Plans only the exact selected member; bundled merge output belongs to one deterministic selected owner.</summary>
		internal static IReadOnlyList<ScriptedInstallOperation> Build(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			IMod mod, ModInstallContext context, JObject root, CollectionWitcher3Data data, CancellationToken cancellationToken)
		{
			if (context.InstallRoot != ModInstallRoot.Data || (context.Method != ModInstallMethod.Direct && context.Method != ModInstallMethod.Virtual))
				throw new NotSupportedException("Witcher Collection installation requires the ordinary Direct or Virtual game data root.");
			if (mod.HasInstallScript) throw new NotSupportedException("Scripted Witcher Collection packages require selected-operation preparation.");
			byte[] edition;
			if (data.Metadata.TryGetValue(".w3edition", out edition) && !String.Equals(Encoding.UTF8.GetString(edition).Trim().TrimStart('\uFEFF'), "remastered", StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("The Collection's bundled Script Merger output targets a different Witcher edition.");
			JObject rawMember = (root["mods"] as JArray)?[member.SourceOrdinal] as JObject;
			if (rawMember == null || !CollectionWitcher3Data.IsWitcherManifest(root)) throw new InvalidDataException("The retained Witcher member source is missing.");
			string type = (string)(rawMember["details"] as JObject)?["type"] ?? String.Empty;
			if (type.Length > 0 && !new[] { "witcher3tl", "witcher3dlc", "witcher3menumodroot" }.Contains(type, StringComparer.Ordinal))
				throw new NotSupportedException("Unsupported Witcher Collection package type: " + type);
			List<string> sources = mod.GetFileList();
			if (sources == null || sources.Count > 1000000) throw new InvalidDataException("Witcher archive inventory is unavailable or too large.");
			var operations = new List<ScriptedInstallOperation>();
			var records = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var archivePaths = mod as IModArchiveSourcePathProvider;
			foreach (string source in sources)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (ModInstallFileFilter.IsIgnored(source)) continue;
				string leaf = Path.GetFileName(source.Replace('/', '\\')).ToLowerInvariant();
				string settingsName = leaf.EndsWith(".part.txt", StringComparison.Ordinal) ? leaf.Substring(0, leaf.Length - ".part.txt".Length) : leaf;
				if (Witcher3CollectionRecordStore.IsDocumentSettings(settingsName) && settingsName != "mods.settings")
				{
					foreach (KeyValuePair<string, byte[]> value in Witcher3CollectionRecordStore.PlanSettings(settingsName, ReadMenuFile(mod, source, cancellationToken)))
						AddRecord(records, value, false);
					continue;
				}
				// Vortex explicitly excludes input.xml fragments from deployment and Documents INI aggregation.
				if (leaf == "input.xml.part.txt") continue;
				if (leaf.EndsWith(".part.txt", StringComparison.Ordinal)) throw new InvalidDataException("Uncharacterized Witcher settings fragment: " + source);
				// NMM may expose content\... after stripping modFoo/dlcFoo. Use the real layout
				// to choose the destination, but keep the logical source for every native archive read.
				string layoutSource = archivePaths == null ? source : archivePaths.GetArchiveSourcePath(source);
				string destination = MapPath(layoutSource, type, member, rawMember, data);
				if (destination == null) continue;
				if (!destinations.Add(destination)) throw new InvalidDataException("Witcher package contains colliding destinations: " + destination);
				if (CollectionWitcher3Data.IsMatrixPath(destination) &&
					(Path.GetExtension(destination).Equals(".xml", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(destination).Equals(".txt", StringComparison.OrdinalIgnoreCase)))
				{
					byte[] menu = ReadMenuFile(mod, source, cancellationToken);
					if (Witcher3CollectionRecordStore.RequiresMenuMerge(destination))
					{
						foreach (KeyValuePair<string, byte[]> value in Witcher3CollectionRecordStore.PlanMenu(destination, menu))
							AddRecord(records, value, false);
					}
					else operations.Add(new GenerateDataFileOperation(destination, Witcher3CollectionRecordStore.PrepareMenuFile(menu), source));
				}
				else operations.Add(new InstallModFileOperation(source, destination));
				AddFolder(destination, folders);
			}
			bool mergeOwner = member.MemberKey.Equals(plan.SelectedMembers.OrderBy(item => item.SourceOrdinal).First().MemberKey);
			if (mergeOwner)
			{
				if (data.GeneratedFiles.Keys.Any(path => path.StartsWith("Mods\\", StringComparison.OrdinalIgnoreCase)) &&
					plan.CapabilityReport.Manifest.Members.Any(item => !item.IsSelected))
					throw new InvalidDataException("The Collection's premerged scripts require its complete member selection. A partial selection needs newly merged scripts.");
				foreach (KeyValuePair<string, byte[]> generated in data.GeneratedFiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (Witcher3CollectionRecordStore.IsDocumentSettings(generated.Key))
					{
						foreach (KeyValuePair<string, byte[]> value in Witcher3CollectionRecordStore.PlanSettings(generated.Key, generated.Value))
							AddRecord(records, value, true);
					}
					else if (CollectionWitcher3Data.IsMatrixPath(generated.Key))
					{
						if (Witcher3CollectionRecordStore.RequiresMenuMerge(generated.Key))
						{
							foreach (KeyValuePair<string, byte[]> value in Witcher3CollectionRecordStore.PlanMenu(generated.Key, generated.Value))
								AddRecord(records, value, true);
						}
						else
						{
							// A curator snapshot supersedes the same custom menu supplied by this member.
							operations.RemoveAll(operation => String.Equals((operation as GenerateDataFileOperation)?.DestinationPath, generated.Key, StringComparison.OrdinalIgnoreCase));
							operations.Add(new GenerateDataFileOperation(generated.Key, Witcher3CollectionRecordStore.PrepareMenuFile(generated.Value), @"__collection_manifest\mergedData\" + generated.Key));
						}
					}
					else
					{
						if (!destinations.Add(generated.Key)) throw new InvalidDataException("A Collection merge collides with its owning member's files.");
						// This source namespace identifies sealed manifest content, not an entry in the member archive.
						// Raw manifest and generated output blobs are both retained by the native recipe/recovery owner.
						operations.Add(new GenerateDataFileOperation(generated.Key, generated.Value, @"__collection_manifest\mergedData\" + generated.Key));
						AddFolder(generated.Key, folders);
					}
				}
			}
			foreach (string folder in folders.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
			{
				CollectionWitcher3OrderEntry order;
				if (!data.Order.TryGetValue(folder, out order)) continue;
				AddRecord(records, new KeyValuePair<string, byte[]>(Witcher3CollectionRecordStore.Key("mods.settings", "ini", folder, "Priority"), Encoding.UTF8.GetBytes(order.Priority.ToString(CultureInfo.InvariantCulture))), false);
				AddRecord(records, new KeyValuePair<string, byte[]>(Witcher3CollectionRecordStore.Key("mods.settings", "ini", folder, "Enabled"), Encoding.UTF8.GetBytes(order.Enabled ? "1" : "0")), false);
			}
			foreach (KeyValuePair<string, byte[]> record in records.OrderBy(item => item.Key, StringComparer.Ordinal))
				operations.Add(new EditGameSpecificValueOperation(record.Key, record.Value, true));
			if (operations.Count == 0) throw new InvalidDataException("Witcher Collection member has no installable files or settings: " + member.DisplayName);
			return operations.AsReadOnly();
		}

		/// <summary>Lets curator-merged settings replace package defaults while rejecting ambiguous duplicate source records.</summary>
		private static void AddRecord(IDictionary<string, byte[]> records, KeyValuePair<string, byte[]> value, bool merged)
		{
			if (!merged && records.ContainsKey(value.Key)) throw new InvalidDataException("Witcher package contains duplicate menu or order records.");
			records[value.Key] = value.Value;
		}

		/// <summary>Maps explicit root/component packages without guessing between archive variants.</summary>
		internal static string MapPath(string source, string type, ResolvedCollectionMemberPlan member, JObject rawMember, CollectionWitcher3Data data)
		{
			string[] parts;
			if (!ManagedContentPath.TryParse(source, out parts)) throw new InvalidDataException("Unsafe Witcher archive source: " + source);
			int root = Array.FindIndex(parts, item => item.Equals("Mods", StringComparison.OrdinalIgnoreCase) || item.Equals("DLC", StringComparison.OrdinalIgnoreCase) || item.Equals("bin", StringComparison.OrdinalIgnoreCase));
			if (root >= 0 && root < parts.Length - 1) return CollectionWitcher3Data.ValidateDestination(String.Join("\\", parts.Skip(root)));
			int content = Array.FindIndex(parts, item => item.Equals("content", StringComparison.OrdinalIgnoreCase));
			if (content > 0)
			{
				string component = parts[content - 1];
				if (component.StartsWith("mod", StringComparison.OrdinalIgnoreCase)) return CollectionWitcher3Data.ValidateDestination("Mods\\" + String.Join("\\", parts.Skip(content - 1)));
				if (component.StartsWith("dlc", StringComparison.OrdinalIgnoreCase)) return CollectionWitcher3Data.ValidateDestination("DLC\\" + String.Join("\\", parts.Skip(content - 1)));
			}
			if (parts.Length > 1 && parts[0].StartsWith("mod", StringComparison.OrdinalIgnoreCase)) return CollectionWitcher3Data.ValidateDestination("Mods\\" + source);
			if (parts.Length > 1 && parts[0].StartsWith("dlc", StringComparison.OrdinalIgnoreCase)) return CollectionWitcher3Data.ValidateDestination("DLC\\" + source);
			if (content == 0)
			{
				string name = (string)rawMember["name"];
				string folder = "mod" + new string((name ?? String.Empty).Where(character => Char.IsLetterOrDigit(character) || character == '_').ToArray());
				if (!data.Order.ContainsKey(folder))
				{
					string expression = (string)(rawMember["source"] as JObject)?["fileExpression"];
					List<string> candidates = data.Order.Where(item => (!String.IsNullOrEmpty(expression) && String.Equals(item.Value.SourceModId, expression, StringComparison.OrdinalIgnoreCase)) ||
						(!String.IsNullOrEmpty(name) && ((item.Value.SourceModId ?? String.Empty).StartsWith(name + " ", StringComparison.OrdinalIgnoreCase) ||
						(item.Value.DisplayName ?? String.Empty).StartsWith(name + " (", StringComparison.OrdinalIgnoreCase)))).Select(item => item.Key).ToList();
					if (candidates.Count > 1) throw new InvalidDataException("The Collection does not identify one folder for this bare-content package.");
					folder = candidates.Count == 1 ? candidates[0] : ManagedContentPath.GenerateComponentName(member.ArtifactChoice.SelectedArtifact.StableId, name, "mod", 63);
					if (data.Order.Count > 0 && candidates.Count == 0) throw new InvalidDataException("The Collection's folder load order cannot be matched to this bare-content package: " + name);
				}
				return CollectionWitcher3Data.ValidateDestination("Mods\\" + folder + "\\" + source);
			}
			string extension = Path.GetExtension(source);
			if (new[] { ".txt", ".md", ".pdf", ".rtf", ".png", ".jpg", ".jpeg", ".url", ".json" }.Contains(extension, StringComparer.OrdinalIgnoreCase)) return null;
			if (parts.Length == 1 && CollectionWitcher3Data.IsRootHelper(source)) return CollectionWitcher3Data.ValidateDestination(source);
			throw new InvalidDataException("Witcher package contains a file with no unambiguous game destination: " + source);
		}

		/// <summary>Associates settings with actual installed mod folders, excluding DLC and omitted optional members.</summary>
		private static void AddFolder(string path, ISet<string> folders)
		{
			string[] parts = path.Split('\\');
			if (parts.Length > 2 && parts[0].Equals("Mods", StringComparison.OrdinalIgnoreCase)) folders.Add(parts[1]);
		}

		/// <summary>Reads bounded menu input through the archive's stream API instead of allocating an unbounded XML member.</summary>
		private static byte[] ReadMenuFile(IMod mod, string source, CancellationToken cancellationToken)
		{
			using (Stream input = mod.GetFileStream(source))
			using (var output = new MemoryStream())
			{
				if (input == null || (input.CanSeek && input.Length > 16 * 1024 * 1024)) throw new InvalidDataException("Witcher menu input exceeds its size limit.");
				byte[] buffer = new byte[8192]; int read;
				while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (output.Length + read > 16 * 1024 * 1024) throw new InvalidDataException("Witcher menu input exceeds its size limit.");
					output.Write(buffer, 0, read);
				}
				return output.ToArray();
			}
		}
	}
}
