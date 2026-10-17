using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.ModManagement.Witcher3
{
    /// <summary>
    /// Non-mutating final-destination preflight for a future first-time native install.
    /// Until a read-locked W3R.3 native snapshot can authorize replacement, an existing
    /// mod/DLC component is treated as unmanaged/uncertain, even if NMM might own it.
    /// A successful preflight is NOT an atomic lock against another process modifying
    /// the destination between review and deployment. Keep Remastered activation gated.
    /// </summary>
    public static class Witcher3NativeDestinationPreflight
    {
        public static void RequireNewComponents(string gameRoot, IEnumerable<string> gameRelativeTargets)
        {
            if (String.IsNullOrWhiteSpace(gameRoot) || !Path.IsPathRooted(gameRoot))
                throw new InvalidDataException("A known absolute Witcher 3 game root is required.");
            if (gameRelativeTargets == null) throw new ArgumentNullException(nameof(gameRelativeTargets));

            string fullRoot = Path.GetFullPath(gameRoot);
            string volumeRoot = Path.GetPathRoot(fullRoot);
            string root = fullRoot.Length > volumeRoot.Length
                ? fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : fullRoot;
            if (root.Length == 0 || !Directory.Exists(root))
                throw new DirectoryNotFoundException("The Witcher 3 installation directory is not accessible.");
            var seenComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string requestedPath in gameRelativeTargets)
            {
                string validated = Witcher3BasicInstallMapping.ValidatePlannedPath(requestedPath);
                string[] parts = validated.Split('\\');
                if (parts.Length < 4)
                    throw new InvalidDataException("A Witcher 3 target lacks its component payload.");

                string component = parts[0] + "\\" + parts[1];
                // Reject reparse ancestors even if the final destination does not exist:
                // otherwise Mods\\junction\\content could redirect native writes outside the game.
                string cursor = root;
                for (int i = 0; i < parts.Length; i++)
                {
                    cursor = Path.Combine(cursor, parts[i]);
                    FileAttributes attributes;
                    if (!TryReadAttributes(cursor, out attributes))
                        continue;
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Witcher 3 native destination traverses a reparse point: " + validated);
                    if (i < parts.Length - 1 && (attributes & FileAttributes.Directory) == 0)
                        throw new InvalidDataException("Witcher 3 native destination traverses an existing file: " + validated);
                    if (i == parts.Length - 1)
                        throw new InvalidDataException("Witcher 3 native destination is already occupied: " + validated);
                    if (i == 1 && seenComponents.Add(component))
                        throw new InvalidDataException("Witcher 3 component already exists and needs native ownership review: " + component);
                }
            }
        }

        private static bool TryReadAttributes(string path, out FileAttributes attributes)
        {
            try
            {
                attributes = File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException)
            {
                attributes = default(FileAttributes);
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                attributes = default(FileAttributes);
                return false;
            }
            // Access-denied, I/O errors and unexpected filesystem state fail closed.
        }
    }
}
