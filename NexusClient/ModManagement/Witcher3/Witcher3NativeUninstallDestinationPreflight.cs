using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.ModManagement.Witcher3
{
    /// <summary>
    /// Checks the physical removal targets under the live native lease without changing files.
    /// Does not infer that current bytes are NMM's original deployed bytes. In particular,
    /// symbolic-link leaves are blocked pending a verified VMA link-target check on Windows.
    /// This is a preflight, not an external-process filesystem lock or rollback guarantee.
    /// </summary>
    public static class Witcher3NativeUninstallDestinationPreflight
    {
        public static void RequireExistingTargets(string gameRoot, IEnumerable<string> targets)
        {
            if (String.IsNullOrWhiteSpace(gameRoot) || !Path.IsPathRooted(gameRoot))
                throw new InvalidDataException("An absolute Witcher 3 game root is required for removal.");
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            string root = Path.GetFullPath(gameRoot);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("The Witcher 3 game root is no longer available.");
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The Witcher 3 game root is a reparse point.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string target in targets)
            {
                string canonical = Witcher3BasicInstallMapping.ValidatePlannedPath(target);
                if (!String.Equals(target, canonical, StringComparison.Ordinal) || !seen.Add(canonical))
                    throw new InvalidDataException("A removal target is duplicated or noncanonical: " + target);
                string[] segments = canonical.Split('\\');
                if (segments.Length < 4 || !String.Equals(segments[2], "content", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Only reviewed Mods/DLC content files can be removed.");
                string cursor = root;
                for (int i = 0; i < segments.Length; ++i)
                {
                    cursor = Path.Combine(cursor, segments[i]);
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(cursor); }
                    catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                    {
                        throw new InvalidDataException("A native-owned removal target is missing: " + canonical, ex);
                    }
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("A removal target traverses a reparse point (including a Virtual symbolic link): " + canonical);
                    if (((attributes & FileAttributes.Directory) != 0) != (i < segments.Length - 1))
                        throw new InvalidDataException("The removal target changed its file/directory type: " + canonical);
                }
            }
            if (seen.Count == 0)
                throw new InvalidDataException("An empty native removal target set cannot be authorized.");
        }
    }
}
