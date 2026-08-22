namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    using Nexus.Client.Util;
    using Nexus.Client.Util.Localization;

    /// <summary>
    /// Resolves expensive file-link information after the File Manager grid has been published.
    /// </summary>
    internal static class FileManagerLinkTypeResolver
    {
        public static readonly string PendingDisplayText = LanguageManager.Get("FileManager.LinkType.Detecting", "Detecting...");
        public static readonly string UnavailableDisplayText = LanguageManager.Get("FileManager.LinkType.Unavailable", "Unavailable");
        private const int DefaultBatchSize = 512;
        private const int DefaultMaximumWorkers = 2;

        /// <summary>
        /// Counts rows whose link type still requires handle-based detection.
        /// </summary>
        /// <param name="rows">The File Manager rows to inspect.</param>
        /// <returns>The number of rows awaiting link-type resolution.</returns>
        public static int CountPendingRows(IList<FileManagerRow> rows)
        {
            if (rows == null)
                return 0;

            int pendingCount = 0;
            for (int index = 0; index < rows.Count; index++)
            {
                FileManagerRow row = rows[index];
                if (row != null && row.IsLinkTypePending)
                    pendingCount++;
            }

            return pendingCount;
        }

        /// <summary>
        /// Resolves pending file-link types using a bounded number of workers and publishes completed rows in batches.
        /// </summary>
        /// <param name="rows">The File Manager rows to resolve.</param>
        /// <param name="pendingCount">The number of rows expected to require resolution.</param>
        /// <param name="publishBatch">The callback that receives each completed batch and aggregate progress.</param>
        /// <param name="cancellationToken">The token used to cancel link-type resolution.</param>
        /// <returns>The completed background link-type diagnostics.</returns>
        public static FileManagerLinkTypeResolutionDiagnostics Resolve(IList<FileManagerRow> rows, int pendingCount, Action<IList<FileManagerLinkTypeUpdate>, int, int> publishBatch, CancellationToken cancellationToken)
        {
            if (rows == null) throw new ArgumentNullException("rows");
            if (publishBatch == null) throw new ArgumentNullException("publishBatch");
            if (pendingCount <= 0)
                return new FileManagerLinkTypeResolutionDiagnostics(0, 0, 0, 0, 0, 0, 0, 0, 0);

            Stopwatch watch = Stopwatch.StartNew();
            int completedCount = 0;
            int unavailableCount = 0;
            int notFoundCount = 0;
            int symbolicLinkCount = 0;
            int hardLinkCount = 0;
            int realFileCount = 0;
            int batchCount = 0;
            object publicationLock = new object();
            ParallelOptions options = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, Math.Min(DefaultMaximumWorkers, Environment.ProcessorCount))
            };

            Parallel.For(0, rows.Count, options,
                () => new List<FileManagerLinkTypeUpdate>(DefaultBatchSize),
                (index, loopState, localBatch) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileManagerRow row = rows[index];
                    if (row == null || !row.IsLinkTypePending)
                        return localBatch;

                    FileManagerLinkTypeState linkTypeState;
                    try
                    {
                        FileLinkType linkType = FileLinkHelper.GetFileLinkType(row.FullPath);
                        linkTypeState = FileManagerRow.GetLinkTypeState(linkType);
                    }
                    catch
                    {
                        linkTypeState = FileManagerLinkTypeState.Unavailable;
                    }

                    switch (linkTypeState)
                    {
                        case FileManagerLinkTypeState.SymbolicLink:
                            Interlocked.Increment(ref symbolicLinkCount);
                            break;
                        case FileManagerLinkTypeState.HardLink:
                            Interlocked.Increment(ref hardLinkCount);
                            break;
                        case FileManagerLinkTypeState.Real:
                            Interlocked.Increment(ref realFileCount);
                            break;
                        case FileManagerLinkTypeState.NotFound:
                            Interlocked.Increment(ref notFoundCount);
                            break;
                        default:
                            Interlocked.Increment(ref unavailableCount);
                            break;
                    }

                    localBatch.Add(new FileManagerLinkTypeUpdate(row, linkTypeState));
                    if (localBatch.Count >= DefaultBatchSize)
                    {
                        PublishBatch(localBatch, pendingCount, publishBatch, publicationLock, ref completedCount, ref batchCount);
                        return new List<FileManagerLinkTypeUpdate>(DefaultBatchSize);
                    }

                    return localBatch;
                },
                localBatch => PublishBatch(localBatch, pendingCount, publishBatch, publicationLock, ref completedCount, ref batchCount));

            watch.Stop();
            FileManagerLinkTypeResolutionDiagnostics diagnostics = new FileManagerLinkTypeResolutionDiagnostics(
                watch.ElapsedMilliseconds,
                completedCount,
                unavailableCount,
                options.MaxDegreeOfParallelism,
                batchCount,
                realFileCount,
                hardLinkCount,
                symbolicLinkCount,
                notFoundCount);
            Trace.TraceInformation("File Manager link-type resolution completed. {0}", diagnostics);
            return diagnostics;
        }

        /// <summary>
        /// Publishes a completed batch while serializing progress accounting across resolver workers.
        /// </summary>
        /// <param name="batch">The completed link-type updates.</param>
        /// <param name="pendingCount">The total number of pending rows.</param>
        /// <param name="publishBatch">The callback receiving the batch.</param>
        /// <param name="publicationLock">The lock serializing publication.</param>
        /// <param name="completedCount">The aggregate number of completed rows.</param>
        /// <param name="batchCount">The aggregate number of published batches.</param>
        private static void PublishBatch(List<FileManagerLinkTypeUpdate> batch, int pendingCount, Action<IList<FileManagerLinkTypeUpdate>, int, int> publishBatch, object publicationLock, ref int completedCount, ref int batchCount)
        {
            if (batch == null || batch.Count == 0)
                return;

            lock (publicationLock)
            {
                completedCount += batch.Count;
                batchCount++;
                publishBatch(batch, completedCount, pendingCount);
            }
        }

    }

    /// <summary>
    /// Records the outcome of one completed background File Manager link-type pass.
    /// </summary>
    internal sealed class FileManagerLinkTypeResolutionDiagnostics
    {
        /// <summary>
        /// Initializes completed background link-type diagnostics.
        /// </summary>
        /// <param name="elapsedMilliseconds">The resolver wall-clock duration.</param>
        /// <param name="completedCount">The number of resolved files.</param>
        /// <param name="unavailableCount">The number of files that could not be inspected.</param>
        /// <param name="workerCount">The maximum number of concurrent workers.</param>
        /// <param name="batchCount">The number of result batches published to the UI.</param>
        /// <param name="realFileCount">The number of regular files.</param>
        /// <param name="hardLinkCount">The number of hardlinks.</param>
        /// <param name="symbolicLinkCount">The number of symbolic links detected by the handle-based pass.</param>
        /// <param name="notFoundCount">The number of files that disappeared before inspection.</param>
        public FileManagerLinkTypeResolutionDiagnostics(long elapsedMilliseconds, int completedCount, int unavailableCount, int workerCount, int batchCount, int realFileCount, int hardLinkCount, int symbolicLinkCount, int notFoundCount)
        {
            ElapsedMilliseconds = elapsedMilliseconds;
            CompletedCount = completedCount;
            UnavailableCount = unavailableCount;
            WorkerCount = workerCount;
            BatchCount = batchCount;
            RealFileCount = realFileCount;
            HardLinkCount = hardLinkCount;
            SymbolicLinkCount = symbolicLinkCount;
            NotFoundCount = notFoundCount;
        }

        public long ElapsedMilliseconds { get; private set; }
        public int CompletedCount { get; private set; }
        public int UnavailableCount { get; private set; }
        public int WorkerCount { get; private set; }
        public int BatchCount { get; private set; }
        public int RealFileCount { get; private set; }
        public int HardLinkCount { get; private set; }
        public int SymbolicLinkCount { get; private set; }
        public int NotFoundCount { get; private set; }

        /// <summary>
        /// Formats the completed resolver counters for the trace log.
        /// </summary>
        /// <returns>A compact diagnostic summary.</returns>
        public override string ToString()
        {
            return String.Format("files={0}, real={1}, hard={2}, symbolic={3}, notFound={4}, unavailable={5}, workers={6}, batches={7}, elapsed={8}ms",
                CompletedCount,
                RealFileCount,
                HardLinkCount,
                SymbolicLinkCount,
                NotFoundCount,
                UnavailableCount,
                WorkerCount,
                BatchCount,
                ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Describes one resolved File Manager link-type value ready to be applied to the grid model.
    /// </summary>
    internal struct FileManagerLinkTypeUpdate
    {
        /// <summary>
        /// Initializes a compact link-type update without allocating a per-file result object or display string.
        /// </summary>
        /// <param name="row">The row whose link type was resolved.</param>
        /// <param name="state">The resolved compact link-type state.</param>
        public FileManagerLinkTypeUpdate(FileManagerRow row, FileManagerLinkTypeState state)
        {
            Row = row;
            State = state;
        }

        public FileManagerRow Row { get; private set; }
        public FileManagerLinkTypeState State { get; private set; }
    }
}
