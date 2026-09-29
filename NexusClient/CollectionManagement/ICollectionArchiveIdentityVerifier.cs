using System.IO;
using System.Threading;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Verifies that immutable candidate bytes correspond to the exact provider artifact identity requested by a Collection.
	/// </summary>
	public interface ICollectionArchiveIdentityVerifier
	{
		/// <summary>
		/// Returns whether the supplied immutable bytes are recognized as the exact requested provider artifact.
		/// </summary>
		bool IsExactMatch(CollectionArtifactReference requestedArtifact, Stream immutableArchive, CancellationToken cancellationToken);
	}

	/// <summary>
	/// Optional fast path for provider verifiers which can consume an MD5 digest already calculated while immutable bytes were retained.
	/// </summary>
	/// <remarks>
	/// Implementations must apply the same exact provider-identity check as <see cref="ICollectionArchiveIdentityVerifier.IsExactMatch"/>;
	/// this interface only avoids rereading a multi-gigabyte retained archive to calculate the provider lookup digest again.
	/// </remarks>
	public interface ICollectionArchiveMd5IdentityVerifier
	{
		/// <summary>Returns whether the supplied MD5 digest identifies the exact requested provider artifact.</summary>
		bool IsExactMatchByMd5(CollectionArtifactReference requestedArtifact, string md5, CancellationToken cancellationToken);
	}
}
