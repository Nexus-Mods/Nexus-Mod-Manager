using System;
using Nexus.Client.CollectionManagement;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Identifies how C2.5 obtained the exact collection.json bytes presented to the normalizer.
	/// </summary>
	public enum NexusCollectionBundleInputKind
	{
		Unknown = 0,
		RawManifest = 1,
		Archive = 2
	}

	/// <summary>
	/// Immutable result of normalizing exact collection.json bytes for one concrete Nexus Collection revision.
	/// </summary>
	/// <remarks>
	/// Raw manifest bytes are kept only as bounded import data here. C4 owns durable retained-blob storage. Signed
	/// download URLs and source filesystem paths are deliberately not part of this result or its identity.
	/// </remarks>
	public sealed class NexusCollectionManifestNormalizationResult
	{
		private readonly byte[] _rawManifestBytes;

		internal NexusCollectionManifestNormalizationResult(
			byte[] rawManifestBytes,
			NormalizedCollectionManifest manifest,
			CollectionCapabilityReport capabilityReport)
		{
			if (rawManifestBytes == null)
				throw new ArgumentNullException(nameof(rawManifestBytes));
			if (manifest == null)
				throw new ArgumentNullException(nameof(manifest));
			if (capabilityReport == null)
				throw new ArgumentNullException(nameof(capabilityReport));
			if (!ReferenceEquals(manifest, capabilityReport.Manifest))
				throw new ArgumentException("The capability report must describe the normalized manifest.", nameof(capabilityReport));

			_rawManifestBytes = (byte[])rawManifestBytes.Clone();
			Manifest = manifest;
			CapabilityReport = capabilityReport;
		}

		/// <summary>
		/// Gets the normalized provider-neutral C1 manifest.
		/// </summary>
		public NormalizedCollectionManifest Manifest { get; }

		/// <summary>
		/// Gets the selected-closure capability report produced while adapting the source manifest.
		/// </summary>
		public CollectionCapabilityReport CapabilityReport { get; }

		/// <summary>
		/// Gets a defensive copy of the exact collection.json bytes which produced <see cref="Manifest"/>.
		/// </summary>
		public byte[] GetRawManifestBytes()
		{
			return (byte[])_rawManifestBytes.Clone();
		}
	}

	/// <summary>
	/// Immutable result of inspecting one local/acquired Nexus Collection bundle without installing anything.
	/// </summary>
	public sealed class NexusCollectionBundleImportResult
	{
		internal NexusCollectionBundleImportResult(
			NexusCollectionBundleInputKind inputKind,
			CollectionContentHash bundleContentHash,
			long bundleByteLength,
			string manifestEntryName,
			NexusCollectionManifestNormalizationResult normalization)
		{
			if (!Enum.IsDefined(typeof(NexusCollectionBundleInputKind), inputKind) || inputKind == NexusCollectionBundleInputKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(inputKind));
			if (bundleContentHash == null)
				throw new ArgumentNullException(nameof(bundleContentHash));
			if (bundleByteLength < 0)
				throw new ArgumentOutOfRangeException(nameof(bundleByteLength));
			if (string.IsNullOrWhiteSpace(manifestEntryName))
				throw new ArgumentException("A manifest entry name is required.", nameof(manifestEntryName));
			if (normalization == null)
				throw new ArgumentNullException(nameof(normalization));

			InputKind = inputKind;
			BundleContentHash = bundleContentHash;
			BundleByteLength = bundleByteLength;
			ManifestEntryName = manifestEntryName;
			Normalization = normalization;
		}

		/// <summary>
		/// Gets whether the source file was collection.json itself or an archive containing it.
		/// </summary>
		public NexusCollectionBundleInputKind InputKind { get; }

		/// <summary>
		/// Gets the SHA-256 identity of the exact outer source file presented to the importer.
		/// </summary>
		/// <remarks>
		/// This is retention/provenance data, not the Collection or revision identity and not an authorization URL.
		/// </remarks>
		public CollectionContentHash BundleContentHash { get; }

		/// <summary>
		/// Gets the exact outer source byte length.
		/// </summary>
		public long BundleByteLength { get; }

		/// <summary>
		/// Gets the archive entry or raw file role which supplied collection.json.
		/// </summary>
		public string ManifestEntryName { get; }

		/// <summary>
		/// Gets the normalized manifest/capability result.
		/// </summary>
		public NexusCollectionManifestNormalizationResult Normalization { get; }

		public NormalizedCollectionManifest Manifest
		{
			get { return Normalization.Manifest; }
		}

		public CollectionCapabilityReport CapabilityReport
		{
			get { return Normalization.CapabilityReport; }
		}
	}
}
