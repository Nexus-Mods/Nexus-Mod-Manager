namespace Nexus.Client.Settings
{
    using Microsoft.Win32;
    using System;
    using System.Diagnostics;
    using System.Windows.Forms;

    using Util;

    /// <summary>
    /// Contains functionality related to Shell Extensions for NMM.
    /// </summary>
    public static class ShellExtensionUtil
    {
        private static readonly string AddToNmmCommand = $"\"{Application.ExecutablePath}\" \"%1\"";

        /// <summary>
        /// Extensions that NMM can associate shell extensions with.
        /// </summary>
        public static string[] Extensions = { "zip", "rar", "7z" };

        public static bool AddShellExtension(string extension)
        {
            ValidateExtensionParameter(ref extension);

            var rootKey = @"HKEY_CLASSES_ROOT\" + extension;

            try
            {
                var key = Registry.GetValue(rootKey, null, null) as string;

                if (key == null)
                {
                    Trace.TraceWarning("[ShellExtension] Couldn't add shell extension for \"{0}\", registry key did not exist.", extension);
                    return false;
                }

                var commandKey = "Add_to_" + CommonData.ModManagerName.Replace(' ', '_');
                var extensionRootKey = "HKEY_CLASSES_ROOT\\" + key + "\\Shell\\" + commandKey;
                var extensionRootKeyValue = "Add to " + CommonData.ModManagerName;
                Trace.TraceInformation("[ShellExtension] Adding key \"{0}\" with data \"{1}\".", extensionRootKey, extensionRootKeyValue);
                Registry.SetValue(extensionRootKey, null, extensionRootKeyValue);

                var extensionSubKey = "HKEY_CLASSES_ROOT\\" + key + "\\Shell\\" + commandKey + "\\command";
                Trace.TraceInformation("[ShellExtension] Adding key \"{0}\" with data \"{1}\".", extensionSubKey, AddToNmmCommand);
                Registry.SetValue(extensionSubKey, null, AddToNmmCommand, RegistryValueKind.String);

                return true;
            }
            catch (Exception e)
            {
                Trace.TraceWarning("[ShellExtension] Couldn't add shell extension for \"{0}\", due to {1} - {2}.", extension, e.GetType(), e.Message);
                return false;
            }
        }

        public static bool ReadShellExtension(string extension)
        {
            ValidateExtensionParameter(ref extension);

            var rootKey = @"HKEY_CLASSES_ROOT\" + extension;

            try
            {
                var key = Registry.GetValue(rootKey, null, null) as string;

                if (key == null)
                {
                    return false;
                }

                var commandKey = "Add_to_" + CommonData.ModManagerName.Replace(' ', '_');
                
                var extensionSubKey = "HKEY_CLASSES_ROOT\\" + key + "\\Shell\\" + commandKey + "\\command";
                var result = Registry.GetValue(extensionSubKey, null, null) as string;

                if (string.IsNullOrEmpty(result) || !result.Equals(AddToNmmCommand, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                
                return true;
            }
            catch (Exception e)
            {
                Trace.TraceWarning("[ShellExtension] Couldn't read shell extension state for \"{0}\", due to {1} - {2}.", extension, e.GetType(), e.Message);
                return false;
            }
        }

        public static bool RemoveShellExtension(string extension)
        {
            ValidateExtensionParameter(ref extension);

            var rootKey = @"HKEY_CLASSES_ROOT\" + extension;

            try
            {
                var key = Registry.GetValue(rootKey, null, null) as string;

                if (key == null)
                {
                    // Key isn't there, so technically it's removed.
                    return true;
                }

                using (var rk = Registry.ClassesRoot.OpenSubKey(key + "\\Shell", true))
                {
                    if (rk == null)
                    {
                        // Key isn't there, so technically it's removed.
                        return true;
                    }

                    var strCommandKey = "Add_to_" + CommonData.ModManagerName.Replace(' ', '_');

                    if (Array.IndexOf(rk.GetSubKeyNames(), strCommandKey) != -1)
                    {
                        rk.DeleteSubKeyTree(strCommandKey);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Trace.TraceWarning("[ShellExtension] Couldn't remove shell extension for \"{0}\", due to {1} - {2}.", extension, e.GetType(), e.Message);
                return false;
            }
        }

        private static void ValidateExtensionParameter(ref string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                throw new ArgumentException("Argument cannot be null/empty.", nameof(extension));
            }

            if (!extension[0].Equals('.'))
            {
                extension = extension.Insert(0, ".");
            }
        }
    }
}
