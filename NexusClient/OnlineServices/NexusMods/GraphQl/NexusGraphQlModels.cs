using System.Collections.Generic;
using Newtonsoft.Json;

namespace Nexus.Client.OnlineServices.NexusMods.GraphQl
{
    /// <summary>
    /// Represents a standard GraphQL response envelope while preserving partial data alongside provider errors.
    /// </summary>
    public sealed class NexusGraphQlResponse<TData>
    {
        /// <summary>
        /// Gets or sets the typed GraphQL data payload, which may be null when the operation failed completely.
        /// </summary>
        [JsonProperty("data")]
        public TData Data { get; set; }

        /// <summary>
        /// Gets or sets provider-reported GraphQL errors, which may accompany otherwise valid partial data.
        /// </summary>
        [JsonProperty("errors")]
        public NexusGraphQlError[] Errors { get; set; }

        /// <summary>
        /// Gets whether the response contains one or more GraphQL errors.
        /// </summary>
        [JsonIgnore]
        public bool HasErrors => Errors != null && Errors.Length > 0;
    }

    /// <summary>
    /// Represents one GraphQL error while retaining standard path, location, and provider-extension metadata.
    /// </summary>
    public sealed class NexusGraphQlError
    {
        /// <summary>
        /// Gets or sets the provider-supplied error message.
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the response-field path associated with the error, preserving string and numeric segments.
        /// </summary>
        [JsonProperty("path")]
        public object[] Path { get; set; }

        /// <summary>
        /// Gets or sets source locations associated with the error.
        /// </summary>
        [JsonProperty("locations")]
        public NexusGraphQlLocation[] Locations { get; set; }

        /// <summary>
        /// Gets or sets arbitrary provider-specific GraphQL error extensions.
        /// </summary>
        [JsonProperty("extensions")]
        public IDictionary<string, object> Extensions { get; set; }

        /// <summary>
        /// Gets the conventional provider error code from the extensions object when one is present.
        /// </summary>
        [JsonIgnore]
        public string Code
        {
            get
            {
                object code;
                return Extensions != null && Extensions.TryGetValue("code", out code) && code != null ? code.ToString() : null;
            }
        }
    }

    /// <summary>
    /// Represents a line and column in the submitted GraphQL document.
    /// </summary>
    public sealed class NexusGraphQlLocation
    {
        /// <summary>
        /// Gets or sets the one-based GraphQL document line number.
        /// </summary>
        [JsonProperty("line")]
        public int Line { get; set; }

        /// <summary>
        /// Gets or sets the one-based GraphQL document column number.
        /// </summary>
        [JsonProperty("column")]
        public int Column { get; set; }
    }

    /// <summary>
    /// Represents the standard JSON request envelope accepted by a GraphQL HTTP endpoint.
    /// </summary>
    internal sealed class NexusGraphQlRequest
    {
        /// <summary>
        /// Initializes a GraphQL request envelope.
        /// </summary>
        public NexusGraphQlRequest(string query, object variables, string operationName)
        {
            Query = query;
            Variables = variables;
            OperationName = operationName;
        }

        /// <summary>
        /// Gets the GraphQL document to execute.
        /// </summary>
        [JsonProperty("query")]
        public string Query { get; }

        /// <summary>
        /// Gets optional GraphQL variables for the operation.
        /// </summary>
        [JsonProperty("variables", NullValueHandling = NullValueHandling.Ignore)]
        public object Variables { get; }

        /// <summary>
        /// Gets the optional operation name when the document contains named operations.
        /// </summary>
        [JsonProperty("operationName", NullValueHandling = NullValueHandling.Ignore)]
        public string OperationName { get; }
    }
}
