using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Resolves a Windows directory to an alias-independent physical identity suitable for cross-process coordination.
	/// </summary>
	internal sealed class CollectionPhysicalDirectoryIdentity
	{
		private const uint FileFlagBackupSemantics = 0x02000000;

		private CollectionPhysicalDirectoryIdentity(string canonicalPath, string stableKey)
		{
			CanonicalPath = canonicalPath;
			StableKey = stableKey;
		}

		/// <summary>
		/// Gets the final Windows path for diagnostics and revalidation.
		/// </summary>
		public string CanonicalPath { get; }

		/// <summary>
		/// Gets the stable physical directory key used in target fingerprinting.
		/// </summary>
		public string StableKey { get; }

		/// <summary>
		/// Resolves an existing directory through a handle so aliases/reparse paths identify the same physical directory.
		/// </summary>
		public static CollectionPhysicalDirectoryIdentity Resolve(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A game installation directory is required.", nameof(path));

			string fullPath = Path.GetFullPath(path);
			if (!Directory.Exists(fullPath))
				throw new DirectoryNotFoundException("The game installation directory does not exist: " + fullPath);

			using (SafeFileHandle handle = CreateFile(fullPath, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero,
				FileMode.Open, FileFlagBackupSemantics, IntPtr.Zero))
			{
				if (handle.IsInvalid)
					throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the game installation directory for identity resolution.");

				ByHandleFileInformation information;
				if (!GetFileInformationByHandle(handle, out information))
					throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the physical game-directory identity.");

				ulong fileIndex = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
				string canonicalPath = ReadFinalPath(handle);
				string stableKey = information.VolumeSerialNumber != 0 && fileIndex != 0
					? string.Format(CultureInfo.InvariantCulture, "win-dir-v1:{0:X8}:{1:X16}", information.VolumeSerialNumber, fileIndex)
					: "win-path-v1:" + canonicalPath.ToUpperInvariant();

				return new CollectionPhysicalDirectoryIdentity(canonicalPath, stableKey);
			}
		}

		/// <summary>
		/// Reads the final normalized DOS/UNC path from an already opened directory handle.
		/// </summary>
		private static string ReadFinalPath(SafeFileHandle handle)
		{
			var buffer = new StringBuilder(512);
			uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
			if (length == 0)
				throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resolve the final game installation path.");

			if (length >= buffer.Capacity)
			{
				buffer = new StringBuilder(checked((int)length + 1));
				length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
				if (length == 0 || length >= buffer.Capacity)
					throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resolve the final game installation path.");
			}

			return NormalizeFinalPath(buffer.ToString());
		}

		/// <summary>
		/// Removes Win32 extended-path prefixes without changing the resolved target.
		/// </summary>
		private static string NormalizeFinalPath(string path)
		{
			const string uncPrefix = @"\\?\UNC\";
			const string extendedPrefix = @"\\?\";

			if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
				path = @"\\" + path.Substring(uncPrefix.Length);
			else if (path.StartsWith(extendedPrefix, StringComparison.OrdinalIgnoreCase))
				path = path.Substring(extendedPrefix.Length);

			string root = Path.GetPathRoot(path);
			if (!string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
				path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			return path;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, FileShare shareMode,
			IntPtr securityAttributes, FileMode creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder filePath, uint filePathSize, uint flags);

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
	}
}
