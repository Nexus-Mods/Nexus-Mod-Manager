namespace Nexus.Client.Settings
{
    using Microsoft.Win32;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Windows.Forms;

    using Util;

    /// <summary>
    /// Contains functionality related to Shell Extensions for NMM.
    /// </summary>
    public static class ShellExtensionUtil
    {
        private static string[] _extensions = { ".zip", ".rar", ".7z" };

        private static string _addToNmmCommand = $"\"{Application.ExecutablePath}\" \"%1\"";

        /// <summary>
        /// Adds shell extensions for NMM.
        /// </summary>
        /// <returns>True if successful, otherwise false.</returns>
        public static bool AddShellExtensions()
        {
            var addedExtensions = new List<string>();

            foreach (var extension in _extensions)
            {
                if (AddShellExtension(extension))
                {
                    Trace.TraceInformation("[ShellExtension] Added shell extension for \"{0}\".", extension);
                    addedExtensions.Add(extension);
                }
                else
                {
                    Trace.TraceInformation("[ShellExtension] Failed to add extension \"{0}\", rolling back any previous changes:", extension);
                    
                    // Try to roll back any previous changes.
                    foreach (var addedExtension in addedExtensions)
                    {
                        RemoveShellExtension(addedExtension);
                    }

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Removes shell extensions for NMM.
        /// </summary>
        /// <returns>True if successful, otherwise false.</returns>
        public static bool RemoveShellExtensions()
        {
            var removedExtensions = new List<string>();

            foreach (var extension in _extensions)
            {
                if (RemoveShellExtension(extension))
                {
                    Trace.TraceInformation("[ShellExtension] Removed shell extension for \"{0}\".", extension);
                    removedExtensions.Add(extension);
                }
                else
                {
                    Trace.TraceInformation("[ShellExtension] Failed to remove extension \"{0}\", rolling back any previous changes:", extension);
                    
                    // Try to roll back any previous changes.
                    foreach (var removedExtension in removedExtensions)
                    {
                        AddShellExtension(removedExtension);
                    }

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if all extensions are set to this program.
        /// </summary>
        /// <returns>True if this installation of NMM is set for the shell extensions.</returns>
        public static bool ReadShellExtensions()
        {
            foreach (var extension in _extensions)
            {
                if (!ReadShellExtension(extension))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AddShellExtension(string extension)
        {
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
                Trace.TraceInformation("[ShellExtension] Adding key \"{0}\" with data \"{1}\".", extensionSubKey, _addToNmmCommand);
                Registry.SetValue(extensionSubKey, null, _addToNmmCommand, RegistryValueKind.String);

                return true;
            }
            catch (Exception e)
            {
                Trace.TraceWarning("[ShellExtension] Couldn't add shell extension for \"{0}\", due to {1} - {2}.", extension, e.GetType(), e.Message);
                return false;
            }
        }

        private static bool ReadShellExtension(string extension)
        {
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

                if (string.IsNullOrEmpty(result) || !result.Equals(_addToNmmCommand, StringComparison.OrdinalIgnoreCase))
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

        private static bool RemoveShellExtension(string extension)
        {
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
    }
}
