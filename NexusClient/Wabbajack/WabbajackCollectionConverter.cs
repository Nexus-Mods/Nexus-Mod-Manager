using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.ModRepositories;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;
using Nexus.Client.OnlineServices.Wabbajack;
using Nexus.Client.Util.Localization;
using Octodiff.Core;
using Octodiff.Diagnostics;
using SevenZip;
using Archive = Nexus.Client.Util.Archive;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace Nexus.Client.Wabbajack
{
	/// <summary>Prepared NMM-owned Collection and companion settings, with the original Wabbajack provenance retained.</summary>
	public sealed class WabbajackConvertedCollection
	{
		public string BundlePath { get; internal set; }
		public string SetupPath { get; internal set; }
		public CollectionDefinition Definition { get; internal set; }
		public CollectionRevision Revision { get; internal set; }
	}

	/// <summary>References original archives in a logical Collection and packages only list-specific custom outputs.</summary>
	public sealed class WabbajackCollectionConverter
	{
		private readonly WabbajackPackage _package;
		private readonly WabbajackProfile _profile;
		private readonly GameModeWabbajackDefinition _configuration;
		private readonly string _gameDirectory;
		private readonly string _domain;
		private readonly List<Output> _outputs;
		private readonly Dictionary<string, string> _sources = new Dictionary<string, string>(StringComparer.Ordinal);
		private readonly Dictionary<string, SevenZipExtractor> _extractors = new Dictionary<string, SevenZipExtractor>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _nested = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Dictionary<string, int>> _entries = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
		private string _downloadsDirectory;
		public IReadOnlyList<WabbajackArchive> RequiredArchives { get; }
		public string CurrentOutput { get; private set; }
		public string CurrentInstruction { get; private set; }
		public int CompletedOutputs { get; private set; }
		public int TotalOutputs { get; private set; }

		/// <summary>Chooses enabled mod outputs and the game JSON's explicit root/tool mappings.</summary>
		public WabbajackCollectionConverter(WabbajackPackage package, WabbajackProfile profile, GameModeWabbajackDefinition configuration,
			string gameDirectory, string nexusDomain)
		{
			_package = package; _profile = profile; _configuration = configuration; _gameDirectory = gameDirectory; _domain = nexusDomain;
			if (!string.Equals(package.GameType, configuration.Game, StringComparison.OrdinalIgnoreCase))
				throw new NotSupportedException(L("GameMismatch", "This Wabbajack list belongs to a different game."));
			_outputs = SelectOutputs();
			TotalOutputs = _outputs.Count;
			foreach (Output output in _outputs)
				if (!new[] { "FromArchive", "PatchedFromArchive", "InlineFile", "RemappedInlineFile" }.Contains(Kind(output.Directive.Type)))
					throw new NotSupportedException(L("UnsupportedInstruction", "Unsupported Wabbajack instruction: ") + output.Directive.Type);
			HashSet<string> hashes = new HashSet<string>(_outputs.Where(output => output.Directive.ArchiveHashPath != null)
				.Select(output => output.Directive.ArchiveHashPath[0]), StringComparer.Ordinal);
			RequiredArchives = package.Archives.Where(archive => hashes.Contains(archive.Hash)).ToList();
		}

		/// <summary>Reuses exact downloaded archives and original game files, as Wabbajack does.</summary>
		public IReadOnlyList<WabbajackArchive> FindMissingSources(IEnumerable<string> downloadDirectories, IProgress<string> progress, CancellationToken token)
		{
			List<string> directories = downloadDirectories.Where(directory => !string.IsNullOrWhiteSpace(directory)).ToList();
			_downloadsDirectory = directories.FirstOrDefault() ?? Path.GetDirectoryName(_package.FilePath);
			Dictionary<long, List<string>> candidates = new Dictionary<long, List<string>>();
			foreach (string directory in directories.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
				// Recover downloads written by the old explicit-path bug as archive.ext/archive.ext.
				foreach (string file in Directory.EnumerateFiles(directory).Concat(Directory.EnumerateDirectories(directory)
					.Select(folder => Path.Combine(folder, Path.GetFileName(folder))).Where(File.Exists)))
				{
					long size = new FileInfo(file).Length;
					List<string> files;
					if (!candidates.TryGetValue(size, out files)) candidates.Add(size, files = new List<string>());
					files.Add(file);
				}
			List<WabbajackArchive> missing = new List<WabbajackArchive>();
			foreach (WabbajackArchive archive in RequiredArchives)
			{
				token.ThrowIfCancellationRequested();
				progress?.Report(L("CheckingSource", "Locating source: ") + archive.Name);
				List<string> files;
				if (Kind(archive.State?.Type).StartsWith("GameFileSource", StringComparison.Ordinal))
				{
					files = candidates.ContainsKey(archive.Size) ? candidates[archive.Size].ToList() : new List<string>();
					if (string.Equals(archive.State.Game, _package.GameType, StringComparison.OrdinalIgnoreCase)) files.Add(Inside(_gameDirectory, archive.State.GameFile));
				}
				else if (!candidates.TryGetValue(archive.Size, out files)) files = new List<string>();
				string found = null;
				foreach (string file in files.OrderByDescending(file => string.Equals(Path.GetFileName(file), archive.Name, StringComparison.OrdinalIgnoreCase)))
				{
					if (!File.Exists(file) || new FileInfo(file).Length != archive.Size) continue;
					using (FileStream stream = File.OpenRead(file))
						if (WabbajackHash.Compute(stream, token) == archive.Hash) { found = file; break; }
				}
				if (found == null) missing.Add(archive);
				else _sources[archive.Hash] = found;
			}
			return missing;
		}

		/// <summary>Acquires supported HTTP/CDN and Premium Nexus sources; free-account downloads remain browser-driven.</summary>
		public async Task DownloadSourcesAsync(IEnumerable<WabbajackArchive> missing, string downloadDirectory, IModRepository repository,
			ModManager manager, IProgress<string> progress, CancellationToken token)
		{
			Directory.CreateDirectory(downloadDirectory);
			List<Task> transfers = new List<Task>();
			foreach (WabbajackArchive archive in missing)
			{
				token.ThrowIfCancellationRequested();
				string kind = Kind(archive.State?.Type);
				string url = archive.State?.Url;
				if (kind.StartsWith("Nexus", StringComparison.Ordinal))
				{
					if (repository.UserStatus?.IsPremium != true || !string.Equals(archive.State.GameName, _package.GameType, StringComparison.OrdinalIgnoreCase)) continue;
					url = "nxm://" + repository.GameDomainName + "/mods/" + archive.State.ModId + "/files/" + archive.State.FileId;
				}
				else if (!(kind.StartsWith("Http", StringComparison.Ordinal) || kind.StartsWith("WabbajackCDN", StringComparison.Ordinal))) continue;
				else if (archive.State.AdditionalData != null && archive.State.AdditionalData.ContainsKey("Headers") && archive.State.AdditionalData["Headers"].HasValues) continue;
				if (string.IsNullOrWhiteSpace(url)) continue;
				progress?.Report(L("DownloadingSource", "Downloading source: ") + archive.Name);
				transfers.Add(WabbajackDownloadQueue.DownloadAsync(manager, new Uri(url), Inside(downloadDirectory, Path.GetFileName(archive.Name)), archive.Hash, archive.Size, progress, token));
			}
			await Task.WhenAll(transfers).ConfigureAwait(false);
		}

		/// <summary>Creates a logical Collection over original archives, bundling only list-specific custom outputs.</summary>
		public WabbajackConvertedCollection Convert(string outputDirectory, string sourceId, IProgress<string> progress, CancellationToken token)
		{
			if (RequiredArchives.Any(archive => !_sources.ContainsKey(archive.Hash))) throw new InvalidOperationException(L("SourcesMissing", "Download the missing sources before preparing the Collection."));
			Directory.CreateDirectory(outputDirectory);
			string workspace = Path.Combine(Path.GetFullPath(outputDirectory), "prepare-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(workspace);
			string name = _package.Name + " — " + _profile.Name;
			string baseName = string.Concat(("Wabbajack - " + name + " - " + _package.Version).Split(Path.GetInvalidFileNameChars()));
			string destination = Path.Combine(outputDirectory, baseName + ".zip");
			string setupPath = Path.Combine(outputDirectory, baseName + " - Setup.zip");
			string temporary = Path.Combine(workspace, "collection.zip");
			string provenance = _package.SourceId ?? sourceId ?? _package.GameType + "/" + _package.Author + "/" + _package.Name;
			CollectionIdentity identity = CollectionIdentity.FromLocal(StableGuid("wabbajack/" + provenance + "/" + _profile.Name));
			List<WabbajackArchive> originals = new List<WabbajackArchive>();
			CompletedOutputs = 0;
			try
			{
				List<string> order = _profile.Mods.ToList();
				order.Insert(0, "[Wabbajack root files]"); order.Add("[Wabbajack overwrite]");
				// Keep each final compiled destination once, with the same winning mod as the selected MO2 profile.
				List<Output> selected = _outputs.OrderBy(output => order.IndexOf(output.Member))
					.GroupBy(output => (output.GameRoot ? "root:" : "data:") + Relative(output.Path), StringComparer.OrdinalIgnoreCase)
					.Select(group => group.Last()).ToList();
				TotalOutputs = selected.Count;
				List<Output> referenced = selected.Where(output => Kind(output.Directive.Type) == "FromArchive" && output.Directive.ArchiveHashPath.Length == 2)
					.GroupBy(output => output.Directive.ArchiveHashPath[0])
					.Where(group => group.All(output => output.GameRoot) || group.All(output => !output.GameRoot)).SelectMany(group => group).ToList();
				List<Output> custom = selected.Except(referenced).ToList();

				// Only custom game-file patches consume vanilla masters. Keep those original inputs for future revisions.
				HashSet<string> customInputs = new HashSet<string>(custom.Where(output => output.Directive.ArchiveHashPath != null)
					.Select(output => output.Directive.ArchiveHashPath[0]));
				foreach (WabbajackArchive archive in RequiredArchives.Where(archive => customInputs.Contains(archive.Hash) && Kind(archive.State?.Type).StartsWith("GameFileSource", StringComparison.Ordinal)))
				{
					string retained = Inside(Path.Combine(outputDirectory, "Wabbajack Sources"), archive.Hash.Replace('/', '_').Replace('+', '-') + "-" + Path.GetFileName(archive.Name));
					Directory.CreateDirectory(Path.GetDirectoryName(retained));
					if (!string.Equals(Path.GetFullPath(_sources[archive.Hash]), Path.GetFullPath(retained), StringComparison.OrdinalIgnoreCase)) File.Copy(_sources[archive.Hash], retained, true);
					_sources[archive.Hash] = retained;
				}

				JArray members = new JArray();
				JArray rules = new JArray();
				using (ZipArchive source = ZipFile.OpenRead(_package.FilePath))
				using (ZipArchive bundle = ZipFile.Open(temporary, ZipArchiveMode.Create))
				{
					// Ordinary mods stay in their original downloaded archives. The hash list selects and maps their files at installation.
					foreach (IGrouping<string, Output> group in referenced.GroupBy(output => output.Directive.ArchiveHashPath[0]))
					{
						token.ThrowIfCancellationRequested();
						WabbajackArchive archive = RequiredArchives.First(item => item.Hash == group.Key);
						string displayName = string.Join(" / ", group.Select(output => output.Member).Distinct(StringComparer.OrdinalIgnoreCase));
						progress?.Report(string.Format(L("ReferencingArchive", "Referencing original archive: {0}"), archive.Name));
						CurrentOutput = archive.Name; CurrentInstruction = "ArchiveReference";
						bool nexus = Kind(archive.State?.Type).StartsWith("Nexus", StringComparison.Ordinal) &&
							string.Equals(archive.State.GameName, _package.GameType, StringComparison.OrdinalIgnoreCase);
						JObject archiveSource = new JObject { ["type"] = nexus ? "nexus" : "manual", ["fileSize"] = archive.Size,
							["fileExpression"] = Path.GetFileName(archive.Name), ["tag"] = "wb-source-" + StableGuid(group.Key).ToString("N") };
						if (nexus) { archiveSource["modId"] = archive.State.ModId; archiveSource["fileId"] = archive.State.FileId; }
						else
						{
							using (MD5 md5 = MD5.Create())
							using (FileStream input = File.OpenRead(_sources[group.Key]))
								archiveSource["md5"] = BitConverter.ToString(md5.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
						}
						JArray files = new JArray(group.Select(output => FileHash(output.Path, output.Directive.Hash)));
						JObject referencedMember = Member(displayName, files, archiveSource, members.Count);
						if (group.First().GameRoot) { referencedMember["details"] = new JObject { ["type"] = "enb" }; members.Insert(0, referencedMember); }
						else members.Add(referencedMember);
						originals.Add(archive);
						CompletedOutputs += group.Count();
					}

					int customCompleted = 0;
					foreach (IGrouping<bool, Output> group in custom.GroupBy(output => output.GameRoot))
					{
						string tag = group.Key ? "wb-custom-root" : "wb-custom-data";
						JArray files = new JArray();
						long size = 0;
						foreach (Output output in group)
						{
							token.ThrowIfCancellationRequested();
							CurrentOutput = output.Directive.To; CurrentInstruction = Kind(output.Directive.Type);
							progress?.Report(string.Format(L("PreparingCustomFile", "Preparing list-specific file {0}/{1}: {2}"), ++customCompleted, custom.Count, output.Path));
							ZipArchiveEntry entry = bundle.CreateEntry("bundled/" + tag + "/" + Relative(output.Path).Replace('\\', '/'), CompressionLevel.Fastest);
							entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
							using (Stream stream = entry.Open())
							using (WabbajackOutputStream target = new WabbajackOutputStream(stream, token))
							{
								try { WriteDirective(source, output.Directive, target, workspace, token); }
								catch (OperationCanceledException) { throw; }
								catch (Exception error) { throw new InvalidDataException(L("ReconstructionFailed", "Could not reconstruct compiled file: ") + CurrentOutput + ". " + error.Message, error); }
								if (Kind(output.Directive.Type) != "RemappedInlineFile" && (target.Length != output.Directive.Size || target.Hash != output.Directive.Hash))
									throw new InvalidDataException(L("OutputMismatch", "Reconstructed file does not match the Wabbajack recipe: ") + CurrentOutput);
								files.Add(FileHash(output.Path, target.Hash)); size += target.Length; CompletedOutputs++;
							}
						}
						JObject member = Member(_package.Name + " - " + L(group.Key ? "CustomRootFiles" : "CustomFiles", group.Key ? "Root setup files" : "Custom files"), files,
							new JObject { ["type"] = "bundle", ["fileSize"] = Math.Max(1, size), ["fileExpression"] = tag, ["tag"] = tag }, members.Count);
						if (group.Key) { member["details"] = new JObject { ["type"] = "enb" }; members.Insert(0, member); }
						else members.Add(member);
					}
					for (int index = 0; index < members.Count; index++)
					{
						members[index]["phase"] = index;
						if (index > 0) rules.Add(new JObject { ["type"] = "after", ["source"] = new JObject { ["tag"] = members[index]["source"]["tag"].Value<string>(), ["versionMatch"] = "*" },
							["reference"] = new JObject { ["tag"] = members[index - 1]["source"]["tag"].Value<string>(), ["versionMatch"] = "*" } });
					}
					JArray plugins = new JArray(_profile.Plugins.Select(plugin => new JObject { ["name"] = plugin, ["enabled"] = true }));
					JArray pluginRules = new JArray();
					for (int index = 1; index < _profile.LoadOrder.Count; index++) pluginRules.Add(new JObject { ["name"] = _profile.LoadOrder[index], ["after"] = new JArray(_profile.LoadOrder[index - 1]) });
					string instructions = L("ConvertedInstructions", "Wabbajack profile: ") + _profile.Name + "\r\n" +
						L("LogicalCollectionInstructions", "This Collection references the original downloaded mods. Only list-specific custom files are bundled.") + "\r\n" +
						L("ApplyProfileSettingsInstructions", "NMM has created a setup ZIP containing this Wabbajack profile's game INI files. After the mods finish installing, open Setup & instructions and click Review Wabbajack settings... to review and apply them. NMM selects the generated ZIP automatically; no additional download is needed. Follow the list author's remaining post-install instructions, including any game patchers.") +
						(custom.Any(output => output.GameRoot && output.Path.StartsWith("Tools\\", StringComparison.OrdinalIgnoreCase)) ? "\r\n" + L("ToolsLocation", "Tools are included beneath the game's Tools folder.") : string.Empty) +
						"\r\n" + (_package.Readme ?? _package.Website ?? string.Empty);
					JObject manifest = new JObject { ["info"] = new JObject { ["name"] = name, ["author"] = _package.Author ?? "Wabbajack",
						["authorUrl"] = _package.Website ?? string.Empty, ["description"] = _package.Description ?? name, ["domainName"] = _domain, ["installInstructions"] = instructions },
						["mods"] = members, ["modRules"] = rules, ["plugins"] = plugins, ["pluginRules"] = new JObject { ["plugins"] = pluginRules } };
					WriteText(bundle, "collection.json", manifest.ToString());
					WriteText(bundle, "wabbajack/modlist.json", _package.RawManifest);
					WriteText(bundle, "wabbajack/provenance.json", new JObject { ["formatVersion"] = 2, ["source"] = provenance, ["profile"] = _profile.Name,
						["version"] = _package.Version, ["wabbajackVersion"] = _package.WabbajackVersion, ["archiveReferences"] = originals.Count, ["customFiles"] = custom.Count }.ToString());
				}
				using (ZipArchive setup = ZipFile.Open(Path.Combine(workspace, "setup.zip"), ZipArchiveMode.Create))
					foreach (KeyValuePair<string, byte[]> file in _profile.FileContents.Where(file => file.Key.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) &&
						!new[] { "settings.ini", "savepath.ini", "initweaks.ini" }.Contains(file.Key, StringComparer.OrdinalIgnoreCase)))
					{
						ZipArchiveEntry entry = setup.CreateEntry("Documents/" + file.Key, CompressionLevel.Fastest);
						entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
						using (Stream stream = entry.Open()) stream.Write(file.Value, 0, file.Value.Length);
					}
				using (ZipArchive bundle = ZipFile.Open(temporary, ZipArchiveMode.Update))
				{
					ZipArchiveEntry entry = bundle.CreateEntry("wabbajack/setup.zip", CompressionLevel.NoCompression);
					entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
					using (Stream target = entry.Open())
					using (FileStream source = File.OpenRead(Path.Combine(workspace, "setup.zip"))) source.CopyTo(target);
				}
				File.Copy(temporary, destination, true); File.Copy(Path.Combine(workspace, "setup.zip"), setupPath, true);
				CollectionRevisionIdentity revisionIdentity;
				using (SHA256 hash = SHA256.Create())
				using (FileStream source = File.OpenRead(destination))
					revisionIdentity = CollectionRevisionIdentity.FromLocal(identity, new Guid(hash.ComputeHash(source).Take(16).ToArray()));
				return new WabbajackConvertedCollection { BundlePath = destination, SetupPath = setupPath,
					Definition = new CollectionDefinition(identity, name, _package.Author, _package.Description),
					Revision = new CollectionRevision(revisionIdentity, _package.Version, "Wabbajack: " + provenance + "; profile: " + _profile.Name, members.Count) };
			}
			finally
			{
				foreach (SevenZipExtractor extractor in _extractors.Values) extractor.Dispose();
				_extractors.Clear(); _nested.Clear(); _entries.Clear();
				if (Path.GetFullPath(workspace).StartsWith(Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
					Directory.Delete(workspace, true);
			}
		}

		/// <summary>Uses the standard Collection member schema for both referenced archives and custom payloads.</summary>
		private JObject Member(string name, JArray files, JObject source, int phase)
		{
			return new JObject { ["name"] = name, ["version"] = _package.Version ?? "1.0", ["optional"] = false,
				["domainName"] = _domain, ["hashes"] = files, ["phase"] = phase, ["source"] = source };
		}

		/// <summary>Translates the compiled digest into the file-list byte order used by Collections.</summary>
		private static JObject FileHash(string path, string hash)
		{
			byte[] digest = System.Convert.FromBase64String(hash); Array.Reverse(digest);
			return new JObject { ["path"] = path.Replace('\\', '/'), ["xxh64"] = System.Convert.ToBase64String(digest) };
		}

		/// <summary>Routes compiled outputs by profile and explicit game configuration; MO2 metadata is not game content.</summary>
		private List<Output> SelectOutputs()
		{
			List<Output> outputs = new List<Output>();
			HashSet<string> enabled = new HashSet<string>(_profile.Mods, StringComparer.OrdinalIgnoreCase);
			HashSet<string> managerArchives = new HashSet<string>(_package.Directives.Where(directive =>
				string.Equals(directive.To, "ModOrganizer.exe", StringComparison.OrdinalIgnoreCase) && directive.ArchiveHashPath != null)
				.Select(directive => directive.ArchiveHashPath[0]));
			foreach (WabbajackDirective directive in _package.Directives)
			{
				string path = Relative(directive.To);
				string[] parts = path.Split('\\');
				if (path.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)) continue;
				if (parts.Length >= 3 && parts[0].Equals("mods", StringComparison.OrdinalIgnoreCase) && enabled.Contains(parts[1]))
				{
					string relative = string.Join("\\", parts.Skip(2));
					if (!relative.Equals("meta.ini", StringComparison.OrdinalIgnoreCase)) outputs.Add(new Output(directive, parts[1], relative, false));
				}
				else if (parts.Length >= 2 && parts[0].Equals("overwrite", StringComparison.OrdinalIgnoreCase))
					outputs.Add(new Output(directive, "[Wabbajack overwrite]", string.Join("\\", parts.Skip(1)), false));
				else
				{
					string root = (_configuration.GameRootFolders ?? new string[0]).FirstOrDefault(folder => path.StartsWith(Relative(folder) + "\\", StringComparison.OrdinalIgnoreCase));
					string tool = (_configuration.ToolFolders ?? new string[0]).FirstOrDefault(folder => path.StartsWith(Relative(folder) + "\\", StringComparison.OrdinalIgnoreCase));
					if (root != null) outputs.Add(new Output(directive, "[Wabbajack root files]", path.Substring(Relative(root).Length + 1), true));
					else if (tool != null) outputs.Add(new Output(directive, "[Wabbajack root files]", "Tools\\" + Path.GetFileName(tool).TrimStart('_') + path.Substring(Relative(tool).Length), true));
					else if (!parts[0].Equals("mods", StringComparison.OrdinalIgnoreCase) && !parts[0].Equals("profiles", StringComparison.OrdinalIgnoreCase) &&
						!(directive.ArchiveHashPath != null && managerArchives.Contains(directive.ArchiveHashPath[0])) &&
						!new[] { ".compiler_settings", ".mo2_compiler_settings", ".png", ".jpg", ".webp", ".txt", ".md" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) &&
						!new[] { "ModOrganizer.ini", "nxmhandler.ini", "portable.txt", "dump_running_process.bat" }.Contains(path, StringComparer.OrdinalIgnoreCase))
						throw new NotSupportedException(L("UnmappedRootFile", "This compiled file needs a gameRootFolders or toolFolders mapping in the game JSON: ") + path);
				}
			}
			return outputs;
		}

		/// <summary>Applies the compiled file instruction without rerunning a mod's original installer.</summary>
		private void WriteDirective(ZipArchive package, WabbajackDirective directive, Stream target, string workspace, CancellationToken token)
		{
			switch (Kind(directive.Type))
			{
				case "FromArchive":
					// Native archive decoders can seek their destination; compressed ZIP entries cannot seek.
					using (FileStream extracted = new FileStream(Path.Combine(workspace, "archive-output"), FileMode.Create, FileAccess.ReadWrite))
					{ ExtractSource(directive.ArchiveHashPath, extracted, workspace); extracted.Position = 0; extracted.CopyTo(target); }
					break;
				case "PatchedFromArchive":
					string inputPath = Path.Combine(workspace, "patch-input");
					string outputPath = Path.Combine(workspace, "patch-output");
					using (FileStream input = File.Create(inputPath)) ExtractSource(directive.ArchiveHashPath, input, workspace);
					using (FileStream input = File.OpenRead(inputPath))
					using (FileStream output = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite))
					using (MemoryStream delta = new MemoryStream())
					{
						// Octodiff inspects the delta length/position while applying commands.
						using (Stream embedded = package.GetEntry(directive.PatchId).Open()) embedded.CopyTo(delta);
						delta.Position = 0;
						if (directive.FromHash != null && WabbajackHash.Compute(input, token) != directive.FromHash)
							throw new InvalidDataException(L("PatchInputMismatch", "The binary patch input does not match the Wabbajack recipe."));
						input.Position = 0;
						new DeltaApplier().Apply(input, new BinaryDeltaReader(delta, new NullProgressReporter()), output);
						output.Position = 0; output.CopyTo(target);
					}
					break;
				case "InlineFile":
					using (Stream data = package.GetEntry(directive.SourceDataId).Open()) data.CopyTo(target);
					break;
				case "RemappedInlineFile":
					string text;
					using (StreamReader reader = new StreamReader(package.GetEntry(directive.SourceDataId).Open())) text = reader.ReadToEnd();
					text = Remap(text, "GAME", _gameDirectory);
					if (text.Contains("{--||MO2_PATH_MAGIC_")) throw new NotSupportedException(L("Mo2PathRequired", "This file requires an MO2 installation path and cannot be deployed as an NMM file: ") + directive.To);
					text = Remap(text, "DOWNLOAD", _downloadsDirectory);
					byte[] bytes = Encoding.UTF8.GetBytes(text); target.Write(bytes, 0, bytes.Length);
					break;
			}
		}

		/// <summary>Extracts an exact source path, supporting nested archives and bare game-file inputs.</summary>
		private void ExtractSource(string[] parts, Stream target, string workspace)
		{
			string path = _sources[parts[0]];
			for (int index = 1; index < parts.Length - 1; index++)
			{
				string key = path + "\n" + parts[index];
				string nested;
				if (!_nested.TryGetValue(key, out nested))
				{
					nested = Path.Combine(workspace, "nested-" + StableGuid(key).ToString("N") + Path.GetExtension(parts[index]));
					using (FileStream output = File.Create(nested)) ExtractEntry(path, parts[index], output);
					_nested.Add(key, nested);
				}
				path = nested;
			}
			if (parts.Length == 1) using (FileStream input = File.OpenRead(path)) input.CopyTo(target);
			else ExtractEntry(path, parts[parts.Length - 1], target);
		}

		/// <summary>Reuses archive readers while reconstructing exact compiled output paths.</summary>
		private void ExtractEntry(string archivePath, string entryPath, Stream output)
		{
			SevenZipExtractor extractor;
			if (!_extractors.TryGetValue(archivePath, out extractor))
			{
				_extractors.Add(archivePath, extractor = Archive.GetExtractor(archivePath));
				_entries.Add(archivePath, extractor.ArchiveFileData.Where(entry => !entry.IsDirectory)
					.ToDictionary(entry => entry.FileName.Replace('/', '\\'), entry => entry.Index, StringComparer.OrdinalIgnoreCase));
			}
			extractor.ExtractFile(_entries[archivePath][entryPath.Replace('/', '\\')], output);
		}

		/// <summary>Uses the same path markers embedded by Wabbajack's compiler.</summary>
		private static string Remap(string text, string kind, string path)
		{
			return text.Replace("{--||" + kind + "_PATH_MAGIC_BACK||--}", path)
				.Replace("{--||" + kind + "_PATH_MAGIC_DOUBLE_BACK||--}", path.Replace("\\", "\\\\"))
				.Replace("{--||" + kind + "_PATH_MAGIC_FORWARD||--}", path.Replace('\\', '/'));
		}

		/// <summary>Normalizes the short and assembly-qualified type aliases used by Wabbajack packages.</summary>
		internal static string Kind(string type) { return (type ?? string.Empty).Split(',')[0].Trim(); }

		/// <summary>Rejects rooted and parent-traversing paths before writing reconstructed files.</summary>
		internal static string Relative(string path)
		{
			if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(":")) throw new InvalidDataException(L("RelativePathRequired", "Expected a relative Wabbajack path."));
			string[] parts = path.Replace('/', '\\').Split('\\');
			if (parts.Any(part => part.Length == 0 || part == "." || part == "..")) throw new InvalidDataException(L("InvalidRelativePath", "Invalid Wabbajack relative path."));
			return string.Join("\\", parts);
		}

		/// <summary>Resolves a recipe path beneath its explicitly selected source directory.</summary>
		internal static string Inside(string directory, string relative) { return Path.Combine(directory, Relative(relative)); }

		/// <summary>Derives a stable NMM-owned identity for repeated imports of the same Wabbajack profile.</summary>
		private static Guid StableGuid(string text)
		{
			using (SHA256 hash = SHA256.Create()) { byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(text)); return new Guid(bytes.Take(16).ToArray()); }
		}

		/// <summary>Writes retained text into the generated Collection or setup ZIP.</summary>
		private static void WriteText(ZipArchive archive, string path, string text)
		{
			ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
			entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
			using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(text);
		}

		/// <summary>Resolves preparation messages through NMM's language system.</summary>
		private static string L(string key, string fallback) { return LanguageManager.Get("Wabbajack." + key, fallback); }

		/// <summary>One selected native output and the compiled instruction that produces it.</summary>
		private sealed class Output
		{
			/// <summary>Creates one exact output mapping.</summary>
			internal Output(WabbajackDirective directive, string member, string path, bool gameRoot) { Directive = directive; Member = member; Path = path; GameRoot = gameRoot; }
			internal WabbajackDirective Directive { get; }
			internal string Member { get; }
			internal string Path { get; }
			internal bool GameRoot { get; }
		}
	}
}
