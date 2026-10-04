using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>Reloads characterized Vortex direct-download metadata from the immutable retained revision source.</summary>
	public sealed class NexusCollectionDirectAcquisitionSourceProvider : ICollectionDirectAcquisitionSourceProvider
	{
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly NexusCollectionManifestNormalizer _normalizer;

		public NexusCollectionDirectAcquisitionSourceProvider(CollectionsCatalogStore catalogStore,
			CollectionsRevisionSourceStore revisionSourceStore)
		{
			_catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
			_revisionSourceStore = revisionSourceStore ?? throw new ArgumentNullException(nameof(revisionSourceStore));
			_normalizer = new NexusCollectionManifestNormalizer();
		}

		public CollectionDirectAcquisitionSource GetSource(CollectionAcquisitionRequest request)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			string md5; long byteLength;
			if (!CollectionExternalArtifactIdentity.TryParse(request.SelectedArtifact, out md5, out byteLength))
				return null;

			CollectionRevision revision = _catalogStore.GetRevision(request.Revision);
			CollectionRevisionSourceRecord source = _revisionSourceStore.GetSource(request.Revision);
			if (revision == null || source == null)
				throw new InvalidOperationException("The retained Collection revision source is unavailable for direct acquisition.");
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
			if (!StringComparer.Ordinal.Equals((string)rawSource["type"], "direct"))
				return null;

			Uri uri;
			if (!Uri.TryCreate((string)rawSource["url"], UriKind.Absolute, out uri))
				throw new InvalidOperationException("The retained direct Collection source URL is no longer valid.");
			return new CollectionDirectAcquisitionSource(uri);
		}
	}
}
