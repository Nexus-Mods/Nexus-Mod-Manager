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

	/// <summary>Reconstructs one compiled profile into the Collection bundle format already consumed by NMM.</summary>
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

		/// <summary>Chooses enabled mod outputs and the game JSON's explicit root/tool mappings.</summary>
		public WabbajackCollectionConverter(WabbajackPackage package, WabbajackProfile profile, GameModeWabbajackDefinition configuration,
			string gameDirectory, string nexusDomain)
		{
			_package = package; _profile = profile; _configuration = configuration; _gameDirectory = gameDirectory; _domain = nexusDomain;
			if (!string.Equals(package.GameType, configuration.Game, StringComparison.OrdinalIgnoreCase))
				throw new NotSupportedException(L("GameMismatch", "This Wabbajack list belongs to a different game."));
			_outputs = SelectOutputs();
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
				foreach (string file in Directory.EnumerateFiles(directory))
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
			WabbajackClient client, IProgress<string> progress, CancellationToken token)
		{
			Directory.CreateDirectory(downloadDirectory);
			foreach (WabbajackArchive archive in missing)
			{
				token.ThrowIfCancellationRequested();
				string kind = Kind(archive.State?.Type);
				string url = archive.State?.Url;
				if (kind.StartsWith("Nexus", StringComparison.Ordinal))
				{
					if (repository.UserStatus?.IsPremium != true || !string.Equals(archive.State.GameName, _package.GameType, StringComparison.OrdinalIgnoreCase)) continue;
					List<RepositoryDownloadLink> links = await Task.Run(() => repository.GetFilePartInfo(archive.State.ModId.ToString(), archive.State.FileId.ToString()), token).ConfigureAwait(false);
					url = links.First().Uri.AbsoluteUri;
				}
				else if (!(kind.StartsWith("Http", StringComparison.Ordinal) || kind.StartsWith("WabbajackCDN", StringComparison.Ordinal))) continue;
				else if (archive.State.AdditionalData != null && archive.State.AdditionalData.ContainsKey("Headers") && archive.State.AdditionalData["Headers"].HasValues) continue;
				if (string.IsNullOrWhiteSpace(url)) continue;
				progress?.Report(L("DownloadingSource", "Downloading source: ") + archive.Name);
				await client.DownloadFileAsync(url, Inside(downloadDirectory, Path.GetFileName(archive.Name)), archive.Hash, null, token).ConfigureAwait(false);
			}
		}

		/// <summary>Writes reconstructed bundled members and exact file/plugin priority into a normal Collection ZIP.</summary>
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
			try
			{
				// Native deployment can replace original masters; retain their inputs for later profile/version conversions.
				foreach (WabbajackArchive archive in RequiredArchives.Where(archive => Kind(archive.State?.Type).StartsWith("GameFileSource", StringComparison.Ordinal)))
				{
					string originals = Path.Combine(outputDirectory, "Wabbajack Sources"); Directory.CreateDirectory(originals);
					string retained = Inside(originals, archive.Hash.Replace('/', '_').Replace('+', '-') + "-" + Path.GetFileName(archive.Name));
					if (!string.Equals(Path.GetFullPath(_sources[archive.Hash]), Path.GetFullPath(retained), StringComparison.OrdinalIgnoreCase)) File.Copy(_sources[archive.Hash], retained, true);
					_sources[archive.Hash] = retained;
				}
				JArray members = new JArray();
				JArray rules = new JArray();
				using (ZipArchive source = ZipFile.OpenRead(_package.FilePath))
				using (ZipArchive bundle = ZipFile.Open(temporary, ZipArchiveMode.Create))
				{
					List<string> order = _profile.Mods.ToList();
					// MO2 overlays enabled mods over the physical game folder, then applies overwrite last.
					order.Insert(0, "[Wabbajack root files]"); order.Add("[Wabbajack overwrite]");
					string previousTag = null;
					int completed = 0;
					foreach (IGrouping<string, Output> group in _outputs.GroupBy(output => output.Member).OrderBy(group => order.IndexOf(group.Key)))
					{
						string tag = "wb-" + StableGuid(group.Key).ToString("N");
						JArray files = new JArray();
						long size = 0;
						foreach (Output output in group)
						{
							token.ThrowIfCancellationRequested();
							completed++;
							if (completed % 50 == 0 || completed == _outputs.Count) progress?.Report(string.Format(L("Reconstructing", "Reconstructing {0}: {1}/{2}"), group.Key, completed, _outputs.Count));
							ZipArchiveEntry entry = bundle.CreateEntry("bundled/" + tag + "/" + Relative(output.Path).Replace('\\', '/'), CompressionLevel.Fastest);
							entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
							using (Stream entryStream = entry.Open())
							using (WabbajackOutputStream target = new WabbajackOutputStream(entryStream, token))
							{
								WriteDirective(source, output.Directive, target, workspace, token);
								if (Kind(output.Directive.Type) != "RemappedInlineFile" &&
									(target.Length != output.Directive.Size || target.Hash != output.Directive.Hash))
									throw new InvalidDataException(L("OutputMismatch", "Reconstructed file does not match the Wabbajack recipe: ") + output.Directive.To);
								byte[] digest = System.Convert.FromBase64String(target.Hash); Array.Reverse(digest);
								files.Add(new JObject { ["path"] = output.Path.Replace('\\', '/'), ["xxh64"] = System.Convert.ToBase64String(digest) });
								size += target.Length;
							}
						}
						JObject member = new JObject { ["name"] = group.Key, ["version"] = _package.Version ?? "1.0", ["optional"] = false,
							["domainName"] = _domain, ["hashes"] = files, ["phase"] = members.Count,
							["source"] = new JObject { ["type"] = "bundle", ["fileSize"] = Math.Max(1, size), ["fileExpression"] = tag, ["tag"] = tag } };
						if (group.Any(output => output.GameRoot)) member["details"] = new JObject { ["type"] = "enb" };
						members.Add(member);
						if (previousTag != null) rules.Add(new JObject { ["type"] = "after", ["source"] = new JObject { ["tag"] = tag, ["versionMatch"] = "*" },
							["reference"] = new JObject { ["tag"] = previousTag, ["versionMatch"] = "*" } });
						previousTag = tag;
					}
					JArray plugins = new JArray(_profile.Plugins.Select(plugin => new JObject { ["name"] = plugin, ["enabled"] = true }));
					JArray pluginRules = new JArray();
					for (int index = 1; index < _profile.LoadOrder.Count; index++) pluginRules.Add(new JObject { ["name"] = _profile.LoadOrder[index], ["after"] = new JArray(_profile.LoadOrder[index - 1]) });
					string instructions = L("ConvertedInstructions", "Wabbajack profile: ") + _profile.Name + "\r\n" +
						L("ApplySetupInstructions", "After installation, apply the companion setup ZIP from Setup & instructions. Follow the author's post-install steps, including any game patchers.") +
						(_outputs.Any(output => output.GameRoot && output.Path.StartsWith("Tools\\", StringComparison.OrdinalIgnoreCase))
							? "\r\n" + L("ToolsLocation", "Tools are included beneath the game's Tools folder.") : string.Empty) +
						"\r\n" + (_package.Readme ?? _package.Website ?? string.Empty);
					JObject manifest = new JObject { ["info"] = new JObject { ["name"] = name, ["author"] = _package.Author ?? "Wabbajack",
						["description"] = _package.Description ?? name, ["domainName"] = _domain, ["installInstructions"] = instructions },
						["mods"] = members, ["modRules"] = rules, ["plugins"] = plugins, ["pluginRules"] = new JObject { ["plugins"] = pluginRules } };
					WriteText(bundle, "collection.json", manifest.ToString());
					WriteText(bundle, "wabbajack/modlist.json", _package.RawManifest);
					WriteText(bundle, "wabbajack/provenance.json", new JObject { ["formatVersion"] = 1, ["source"] = provenance, ["profile"] = _profile.Name,
						["version"] = _package.Version, ["wabbajackVersion"] = _package.WabbajackVersion }.ToString());
				}
				using (ZipArchive setup = ZipFile.Open(Path.Combine(workspace, "setup.zip"), ZipArchiveMode.Create))
					foreach (KeyValuePair<string, string> file in _profile.Files.Where(file => file.Key.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) &&
						!new[] { "settings.ini", "savepath.ini", "initweaks.ini" }.Contains(file.Key, StringComparer.OrdinalIgnoreCase)))
						{
							byte[] bytes = _profile.FileContents[file.Key];
							ZipArchiveEntry entry = setup.CreateEntry("Documents/" + file.Key, CompressionLevel.Fastest);
							entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
							using (Stream stream = entry.Open()) stream.Write(bytes, 0, bytes.Length);
						}
				File.Copy(temporary, destination, true);
				File.Copy(Path.Combine(workspace, "setup.zip"), setupPath, true);
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
				case "FromArchive": ExtractSource(directive.ArchiveHashPath, target, workspace); break;
				case "PatchedFromArchive":
					string inputPath = Path.Combine(workspace, "patch-input");
					string outputPath = Path.Combine(workspace, "patch-output");
					using (FileStream input = File.Create(inputPath)) ExtractSource(directive.ArchiveHashPath, input, workspace);
					using (FileStream input = File.OpenRead(inputPath))
					using (FileStream output = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite))
					using (Stream delta = package.GetEntry(directive.PatchId).Open())
					{
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
