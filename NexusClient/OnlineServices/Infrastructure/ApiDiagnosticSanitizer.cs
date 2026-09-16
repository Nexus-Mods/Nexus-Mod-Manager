using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.OnlineServices.Infrastructure
{
    /// <summary>
    /// Redacts credentials and caller-selected secrets before HTTP diagnostics are written to logs.
    /// </summary>
    public static class ApiDiagnosticSanitizer
    {
        private const string RedactedValue = "<redacted>";
        private static readonly string[] StandardSensitiveHeaders = { "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie" };

        /// <summary>
        /// Returns a copy of HTTP headers with standard and caller-specified sensitive values redacted.
        /// </summary>
        public static IReadOnlyDictionary<string, string[]> SanitizeHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, IEnumerable<string> additionalSensitiveHeaders = null)
        {
            var sensitiveHeaders = new HashSet<string>(StandardSensitiveHeaders, StringComparer.OrdinalIgnoreCase);
            AddNames(sensitiveHeaders, additionalSensitiveHeaders);

            var sanitized = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (headers == null)
                return sanitized;

            foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
            {
                sanitized[header.Key] = sensitiveHeaders.Contains(header.Key)
                    ? new[] { RedactedValue }
                    : (header.Value == null ? new string[0] : header.Value.ToArray());
            }

            return sanitized;
        }

        /// <summary>
        /// Returns a URI string with caller-specified query parameters redacted, or with every query value redacted when requested.
        /// </summary>
        public static string SanitizeUri(Uri uri, IEnumerable<string> sensitiveQueryNames = null, bool redactAllQueryValues = false)
        {
            if (uri == null)
                return string.Empty;

            var sensitiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNames(sensitiveNames, sensitiveQueryNames);
            if ((!redactAllQueryValues && sensitiveNames.Count == 0) || string.IsNullOrEmpty(uri.Query))
                return uri.ToString();

            var builder = new UriBuilder(uri);
            string query = uri.Query.TrimStart('?');
            string[] pairs = query.Split('&');
            var sanitized = new StringBuilder();

            foreach (string pair in pairs)
            {
                if (sanitized.Length > 0)
                    sanitized.Append('&');

                int separator = pair.IndexOf('=');
                string encodedName = separator < 0 ? pair : pair.Substring(0, separator);
                string decodedName = Uri.UnescapeDataString(encodedName.Replace("+", " "));
                if (!redactAllQueryValues && !sensitiveNames.Contains(decodedName))
                {
                    sanitized.Append(pair);
                    continue;
                }

                sanitized.Append(encodedName);
                if (separator >= 0)
                    sanitized.Append('=').Append(Uri.EscapeDataString(RedactedValue));
            }

            builder.Query = sanitized.ToString();
            return builder.Uri.ToString();
        }

        /// <summary>
        /// Adds non-empty names to a case-insensitive sensitive-name set.
        /// </summary>
        private static void AddNames(ISet<string> target, IEnumerable<string> names)
        {
            if (names == null)
                return;

            foreach (string name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    target.Add(name);
            }
        }
    }
}
