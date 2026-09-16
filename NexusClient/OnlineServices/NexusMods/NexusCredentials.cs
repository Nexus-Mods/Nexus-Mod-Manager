using System;

namespace Nexus.Client.OnlineServices.NexusMods
{
    /// <summary>
    /// Stores Nexus Mods credentials without exposing their secret value through diagnostics or formatting.
    /// </summary>
    public sealed class NexusCredentials
    {
        private readonly string _secret;

        /// <summary>
        /// Initializes credentials with the selected authentication mechanism and secret material.
        /// </summary>
        private NexusCredentials(NexusCredentialKind kind, string secret)
        {
            Kind = kind;
            _secret = secret;
        }

        /// <summary>
        /// Gets an empty credential set.
        /// </summary>
        public static NexusCredentials None { get; } = new NexusCredentials(NexusCredentialKind.None, null);

        /// <summary>
        /// Gets the credential mechanism.
        /// </summary>
        public NexusCredentialKind Kind { get; }

        /// <summary>
        /// Gets whether authentication material is available.
        /// </summary>
        public bool HasCredentials => Kind != NexusCredentialKind.None;

        /// <summary>
        /// Creates credentials backed by a Nexus Mods API key.
        /// </summary>
        public static NexusCredentials FromApiKey(string apiKey)
        {
            return new NexusCredentials(NexusCredentialKind.ApiKey, RequireSecret(apiKey, nameof(apiKey)));
        }

        /// <summary>
        /// Creates credentials backed by a bearer token for future OAuth-based authentication.
        /// </summary>
        public static NexusCredentials FromBearerToken(string bearerToken)
        {
            return new NexusCredentials(NexusCredentialKind.BearerToken, RequireSecret(bearerToken, nameof(bearerToken)));
        }

        /// <summary>
        /// Returns a non-secret description suitable for diagnostics.
        /// </summary>
        public override string ToString()
        {
            return Kind.ToString();
        }

        /// <summary>
        /// Gets the secret value for the Nexus request policy only.
        /// </summary>
        internal string GetSecret()
        {
            return _secret;
        }

        /// <summary>
        /// Rejects missing authentication material while preserving the supplied secret verbatim.
        /// </summary>
        private static string RequireSecret(string secret, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(secret))
                throw new ArgumentException("Nexus Mods credentials cannot be empty.", parameterName);

            return secret;
        }
    }
}
