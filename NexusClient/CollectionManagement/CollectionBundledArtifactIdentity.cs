using System;
using System.Security.Cryptography;
using System.Text;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies a Vortex Collection member whose bytes are carried inside one exact retained Collection revision bundle.
	/// </summary>
	/// <remarks>
	/// Vortex bundle members do not have a stable archive hash because Vortex stores them unpacked and recompresses them on import.
	/// The portable reference tag therefore identifies the logical member, while the owning immutable revision prevents tags from
	/// colliding across revisions. Materialized archive bytes receive their own retained SHA-256 identity later.
	/// </remarks>
	internal static class CollectionBundledArtifactIdentity
	{
		public const string Scheme = "collection-bundle";

		/// <summary>Creates the stable logical artifact token for one bundle tag in one exact revision.</summary>
		public static string Format(CollectionRevisionIdentity revision, string referenceTag)
		{
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			ValidateReferenceTag(referenceTag, nameof(referenceTag));

			string revisionFingerprint;
			using (SHA256 sha = SHA256.Create())
			{
				string payload = ((int)revision.Collection.Origin).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
					revision.Collection.StableId + "\n" + revision.StableRevisionId + "\n" +
					(revision.NexusRevisionNumber.HasValue
						? revision.NexusRevisionNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
						: String.Empty);
				byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
				var builder = new StringBuilder(digest.Length * 2);
				for (int index = 0; index < digest.Length; index++)
					builder.Append(digest[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
				revisionFingerprint = builder.ToString();
			}
			return revisionFingerprint + "/" + referenceTag;
		}

		/// <summary>Returns whether the artifact is the characterized embedded-bundle scheme.</summary>
		public static bool IsBundle(CollectionArtifactReference artifact)
		{
			return artifact != null && StringComparer.Ordinal.Equals(artifact.Scheme, Scheme);
		}

		/// <summary>Validates the portable Vortex reference tag used in the stable token.</summary>
		public static bool IsValidReferenceTag(string referenceTag)
		{
			if (String.IsNullOrWhiteSpace(referenceTag) || !StringComparer.Ordinal.Equals(referenceTag, referenceTag.Trim()))
				return false;
			for (int index = 0; index < referenceTag.Length; index++)
			{
				char value = referenceTag[index];
				if (Char.IsControl(value) || value == '/' || value == '\\')
					return false;
			}
			return true;
		}

		private static void ValidateReferenceTag(string referenceTag, string parameterName)
		{
			if (!IsValidReferenceTag(referenceTag))
				throw new ArgumentException("A Collection bundle reference tag must be a non-empty normalized path-free token.", parameterName);
		}
	}
}
