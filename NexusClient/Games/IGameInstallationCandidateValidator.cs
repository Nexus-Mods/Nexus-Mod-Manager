using System;

namespace Nexus.Client.Games
{
    /// <summary>
    /// Optional edition-aware validation for a candidate game installation. This is read-only
    /// discovery validation, not ownership authorization for native installation/deployment.
    /// </summary>
    public interface IGameInstallationCandidateValidator
    {
        bool IsValidInstallationCandidate(string absoluteGameRoot);
    }
}
