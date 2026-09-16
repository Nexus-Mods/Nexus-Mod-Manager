using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nexus.Client.OnlineServices.Infrastructure;

namespace Nexus.Client.OnlineServices.NexusMods.V1
{
    /// <summary>
    /// Provides NMM-owned access to the Nexus Mods REST v1 endpoints used by the legacy repository.
    /// </summary>
    public sealed class NexusV1Client
    {
        private static readonly Uri BaseUri = new Uri("https://api.nexusmods.com/v1/", UriKind.Absolute);
        private readonly NexusModsService _service;

        /// <summary>
        /// Initializes the REST v1 client over the shared Nexus Mods service.
        /// </summary>
        public NexusV1Client(NexusModsService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Validates the current Nexus Mods credentials and returns account metadata.
        /// </summary>
        public Task<NexusV1User> ValidateUserAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAsync<NexusV1User>("users/validate.json", cancellationToken);
        }

        /// <summary>
        /// Gets metadata for one mod.
        /// </summary>
        public Task<NexusV1Mod> GetModAsync(string domainName, int modId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAsync<NexusV1Mod>($"games/{RequireSegment(domainName, nameof(domainName))}/mods/{modId}.json", cancellationToken);
        }

        /// <summary>
        /// Gets files for one mod, optionally filtered by legacy v1 file categories.
        /// </summary>
        public Task<NexusV1ModFileList> GetModFilesAsync(string domainName, int modId, params NexusV1FileCategory[] categories)
        {
            return GetModFilesAsync(domainName, modId, default(CancellationToken), categories);
        }

        /// <summary>
        /// Gets files for one mod with explicit cancellation, optionally filtered by legacy v1 file categories.
        /// </summary>
        public Task<NexusV1ModFileList> GetModFilesAsync(string domainName, int modId, CancellationToken cancellationToken, params NexusV1FileCategory[] categories)
        {
            string relativeUri = $"games/{RequireSegment(domainName, nameof(domainName))}/mods/{modId}/files.json";
            string categoryFilter = BuildCategoryFilter(categories);
            if (!string.IsNullOrEmpty(categoryFilter))
                relativeUri += "?category=" + Uri.EscapeDataString(categoryFilter);

            return GetAsync<NexusV1ModFileList>(relativeUri, cancellationToken);
        }

        /// <summary>
        /// Gets metadata for one specific mod file.
        /// </summary>
        public Task<NexusV1ModFile> GetModFileAsync(string domainName, int modId, int fileId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAsync<NexusV1ModFile>($"games/{RequireSegment(domainName, nameof(domainName))}/mods/{modId}/files/{fileId}.json", cancellationToken);
        }

        /// <summary>
        /// Gets download links for a mod file using the authenticated Premium-account flow.
        /// </summary>
        public Task<NexusV1DownloadLink[]> GetDownloadLinksAsync(string domainName, int modId, int fileId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAsync<NexusV1DownloadLink[]>($"games/{RequireSegment(domainName, nameof(domainName))}/mods/{modId}/files/{fileId}/download_link.json", cancellationToken);
        }

        /// <summary>
        /// Gets download links for a mod file using the website-authorized free-account key and expiry values.
        /// </summary>
        public Task<NexusV1DownloadLink[]> GetDownloadLinksAsync(string domainName, int modId, int fileId, string key, int expiry, CancellationToken cancellationToken = default(CancellationToken))
        {
            string relativeUri = $"games/{RequireSegment(domainName, nameof(domainName))}/mods/{modId}/files/{fileId}/download_link.json" +
                                 $"?key={Uri.EscapeDataString(key ?? string.Empty)}&expires={expiry.ToString(CultureInfo.InvariantCulture)}";
            return GetAsync<NexusV1DownloadLink[]>(relativeUri, cancellationToken);
        }

        /// <summary>
        /// Endorses a mod using the legacy case-sensitive REST v1 request body.
        /// </summary>
        public Task EndorseAsync(string domainName, int modId, string version, CancellationToken cancellationToken = default(CancellationToken))
        {
            return SendMutationAsync(
                $"games/{RequireSegment(domainName, nameof(domainName))}/mods/{modId}/endorse.json",
                new { Version = version },
                cancellationToken);
        }

        /// <summary>
        /// Removes an endorsement by posting the legacy abstain action.
        /// </summary>
        public Task UnendorseAsync(string domainName, int modId, string version, CancellationToken cancellationToken = default(CancellationToken))
        {
            return SendMutationAsync(
                $"games/{RequireSegment(domainName, nameof(domainName))}/mods/{modId}/abstain.json",
                new { Version = version },
                cancellationToken);
        }

        /// <summary>
        /// Gets mods updated within the provider-defined v1 period.
        /// </summary>
        public Task<NexusV1ModUpdate[]> GetUpdatedModsAsync(string domainName, string period, CancellationToken cancellationToken = default(CancellationToken))
        {
            string relativeUri = $"games/{RequireSegment(domainName, nameof(domainName))}/mods/updated.json?period={Uri.EscapeDataString(RequireValue(period, nameof(period)))}";
            return GetAsync<NexusV1ModUpdate[]>(relativeUri, cancellationToken);
        }

        /// <summary>
        /// Finds mod and file metadata matching an MD5 hash.
        /// </summary>
        public Task<NexusV1ModHashResult[]> FindModsByMd5Async(string domainName, string hash, CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAsync<NexusV1ModHashResult[]>($"games/{RequireSegment(domainName, nameof(domainName))}/mods/md5_search/{RequireSegment(hash, nameof(hash))}.json", cancellationToken);
        }

        /// <summary>
        /// Gets game metadata and categories for one Nexus Mods game domain.
        /// </summary>
        public Task<NexusV1Game> GetGameAsync(string domainName, CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetAsync<NexusV1Game>($"games/{RequireSegment(domainName, nameof(domainName))}.json", cancellationToken);
        }

        /// <summary>
        /// Sends one v1 GET request and deserializes its buffered JSON response.
        /// </summary>
        private async Task<T> GetAsync<T>(string relativeUri, CancellationToken cancellationToken)
        {
            ApiResponse response = await _service.SendAsync(NexusApiSurface.V1, HttpMethod.Get, new Uri(BaseUri, relativeUri), cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                T result = ApiJson.Deserialize<T>(response.Content);
                if (ReferenceEquals(result, null))
                    throw new JsonSerializationException("The response JSON deserialized to null.");

                return result;
            }
            catch (JsonException ex)
            {
                throw new ApiException(ApiErrorKind.InvalidResponse, "Nexus Mods returned an invalid REST v1 response.", response.StatusCode, innerException: ex);
            }
        }

        /// <summary>
        /// Sends one v1 mutation request without retrying or interpreting its response body.
        /// </summary>
        private async Task SendMutationAsync(string relativeUri, object body, CancellationToken cancellationToken)
        {
            await _service.SendAsync(
                NexusApiSurface.V1,
                HttpMethod.Post,
                new Uri(BaseUri, relativeUri),
                ApiJson.CreateContent(body),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds the category filter using the same values and Deleted-category omission as the previous v1 client.
        /// </summary>
        private static string BuildCategoryFilter(IEnumerable<NexusV1FileCategory> categories)
        {
            if (categories == null)
                return null;

            string[] filters = categories
                .Where(category => category != NexusV1FileCategory.Deleted)
                .Distinct()
                .Select(GetCategoryFilter)
                .ToArray();
            return filters.Length == 0 ? null : string.Join(",", filters);
        }

        /// <summary>
        /// Converts an NMM-owned file category to the REST v1 query value.
        /// </summary>
        private static string GetCategoryFilter(NexusV1FileCategory category)
        {
            switch (category)
            {
                case NexusV1FileCategory.Main:
                    return "main";
                case NexusV1FileCategory.Update:
                    return "update";
                case NexusV1FileCategory.Optional:
                    return "optional";
                case NexusV1FileCategory.Old:
                    return "old_version";
                case NexusV1FileCategory.Miscellaneous:
                    return "miscellaneous";
                case NexusV1FileCategory.Archived:
                    return "archived";
                case NexusV1FileCategory.Deleted:
                    throw new InvalidOperationException("Deleted files cannot be requested as a REST v1 category filter.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        /// <summary>
        /// Validates and escapes one URI path segment.
        /// </summary>
        private static string RequireSegment(string value, string parameterName)
        {
            return Uri.EscapeDataString(RequireValue(value, parameterName));
        }

        /// <summary>
        /// Validates a required request value without changing legacy provider semantics.
        /// </summary>
        private static string RequireValue(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);

            return value;
        }
    }
}
