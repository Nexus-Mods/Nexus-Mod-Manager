using System;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Builds read-only C2 preview snapshots from incoming Collection NXM metadata and local/acquired bundle files.
	/// </summary>
	/// <remarks>
	/// The controller performs only provider reads and bounded bundle inspection. It owns no native ModManager reference and
	/// exposes no install/replace operation, keeping the C2 preview boundary incapable of game mutation by construction.
	/// </remarks>
	public sealed class NexusCollectionPreviewController
	{
		private readonly INexusCollectionsProvider _provider;
		private readonly NexusCollectionBundleImporter _bundleImporter;

		public NexusCollectionPreviewController(INexusCollectionsProvider provider)
			: this(provider, new NexusCollectionBundleImporter())
		{
		}

		public NexusCollectionPreviewController(
			INexusCollectionsProvider provider,
			NexusCollectionBundleImporter bundleImporter)
		{
			_provider = provider ?? throw new ArgumentNullException(nameof(provider));
			_bundleImporter = bundleImporter ?? throw new ArgumentNullException(nameof(bundleImporter));
		}

		/// <summary>
		/// Enriches a completed NXM revision lookup with optional collection summary metadata and maps stable identity to C1.
		/// </summary>
		public async Task<NexusCollectionPreviewSnapshot> CreateFromDispatchAsync(
			NexusCollectionNxmDispatchResult dispatch,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (dispatch == null)
				throw new ArgumentNullException(nameof(dispatch));

			NexusCollectionRevisionLookupResult revisionLookup = dispatch.RevisionLookup;
			CollectionDefinition definition = null;
			CollectionRevision revision = null;
			string metadataWarning = null;

			if (revisionLookup != null && revisionLookup.Revision != null)
			{
				definition = NexusCollectionDomainMapper.ToDefinition(revisionLookup.Revision);
				if (definition != null)
					revision = NexusCollectionDomainMapper.ToRevision(definition, revisionLookup.Revision);
			}

			NexusCollectionSummaryLookupResult summaryLookup = null;
			Exception summaryError = null;
			try
			{
				summaryLookup = await _provider
					.GetSummaryAsync(dispatch.Link.CollectionSlug, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				summaryError = ex;
			}

			if (summaryLookup != null && summaryLookup.Collection != null && summaryLookup.Collection.HasStableIdentity)
			{
				CollectionDefinition summaryDefinition = NexusCollectionDomainMapper.ToDefinition(summaryLookup.Collection);
				if (definition == null)
				{
					definition = summaryDefinition;
				}
				else if (!definition.Identity.Equals(summaryDefinition.Identity))
				{
					metadataWarning = "Nexus returned collection summary identity that does not match the concrete revision identity. Decorative summary metadata was ignored.";
				}
				else
				{
					definition = summaryDefinition;
				}

				if (definition != null && revisionLookup != null && revisionLookup.Revision != null &&
					revisionLookup.Revision.HasStableIdentity &&
					definition.Identity.Equals(CollectionIdentity.FromNexus(revisionLookup.Revision.CollectionId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))))
				{
					revision = NexusCollectionDomainMapper.ToRevision(definition, revisionLookup.Revision);
				}
			}

			return new NexusCollectionPreviewSnapshot(
				dispatch.Link,
				revisionLookup,
				summaryLookup,
				definition,
				revision,
				null,
				dispatch.Error,
				summaryError,
				metadataWarning);
		}

		/// <summary>
		/// Imports a local/acquired bundle for the already-resolved concrete revision without changing native state.
		/// </summary>
		public NexusCollectionPreviewSnapshot ImportFile(NexusCollectionPreviewSnapshot preview, string sourcePath)
		{
			if (preview == null)
				throw new ArgumentNullException(nameof(preview));
			if (!preview.HasConcreteRevision)
				throw new InvalidOperationException("A concrete Nexus Collection revision must be resolved before a bundle can be normalized against it.");

			NexusCollectionBundleImportResult import = _bundleImporter.ImportFile(sourcePath, preview.Revision);
			return preview.WithBundleImport(import);
		}
	}
}
