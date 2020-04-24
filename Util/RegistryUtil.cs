namespace Nexus.Client.Util
{
    using System.Security.Permissions;
    using System.Security;
    using Microsoft.Win32;

    public static class RegistryUtil
	{
		/// <summary>
		/// Checks whether the user has the given permission to operate on the given registry key.
		/// </summary>
		public static bool HavePermissionsOnKey(RegistryPermissionAccess p_rpaAccessLevel, string strRegKey)
		{
			try
			{
				RegistryPermission rpPermission = new RegistryPermission(p_rpaAccessLevel, strRegKey);
				rpPermission.Demand();
				return true;
			}
			catch (SecurityException)
			{
				return false;
			}
		}

		/// <summary>
		/// Checks whether the user has the right to write the given registry key.
		/// </summary>
		public static bool CanWriteKey(string strRegKey)
		{
			try
			{
				RegistryPermission rpPermission = new RegistryPermission(RegistryPermissionAccess.Write, strRegKey);
				rpPermission.Demand();
				return true;
			}
			catch (SecurityException)
			{
				return false;
			}
		}

		/// <summary>
		/// Checks whether the user has the right to read the given registry key.
		/// </summary>
		public static bool CanReadKey(string strRegKey)
		{
			try
			{
				RegistryPermission rpPermission = new RegistryPermission(RegistryPermissionAccess.Read, strRegKey);
				rpPermission.Demand();
				return true;
			}
			catch (SecurityException)
			{
				return false;
			}
		}

		/// <summary>
		/// Checks whether the user has the right to create the given registry key.
		/// </summary>
		public static bool CanCreateKey(string strRegKey)
		{
			try
			{
				RegistryPermission rpPermission = new RegistryPermission(RegistryPermissionAccess.Create, strRegKey);
				rpPermission.Demand();
				return true;
			}
			catch (SecurityException)
			{
				return false;
			}
		}

        public static string ReadValue(RegistryHive hive, string registryKey, string value)
        {
            var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(registryKey, false);
            return key?.GetValue(value, string.Empty).ToString();
        }
	}
}
