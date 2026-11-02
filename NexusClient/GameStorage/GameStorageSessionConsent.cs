using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Nexus.Client.Settings;

namespace Nexus.Client.GameStorage
{
    /// <summary>Retains explicit Continue choices for reviewed paths in one settings instance and process session.</summary>
    internal static class GameStorageSessionConsent
    {
        private static readonly ConditionalWeakTable<ISettings, Dictionary<string, HashSet<string>>> Approvals =
            new ConditionalWeakTable<ISettings, Dictionary<string, HashSet<string>>>();

        /// <summary>Captures both the full storage selection and its native destination before a warning is displayed.</summary>
        internal static Action CreateStorageApproval(IEnvironmentInfo environment, GameStoragePathSet paths)
        {
            ISettings settings = environment.Settings;
            string modeId = paths.GameId;
            string storageKey = Key(environment, paths, false);
            string nativeKey = Key(environment, paths, true);
            return () => Add(settings, modeId, storageKey, nativeKey);
        }

        /// <summary>Captures a native ownership warning without accepting changes to unrelated archive or link-folder choices.</summary>
        internal static Action CreateNativeApproval(IEnvironmentInfo environment, string modeId, string root, string info, string virtualInstall)
        {
            var paths = new GameStoragePathSet { GameId = modeId, GameInstallPath = root, InstallInfoPath = info, VirtualInstallPath = virtualInstall };
            ISettings settings = environment.Settings;
            string key = Key(environment, paths, true);
            return () => Add(settings, modeId, key);
        }

        /// <summary>Checks the full reviewed selection, including archive, staging and link paths.</summary>
        internal static bool HasStorageApproval(IEnvironmentInfo environment, GameStoragePathSet paths)
        {
            return environment != null && paths != null && Contains(environment, paths.GameId, Key(environment, paths, false));
        }

        /// <summary>Allows subsequent per-game native ownership checks to recognize the same Continue choice.</summary>
        internal static bool HasNativeApproval(IEnvironmentInfo environment, string modeId, string root, string info, string virtualInstall)
        {
            if (environment == null) return false;
            return Contains(environment, modeId, Key(environment, new GameStoragePathSet { GameId = modeId,
                GameInstallPath = root, InstallInfoPath = info, VirtualInstallPath = virtualInstall }, true));
        }

        /// <summary>Clears choices when the user resets or forgets a game, without changing persisted storage records.</summary>
        internal static void Revoke(ISettings settings, string modeId)
        {
            Dictionary<string, HashSet<string>> approvals;
            if (Approvals.TryGetValue(settings, out approvals)) lock (approvals) approvals.Remove(modeId);
        }

        /// <summary>Enumerates saved game paths without constructing any game modes or reading side-effecting descriptors.</summary>
        internal static IEnumerable<GameStoragePathSet> OtherConfigurations(IEnvironmentInfo environment, string modeId)
        {
            var settings = environment.Settings;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dictionary in new[] { settings.InstallationPaths, settings.InstallInfoFolder, settings.ModFolder, settings.VirtualFolder, settings.HDLinkFolder })
                if (dictionary != null) ids.UnionWith(dictionary.Keys);
            foreach (string id in ids.Where(id => !String.IsNullOrWhiteSpace(id) && !String.Equals(id, modeId, StringComparison.OrdinalIgnoreCase)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
                yield return new GameStoragePathSet { GameId = id, GameInstallPath = Setting(settings.InstallationPaths, id),
                    InstallInfoPath = Setting(settings.InstallInfoFolder, id), ModsPath = Setting(settings.ModFolder, id),
                    VirtualInstallPath = ConfiguredVirtualPath(environment, id), LinkFolderPath = Setting(settings.HDLinkFolder, id) };
        }

        /// <summary>Formats a stale Virtual setting even when its path cannot be normalized.</summary>
        internal static string ConfiguredVirtualPath(IEnvironmentInfo environment, string modeId)
        {
            string value = Setting(environment.Settings.VirtualFolder, modeId);
            try { return new GameStorageService(environment).NormalizeVirtualInstallDirectory(value); }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is IOException || error is System.Security.SecurityException)
            { return value; }
        }

        /// <summary>Compares normalized directories without requiring old installations to remain present.</summary>
        internal static bool Overlaps(string first, string second)
        {
            if (String.IsNullOrWhiteSpace(first) || String.IsNullOrWhiteSpace(second)) return false;
            string left = PathKey(first) + Path.DirectorySeparatorChar;
            string right = PathKey(second) + Path.DirectorySeparatorChar;
            return left.StartsWith(right, StringComparison.Ordinal) || right.StartsWith(left, StringComparison.Ordinal);
        }

        /// <summary>Reads an optional saved path without assuming legacy dictionaries are populated.</summary>
        private static string Setting(IDictionary<string, string> values, string id)
        {
            string value;
            return values != null && values.TryGetValue(id, out value) ? value : null;
        }

        /// <summary>Scopes approval to selected paths and relevant other configurations; unrelated game changes do not revoke it.</summary>
        private static string Key(IEnvironmentInfo environment, GameStoragePathSet paths, bool native)
        {
            string[] selected = native ? new[] { paths.GameInstallPath, paths.InstallInfoPath, paths.VirtualInstallPath }
                : new[] { paths.GameInstallPath, paths.InstallInfoPath, paths.VirtualInstallPath, paths.ModsPath, paths.LinkFolderRequired ? paths.LinkFolderPath : null };
            var related = new List<string[]>();
            foreach (GameStoragePathSet other in OtherConfigurations(environment, paths.GameId))
            {
                string[] others = native ? new[] { other.GameInstallPath, other.InstallInfoPath, other.VirtualInstallPath }
                    : new[] { other.GameInstallPath, other.InstallInfoPath, other.VirtualInstallPath, other.ModsPath, other.LinkFolderPath };
                bool witcherPair = ModManagement.Witcher3.Witcher3RootOwnershipGuard.CanShareNativeStorage(paths.GameId, other.GameId);
                if (witcherPair || selected.Any(path => others.Any(candidate => Overlaps(path, candidate))))
                    related.Add(new[] { other.GameId.ToUpperInvariant() }.Concat(others.Select(PathKey)).ToArray());
            }
            return (native ? "N:" : "S:") + JsonConvert.SerializeObject(new { Mode = paths.GameId.ToUpperInvariant(),
                Paths = selected.Select(PathKey).ToArray(), LinkRequired = !native && paths.LinkFolderRequired, Related = related });
        }

        /// <summary>Normalizes spelling while retaining malformed settings for a user-controllable warning.</summary>
        private static string PathKey(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return String.Empty;
            try { return Path.GetFullPath(path).TrimEnd('\\', '/').ToUpperInvariant(); }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is IOException || error is System.Security.SecurityException)
            { return path.Trim().ToUpperInvariant(); }
        }

        /// <summary>Registers captured keys only after the user chooses Continue.</summary>
        private static void Add(ISettings settings, string modeId, params string[] keys)
        {
            var approvals = Approvals.GetValue(settings, ignored => new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
            lock (approvals)
            {
                HashSet<string> values;
                if (!approvals.TryGetValue(modeId, out values)) approvals[modeId] = values = new HashSet<string>(StringComparer.Ordinal);
                values.UnionWith(keys);
            }
        }

        /// <summary>Checks a captured key under the same lock used to publish and revoke choices.</summary>
        private static bool Contains(IEnvironmentInfo environment, string modeId, string key)
        {
            Dictionary<string, HashSet<string>> approvals;
            if (!Approvals.TryGetValue(environment.Settings, out approvals)) return false;
            lock (approvals)
            {
                HashSet<string> values;
                return approvals.TryGetValue(modeId, out values) && values.Contains(key);
            }
        }
    }
}
