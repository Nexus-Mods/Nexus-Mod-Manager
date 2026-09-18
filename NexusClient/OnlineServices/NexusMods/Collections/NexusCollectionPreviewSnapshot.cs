using System;
using Nexus.Client.CollectionManagement;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Immutable read-only preview state for one incoming Nexus Collection revision.
	/// </summary>
	/// <remarks>
	/// This is an in-process C2 presentation snapshot only. It is not durable Collection storage, an installation plan,
	/// an applied-state record, or permission to mutate the game. C4 owns persistent feature state and operation journaling.
	/// </remarks>
	public sealed class NexusCollectionPreviewSnapshot
	{
		internal NexusCollectionPreviewSnapshot(
			NexusCollectionNxmLink link,
			NexusCollectionRevisionLookupResult revisionLookup,
			NexusCollectionSummaryLookupResult summaryLookup,
			CollectionDefinition definition,
			CollectionRevision revision,
			NexusCollectionBundleImportResult bundleImport,
			Exception revisionError,
			Exception summaryError,
			string metadataWarning)
		{
			Link = link;
			RevisionLookup = revisionLookup;
			SummaryLookup = summaryLookup;
			Definition = definition;
			Revision = revision;
			BundleImport = bundleImport;
			RevisionError = revisionError;
			SummaryError = summaryError;
			MetadataWarning = metadataWarning;
		}

		public NexusCollectionNxmLink Link { get; }
		public NexusCollectionRevisionLookupResult RevisionLookup { get; }
		public NexusCollectionSummaryLookupResult SummaryLookup { get; }
		public CollectionDefinition Definition { get; }
		public CollectionRevision Revision { get; }
		public NexusCollectionBundleImportResult BundleImport { get; }
		public Exception RevisionError { get; }
		public Exception SummaryError { get; }
		public string MetadataWarning { get; }

		/// <summary>
		/// Gets whether stable provider collection/revision identity has been promoted to the C1 domain.
		/// </summary>
		public bool HasConcreteRevision
		{
			get { return Definition != null && Revision != null; }
		}

		/// <summary>
		/// Gets whether exact collection.json bytes have been imported and normalized for this revision.
		/// </summary>
		public bool HasManifestPreview
		{
			get { return BundleImport != null; }
		}

		/// <summary>
		/// Gets the current capability report when a manifest has been imported.
		/// </summary>
		public CollectionCapabilityReport CapabilityReport
		{
			get { return BundleImport == null ? null : BundleImport.CapabilityReport; }
		}

		internal NexusCollectionPreviewSnapshot WithBundleImport(NexusCollectionBundleImportResult bundleImport)
		{
			if (bundleImport == null)
				throw new ArgumentNullException(nameof(bundleImport));
			if (Revision == null || !bundleImport.Manifest.Revision.Equals(Revision.Identity))
				throw new ArgumentException("The imported manifest must belong to the previewed concrete revision.", nameof(bundleImport));

			return new NexusCollectionPreviewSnapshot(
				Link,
				RevisionLookup,
				SummaryLookup,
				Definition,
				Revision,
				bundleImport,
				RevisionError,
				SummaryError,
				MetadataWarning);
		}
	}
}
