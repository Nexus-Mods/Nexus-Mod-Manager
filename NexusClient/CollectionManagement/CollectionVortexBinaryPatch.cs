using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>One characterized Vortex binary-patch declaration for an exact member output path.</summary>
	public sealed class CollectionVortexBinaryPatch : IEquatable<CollectionVortexBinaryPatch>
	{
		public CollectionVortexBinaryPatch(string destinationPath, string sourceCrc32)
		{
			DestinationPath = new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, destinationPath).Path;
			if (String.IsNullOrWhiteSpace(sourceCrc32) || sourceCrc32.Length != 8 ||
				sourceCrc32.Any(c => !IsHex(c)))
				throw new ArgumentException("A Vortex binary patch CRC32 must be exactly eight hexadecimal characters.", nameof(sourceCrc32));
			SourceCrc32 = sourceCrc32.ToUpperInvariant();
		}

		public string DestinationPath { get; }
		public string SourceCrc32 { get; }

		public bool Equals(CollectionVortexBinaryPatch other)
		{
			return other != null && StringComparer.OrdinalIgnoreCase.Equals(DestinationPath, other.DestinationPath) &&
				StringComparer.Ordinal.Equals(SourceCrc32, other.SourceCrc32);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionVortexBinaryPatch); }
		public override int GetHashCode() { return StringComparer.OrdinalIgnoreCase.GetHashCode(DestinationPath) ^ StringComparer.Ordinal.GetHashCode(SourceCrc32); }

		private static bool IsHex(char value)
		{
			return (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F');
		}
	}

	/// <summary>Immutable, collision-free Vortex binary-patch set.</summary>
	public sealed class CollectionVortexBinaryPatchList : IEquatable<CollectionVortexBinaryPatchList>
	{
		private readonly ReadOnlyCollection<CollectionVortexBinaryPatch> _items;

		public CollectionVortexBinaryPatchList(IEnumerable<CollectionVortexBinaryPatch> items)
		{
			List<CollectionVortexBinaryPatch> values = (items ?? throw new ArgumentNullException(nameof(items))).ToList();
			if (values.Count == 0 || values.Any(x => x == null))
				throw new ArgumentException("A Vortex binary-patch set must contain at least one non-null entry.", nameof(items));
			if (values.Select(x => x.DestinationPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
				throw new ArgumentException("A Vortex binary-patch set contains duplicate canonical destination paths.", nameof(items));
			_items = new ReadOnlyCollection<CollectionVortexBinaryPatch>(values.OrderBy(x => x.DestinationPath, StringComparer.OrdinalIgnoreCase).ToList());
		}

		public ReadOnlyCollection<CollectionVortexBinaryPatch> Items { get { return _items; } }

		public bool Equals(CollectionVortexBinaryPatchList other)
		{
			return other != null && _items.SequenceEqual(other._items);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionVortexBinaryPatchList); }
		public override int GetHashCode() { unchecked { int hash = 17; foreach (CollectionVortexBinaryPatch item in _items) hash = hash * 31 + item.GetHashCode(); return hash; } }
	}
}
