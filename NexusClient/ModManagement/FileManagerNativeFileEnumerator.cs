namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;

    /// <summary>
    /// Enumerates deployment files and returns the metadata supplied by the native directory scan.
    /// </summary>
    internal static class FileManagerNativeFileEnumerator
    {
        private const int ErrorFileNotFound = 2;
        private const int ErrorNoMoreFiles = 18;
        private const int ErrorInvalidParameter = 87;
        private const int FindFirstExLargeFetch = 2;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        /// <summary>
        /// Enumerates every regular file beneath the specified root without issuing a separate metadata query for each file.
        /// </summary>
        /// <param name="root">The deployment directory to scan.</param>
        /// <param name="cancellationToken">The token used to cancel the scan.</param>
        /// <param name="stats">The counters updated when directories or entries cannot be scanned.</param>
        /// <returns>The files discovered beneath the deployment root.</returns>
        public static IEnumerable<FileManagerFileEntry> EnumerateFiles(string root, CancellationToken cancellationToken, FileManagerEnumerationStats stats)
        {
            if (String.IsNullOrWhiteSpace(root))
                throw new ArgumentException("A deployment root is required.", "root");
            if (stats == null)
                throw new ArgumentNullException("stats");

            string fullRoot = Path.GetFullPath(root);
            Stack<PendingDirectory> pendingDirectories = new Stack<PendingDirectory>();
            pendingDirectories.Push(new PendingDirectory(fullRoot, String.Empty));

            while (pendingDirectories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PendingDirectory directory = pendingDirectories.Pop();
                string searchPattern;
                if (!TryCombinePath(directory.FullPath, "*", out searchPattern))
                {
                    stats.SkippedDirectories++;
                    continue;
                }

                NativeFindData findData;
                IntPtr findHandle = OpenDirectorySearch(searchPattern, out findData);
                if (findHandle == InvalidHandleValue)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (!IsEmptyDirectoryError(errorCode))
                        stats.SkippedDirectories++;
                    continue;
                }

                try
                {
                    bool hasEntry = true;
                    while (hasEntry)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string entryName = findData.FileName;
                        if (!IsCurrentOrParentDirectory(entryName))
                        {
                            bool isDirectory = (findData.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                            bool isReparsePoint = (findData.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;

                            if (isDirectory)
                            {
                                if (isReparsePoint)
                                {
                                    stats.SkippedReparseDirectories++;
                                }
                                else
                                {
                                    PendingDirectory childDirectory;
                                    if (TryCreateChildDirectory(directory, entryName, out childDirectory))
                                        pendingDirectories.Push(childDirectory);
                                    else
                                        stats.SkippedDirectories++;
                                }
                            }
                            else
                            {
                                FileManagerFileEntry fileEntry;
                                if (TryCreateFileEntry(directory, entryName, findData, out fileEntry))
                                {
                                    stats.EnumeratedFiles++;
                                    if (isReparsePoint)
                                        stats.ReparseFiles++;
                                    yield return fileEntry;
                                }
                                else
                                {
                                    stats.SkippedFiles++;
                                }
                            }
                        }

                        hasEntry = FindNextFile(findHandle, out findData);
                    }

                    int lastError = Marshal.GetLastWin32Error();
                    if (lastError != ErrorNoMoreFiles)
                        stats.SkippedDirectories++;
                }
                finally
                {
                    FindClose(findHandle);
                }
            }
        }

        /// <summary>
        /// Opens a native directory search and falls back to the broadly supported search mode when large-fetch enumeration is unavailable.
        /// </summary>
        /// <param name="searchPattern">The directory search pattern.</param>
        /// <param name="findData">The first entry returned by the search.</param>
        /// <returns>A native search handle, or the invalid handle value when the search cannot be opened.</returns>
        private static IntPtr OpenDirectorySearch(string searchPattern, out NativeFindData findData)
        {
            IntPtr findHandle = FindFirstFileEx(searchPattern, FindExInfoLevel.Basic, out findData, FindExSearchOperation.NameMatch, IntPtr.Zero, FindFirstExLargeFetch);
            if (findHandle != InvalidHandleValue || Marshal.GetLastWin32Error() != ErrorInvalidParameter)
                return findHandle;

            return FindFirstFileEx(searchPattern, FindExInfoLevel.Standard, out findData, FindExSearchOperation.NameMatch, IntPtr.Zero, 0);
        }

        /// <summary>
        /// Creates the pending-directory value for a child discovered by the native scan.
        /// </summary>
        /// <param name="parent">The parent directory being scanned.</param>
        /// <param name="entryName">The child directory name.</param>
        /// <param name="childDirectory">The resulting pending-directory value.</param>
        /// <returns><c>true</c> when both full and relative paths were created successfully; otherwise, <c>false</c>.</returns>
        private static bool TryCreateChildDirectory(PendingDirectory parent, string entryName, out PendingDirectory childDirectory)
        {
            string fullPath;
            string relativePath;
            if (!TryCombinePath(parent.FullPath, entryName, out fullPath) || !TryCombineRelativePath(parent.RelativePath, entryName, out relativePath))
            {
                childDirectory = default(PendingDirectory);
                return false;
            }

            childDirectory = new PendingDirectory(fullPath, relativePath);
            return true;
        }

        /// <summary>
        /// Creates a File Manager file entry from metadata already returned by the directory scan.
        /// </summary>
        /// <param name="directory">The directory containing the file.</param>
        /// <param name="entryName">The file name.</param>
        /// <param name="findData">The native metadata for the file.</param>
        /// <param name="fileEntry">The resulting File Manager entry.</param>
        /// <returns><c>true</c> when the entry paths were created successfully; otherwise, <c>false</c>.</returns>
        private static bool TryCreateFileEntry(PendingDirectory directory, string entryName, NativeFindData findData, out FileManagerFileEntry fileEntry)
        {
            string fullPath;
            string relativePath;
            if (!TryCombinePath(directory.FullPath, entryName, out fullPath) || !TryCombineRelativePath(directory.RelativePath, entryName, out relativePath))
            {
                fileEntry = default(FileManagerFileEntry);
                return false;
            }

            long length = ((long)findData.FileSizeHigh << 32) + findData.FileSizeLow;
            fileEntry = new FileManagerFileEntry(fullPath, relativePath, entryName, length, findData.Attributes);
            return true;
        }

        /// <summary>
        /// Combines a directory and child name without allowing path exceptions to abort the complete scan.
        /// </summary>
        /// <param name="directory">The parent directory.</param>
        /// <param name="entryName">The child entry name.</param>
        /// <param name="combinedPath">The resulting path.</param>
        /// <returns><c>true</c> when the path was created successfully; otherwise, <c>false</c>.</returns>
        private static bool TryCombinePath(string directory, string entryName, out string combinedPath)
        {
            try
            {
                combinedPath = Path.Combine(directory, entryName);
                return true;
            }
            catch
            {
                combinedPath = String.Empty;
                return false;
            }
        }

        /// <summary>
        /// Combines a relative directory and child name while avoiding a leading separator at the deployment root.
        /// </summary>
        /// <param name="relativeDirectory">The relative parent directory.</param>
        /// <param name="entryName">The child entry name.</param>
        /// <param name="relativePath">The resulting relative path.</param>
        /// <returns><c>true</c> when the relative path was created successfully; otherwise, <c>false</c>.</returns>
        private static bool TryCombineRelativePath(string relativeDirectory, string entryName, out string relativePath)
        {
            if (String.IsNullOrEmpty(relativeDirectory))
            {
                relativePath = entryName;
                return !String.IsNullOrEmpty(relativePath);
            }

            return TryCombinePath(relativeDirectory, entryName, out relativePath);
        }

        /// <summary>
        /// Determines whether a native directory entry is the current or parent directory marker.
        /// </summary>
        /// <param name="entryName">The native entry name.</param>
        /// <returns><c>true</c> for the current or parent directory marker; otherwise, <c>false</c>.</returns>
        private static bool IsCurrentOrParentDirectory(string entryName)
        {
            return String.Equals(entryName, ".", StringComparison.Ordinal) || String.Equals(entryName, "..", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an unsuccessful search represents an empty directory.
        /// </summary>
        /// <param name="errorCode">The native Win32 error code.</param>
        /// <returns><c>true</c> when the directory has no entries; otherwise, <c>false</c>.</returns>
        private static bool IsEmptyDirectoryError(int errorCode)
        {
            return errorCode == ErrorFileNotFound || errorCode == ErrorNoMoreFiles;
        }

        /// <summary>
        /// Starts a native directory search.
        /// </summary>
        /// <param name="fileName">The directory search pattern.</param>
        /// <param name="infoLevel">The amount of metadata requested for each result.</param>
        /// <param name="findData">The first result returned by the search.</param>
        /// <param name="searchOperation">The native search filtering mode.</param>
        /// <param name="searchFilter">An optional native search filter.</param>
        /// <param name="additionalFlags">Additional native search flags.</param>
        /// <returns>The native search handle.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindFirstFileExW", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr FindFirstFileEx(string fileName, FindExInfoLevel infoLevel, out NativeFindData findData, FindExSearchOperation searchOperation, IntPtr searchFilter, int additionalFlags);

        /// <summary>
        /// Advances a native directory search to its next entry.
        /// </summary>
        /// <param name="findHandle">The native directory search handle.</param>
        /// <param name="findData">The next result returned by the search.</param>
        /// <returns><c>true</c> when another entry was found; otherwise, <c>false</c>.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindNextFileW", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextFile(IntPtr findHandle, out NativeFindData findData);

        /// <summary>
        /// Releases a native directory search handle.
        /// </summary>
        /// <param name="findHandle">The native directory search handle.</param>
        /// <returns><c>true</c> when the handle was released; otherwise, <c>false</c>.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindClose(IntPtr findHandle);

        /// <summary>
        /// Identifies the native metadata detail level requested from a directory search.
        /// </summary>
        private enum FindExInfoLevel
        {
            Standard = 0,
            Basic = 1
        }

        /// <summary>
        /// Identifies the filtering operation used by a native directory search.
        /// </summary>
        private enum FindExSearchOperation
        {
            NameMatch = 0
        }

        /// <summary>
        /// Stores the native metadata returned for one directory entry.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeFindData
        {
            public FileAttributes Attributes;
            public uint CreationTimeLow;
            public uint CreationTimeHigh;
            public uint LastAccessTimeLow;
            public uint LastAccessTimeHigh;
            public uint LastWriteTimeLow;
            public uint LastWriteTimeHigh;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint Reserved0;
            public uint Reserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string FileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string AlternateFileName;
        }

        /// <summary>
        /// Stores a directory waiting to be scanned and its path relative to the deployment root.
        /// </summary>
        private struct PendingDirectory
        {
            /// <summary>
            /// Initializes a pending directory.
            /// </summary>
            /// <param name="fullPath">The full directory path.</param>
            /// <param name="relativePath">The directory path relative to the deployment root.</param>
            public PendingDirectory(string fullPath, string relativePath)
            {
                FullPath = fullPath;
                RelativePath = relativePath;
            }

            public string FullPath;
            public string RelativePath;
        }
    }

    /// <summary>
    /// Describes a file and the metadata returned while its containing directory was enumerated.
    /// </summary>
    internal struct FileManagerFileEntry
    {
        /// <summary>
        /// Initializes a File Manager file entry.
        /// </summary>
        /// <param name="fullPath">The full path to the file.</param>
        /// <param name="relativePath">The path relative to the deployment root.</param>
        /// <param name="fileName">The file name.</param>
        /// <param name="length">The file size in bytes.</param>
        /// <param name="attributes">The native file attributes.</param>
        public FileManagerFileEntry(string fullPath, string relativePath, string fileName, long length, FileAttributes attributes)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
            FileName = fileName;
            Length = length;
            Attributes = attributes;
        }

        public string FullPath { get; private set; }
        public string RelativePath { get; private set; }
        public string FileName { get; private set; }
        public long Length { get; private set; }
        public FileAttributes Attributes { get; private set; }
    }
}
