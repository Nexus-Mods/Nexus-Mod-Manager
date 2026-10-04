using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Stable direct-download metadata reloaded from retained Collection source; never part of artifact identity.</summary>
	public sealed class CollectionDirectAcquisitionSource
	{
		public CollectionDirectAcquisitionSource(Uri downloadUri)
		{
			ValidateDownloadUri(downloadUri, nameof(downloadUri));
			DownloadUri = downloadUri;
		}

		public Uri DownloadUri { get; }

		internal static void ValidateDownloadUri(Uri uri, string parameterName)
		{
			if (uri == null) throw new ArgumentNullException(parameterName);
			if (!uri.IsAbsoluteUri || !StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, Uri.UriSchemeHttps) ||
				String.IsNullOrWhiteSpace(uri.Host) || !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment))
				throw new ArgumentException("A direct Collection source must be an absolute HTTPS URI without embedded credentials or a fragment.", parameterName);
		}
	}

	/// <summary>Resolves exact direct-download metadata from the immutable retained Collection revision source.</summary>
	public interface ICollectionDirectAcquisitionSourceProvider
	{
		CollectionDirectAcquisitionSource GetSource(CollectionAcquisitionRequest request);
	}

	/// <summary>
	/// Runs the characterized Vortex direct-source path without widening the global NMM downloader contract.
	/// </summary>
	public sealed class CollectionDirectAcquisitionCoordinator
	{
		private readonly ICollectionDirectAcquisitionSourceProvider _sourceProvider;
		private readonly CollectionVerifiedArchiveAdopter _archiveAdopter;
		private readonly CollectionsAcquisitionStore _acquisitionStore;

		public CollectionDirectAcquisitionCoordinator(ICollectionDirectAcquisitionSourceProvider sourceProvider,
			CollectionVerifiedArchiveAdopter archiveAdopter, CollectionsAcquisitionStore acquisitionStore)
		{
			_sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
			_archiveAdopter = archiveAdopter ?? throw new ArgumentNullException(nameof(archiveAdopter));
			_acquisitionStore = acquisitionStore ?? throw new ArgumentNullException(nameof(acquisitionStore));
		}

		/// <summary>Queues automatic direct acquisition when the retained member uses the characterized direct source.</summary>
		/// <returns>A correlation for the Collection-owned producer, or <c>null</c> when the request is not a direct source.</returns>
		public CollectionAcquisitionQueueCorrelation TryQueue(CollectionAcquisitionRequest request)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			CollectionDirectAcquisitionSource source = _sourceProvider.GetSource(request);
			if (source == null)
				return null;

			var task = new CollectionDirectDownloadTask(request, source, _archiveAdopter);
			Guid producerId = Guid.NewGuid();
			_acquisitionStore.TrackQueued(request, producerId, CollectionAcquisitionPersistenceMode.Direct);
			task.TaskEnded += (sender, args) => _acquisitionStore.MarkProducerState(producerId, args.Status);
			var correlation = new CollectionAcquisitionQueueCorrelation(request, producerId, task, CollectionArchiveOverwritePolicy.Prompt);
			task.StartDownload();
			return correlation;
		}
	}

	/// <summary>Collection-owned HTTPS downloader for one exact Vortex direct artifact.</summary>
	internal sealed class CollectionDirectDownloadTask : ThreadedBackgroundTask
	{
		private const int MaximumRedirects = 5;
		private readonly CollectionAcquisitionRequest _request;
		private readonly CollectionDirectAcquisitionSource _source;
		private readonly CollectionVerifiedArchiveAdopter _archiveAdopter;
		private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

		public CollectionDirectDownloadTask(CollectionAcquisitionRequest request, CollectionDirectAcquisitionSource source,
			CollectionVerifiedArchiveAdopter archiveAdopter)
		{
			_request = request ?? throw new ArgumentNullException(nameof(request));
			_source = source ?? throw new ArgumentNullException(nameof(source));
			_archiveAdopter = archiveAdopter ?? throw new ArgumentNullException(nameof(archiveAdopter));
			IsRemote = true;
			ShowItemProgress = false;
		}

		public void StartDownload()
		{
			Start(true);
		}

		public override void Cancel()
		{
			base.Cancel();
			_cancellation.Cancel();
		}

		protected override object DoWork(object[] args)
		{
			string md5;
			long expectedLength;
			if (!CollectionExternalArtifactIdentity.TryParse(_request.SelectedArtifact, out md5, out expectedLength))
			{
				Status = TaskStatus.Error;
				ItemMessage = "The direct Collection request no longer has exact external archive identity.";
				return null;
			}

			OverallMessage = "Downloading Collection archive...";
			OverallProgress = 0;
			OverallProgressMaximum = expectedLength;
			string tempDirectory = Path.Combine(Path.GetTempPath(), "NMM-Collections-Direct");
			Directory.CreateDirectory(tempDirectory);
			string tempPath = Path.Combine(tempDirectory, _request.RequestId.ToString("N") + ".archive");
			try
			{
				if (File.Exists(tempPath)) File.Delete(tempPath);
				DownloadExact(_source.DownloadUri, tempPath, expectedLength, _cancellation.Token);
				_cancellation.Token.ThrowIfCancellationRequested();
				CollectionVerifiedArchive verified = _archiveAdopter.TryAdoptFile(_request, tempPath, _cancellation.Token);
				if (verified == null)
				{
					Status = TaskStatus.Error;
					ItemMessage = "The downloaded Collection archive did not match the retained exact identity.";
					return null;
				}
				return verified;
			}
			catch (OperationCanceledException)
			{
				Status = TaskStatus.Cancelled;
				return null;
			}
			catch (Exception ex)
			{
				Status = TaskStatus.Error;
				ItemMessage = ex.Message;
				return null;
			}
			finally
			{
				try { if (File.Exists(tempPath)) File.Delete(tempPath); }
				catch { }
			}
		}

		private void DownloadExact(Uri initialUri, string targetPath, long expectedLength, CancellationToken cancellationToken)
		{
			using (var handler = new HttpClientHandler
			{
				AllowAutoRedirect = false,
				UseCookies = false,
				UseDefaultCredentials = false,
				Credentials = null,
				PreAuthenticate = false,
				AutomaticDecompression = DecompressionMethods.None
			})
			using (var client = new HttpClient(handler))
			{
				Uri current = initialUri;
				for (int redirects = 0; ; redirects++)
				{
					CollectionDirectAcquisitionSource.ValidateDownloadUri(current, nameof(initialUri));
					using (var request = new HttpRequestMessage(HttpMethod.Get, current))
					using (HttpResponseMessage response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).GetAwaiter().GetResult())
					{
						int status = (int)response.StatusCode;
						if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
						{
							if (redirects >= MaximumRedirects)
								throw new InvalidDataException("The direct Collection source exceeded the HTTPS redirect limit.");
							Uri location = response.Headers.Location;
							if (location == null)
								throw new InvalidDataException("The direct Collection source returned a redirect without a Location header.");
							current = location.IsAbsoluteUri ? location : new Uri(current, location);
							CollectionDirectAcquisitionSource.ValidateDownloadUri(current, "redirectUri");
							continue;
						}

						if (!response.IsSuccessStatusCode)
							throw new InvalidDataException("The direct Collection source returned HTTP status " + status + ".");
						if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != expectedLength)
							throw new InvalidDataException("The direct Collection source returned an unexpected Content-Length.");

						using (Stream input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
						using (var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan))
						{
							var buffer = new byte[128 * 1024];
							long total = 0;
							for (;;)
							{
								cancellationToken.ThrowIfCancellationRequested();
								int read = input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).GetAwaiter().GetResult();
								if (read <= 0) break;
								total += read;
								if (total > expectedLength)
									throw new InvalidDataException("The direct Collection source exceeded the declared archive size.");
								output.Write(buffer, 0, read);
								OverallProgress = total;
							}
							if (total != expectedLength)
								throw new InvalidDataException("The direct Collection source did not produce the declared archive size.");
						}
						return;
					}
				}
			}
		}
	}
}
