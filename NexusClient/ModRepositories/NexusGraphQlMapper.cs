using System;
using Nexus.Client.OnlineServices.NexusMods.GraphQl;
using Nexus.Client.Util;

namespace Nexus.Client.ModRepositories
{
    /// <summary>
    /// Maps GraphQL bulk parent-mod metadata into the existing NMM repository model.
    /// </summary>
    internal static class NexusGraphQlMapper
    {
        /// <summary>
        /// Maps one complete GraphQL legacy-mod result while preserving REST v1 endorsement and version semantics.
        /// </summary>
        public static ModInfo ToModInfo(NexusLegacyModMetadata result)
        {
            if (result == null || !result.HasRequiredParentMetadata)
                return null;

            bool? endorsementState = result.ViewerEndorsed == true ? true : (bool?)null;
            return new ModInfo(
                result.ModId.ToString(),
                null,
                result.Name,
                null,
                result.Version,
                result.Version,
                endorsementState,
                NexusV1Mapper.FindProperVersion(result.Version),
                result.Author,
                result.CategoryId.Value,
                -1,
                result.Description,
                null,
                new Uri($"https://www.nexusmods.com/{GameDomainTranslator.DetermineGameDomain(result.GameDomain)}/mods/{result.ModId}"),
                null,
                false,
                false);
        }
    }
}
