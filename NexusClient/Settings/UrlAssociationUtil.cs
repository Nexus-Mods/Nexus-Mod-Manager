namespace Nexus.Client.Settings
{
    using System;
    using System.Windows.Forms;
    using Microsoft.Win32;

    using Util;

    public static class UrlAssociationUtil
    {
        private static readonly string OpenWithNmmCommand = $"\"{Application.ExecutablePath}\" \"%1\"";

        /// <summary>
		/// Associates the specifed URL protocol with the client.
		/// </summary>
		/// <param name="urlProtocol">The URL protocol for which to create an association.</param>
		/// <param name="description">The description of the URL protocol.</param>
		/// <exception cref="InvalidOperationException">Thrown if the user does not have sufficient priviledges
		/// to create the association.</exception>
		public static void AssociateUrl(string urlProtocol, string description)
        {
            if (!UacUtil.IsElevated)
            {
                throw new InvalidOperationException("You must have administrative privileges to change URL associations.");
            }

            var strUrlId = "URL:" + description;

            Registry.SetValue(@"HKEY_CLASSES_ROOT\" + urlProtocol, null, strUrlId, RegistryValueKind.String);
            Registry.SetValue(@"HKEY_CLASSES_ROOT\" + urlProtocol, "URL Protocol", "", RegistryValueKind.String);
            Registry.SetValue(@"HKEY_CLASSES_ROOT\" + urlProtocol + @"\DefaultIcon", null, Application.ExecutablePath + ",0", RegistryValueKind.String);
            Registry.SetValue(@"HKEY_CLASSES_ROOT\" + urlProtocol + @"\shell\open\command", null, OpenWithNmmCommand, RegistryValueKind.String);
        }

        /// <summary>
        /// Removes the association of the specifed URL protocol with the client.
        /// </summary>
        /// <param name="urlProtocol">The URL protocol for which to remove the association.</param>
        /// <exception cref="InvalidOperationException">Thrown if the user does not have sufficient priviledges
        /// to remove the association.</exception>
        public static void UnassociateUrl(string urlProtocol)
        {
            if (!UacUtil.IsElevated)
            {
                throw new InvalidOperationException("You must have administrative privileges to change URL associations.");
            }

            var keys = Registry.ClassesRoot.GetSubKeyNames();

            if (Array.IndexOf(keys, urlProtocol) != -1)
            {
                Registry.ClassesRoot.DeleteSubKeyTree(urlProtocol);
            }
        }

        /// <summary>
        /// Determines if the specified URL protocol is associated with the client.
        /// </summary>
        /// <param name="p_strUrlProtocol">The protocol of the URL for which it is to be determined
        /// whether it is associated with the client.</param>
        /// <returns><c>true</c> if the URL protocol is associated with the client;
        /// <c>false</c> otherwise.</returns>
        public static bool IsUrlAssociated(string p_strUrlProtocol)
        {
            try
            {
                string key = Registry.GetValue($"HKEY_CLASSES_ROOT\\{p_strUrlProtocol}\\shell\\open\\command\\", null, string.Empty) as string;

                return key.Equals(OpenWithNmmCommand);
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }
    }
}
