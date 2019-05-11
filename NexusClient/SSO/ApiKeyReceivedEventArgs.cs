namespace Nexus.Client.SSO
{
    using System;

    /// <summary>
    /// Arguments related to the ApiKeyReceived event.
    /// </summary>
    public class ApiKeyReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// API key that was received.
        /// </summary>
        public string ApiKey { get; }

        /// <summary>
        /// Creates a new <see cref="ApiKeyReceivedEventArgs"/>.
        /// </summary>
        /// <param name="apiKey">API key that was received.</param>
        public ApiKeyReceivedEventArgs(string apiKey)
        {
            ApiKey = apiKey;
        }
    }
}
