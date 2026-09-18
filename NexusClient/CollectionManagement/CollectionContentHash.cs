using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies the digest algorithm used for exact retained or expected collection content.
	/// </summary>
	public enum CollectionContentHashAlgorithm
	{
		/// <summary>
		/// No supported digest algorithm has been selected.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// SHA-256 over the canonical byte sequence.
		/// </summary>
		Sha256 = 1
	}

	/// <summary>
	/// Immutable cryptographic content digest used by collection-domain records.
	/// </summary>
	public sealed class CollectionContentHash : IEquatable<CollectionContentHash>
	{
		private CollectionContentHash(CollectionContentHashAlgorithm algorithm, string value)
		{
			Algorithm = algorithm;
			Value = value;
		}

		/// <summary>
		/// Gets the digest algorithm.
		/// </summary>
		public CollectionContentHashAlgorithm Algorithm { get; }

		/// <summary>
		/// Gets the canonical lowercase hexadecimal digest.
		/// </summary>
		public string Value { get; }

		/// <summary>
		/// Creates a SHA-256 content hash from a 64-character hexadecimal digest.
		/// </summary>
		public static CollectionContentHash FromSha256(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentException("A SHA-256 digest is required.", nameof(value));
			if (!StringComparer.Ordinal.Equals(value, value.Trim()))
				throw new ArgumentException("A SHA-256 digest must not contain leading or trailing whitespace.", nameof(value));
			if (value.Length != 64)
				throw new ArgumentException("A SHA-256 digest must contain exactly 64 hexadecimal characters.", nameof(value));

			for (int index = 0; index < value.Length; index++)
			{
				char character = value[index];
				bool isHex = (character >= '0' && character <= '9') ||
					(character >= 'a' && character <= 'f') ||
					(character >= 'A' && character <= 'F');
				if (!isHex)
					throw new ArgumentException("A SHA-256 digest may contain only hexadecimal characters.", nameof(value));
			}

			return new CollectionContentHash(CollectionContentHashAlgorithm.Sha256, value.ToLowerInvariant());
		}

		/// <inheritdoc />
		public bool Equals(CollectionContentHash other)
		{
			return !ReferenceEquals(other, null) &&
				Algorithm == other.Algorithm &&
				StringComparer.Ordinal.Equals(Value, other.Value);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionContentHash);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return ((int)Algorithm * 397) ^ StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return Algorithm + ":" + Value;
		}
	}
}
