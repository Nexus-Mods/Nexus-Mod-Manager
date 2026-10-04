using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>Reloads stable user-mediated acquisition hints from the retained Vortex manifest without making them artifact identity.</summary>
	public sealed class NexusCollectionManualAcquisitionHintProvider : ICollectionManualAcquisitionHintProvider
	{
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly NexusCollectionManifestNormalizer _normalizer;

		public NexusCollectionManualAcquisitionHintProvider(CollectionsCatalogStore catalogStore,
			CollectionsRevisionSourceStore revisionSourceStore)
		{
			_catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
			_revisionSourceStore = revisionSourceStore ?? throw new ArgumentNullException(nameof(revisionSourceStore));
			_normalizer = new NexusCollectionManifestNormalizer();
		}

		public CollectionManualAcquisitionHint GetHint(CollectionAcquisitionRequest request)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			string md5; long byteLength;
			if (!CollectionExternalArtifactIdentity.TryParse(request.SelectedArtifact, out md5, out byteLength))
				return null;

			CollectionRevision revision = _catalogStore.GetRevision(request.Revision);
			CollectionRevisionSourceRecord source = _revisionSourceStore.GetSource(request.Revision);
			if (revision == null || source == null)
				throw new InvalidOperationException("The retained Collection revision source is unavailable for manual acquisition.");
			byte[] raw = _revisionSourceStore.LoadManifest(request.Revision, source.ManifestSource);
			NexusCollectionManifestNormalizationResult normalized = _normalizer.Normalize(raw, revision);
			NormalizedCollectionMember member = normalized.Manifest.Members.SingleOrDefault(x =>
				x.IdentityResolution.IsResolved && x.IdentityResolution.Key.Equals(request.MemberKey));
			if (member == null || member.Artifact == null || !member.Artifact.Equals(request.SelectedArtifact))
				throw new InvalidOperationException("The retained Collection manifest no longer reproduces the pending external artifact.");

			string json = Encoding.UTF8.GetString(raw).TrimStart('﻿');
			JObject root = JObject.Parse(json);
			JArray mods = root["mods"] as JArray;
			JObject rawMember = mods != null && member.SourceOrdinal < mods.Count ? mods[member.SourceOrdinal] as JObject : null;
			JObject rawSource = rawMember == null ? null : rawMember["source"] as JObject;
			if (rawSource == null)
				throw new InvalidOperationException("The retained Collection member no longer has its source descriptor.");

			string type = (string)rawSource["type"];
			if (!StringComparer.Ordinal.Equals(type, "manual") && !StringComparer.Ordinal.Equals(type, "browse"))
				throw new InvalidOperationException("The retained external artifact no longer uses a characterized manual/browse source.");
			string url = (string)rawSource["url"];
			Uri browserUri = null;
			if (!String.IsNullOrWhiteSpace(url))
			{
				Uri parsed;
				if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) ||
					(!StringComparer.OrdinalIgnoreCase.Equals(parsed.Scheme, Uri.UriSchemeHttp) &&
					 !StringComparer.OrdinalIgnoreCase.Equals(parsed.Scheme, Uri.UriSchemeHttps)))
					throw new InvalidOperationException("The retained manual/browse source URL is no longer a safe HTTP(S) page.");
				browserUri = parsed;
			}
			if (StringComparer.Ordinal.Equals(type, "browse") && browserUri == null)
				throw new InvalidOperationException("A characterized browse source requires its retained HTTP(S) page.");
			return new CollectionManualAcquisitionHint(browserUri, (string)rawSource["instructions"]);
		}
	}
}
