using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Identifies the user-facing lifecycle state of Collection workflow activity.</summary>
	internal enum CollectionWorkflowActivityState
	{
		Idle = 0,
		Working = 1,
		Queued = 2,
		WaitingForUser = 3,
		Paused = 4,
		Completed = 5,
		Failed = 6
	}

	/// <summary>Identifies the broad workflow phase without coupling presentation to internal roadmap stage names.</summary>
	internal enum CollectionWorkflowActivityPhase
	{
		None = 0,
		Importing = 1,
		Preparing = 2,
		Acquiring = 3,
		Reviewing = 4,
		Applying = 5,
		Verifying = 6,
		Recovering = 7,
		Capturing = 8,
		Restoring = 9,
		Managing = 10
	}

	/// <summary>Producer-level acquisition state used to build one deduplicated workflow activity snapshot.</summary>
	internal sealed class CollectionWorkflowProducerActivity
	{
		internal CollectionWorkflowProducerActivity(Guid queueOperationId, TaskStatus status)
			: this(queueOperationId, status, false, 0L, 0L, 0)
		{
		}

		internal CollectionWorkflowProducerActivity(Guid queueOperationId, TaskStatus status, bool hasKnownByteProgress,
			long currentBytes, long totalBytes, int bytesPerSecond)
		{
			QueueOperationId = queueOperationId;
			Status = status;
			HasKnownByteProgress = hasKnownByteProgress && totalBytes > 0 && currentBytes >= 0 && currentBytes <= totalBytes;
			CurrentBytes = HasKnownByteProgress ? currentBytes : 0L;
			TotalBytes = HasKnownByteProgress ? totalBytes : 0L;
			BytesPerSecond = Math.Max(0, bytesPerSecond);
		}

		internal static CollectionWorkflowProducerActivity FromTask(Guid queueOperationId, IBackgroundTask task)
		{
			if (task == null)
				throw new ArgumentNullException(nameof(task));

			long minimum = task.ItemProgressMinimum;
			long maximum = task.ItemProgressMaximum;
			long current = task.ItemProgress;
			bool knownByteProgress = task.Status == TaskStatus.Running && task.ShowItemProgress && !task.ShowItemProgressAsMarquee &&
				task.TaskSpeed > 0 && maximum > minimum && current >= minimum && current <= maximum;
			return new CollectionWorkflowProducerActivity(queueOperationId, task.Status, knownByteProgress,
				knownByteProgress ? current - minimum : 0L, knownByteProgress ? maximum - minimum : 0L, task.TaskSpeed);
		}

		internal Guid QueueOperationId { get; }
		internal TaskStatus Status { get; }
		internal bool HasKnownByteProgress { get; }
		internal long CurrentBytes { get; }
		internal long TotalBytes { get; }
		internal int BytesPerSecond { get; }
	}

	/// <summary>Immutable phase-specific time-remaining estimate for the Collections workflow.</summary>
	internal sealed class CollectionWorkflowEtaSnapshot
	{
		internal CollectionWorkflowEtaSnapshot(bool isAvailable, bool isEstimating, TimeSpan? remaining, string basis,
			long? remainingBytes, double? bytesPerSecond, int sampleCount)
		{
			IsAvailable = isAvailable;
			IsEstimating = isEstimating;
			Remaining = remaining;
			Basis = basis ?? String.Empty;
			RemainingBytes = remainingBytes;
			BytesPerSecond = bytesPerSecond;
			SampleCount = Math.Max(0, sampleCount);
		}

		internal bool IsAvailable { get; }
		internal bool IsEstimating { get; }
		internal TimeSpan? Remaining { get; }
		internal string Basis { get; }
		internal long? RemainingBytes { get; }
		internal double? BytesPerSecond { get; }
		internal int SampleCount { get; }

		internal static CollectionWorkflowEtaSnapshot Unavailable(string basis)
		{
			return new CollectionWorkflowEtaSnapshot(false, false, null, basis, null, null, 0);
		}

		internal static CollectionWorkflowEtaSnapshot Estimating(string basis, int sampleCount)
		{
			return new CollectionWorkflowEtaSnapshot(false, true, null, basis, null, null, sampleCount);
		}
	}

	/// <summary>
	/// Conservative acquisition ETA estimator. Numeric estimates are emitted only when every unfinished shared producer has
	/// a stable byte range and the aggregate download throughput has produced repeatable samples.
	/// </summary>
	internal sealed class CollectionWorkflowEtaEstimator
	{
		private const int MinimumStableProducerSamples = 2;
		private const int MinimumStableAggregateSamples = 2;
		private const double SmoothingFactor = 0.30D;
		private const double MinimumStableSpeedRatio = 0.40D;
		private const double MaximumStableSpeedRatio = 2.50D;
		private static readonly TimeSpan MaximumReportedEta = TimeSpan.FromDays(30);

		private sealed class ProducerHistory
		{
			internal long CurrentBytes;
			internal long TotalBytes;
			internal int StableSamples;
		}

		private readonly Dictionary<Guid, ProducerHistory> _history = new Dictionary<Guid, ProducerHistory>();
		private string _producerShape = String.Empty;
		private double _smoothedBytesPerSecond;
		private int _aggregateStableSamples;

		internal void Reset()
		{
			_history.Clear();
			_producerShape = String.Empty;
			_smoothedBytesPerSecond = 0D;
			_aggregateStableSamples = 0;
		}

		internal CollectionWorkflowEtaSnapshot UpdateAcquisition(IEnumerable<CollectionWorkflowProducerActivity> producerActivities)
		{
			List<CollectionWorkflowProducerActivity> producers = (producerActivities ?? Enumerable.Empty<CollectionWorkflowProducerActivity>())
				.GroupBy(x => x.QueueOperationId)
				.Select(x => x.First())
				.OrderBy(x => x.QueueOperationId)
				.ToList();

			if (producers.Count == 0)
			{
				Reset();
				return CollectionWorkflowEtaSnapshot.Unavailable("acquisition-no-producers");
			}

			if (producers.Any(x => x.Status == TaskStatus.Error || x.Status == TaskStatus.Cancelled))
			{
				Reset();
				return CollectionWorkflowEtaSnapshot.Unavailable("acquisition-terminal-failure");
			}

			List<CollectionWorkflowProducerActivity> unfinished = producers.Where(x => x.Status != TaskStatus.Complete).ToList();
			if (unfinished.Count == 0)
			{
				Reset();
				return CollectionWorkflowEtaSnapshot.Unavailable("acquisition-complete");
			}

			if (unfinished.Any(x => x.Status == TaskStatus.Paused || x.Status == TaskStatus.Incomplete ||
				x.Status == TaskStatus.Cancelling || x.Status == TaskStatus.Retrying))
			{
				Reset();
				return CollectionWorkflowEtaSnapshot.Unavailable("acquisition-not-sampling-while-paused-or-retrying");
			}

			string shape = String.Join("|", producers.Select(x => x.QueueOperationId.ToString("N") + ":" +
				(x.HasKnownByteProgress ? x.TotalBytes.ToString() : "?")));
			if (!StringComparer.Ordinal.Equals(_producerShape, shape))
			{
				_history.Clear();
				_smoothedBytesPerSecond = 0D;
				_aggregateStableSamples = 0;
				_producerShape = shape;
			}

			foreach (CollectionWorkflowProducerActivity producer in producers.Where(x => x.Status == TaskStatus.Running && x.HasKnownByteProgress))
			{
				ProducerHistory previous;
				if (!_history.TryGetValue(producer.QueueOperationId, out previous) || previous.TotalBytes != producer.TotalBytes ||
					producer.CurrentBytes < previous.CurrentBytes)
				{
					_history[producer.QueueOperationId] = new ProducerHistory
					{
						CurrentBytes = producer.CurrentBytes,
						TotalBytes = producer.TotalBytes,
						StableSamples = 0
					};
					continue;
				}

				if (producer.CurrentBytes > previous.CurrentBytes && producer.BytesPerSecond > 0)
					previous.StableSamples++;
				previous.CurrentBytes = producer.CurrentBytes;
				previous.TotalBytes = producer.TotalBytes;
			}

			if (unfinished.Any(x => x.Status != TaskStatus.Running || !x.HasKnownByteProgress))
				return CollectionWorkflowEtaSnapshot.Estimating("acquisition-waiting-for-known-byte-totals", _aggregateStableSamples);

			foreach (CollectionWorkflowProducerActivity producer in unfinished)
			{
				ProducerHistory history;
				if (!_history.TryGetValue(producer.QueueOperationId, out history) || history.StableSamples < MinimumStableProducerSamples)
					return CollectionWorkflowEtaSnapshot.Estimating("acquisition-throughput-warming-up", _aggregateStableSamples);
			}

			long remainingBytes = unfinished.Sum(x => Math.Max(0L, x.TotalBytes - x.CurrentBytes));
			double rawBytesPerSecond = unfinished.Sum(x => (double)Math.Max(0, x.BytesPerSecond));
			if (remainingBytes <= 0 || rawBytesPerSecond <= 0D)
				return CollectionWorkflowEtaSnapshot.Estimating("acquisition-throughput-unavailable", _aggregateStableSamples);

			if (_smoothedBytesPerSecond <= 0D)
			{
				_smoothedBytesPerSecond = rawBytesPerSecond;
				_aggregateStableSamples = 1;
			}
			else
			{
				double ratio = rawBytesPerSecond / _smoothedBytesPerSecond;
				if (ratio < MinimumStableSpeedRatio || ratio > MaximumStableSpeedRatio)
				{
					_smoothedBytesPerSecond = rawBytesPerSecond;
					_aggregateStableSamples = 1;
					return CollectionWorkflowEtaSnapshot.Estimating("acquisition-throughput-changed", _aggregateStableSamples);
				}

				_smoothedBytesPerSecond = (SmoothingFactor * rawBytesPerSecond) + ((1D - SmoothingFactor) * _smoothedBytesPerSecond);
				_aggregateStableSamples++;
			}

			if (_aggregateStableSamples < MinimumStableAggregateSamples)
				return CollectionWorkflowEtaSnapshot.Estimating("acquisition-throughput-stabilizing", _aggregateStableSamples);

			double seconds = remainingBytes / _smoothedBytesPerSecond;
			if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds <= 0D)
				return CollectionWorkflowEtaSnapshot.Estimating("acquisition-eta-invalid", _aggregateStableSamples);

			if (seconds > MaximumReportedEta.TotalSeconds)
				return CollectionWorkflowEtaSnapshot.Estimating("acquisition-eta-outside-stable-range", _aggregateStableSamples);

			TimeSpan remaining = TimeSpan.FromSeconds(seconds);

			return new CollectionWorkflowEtaSnapshot(true, false, remaining,
				"acquisition-known-bytes-smoothed-throughput-v1", remainingBytes, _smoothedBytesPerSecond, _aggregateStableSamples);
		}
	}

	/// <summary>
	/// Immutable UI-facing workflow activity snapshot. Command locking is deliberately independent from actual work activity.
	/// </summary>
	internal sealed class CollectionWorkflowActivitySnapshot
	{
		internal CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState state, CollectionWorkflowActivityPhase phase,
			string statusText, bool commandsLocked, bool isWorkActive, bool isBackgroundContinuation,
			bool isIndeterminate, long? current, long? total, string progressBasis)
		{
			State = state;
			Phase = phase;
			StatusText = statusText ?? String.Empty;
			CommandsLocked = commandsLocked;
			IsWorkActive = isWorkActive;
			IsBackgroundContinuation = isBackgroundContinuation;
			IsIndeterminate = isIndeterminate;
			Current = current;
			Total = total;
			ProgressBasis = progressBasis ?? String.Empty;
		}

		internal CollectionWorkflowActivityState State { get; }
		internal CollectionWorkflowActivityPhase Phase { get; }
		internal string StatusText { get; }
		internal bool CommandsLocked { get; }
		internal bool IsWorkActive { get; }
		internal bool IsBackgroundContinuation { get; }
		internal bool IsIndeterminate { get; }
		internal long? Current { get; }
		internal long? Total { get; }
		internal string ProgressBasis { get; }
	}

	/// <summary>Pure visual decisions derived from one workflow snapshot. No WinForms control state is stored here.</summary>
	internal sealed class CollectionWorkflowActivityPresentation
	{
		internal CollectionWorkflowActivityPresentation(string iconText, bool animateIcon, bool showMemberLoadingOverlay)
		{
			IconText = iconText ?? String.Empty;
			AnimateIcon = animateIcon;
			ShowMemberLoadingOverlay = showMemberLoadingOverlay;
		}

		internal string IconText { get; }
		internal bool AnimateIcon { get; }
		internal bool ShowMemberLoadingOverlay { get; }
	}

	/// <summary>Maps activity semantics to accessibility-safe icon/overlay behavior without depending on color.</summary>
	internal static class CollectionWorkflowActivityPresentationBuilder
	{
		internal static CollectionWorkflowActivityPresentation Build(CollectionWorkflowActivitySnapshot snapshot)
		{
			if (snapshot == null)
				return new CollectionWorkflowActivityPresentation(String.Empty, false, false);

			string icon;
			bool animate = false;
			switch (snapshot.State)
			{
				case CollectionWorkflowActivityState.Working:
					icon = "◐";
					animate = snapshot.IsWorkActive;
					break;
				case CollectionWorkflowActivityState.Queued:
					icon = "…";
					break;
				case CollectionWorkflowActivityState.WaitingForUser:
					icon = "!";
					break;
				case CollectionWorkflowActivityState.Paused:
					icon = "Ⅱ";
					break;
				case CollectionWorkflowActivityState.Completed:
					icon = "✓";
					break;
				case CollectionWorkflowActivityState.Failed:
					icon = "×";
					break;
				default:
					icon = String.Empty;
					break;
			}

			// Only foreground work that actually locks commands covers the member editor. Background acquisition and
			// waiting-for-user states intentionally keep the member list and manual-download actions readable.
			bool showMemberLoadingOverlay = snapshot.CommandsLocked && snapshot.IsWorkActive;
			return new CollectionWorkflowActivityPresentation(icon, animate, showMemberLoadingOverlay);
		}
	}

	/// <summary>Pure presentation-state builder shared by the Collections control and focused tests.</summary>
	internal static class CollectionWorkflowActivityBuilder
	{
		internal static CollectionWorkflowActivitySnapshot Idle(string statusText)
		{
			return new CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState.Idle, CollectionWorkflowActivityPhase.None,
				statusText, false, false, false, false, null, null, "none");
		}

		internal static CollectionWorkflowActivitySnapshot Foreground(CollectionWorkflowActivityPhase phase, string statusText)
		{
			return new CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState.Working, phase, statusText,
				true, true, false, true, null, null, "indeterminate-phase-work");
		}

		internal static CollectionWorkflowActivitySnapshot Waiting(CollectionWorkflowActivityPhase phase, string statusText, bool commandsLocked)
		{
			return new CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState.WaitingForUser, phase, statusText,
				commandsLocked, false, false, false, null, null, "user-input");
		}

		internal static CollectionWorkflowActivitySnapshot Paused(CollectionWorkflowActivityPhase phase, string statusText, bool commandsLocked)
		{
			return new CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState.Paused, phase, statusText,
				commandsLocked, false, false, false, null, null, "paused");
		}

		internal static CollectionWorkflowActivitySnapshot Completed(CollectionWorkflowActivityPhase phase, string statusText)
		{
			return new CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState.Completed, phase, statusText,
				false, false, false, false, null, null, "completed");
		}

		internal static CollectionWorkflowActivitySnapshot Failed(CollectionWorkflowActivityPhase phase, string statusText)
		{
			return new CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState.Failed, phase, statusText,
				false, false, false, false, null, null, "failed");
		}

		internal static CollectionWorkflowActivitySnapshot FromAcquisition(IEnumerable<CollectionWorkflowProducerActivity> producerActivities,
			bool waitingForUser, string statusText)
		{
			if (waitingForUser)
				return Waiting(CollectionWorkflowActivityPhase.Acquiring, statusText, false);

			List<CollectionWorkflowProducerActivity> producers = (producerActivities ?? Enumerable.Empty<CollectionWorkflowProducerActivity>())
				.GroupBy(x => x.QueueOperationId)
				.Select(x => x.First())
				.ToList();
			if (producers.Count == 0)
				return Idle(statusText);

			long completed = producers.LongCount(x => x.Status == TaskStatus.Complete);
			long total = producers.Count;
			if (producers.Any(x => x.Status == TaskStatus.Error || x.Status == TaskStatus.Cancelled))
				return new CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState.Failed, CollectionWorkflowActivityPhase.Acquiring,
					statusText, false, false, false, false, completed, total, "shared-producer-count");
			if (producers.Any(x => x.Status == TaskStatus.Running || x.Status == TaskStatus.Retrying || x.Status == TaskStatus.Cancelling))
				return new CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState.Working, CollectionWorkflowActivityPhase.Acquiring,
					statusText, false, true, true, false, completed, total, "shared-producer-count");
			if (producers.Any(x => x.Status == TaskStatus.Queued))
				return new CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState.Queued, CollectionWorkflowActivityPhase.Acquiring,
					statusText, false, false, true, false, completed, total, "shared-producer-count");
			if (producers.Any(x => x.Status == TaskStatus.Paused || x.Status == TaskStatus.Incomplete))
				return new CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState.Paused, CollectionWorkflowActivityPhase.Acquiring,
					statusText, false, false, true, false, completed, total, "shared-producer-count");

			return new CollectionWorkflowActivitySnapshot(CollectionWorkflowActivityState.Completed, CollectionWorkflowActivityPhase.Acquiring,
				statusText, false, false, false, false, completed, total, "shared-producer-count");
		}
	}
}
