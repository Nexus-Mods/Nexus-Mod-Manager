using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Nexus.Client.OnlineServices.Infrastructure
{
    /// <summary>
    /// Contains a buffered HTTP response whose underlying network response has already been disposed.
    /// </summary>
    public sealed class ApiResponse
    {
        private readonly IReadOnlyDictionary<string, string[]> _headers;

        /// <summary>
        /// Initializes a buffered API response.
        /// </summary>
        internal ApiResponse(HttpStatusCode statusCode, string reasonPhrase, string content, IDictionary<string, string[]> headers)
        {
            StatusCode = statusCode;
            ReasonPhrase = reasonPhrase ?? string.Empty;
            Content = content ?? string.Empty;
            _headers = new Dictionary<string, string[]>(headers ?? new Dictionary<string, string[]>(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the HTTP status code.
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// Gets the HTTP reason phrase when one was supplied.
        /// </summary>
        public string ReasonPhrase { get; }

        /// <summary>
        /// Gets the fully buffered response body.
        /// </summary>
        public string Content { get; }

        /// <summary>
        /// Gets whether the status code is in the HTTP success range.
        /// </summary>
        public bool IsSuccessStatusCode => (int)StatusCode >= 200 && (int)StatusCode <= 299;

        /// <summary>
        /// Gets the buffered response and content headers using case-insensitive names.
        /// </summary>
        public IReadOnlyDictionary<string, string[]> Headers => _headers;

        /// <summary>
        /// Tries to retrieve all values associated with a response header.
        /// </summary>
        public bool TryGetHeader(string name, out string[] values)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                values = null;
                return false;
            }

            return _headers.TryGetValue(name, out values);
        }

        /// <summary>
        /// Creates a buffered response from an HTTP response message.
        /// </summary>
        internal static ApiResponse FromHttpResponse(HttpResponseMessage response, string content)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            CopyHeaders(response.Headers, headers);
            if (response.Content != null)
                CopyHeaders(response.Content.Headers, headers);

            return new ApiResponse(response.StatusCode, response.ReasonPhrase, content, headers);
        }

        /// <summary>
        /// Copies HTTP headers into the buffered header collection.
        /// </summary>
        private static void CopyHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> source, IDictionary<string, string[]> destination)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in source)
                destination[header.Key] = header.Value == null ? new string[0] : header.Value.ToArray();
        }
    }
}
