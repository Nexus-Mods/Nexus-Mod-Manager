namespace ChinhDo.Transactions
{
	using System;
	using System.ComponentModel;
	using System.IO;
	using System.Runtime.InteropServices;
	using System.Runtime.InteropServices.ComTypes;

	/// <summary>
	/// Describes the concrete filesystem topology of a deployed file entry.
	/// </summary>
	public enum FileEntryKind
	{
		Unknown,
		Absent,
		RegularFile,
		HardLink,
		SymbolicLink
	}

	/// <summary>
	/// Transaction-tracked Windows link creation helpers used by deployment backends.
	/// </summary>
	public partial class TxFileManager
	{
		[DllImport("Kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool CreateHardLinkNative(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

		[DllImport("Kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool CreateSymbolicLinkNative(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

		[DllImport("Kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr CreateFileNative(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
			IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

		[DllImport("Kernel32.dll", EntryPoint = "FindFirstFileW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr FindFirstFileNative(string lpFileName, out Win32FindData lpFindFileData);

		[DllImport("Kernel32.dll", SetLastError = true)]
		private static extern bool FindClose(IntPtr hFindFile);

		[DllImport("Kernel32.dll", EntryPoint = "DeleteFileW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool DeleteFileNative(string lpFileName);

		[DllImport("Kernel32.dll", SetLastError = true)]
		private static extern bool CloseHandle(IntPtr hObject);

		[DllImport("Kernel32.dll", SetLastError = true)]
		private static extern bool GetFileInformationByHandle(IntPtr hFile, out ByHandleFileInformation lpFileInformation);

		private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);
		private const int ErrorFileNotFound = 2;
		private const int ErrorPathNotFound = 3;
		private const uint FileShareRead = 0x00000001;
		private const uint FileShareWrite = 0x00000002;
		private const uint FileShareDelete = 0x00000004;
		private const uint OpenExisting = 3;
		private const uint FileFlagBackupSemantics = 0x02000000;

		[StructLayout(LayoutKind.Sequential)]
		private struct ByHandleFileInformation
		{
			public uint FileAttributes;
			public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
			public uint VolumeSerialNumber;
			public uint FileSizeHigh;
			public uint FileSizeLow;
			public uint NumberOfLinks;
			public uint FileIndexHigh;
			public uint FileIndexLow;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct Win32FindData
		{
			public FileAttributes FileAttributes;
			public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
			public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
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
		/// Deletes a deployed file link while preserving its link topology for transaction rollback.
		/// </summary>
		public void DeleteLink(string p_strLinkName, string p_strTargetPath)
		{
			if (string.IsNullOrWhiteSpace(p_strLinkName))
				throw new ArgumentException("A link path is required.", nameof(p_strLinkName));
			if (string.IsNullOrWhiteSpace(p_strTargetPath))
				throw new ArgumentException("A link target is required.", nameof(p_strTargetPath));

			GetEnlistment().DeleteLink(p_strLinkName, p_strTargetPath);
		}

		/// <summary>
		/// Creates a hard link while journaling the destination for transaction rollback.
		/// </summary>
		public bool CreateHardLink(string p_strLinkName, string p_strTargetPath)
		{
			if (string.IsNullOrWhiteSpace(p_strLinkName))
				throw new ArgumentException("A link path is required.", nameof(p_strLinkName));
			if (string.IsNullOrWhiteSpace(p_strTargetPath))
				throw new ArgumentException("A link target is required.", nameof(p_strTargetPath));

			Snapshot(p_strLinkName);
			return CreateHardLinkNative(p_strLinkName, p_strTargetPath, IntPtr.Zero);
		}

		/// <summary>
		/// Creates a file symbolic link while journaling the destination for transaction rollback.
		/// </summary>
		public bool CreateSymbolicLink(string p_strLinkName, string p_strTargetPath)
		{
			if (string.IsNullOrWhiteSpace(p_strLinkName))
				throw new ArgumentException("A link path is required.", nameof(p_strLinkName));
			if (string.IsNullOrWhiteSpace(p_strTargetPath))
				throw new ArgumentException("A link target is required.", nameof(p_strTargetPath));

			Snapshot(p_strLinkName);
			if (CreateSymbolicLinkNative(p_strLinkName, p_strTargetPath, 0))
				return true;

			int error = Marshal.GetLastWin32Error();
			throw CreateLinkIOException("create", "symbolic", p_strLinkName, p_strTargetPath, error);
		}

		/// <summary>
		/// Determines whether two existing paths resolve to the same underlying file.
		/// </summary>
		public bool IsSameFile(string p_strFirstPath, string p_strSecondPath)
		{
			if (string.IsNullOrWhiteSpace(p_strFirstPath) || string.IsNullOrWhiteSpace(p_strSecondPath))
				return false;

			return AreSameFile(p_strFirstPath, p_strSecondPath);
		}

		/// <summary>
		/// Gets the concrete filesystem topology of a file entry without dereferencing symbolic links.
		/// </summary>
		public FileEntryKind GetFileEntryKind(string p_strPath, string p_strExpectedTarget)
		{
			if (string.IsNullOrWhiteSpace(p_strPath))
				throw new ArgumentException("A file path is required.", nameof(p_strPath));

			return GetFileEntryState(p_strPath, p_strExpectedTarget);
		}

		/// <summary>
		/// Deletes a filesystem file entry, including a dangling symbolic link, if it is present.
		/// </summary>
		public void DeleteFileEntryIfPresent(string p_strPath)
		{
			if (string.IsNullOrWhiteSpace(p_strPath))
				return;

			DeleteFileEntryIfPresentCore(p_strPath);
		}

		/// <summary>
		/// Captures the concrete deployment-entry state without relying on File.Exists for reparse points.
		/// </summary>
		private static FileEntryKind GetFileEntryState(string p_strPath, string p_strExpectedTarget)
		{
			FileAttributes attributes;
			if (!TryGetFileEntryAttributes(p_strPath, out attributes))
				return FileEntryKind.Absent;

			if ((attributes & FileAttributes.ReparsePoint) != 0)
				return FileEntryKind.SymbolicLink;

			if (!string.IsNullOrWhiteSpace(p_strExpectedTarget) && IsSameFileCore(p_strPath, p_strExpectedTarget))
				return FileEntryKind.HardLink;

			return FileEntryKind.RegularFile;
		}

		/// <summary>
		/// Reads attributes from the directory entry itself, so dangling symbolic links remain observable.
		/// </summary>
		private static bool TryGetFileEntryAttributes(string p_strPath, out FileAttributes p_fatAttributes)
		{
			p_fatAttributes = 0;
			Win32FindData findData;
			IntPtr findHandle = FindFirstFileNative(p_strPath, out findData);
			if (findHandle == InvalidHandleValue)
			{
				int error = Marshal.GetLastWin32Error();
				if (error == ErrorFileNotFound || error == ErrorPathNotFound)
					return false;

				throw CreateEntryIOException("inspect", p_strPath, error);
			}

			try
			{
				p_fatAttributes = findData.FileAttributes;
				return true;
			}
			finally
			{
				FindClose(findHandle);
			}
		}

		/// <summary>
		/// Compares two paths by Windows file identity without reading their payload.
		/// </summary>
		private static bool IsSameFileCore(string p_strFirstPath, string p_strSecondPath)
		{
			if (string.IsNullOrWhiteSpace(p_strFirstPath) || string.IsNullOrWhiteSpace(p_strSecondPath))
				return false;
			return AreSameFile(p_strFirstPath, p_strSecondPath);
		}

		/// <summary>
		/// Compares two file-system paths by their Windows file identity without reading their payload.
		/// </summary>
		private static bool AreSameFile(string p_strFirstPath, string p_strSecondPath)
		{
			ByHandleFileInformation firstInfo;
			ByHandleFileInformation secondInfo;
			return TryGetFileIdentity(p_strFirstPath, out firstInfo) &&
				TryGetFileIdentity(p_strSecondPath, out secondInfo) &&
				firstInfo.VolumeSerialNumber == secondInfo.VolumeSerialNumber &&
				firstInfo.FileIndexHigh == secondInfo.FileIndexHigh &&
				firstInfo.FileIndexLow == secondInfo.FileIndexLow;
		}

		/// <summary>
		/// Opens a path normally so symbolic links resolve to their targets, then captures file identity.
		/// </summary>
		private static bool TryGetFileIdentity(string p_strPath, out ByHandleFileInformation p_bfiFileInformation)
		{
			p_bfiFileInformation = new ByHandleFileInformation();
			IntPtr handle = CreateFileNative(
				p_strPath,
				0,
				FileShareRead | FileShareWrite | FileShareDelete,
				IntPtr.Zero,
				OpenExisting,
				FileFlagBackupSemantics,
				IntPtr.Zero);
			if (handle == InvalidHandleValue)
			{
				int error = Marshal.GetLastWin32Error();
				if (error == ErrorFileNotFound || error == ErrorPathNotFound)
					return false;

				throw CreateEntryIOException("resolve", p_strPath, error);
			}

			try
			{
				if (GetFileInformationByHandle(handle, out p_bfiFileInformation))
					return true;

				int error = Marshal.GetLastWin32Error();
				throw CreateEntryIOException("read file identity for", p_strPath, error);
			}
			finally
			{
				CloseHandle(handle);
			}
		}

		/// <summary>
		/// Deletes a file-system entry even when it is a dangling symbolic link.
		/// </summary>
		private static void DeleteFileEntryIfPresentCore(string p_strPath)
		{
			if (DeleteFileNative(p_strPath))
				return;

			int error = Marshal.GetLastWin32Error();
			if (error == ErrorFileNotFound || error == ErrorPathNotFound)
				return;

			throw CreateEntryIOException("delete", p_strPath, error);
		}

		/// <summary>
		/// Recreates and verifies the original deployed file-link topology during transaction rollback.
		/// </summary>
		private static void RestoreFileLink(FileEntryKind p_fekEntryKind, string p_strLinkName, string p_strTargetPath)
		{
			DeleteFileEntryIfPresentCore(p_strLinkName);

			bool restored;
			string linkKind;
			switch (p_fekEntryKind)
			{
				case FileEntryKind.HardLink:
					linkKind = "hard";
					restored = CreateHardLinkNative(p_strLinkName, p_strTargetPath, IntPtr.Zero);
					break;
				case FileEntryKind.SymbolicLink:
					linkKind = "symbolic";
					restored = CreateSymbolicLinkNative(p_strLinkName, p_strTargetPath, 0);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(p_fekEntryKind));
			}

			if (!restored)
			{
				int error = Marshal.GetLastWin32Error();
				throw CreateLinkIOException("restore", linkKind, p_strLinkName, p_strTargetPath, error);
			}

			VerifyRestoredFileLink(p_fekEntryKind, p_strLinkName, p_strTargetPath);
		}

		/// <summary>
		/// Verifies that rollback recreated the original link kind and expected target identity when resolvable.
		/// </summary>
		private static void VerifyRestoredFileLink(FileEntryKind p_fekEntryKind, string p_strLinkName, string p_strTargetPath)
		{
			FileEntryKind restoredKind = GetFileEntryState(p_strLinkName, p_strTargetPath);
			if (restoredKind != p_fekEntryKind)
			{
				throw new IOException(string.Format(
					"Rollback restored '{0}' with topology '{1}' instead of '{2}'. Expected target: '{3}'.",
					p_strLinkName, restoredKind, p_fekEntryKind, p_strTargetPath));
			}

			FileAttributes targetAttributes;
			if (TryGetFileEntryAttributes(p_strTargetPath, out targetAttributes) && !IsSameFileCore(p_strLinkName, p_strTargetPath))
			{
				throw new IOException(string.Format(
					"Rollback restored link '{0}', but it does not resolve to the expected target '{1}'.",
					p_strLinkName, p_strTargetPath));
			}
		}

		/// <summary>
		/// Builds a diagnostic link failure that preserves the native error and both link paths.
		/// </summary>
		private static IOException CreateLinkIOException(string p_strAction, string p_strLinkKind, string p_strLinkName,
			string p_strTargetPath, int p_intWin32Error)
		{
			var win32 = new Win32Exception(p_intWin32Error);
			return new IOException(string.Format(
				"Unable to {0} {1} link '{2}' -> '{3}'. Win32 error {4}: {5}",
				p_strAction, p_strLinkKind, p_strLinkName, p_strTargetPath, p_intWin32Error, win32.Message), win32);
		}

		/// <summary>
		/// Builds a diagnostic filesystem-entry failure that preserves the native error and path.
		/// </summary>
		private static IOException CreateEntryIOException(string p_strAction, string p_strPath, int p_intWin32Error)
		{
			var win32 = new Win32Exception(p_intWin32Error);
			return new IOException(string.Format(
				"Unable to {0} filesystem entry '{1}'. Win32 error {2}: {3}",
				p_strAction, p_strPath, p_intWin32Error, win32.Message), win32);
		}
	}
}
