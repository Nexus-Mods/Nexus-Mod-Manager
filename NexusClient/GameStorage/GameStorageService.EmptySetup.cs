using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.Settings;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.GameStorage
{
    public partial class GameStorageService
    {
        /// <summary>Plans explicit cleanup without opening a game session, migrating stores or following directory links.</summary>
        internal EmptySetupCleanupPlan PlanEmptySetupCleanup(string gameId)
        {
            var plan = new EmptySetupCleanupPlan();
            try
            {
                var registries = new Dictionary<string, GameStorageRegistry>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in new[] { RegistryPath, LastKnownGoodPath })
                {
                    plan.ObserveFile(file);
                    GameStorageMetadataReadResult<GameStorageRegistry> read = ReadRegistryResult(file);
                    if (read.Status == GameStorageMetadataReadStatus.Missing) continue;
                    if (read.Status != GameStorageMetadataReadStatus.Valid)
                        throw new InvalidDataException(LanguageManager.Get("GameDiscovery.CleanupInvalidRegistry", "Storage history is unreadable or uses a newer format."));
                    read.Value.KnownStorages = read.Value.KnownStorages ?? new List<GameStorageRegistryEntry>();
                    read.Value.ActiveStorageByGame = read.Value.ActiveStorageByGame ?? new Dictionary<string, string>();
                    registries.Add(file, read.Value);
                }
                ISettings settings = _environmentInfo.Settings;
                var saved = new GameStoragePathSet
                {
                    GameId = gameId,
                    GameInstallPath = GetSettingValue(settings.InstallationPaths, gameId),
                    InstallInfoPath = GetSettingValue(settings.InstallInfoFolder, gameId),
                    ModsPath = GetSettingValue(settings.ModFolder, gameId),
                    VirtualInstallPath = NormalizeVirtualInstallDirectory(GetSettingValue(settings.VirtualFolder, gameId)),
                    LinkFolderPath = GetSettingValue(settings.HDLinkFolder, gameId)
                };
                // Direct Install still keeps an empty virtual topology beneath InstallInfo.
                if (String.IsNullOrWhiteSpace(saved.VirtualInstallPath) && !String.IsNullOrWhiteSpace(saved.InstallInfoPath))
                    saved.VirtualInstallPath = Path.Combine(saved.InstallInfoPath, GameStorageConstants.VirtualInstallDirectoryName);
                var entries = registries.Values.SelectMany(registry => registry.KnownStorages).Where(entry => entry != null).ToList();
                var candidates = entries.Where(entry => String.Equals(entry.GameId, gameId, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => new GameStoragePathSet { GameId = gameId, InstallInfoPath = entry.InstallInfoPath,
                        ModsPath = entry.ModsPath, VirtualInstallPath = CleanupVirtualDirectory(entry.InstallInfoPath, entry.VirtualInstallPath), LinkFolderPath = entry.LinkFolderPath }).ToList();
                candidates.Insert(0, saved);
                var cleaned = new List<GameStoragePathSet>();
                foreach (GameStoragePathSet paths in candidates)
                {
                    string[] folders = CleanupFolders(paths).ToArray();
                    if (folders.Length == 0 || cleaned.Any(previous => CleanupFolders(previous).SequenceEqual(folders, StringComparer.OrdinalIgnoreCase))) continue;
                    try
                    {
                        var local = new EmptySetupCleanupPlan();
                        var otherPaths = entries.Where(entry => !String.Equals(entry.GameId, gameId, StringComparison.OrdinalIgnoreCase))
                            .SelectMany(entry => new[] { entry.InstallInfoPath, entry.ModsPath, entry.VirtualInstallPath, entry.LinkFolderPath });
                        foreach (var dictionary in new[] { settings.InstallInfoFolder, settings.ModFolder, settings.VirtualFolder, settings.HDLinkFolder })
                            otherPaths = otherPaths.Concat(dictionary.Where(pair => !String.Equals(pair.Key, gameId, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value));
                        string[] protectedPaths = settings.InstallationPaths.Values.Concat(settings.ExecutablePaths.Values).Concat(otherPaths)
                            .Where(path => !String.IsNullOrWhiteSpace(path)).ToArray();
                        if (folders.Any(folder => protectedPaths.Any(other => CleanupPathsOverlap(folder, other))))
                            throw new InvalidDataException(LanguageManager.Get("GameDiscovery.CleanupShared", "Shared storage or a game installation is preserved."));

                        foreach (GameStorageRegistryEntry history in entries.Where(entry => String.Equals(entry.GameId, gameId, StringComparison.OrdinalIgnoreCase) &&
                            CleanupSamePath(entry.InstallInfoPath, paths.InstallInfoPath) && CleanupSamePath(CleanupVirtualDirectory(entry.InstallInfoPath, entry.VirtualInstallPath), paths.VirtualInstallPath)))
                        {
                            bool missingLog = history.LastKnownInstallLogPresent && (String.IsNullOrWhiteSpace(paths.InstallInfoPath) ||
                                !File.Exists(Path.Combine(paths.InstallInfoPath, "InstallLog.sqlite")) && !File.Exists(Path.Combine(paths.InstallInfoPath, "InstallLog.xml")));
                            bool missingTopology = history.LastKnownVirtualFileCount > 0 && (String.IsNullOrWhiteSpace(paths.VirtualInstallPath) ||
                                !File.Exists(Path.Combine(paths.VirtualInstallPath, "VirtualModConfig.sqlite")) && !File.Exists(Path.Combine(paths.VirtualInstallPath, "VirtualModConfig.xml")));
                            if (missingLog || missingTopology)
                                throw new InvalidDataException(LanguageManager.Get("GameDiscovery.CleanupMissingAuthority", "Recorded native state is missing and cannot be verified as empty."));
                        }
                        string storageRoot = TryGetSharedStorageRoot(paths);
                        if (!String.IsNullOrWhiteSpace(storageRoot))
                        {
                            string rootFile = Path.Combine(storageRoot, GameStorageConstants.RootManifestFileName);
                            local.ObserveFile(rootFile);
                            GameStorageMetadataReadResult<GameStorageRootManifest> root = ReadRootManifestResult(storageRoot);
                            if (root.Status != GameStorageMetadataReadStatus.Missing)
                            {
                                if (root.Status != GameStorageMetadataReadStatus.Valid || !String.Equals(root.Value.GameId, gameId, StringComparison.OrdinalIgnoreCase) ||
                                    !RootManifestFolderMatches(storageRoot, root.Value, GameStorageFolderRole.InstallInfo, paths.InstallInfoPath) ||
                                    !RootManifestFolderMatches(storageRoot, root.Value, GameStorageFolderRole.Mods, paths.ModsPath) ||
                                    !RootManifestFolderMatches(storageRoot, root.Value, GameStorageFolderRole.VirtualInstall, paths.VirtualInstallPath))
                                    throw new InvalidDataException(LanguageManager.Get("GameDiscovery.CleanupShared", "Shared storage or a game installation is preserved."));
                                local.RemoveFile(Path.Combine(storageRoot, GameStorageConstants.RootManifestFileName));
                                local.AddEmptyDirectory(storageRoot);
                            }
                        }
                        // Keep both native stores together: unknown files or recovery state preserve the entire native setup.
                        foreach (string folder in new[] { paths.InstallInfoPath, paths.VirtualInstallPath }.Where(path => !String.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                            InspectEmptySetupDirectory(local, folder, paths);
                        if (!String.IsNullOrWhiteSpace(paths.LinkFolderPath))
                        {
                            InspectEmptySetupDirectory(local, Path.Combine(paths.LinkFolderPath, gameId, "NMMLink"), paths);
                            string linkManifest = Path.Combine(paths.LinkFolderPath, GameStorageConstants.FolderManifestFileName);
                            local.ObserveFile(linkManifest);
                            var link = ReadFolderManifestResult(paths.LinkFolderPath);
                            if (link.Status != GameStorageMetadataReadStatus.Missing)
                            {
                                if (link.Status != GameStorageMetadataReadStatus.Valid || link.Value.FolderRole != GameStorageFolderRole.LinkFolder ||
                                    !String.Equals(link.Value.GameId, gameId, StringComparison.OrdinalIgnoreCase) ||
                                    !GetManifestBindings(link.Value).All(binding => String.Equals(binding.GameId, gameId, StringComparison.OrdinalIgnoreCase)))
                                    throw new InvalidDataException(LanguageManager.Get("GameDiscovery.CleanupShared", "Shared storage or a game installation is preserved."));
                                local.RemoveFile(linkManifest);
                            }
                            local.AddEmptyDirectory(Path.Combine(paths.LinkFolderPath, gameId));
                            local.AddEmptyDirectory(paths.LinkFolderPath);
                        }
                        if (!String.IsNullOrWhiteSpace(paths.ModsPath))
                        {
                            var mods = new EmptySetupCleanupPlan();
                            try
                            {
                                InspectEmptySetupDirectory(mods, paths.ModsPath, paths);
                                local.Merge(mods);
                            }
                            catch (Exception error) when (IsCleanupInspectionError(error))
                            {
                                local.Retained.Add(paths.ModsPath + ": " + error.Message);
                            }
                        }
                        plan.Merge(local);
                        cleaned.Add(paths);
                    }
                    catch (Exception error) when (IsCleanupInspectionError(error))
                    {
                        plan.Retained.Add(String.Join(", ", folders) + ": " + error.Message);
                    }
                }
                foreach (KeyValuePair<string, GameStorageRegistry> item in registries)
                {
                    var registry = item.Value;
                    var removeIndexes = registry.KnownStorages.Select((entry, index) => new { Entry = entry, Index = index })
                        .Where(itemEntry => itemEntry.Entry != null && String.Equals(itemEntry.Entry.GameId, gameId, StringComparison.OrdinalIgnoreCase) &&
                            cleaned.Any(paths => CleanupSamePath(itemEntry.Entry.InstallInfoPath, paths.InstallInfoPath) && CleanupSamePath(CleanupVirtualDirectory(itemEntry.Entry.InstallInfoPath, itemEntry.Entry.VirtualInstallPath), paths.VirtualInstallPath) &&
                                CleanupSamePath(itemEntry.Entry.ModsPath, paths.ModsPath) && CleanupSamePath(itemEntry.Entry.LinkFolderPath, paths.LinkFolderPath)))
                        .Select(itemEntry => itemEntry.Index).OrderByDescending(index => index).ToArray();
                    if (removeIndexes.Length == 0) continue;
                    // Retain unrecognized properties in supported-schema registries, including those of other games.
                    JObject document = JObject.Parse(File.ReadAllText(item.Key));
                    JArray records = document.GetValue("KnownStorages", StringComparison.OrdinalIgnoreCase) as JArray;
                    if (records == null) throw new InvalidDataException(LanguageManager.Get("GameDiscovery.CleanupInvalidRegistry", "Storage history is unreadable or uses a newer format."));
                    foreach (int index in removeIndexes)
                    {
                        records.RemoveAt(index);
                        registry.KnownStorages.RemoveAt(index);
                    }
                    JObject active = document.GetValue("ActiveStorageByGame", StringComparison.OrdinalIgnoreCase) as JObject;
                    if (active != null)
                        foreach (JProperty property in active.Properties().Where(property => String.Equals(property.Name, gameId, StringComparison.OrdinalIgnoreCase)).ToArray())
                            if (!registry.KnownStorages.Any(entry => entry != null && String.Equals(entry.GameId, gameId, StringComparison.OrdinalIgnoreCase) &&
                                String.Equals(entry.StorageId, (string)property.Value, StringComparison.OrdinalIgnoreCase)))
                                property.Remove();
                    plan.ReplaceFile(item.Key, Encoding.UTF8.GetBytes(document.ToString(Formatting.Indented)));
                }
            }
            catch (Exception error) when (IsCleanupInspectionError(error))
            {
                // A malformed global registry cannot establish exclusive ownership of any folder.
                plan = new EmptySetupCleanupPlan();
                plan.Retained.Add(error.Message);
            }
            return plan;
        }

        /// <summary>Includes the Direct Install topology while retaining the registry's serialized optional path.</summary>
        private static string CleanupVirtualDirectory(string installInfo, string virtualInstall)
        {
            return !String.IsNullOrWhiteSpace(virtualInstall) || String.IsNullOrWhiteSpace(installInfo)
                ? virtualInstall : Path.Combine(installInfo, GameStorageConstants.VirtualInstallDirectoryName);
        }

        /// <summary>Lists configured role directories without treating the VirtualFolder parent as disposable storage.</summary>
        private static IEnumerable<string> CleanupFolders(GameStoragePathSet paths)
        {
            return new[] { paths.InstallInfoPath, paths.ModsPath, paths.VirtualInstallPath, paths.LinkFolderPath }
                .Where(path => !String.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Requires exclusive non-linked directories and records only recognized, empty NMM artifacts.</summary>
        private void InspectEmptySetupDirectory(EmptySetupCleanupPlan plan, string directory, GameStoragePathSet paths)
        {
            EmptySetupCleanupPlan.RequirePlainPath(directory);
            plan.ObserveDirectory(directory);
            if (!Directory.Exists(directory)) return;
            if (plan.Directories.Count > 1000) throw new InvalidDataException(LanguageManager.Get("GameDiscovery.CleanupUnknown", "Contains user data or unrecognized setup files."));
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                EmptySetupCleanupPlan.RequirePlainPath(file);
                plan.ObserveFile(file);
                string name = Path.GetFileName(file);
                bool empty = false;
                if (String.Equals(name, GameStorageConstants.FolderManifestFileName, StringComparison.OrdinalIgnoreCase))
                {
                    var read = ReadFolderManifestResult(directory);
                    empty = read.Status == GameStorageMetadataReadStatus.Valid && String.Equals(read.Value.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) &&
                        GetManifestBindings(read.Value).All(binding => String.Equals(binding.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase));
                }
                else if (String.Equals(name, GameStorageConstants.RootManifestFileName, StringComparison.OrdinalIgnoreCase))
                {
                    var read = ReadRootManifestResult(directory);
                    empty = read.Status == GameStorageMetadataReadStatus.Valid && String.Equals(read.Value.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) &&
                        RootManifestFolderMatches(directory, read.Value, GameStorageFolderRole.InstallInfo, paths.InstallInfoPath) &&
                        RootManifestFolderMatches(directory, read.Value, GameStorageFolderRole.Mods, paths.ModsPath) &&
                        RootManifestFolderMatches(directory, read.Value, GameStorageFolderRole.VirtualInstall, paths.VirtualInstallPath);
                }
                else if (CleanupSamePath(directory, paths.InstallInfoPath))
                    empty = IsEmptyNativeSetupFile(file, "InstallLog");
                if (!empty && CleanupSamePath(directory, paths.VirtualInstallPath))
                    empty = IsEmptyNativeSetupFile(file, "VirtualModConfig") || IsEmptySetupXml(file, "IniEdits.xml", "virtualModActivator", new[] { "iniEdits" });
                if (!empty && (String.Equals(name, "ProfileManagerCfg.xml", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(name, "BackedProfileManagerCfg.xml", StringComparison.OrdinalIgnoreCase)))
                    empty = IsEmptySetupXml(file, name, "profileManager", new[] { "profileList" });
                if (!empty) throw new InvalidDataException(LanguageManager.Get("GameDiscovery.CleanupUnknown", "Contains user data or unrecognized setup files."));
                plan.RemoveFile(file);
            }
            foreach (string child in Directory.EnumerateDirectories(directory))
                InspectEmptySetupDirectory(plan, child, paths);
        }

        /// <summary>Validates empty XML, SQLite and authority siblings without initializing or migrating either native store.</summary>
        internal static bool IsEmptyNativeSetupFile(string path, string stem)
        {
            string name = Path.GetFileName(path);
            if (String.Equals(name, stem + ".xml", StringComparison.OrdinalIgnoreCase) || String.Equals(name, stem + ".xml.pre-sqlite", StringComparison.OrdinalIgnoreCase))
                return IsEmptySetupXml(path, name, stem == "InstallLog" ? "installLog" : "virtualModActivator",
                    stem == "InstallLog" ? new[] { "modList", "dataFiles", "iniEdits", "gameSpecificEdits", "deploymentFiles" } : new[] { "modList" });
            if (String.Equals(name, stem + ".sqlite.authority", StringComparison.OrdinalIgnoreCase))
                return File.Exists(Path.Combine(Path.GetDirectoryName(path), stem + ".sqlite")) &&
                    File.ReadAllText(path) == "nmm-ce.native-sqlite-authority/1\n" + stem + ".sqlite";
            if (!String.Equals(name, stem + ".sqlite", StringComparison.OrdinalIgnoreCase)) return false;
            if (File.Exists(path + "-wal") || File.Exists(path + "-shm") || File.Exists(path + "-journal")) return false;
            using (var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, ReadOnly = true, FailIfMissing = true, Pooling = false, Enlist = false }.ConnectionString))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = stem == "InstallLog"
                        ? "SELECT Value FROM Metadata WHERE Key='schema_version'"
                        : "SELECT Value FROM StoreMetadata WHERE Key='schema_version'";
                    string schema = Convert.ToString(command.ExecuteScalar());
                    if (stem == "InstallLog" ? schema != "1" : schema != "1" && schema != "2") return false;
                    command.CommandText = stem == "InstallLog" ? "SELECT Value FROM Metadata WHERE Key='file_version'"
                        : "SELECT Value FROM StoreMetadata WHERE Key='file_version'";
                    Version version;
                    Version supported = stem == "InstallLog" ? ModManagement.InstallationLog.InstallLog.CurrentVersion : ModManagement.VirtualModActivator.CurrentVersion;
                    if (!Version.TryParse(Convert.ToString(command.ExecuteScalar()), out version) || version > supported) return false;
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                    string[] knownTables = stem == "InstallLog" ? new[] { "Metadata", "Mods", "Items", "Owners" }
                        : new[] { "StoreMetadata", "VirtualMods", "VirtualLinks", "sqlite_sequence" };
                    using (SQLiteDataReader reader = command.ExecuteReader())
                        while (reader.Read()) if (!knownTables.Contains(reader.GetString(0), StringComparer.Ordinal)) return false;
                    command.CommandText = stem == "InstallLog"
                        ? "SELECT (SELECT COUNT(*) FROM Mods WHERE Path NOT IN ('Dummy Mod: ORIGINAL_VALUE','Dummy Mod: MOD_MANAGER_VALUE')) + (SELECT COUNT(*) FROM Items) + (SELECT COUNT(*) FROM Owners)"
                        : "SELECT (SELECT COUNT(*) FROM VirtualMods) + (SELECT COUNT(*) FROM VirtualLinks)";
                    if (Convert.ToInt64(command.ExecuteScalar()) != 0) return false;
                    command.CommandText = "PRAGMA quick_check";
                    return String.Equals(Convert.ToString(command.ExecuteScalar()), "ok", StringComparison.Ordinal);
                }
            }
        }

        /// <summary>Accepts only known empty containers and the two built-in InstallLog sentinel registrations.</summary>
        private static bool IsEmptySetupXml(string path, string expectedName, string rootName, string[] containers)
        {
            if (!String.Equals(Path.GetFileName(path), expectedName, StringComparison.OrdinalIgnoreCase)) return false;
            XElement root = XDocument.Load(path).Root;
            if (root == null || root.Name != rootName) return false;
            Version version;
            Version supported = rootName == "installLog" ? ModManagement.InstallationLog.InstallLog.CurrentVersion
                : rootName == "virtualModActivator" ? ModManagement.VirtualModActivator.CurrentVersion : new Version("0.1.0.0");
            if (!Version.TryParse((string)root.Attribute("fileVersion"), out version) || version > supported) return false;
            return root.Elements().All(container => containers.Contains(container.Name.LocalName) &&
                (container.Name == "modList" && rootName == "installLog"
                    ? container.Elements().All(mod => mod.Name == "mod" &&
                        ((string)mod.Attribute("path") == "Dummy Mod: ORIGINAL_VALUE" || (string)mod.Attribute("path") == "Dummy Mod: MOD_MANAGER_VALUE"))
                    : !container.HasElements && String.IsNullOrWhiteSpace(container.Value)));
        }

        /// <summary>Compares complete normalized paths, including absent optional role paths.</summary>
        private static bool CleanupSamePath(string first, string second)
        {
            if (String.IsNullOrWhiteSpace(first) || String.IsNullOrWhiteSpace(second))
                return String.IsNullOrWhiteSpace(first) && String.IsNullOrWhiteSpace(second);
            return String.Equals(Path.GetFullPath(first).TrimEnd('\\', '/'), Path.GetFullPath(second).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Protects shared, nested and ancestor storage paths from cleanup by another mode.</summary>
        private static bool CleanupPathsOverlap(string first, string second)
        {
            string left = Path.GetFullPath(first).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string right = Path.GetFullPath(second).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return left.StartsWith(right, StringComparison.OrdinalIgnoreCase) || right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Limits best-effort inspection failures to recoverable data and filesystem errors.</summary>
        private static bool IsCleanupInspectionError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException ||
                error is System.Security.SecurityException || error is JsonException || error is System.Xml.XmlException || error is SQLiteException;
        }
    }

    /// <summary>A reviewable snapshot of empty setup files; failed persistence restores changed files before returning.</summary>
    internal sealed class EmptySetupCleanupPlan
    {
        private readonly Dictionary<string, byte[]> _observed = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _changes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string[]> _contents = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> Directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal readonly List<string> Retained = new List<string>();
        internal bool HasChanges => _changes.Count != 0 || Directories.Count != 0;
        internal IEnumerable<string> ChangedPaths => _changes.Keys.Concat(Directories).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        /// <summary>Rejects roots and junctions, including links in any existing ancestor of a configured path.</summary>
        internal static void RequirePlainPath(string path)
        {
            if (!Path.IsPathRooted(path) || !Directory.Exists(Path.GetPathRoot(Path.GetFullPath(path))) || String.Equals(Path.GetFullPath(path).TrimEnd('\\', '/'), Path.GetPathRoot(Path.GetFullPath(path)).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new IOException(LanguageManager.Get("GameDiscovery.CleanupUnsafePath", "Drive roots and linked folders are preserved."));
            for (string current = Path.GetFullPath(path); !String.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException(LanguageManager.Get("GameDiscovery.CleanupUnsafePath", "Drive roots and linked folders are preserved."));
        }

        /// <summary>Snapshots a bounded metadata file before inspection or mutation.</summary>
        internal void ObserveFile(string path)
        {
            if (_observed.ContainsKey(path)) return;
            RequirePlainPath(path);
            if (!File.Exists(path)) { _observed[path] = null; return; }
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new IOException(LanguageManager.Get("GameDiscovery.CleanupUnknown", "Contains user data or unrecognized setup files."));
            _observed[path] = File.ReadAllBytes(path);
        }

        /// <summary>Records a file whose validated contents may be removed.</summary>
        internal void RemoveFile(string path) { ObserveFile(path); _changes[path] = null; }

        /// <summary>Records a registry rewrite without changing unrelated entries.</summary>
        internal void ReplaceFile(string path, byte[] bytes) { _changes[path] = bytes; }

        /// <summary>Snapshots directory membership so concurrent additions invalidate the proposed cleanup.</summary>
        internal void ObserveDirectory(string path)
        {
            RequirePlainPath(path);
            if (!Directory.Exists(path)) { _contents[path] = null; return; }
            Directories.Add(path);
            string[] entries = Directory.EnumerateFileSystemEntries(path).Take(1001).ToArray();
            if (entries.Length > 1000) throw new IOException(LanguageManager.Get("GameDiscovery.CleanupUnknown", "Contains user data or unrecognized setup files."));
            _contents[path] = entries.OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>Attempts to remove only an empty, verified storage root after settings have been saved.</summary>
        internal void AddEmptyDirectory(string path) { RequirePlainPath(path); Directories.Add(path); }

        /// <summary>Combines successfully inspected role folders without publishing a partial inspection.</summary>
        internal void Merge(EmptySetupCleanupPlan other)
        {
            foreach (var item in other._observed)
            {
                byte[] previous;
                if (_observed.TryGetValue(item.Key, out previous) &&
                    (previous == null ? item.Value != null : item.Value == null || !previous.SequenceEqual(item.Value)))
                    throw new IOException(LanguageManager.Get("GameDiscovery.CleanupChanged", "Setup files changed. Review the cleanup again."));
            }
            foreach (var item in other._contents)
            {
                string[] previous;
                if (_contents.TryGetValue(item.Key, out previous) &&
                    (previous == null ? item.Value != null : item.Value == null || !previous.SequenceEqual(item.Value, StringComparer.OrdinalIgnoreCase)))
                    throw new IOException(LanguageManager.Get("GameDiscovery.CleanupChanged", "Setup files changed. Review the cleanup again."));
            }
            foreach (var item in other._observed) _observed[item.Key] = item.Value;
            foreach (var item in other._changes) _changes[item.Key] = item.Value;
            foreach (var item in other._contents) _contents[item.Key] = item.Value;
            Directories.UnionWith(other.Directories);
            Retained.AddRange(other.Retained);
        }

        /// <summary>Revalidates the preview, rolls file changes back on failure, and never recursively deletes directories.</summary>
        internal void Execute(Action saveSettings)
        {
            foreach (var item in _observed)
            {
                RequirePlainPath(item.Key);
                if (item.Value == null ? File.Exists(item.Key) || Directory.Exists(item.Key)
                    : !File.Exists(item.Key) || !File.ReadAllBytes(item.Key).SequenceEqual(item.Value))
                    throw new IOException(LanguageManager.Get("GameDiscovery.CleanupChanged", "Setup files changed. Review the cleanup again."));
            }
            foreach (var item in _contents)
            {
                RequirePlainPath(item.Key);
                if (item.Value == null ? Directory.Exists(item.Key) || File.Exists(item.Key) :
                    !Directory.Exists(item.Key) || !Directory.GetFileSystemEntries(item.Key).OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase).SequenceEqual(item.Value, StringComparer.OrdinalIgnoreCase))
                    throw new IOException(LanguageManager.Get("GameDiscovery.CleanupChanged", "Setup files changed. Review the cleanup again."));
            }
            var changed = new List<string>();
            try
            {
                foreach (var item in _changes)
                {
                    changed.Add(item.Key);
                    if (item.Value == null) File.Delete(item.Key);
                    else WriteMetadata(item.Key, item.Value);
                }
                saveSettings();
            }
            catch (Exception failure)
            {
                var failures = new List<Exception> { failure };
                foreach (string path in changed.AsEnumerable().Reverse())
                    try { if (_observed[path] == null) File.Delete(path); else WriteMetadata(path, _observed[path]); }
                    catch (Exception rollback) { failures.Add(rollback); }
                if (failures.Count > 1) throw new AggregateException(failures);
                throw;
            }
            foreach (string directory in Directories.OrderByDescending(path => path.Length))
                try
                {
                    RequirePlainPath(directory);
                    if (Directory.Exists(directory)) Directory.Delete(directory, false);
                }
                catch (IOException) { Retained.Add(directory); }
                catch (UnauthorizedAccessException) { Retained.Add(directory); }
                catch (System.Security.SecurityException) { Retained.Add(directory); }
        }

        /// <summary>Atomically replaces metadata using a flushed sibling, including during rollback.</summary>
        private static void WriteMetadata(string path, byte[] bytes)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
