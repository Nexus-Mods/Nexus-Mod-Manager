using System;
using System.IO;
using System.Linq;

namespace Nexus.Client.Games
{
    /// <summary>
    /// Resolve a *candidate* game root from a found executable and a game-relative executable
    /// path. No filesystem mutation and no edition/ownership approval occurs here.
    /// </summary>
    public static class GameInstallationCandidatePath
    {
        public static bool TryResolveGameRoot(string foundExecutablePath, string gameRelativeExecutable,
            out string gameRoot)
        {
            gameRoot = null;
            if (string.IsNullOrWhiteSpace(foundExecutablePath) || string.IsNullOrWhiteSpace(gameRelativeExecutable))
                return false;

            string relative = gameRelativeExecutable.Replace('\\', '/');
            if (relative.StartsWith("/", StringComparison.Ordinal) ||
                Path.IsPathRooted(gameRelativeExecutable) || relative.IndexOf(':') >= 0)
                return false;

            string[] parts = relative.Split('/');
            if (parts.Length == 0 || parts.Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".."))
                return false;

            try
            {
                if (!Path.IsPathRooted(foundExecutablePath))
                    return false;
                string current = Path.GetFullPath(foundExecutablePath);
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    if (!string.Equals(Path.GetFileName(current), parts[i], StringComparison.OrdinalIgnoreCase))
                        return false;
                    current = Path.GetDirectoryName(current);
                    if (string.IsNullOrEmpty(current))
                        return false;
                }
                gameRoot = current;
                return true;
            }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException ||
                error is PathTooLongException)
            {
                return false;
            }
        }
    }
}
