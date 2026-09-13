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

		[DllImport("Kernel32.dll", SetLastError = true)]
		private static extern bool GetFileInformationByHandle(IntPtr hFile, out ByHandleFileInformation lpFileInformation);

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

		private enum FileLinkType
		{
			None,
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
			return CreateSymbolicLinkNative(p_strLinkName, p_strTargetPath, 0);
		}

		/// <summary>
		/// Determines whether a deployed path is the expected hard link or a symbolic link that must be recreated on rollback.
		/// </summary>
		private static FileLinkType GetFileLinkType(string p_strPath, string p_strExpectedTarget)
		{
			if (string.IsNullOrWhiteSpace(p_strExpectedTarget) || !File.Exists(p_strPath))
				return FileLinkType.None;

			if ((File.GetAttributes(p_strPath) & FileAttributes.ReparsePoint) != 0)
				return FileLinkType.SymbolicLink;

			return File.Exists(p_strExpectedTarget) && AreSameFile(p_strPath, p_strExpectedTarget)
				? FileLinkType.HardLink
				: FileLinkType.None;
		}

		/// <summary>
		/// Compares two file-system paths by their Windows file identity without reading their payload.
		/// </summary>
		private static bool AreSameFile(string p_strFirstPath, string p_strSecondPath)
		{
			using (FileStream first = File.Open(p_strFirstPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			using (FileStream second = File.Open(p_strSecondPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			{
				ByHandleFileInformation firstInfo;
				ByHandleFileInformation secondInfo;
				if (!GetFileInformationByHandle(first.SafeFileHandle.DangerousGetHandle(), out firstInfo) ||
					!GetFileInformationByHandle(second.SafeFileHandle.DangerousGetHandle(), out secondInfo))
				{
					return false;
				}

				return firstInfo.VolumeSerialNumber == secondInfo.VolumeSerialNumber &&
					firstInfo.FileIndexHigh == secondInfo.FileIndexHigh &&
					firstInfo.FileIndexLow == secondInfo.FileIndexLow;
			}
		}

		/// <summary>
		/// Recreates the original deployed file-link topology during transaction rollback.
		/// </summary>
		private static void RestoreFileLink(FileLinkType p_fltLinkType, string p_strLinkName, string p_strTargetPath)
		{
			bool restored;
			switch (p_fltLinkType)
			{
				case FileLinkType.HardLink:
					restored = CreateHardLinkNative(p_strLinkName, p_strTargetPath, IntPtr.Zero);
					break;
				case FileLinkType.SymbolicLink:
					restored = CreateSymbolicLinkNative(p_strLinkName, p_strTargetPath, 0);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(p_fltLinkType));
			}

			if (!restored)
				throw new IOException("Unable to restore the original file-link topology during transaction rollback.",
					new Win32Exception(Marshal.GetLastWin32Error()));
		}
	}
}
