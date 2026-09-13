namespace ChinhDo.Transactions
{
	using System;
	using System.ComponentModel;
	using System.IO;
	using System.Runtime.InteropServices;
	using System.Runtime.InteropServices.ComTypes;

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
		private const uint FileFlagOpenReparsePoint = 0x00200000;
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

		private enum FileEntryState
		{
			Absent,
			RegularFile,
			HardLink,
			SymbolicLink
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

			throw CreateLinkIOException("create", "symbolic", p_strLinkName, p_strTargetPath, Marshal.GetLastWin32Error());
		}

		/// <summary>
		/// Determines whether two existing paths resolve to the same underlying file.
		/// </summary>
		public bool IsSameFile(string p_strFirstPath, string p_strSecondPath)
		{
			if (string.IsNullOrWhiteSpace(p_strFirstPath) || string.IsNullOrWhiteSpace(p_strSecondPath))
				return false;

			FileAttributes ignored;
			if (!TryGetFileEntryAttributes(p_strFirstPath, out ignored) || !TryGetFileEntryAttributes(p_strSecondPath, out ignored))
				return false;

			return AreSameFile(p_strFirstPath, p_strSecondPath);
		}

		/// <summary>
		/// Captures the concrete deployment-entry state without relying on File.Exists for reparse points.
		/// </summary>
		private static FileEntryState GetFileEntryState(string p_strPath, string p_strExpectedTarget)
		{
			FileAttributes attributes;
			if (!TryGetFileEntryAttributes(p_strPath, out attributes))
				return FileEntryState.Absent;

			if (!string.IsNullOrWhiteSpace(p_strExpectedTarget) && (attributes & FileAttributes.ReparsePoint) != 0)
				return FileEntryState.SymbolicLink;

			if (!string.IsNullOrWhiteSpace(p_strExpectedTarget) && IsSameFileCore(p_strPath, p_strExpectedTarget))
				return FileEntryState.HardLink;

			return FileEntryState.RegularFile;
		}

		/// <summary>
		/// Reads attributes for the filesystem entry itself, including a dangling symbolic link.
		/// </summary>
		private static bool TryGetFileEntryAttributes(string p_strPath, out FileAttributes p_fatAttributes)
		{
			p_fatAttributes = 0;
			IntPtr handle = CreateFileNative(
				p_strPath,
				0,
				FileShareRead | FileShareWrite | FileShareDelete,
				IntPtr.Zero,
				OpenExisting,
				FileFlagOpenReparsePoint | FileFlagBackupSemantics,
				IntPtr.Zero);
			if (handle == InvalidHandleValue)
			{
				int error = Marshal.GetLastWin32Error();
				if (error == ErrorFileNotFound || error == ErrorPathNotFound)
					return false;

				var win32 = new Win32Exception(error);
				throw new IOException(string.Format("Unable to inspect filesystem entry '{0}'. Win32 error {1}: {2}",
					p_strPath, error, win32.Message), win32);
			}

			try
			{
				ByHandleFileInformation fileInfo;
				if (!GetFileInformationByHandle(handle, out fileInfo))
				{
					int error = Marshal.GetLastWin32Error();
					var win32 = new Win32Exception(error);
					throw new IOException(string.Format("Unable to inspect filesystem entry '{0}'. Win32 error {1}: {2}",
						p_strPath, error, win32.Message), win32);
				}

				p_fatAttributes = (FileAttributes)fileInfo.FileAttributes;
				return true;
			}
			finally
			{
				CloseHandle(handle);
			}
		}

		/// <summary>
		/// Compares two paths by Windows file identity without reading their payload.
		/// </summary>
		private static bool IsSameFileCore(string p_strFirstPath, string p_strSecondPath)
		{
			FileAttributes ignored;
			if (!TryGetFileEntryAttributes(p_strFirstPath, out ignored) || !TryGetFileEntryAttributes(p_strSecondPath, out ignored))
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
				return false;

			try
			{
				return GetFileInformationByHandle(handle, out p_bfiFileInformation);
			}
			finally
			{
				CloseHandle(handle);
			}
		}

		/// <summary>
		/// Deletes a file-system entry even when it is a dangling symbolic link.
		/// </summary>
		private static void DeleteFileEntryIfPresent(string p_strPath)
		{
			try
			{
				File.Delete(p_strPath);
			}
			catch (FileNotFoundException)
			{
			}
			catch (DirectoryNotFoundException)
			{
			}
		}

		/// <summary>
		/// Recreates and verifies the original deployed file-link topology during transaction rollback.
		/// </summary>
		private static void RestoreFileLink(FileEntryState p_fesEntryState, string p_strLinkName, string p_strTargetPath)
		{
			DeleteFileEntryIfPresent(p_strLinkName);

			bool restored;
			string linkKind;
			switch (p_fesEntryState)
			{
				case FileEntryState.HardLink:
					linkKind = "hard";
					restored = CreateHardLinkNative(p_strLinkName, p_strTargetPath, IntPtr.Zero);
					break;
				case FileEntryState.SymbolicLink:
					linkKind = "symbolic";
					restored = CreateSymbolicLinkNative(p_strLinkName, p_strTargetPath, 0);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(p_fesEntryState));
			}

			if (!restored)
				throw CreateLinkIOException("restore", linkKind, p_strLinkName, p_strTargetPath, Marshal.GetLastWin32Error());

			VerifyRestoredFileLink(p_fesEntryState, p_strLinkName, p_strTargetPath);
		}

		/// <summary>
		/// Verifies that rollback recreated the original link kind and expected target identity.
		/// </summary>
		private static void VerifyRestoredFileLink(FileEntryState p_fesEntryState, string p_strLinkName, string p_strTargetPath)
		{
			FileAttributes attributes;
			if (!TryGetFileEntryAttributes(p_strLinkName, out attributes))
				throw new IOException(string.Format("Rollback reported success but restored link '{0}' does not exist. Expected target: '{1}'.",
					p_strLinkName, p_strTargetPath));

			if (p_fesEntryState == FileEntryState.SymbolicLink && (attributes & FileAttributes.ReparsePoint) == 0)
				throw new IOException(string.Format("Rollback restored '{0}', but it is not a symbolic-link reparse point. Expected target: '{1}'.",
					p_strLinkName, p_strTargetPath));
			if (p_fesEntryState == FileEntryState.HardLink && (attributes & FileAttributes.ReparsePoint) != 0)
				throw new IOException(string.Format("Rollback restored '{0}' as a reparse point instead of a hard link. Expected target: '{1}'.",
					p_strLinkName, p_strTargetPath));

			FileAttributes targetAttributes;
			if (TryGetFileEntryAttributes(p_strTargetPath, out targetAttributes) && !AreSameFile(p_strLinkName, p_strTargetPath))
				throw new IOException(string.Format("Rollback restored link '{0}', but it does not resolve to the expected target '{1}'.",
					p_strLinkName, p_strTargetPath));
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
	}
}
