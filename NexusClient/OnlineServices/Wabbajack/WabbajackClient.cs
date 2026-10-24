using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Nexus.Client.OnlineServices.Wabbajack
{
	/// <summary>Reads Wabbajack's public gallery feeds and downloads authored list packages.</summary>
	public sealed class WabbajackClient : IDisposable
	{
		private const string BaseUrl = "https://raw.githubusercontent.com/wabbajack-tools/mod-lists/master/";
		private readonly HttpClient _http;
		private readonly SemaphoreSlim _requests = new SemaphoreSlim(6);
		internal static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
		{
			TypeNameHandling = TypeNameHandling.None,
			MetadataPropertyHandling = MetadataPropertyHandling.Ignore
		};

		/// <summary>Creates an unauthenticated client; Nexus credentials are not sent to gallery or package hosts.</summary>
		public WabbajackClient() : this(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { }

		/// <summary>Creates a client with a supplied HTTP handler.</summary>
		public WabbajackClient(HttpMessageHandler handler)
		{
			_http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
			_http.DefaultRequestHeaders.UserAgent.ParseAdd("NMM-Community-Edition");
		}

		/// <summary>Loads the same repositories, featured lists, tags, availability reports and mod index as Wabbajack.</summary>
		public async Task<WabbajackCatalogue> LoadAsync(CancellationToken cancellationToken)
		{
			Task<Dictionary<string, string>> repositoriesTask = ReadAsync<Dictionary<string, string>>(BaseUrl + "repositories.json", cancellationToken);
			Task<List<string>> featuredTask = ReadOptionalAsync(BaseUrl + "featured_lists.json", new List<string>(), cancellationToken);
			Task<List<string>> tagsTask = ReadOptionalAsync(BaseUrl + "allowed_tags.json", new List<string>(), cancellationToken);
			Task<Dictionary<string, string>> mappingsTask = ReadOptionalAsync(BaseUrl + "tag_mappings.json", new Dictionary<string, string>(), cancellationToken);
			Task<List<WabbajackListStatus>> statusesTask = ReadOptionalAsync(BaseUrl + "reports/modListSummary.json", new List<WabbajackListStatus>(), cancellationToken);
			Task<WabbajackSearchIndex> searchTask = ReadOptionalAsync(BaseUrl + "reports/searchIndex.json", new WabbajackSearchIndex(), cancellationToken);
			await Task.WhenAll(repositoriesTask, featuredTask, tagsTask, mappingsTask, statusesTask, searchTask).ConfigureAwait(false);

			Dictionary<string, string> repositories = await repositoriesTask.ConfigureAwait(false);
			HashSet<string> featured = new HashSet<string>(await featuredTask.ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);
			HashSet<string> allowedTags = new HashSet<string>(await tagsTask.ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);
			Dictionary<string, string> mappings = await mappingsTask.ConfigureAwait(false);
			Dictionary<string, WabbajackListStatus> statuses = (await statusesTask.ConfigureAwait(false))
				.Where(status => !string.IsNullOrEmpty(status.MachineUrl)).GroupBy(status => status.MachineUrl, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
			WabbajackCatalogue catalogue = new WabbajackCatalogue { SearchIndex = await searchTask.ConfigureAwait(false) };
			IEnumerable<WabbajackList>[] lists = await Task.WhenAll(repositories.Select(async repository =>
			{
				try
				{
					List<WabbajackList> entries = await ReadAsync<List<WabbajackList>>(repository.Value, cancellationToken).ConfigureAwait(false);
					foreach (WabbajackList list in entries.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Title) &&
						item.Links != null && !string.IsNullOrWhiteSpace(item.Links.MachineUrl)))
					{
						list.Repository = repository.Key;
						list.Official = repository.Key == "wj-featured" || featured.Contains(list.Id);
						WabbajackListStatus status;
						if (statuses.TryGetValue(list.Id, out status)) list.Status = status;
						list.Tags = (list.Tags ?? new List<string>()).Where(tag => tag != null)
							.Select(tag => mappings.ContainsKey(tag) ? mappings[tag] : tag)
							.Where(tag => allowedTags.Count == 0 || allowedTags.Contains(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
						if (list.Official) list.Tags.Add("Featured");
						if (list.Nsfw) list.Tags.Add("NSFW");
						if (list.IsUnavailable) list.Tags.Add("Unavailable");
					}
					return entries.Where(item => item != null && item.Repository != null);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
				catch (Exception error)
				{
					Trace.TraceWarning("Wabbajack repository {0} could not be loaded: {1}", repository.Key, error.Message);
					lock (catalogue) catalogue.FailedRepositories++;
					return Enumerable.Empty<WabbajackList>();
				}
			})).ConfigureAwait(false);
			catalogue.Lists.AddRange(lists.SelectMany(items => items).GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()));
			return catalogue;
		}

		/// <summary>Reads a gallery image using the same bounded request queue as the metadata feeds.</summary>
		public async Task<byte[]> ReadImageAsync(string url, CancellationToken cancellationToken)
		{
			await _requests.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					timeout.CancelAfter(TimeSpan.FromSeconds(30));
					using (HttpResponseMessage response = await _http.GetAsync(url, timeout.Token).ConfigureAwait(false))
					{
						response.EnsureSuccessStatusCode();
						return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
					}
				}
			}
			finally { _requests.Release(); }
		}

		/// <summary>Streams a package to the chosen file and publishes it only after the download completes.</summary>
		public async Task DownloadAsync(WabbajackList list, string destination, IProgress<int> progress, CancellationToken cancellationToken)
		{
			string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
			try
			{
				using (HttpResponseMessage response = await _http.GetAsync(list.Links.Download, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
				{
					response.EnsureSuccessStatusCode();
					long? length = response.Content.Headers.ContentLength;
					using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
					using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
					{
						byte[] buffer = new byte[81920];
						long received = 0;
						int lastProgress = -1;
						int count;
						while ((count = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
						{
							await output.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
							received += count;
							int percent = length > 0 ? (int)Math.Min(100, received * 100 / length.Value) : -1;
							if (percent != lastProgress) { progress?.Report(percent); lastProgress = percent; }
						}
					}
				}
				cancellationToken.ThrowIfCancellationRequested();
				if (File.Exists(destination)) File.Replace(temporary, destination, null);
				else File.Move(temporary, destination);
			}
			finally { if (File.Exists(temporary)) File.Delete(temporary); }
		}

		/// <summary>Reads one feed, allowing ordinary redirects used by repository and download hosts.</summary>
		private async Task<T> ReadAsync<T>(string url, CancellationToken cancellationToken) where T : class
		{
			await _requests.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					timeout.CancelAfter(TimeSpan.FromSeconds(30));
					using (HttpResponseMessage response = await _http.GetAsync(url, timeout.Token).ConfigureAwait(false))
					{
						response.EnsureSuccessStatusCode();
						return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync().ConfigureAwait(false), JsonSettings)
							?? throw new JsonSerializationException("The Wabbajack feed is empty.");
					}
				}
			}
			finally { _requests.Release(); }
		}

		/// <summary>Keeps optional gallery filters usable when an auxiliary upstream feed is temporarily unavailable.</summary>
		private async Task<T> ReadOptionalAsync<T>(string url, T fallback, CancellationToken cancellationToken) where T : class
		{
			try { return await ReadAsync<T>(url, cancellationToken).ConfigureAwait(false) ?? fallback; }
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
			catch (Exception error) when (error is HttpRequestException || error is JsonException || error is OperationCanceledException)
			{
				Trace.TraceWarning("Wabbajack optional feed {0} could not be loaded: {1}", url, error.Message);
				return fallback;
			}
		}

		/// <summary>Releases the owned HTTP client; queued requests are cancelled by the owning gallery.</summary>
		public void Dispose() { _http.Dispose(); }
	}
}
