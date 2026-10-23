using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Nexus.Client.ModManagement
{
	/// <summary>Prunes empty ancestors of removed deployment files after uninstall has committed.</summary>
	internal static class UninstalledModDirectoryCleanup
	{
		/// <summary>Preserves the deployment root, unrelated directories, nonempty folders and all reparse points.</summary>
		internal static void RemoveEmptyDirectories(string rootPath, IEnumerable<string> removedFiles)
		{
			if (String.IsNullOrWhiteSpace(rootPath) || removedFiles == null) return;
			try
			{
				if (!Path.IsPathRooted(rootPath)) return;
				string root = Path.GetFullPath(rootPath);
				if (root.Length > Path.GetPathRoot(root).Length)
					root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
					? root : root + Path.DirectorySeparatorChar;
				var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (string file in removedFiles)
				{
					if (String.IsNullOrWhiteSpace(file) || !Path.IsPathRooted(file)) continue;
					string fullPath = Path.GetFullPath(file);
					if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
					string directory = Path.GetDirectoryName(fullPath);
					while (directory != null && !directory.Equals(root, StringComparison.OrdinalIgnoreCase) &&
						directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					{
						directories.Add(directory);
						directory = Path.GetDirectoryName(directory);
					}
				}
				foreach (string directory in directories.OrderByDescending(path => path.Length))
				{
					try
					{
						if (IsUnlinkedDirectory(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
							Directory.Delete(directory, false);
					}
					catch (DirectoryNotFoundException) { }
					catch (FileNotFoundException) { }
					catch (Exception error) when (IsCleanupError(error))
					{
						// Cosmetic cleanup must not turn a committed uninstall into a reported failure.
						Trace.TraceWarning("Could not remove empty mod directory '{0}': {1}", directory, error.Message);
					}
				}
			}
			catch (Exception error) when (IsCleanupError(error))
			{
				Trace.TraceWarning("Could not prepare empty mod directory cleanup: {0}", error.Message);
			}
		}

		/// <summary>Checks the complete ancestor chain so a junction cannot redirect cleanup outside the deployment root.</summary>
		private static bool IsUnlinkedDirectory(string directory)
		{
			for (string cursor = directory; cursor != null; cursor = Path.GetDirectoryName(cursor))
			{
				FileAttributes attributes = File.GetAttributes(cursor);
				if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
					return false;
			}
			return true;
		}

		/// <summary>Identifies expected filesystem and legacy-path failures that must leave folders in place.</summary>
		private static bool IsCleanupError(Exception error)
		{
			return error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException ||
				error is ArgumentException || error is NotSupportedException;
		}
	}
}
