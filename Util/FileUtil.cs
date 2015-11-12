using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using ChinhDo.Transactions;
using Nexus.Client.Util.Collections;
using System.Collections.Generic;

namespace Nexus.Client.Util
{
	/// <summary>
	/// Utility functions to work with files.
	/// </summary>
	public class FileUtil
	{
		private static readonly Regex m_rgxCleanPath = new Regex("[" + Path.DirectorySeparatorChar + Path.AltDirectorySeparatorChar + "]{2,}");

		/// <summary>
		/// Creates a temporary directory.
		/// </summary>
		/// <returns>The path to the newly created temporary directory.</returns>
		public virtual string CreateTempDirectory()
		{
			return CreateTempDirectory(Path.GetTempPath());
		}

		/// <summary>
		/// Creates a temporary directory rooted at the given path.
		/// </summary>
		/// <param name="p_strBasePath">The path under which to create the temporary directory.</param>
		/// <returns>The path to the newly created temporary directory.</returns>
		protected string CreateTempDirectory(string p_strBasePath)
		{
			for (Int32 i = 0; i < Int32.MaxValue; i++)
			{
				string strPath = Path.Combine(p_strBasePath, Path.GetRandomFileName());
				if (!Directory.Exists(strPath))
				{
					Directory.CreateDirectory(strPath);
					return strPath + Path.DirectorySeparatorChar;
				}
			}
			throw new Exception("Could not create temporary folder because directory is full.");
		}

		/// <summary>
		/// Copies the source to the destination.
		/// </summary>
		/// <remarks>
		/// If the source is a directory, it is copied recursively.
		/// </remarks>
		/// <param name="p_tfmFileManager">The transactional file manager to use to copy the files.</param>
		/// <param name="p_strSource">The path from which to copy.</param>
		/// <param name="p_strDestination">The path to which to copy.</param>
		/// <param name="p_fncCopyCallback">A callback method that notifies the caller when a file has been copied,
		/// and provides the opportunity to cancel the copy operation.</param>
		/// <returns><c>true</c> if the copy operation wasn't cancelled; <c>false</c> otherwise.</returns>
		public static bool Copy(TxFileManager p_tfmFileManager, string p_strSource, string p_strDestination, Func<string, bool> p_fncCopyCallback)
		{
			if (File.Exists(p_strSource))
			{
				if (!Directory.Exists(Path.GetDirectoryName(p_strDestination)))
					p_tfmFileManager.CreateDirectory(Path.GetDirectoryName(p_strDestination));
				p_tfmFileManager.Copy(p_strSource, p_strDestination, true);
				if ((p_fncCopyCallback != null) && p_fncCopyCallback(p_strSource))
					return false;
			}
			else if (Directory.Exists(p_strSource))
			{
				if (!Directory.Exists(p_strDestination))
					p_tfmFileManager.CreateDirectory(p_strDestination);
				string[] strFiles = Directory.GetFiles(p_strSource);
				foreach (string strFile in strFiles)
				{
					p_tfmFileManager.Copy(strFile, Path.Combine(p_strDestination, Path.GetFileName(strFile)), true);
					if ((p_fncCopyCallback != null) && p_fncCopyCallback(strFile))
						return false;
				}
				string[] strDirectories = Directory.GetDirectories(p_strSource);
				foreach (string strDirectory in strDirectories)
					if (!Copy(strDirectory, Path.Combine(p_strDestination, Path.GetFileName(strDirectory)), p_fncCopyCallback))
						return false;
			}
			return true;
		}

		/// <summary>
		/// Copies the source to the destination.
		/// </summary>
		/// <remarks>
		/// If the source is a directory, it is copied recursively.
		/// </remarks>
		/// <param name="p_strSource">The path from which to copy.</param>
		/// <param name="p_strDestination">The path to which to copy.</param>
		/// <param name="p_fncCopyCallback">A callback method that notifies the caller when a file has been copied,
		/// and provides the opportunity to cancel the copy operation.</param>
		/// <returns><c>true</c> if the copy operation wasn't cancelled; <c>false</c> otherwise.</returns>
		public static bool Copy(string p_strSource, string p_strDestination, Func<string, bool> p_fncCopyCallback)
		{
			if (File.Exists(p_strSource))
			{
				if (!Directory.Exists(Path.GetDirectoryName(p_strDestination)))
					Directory.CreateDirectory(Path.GetDirectoryName(p_strDestination));
				File.Copy(p_strSource, p_strDestination, true);
				if ((p_fncCopyCallback != null) && p_fncCopyCallback(p_strSource))
					return false;
			}
			else if (Directory.Exists(p_strSource))
			{
				if (!Directory.Exists(p_strDestination))
					Directory.CreateDirectory(p_strDestination);
				string[] strFiles = Directory.GetFiles(p_strSource);
				foreach (string strFile in strFiles)
				{
					File.Copy(strFile, Path.Combine(p_strDestination, Path.GetFileName(strFile)), true);
					if ((p_fncCopyCallback != null) && p_fncCopyCallback(strFile))
						return false;
				}
				string[] strDirectories = Directory.GetDirectories(p_strSource);
				foreach (string strDirectory in strDirectories)
					if (!Copy(strDirectory, Path.Combine(p_strDestination, Path.GetFileName(strDirectory)), p_fncCopyCallback))
						return false;
			}
			return true;
		}

		/// <summary>
		/// Moves the specified file to the specified path, optionally overwritting
		/// any existing file.
		/// </summary>
		/// <param name="p_strFrom">The path to the file to move.</param>
		/// <param name="p_strTo">the path to which to move the file.</param>
		/// <param name="p_booOverwrite">Whether to overwrite any file found at the destination.</param>
		public static void Move(string p_strFrom, string p_strTo, bool p_booOverwrite)
		{
			if (p_booOverwrite)
				ForceDelete(p_strTo);
			File.Move(p_strFrom, p_strTo);
		}

		/// <summary>
		/// Creates a directory at the given path.
		/// </summary>
		/// <remarks>
		/// The standard <see cref="Directory.CreateDirectory()"/> has a latency issue where
		/// the directory is not necessarily ready for use immediately after creation. This
		/// method waits until the cirectory is created and ready before returning.
		/// </remarks>
		/// <param name="p_strPath">The path of the directory to create.</param>
		public static void CreateDirectory(string p_strPath)
		{
			int intRetries = 1;
			Directory.CreateDirectory(p_strPath);
			while (!Directory.Exists(p_strPath) && intRetries <= 10)
			{
				intRetries++;
				Thread.Sleep(100);
			}
		}

		/// <summary>
		/// Forces deletion of the given path.
		/// </summary>
		/// <remarks>
		/// This method is recursive if the given path is a directory. This method will clear read only/system
		/// attributes if required to delete the path.
		/// </remarks>
		/// <param name="p_strPath">The path to delete.</param>
		public static void ForceDelete(string p_strPath)
		{
			for (Int32 i = 0; i < 5; i++)
			{
				try
				{
					if (File.Exists(p_strPath))
						File.Delete(p_strPath);
					else if (Directory.Exists(p_strPath))
						Directory.Delete(p_strPath, true);
					return;
				}
				catch (Exception e)
				{
					if (!(e is IOException || e is UnauthorizedAccessException || e is DirectoryNotFoundException || e is FileNotFoundException))
						throw;
					try
					{
						ClearAttributes(p_strPath, true);
					}
					catch (Exception ex)
					{
						if (!(ex is IOException || ex is ArgumentException || ex is DirectoryNotFoundException || e is FileNotFoundException))
							throw;
						//we couldn't clear the attributes
					}
				}
			}
		}

		/// <summary>
		/// Clears the attributes of the given path.
		/// </summary>
		/// <remarks>
		/// This sets the path's attributes to <see cref="FileAttributes.Normal"/>. This operation is
		/// optionally recursive.
		/// </remarks>
		/// <param name="p_strPath">The path whose attributes are to be cleared.</param>
		/// <param name="p_booRecurse">Whether or not to clear the attributes on all children files and folers.</param>
		public static void ClearAttributes(string p_strPath, bool p_booRecurse)
		{
			try
			{
				if (File.Exists(p_strPath))
				{
					FileInfo fifFile = new FileInfo(p_strPath);
					fifFile.Attributes = FileAttributes.Normal;
				}
				else if (Directory.Exists(p_strPath))
					ClearAttributes(new DirectoryInfo(p_strPath), p_booRecurse);
			}
			catch (Exception e)
			{
				if (!(e is IOException || e is UnauthorizedAccessException || e is DirectoryNotFoundException || e is FileNotFoundException))
					throw;
			}
		}

		/// <summary>
		/// Clears the attributes of the given directory.
		/// </summary>
		/// <remarks>
		/// This sets the directory's attributes to <see cref="FileAttributes.Normal"/>. This operation is
		/// optionally recursive.
		/// </remarks>
		/// <param name="p_difPath">The directory whose attributes are to be cleared.</param>
		/// <param name="p_booRecurse">Whether or not to clear the attributes on all children files and folers.</param>
		public static void ClearAttributes(DirectoryInfo p_difPath, bool p_booRecurse)
		{
			try
			{
				p_difPath.Attributes = FileAttributes.Normal;
				if (p_booRecurse)
				{
					foreach (DirectoryInfo difDirectory in p_difPath.GetDirectories())
						ClearAttributes(difDirectory, p_booRecurse);
					foreach (FileInfo fifFile in p_difPath.GetFiles())
						fifFile.Attributes = FileAttributes.Normal;
				}
			}
			catch (Exception e)
			{
				if (!(e is IOException || e is UnauthorizedAccessException || e is DirectoryNotFoundException || e is FileNotFoundException))
					throw;
			}
		}

		/// <summary>
		/// Determines whether or not the given path represents a drive.
		/// </summary>
		/// <param name="p_strPath">The path for which it is to be determine whether it represents a drive.</param>
		/// <returns><c>true</c> the the given path represents a drive;
		/// <c>false</c> otherwise.</returns>
		public static bool IsDrivePath(string p_strPath)
		{
			return p_strPath.Trim(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).EndsWith(Path.VolumeSeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Writes the given data to the specified file.
		/// </summary>
		/// <remarks>
		/// If the specified file exists, it will be overwritten. If the specified file
		/// does not exist, it is created. If the directory containing the specified file
		/// does not exist, it is created.
		/// </remarks>
		/// <param name="p_strPath">The path to which to write the given data.</param>
		/// <param name="p_bteData">The data to write to the file.</param>
		public static void WriteAllBytes(string p_strPath, byte[] p_bteData)
		{
			string strDirectory = Path.GetDirectoryName(p_strPath);
			if (!Directory.Exists(strDirectory))
				Directory.CreateDirectory(strDirectory);
			File.WriteAllBytes(p_strPath, p_bteData);
		}

		/// <summary>
		/// Writes the given data to the specified file.
		/// </summary>
		/// <remarks>
		/// If the specified file exists, it will be overwritten. If the specified file
		/// does not exist, it is created. If the directory containing the specified file
		/// does not exist, it is created.
		/// </remarks>
		/// <param name="p_strPath">The path to which to write the given text.</param>
		/// <param name="p_strData">The text to write to the file.</param>
		public static void WriteAllText(string p_strPath, string p_strData)
		{
			string strDirectory = Path.GetDirectoryName(p_strPath);
			if (!Directory.Exists(strDirectory))
				Directory.CreateDirectory(strDirectory);
			File.WriteAllText(p_strPath, p_strData);
		}

		/// <summary>
		/// Determines if the given path is valid.
		/// </summary>
		/// <param name="p_strPath">The path to examine.</param>
		/// <returns><c>true</c> if the given path is valid;
		/// <c>false</c> if it contains invalid chars or it's too long.</returns>
		public static bool IsValidPath(string p_strPath)
		{
			if (String.IsNullOrEmpty(p_strPath))
				return false;
			else if ((p_strPath.Length >= 260) || (p_strPath.LastIndexOf(@"\") >= 247))
				return false;
			else
				return !ContainsInvalidPathChars(p_strPath);
		}

		/// <summary>
		/// Determines if the given path contains invalid characters.
		/// </summary>
		/// <param name="p_strPath">The path to examine.</param>
		/// <returns><c>true</c> if the given path contains invalid characters;
		/// <c>false</c> otherwise.</returns>
		public static bool ContainsInvalidPathChars(string p_strPath)
		{
			if (String.IsNullOrEmpty(p_strPath))
				return false;
			Set<string> setChars = new Set<string>();

			string strPath = Path.GetDirectoryName(p_strPath);
			if (!String.IsNullOrEmpty(strPath))
			{
				foreach (char chrInvalidChar in Path.GetInvalidPathChars())
					setChars.Add("\\x" + ((Int32)chrInvalidChar).ToString("x2"));
				Regex rgxInvalidPath = new Regex("[" + String.Join("", setChars.ToArray()) + "]");
				if (rgxInvalidPath.IsMatch(strPath))
					return true;
			}

			string strFile = Path.GetFileName(p_strPath);
			if (String.IsNullOrEmpty(strPath))
				return false;
			setChars.Clear();
			foreach (char chrInvalidChar in Path.GetInvalidFileNameChars())
				setChars.Add("\\x" + ((Int32)chrInvalidChar).ToString("x2"));
			Regex rgxInvalidFile = new Regex("[" + String.Join("", setChars.ToArray()) + "]");
			return rgxInvalidFile.IsMatch(strFile);
		}

		/// <summary>
		/// Removes all invalid characters from the given path.
		/// </summary>
		/// <param name="p_strPath">The path to clean.</param>
		/// <returns>The given path with all invalid characters removed.</returns>
		public static string StripInvalidPathChars(string p_strPath)
		{
			if (String.IsNullOrEmpty(p_strPath))
				return p_strPath;
			Set<string> setChars = new Set<string>();

			p_strPath = p_strPath.Replace("\"", "");

			string strPath = Path.GetDirectoryName(p_strPath);
			foreach (char chrInvalidChar in Path.GetInvalidPathChars())
				setChars.Add("\\x" + ((Int32)chrInvalidChar).ToString("x2"));
			Regex rgxInvalidPath = new Regex("[" + String.Join("", setChars.ToArray()) + "]");
			strPath = rgxInvalidPath.Replace(strPath, "");

			string strFile = Path.GetFileName(p_strPath);
			setChars.Clear();
			foreach (char chrInvalidChar in Path.GetInvalidFileNameChars())
				setChars.Add("\\x" + ((Int32)chrInvalidChar).ToString("x2"));
			rgxInvalidPath = new Regex("[" + String.Join("", setChars.ToArray()) + "]");
			strFile = rgxInvalidPath.Replace(strFile, "");

			return Path.Combine(strPath, strFile);
		}

		/// <summary>
		/// Generates a path that is relative to another path.
		/// </summary>
		/// <param name="p_strRoot">The root directory with respect to which the path will be made relative.</param>
		/// <param name="p_strPath">The path to make relative.</param>
		/// <returns>The relative form of the given path, relative with respect to the given root.</returns>
		/// <exception cref="ArgumentNullException">Thrown if either parameter is <c>null</c>.</exception>
		public static string RelativizePath(string p_strRoot, string p_strPath)
		{
			if (p_strRoot == null)
				throw new ArgumentNullException("p_strFrom");
			if (p_strPath == null)
				throw new ArgumentNullException("p_strRoot");

			p_strRoot = p_strRoot.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			p_strPath = p_strPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

			if (Path.IsPathRooted(p_strRoot) && Path.IsPathRooted(p_strPath) && !Path.GetPathRoot(p_strRoot).Equals(Path.GetPathRoot(p_strPath), StringComparison.OrdinalIgnoreCase))
				return p_strPath;

			string[] strRootPaths = p_strRoot.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
			string[] strPathsToRelativize = p_strPath.Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

			Int32 intMinPathLength = Math.Min(strRootPaths.Length, strPathsToRelativize.Length);
			Int32 intLastCommonPathIndex = -1;
			for (Int32 i = 0; i < intMinPathLength; i++)
			{
				if (!strRootPaths[i].Equals(strPathsToRelativize[i], StringComparison.OrdinalIgnoreCase))
					break;
				intLastCommonPathIndex = i;
			}
			if (intLastCommonPathIndex == -1)
				return p_strPath;

			List<string> lstRelativePaths = new List<string>();
			for (Int32 i = intLastCommonPathIndex + 1; i < strRootPaths.Length; i++)
				lstRelativePaths.Add("..");
			for (Int32 i = intLastCommonPathIndex + 1; i < strPathsToRelativize.Length; i++)
				lstRelativePaths.Add(strPathsToRelativize[i]);

			return String.Join(Path.DirectorySeparatorChar.ToString(), lstRelativePaths.ToArray());
		}

		/// <summary>
		/// Normalizes the given path.
		/// </summary>
		/// <remarks>
		/// This removes multiple consecutive path separators and makes sure all path
		/// separators are <see cref="Path.DirectorySeparatorChar"/>.
		/// </remarks>
		/// <param name="p_strPath">The path to normalize.</param>
		/// <returns>The normalized path.</returns>
		public static string NormalizePath(string p_strPath)
		{
			string strNormalizedPath = m_rgxCleanPath.Replace(p_strPath, Path.DirectorySeparatorChar.ToString());
			strNormalizedPath = strNormalizedPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			strNormalizedPath = strNormalizedPath.Trim(Path.DirectorySeparatorChar);
			return strNormalizedPath;
		}
	}
}
