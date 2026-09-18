using System;
using System.Collections.Generic;
using System.Globalization;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Correlates immutable Collection acquisition requests with NMM's existing AddMod/download queue.
	/// </summary>
	/// <remarks>
	/// C4.17 established exact request-to-queue correlation and C4.21 added in-process consumer sharing. C4.22 also
	/// reuses persisted producer identities after restart so several consumers continue to refer to one native acquisition.
	/// Installation remains outside this layer.
	/// </remarks>
	public sealed class CollectionAcquisitionRequestCoordinator
	{
		private readonly ICollectionAddModQueue _queue;
		private readonly CollectionsAcquisitionStore _acquisitionStore;
		private readonly object _syncRoot = new object();
		private readonly Dictionary<string, SharedAcquisitionProducer> _activeProducers =
			new Dictionary<string, SharedAcquisitionProducer>(StringComparer.Ordinal);
		private readonly Dictionary<Guid, CollectionAcquisitionQueueCorrelation> _requestCorrelations =
			new Dictionary<Guid, CollectionAcquisitionQueueCorrelation>();
		private readonly Dictionary<Guid, AcquisitionRequestIdentity> _requestIdentities =
			new Dictionary<Guid, AcquisitionRequestIdentity>();

		/// <summary>
		/// Creates a coordinator over the existing AddMod queue adapter.
		/// </summary>
		public CollectionAcquisitionRequestCoordinator(ICollectionAddModQueue queue)
			: this(queue, null)
		{
		}

		/// <summary>
		/// Creates a coordinator with optional durable acquisition restart tracking.
		/// </summary>
		public CollectionAcquisitionRequestCoordinator(ICollectionAddModQueue queue, CollectionsAcquisitionStore acquisitionStore)
		{
			_queue = queue ?? throw new ArgumentNullException(nameof(queue));
			_acquisitionStore = acquisitionStore;
		}

		/// <summary>
		/// Persists a request which is waiting for user-mediated input before native AddMod work exists.
		/// </summary>
		public void TrackPending(CollectionAcquisitionRequest request, CollectionAcquisitionPersistenceMode mode)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			if (_acquisitionStore != null)
				_acquisitionStore.TrackPending(request, mode);
		}

		/// <summary>
		/// Queues the supplied source through NMM's existing AddMod pipeline and returns consumer-scoped correlation.
		/// </summary>
		/// <remarks>
		/// Requests for the same stable provider artifact share one active native AddMod producer even when their temporary NXM
		/// authorization query differs. Each request receives its own task proxy; cancelling that proxy only detaches the request.
		/// The producer is cancelled only when its final active Collection consumer detaches.
		/// </remarks>
		public CollectionAcquisitionQueueCorrelation Queue(
			CollectionAcquisitionRequest request,
			Uri sourceUri,
			ConfirmOverwriteCallback confirmOverwriteCallback)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			if (sourceUri == null)
				throw new ArgumentNullException(nameof(sourceUri));
			if (!sourceUri.IsAbsoluteUri)
				throw new ArgumentException("An absolute AddMod source URI is required.", nameof(sourceUri));

			ValidateSourceMatchesRequest(request, sourceUri);
			string producerKey = CreateProducerKey(request.SelectedArtifact);

			lock (_syncRoot)
			{
				AcquisitionRequestIdentity existingIdentity;
				if (_requestIdentities.TryGetValue(request.RequestId, out existingIdentity))
				{
					if (!existingIdentity.Matches(request))
						throw new InvalidOperationException("An acquisition request identifier cannot be rebound to different Collection acquisition intent.");
				}
				else
				{
					_requestIdentities[request.RequestId] = new AcquisitionRequestIdentity(request);
				}

				CollectionAcquisitionQueueCorrelation existingCorrelation;
				if (_requestCorrelations.TryGetValue(request.RequestId, out existingCorrelation))
					return existingCorrelation;

				SharedAcquisitionProducer producer;
				if (_activeProducers.TryGetValue(producerKey, out producer))
				{
					if (CollectionAcquisitionConsumerTask.IsTerminal(producer.Task.Status))
					{
						RemoveActiveProducer(producer);
						producer = null;
					}
					else if (!producer.AcceptingConsumers || producer.Task.Status == TaskStatus.Cancelling)
					{
						throw new InvalidOperationException("The shared AddMod acquisition producer is already cancelling. Retry only after that native operation reaches a terminal state.");
					}
				}

				if (producer == null)
				{
					Guid queueOperationId = request.RequestId;
					if (_acquisitionStore != null)
					{
						Guid? persistedQueueOperationId = _acquisitionStore.GetReusableQueueOperationId(request);
						if (persistedQueueOperationId.HasValue)
							queueOperationId = persistedQueueOperationId.Value;
					}
					IBackgroundTask task = _queue.Queue(sourceUri, confirmOverwriteCallback, queueOperationId);
					if (task == null)
						throw new InvalidOperationException("The native AddMod queue returned no background task for the acquisition request.");

					producer = new SharedAcquisitionProducer(producerKey, queueOperationId, task);
					if (!CollectionAcquisitionConsumerTask.IsTerminal(task.Status))
					{
						producer.TaskEndedHandler = (sender, args) => ProducerTaskEnded(producer, args);
						task.TaskEnded += producer.TaskEndedHandler;
						_activeProducers[producerKey] = producer;
					}
				}

				CollectionAcquisitionConsumerTask consumerTask = null;
				consumerTask = new CollectionAcquisitionConsumerTask(
					producer.Task,
					consumer => DetachConsumer(producer, request.RequestId, consumer));
				var correlation = new CollectionAcquisitionQueueCorrelation(
					request,
					producer.QueueOperationId,
					consumerTask);

				if (_acquisitionStore != null)
				{
					_acquisitionStore.TrackQueued(request, producer.QueueOperationId, GetPersistenceMode(sourceUri));
					if (CollectionAcquisitionConsumerTask.IsTerminal(producer.Task.Status))
						_acquisitionStore.MarkProducerState(producer.QueueOperationId, producer.Task.Status);
				}

				if (!CollectionAcquisitionConsumerTask.IsTerminal(producer.Task.Status))
				{
					producer.Consumers[request.RequestId] = consumerTask;
					_requestCorrelations[request.RequestId] = correlation;
				}
				return correlation;
			}
		}

		/// <summary>
		/// Detaches one Collection consumer and requests native cancellation only when no consumer still requires the producer.
		/// </summary>
		private void DetachConsumer(SharedAcquisitionProducer producer, Guid requestId,
			CollectionAcquisitionConsumerTask consumer)
		{
			IBackgroundTask producerToCancel = null;
			lock (_syncRoot)
			{
				CollectionAcquisitionConsumerTask registeredConsumer;
				if (!producer.Consumers.TryGetValue(requestId, out registeredConsumer) ||
					!ReferenceEquals(registeredConsumer, consumer))
					return;

				producer.Consumers.Remove(requestId);
				_requestCorrelations.Remove(requestId);
				if (_acquisitionStore != null)
					_acquisitionStore.MarkCancelled(requestId);
				if (producer.Consumers.Count != 0 || CollectionAcquisitionConsumerTask.IsTerminal(producer.Task.Status))
					return;

				producer.AcceptingConsumers = false;
				producerToCancel = producer.Task;
			}

			producerToCancel.Cancel();
		}

		/// <summary>
		/// Releases coordinator references after the shared native producer reaches a non-resumable terminal state.
		/// </summary>
		private void ProducerTaskEnded(SharedAcquisitionProducer producer, TaskEndedEventArgs args)
		{
			if (args == null || !CollectionAcquisitionConsumerTask.IsTerminal(args.Status))
				return;

			if (_acquisitionStore != null)
				_acquisitionStore.MarkProducerState(producer.QueueOperationId, args.Status);

			lock (_syncRoot)
			{
				RemoveActiveProducer(producer);
				foreach (Guid requestId in producer.Consumers.Keys)
					_requestCorrelations.Remove(requestId);
				producer.Consumers.Clear();
			}
		}

		/// <summary>
		/// Removes one producer from the active sharing index and detaches the coordinator terminal handler.
		/// </summary>
		private void RemoveActiveProducer(SharedAcquisitionProducer producer)
		{
			SharedAcquisitionProducer registered;
			if (_activeProducers.TryGetValue(producer.ProducerKey, out registered) && ReferenceEquals(registered, producer))
				_activeProducers.Remove(producer.ProducerKey);

			if (producer.TaskEndedHandler != null)
			{
				producer.Task.TaskEnded -= producer.TaskEndedHandler;
				producer.TaskEndedHandler = null;
			}
		}

		/// <summary>
		/// Builds the stable provider identity used to coalesce acquisition work independently from recipe and expected-hash choices.
		/// </summary>
		private static string CreateProducerKey(CollectionArtifactReference artifact)
		{
			string gameDomain;
			long modId;
			long fileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(artifact, out gameDomain, out modId, out fileId))
				throw new InvalidOperationException("The resolved Nexus artifact identity is malformed and cannot participate in shared acquisition.");

			return NexusCollectionModFileArtifactIdentity.Scheme + ":" +
				NexusCollectionModFileArtifactIdentity.Format(gameDomain, modId, fileId);
		}


		/// <summary>Classifies the durable acquisition route without retaining temporary NXM authorization.</summary>
		private static CollectionAcquisitionPersistenceMode GetPersistenceMode(Uri sourceUri)
		{
			NexusUrl nexusUrl = new NexusUrl(sourceUri);
			bool manual = !String.IsNullOrWhiteSpace(nexusUrl.Key) || nexusUrl.Expiry > 0 || nexusUrl.UserId > 0;
			return manual ? CollectionAcquisitionPersistenceMode.Manual : CollectionAcquisitionPersistenceMode.PremiumNxm;
		}


		private static void ValidateSourceMatchesRequest(CollectionAcquisitionRequest request, Uri sourceUri)
		{
			if (!StringComparer.OrdinalIgnoreCase.Equals(sourceUri.Scheme, "nxm"))
				throw new ArgumentException("Collection AddMod acquisition requires an exact Nexus mod/file NXM URI; verified reuse and manual/local candidates use their dedicated acquisition paths.", nameof(sourceUri));

			if (!StringComparer.Ordinal.Equals(request.SelectedArtifact.Scheme, NexusCollectionModFileArtifactIdentity.Scheme))
				throw new ArgumentException("An NXM mod/file URI can only satisfy a nexus-mod-file acquisition request.", nameof(sourceUri));

			string expectedDomain;
			long expectedModId;
			long expectedFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(request.SelectedArtifact, out expectedDomain, out expectedModId, out expectedFileId))
				throw new InvalidOperationException("The resolved Nexus artifact identity is malformed and cannot be correlated with AddMod.");

			NexusUrl nexusUrl = new NexusUrl(sourceUri);
			long actualModId;
			long actualFileId;
			if (!Int64.TryParse(nexusUrl.ModId, NumberStyles.None, CultureInfo.InvariantCulture, out actualModId) ||
				!Int64.TryParse(nexusUrl.FileId, NumberStyles.None, CultureInfo.InvariantCulture, out actualFileId) ||
				!StringComparer.OrdinalIgnoreCase.Equals(nexusUrl.Host, expectedDomain) ||
				actualModId != expectedModId || actualFileId != expectedFileId)
			{
				throw new ArgumentException("The NXM source does not identify the exact Nexus mod/file selected by the resolved Collection plan.", nameof(sourceUri));
			}
		}

		/// <summary>
		/// Lightweight immutable snapshot used to prevent request identifiers from being rebound after cancellation or completion.
		/// </summary>
		private sealed class AcquisitionRequestIdentity
		{
			/// <summary>Creates an immutable identity snapshot for one acquisition request.</summary>
			public AcquisitionRequestIdentity(CollectionAcquisitionRequest request)
			{
				PlanIdentity = request.PlanIdentity;
				Revision = request.Revision;
				Target = request.Target;
				MemberKey = request.MemberKey;
				Requirement = request.Requirement;
				SelectedArtifact = request.SelectedArtifact;
				RecipeIdentity = request.RecipeIdentity;
			}

			public CollectionPlanIdentity PlanIdentity { get; }
			public CollectionRevisionIdentity Revision { get; }
			public CollectionTargetIdentity Target { get; }
			public CollectionMemberKey MemberKey { get; }
			public CollectionMemberRequirement Requirement { get; }
			public CollectionArtifactReference SelectedArtifact { get; }
			public CollectionRecipeIdentity RecipeIdentity { get; }

			/// <summary>Returns whether the supplied request represents the same immutable acquisition intent.</summary>
			public bool Matches(CollectionAcquisitionRequest request)
			{
				return request != null &&
					Equals(PlanIdentity, request.PlanIdentity) &&
					Equals(Revision, request.Revision) &&
					Equals(Target, request.Target) &&
					Equals(MemberKey, request.MemberKey) &&
					Requirement == request.Requirement &&
					Equals(SelectedArtifact, request.SelectedArtifact) &&
					Equals(RecipeIdentity, request.RecipeIdentity);
			}
		}

		/// <summary>
		/// Tracks one active native AddMod producer and the Collection consumers currently depending on it.
		/// </summary>
		private sealed class SharedAcquisitionProducer
		{
			/// <summary>Creates the shared producer record around one native AddMod task.</summary>
			public SharedAcquisitionProducer(string producerKey, Guid queueOperationId, IBackgroundTask task)
			{
				ProducerKey = producerKey;
				QueueOperationId = queueOperationId;
				Task = task;
				AcceptingConsumers = true;
				Consumers = new Dictionary<Guid, CollectionAcquisitionConsumerTask>();
			}

			public string ProducerKey { get; }
			public Guid QueueOperationId { get; }
			public IBackgroundTask Task { get; }
			public bool AcceptingConsumers { get; set; }
			public Dictionary<Guid, CollectionAcquisitionConsumerTask> Consumers { get; }
			public EventHandler<TaskEndedEventArgs> TaskEndedHandler { get; set; }
		}
	}
}
