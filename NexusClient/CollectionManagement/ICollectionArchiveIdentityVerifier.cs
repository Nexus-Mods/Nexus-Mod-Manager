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
}
