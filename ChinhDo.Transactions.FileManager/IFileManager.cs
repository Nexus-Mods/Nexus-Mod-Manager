using System;
using System.Collections.Generic;
using System.Text;

namespace ChinhDo.Transactions
{
    /// <summary>
    /// Classes implementing this interface provide methods to work with files.
    /// </summary>
    public interface IFileManager : IFileOperations
    {
        /// <summary>
        /// Gets or sets a value indicating whether Transactions are enabled.
        /// </summary>
        bool TxEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to ignore exceptions during Rollback.
        /// </summary>
        bool IgnoreExceptionsInRollback { get; set; }

        /// <summary>
        /// Determines whether the specified path refers to a directory that exists on disk.
        /// </summary>
        /// <param name="path">The directory to check.</param>
        /// <returns>True if the directory exists.</returns>
        bool DirectoryExists(string path);

        /// <summary>
        /// Determines whether the specified file exists.
        /// </summary>
        /// <param name="path">The file to check.</param>
        /// <returns>True if the file exists.</returns>
        bool FileExists(string path);

        /// <summary>
        /// Creates a temporary file name. The file is not automatically created.
        /// </summary>
        /// <param name="extension">File extension (with the dot).</param>
        string GetTempFileName(string extension);

        /// <summary>
        /// Gets a temporary filename. The file is not automatically created.
        /// </summary>
        string GetTempFileName();
    }
}
