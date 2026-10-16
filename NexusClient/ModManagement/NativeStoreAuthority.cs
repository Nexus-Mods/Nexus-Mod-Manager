using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Nexus.Client.ModManagement
{
	/// <summary>Records a one-time SQLite authority transition independently of the database it protects.</summary>
	internal static class NativeStoreAuthority
	{
		/// <summary>Requires an authoritative database to remain present rather than importing a stale legacy snapshot.</summary>
		internal static void RequireAvailable(string databasePath)
		{
			if (File.Exists(databasePath + ".authority") && !File.Exists(databasePath))
				throw new InvalidDataException("The authoritative native database is missing. Restore a current backup before continuing: " + databasePath);
		}

		/// <summary>Publishes the authority marker before the first database-only mutation.</summary>
		internal static void Mark(string databasePath)
		{
			RequireAvailable(databasePath);
			if (!File.Exists(databasePath))
				throw new FileNotFoundException("The native database has not been published.", databasePath);
			string marker = databasePath + ".authority";
			if (File.Exists(marker))
				return;
			using (var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
			{
				byte[] bytes = Encoding.UTF8.GetBytes("nmm-ce.native-sqlite-authority/1\n" + Path.GetFileName(databasePath));
				stream.Write(bytes, 0, bytes.Length);
				stream.Flush(true);
			}
		}
	}

	/// <summary>Identifies mutable store records by reference rather than by editable metadata.</summary>
	internal sealed class NativeReferenceComparer<T> : IEqualityComparer<T> where T : class
	{
		internal static readonly NativeReferenceComparer<T> Instance = new NativeReferenceComparer<T>();
		/// <summary>Compares record instances without invoking metadata equality.</summary>
		public bool Equals(T left, T right)
		{
			return ReferenceEquals(left, right);
		}

		/// <summary>Returns the stable identity hash of a record instance.</summary>
		public int GetHashCode(T value)
		{
			return RuntimeHelpers.GetHashCode(value);
		}
	}
}
