using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.ModManagement.Witcher3
{
    /// <summary>
    /// Final read-only physical-path check for an EXACT same-target native replacement.
    /// This is NOT ownership authorization, filesystem snapshot isolation, or a substitute
    /// for native transactional rollback. Keep the Remastered activation gate until Windows
    /// tests verify the Direct/Virtual restore paths under failure injection.
    /// </summary>
    public static class Witcher3NativeUpgradeDestinationPreflight
    {
        public static void RequireExistingOwnedFiles(string gameRoot, IEnumerable<string> targets)
        {
            if (String.IsNullOrWhiteSpace(gameRoot) || !Path.IsPathRooted(gameRoot))
                throw new InvalidDataException("A known absolute Witcher 3 root is required for an upgrade.");
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            string root = Path.GetFullPath(gameRoot);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("The Witcher 3 installation root is missing.");
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The Remastered game root itself is a filesystem reparse point.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string target in targets)
            {
                string canonical = Witcher3BasicInstallMapping.ValidatePlannedPath(target);
                if (!String.Equals(canonical, target, StringComparison.Ordinal) || !seen.Add(canonical))
                    throw new InvalidDataException("An upgrade destination is duplicated or noncanonical: " + target);
                string[] segments = canonical.Split('\\');
                if (segments.Length < 4)
                    throw new InvalidDataException("A Witcher 3 upgrade target must be a component payload file.");
                string cursor = root;
                for (int i = 0; i < segments.Length; ++i)
                {
                    cursor = Path.Combine(cursor, segments[i]);
                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(cursor);
                    }
                    catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                    {
                        throw new InvalidDataException("A previously owned upgrade destination is missing: " + canonical, ex);
                    }
                    // Access-denied and unexpected I/O failure deliberately propagate; never
                    // interpret them as an absent, safe-to-create destination.
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Upgrade target traverses a reparse point: " + canonical);
                    bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (isDirectory != (i < segments.Length - 1))
                        throw new InvalidDataException("An upgrade target changed its file/directory type: " + canonical);
                }
            }
            if (seen.Count == 0)
                throw new InvalidDataException("An empty upgrade destination set cannot be authorized.");
        }
    }
}
