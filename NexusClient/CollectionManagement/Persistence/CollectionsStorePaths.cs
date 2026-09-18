using System;
using System.IO;
using Nexus.Client.GameStorage;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Resolves Collections-owned storage from the active Game Storage path set.
	/// </summary>
	/// <remarks>
	/// Collections persistence follows the InstallInfo location selected by Game Storage. It is not a separately movable
	/// live-install root, and callers must not derive target authority from these paths.
	/// </remarks>
	public static class CollectionsStorePaths
	{
		public const string DirectoryName = "Collections";
		public const string DatabaseFileName = "collections.sqlite";
		public const string RetainedContentDirectoryName = "Content";

		public static string GetStoreDirectory(GameStoragePathSet paths)
		{
			if (paths == null)
				throw new ArgumentNullException(nameof(paths));

			return GetStoreDirectory(paths.InstallInfoPath);
		}

		public static string GetStoreDirectory(string installInfoDirectory)
		{
			return Path.Combine(RequireDirectory(installInfoDirectory), DirectoryName);
		}

		public static string GetDatabasePath(GameStoragePathSet paths)
		{
			return Path.Combine(GetStoreDirectory(paths), DatabaseFileName);
		}

		public static string GetDatabasePath(string installInfoDirectory)
		{
			return Path.Combine(GetStoreDirectory(installInfoDirectory), DatabaseFileName);
		}

		public static string GetRetainedContentDirectory(GameStoragePathSet paths)
		{
			return Path.Combine(GetStoreDirectory(paths), RetainedContentDirectoryName);
		}

		public static string GetRetainedContentDirectory(string installInfoDirectory)
		{
			return Path.Combine(GetStoreDirectory(installInfoDirectory), RetainedContentDirectoryName);
		}

		private static string RequireDirectory(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("An InstallInfo directory is required.", nameof(path));

			return Path.GetFullPath(path);
		}
	}
}
