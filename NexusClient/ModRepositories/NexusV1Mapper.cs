using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Nexus.Client.ModManagement;
using Nexus.Client.OnlineServices.NexusMods;
using Nexus.Client.OnlineServices.NexusMods.V1;
using Nexus.Client.Util;

namespace Nexus.Client.ModRepositories
{
    /// <summary>
    /// Maps NMM-owned Nexus REST v1 models into existing repository-domain models.
    /// </summary>
    internal static class NexusV1Mapper
    {
        /// <summary>
        /// Maps REST v1 account metadata into repository user status information.
        /// </summary>
        public static RepositoryUserStatus ToRepositoryUserStatus(NexusV1User user)
        {
            return user == null ? null : new RepositoryUserStatus(user.Name, user.IsPremium, user.IsSupporter);
        }

        /// <summary>
        /// Maps REST v1 mod metadata into NMM mod metadata.
        /// </summary>
        public static ModInfo ToModInfo(NexusV1Mod result)
        {
            if (result == null)
                return null;

            bool? endorsementState = result.Endorsement?.Status == NexusV1EndorsementStatus.Endorsed
                ? true
                : (bool?)null;

            return new ModInfo(
                result.ModId.ToString(),
                null,
                result.Name,
                null,
                result.Version,
                result.Version,
                endorsementState,
                FindProperVersion(result.Version),
                result.Author,
                result.CategoryId,
                -1,
                result.Description,
                null,
                new Uri($"https://www.nexusmods.com/{GameDomainTranslator.DetermineGameDomain(result.DomainName)}/mods/{result.ModId}"),
                null,
                false,
                false);
        }

        /// <summary>
        /// Maps REST v1 file metadata into NMM file metadata.
        /// </summary>
        public static ModFileInfo ToModFileInfo(NexusV1ModFile modFile)
        {
            return new ModFileInfo(modFile?.FileId.ToString(), modFile?.FileName, modFile?.Name, modFile?.ModVersion);
        }

        /// <summary>
        /// Maps one REST v1 download source into the repository download-link model.
        /// </summary>
        public static RepositoryDownloadLink ToRepositoryDownloadLink(NexusV1DownloadLink link)
        {
            return link == null ? null : new RepositoryDownloadLink(link.Uri, link.CdnShortName);
        }

        /// <summary>
        /// Maps a complete REST v1 rate-limit snapshot into the repository model.
        /// </summary>
        public static RepositoryRateLimit ToRepositoryRateLimit(NexusRateLimitSnapshot owned)
        {
            if (owned == null ||
                !owned.DailyLimit.HasValue || !owned.DailyRemaining.HasValue || !owned.DailyReset.HasValue ||
                !owned.HourlyLimit.HasValue || !owned.HourlyRemaining.HasValue || !owned.HourlyReset.HasValue)
                return null;

            return new RepositoryRateLimit(
                owned.DailyLimit.Value,
                owned.DailyRemaining.Value,
                owned.DailyReset.Value,
                owned.HourlyLimit.Value,
                owned.HourlyRemaining.Value,
                owned.HourlyReset.Value);
        }

        /// <summary>
        /// Maps REST v1 game-category metadata into NMM category metadata.
        /// </summary>
        public static CategoriesInfo ToCategoriesInfo(NexusV1GameCategory category)
        {
            if (category == null)
                return null;

            return new CategoriesInfo
            {
                Id = category.Id,
                ParentId = category.ParentCategory,
                Name = category.Name
            };
        }

        /// <summary>
        /// Converts Nexus' free-form mod version into the legacy NMM machine version.
        /// </summary>
        private static Version FindProperVersion(string input)
        {
            var properVersion = new Version(0, 0);
            var version = Regex.Replace(input, "[^.0-9]", string.Empty);

            if (version.EndsWith("."))
                version = version.TrimEnd('.');

            try
            {
                if (!string.IsNullOrEmpty(version))
                {
                    var crazyVersioningCheck = version.IndexOf(".");
                    if (crazyVersioningCheck > 0)
                        properVersion = new Version(version);
                    else if (crazyVersioningCheck == 0)
                        properVersion = new Version("0" + version);
                    else
                        properVersion = new Version(version + ".0");
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Could not determine version from \"{input}\", falling back to default version.");
                TraceUtil.TraceException(ex);
            }

            return properVersion;
        }
    }
}
