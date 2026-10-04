using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Identifies the homogeneous content digest algorithm selected by Vortex for one hashes/fileList install.</summary>
	public enum CollectionVortexFileListHashAlgorithm
	{
		Md5 = 0,
		Xxh64 = 1
	}

	/// <summary>One exact Vortex list-installer output captured from collection.json hashes/fileList data.</summary>
	public sealed class CollectionVortexFileListItem : IEquatable<CollectionVortexFileListItem>
	{
		public CollectionVortexFileListItem(string destinationPath, string contentMd5)
			: this(destinationPath, CollectionVortexFileListHashAlgorithm.Md5, contentMd5)
		{
		}

		public CollectionVortexFileListItem(string destinationPath, CollectionVortexFileListHashAlgorithm hashAlgorithm, string contentHash)
		{
			DestinationPath = new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, destinationPath).Path;
			HashAlgorithm = hashAlgorithm;
			switch (hashAlgorithm)
			{
				case CollectionVortexFileListHashAlgorithm.Md5:
					string normalizedMd5;
					if (!CollectionExternalArtifactIdentity.TryNormalizeMd5(contentHash, out normalizedMd5))
						throw new ArgumentException("A Vortex MD5 file-list item requires a 32-character hexadecimal digest.", nameof(contentHash));
					ContentHash = normalizedMd5;
					break;
				case CollectionVortexFileListHashAlgorithm.Xxh64:
					ContentHash = NormalizeCanonicalXxh64(contentHash, nameof(contentHash));
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(hashAlgorithm));
			}
		}

		public string DestinationPath { get; }
		public CollectionVortexFileListHashAlgorithm HashAlgorithm { get; }
		public string ContentHash { get; }
		public string ContentMd5 { get { return HashAlgorithm == CollectionVortexFileListHashAlgorithm.Md5 ? ContentHash : null; } }
		public string ContentXxh64 { get { return HashAlgorithm == CollectionVortexFileListHashAlgorithm.Xxh64 ? ContentHash : null; } }

		public bool Equals(CollectionVortexFileListItem other)
		{
			return !ReferenceEquals(other, null) &&
				StringComparer.OrdinalIgnoreCase.Equals(DestinationPath, other.DestinationPath) &&
				HashAlgorithm == other.HashAlgorithm &&
				StringComparer.Ordinal.Equals(ContentHash, other.ContentHash);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionVortexFileListItem); }
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(DestinationPath);
				hash = (hash * 397) ^ (int)HashAlgorithm;
				hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ContentHash);
				return hash;
			}
		}

		private static string NormalizeCanonicalXxh64(string value, string parameterName)
		{
			if (String.IsNullOrWhiteSpace(value) || !StringComparer.Ordinal.Equals(value, value.Trim()))
				throw new ArgumentException("A Vortex XXH64 file-list item requires one canonical base64-encoded 64-bit digest.", parameterName);
			byte[] decoded;
			try
			{
				decoded = Convert.FromBase64String(value);
			}
			catch (FormatException ex)
			{
				throw new ArgumentException("A Vortex XXH64 file-list item requires one canonical base64-encoded 64-bit digest.", parameterName, ex);
			}
			if (decoded.Length != 8 || !StringComparer.Ordinal.Equals(Convert.ToBase64String(decoded), value))
				throw new ArgumentException("A Vortex XXH64 file-list item requires one canonical base64-encoded 64-bit digest.", parameterName);
			return value;
		}
	}

	/// <summary>Immutable characterized Vortex hashes/fileList list-installer semantics.</summary>
	public sealed class CollectionVortexFileList : IEquatable<CollectionVortexFileList>
	{
		private readonly ReadOnlyCollection<CollectionVortexFileListItem> _items;

		public CollectionVortexFileList(IEnumerable<CollectionVortexFileListItem> items)
		{
			if (items == null) throw new ArgumentNullException(nameof(items));
			List<CollectionVortexFileListItem> copied = items.ToList();
			if (copied.Count == 0 || copied.Any(x => x == null))
				throw new ArgumentException("A characterized Vortex file list requires at least one non-null output.", nameof(items));
			if (copied.Select(x => x.DestinationPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copied.Count)
				throw new ArgumentException("A Vortex file list cannot contain duplicate native destinations.", nameof(items));
			if (copied.Select(x => x.HashAlgorithm).Distinct().Count() != 1)
				throw new ArgumentException("A characterized Vortex file list must use one homogeneous checksum algorithm.", nameof(items));
			_items = new ReadOnlyCollection<CollectionVortexFileListItem>(copied);
		}

		public IReadOnlyList<CollectionVortexFileListItem> Items { get { return _items; } }
		public CollectionVortexFileListHashAlgorithm HashAlgorithm { get { return _items[0].HashAlgorithm; } }

		public bool Equals(CollectionVortexFileList other)
		{
			return !ReferenceEquals(other, null) && _items.SequenceEqual(other._items);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionVortexFileList); }
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				foreach (CollectionVortexFileListItem item in _items) hash = (hash * 397) ^ item.GetHashCode();
				return hash;
			}
		}
	}
}
