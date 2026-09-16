using System.Collections.Generic;
using Newtonsoft.Json;

namespace Nexus.Client.OnlineServices.NexusMods.V3
{
    /// <summary>
    /// Represents an RFC 9457 Problem Details response returned by Nexus Mods REST v3.
    /// </summary>
    public sealed class NexusV3ProblemDetails
    {
        /// <summary>
        /// Gets or sets the URI identifying the problem type.
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the short human-readable problem title.
        /// </summary>
        [JsonProperty("title")]
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the provider-reported HTTP status code.
        /// </summary>
        [JsonProperty("status")]
        public int? Status { get; set; }

        /// <summary>
        /// Gets or sets the explanation specific to this occurrence.
        /// </summary>
        [JsonProperty("detail")]
        public string Detail { get; set; }

        /// <summary>
        /// Gets or sets the URI identifying this specific occurrence.
        /// </summary>
        [JsonProperty("instance")]
        public string Instance { get; set; }

        /// <summary>
        /// Gets or sets validation failures associated with the request, when supplied.
        /// </summary>
        [JsonProperty("errors")]
        public NexusV3ValidationProblemItem[] Errors { get; set; }

        /// <summary>
        /// Gets or sets additional provider fields so additive v3 Problem Details extensions are not lost.
        /// </summary>
        [JsonExtensionData]
        public IDictionary<string, object> AdditionalFields { get; set; }
    }

    /// <summary>
    /// Represents one Nexus Mods REST v3 validation error and its JSON Pointer.
    /// </summary>
    public sealed class NexusV3ValidationProblemItem
    {
        /// <summary>
        /// Gets or sets the human-readable validation failure detail.
        /// </summary>
        [JsonProperty("detail")]
        public string Detail { get; set; }

        /// <summary>
        /// Gets or sets the RFC 6901 JSON Pointer identifying the invalid field.
        /// </summary>
        [JsonProperty("pointer")]
        public string Pointer { get; set; }

        /// <summary>
        /// Gets or sets additive provider fields not known to this client version.
        /// </summary>
        [JsonExtensionData]
        public IDictionary<string, object> AdditionalFields { get; set; }
    }
}
