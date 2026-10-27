using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Nexus.Client.Games.DataDriven
{
    /// <summary>Resolves LOOT's installation-specific folder identifier without changing its persisted configuration.</summary>
    internal static class LootGameSelection
    {
        private static readonly Regex GamesHeader = new Regex(@"^\[\[\s*games\s*\]\]\s*(?:#.*)?$", RegexOptions.CultureInvariant);
        private static readonly Regex StringSetting = new Regex("^(folder|gameId|path)\\s*=\\s*('([^'\\r\\n]*)'|\"(?:[^\"\\\\\\r\\n]|\\\\.)*\")\\s*(?:#.*)?$", RegexOptions.CultureInvariant);
        private static readonly Regex RelevantSetting = new Regex(@"^(folder|gameId|path)\s*=", RegexOptions.CultureInvariant);

        /// <summary>Uses a matching configured installation, retaining the shipped identifier when matching is unavailable or ambiguous.</summary>
        internal static string ResolveGameIdentifier(string defaultIdentifier, string gamePath, string settingsPath)
        {
            string normalizedPath = NormalizePath(gamePath);
            if (normalizedPath == null || string.IsNullOrWhiteSpace(defaultIdentifier))
                return defaultIdentifier;

            try
            {
                List<LootGame> games = ReadGames(File.ReadLines(settingsPath));
                List<string> matches = games.Where(game => !game.Invalid && !string.IsNullOrWhiteSpace(game.Folder) &&
                        (string.IsNullOrEmpty(game.GameId) || string.Equals(game.GameId, defaultIdentifier, StringComparison.OrdinalIgnoreCase)) &&
                        string.Equals(NormalizePath(game.Path), normalizedPath, StringComparison.OrdinalIgnoreCase))
                    .Select(game => game.Folder).Distinct(StringComparer.Ordinal).ToList();
                if (matches.Contains(defaultIdentifier))
                    return defaultIdentifier;
                if (matches.Count == 1)
                    return matches[0];
                if (matches.Count > 1)
                    Trace.TraceWarning("LOOT has multiple entries for the current game path; retaining game identifier {0}.", defaultIdentifier);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is System.Security.SecurityException)
            {
                Trace.TraceInformation("Could not read LOOT installation settings; retaining game identifier {0}: {1}", defaultIdentifier, ex.Message);
            }
            return defaultIdentifier;
        }

        /// <summary>Reads the single-line string fields emitted by LOOT, ignoring unsupported or duplicate fields safely.</summary>
        private static List<LootGame> ReadGames(IEnumerable<string> lines)
        {
            var games = new List<LootGame>();
            LootGame current = null;
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    current = GamesHeader.IsMatch(line) ? new LootGame() : null;
                    if (current != null) games.Add(current);
                    continue;
                }
                if (current == null || !RelevantSetting.IsMatch(line))
                    continue;

                Match match = StringSetting.Match(line);
                if (!match.Success || !current.Keys.Add(match.Groups[1].Value))
                {
                    current.Invalid = true;
                    continue;
                }
                string encoded = match.Groups[2].Value;
                string value;
                try
                {
                    value = encoded[0] == '\'' ? encoded.Substring(1, encoded.Length - 2) : JsonConvert.DeserializeObject<string>(encoded);
                }
                catch (JsonException)
                {
                    current.Invalid = true;
                    continue;
                }
                switch (match.Groups[1].Value)
                {
                    case "folder": current.Folder = value; break;
                    case "gameId": current.GameId = value; break;
                    case "path": current.Path = value; break;
                }
            }
            return games;
        }

        /// <summary>Compares absolute installation paths without depending on drive availability or trailing separator spelling.</summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            try
            {
                if (!Path.IsPathRooted(path))
                    return null;
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                return null;
            }
        }

        /// <summary>Contains only the LOOT installation fields needed for selecting an existing entry.</summary>
        private sealed class LootGame
        {
            internal string Folder;
            internal string GameId;
            internal string Path;
            internal bool Invalid;
            internal readonly HashSet<string> Keys = new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
