using System;
using System.Collections.Generic;
using System.Text;

namespace ChinhDo.Transactions
{
	/// <summary>
	/// Classes implementing this interface provide methods to manipulate files.
	/// </summary>
	public interface IFileOperations
	{
		/// <summary>
		/// Appends the specified string the file, creating the file if it doesn't already exist.
		/// </summary>
		/// <param name="path">The file to append the string to.</param>
		/// <param name="contents">The string to append to the file.</param>
		void AppendAllText(string path, string contents);

		/// <summary>
		/// Copies the specified <paramref name="sourceFileName"/> to <paramref name="destFileName"/>.
		/// </summary>
		/// <param name="sourceFileName">The file to copy.</param>
		/// <param name="destFileName">The name of the destination file.</param>
		/// <param name="overwrite">true if the destination file can be overwritten, otherwise false.</param>
		void Copy(string sourceFileName, string destFileName, bool overwrite);

		/// <summary>
		/// Creates all directories in the specified path.
		/// </summary>
		/// <param name="path">The directory path to create.</param>
		void CreateDirectory(string path);

		/// <summary>
		/// Deletes the specified file. An exception is not thrown if the file does not exist.
		/// </summary>
		/// <param name="path">The file to be deleted.</param>
		void Delete(string path);

		/// <summary>
		/// Moves the specified file to a new location.
		/// </summary>
		/// <param name="srcFileName">The name of the file to move.</param>
		/// <param name="destFileName">The new path for the file.</param>
		void Move(string srcFileName, string destFileName);

		/// <summary>
		/// Take a snapshot of the specified file. The snapshot is used to rollback the file later if needed.
		/// </summary>
		/// <param name="fileName">The file to take a snapshot for.</param>
		void Snapshot(string fileName);

		/// <summary>
		/// Creates a file, write the specified <paramref name="contents"/> to the file.
		/// </summary>
		/// <param name="path">The file to write to.</param>
		/// <param name="contents">The string to write to the file.</param>
		void WriteAllText(string path, string contents);

		/// <summary>
		/// Creates a file, write the specified <paramref name="contents"/> to the file.
		/// </summary>
		/// <param name="path">The file to write to.</param>
		/// <param name="contents">The bytes to write to the file.</param>
		void WriteAllBytes(string path, byte[] contents);
	}
}
