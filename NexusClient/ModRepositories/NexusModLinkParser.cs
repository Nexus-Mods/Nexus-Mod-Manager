namespace Nexus.Client.ModRepositories
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;

	/// <summary>
	/// Represents the Nexus Mods identifiers extracted from a web or NXM link.
	/// </summary>
	public sealed class NexusModLink
	{
		/// <summary>
		/// Initializes a new parsed Nexus Mods link.
		/// </summary>
		/// <param name="gameDomain">The Nexus game domain.</param>
		/// <param name="modId">The Nexus mod identifier.</param>
		/// <param name="fileId">The optional Nexus file identifier.</param>
		/// <param name="sourceUri">The normalized source URI.</param>
		public NexusModLink(string gameDomain, string modId, string fileId, Uri sourceUri)
		{
			GameDomain = gameDomain;
			ModId = modId;
			FileId = fileId;
			SourceUri = sourceUri;
		}

		/// <summary>
		/// Gets the Nexus game domain.
		/// </summary>
		public string GameDomain { get; private set; }

		/// <summary>
		/// Gets the Nexus mod identifier.
		/// </summary>
		public string ModId { get; private set; }

		/// <summary>
		/// Gets the optional Nexus file identifier.
		/// </summary>
		public string FileId { get; private set; }

		/// <summary>
		/// Gets the normalized source URI.
		/// </summary>
		public Uri SourceUri { get; private set; }
	}

	/// <summary>
	/// Parses Nexus Mods links and builds stable mod or file navigation URLs.
	/// </summary>
	public static class NexusModLinkParser
	{
		private const string NexusHostSuffix = ".nexusmods.com";

		/// <summary>
		/// Tries to parse a Nexus Mods web or NXM link.
		/// </summary>
		/// <param name="value">The link text to parse.</param>
		/// <param name="link">The parsed Nexus Mods identifiers.</param>
		/// <returns><c>true</c> when the value identifies a Nexus mod.</returns>
		public static bool TryParse(string value, out NexusModLink link)
		{
			link = null;
			Uri uri;
			if (!TryCreateAbsoluteUri(value, out uri))
				return false;

			if (String.Equals(uri.Scheme, "nxm", StringComparison.OrdinalIgnoreCase))
				return TryParseNxmUri(uri, out link);

			if (!String.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
				!String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
				return false;

			return TryParseWebUri(uri, out link);
		}

		/// <summary>
		/// Tries to normalize a general web address, adding HTTPS when the scheme is omitted.
		/// </summary>
		/// <param name="value">The web address to normalize.</param>
		/// <param name="uri">The resulting HTTP or HTTPS URI.</param>
		/// <returns><c>true</c> when a valid web address was produced.</returns>
		public static bool TryNormalizeWebsite(string value, out Uri uri)
		{
			uri = null;
			if (!TryCreateAbsoluteUri(value, out uri))
				return false;

			if (uri.IsFile ||
				(!String.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
				 !String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
			{
				uri = null;
				return false;
			}

			return true;
		}

		/// <summary>
		/// Determines whether a Nexus identifier is a positive numeric value.
		/// </summary>
		/// <param name="value">The identifier text.</param>
		/// <returns><c>true</c> when the identifier is valid.</returns>
		public static bool IsValidId(string value)
		{
			Int32 parsed;
			return Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0;
		}

		/// <summary>
		/// Creates a canonical Nexus Mods page URI for a mod or a specific file.
		/// </summary>
		/// <param name="gameDomain">The Nexus game domain.</param>
		/// <param name="modId">The Nexus mod identifier.</param>
		/// <param name="fileId">The optional Nexus file identifier.</param>
		/// <returns>The canonical Nexus Mods URI, or <c>null</c> when the required values are invalid.</returns>
		public static Uri CreateModUri(string gameDomain, string modId, string fileId)
		{
			if (String.IsNullOrWhiteSpace(gameDomain) || !IsValidId(modId))
				return null;

			string normalizedGameDomain = gameDomain.Trim().Trim('/').ToLowerInvariant();
			string address = String.Format(
				CultureInfo.InvariantCulture,
				"https://www.nexusmods.com/{0}/mods/{1}",
				Uri.EscapeDataString(normalizedGameDomain),
				modId.Trim());

			if (IsValidId(fileId))
				address += "?tab=files&file_id=" + fileId.Trim();

			return new Uri(address, UriKind.Absolute);
		}

		/// <summary>
		/// Resolves the URI used when the user opens a mod from the Latest Version column.
		/// </summary>
		/// <param name="storedWebsite">The website explicitly stored on the mod.</param>
		/// <param name="gameDomain">The active Nexus game domain.</param>
		/// <param name="modId">The stored Nexus mod identifier.</param>
		/// <param name="fileId">The stored Nexus file identifier.</param>
		/// <returns>The best available navigation URI, or <c>null</c>.</returns>
		public static Uri ResolveNavigationUri(Uri storedWebsite, string gameDomain, string modId, string fileId)
		{
			if (storedWebsite != null)
			{
				NexusModLink parsedLink;
				if (TryParse(storedWebsite.ToString(), out parsedLink) &&
					String.IsNullOrEmpty(parsedLink.FileId) &&
					IsValidId(fileId) &&
					String.Equals(parsedLink.ModId, modId, StringComparison.OrdinalIgnoreCase))
				{
					Uri fileUri = CreateModUri(parsedLink.GameDomain, parsedLink.ModId, fileId);
					if (fileUri != null)
						return fileUri;
				}

				return storedWebsite;
			}

			return CreateModUri(gameDomain, modId, fileId);
		}

		/// <summary>
		/// Creates an absolute URI and supplies HTTPS when the input omits a scheme.
		/// </summary>
		/// <param name="value">The URI text.</param>
		/// <param name="uri">The resulting absolute URI.</param>
		/// <returns><c>true</c> when an absolute URI was created.</returns>
		private static bool TryCreateAbsoluteUri(string value, out Uri uri)
		{
			uri = null;
			if (String.IsNullOrWhiteSpace(value))
				return false;

			string normalized = value.Trim();
			if (Uri.TryCreate(normalized, UriKind.Absolute, out uri))
				return true;

			return Uri.TryCreate("https://" + normalized, UriKind.Absolute, out uri);
		}

		/// <summary>
		/// Extracts Nexus identifiers from an NXM download URI.
		/// </summary>
		/// <param name="uri">The NXM URI.</param>
		/// <param name="link">The parsed Nexus link.</param>
		/// <returns><c>true</c> when the URI identifies a Nexus mod.</returns>
		private static bool TryParseNxmUri(Uri uri, out NexusModLink link)
		{
			link = null;
			string[] segments = GetPathSegments(uri);
			if (segments.Length < 2 || !String.Equals(segments[0], "mods", StringComparison.OrdinalIgnoreCase) || !IsValidId(segments[1]))
				return false;

			string fileId = null;
			if (segments.Length >= 4 && String.Equals(segments[2], "files", StringComparison.OrdinalIgnoreCase) && IsValidId(segments[3]))
				fileId = segments[3];

			link = new NexusModLink(uri.Host.ToLowerInvariant(), segments[1], fileId, uri);
			return true;
		}

		/// <summary>
		/// Extracts Nexus identifiers from current or legacy Nexus web URLs.
		/// </summary>
		/// <param name="uri">The web URI.</param>
		/// <param name="link">The parsed Nexus link.</param>
		/// <returns><c>true</c> when the URI identifies a Nexus mod.</returns>
		private static bool TryParseWebUri(Uri uri, out NexusModLink link)
		{
			link = null;
			string host = uri.Host.ToLowerInvariant();
			if (!String.Equals(host, "nexusmods.com", StringComparison.OrdinalIgnoreCase) &&
				!host.EndsWith(NexusHostSuffix, StringComparison.OrdinalIgnoreCase))
				return false;

			string[] segments = GetPathSegments(uri);
			string gameDomain;
			string modId;

			if (String.Equals(host, "nexusmods.com", StringComparison.OrdinalIgnoreCase) ||
				String.Equals(host, "www.nexusmods.com", StringComparison.OrdinalIgnoreCase))
			{
				if (segments.Length < 3 || !String.Equals(segments[1], "mods", StringComparison.OrdinalIgnoreCase))
					return false;

				gameDomain = segments[0];
				modId = segments[2];
			}
			else
			{
				if (segments.Length < 2 || !String.Equals(segments[0], "mods", StringComparison.OrdinalIgnoreCase))
					return false;

				gameDomain = host.Substring(0, host.Length - NexusHostSuffix.Length);
				modId = segments[1];
			}

			if (!IsValidId(modId) || String.IsNullOrWhiteSpace(gameDomain))
				return false;

			string fileId = GetQueryValue(uri.Query, "file_id");
			if (!IsValidId(fileId))
				fileId = null;

			link = new NexusModLink(gameDomain.ToLowerInvariant(), modId, fileId, uri);
			return true;
		}

		/// <summary>
		/// Returns the decoded, non-empty path segments of a URI.
		/// </summary>
		/// <param name="uri">The URI to split.</param>
		/// <returns>The decoded path segments.</returns>
		private static string[] GetPathSegments(Uri uri)
		{
			return uri.AbsolutePath
				.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(Uri.UnescapeDataString)
				.ToArray();
		}

		/// <summary>
		/// Reads a decoded value from a URI query string.
		/// </summary>
		/// <param name="query">The query string.</param>
		/// <param name="name">The parameter name.</param>
		/// <returns>The decoded value, or <c>null</c> when absent.</returns>
		private static string GetQueryValue(string query, string name)
		{
			if (String.IsNullOrEmpty(query))
				return null;

			IEnumerable<string> pairs = query.TrimStart('?').Split('&');
			foreach (string pair in pairs)
			{
				string[] parts = pair.Split(new[] { '=' }, 2);
				if (parts.Length == 0 || !String.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.OrdinalIgnoreCase))
					continue;

				return parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : String.Empty;
			}

			return null;
		}
	}
}
