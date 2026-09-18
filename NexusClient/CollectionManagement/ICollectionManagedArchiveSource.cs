using System.Collections.Generic;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Finds NMM-managed archive candidates whose native metadata may correspond to one requested Collection artifact.
	/// </summary>
	public interface ICollectionManagedArchiveSource
	{
		/// <summary>
		/// Returns bounded candidate metadata for the requested artifact. Returned paths are still untrusted/mutable until sealed.
		/// </summary>
		IReadOnlyList<CollectionManagedArchiveCandidate> FindCandidates(CollectionArtifactReference requestedArtifact);
	}
}
