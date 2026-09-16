using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace Nexus.Client.OnlineServices.Infrastructure
{
    /// <summary>
    /// Provides provider-neutral JSON serialization helpers for online-service clients.
    /// </summary>
    public static class ApiJson
    {
        /// <summary>
        /// Serializes a value using NMM's existing Newtonsoft.Json dependency.
        /// </summary>
        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value);
        }

        /// <summary>
        /// Deserializes JSON into the requested type.
        /// </summary>
        public static T Deserialize<T>(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            return JsonConvert.DeserializeObject<T>(json);
        }

        /// <summary>
        /// Creates UTF-8 JSON HTTP content for a request body.
        /// </summary>
        public static StringContent CreateContent(object value)
        {
            return new StringContent(Serialize(value), Encoding.UTF8, "application/json");
        }
    }
}
