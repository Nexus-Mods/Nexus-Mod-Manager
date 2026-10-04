using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Immutable exact Vortex per-member deployment blacklist captured from collection.json fileOverrides.</summary>
	/// <remarks>
	/// Vortex stores deployment paths for the member files it must not contribute. Paths are retained as lexical Windows-style
	/// values here because curator absolute roots are machine-specific; native preparation resolves each path against the exact
	/// translated member outputs and requires one unambiguous match before suppressing anything.
	/// </remarks>
	public sealed class CollectionVortexFileOverrideList : IEquatable<CollectionVortexFileOverrideList>
	{
		private readonly ReadOnlyCollection<string> _paths;

		public CollectionVortexFileOverrideList(IEnumerable<string> paths)
		{
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			var normalized = new List<string>();
			var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string path in paths)
			{
				string value = Normalize(path);
				if (!unique.Add(value))
					throw new ArgumentException("Vortex fileOverrides cannot contain duplicate paths.", nameof(paths));
				normalized.Add(value);
			}
			if (normalized.Count == 0)
				throw new ArgumentException("A characterized Vortex fileOverrides list requires at least one path.", nameof(paths));
			normalized.Sort(StringComparer.OrdinalIgnoreCase);
			_paths = new ReadOnlyCollection<string>(normalized);
		}

		public IReadOnlyList<string> Paths { get { return _paths; } }

		public bool Equals(CollectionVortexFileOverrideList other)
		{
			return !ReferenceEquals(other, null) && _paths.SequenceEqual(other._paths, StringComparer.OrdinalIgnoreCase);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionVortexFileOverrideList); }
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				foreach (string path in _paths) hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(path);
				return hash;
			}
		}

		private static string Normalize(string path)
		{
			if (String.IsNullOrWhiteSpace(path) || !StringComparer.Ordinal.Equals(path, path.Trim()))
				throw new ArgumentException("A Vortex file override path must be non-empty and cannot contain surrounding whitespace.", nameof(path));
			if (path.IndexOf('\0') >= 0)
				throw new ArgumentException("A Vortex file override path cannot contain NUL characters.", nameof(path));

			string normalized = path.Replace('/', '\\');
			if (normalized.EndsWith("\\", StringComparison.Ordinal))
				throw new ArgumentException("A Vortex file override must identify a file, not a directory.", nameof(path));
			foreach (string segment in normalized.Split('\\'))
			{
				if (segment == "." || segment == "..")
					throw new ArgumentException("A Vortex file override path cannot contain relative traversal segments.", nameof(path));
			}
			return normalized;
		}
	}
}
