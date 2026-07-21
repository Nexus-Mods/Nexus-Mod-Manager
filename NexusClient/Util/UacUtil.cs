using System;
using System.Runtime.InteropServices;

namespace Nexus.Client.Util
{
	#region Enumerations

	/// <summary>
	/// The TOKEN_ELEVATION enum.
	/// </summary>
	public struct TOKEN_ELEVATION
	{
		/// <summary>
		/// Indicates if the token is elevated.
		/// </summary>
		public UInt32 TokenIsElevated;
	}

	/// <summary>
	/// The TOKEN_INFORMATION_CLASS enum.
	/// </summary>
	public enum TOKEN_INFORMATION_CLASS
	{
		/// <summary>
		/// The information class contains the user account of the token.
		/// </summary>
		TokenUser = 1,

		/// <summary>
		/// The information class contains the group accounts of the token.
		/// </summary>
		TokenGroups = 2,

		/// <summary>
		/// The information class contains the privileges of the token.
		/// </summary>
		TokenPrivileges = 3,

		/// <summary>
		/// The information class contains the owner security identifier for newly created objects.
		/// </summary>
		TokenOwner = 4,

		/// <summary>
		/// The information class contains the primary group security identifier for newly created objects.
		/// </summary>
		TokenPrimaryGroup = 5,

		/// <summary>
		/// The information class contains the default DACL for newly created objects.
		/// </summary>
		TokenDefaultDacl = 6,

		/// <summary>
		/// The information class contains the source of the token.
		/// </summary>
		TokenSource = 7,

		/// <summary>
		/// The information class contains a value that indicates the type of the token.
		/// </summary>
		TokenType = 8,

		/// <summary>
		/// The information class contains a value that indicates the impersonation level of the token.
		/// </summary>
		TokenImpersonationLevel = 9,

		/// <summary>
		/// The information class contains token statistics.
		/// </summary>
		TokenStatistics = 10,

		/// <summary>
		/// The information class contains the list of restricting security identifiers in a token.
		/// </summary>
		TokenRestrictedSids = 11,

		/// <summary>
		/// The information class contains a value that indicates the session id associated with the token.
		/// </summary>
		TokenSessionId = 12,

		/// <summary>
		/// The information class contains the user groups and privileges associated with the token.
		/// </summary>
		TokenGroupsAndPrivileges = 13,

		/// <summary>
		/// Reserved.
		/// </summary>
		TokenSessionReference = 14,

		/// <summary>
		/// The information class contains a value that indicates whether the token includes the SANDBOX_INERT flag.
		/// </summary>
		TokenSandBoxInert = 15,

		/// <summary>
		/// Reserved.
		/// </summary>
		TokenAuditPolicy = 16,

		/// <summary>
		/// The information class contains a value describing the origin of the token.
		/// </summary>
		TokenOrigin = 17,

		/// <summary>
		/// The information class contains a value that specifies the elevation level of the token.
		/// </summary>
		TokenElevationType = 18,

		/// <summary>
		/// The information class contains a handle to a token linked to this token.
		/// </summary>
		TokenLinkedToken = 19,

		/// <summary>
		/// The information class contains a value that specifies whether the token is elevated.
		/// </summary>
		TokenElevation = 20,
		
		/// <summary>
		/// The information class contains a value that specified if the token has ever been filtered.
		/// </summary>
		TokenHasRestrictions = 21,

		/// <summary>
		/// The information class contains a value that specifies security information in the token.
		/// </summary>
		TokenAccessInformation = 22,

		/// <summary>
		/// The information class contains a value that specifies if virtualization is allowed.
		/// </summary>
		TokenVirtualizationAllowed = 23,

		/// <summary>
		/// The information class contains a value that specifies if virtualization is enabled.
		/// </summary>
		TokenVirtualizationEnabled = 24,

		/// <summary>
		/// The information class contains a value that specifies the integrity level of the token.
		/// </summary>
		TokenIntegrityLevel = 25,

		/// <summary>
		/// The information class contains a value that specifies whether the token includes the UIAccess flag.
		/// </summary>
		TokenUIAccess = 26,

		/// <summary>
		/// The information class contains a value that specifies the token's mandatory policy.
		/// </summary>
		TokenMandatoryPolicy = 27,

		/// <summary>
		/// The information class contains the token's logon security identifier.
		/// </summary>
		TokenLogonSid = 28,
		
		/// <summary>
		/// The maximum value.
		/// </summary>
		MaxTokenInfoClass = 29
	}

	#endregion

	/// <summary>
	/// Utility class for getting information about UAC.
	/// </summary>
	public class UacUtil
	{
		/// <summary>
		/// Constant from the Windows SDK.
		/// </summary>
		public const uint TOKEN_QUERY = 0x0008;

		/// <summary>
		/// Opens the access token of a process.
		/// </summary>
		/// <param name="ProcessHandle">The process whose token is to be opened.</param>
		/// <param name="DesiredAccess">The desired access token we wish to open.</param>
		/// <param name="TokenHandle">The output parameter for the opened token.</param>
		/// <returns><c>true</c> if the desired token was opened;
		/// <c>false</c> otherwise.</returns>
		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool OpenProcessToken(IntPtr ProcessHandle, UInt32 DesiredAccess, out IntPtr TokenHandle);

		/// <summary>
		/// Gets the current process's handle.
		/// </summary>
		/// <returns>The cur</returns>
		[DllImport("kernel32.dll", SetLastError = true)]
		static extern IntPtr GetCurrentProcess();

		/// <summary>
		/// Gets information about the specified process access token.
		/// </summary>
		/// <param name="TokenHandle">The handle to the token about which to get the information.</param>
		/// <param name="TokenInformationClass">The type of infromation we want.</param>
		/// <param name="TokenInformation">The structure into which the information will be copied.</param>
		/// <param name="TokenInformationLength">The length of the information data structure.</param>
		/// <param name="ReturnLength">The length of the return information.</param>
		/// <returns><c>true</c> if the information was successfully retrieved;
		/// <c>false</c> otherwise.</returns>
		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool GetTokenInformation(
			IntPtr TokenHandle,
			TOKEN_INFORMATION_CLASS TokenInformationClass,
			IntPtr TokenInformation,
			uint TokenInformationLength,
			out uint ReturnLength);

		/// <summary>
		/// Closes the given handle.
		/// </summary>
		/// <param name="hObject">The handle to close.</param>
		/// <returns><c>true</c> if the was closed;
		/// <c>false</c> otherwise.</returns>
		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool CloseHandle(IntPtr hObject);

		/// <summary>
		/// Loads the specified library.
		/// </summary>
		/// <param name="lpFileName">The library to load.</param>
		/// <returns>A handle to the loaded library.</returns>
		[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = false)]
		public static extern IntPtr LoadLibrary(string lpFileName);

		/// <summary>
		/// Gets the address of the specified process.
		/// </summary>
		/// <param name="hmodule">The handle of the module.</param>
		/// <param name="procName">The process name.</param>
		/// <returns>The address of the specified process.</returns>
		[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
		public static extern IntPtr GetProcAddress(IntPtr hmodule, string procName);

		/// <summary>
		/// Gets whether the OS has UAC.
		/// </summary>
		/// <value>Whether the OS has UAC.</value>
		public static bool IsUACOperatingSystem
		{
			get
			{
				//Vista was version 6.0 - it is the earliest version with UAC
				return Environment.OSVersion.Version >= new Version("6.0");
			}
		}

		/// <summary>
		/// Gets whether or not the current process is elevated.
		/// </summary>
		/// <remarks>
		/// This return <c>true</c> if:
		/// The current OS supports UAC, UAC is on, and the process is being run as an elevated user.
		/// OR
		/// The current OS supports UAC, UAC is off, and the process is being run by an administrator.
		/// OR
		/// The current OS doesn't support UAC.
		/// 
		/// Otherwise, this returns <c>false</c>.
		/// </remarks>
		/// <value>Whether or not the current process is elevated.</value>
		public static bool IsElevated
		{
			get
			{
				if (!IsUACOperatingSystem)
					return true;

				bool booCallSucceeded = false;
				IntPtr hToken = IntPtr.Zero;

				IntPtr ptrProcessHandle = GetCurrentProcess();
				if (ptrProcessHandle == IntPtr.Zero)
					throw new Exception("Could not get hanlde to current process.");

				if (!(booCallSucceeded = OpenProcessToken(ptrProcessHandle, TOKEN_QUERY, out hToken)))
					throw new Exception("Could not open process token.");

				try
				{
					TOKEN_ELEVATION tevTokenElevation;
					tevTokenElevation.TokenIsElevated = 0;

					UInt32 uintReturnLength = 0;
					Int32 intTokenElevationSize = Marshal.SizeOf(tevTokenElevation);
					IntPtr pteTokenElevation = Marshal.AllocHGlobal(intTokenElevationSize);
					try
					{
						Marshal.StructureToPtr(tevTokenElevation, pteTokenElevation, true);
						booCallSucceeded = GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenElevation, pteTokenElevation, (UInt32)intTokenElevationSize, out uintReturnLength);
						if ((!booCallSucceeded) || (intTokenElevationSize != uintReturnLength))
							throw new Exception("Could not get token information.");
						tevTokenElevation = (TOKEN_ELEVATION)Marshal.PtrToStructure(pteTokenElevation, typeof(TOKEN_ELEVATION));
					}
					finally
					{
						Marshal.FreeHGlobal(pteTokenElevation);
					}

					return (tevTokenElevation.TokenIsElevated != 0);
				}
				finally
				{
					CloseHandle(hToken);
				}
			}
		}
	}
}
