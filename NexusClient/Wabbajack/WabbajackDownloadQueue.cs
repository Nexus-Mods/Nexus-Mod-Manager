using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModManagement;
using TaskStatus = Nexus.Client.BackgroundTasks.TaskStatus;

namespace Nexus.Client.Wabbajack
{
	/// <summary>Awaits raw file acquisition owned by NMM's existing Download Manager.</summary>
	internal static class WabbajackDownloadQueue
	{
		/// <summary>Queues a file, observes completion, and leaves pause/resume/cancellation ownership with Download Manager.</summary>
		internal static async Task<string> DownloadAsync(ModManager manager, Uri source, string destination, string hash, long size, IProgress<string> progress, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			IBackgroundTask task = manager.QueueWabbajackDownload(source, destination, hash, size);
			TaskCompletionSource<string> completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
			Action observe = () => {
				if (task.Status == TaskStatus.Complete) completion.TrySetResult(task.ReturnValue as string ?? destination);
				else if (task.Status == TaskStatus.Cancelled) completion.TrySetCanceled();
				else if (task.Status == TaskStatus.Error || task.Status == TaskStatus.Incomplete)
					completion.TrySetException(new System.IO.IOException(task.ItemMessage, (task as WabbajackDownloadTask)?.Failure));
			};
			EventHandler<TaskEndedEventArgs> ended = (sender, args) => observe();
			PropertyChangedEventHandler changed = (sender, args) => {
				if (args.PropertyName == nameof(IBackgroundTask.Status) || args.PropertyName == nameof(IBackgroundTask.ItemProgress))
					progress?.Report(task.OverallMessage + " · " + task.Status + (task.ItemProgressMaximum > 0 ? " · " + Math.Min(100, task.ItemProgress * 100 / task.ItemProgressMaximum) + "%" : string.Empty));
			};
			task.TaskEnded += ended; task.PropertyChanged += changed;
			try
			{
				using (token.Register(() => completion.TrySetCanceled())) { observe(); return await completion.Task.ConfigureAwait(false); }
			}
			finally { task.TaskEnded -= ended; task.PropertyChanged -= changed; }
		}
	}
}
