using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>
	/// Resolves a directory through Windows filesystem handles. Path.GetFullPath alone does not
	/// collapse junctions, symlinks, SUBST drives or other aliases to the same game install.
	/// The NT device name is compared only within the current machine/session; it is not stored.
	/// No directory is created or modified.
	/// </summary>
	public static class Witcher3PhysicalPathIdentity
	{
		private const uint OpenExisting = 3;
		private const uint FileFlagBackupSemantics = 0x02000000;
		private const uint ShareReadWriteDelete = 0x1 | 0x2 | 0x4;
		private const uint VolumeNameNt = 0x2;

		[DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern SafeFileHandle OpenDirectory(string path, uint desiredAccess, uint shareMode,
			IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr templateFile);

		[DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path,
			uint characterCount, uint flags);

		/// <param name="requireExisting">True for a game root. False only for not-yet-created InstallInfo paths.</param>
		public static string Resolve(string configuredDirectory, bool requireExisting)
		{
			if (String.IsNullOrWhiteSpace(configuredDirectory) || !Path.IsPathRooted(configuredDirectory))
				throw new ArgumentException("An absolute directory path is required.", nameof(configuredDirectory));

			string fullPath = Path.GetFullPath(configuredDirectory);
			var missingSegments = new System.Collections.Generic.Stack<string>();
			string existing = fullPath;
			while (true)
			{
				try
				{
					FileAttributes attributes = File.GetAttributes(existing);
					if ((attributes & FileAttributes.Directory) == 0)
						throw new IOException("The configured directory resolves to a file: " + existing);
					break;
				}
				catch (FileNotFoundException)
				{
					// Only a genuinely absent component may be projected from its existing parent.
				}
				catch (DirectoryNotFoundException)
				{
				}

				if (requireExisting)
					throw new DirectoryNotFoundException("The Witcher game directory is missing: " + fullPath);

				string parent = Path.GetDirectoryName(existing);
				if (String.IsNullOrWhiteSpace(parent) || String.Equals(parent, existing, StringComparison.OrdinalIgnoreCase))
					throw new IOException("Cannot resolve the physical parent directory for " + fullPath);
				missingSegments.Push(Path.GetFileName(existing));
				existing = parent;
			}

			string identity;
			using (SafeFileHandle handle = OpenDirectory(existing, 0, ShareReadWriteDelete, IntPtr.Zero,
				OpenExisting, FileFlagBackupSemantics, IntPtr.Zero))
			{
				if (handle.IsInvalid)
					throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to resolve the physical Witcher 3 directory.");
				var buffer = new StringBuilder(512);
				uint needed = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, VolumeNameNt);
				if (needed == 0)
					throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to identify the Witcher 3 directory.");
				if (needed >= buffer.Capacity)
				{
					buffer = new StringBuilder(checked((int)needed + 1));
					needed = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, VolumeNameNt);
					if (needed == 0 || needed >= buffer.Capacity)
						throw new IOException("The physical Witcher 3 path could not be read completely.");
				}
				identity = buffer.ToString().TrimEnd('\\');
			}

			while (missingSegments.Count > 0)
				identity += "\\" + missingSegments.Pop();
			return identity;
		}
	}
}
