using System;
using System.ComponentModel;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Per-consumer view over one shared native AddMod acquisition task.
	/// </summary>
	/// <remarks>
	/// Progress and terminal state are observed from the shared producer. Consumer cancellation is intercepted so one
	/// Collection request cannot directly cancel, pause, queue or resume native work still required by another consumer.
	/// </remarks>
	internal sealed class CollectionAcquisitionConsumerTask : IBackgroundTask
	{
		private readonly object _syncRoot = new object();
		private readonly IBackgroundTask _producerTask;
		private readonly Action<CollectionAcquisitionConsumerTask> _detachConsumer;
		private bool _consumerCancelled;
		private bool _terminalEventObserved;
		private bool _subscribed;

		/// <summary>
		/// Creates a consumer-scoped task view for one shared producer.
		/// </summary>
		internal CollectionAcquisitionConsumerTask(
			IBackgroundTask producerTask,
			Action<CollectionAcquisitionConsumerTask> detachConsumer)
		{
			_producerTask = producerTask ?? throw new ArgumentNullException(nameof(producerTask));
			_detachConsumer = detachConsumer ?? throw new ArgumentNullException(nameof(detachConsumer));
			if (IsTerminal(_producerTask.Status))
			{
				_terminalEventObserved = true;
			}
			else
			{
				_producerTask.PropertyChanged += ProducerTask_PropertyChanged;
				_producerTask.TaskEnded += ProducerTask_TaskEnded;
				_subscribed = true;
			}
		}

		/// <inheritdoc />
		public event EventHandler<TaskEndedEventArgs> TaskEnded = delegate { };

		/// <inheritdoc />
		public event PropertyChangedEventHandler PropertyChanged = delegate { };

		/// <inheritdoc />
		public int TaskSpeed
		{
			get { return _producerTask.TaskSpeed; }
			set { _producerTask.TaskSpeed = value; }
		}

		/// <inheritdoc />
		public int ActiveThreads
		{
			get { return _producerTask.ActiveThreads; }
			set { _producerTask.ActiveThreads = value; }
		}

		/// <inheritdoc />
		public string OverallMessage => _consumerCancelled ? "Collection acquisition consumer cancelled." : _producerTask.OverallMessage;

		/// <inheritdoc />
		public bool ShowOverallProgressAsMarquee => _producerTask.ShowOverallProgressAsMarquee;

		/// <inheritdoc />
		public long OverallProgress => _producerTask.OverallProgress;

		/// <inheritdoc />
		public long OverallProgressMinimum => _producerTask.OverallProgressMinimum;

		/// <inheritdoc />
		public long OverallProgressMaximum => _producerTask.OverallProgressMaximum;

		/// <inheritdoc />
		public int OverallProgressStepSize => _producerTask.OverallProgressStepSize;

		/// <inheritdoc />
		public bool ShowItemProgress => _producerTask.ShowItemProgress;

		/// <inheritdoc />
		public bool ShowItemProgressAsMarquee => _producerTask.ShowItemProgressAsMarquee;

		/// <inheritdoc />
		public string ItemMessage => _producerTask.ItemMessage;

		/// <inheritdoc />
		public long ItemProgress => _producerTask.ItemProgress;

		/// <inheritdoc />
		public long ItemProgressMinimum => _producerTask.ItemProgressMinimum;

		/// <inheritdoc />
		public long ItemProgressMaximum => _producerTask.ItemProgressMaximum;

		/// <inheritdoc />
		public int ItemProgressStepSize => _producerTask.ItemProgressStepSize;

		/// <inheritdoc />
		public TaskStatus Status => _consumerCancelled ? TaskStatus.Cancelled : _producerTask.Status;

		/// <inheritdoc />
		public TaskStatus InnerTaskStatus => _consumerCancelled ? TaskStatus.Cancelled : _producerTask.InnerTaskStatus;

		/// <inheritdoc />
		public object ReturnValue => _consumerCancelled ? null : _producerTask.ReturnValue;

		/// <inheritdoc />
		public bool IsActive => Status == TaskStatus.Running || Status == TaskStatus.Cancelling;

		/// <inheritdoc />
		public bool IsRemote => _producerTask.IsRemote;

		/// <inheritdoc />
		public bool SupportsPause => false;

		/// <inheritdoc />
		public bool SupportsQueue => false;

		/// <summary>
		/// Cancels this consumer without cancelling shared native work unless it is the final active consumer.
		/// </summary>
		public void Cancel()
		{
			lock (_syncRoot)
			{
				if (_consumerCancelled || _terminalEventObserved || IsTerminal(_producerTask.Status))
					return;

				_consumerCancelled = true;
				_terminalEventObserved = true;
			}

			_detachConsumer(this);
			Unsubscribe();

			PropertyChanged(this, new PropertyChangedEventArgs(nameof(Status)));
			PropertyChanged(this, new PropertyChangedEventArgs(nameof(IsActive)));
			TaskEnded(this, new TaskEndedEventArgs(TaskStatus.Cancelled,
				"Collection acquisition consumer cancelled.", null));
		}

		/// <inheritdoc />
		public void Pause()
		{
			throw new InvalidOperationException("A Collection acquisition consumer cannot pause a shared native producer.");
		}

		/// <inheritdoc />
		public void Queue()
		{
			throw new InvalidOperationException("A Collection acquisition consumer cannot queue a shared native producer.");
		}

		/// <inheritdoc />
		public void Resume()
		{
			throw new InvalidOperationException("A Collection acquisition consumer cannot resume a shared native producer.");
		}

		/// <summary>Forwards producer progress/state changes while this consumer remains attached.</summary>
		private void ProducerTask_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			lock (_syncRoot)
			{
				if (_consumerCancelled || _terminalEventObserved)
					return;
			}

			PropertyChanged(this, e);
		}

		/// <summary>Forwards producer lifecycle notifications and releases subscriptions after final completion.</summary>
		private void ProducerTask_TaskEnded(object sender, TaskEndedEventArgs e)
		{
			bool terminal = IsTerminal(e.Status);
			lock (_syncRoot)
			{
				if (_consumerCancelled || (terminal && _terminalEventObserved))
					return;
				if (terminal)
					_terminalEventObserved = true;
			}

			TaskEnded(this, e);
			if (terminal)
				Unsubscribe();
		}

		/// <summary>Detaches this consumer proxy from producer events exactly once.</summary>
		private void Unsubscribe()
		{
			lock (_syncRoot)
			{
				if (!_subscribed)
					return;
				_subscribed = false;
			}

			_producerTask.PropertyChanged -= ProducerTask_PropertyChanged;
			_producerTask.TaskEnded -= ProducerTask_TaskEnded;
		}

		/// <summary>Returns whether a producer state is final and cannot be resumed through the existing AddMod task.</summary>
		internal static bool IsTerminal(TaskStatus status)
		{
			return status == TaskStatus.Complete || status == TaskStatus.Cancelled || status == TaskStatus.Error;
		}
	}
}
