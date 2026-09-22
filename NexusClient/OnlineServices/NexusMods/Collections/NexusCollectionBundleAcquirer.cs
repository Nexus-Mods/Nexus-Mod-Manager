using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Represents one completed remote Collection-bundle acquisition after exact import and durable revision-source retention.
	/// </summary>
	/// <remarks>
	/// Ephemeral signed download locations and staging paths are deliberately not exposed or persisted by this result.
	/// </remarks>
	public sealed class NexusCollectionBundleAcquisitionResult
	{
		/// <summary>
		/// Creates one completed acquisition result from the verified import and retained revision source.
		/// </summary>
		public NexusCollectionBundleAcquisitionResult(NexusCollectionBundleImportResult bundleImport,
			CollectionRevisionSourceRecord revisionSource)
		{
			BundleImport = bundleImport ?? throw new ArgumentNullException(nameof(bundleImport));
			RevisionSource = revisionSource ?? throw new ArgumentNullException(nameof(revisionSource));
			CollectionRevisionSourceInputKind expectedInputKind;
			switch (BundleImport.InputKind)
			{
				case NexusCollectionBundleInputKind.RawManifest:
					expectedInputKind = CollectionRevisionSourceInputKind.RawManifest;
					break;
				case NexusCollectionBundleInputKind.Archive:
					expectedInputKind = CollectionRevisionSourceInputKind.Archive;
					break;
				default:
					throw new ArgumentException("The imported Collection bundle has no supported durable source kind.", nameof(bundleImport));
			}

			if (!BundleImport.Manifest.Revision.Equals(RevisionSource.Revision) ||
				RevisionSource.InputKind != expectedInputKind ||
				!BundleImport.BundleContentHash.Equals(RevisionSource.BundleContentHash) ||
				BundleImport.BundleByteLength != RevisionSource.BundleByteLength ||
				!StringComparer.Ordinal.Equals(BundleImport.ManifestEntryName, RevisionSource.ManifestEntryName) ||
				!BundleImport.Manifest.Source.Equals(RevisionSource.ManifestSource) ||
				String.IsNullOrEmpty(RevisionSource.RawManifestArtifactId) ||
				String.IsNullOrEmpty(RevisionSource.RawBundleArtifactId))
				throw new ArgumentException("The imported bundle and retained source must describe one fully retained immutable Collection revision.", nameof(revisionSource));
		}

		/// <summary>Gets the exact bounded bundle import/normalization result.</summary>
		public NexusCollectionBundleImportResult BundleImport { get; }

		/// <summary>Gets the durable retained source/provenance record for the immutable revision.</summary>
		public CollectionRevisionSourceRecord RevisionSource { get; }
	}

	/// <summary>
	/// Resolves, downloads, imports and durably retains one concrete Nexus Collection revision bundle without native mutation.
	/// </summary>
	/// <remarks>
	/// Nexus API authorization remains on <see cref="INexusCollectionsProvider"/>. Final content download uses a separate
	/// credential-free HTTP client with redirects disabled, bounded staging and no durable signed-URL identity.
	/// </remarks>
	public sealed class NexusCollectionBundleAcquirer : IDisposable
	{
		/// <summary>
		/// Gets the default defensive outer-bundle acquisition ceiling used until real bundle fixtures justify a different bound.
		/// </summary>
		public const long DefaultMaximumBundleBytes = 512L * 1024L * 1024L;

		private const int CopyBufferSize = 81920;
		private const string StagingDirectoryName = ".bundle-staging";
		private const string GenericBundleFileName = "bundle.download";

		private readonly INexusCollectionsProvider _provider;
		private readonly CollectionsStore _store;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly NexusCollectionBundleImporter _bundleImporter;
		private readonly HttpClient _httpClient;
		private readonly long _maximumBundleBytes;
		private bool _disposed;

		/// <summary>
		/// Creates a bundle acquirer using a credential-free HTTPS content client and the default bounded bundle ceiling.
		/// </summary>
		public NexusCollectionBundleAcquirer(INexusCollectionsProvider provider, CollectionsStore store)
			: this(provider, store, CreateDefaultHandler(), DefaultMaximumBundleBytes)
		{
		}

		/// <summary>
		/// Creates a bundle acquirer with a caller-supplied content handler and explicit byte ceiling, primarily for deterministic tests.
		/// </summary>
		/// <remarks>The acquirer owns and disposes the supplied handler through its private <see cref="HttpClient"/>.</remarks>
		public NexusCollectionBundleAcquirer(INexusCollectionsProvider provider, CollectionsStore store,
			HttpMessageHandler contentHandler, long maximumBundleBytes)
		{
			_provider = provider ?? throw new ArgumentNullException(nameof(provider));
			_store = store ?? throw new ArgumentNullException(nameof(store));
			if (contentHandler == null)
				throw new ArgumentNullException(nameof(contentHandler));
			if (maximumBundleBytes <= 0)
				throw new ArgumentOutOfRangeException(nameof(maximumBundleBytes), "The Collection bundle byte ceiling must be positive.");

			_revisionSourceStore = new CollectionsRevisionSourceStore(store);
			_bundleImporter = new NexusCollectionBundleImporter();
			_httpClient = new HttpClient(contentHandler, true)
			{
				Timeout = Timeout.InfiniteTimeSpan
			};
			_maximumBundleBytes = maximumBundleBytes;
		}

		/// <summary>
		/// Acquires the authorized bundle for one exact provider revision, imports it through the existing C2 importer and
		/// durably retains both the exact manifest source and downloaded outer bundle through the C4 retained-content stores.
		/// </summary>
		public async Task<NexusCollectionBundleAcquisitionResult> AcquireAsync(NexusCollectionRevisionMetadata providerRevision,
			CollectionRevision revision, CancellationToken cancellationToken = default(CancellationToken))
		{
			ThrowIfDisposed();
			ValidateRequestedRevision(providerRevision, revision);

			// Fail before network work when the immutable revision is not present in the authoritative Collections catalog.
			_revisionSourceStore.GetSource(revision.Identity);

			NexusCollectionBundleResolutionResult resolution = await _provider.ResolveBundleAsync(providerRevision, cancellationToken)
				.ConfigureAwait(false);
			ValidateResolution(resolution, revision);

			NexusCollectionBundleDownloadLocation location = resolution.DownloadLocations[0];
			Uri contentUri = ValidateContentUri(location == null ? null : location.Uri);
			string stagingDirectory = CreateStagingDirectory();
			string stagingPath = Path.Combine(stagingDirectory, GetStagingFileName(contentUri));

			try
			{
				await DownloadToStagingAsync(contentUri, stagingPath, cancellationToken).ConfigureAwait(false);
				NexusCollectionBundleImportResult bundleImport = _bundleImporter.ImportFile(stagingPath, revision);
				if (!bundleImport.Manifest.Revision.Equals(revision.Identity))
					throw new InvalidDataException("The acquired Collection bundle normalized to a different immutable revision identity.");

				CollectionRevisionSourceInputKind inputKind = ToRetainedInputKind(bundleImport.InputKind);
				CollectionRevisionSourceRecord retained = _revisionSourceStore.RetainManifest(bundleImport.Manifest,
					inputKind, bundleImport.BundleContentHash, bundleImport.BundleByteLength, bundleImport.ManifestEntryName,
					bundleImport.Normalization.GetRawManifestBytes());

				using (FileStream bundleStream = new FileStream(stagingPath, FileMode.Open, FileAccess.Read, FileShare.Read,
					CopyBufferSize, FileOptions.SequentialScan))
				{
					retained = _revisionSourceStore.RetainBundle(revision.Identity, bundleStream, cancellationToken);
				}

				return new NexusCollectionBundleAcquisitionResult(bundleImport, retained);
			}
			finally
			{
				DeleteStagingDirectory(stagingDirectory);
			}
		}

		/// <summary>Releases the isolated credential-free content HTTP client.</summary>
		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			_httpClient.Dispose();
		}

		private async Task DownloadToStagingAsync(Uri contentUri, string stagingPath, CancellationToken cancellationToken)
		{
			using (var request = new HttpRequestMessage(HttpMethod.Get, contentUri))
			using (HttpResponseMessage response = await SendContentRequestAsync(request, cancellationToken).ConfigureAwait(false))
			{
				if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
					throw new IOException("The Collection bundle content location redirected; resolve a new validated content location before continuing.");
				if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
					throw new IOException("The Collection bundle content authorization was rejected or has expired.");
				if (!response.IsSuccessStatusCode)
					throw new IOException("The Collection bundle content host returned HTTP status " + ((int)response.StatusCode) + ".");
				if (response.Content == null)
					throw new InvalidDataException("The Collection bundle content response has no body.");

				long? declaredLength = response.Content.Headers.ContentLength;
				if (declaredLength.HasValue && declaredLength.Value > _maximumBundleBytes)
					throw new InvalidDataException("The Collection bundle exceeds the configured bounded download size.");

				long copied = 0;
				using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
				using (var output = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
					CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
				{
					byte[] buffer = new byte[CopyBufferSize];
					while (true)
					{
						cancellationToken.ThrowIfCancellationRequested();
						int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
						if (read <= 0)
							break;
						if (copied > _maximumBundleBytes - read)
							throw new InvalidDataException("The Collection bundle exceeded the configured bounded download size while streaming.");
						await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
						copied += read;
					}
					await output.FlushAsync(cancellationToken).ConfigureAwait(false);
				}

				if (declaredLength.HasValue && copied != declaredLength.Value)
					throw new EndOfStreamException("The Collection bundle content ended before its declared byte length.");
			}
		}

		private async Task<HttpResponseMessage> SendContentRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			try
			{
				return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (HttpRequestException)
			{
				throw new IOException("The Collection bundle content download failed before completion.");
			}
		}

		private static void ValidateRequestedRevision(NexusCollectionRevisionMetadata providerRevision, CollectionRevision revision)
		{
			if (providerRevision == null)
				throw new ArgumentNullException(nameof(providerRevision));
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (!providerRevision.HasStableIdentity)
				throw new ArgumentException("A concrete Nexus Collection revision is required before bundle acquisition.", nameof(providerRevision));
			if (!revision.IsRemoteBaseline)
				throw new ArgumentException("A Nexus bundle can only be acquired for an immutable Nexus Collection revision.", nameof(revision));

			CollectionDefinition definition = NexusCollectionDomainMapper.ToDefinition(providerRevision);
			CollectionRevision mapped = definition == null ? null : NexusCollectionDomainMapper.ToRevision(definition, providerRevision);
			if (mapped == null || !mapped.Identity.Equals(revision.Identity))
				throw new InvalidOperationException("The provider revision identity does not match the requested persisted Collection revision.");
		}

		private static void ValidateResolution(NexusCollectionBundleResolutionResult resolution, CollectionRevision revision)
		{
			if (resolution == null)
				throw new InvalidDataException("Nexus returned no Collection bundle authorization result.");
			if (resolution.Revision == null || !resolution.Revision.HasStableIdentity)
				throw new InvalidDataException("Nexus returned Collection bundle authorization without a concrete revision identity.");

			CollectionDefinition definition = NexusCollectionDomainMapper.ToDefinition(resolution.Revision);
			CollectionRevision mapped = definition == null ? null : NexusCollectionDomainMapper.ToRevision(definition, resolution.Revision);
			if (mapped == null || !mapped.Identity.Equals(revision.Identity))
				throw new InvalidDataException("Nexus returned Collection bundle authorization for a different immutable revision.");
			if (!resolution.HasDownloadLocations)
				throw new InvalidDataException("Nexus returned no usable Collection bundle content location.");
		}

		private static Uri ValidateContentUri(Uri uri)
		{
			if (uri == null || !uri.IsAbsoluteUri || !StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, Uri.UriSchemeHttps))
				throw new InvalidDataException("A Collection bundle content location must be an absolute HTTPS URI.");
			if (!uri.IsDefaultPort)
				throw new InvalidDataException("A Collection bundle content location must use the default HTTPS port.");
			if (!String.IsNullOrEmpty(uri.UserInfo))
				throw new InvalidDataException("A Collection bundle content location must not contain URI user information.");
			if (!String.IsNullOrEmpty(uri.Fragment))
				throw new InvalidDataException("A Collection bundle content location must not contain a URI fragment.");
			if (Uri.IsWellFormedUriString(uri.AbsoluteUri, UriKind.Absolute) == false || String.IsNullOrWhiteSpace(uri.Host))
				throw new InvalidDataException("A Collection bundle content location is not a valid absolute URI.");
			if (uri.IsLoopback || StringComparer.OrdinalIgnoreCase.Equals(uri.Host, "localhost") ||
				uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("A Collection bundle content location must not target the local machine.");

			IPAddress literalAddress;
			if (IPAddress.TryParse(uri.DnsSafeHost, out literalAddress) && IsDisallowedLiteralAddress(literalAddress))
				throw new InvalidDataException("A Collection bundle content location must not target a private or reserved network address.");

			return uri;
		}

		private static bool IsDisallowedLiteralAddress(IPAddress address)
		{
			if (address == null || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
				return true;

			if (address.AddressFamily == AddressFamily.InterNetworkV6)
			{
				if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
					return true;
				if (address.IsIPv4MappedToIPv6)
					return IsDisallowedLiteralAddress(address.MapToIPv4());

				byte[] ipv6 = address.GetAddressBytes();
				if ((ipv6[0] & 0xfe) == 0xfc)
					return true;
				if (ipv6.Length >= 4 && ipv6[0] == 0x20 && ipv6[1] == 0x01 && ipv6[2] == 0x0d && ipv6[3] == 0xb8)
					return true;
				return false;
			}

			if (address.AddressFamily != AddressFamily.InterNetwork)
				return true;

			byte[] bytes = address.GetAddressBytes();
			return bytes[0] == 0 ||
				bytes[0] == 10 ||
				bytes[0] == 127 ||
				(bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
				(bytes[0] == 169 && bytes[1] == 254) ||
				(bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
				(bytes[0] == 192 && bytes[1] == 0 && (bytes[2] == 0 || bytes[2] == 2)) ||
				(bytes[0] == 192 && bytes[1] == 168) ||
				(bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19 || (bytes[1] == 51 && bytes[2] == 100))) ||
				(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) ||
				bytes[0] >= 224;
		}

		private static CollectionRevisionSourceInputKind ToRetainedInputKind(NexusCollectionBundleInputKind inputKind)
		{
			switch (inputKind)
			{
				case NexusCollectionBundleInputKind.RawManifest:
					return CollectionRevisionSourceInputKind.RawManifest;
				case NexusCollectionBundleInputKind.Archive:
					return CollectionRevisionSourceInputKind.Archive;
				default:
					throw new InvalidDataException("The acquired Collection bundle has an unsupported source kind.");
			}
		}

		private string CreateStagingDirectory()
		{
			_store.OpenExisting();
			string root = Path.Combine(_store.StoreDirectory, StagingDirectoryName);
			Directory.CreateDirectory(root);
			string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			return directory;
		}

		private static string GetStagingFileName(Uri uri)
		{
			string path = uri == null ? null : uri.AbsolutePath;
			if (String.IsNullOrEmpty(path))
				return GenericBundleFileName;
			int separator = path.LastIndexOf('/');
			string fileName = separator < 0 ? path : path.Substring(separator + 1);
			if (StringComparer.OrdinalIgnoreCase.Equals(fileName, NexusCollectionBundleImporter.ManifestFileName))
				return NexusCollectionBundleImporter.ManifestFileName;

			string extension = Path.GetExtension(fileName);
			if (String.IsNullOrEmpty(extension) || extension.Length > 16)
				return GenericBundleFileName;
			for (int index = 1; index < extension.Length; index++)
			{
				if (!Char.IsLetterOrDigit(extension[index]))
					return GenericBundleFileName;
			}
			return "bundle" + extension.ToLowerInvariant();
		}

		private static void DeleteStagingDirectory(string path)
		{
			if (String.IsNullOrEmpty(path) || !Directory.Exists(path))
				return;
			try
			{
				Directory.Delete(path, true);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}

		private static HttpMessageHandler CreateDefaultHandler()
		{
			return new HttpClientHandler
			{
				AllowAutoRedirect = false,
				UseCookies = false,
				AutomaticDecompression = DecompressionMethods.None
			};
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(NexusCollectionBundleAcquirer));
		}
	}
}
