using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Marks read-only inventory progress that reused an unchanged package observation.</summary>
	internal interface ILoadOrderInspectionReuse
	{
		bool LastResultWasCached { get; }
	}

	/// <summary>Session-only display cache. Publication and repair preflight continue to use uncached package inspection.</summary>
	internal sealed class LoadOrderPackageInspectionCache
	{
		private readonly object _sync = new object();
		private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
		private string _readerIdentity;

		/// <summary>Forgets display hints when a user explicitly requests a complete reinspection.</summary>
		internal void Clear() { lock (_sync) _entries.Clear(); }

		/// <summary>Decorates only a reader whose executable/dependency metadata can be identified; incomplete helper state bypasses caching.</summary>
		internal ILoadOrderPackageInspector Wrap(ILoadOrderPackageInspector reader, string executablePath)
		{
			string identity = ReaderIdentity(executablePath);
			if (identity == null || !reader.IsAvailable) return reader;
			lock (_sync)
			{
				if (_readerIdentity != identity) { _entries.Clear(); _readerIdentity = identity; }
			}
			return new CachedInspector(this, reader, identity);
		}

		/// <summary>Uses bounded, nonrecursive helper metadata as an invalidation hint, never as publication authority.</summary>
		private static string ReaderIdentity(string executablePath)
		{
			try
			{
				if (!File.Exists(executablePath)) return null;
				string[] files = Directory.EnumerateFiles(Path.GetDirectoryName(executablePath)).Where(path =>
					new[] { ".exe", ".dll", ".json" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).Take(2049).ToArray();
				if (files.Length > 2048) return null;
				var stamp = new StringBuilder(executablePath);
				foreach (string path in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
				{
					var file = new FileInfo(path);
					stamp.Append('|').Append(file.Name).Append(':').Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks);
				}
				return stamp.ToString();
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is ArgumentException) { return null; }
		}

		/// <summary>Retains all split-package inputs so changing any part invalidates every dependent cached result.</summary>
		private sealed class Entry
		{
			internal LoadOrderPackageInspection Inspection;
			internal LoadOrderPackageFile[] Inputs;
		}

		/// <summary>Reuses immutable observations only after the current directory scan matches every recorded input.</summary>
		private sealed class CachedInspector : ILoadOrderPackageInspector, ILoadOrderInspectionReuse
		{
			private readonly LoadOrderPackageInspectionCache _cache;
			private readonly ILoadOrderPackageInspector _reader;
			private readonly string _identity;
			private Dictionary<string, LoadOrderPackageFile> _files;
			/// <summary>Creates one reader for one directory scan; no file contents are read by construction.</summary>
			internal CachedInspector(LoadOrderPackageInspectionCache cache, ILoadOrderPackageInspector reader, string identity)
			{ _cache = cache; _reader = reader; _identity = identity; }
			public bool IsAvailable => _reader.IsAvailable;
			public bool LastResultWasCached { get; private set; }

			/// <summary>Transient failures are retried; complete and partial observations are reusable until their inputs or helper change.</summary>
			public LoadOrderPackageInspection Inspect(LoadOrderPackageFile file, IReadOnlyList<LoadOrderPackageFile> availableFiles, CancellationToken cancellationToken)
			{
				cancellationToken.ThrowIfCancellationRequested(); LastResultWasCached = false;
				if (_files == null)
				{
					_files = availableFiles.ToDictionary(item => item.FileName, StringComparer.OrdinalIgnoreCase);
					var paths = new HashSet<string>(availableFiles.Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
					lock (_cache._sync) foreach (string obsolete in _cache._entries.Keys.Where(path => !paths.Contains(path)).ToArray()) _cache._entries.Remove(obsolete);
				}
				lock (_cache._sync)
				{
					if (_identity == _cache._readerIdentity && _cache._entries.TryGetValue(file.Path, out Entry cached) && cached.Inputs.All(Matches))
					{ LastResultWasCached = true; return cached.Inspection; }
				}
				LoadOrderPackageInspection inspection = _reader.Inspect(file, availableFiles, cancellationToken);
				if (inspection != null && (inspection.Status == LoadOrderInspectionStatus.Complete || inspection.Status == LoadOrderInspectionStatus.Partial) &&
					inspection.ReadFileNames.Contains(file.FileName, StringComparer.OrdinalIgnoreCase))
				{
					string[] names = inspection.ReadFileNames.Concat(new[] { file.FileName, inspection.PrimaryFileName }).Where(name => name != null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
					if (names.All(_files.ContainsKey))
					{
						var entry = new Entry { Inspection = inspection, Inputs = names.Select(name => _files[name]).ToArray() };
						lock (_cache._sync) if (_identity == _cache._readerIdentity) _cache._entries[file.Path] = entry;
					}
				}
				return inspection;
			}

			/// <summary>Compares the exact observed path, size and timestamp; directory policy and duplicates are still checked every scan.</summary>
			private bool Matches(LoadOrderPackageFile expected) => _files.TryGetValue(expected.FileName, out LoadOrderPackageFile current) &&
				String.Equals(expected.Path, current.Path, StringComparison.OrdinalIgnoreCase) && expected.Length == current.Length && expected.LastWriteTimeUtcTicks == current.LastWriteTimeUtcTicks;
		}
	}
}
