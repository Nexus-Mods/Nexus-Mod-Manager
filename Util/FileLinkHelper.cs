using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nexus.Client.Util
{
	public enum FileLinkType { NotFound, SymbolicLink, HardLink, Real }

	public static class FileLinkHelper
	{
		public static FileLinkType GetFileLinkType(string path)
		{
			using (SafeFileHandle handle = CreateFile(path, 0, FileShare.ReadWrite | FileShare.Delete,
					   IntPtr.Zero, FileMode.Open,
					   FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero))
			{
				if (handle.IsInvalid)
				{
					int err = Marshal.GetLastWin32Error();
					if (err == ERROR_FILE_NOT_FOUND || err == ERROR_PATH_NOT_FOUND)
						return FileLinkType.NotFound;
					throw new Win32Exception(err);
				}

				if (!GetFileInformationByHandle(handle, out BY_HANDLE_FILE_INFORMATION info))
					throw new Win32Exception(Marshal.GetLastWin32Error());

				if ((info.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
					return FileLinkType.SymbolicLink;

				return info.NumberOfLinks > 1 ? FileLinkType.HardLink : FileLinkType.Real;
			}
		}

		private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
		private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
		private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
		private const int ERROR_FILE_NOT_FOUND = 2;
		private const int ERROR_PATH_NOT_FOUND = 3;

		[StructLayout(LayoutKind.Sequential)]
		private struct BY_HANDLE_FILE_INFORMATION
		{
			public uint FileAttributes;
			public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
			public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess,
			FileShare dwShareMode, IntPtr lpSecurityAttributes, FileMode dwCreationDisposition,
			uint dwFlagsAndAttributes, IntPtr hTemplateFile);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpInfo);
	}
}
