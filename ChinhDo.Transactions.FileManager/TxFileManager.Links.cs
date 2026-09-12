namespace ChinhDo.Transactions
{
	using System;
	using System.Runtime.InteropServices;

	/// <summary>
	/// Transaction-tracked Windows link creation helpers used by deployment backends.
	/// </summary>
	public partial class TxFileManager
	{
		[DllImport("Kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool CreateHardLinkNative(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

		[DllImport("Kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool CreateSymbolicLinkNative(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

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
	}
}
