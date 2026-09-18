using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Contains the read-only result of resolving one Collection NXM selector through the owned Nexus provider.
	/// </summary>
	public sealed class NexusCollectionNxmDispatchResult
	{
		internal NexusCollectionNxmDispatchResult(
			NexusCollectionNxmLink link,
			NexusCollectionRevisionLookupResult revisionLookup,
			Exception error)
		{
			Link = link ?? throw new ArgumentNullException(nameof(link));
			RevisionLookup = revisionLookup;
			Error = error;
		}

		/// <summary>
		/// Gets the original parsed Collection NXM link.
		/// </summary>
		public NexusCollectionNxmLink Link { get; }

		/// <summary>
		/// Gets the provider revision result when metadata resolution completed normally.
		/// </summary>
		/// <remarks>
		/// GraphQL field errors remain inside this object and may coexist with useful partial data.
		/// </remarks>
		public NexusCollectionRevisionLookupResult RevisionLookup { get; }

		/// <summary>
		/// Gets the transport/session/provider failure, if resolution could not complete normally.
		/// </summary>
		public Exception Error { get; }

		/// <summary>
		/// Gets whether the provider request itself completed without throwing.
		/// </summary>
		public bool Succeeded
		{
			get { return Error == null; }
		}

		/// <summary>
		/// Gets whether the provider resolved a durable concrete collection/revision identity.
		/// </summary>
		public bool HasConcreteRevision
		{
			get
			{
				return Succeeded &&
					RevisionLookup != null &&
					RevisionLookup.Revision != null &&
					RevisionLookup.Revision.HasStableIdentity;
			}
		}
	}

	/// <summary>
	/// Event arguments for one completed incoming Collection NXM resolution.
	/// </summary>
	public sealed class NexusCollectionNxmDispatchCompletedEventArgs : EventArgs
	{
		internal NexusCollectionNxmDispatchCompletedEventArgs(NexusCollectionNxmDispatchResult result)
		{
			Result = result ?? throw new ArgumentNullException(nameof(result));
		}

		/// <summary>
		/// Gets the completed read-only dispatch result.
		/// </summary>
		public NexusCollectionNxmDispatchResult Result { get; }
	}

	/// <summary>
	/// Routes parsed Collection NXM links into read-only provider resolution without invoking the native mod downloader.
	/// </summary>
	/// <remarks>
	/// Completed results are also retained in a small in-process queue so a startup NXM link is not lost before the
	/// Collections tab subscribes. This is not durable operation persistence; C4 owns crash-safe feature journaling.
	/// </remarks>
	public sealed class NexusCollectionNxmDispatcher
	{
		private readonly object _syncRoot = new object();
		private const int MaxCompletedResults = 32;
		private readonly INexusCollectionsProvider _provider;
		private readonly Queue<NexusCollectionNxmDispatchResult> _completed = new Queue<NexusCollectionNxmDispatchResult>();
		private readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly SemaphoreSlim _metadataGate = new SemaphoreSlim(2, 2);

		/// <summary>
		/// Raised after a read-only Collection NXM resolution has been placed in the completed queue.
		/// </summary>
		/// <remarks>Subscribers which touch WinForms controls must marshal to the UI thread.</remarks>
		public event EventHandler<NexusCollectionNxmDispatchCompletedEventArgs> DispatchCompleted = delegate { };

		/// <summary>
		/// Creates a dispatcher over the owned Nexus Collections provider.
		/// </summary>
		public NexusCollectionNxmDispatcher(INexusCollectionsProvider provider)
		{
			_provider = provider ?? throw new ArgumentNullException(nameof(provider));
		}

		/// <summary>
		/// Gets the owned provider used by the read-only Collections preview surface.
		/// </summary>
		/// <remarks>
		/// Exposing the provider does not expose the underlying transport or credentials. Consumers still use only the
		/// typed Collections operations and their session-generation checks.
		/// </remarks>
		public INexusCollectionsProvider Provider
		{
			get { return _provider; }
		}

		/// <summary>
		/// Resolves one selector without downloading the Collection bundle or mutating game/native state.
		/// </summary>
		public async Task<NexusCollectionNxmDispatchResult> ResolveAsync(
			NexusCollectionNxmLink link,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (link == null)
				throw new ArgumentNullException(nameof(link));

			bool enteredGate = false;
			try
			{
				await _metadataGate.WaitAsync(cancellationToken).ConfigureAwait(false);
				enteredGate = true;
				NexusCollectionRevisionLookupResult lookup = await _provider
					.GetRevisionAsync(link.RevisionRequest, cancellationToken)
					.ConfigureAwait(false);
				return new NexusCollectionNxmDispatchResult(link, lookup, null);
			}
			catch (Exception ex)
			{
				return new NexusCollectionNxmDispatchResult(link, null, ex);
			}
			finally
			{
				if (enteredGate)
					_metadataGate.Release();
			}
		}

		/// <summary>
		/// Starts a non-blocking read-only resolution and retains its result for the Collections UI.
		/// </summary>
		/// <remarks>
		/// Identical selectors already in flight are coalesced. This method intentionally performs no bundle acquisition
		/// and creates no Collection installation operation.
		/// </remarks>
		public void Enqueue(NexusCollectionNxmLink link)
		{
			if (link == null)
				throw new ArgumentNullException(nameof(link));

			string key = GetDispatchKey(link);
			lock (_syncRoot)
			{
				if (!_inFlight.Add(key))
					return;
			}

			ResolveQueueAndPublishAsync(link, key);
		}

		/// <summary>
		/// Removes the oldest completed incoming Collection request for consumption by the UI/coordinator.
		/// </summary>
		public bool TryDequeueCompleted(out NexusCollectionNxmDispatchResult result)
		{
			lock (_syncRoot)
			{
				if (_completed.Count == 0)
				{
					result = null;
					return false;
				}

				result = _completed.Dequeue();
				return true;
			}
		}

		private async Task ResolveQueueAndPublishAsync(NexusCollectionNxmLink link, string key)
		{
			NexusCollectionNxmDispatchResult result = await ResolveAsync(link).ConfigureAwait(false);
			lock (_syncRoot)
			{
				if (_completed.Count >= MaxCompletedResults)
				{
					_completed.Dequeue();
					Trace.TraceWarning("Discarded the oldest unconsumed Collection NXM result because the bounded startup queue is full.");
				}
				_completed.Enqueue(result);
				_inFlight.Remove(key);
			}

			try
			{
				DispatchCompleted(this, new NexusCollectionNxmDispatchCompletedEventArgs(result));
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection NXM completion subscriber failed: " + ex);
			}
		}

		private static string GetDispatchKey(NexusCollectionNxmLink link)
		{
			string revision = link.RevisionRequest.IsLatest
				? "latest"
				: link.RevisionRequest.RevisionNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
			return link.GameDomain + "|" + link.CollectionSlug + "|" + revision;
		}
	}
}
