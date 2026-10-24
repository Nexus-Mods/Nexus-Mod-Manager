using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.DownloadManagement;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.OnlineServices.Wabbajack;
using Nexus.Client.Util.Localization;
using TaskStatus = Nexus.Client.BackgroundTasks.TaskStatus;

namespace Nexus.Client.Wabbajack
{
	/// <summary>Downloads raw Wabbajack inputs in NMM's normal queue without registering them as ready-to-install mods.</summary>
	internal sealed class WabbajackDownloadTask : AddModTask
	{
		private readonly IModRepository _repository;
		private readonly Uri _source;
		private readonly string _destination;
		private readonly string _hash;
		private readonly int _connections;
		private readonly long _size;
		private readonly object _sync = new object();
		private CancellationTokenSource _cancellation;
		private FileDownloadTask _native;
		private TaskStatus _stopStatus;
		private int _running;
		private bool _disposed;
		internal Exception Failure { get; private set; }

		/// <summary>Keeps raw source identity separate from the converted Collection members.</summary>
		internal WabbajackDownloadTask(IGameMode game, IEnvironmentInfo environment, IModRepository repository, Uri source, string destination, string hash, long size)
			: base(game, null, environment, null, null, repository, source, null)
		{
			_repository = repository; _source = source; _destination = destination; _hash = hash;
			_connections = environment.Settings.UseMultithreadedDownloads ? repository.AllowedConnections : 1;
			_size = size;
			IsRemote = true; ShowItemProgress = true; OverallMessage = "Wabbajack · " + Path.GetFileName(destination);
		}

		/// <summary>Starts or resumes the file through the normal Download Manager scheduling contract.</summary>
		public override async void Resume()
		{
			if (_disposed || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
			_cancellation = new CancellationTokenSource();
			_stopStatus = TaskStatus.Cancelled;
			Failure = null;
			Status = TaskStatus.Running;
			TaskStatus result = TaskStatus.Error;
			string savedPath = _destination;
			try
			{
				CancellationToken token = _cancellation.Token;
				Directory.CreateDirectory(Path.GetDirectoryName(_destination));
				Uri download = _source;
				if (_source.Scheme == "nxm")
				{
					string[] parts = _source.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
					download = (await Task.Run(() => _repository.GetFilePartInfo(parts[1], parts[3]), token).ConfigureAwait(false)).First().Uri;
				}
				token.ThrowIfCancellationRequested();
				if (WabbajackClient.IsCdn(download))
				{
					System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
					using (WabbajackClient client = new WabbajackClient())
						await client.DownloadFileAsync(download.AbsoluteUri, _destination, _hash,
							new TransferProgress(percent => { long total = Math.Max(1, _size / 1024); SetProgress(total * Math.Max(0, percent) / 100, total); TaskSpeed = (int)Math.Min(int.MaxValue, DownloadProgress / Math.Max(1, elapsed.Elapsed.TotalSeconds)); }), token).ConfigureAwait(false);
				}
				else
				{
					FileDownloadTask native = new FileDownloadTask(_repository, _connections, 1024 * 500, _repository.UserAgent);
					_native = native;
					TaskCompletionSource<TaskEndedEventArgs> finished = new TaskCompletionSource<TaskEndedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
					native.TaskEnded += (sender, args) => finished.TrySetResult(args);
					native.PropertyChanged += (sender, args) => {
						SetProgress(native.OverallProgress, native.OverallProgressMaximum);
						TaskSpeed = native.DownloadSpeed / 1024; InnerTaskStatus = native.Status;
						TimeSpan remaining = native.TimeRemaining > TimeSpan.Zero ? native.TimeRemaining : TimeSpan.Zero;
						ETA_Hours = (int)remaining.TotalHours; ETA_Minutes = remaining.Minutes; ETA_Seconds = remaining.Seconds;
					};
					await Task.Run(() => native.DownloadAsync(download, _destination, false), token).ConfigureAwait(false);
					TaskEndedEventArgs ended = await finished.Task.ConfigureAwait(false);
					token.ThrowIfCancellationRequested();
					if (ended.Status != TaskStatus.Complete)
						throw new IOException(!string.IsNullOrWhiteSpace(ended.Message) ? ended.Message : !string.IsNullOrWhiteSpace(native.ErrorInfo) ? native.ErrorInfo :
							LanguageManager.Get("Wabbajack.SourceDownloadFailed", "The download failed. See the technical report for details."));
					savedPath = ((DownloadedFileInfo)ended.ReturnValue).SavedFilePath;
					if (_hash != null)
						using (FileStream file = File.OpenRead(savedPath))
							if (WabbajackHash.Compute(file, token) != _hash)
								throw new InvalidDataException(LanguageManager.Get("Wabbajack.DownloadHashMismatch", "The downloaded source does not match the Wabbajack recipe."));
				}
				token.ThrowIfCancellationRequested();
				result = TaskStatus.Complete;
				ItemMessage = LanguageManager.Get("Wabbajack.SourceDownloaded", "Downloaded");
			}
			catch (OperationCanceledException) { result = _stopStatus; }
			catch (Exception error)
			{
				Failure = error;
				result = _cancellation.IsCancellationRequested ? _stopStatus : TaskStatus.Error;
				ItemMessage = Path.GetFileName(_destination) + ": " + error.Message;
				System.Diagnostics.Trace.TraceError("Wabbajack source download failed for {0}: {1}", Path.GetFileName(_destination), error);
			}
			finally
			{
				_native?.Dispose(); _native = null;
				lock (_sync) { _cancellation.Dispose(); _cancellation = null; }
				TaskSpeed = 0;
				Interlocked.Exchange(ref _running, 0);
				Status = result;
				InnerTaskStatus = Status;
				OnTaskEnded(ItemMessage, Status == TaskStatus.Complete ? savedPath : null);
			}
		}

		/// <summary>Cancels the current transfer using its native cancellation path.</summary>
		public override void Cancel() { Stop(TaskStatus.Cancelled); }
		/// <summary>Retains native partial files for a later resume.</summary>
		public override void Pause() { Stop(TaskStatus.Paused); }
		/// <summary>Returns the transfer to NMM's ordinary queue.</summary>
		public override void Queue() { Stop(TaskStatus.Queued); }

		/// <summary>Stops an active worker or transitions an inactive queued download.</summary>
		private void Stop(TaskStatus status)
		{
			lock (_sync)
			{
			if (Status == TaskStatus.Complete || Status == TaskStatus.Error || Status == TaskStatus.Cancelled) return;
			_stopStatus = status;
			if (_cancellation != null)
			{
				Status = TaskStatus.Cancelling;
				_cancellation.Cancel();
				if (status == TaskStatus.Cancelled) _native?.Cancel(); else _native?.Pause();
			}
			else { Status = status; OnTaskEnded(ItemMessage, null); }
			}
		}

		/// <summary>Populates the background task and Download Manager's established KiB progress properties.</summary>
		private void SetProgress(long current, long total)
		{
			DownloadMaximum = ItemProgressMaximum = total; DownloadProgress = ItemProgress = current;
		}

		/// <summary>Stops acquisition when the owning game session closes.</summary>
		public override void Dispose() { _disposed = true; Cancel(); base.Dispose(); }

		/// <summary>Delivers multipart progress directly to the monitored task.</summary>
		private sealed class TransferProgress : IProgress<int>
		{
			private readonly Action<int> _report;
			/// <summary>Creates one monitored progress callback.</summary>
			internal TransferProgress(Action<int> report) { _report = report; }
			/// <summary>Reports the completed portion of the CDN file.</summary>
			public void Report(int value) { _report(value); }
		}
	}
}
