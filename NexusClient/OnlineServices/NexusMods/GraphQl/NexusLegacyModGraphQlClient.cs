using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nexus.Client.OnlineServices.Infrastructure;

namespace Nexus.Client.OnlineServices.NexusMods.GraphQl
{
    /// <summary>
    /// Resolves legacy Nexus mod identities through the GraphQL v2 bulk lookup while preserving partial results for REST fallback.
    /// </summary>
    internal sealed class NexusLegacyModGraphQlClient
    {
        // This is a conservative NMM request/page size, not a claimed Nexus provider maximum.
        private const int DefaultBatchSize = 50;
        private const string OperationName = "NmmLegacyModsByDomain";
        private const string Query = @"query NmmLegacyModsByDomain($ids: [CompositeDomainWithIdInput!]!, $offset: Int, $count: Int) {
  legacyModsByDomain(ids: $ids, offset: $offset, count: $count) {
    nodes {
      modId
      name
      version
      author
      description
      modCategory { categoryId }
      viewerEndorsed
    }
    totalCount
  }
}";

        private readonly NexusModsService _service;

        /// <summary>
        /// Initializes the bulk legacy-mod resolver over the shared Nexus provider service.
        /// </summary>
        public NexusLegacyModGraphQlClient(NexusModsService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Resolves the requested legacy mod IDs in bounded GraphQL batches for one game domain.
        /// Missing or unusable identities are intentionally omitted so the repository can fall back to REST v1 per identity.
        /// </summary>
        public async Task<NexusLegacyModLookupResult> GetModsByDomainAsync(string gameDomain, IEnumerable<int> modIds, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(gameDomain))
                throw new ArgumentException("A Nexus game domain is required.", nameof(gameDomain));
            if (modIds == null)
                throw new ArgumentNullException(nameof(modIds));

            int[] requestedIds = modIds.Where(id => id > 0).Distinct().ToArray();
            NexusSessionContext session = _service.CaptureSession();
            var result = new NexusLegacyModLookupResult(session.Generation);
            if (requestedIds.Length == 0)
                return result;

            try
            {
                using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Token))
                {
                    for (int batchStart = 0; batchStart < requestedIds.Length; batchStart += DefaultBatchSize)
                    {
                        int batchLength = Math.Min(DefaultBatchSize, requestedIds.Length - batchStart);
                        int[] batchIds = new int[batchLength];
                        Array.Copy(requestedIds, batchStart, batchIds, 0, batchLength);
                        var requestedBatchIds = new HashSet<int>(batchIds);
                        int offset = 0;

                        while (offset < batchIds.Length)
                        {
                            int count = batchIds.Length - offset;
                            var variables = new
                            {
                                ids = batchIds.Select(id => new NexusLegacyModGraphQlIdentityInput(gameDomain, id)).ToArray(),
                                offset,
                                count
                            };

                            result.RequestCount++;
                            NexusGraphQlResponse<NexusLegacyModsByDomainData> response = await _service.GraphQl.ExecuteAsync<NexusLegacyModsByDomainData>(
                                Query,
                                variables,
                                OperationName,
                                linkedCancellation.Token).ConfigureAwait(false);

                            NexusLegacyModPage page = response?.Data?.LegacyModsByDomain;
                            if (page == null)
                            {
                                result.IsIncomplete = true;
                                return result;
                            }

                            bool invalidatePage;
                            HashSet<int> invalidNodeIndexes = GetInvalidNodeIndexes(response.Errors, out invalidatePage);
                            if (invalidatePage)
                            {
                                result.IsIncomplete = true;
                                return result;
                            }

                            NexusLegacyModNode[] nodes = page.Nodes ?? new NexusLegacyModNode[0];
                            for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
                            {
                                if (invalidNodeIndexes.Contains(nodeIndex))
                                    continue;

                                NexusLegacyModNode node = nodes[nodeIndex];
                                if (node == null || !requestedBatchIds.Contains(node.ModId) || result.Mods.ContainsKey(node.ModId))
                                    continue;

                                result.Mods[node.ModId] = new NexusLegacyModMetadata(
                                    gameDomain,
                                    node.ModId,
                                    node.Name,
                                    node.Version,
                                    node.Author,
                                    node.Description,
                                    node.ModCategory == null ? (int?)null : node.ModCategory.CategoryId,
                                    node.ViewerEndorsed);
                            }

                            int returnedCount = nodes.Length;
                            if (returnedCount <= 0 || page.TotalCount <= offset + returnedCount || offset + returnedCount >= batchIds.Length)
                                break;

                            offset += returnedCount;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;

                // Session replacement cancels the captured generation. Preserve no stale data beyond that generation.
                result.IsIncomplete = true;
            }
            catch (ApiException ex)
            {
                result.ApiException = ex;
            }
            catch (Exception ex)
            {
                result.UnexpectedException = ex;
            }

            return result;
        }

        /// <summary>
        /// Converts GraphQL field errors into per-node invalidation when possible, preserving unaffected partial data.
        /// </summary>
        private static HashSet<int> GetInvalidNodeIndexes(NexusGraphQlError[] errors, out bool invalidatePage)
        {
            var invalidNodeIndexes = new HashSet<int>();
            invalidatePage = false;
            if (errors == null || errors.Length == 0)
                return invalidNodeIndexes;

            foreach (NexusGraphQlError error in errors)
            {
                object[] path = error?.Path;
                if (path == null || path.Length == 0)
                {
                    invalidatePage = true;
                    continue;
                }

                int nodePathIndex = -1;
                for (int index = 0; index < path.Length; index++)
                {
                    if (string.Equals(path[index]?.ToString(), "nodes", StringComparison.Ordinal))
                    {
                        nodePathIndex = index;
                        break;
                    }
                }

                if (nodePathIndex < 0 || nodePathIndex + 1 >= path.Length)
                {
                    invalidatePage = true;
                    continue;
                }

                int nodeIndex;
                if (!Int32.TryParse(path[nodePathIndex + 1]?.ToString(), out nodeIndex) || nodeIndex < 0)
                {
                    invalidatePage = true;
                    continue;
                }

                invalidNodeIndexes.Add(nodeIndex);
            }

            return invalidNodeIndexes;
        }

        /// <summary>
        /// Represents one GraphQL composite legacy identity input.
        /// </summary>
        private sealed class NexusLegacyModGraphQlIdentityInput
        {
            /// <summary>
            /// Initializes one domain/mod identity input.
            /// </summary>
            public NexusLegacyModGraphQlIdentityInput(string gameDomain, int modId)
            {
                GameDomain = gameDomain;
                ModId = modId;
            }

            [JsonProperty("gameDomain")]
            public string GameDomain { get; }

            [JsonProperty("modId")]
            public int ModId { get; }
        }

        /// <summary>
        /// Represents the typed GraphQL data envelope for the legacy mod lookup.
        /// </summary>
        private sealed class NexusLegacyModsByDomainData
        {
            [JsonProperty("legacyModsByDomain")]
            public NexusLegacyModPage LegacyModsByDomain { get; set; }
        }

        /// <summary>
        /// Represents one page returned by the legacy mod lookup.
        /// </summary>
        private sealed class NexusLegacyModPage
        {
            [JsonProperty("nodes")]
            public NexusLegacyModNode[] Nodes { get; set; }

            [JsonProperty("totalCount")]
            public int TotalCount { get; set; }
        }

        /// <summary>
        /// Represents the GraphQL parent-mod fields required by the repository mapper.
        /// </summary>
        private sealed class NexusLegacyModNode
        {
            [JsonProperty("modId")]
            public int ModId { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("author")]
            public string Author { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("modCategory")]
            public NexusLegacyModCategory ModCategory { get; set; }

            [JsonProperty("viewerEndorsed")]
            public bool? ViewerEndorsed { get; set; }
        }

        /// <summary>
        /// Represents the nested Nexus mod category returned by GraphQL.
        /// </summary>
        private sealed class NexusLegacyModCategory
        {
            [JsonProperty("categoryId")]
            public int CategoryId { get; set; }
        }
    }

    /// <summary>
    /// Stores one GraphQL bulk parent lookup, including partial results and any failure that requires REST v1 fallback.
    /// </summary>
    internal sealed class NexusLegacyModLookupResult
    {
        /// <summary>
        /// Initializes one operation-scoped bulk lookup result for the captured Nexus session generation.
        /// </summary>
        public NexusLegacyModLookupResult(long generation)
        {
            Generation = generation;
            Mods = new Dictionary<int, NexusLegacyModMetadata>();
        }

        public long Generation { get; }

        public Dictionary<int, NexusLegacyModMetadata> Mods { get; }

        public int RequestCount { get; set; }

        public bool IsIncomplete { get; set; }

        public ApiException ApiException { get; set; }

        public Exception UnexpectedException { get; set; }
    }

    /// <summary>
    /// Contains the GraphQL parent-mod fields needed to reproduce NMM's existing repository metadata mapping.
    /// </summary>
    internal sealed class NexusLegacyModMetadata
    {
        /// <summary>
        /// Initializes one NMM-owned GraphQL parent metadata record.
        /// </summary>
        public NexusLegacyModMetadata(string gameDomain, int modId, string name, string version, string author, string description, int? categoryId, bool? viewerEndorsed)
        {
            GameDomain = gameDomain;
            ModId = modId;
            Name = name;
            Version = version;
            Author = author;
            Description = description;
            CategoryId = categoryId;
            ViewerEndorsed = viewerEndorsed;
        }

        public string GameDomain { get; }

        public int ModId { get; }

        public string Name { get; }

        public string Version { get; }

        public string Author { get; }

        public string Description { get; }

        public int? CategoryId { get; }

        public bool? ViewerEndorsed { get; }

        /// <summary>
        /// Gets whether this result contains all fields required to preserve the existing REST v1 parent-metadata behavior.
        /// </summary>
        public bool HasRequiredParentMetadata =>
            ModId > 0 &&
            !string.IsNullOrWhiteSpace(GameDomain) &&
            Name != null &&
            Version != null &&
            Description != null &&
            CategoryId.HasValue;
    }
}
