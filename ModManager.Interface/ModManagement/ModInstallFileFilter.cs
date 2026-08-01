namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.IO;

	/// <summary>
	/// Identifies archive files that must not be installed or deployed by NMM.
	/// </summary>
	public static class ModInstallFileFilter
	{
		private static readonly HashSet<string> IgnoredFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"meta.ini"
		};

		/// <summary>
		/// Determines whether the specified archive or install-relative path is globally ignored.
		/// </summary>
		/// <param name="p_strPath">The archive or install-relative path to inspect.</param>
		/// <returns><c>true</c> if the file must not be installed or deployed; otherwise, <c>false</c>.</returns>
		public static bool IsIgnored(string p_strPath)
		{
			if (String.IsNullOrWhiteSpace(p_strPath))
				return false;

			string strNormalizedPath = p_strPath.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			return IgnoredFileNames.Contains(Path.GetFileName(strNormalizedPath));
		}
	}
}
