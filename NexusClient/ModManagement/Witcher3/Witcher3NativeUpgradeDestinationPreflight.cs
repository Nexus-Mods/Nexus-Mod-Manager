using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.ModManagement.Witcher3
{
    /// <summary>
    /// Final read-only physical-path check for an exclusive native owner upgrade.
    /// This is NOT ownership authorization, filesystem snapshot isolation, or a substitute
    /// for native transactional rollback. Remastered runtime validation remains outstanding.
    /// </summary>
    public static class Witcher3NativeUpgradeDestinationPreflight
    {
        /// <summary>
        /// Validate the complete old file set as existing, then ensure each new destination
        /// is either that owned file or physically absent. New directories are allowed only
        /// when their component folder is also new. Links and file/directory collisions fail
        /// before the native upgrader can mutate InstallLog or any target.
        /// </summary>
        public static void RequireUpgradeTargets(string gameRoot, IEnumerable<string> previousTargets,
            IEnumerable<string> replacementTargets)
        {
            if (previousTargets == null) throw new ArgumentNullException(nameof(previousTargets));
            if (replacementTargets == null) throw new ArgumentNullException(nameof(replacementTargets));
            var old = new HashSet<string>(previousTargets, StringComparer.OrdinalIgnoreCase);
            RequireExistingOwnedFiles(gameRoot, old);
            string root = Path.GetFullPath(gameRoot);
            var oldFolders = new HashSet<string>(old.Select(path =>
                String.Join("\\", path.Split('\\').Take(2))), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string destination in replacementTargets)
            {
                string canonical = Witcher3BasicInstallMapping.ValidatePlannedPath(destination);
                if (!String.Equals(canonical, destination, StringComparison.Ordinal) || !seen.Add(canonical))
                    throw new InvalidDataException("An upgrade target is duplicated or noncanonical: " + destination);
                string[] parts = canonical.Split('\\');
                if (parts.Length < 4)
                    throw new InvalidDataException("An upgrade target must be a component payload file: " + destination);
                string folder = parts[0] + "\\" + parts[1];
                if (!oldFolders.Contains(folder) &&
                    (Directory.Exists(Path.Combine(root, parts[0], parts[1])) ||
                     File.Exists(Path.Combine(root, parts[0], parts[1]))))
                    throw new InvalidDataException("A new component folder is already occupied: " + folder);
                string cursor = root;
                for (int i = 0; i < parts.Length; i++)
                {
                    cursor = Path.Combine(cursor, parts[i]);
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(cursor); }
                    catch (FileNotFoundException) { continue; }
                    catch (DirectoryNotFoundException) { continue; }
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("An upgrade destination traverses a link: " + canonical);
                    bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (isDirectory != (i < parts.Length - 1))
                        throw new InvalidDataException("An upgrade destination has the wrong file/directory type: " + canonical);
                    if (i == parts.Length - 1 && !old.Contains(canonical))
                        throw new InvalidDataException("An added upgrade destination already exists on disk: " + canonical);
                }
            }
            if (seen.Count == 0)
                throw new InvalidDataException("An upgrade must produce at least one component payload file.");
        }

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
