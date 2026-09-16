using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Nexus.Client.OnlineServices.NexusMods
{
    /// <summary>
    /// Validates Nexus Mods API destinations and applies credentials only to approved requests.
    /// </summary>
    public sealed class NexusRequestPolicy
    {
        /// <summary>
        /// Nexus Mods API host approved to receive account credentials.
        /// </summary>
        public const string ApiHost = "api.nexusmods.com";

        private static readonly IReadOnlyCollection<string> _sensitiveHeaderNames = new ReadOnlyCollection<string>(new[] { "apikey" });
        private static readonly IReadOnlyCollection<string> _sensitiveQueryParameterNames = new ReadOnlyCollection<string>(new[] { "key" });
        private readonly string _userAgent;
        private readonly string _applicationName;
        private readonly string _applicationVersion;

        /// <summary>
        /// Gets Nexus-specific credential headers which must be redacted from diagnostics.
        /// </summary>
        public static IReadOnlyCollection<string> SensitiveHeaderNames => _sensitiveHeaderNames;

        /// <summary>
        /// Gets Nexus-specific query parameters which must be redacted from diagnostics.
        /// </summary>
        public static IReadOnlyCollection<string> SensitiveQueryParameterNames => _sensitiveQueryParameterNames;

        /// <summary>
        /// Initializes the request policy with the application identity reported to Nexus Mods.
        /// </summary>
        public NexusRequestPolicy(string userAgent, string applicationName, string applicationVersion)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                throw new ArgumentException("A Nexus Mods user agent is required.", nameof(userAgent));
            if (string.IsNullOrWhiteSpace(applicationName))
                throw new ArgumentException("A Nexus Mods application name is required.", nameof(applicationName));
            if (string.IsNullOrWhiteSpace(applicationVersion))
                throw new ArgumentException("A Nexus Mods application version is required.", nameof(applicationVersion));

            _userAgent = userAgent;
            _applicationName = applicationName;
            _applicationVersion = applicationVersion;
        }

        /// <summary>
        /// Gets whether a URI is an approved Nexus Mods API destination for credentialed requests.
        /// </summary>
        public bool IsApprovedApiDestination(Uri uri)
        {
            return uri != null && uri.IsAbsoluteUri &&
                   string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(uri.Host, ApiHost, StringComparison.OrdinalIgnoreCase) &&
                   uri.IsDefaultPort;
        }

        /// <summary>
        /// Creates a Nexus Mods API request after validating the destination and applying session credentials per request.
        /// </summary>
        public HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, NexusCredentials credentials, HttpContent content = null)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));
            if (!IsApprovedApiDestination(uri))
                throw new InvalidOperationException("Nexus Mods credentials may only be sent to the approved HTTPS API host.");
            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));

            var request = new HttpRequestMessage(method, uri)
            {
                Content = content
            };
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
            request.Headers.TryAddWithoutValidation("Application-Name", _applicationName);
            request.Headers.TryAddWithoutValidation("Application-Version", _applicationVersion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            ApplyCredentials(request, credentials);
            return request;
        }

        /// <summary>
        /// Applies the selected authentication scheme after destination validation has succeeded.
        /// </summary>
        private static void ApplyCredentials(HttpRequestMessage request, NexusCredentials credentials)
        {
            switch (credentials.Kind)
            {
                case NexusCredentialKind.None:
                    return;
                case NexusCredentialKind.ApiKey:
                    request.Headers.TryAddWithoutValidation("apikey", credentials.GetSecret());
                    return;
                case NexusCredentialKind.BearerToken:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.GetSecret());
                    return;
                default:
                    throw new InvalidOperationException("Unsupported Nexus Mods credential type.");
            }
        }
    }
}
