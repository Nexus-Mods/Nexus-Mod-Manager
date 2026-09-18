using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Describes how an incoming path should be handled by the legacy mod/file pipeline or the Collections feature.
	/// </summary>
	public enum NexusNxmLinkDisposition
	{
		/// <summary>
		/// The input is not a Collection NXM link and must retain the pre-Collections handling path.
		/// </summary>
		LegacyModOrFile = 0,

		/// <summary>
		/// The input is a valid canonical Collection NXM link.
		/// </summary>
		Collection = 1,

		/// <summary>
		/// The input declares a Collection NXM path but is malformed and must not fall through as a mod download.
		/// </summary>
		InvalidCollection = 2
	}

	/// <summary>
	/// Represents the canonical Nexus Mods NXM locator for one Collection revision selector.
	/// </summary>
	/// <remarks>
	/// The public collection slug is a lookup locator only. A selector of <c>latest</c> remains an explicit request until
	/// <see cref="INexusCollectionsProvider.GetRevisionAsync"/> resolves it once to a concrete provider revision.
	/// </remarks>
	public sealed class NexusCollectionNxmLink
	{
		internal NexusCollectionNxmLink(
			Uri sourceUri,
			string gameDomain,
			string collectionSlug,
			NexusCollectionRevisionRequest revisionRequest)
		{
			SourceUri = sourceUri ?? throw new ArgumentNullException(nameof(sourceUri));
			GameDomain = gameDomain ?? throw new ArgumentNullException(nameof(gameDomain));
			CollectionSlug = collectionSlug ?? throw new ArgumentNullException(nameof(collectionSlug));
			RevisionRequest = revisionRequest ?? throw new ArgumentNullException(nameof(revisionRequest));
		}

		/// <summary>
		/// Gets a sanitized absolute NXM URI with transient query/fragment/user-info material removed.
		/// </summary>
		public Uri SourceUri { get; }

		/// <summary>
		/// Gets the Nexus game domain encoded in the URI host.
		/// </summary>
		public string GameDomain { get; }

		/// <summary>
		/// Gets the public Collection slug used to locate provider metadata.
		/// </summary>
		public string CollectionSlug { get; }

		/// <summary>
		/// Gets the typed latest/concrete revision request without a magic latest sentinel.
		/// </summary>
		public NexusCollectionRevisionRequest RevisionRequest { get; }
	}

	/// <summary>
	/// Classifies incoming values without changing the legacy mod/file NXM parser.
	/// </summary>
	/// <remarks>
	/// The canonical Collection form is <c>nxm://game/collections/slug/revisions/number-or-latest</c>, matching the ASCII word-character slug form used by the
	/// pinned Nexus/Vortex NXM contract. Inputs that merely look like Collection paths are rejected separately so they cannot
	/// accidentally enter <c>ModManager.AddMod</c> and be interpreted as ordinary mod downloads.
	/// </remarks>
	public static class NexusCollectionNxmLinkParser
	{
		private static readonly Regex CollectionSlugPattern = new Regex(
			@"^[A-Za-z0-9_]+$",
			RegexOptions.CultureInvariant | RegexOptions.Compiled);

		/// <summary>
		/// Classifies an incoming path and parses Collection NXM identity when applicable.
		/// </summary>
		/// <param name="value">The path or URI supplied to the application.</param>
		/// <param name="collectionLink">The parsed Collection link when the disposition is <see cref="NexusNxmLinkDisposition.Collection"/>.</param>
		/// <returns>The safe dispatch disposition.</returns>
		public static NexusNxmLinkDisposition Classify(string value, out NexusCollectionNxmLink collectionLink)
		{
			collectionLink = null;

			if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
				return NexusNxmLinkDisposition.LegacyModOrFile;
			if (!StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, "nxm"))
				return NexusNxmLinkDisposition.LegacyModOrFile;

			string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
			if (segments.Length == 0 || !StringComparer.OrdinalIgnoreCase.Equals(Uri.UnescapeDataString(segments[0]), "collections"))
				return NexusNxmLinkDisposition.LegacyModOrFile;

			if (string.IsNullOrWhiteSpace(uri.Host) || segments.Length != 4)
				return NexusNxmLinkDisposition.InvalidCollection;

			string collectionSlug = Uri.UnescapeDataString(segments[1]);
			string revisionsSegment = Uri.UnescapeDataString(segments[2]);
			string revisionSelector = Uri.UnescapeDataString(segments[3]);

			if (!StringComparer.OrdinalIgnoreCase.Equals(revisionsSegment, "revisions") ||
				string.IsNullOrWhiteSpace(collectionSlug) ||
				!CollectionSlugPattern.IsMatch(collectionSlug))
			{
				return NexusNxmLinkDisposition.InvalidCollection;
			}

			// Vortex historically interpreted short numeric collection tokens as provider IDs paired with revision IDs.
			// NMM's owned provider contract resolves current slug/revision-number links only, so do not silently reinterpret
			// that legacy shape as a slug and request a completely different revision number.
			if (collectionSlug.Length < 6 && Int64.TryParse(collectionSlug, NumberStyles.None, CultureInfo.InvariantCulture, out long legacyCollectionId) && legacyCollectionId > 0)
				return NexusNxmLinkDisposition.InvalidCollection;

			NexusCollectionRevisionRequest request;
			if (StringComparer.OrdinalIgnoreCase.Equals(revisionSelector, "latest"))
			{
				request = NexusCollectionRevisionRequest.Latest(collectionSlug);
			}
			else
			{
				if (!Int32.TryParse(revisionSelector, NumberStyles.None, CultureInfo.InvariantCulture, out int revisionNumber) || revisionNumber <= 0)
					return NexusNxmLinkDisposition.InvalidCollection;

				request = NexusCollectionRevisionRequest.Concrete(collectionSlug, revisionNumber);
			}

			var sanitizedSource = new UriBuilder(uri)
			{
				Query = string.Empty,
				Fragment = string.Empty,
				UserName = string.Empty,
				Password = string.Empty
			}.Uri;

			collectionLink = new NexusCollectionNxmLink(
				sanitizedSource,
				uri.Host.ToLowerInvariant(),
				collectionSlug,
				request);
			return NexusNxmLinkDisposition.Collection;
		}
	}
}
