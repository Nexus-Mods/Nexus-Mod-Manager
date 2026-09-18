using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Versioned opaque fingerprint of the relevant managed state observed during collection planning.
	/// </summary>
	/// <remarks>
	/// The fingerprint producer defines which native identities/effects participate. The domain layer only preserves the
	/// version and value so a later mutation boundary can compare like-with-like rather than treating a stale preview as current.
	/// </remarks>
	public sealed class CollectionCurrentStateFingerprint : IEquatable<CollectionCurrentStateFingerprint>
	{
		/// <summary>
		/// Creates a versioned state fingerprint.
		/// </summary>
		public CollectionCurrentStateFingerprint(string formatVersion, string value)
		{
			FormatVersion = CollectionIdentityValidation.RequireOpaqueToken(formatVersion, nameof(formatVersion));
			Value = CollectionIdentityValidation.RequireOpaqueToken(value, nameof(value));
		}

		/// <summary>
		/// Gets the fingerprint format/version identifier.
		/// </summary>
		public string FormatVersion { get; }

		/// <summary>
		/// Gets the opaque fingerprint value.
		/// </summary>
		public string Value { get; }

		/// <inheritdoc />
		public bool Equals(CollectionCurrentStateFingerprint other)
		{
			return !ReferenceEquals(other, null) &&
				StringComparer.Ordinal.Equals(FormatVersion, other.FormatVersion) &&
				StringComparer.Ordinal.Equals(Value, other.Value);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionCurrentStateFingerprint);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return (StringComparer.Ordinal.GetHashCode(FormatVersion ?? string.Empty) * 397) ^
					StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return FormatVersion + ":" + Value;
		}
	}
}
