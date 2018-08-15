using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Win32;
using Nexus.Client.Util;

namespace Nexus.Client.Settings
{
    public class OsSettingsGroup : SettingsGroup
    {
        private bool _addShellExtensions = true;
        private bool _associateNxmUrls = true;

        public OsSettingsGroup(IEnvironmentInfo p_eifEnvironmentInfo) : base(p_eifEnvironmentInfo)
        {
            FileAssociations = new List<FileAssociationSetting>();
        }

        /// <summary>
        /// Gets the enumeration of file types associated with the client.
        /// </summary>
        /// <value>Ehe enumeration of file types associated with the client.</value>
        public IEnumerable<FileAssociationSetting> FileAssociations { get; }

        /// <summary>
        /// Gets whether or not file associations can be made.
        /// </summary>
        /// <value>Whether or not file associations can be made.</value>
        public bool CanAssociateFiles { get; } = UacUtil.IsElevated;

        /// <summary>
        /// Gets or sets whether the client should integrate with the explorer shell (right-click menu).
        /// </summary>
        /// <value>Whether the client should integrate with the explorer shell (right-click menu).</value>
        public bool AddShellExtensions
        {
            get => _addShellExtensions;
            set
            {
                SetPropertyIfChanged(ref _addShellExtensions, value, () => AddShellExtensions);
            }
        }

        /// <summary>
        /// Gets or sets whether the client should be associated with NXM URLs.
        /// </summary>
        /// <value>Whether the client should be associated with NXM URLs.</value>
        public bool AssociateNxmUrl
        {
            get => _associateNxmUrls;
            set
            {
                SetPropertyIfChanged(ref _associateNxmUrls, value, () => AssociateNxmUrl);
            }
        }

        public override string Title => "OS Settings";

        public override void Load()
        {
            foreach (var fasFileAssociation in FileAssociations)
            {
                fasFileAssociation.IsAssociated = IsAssociated(fasFileAssociation.Extension);
            }

            AddShellExtensions = ShellExtensionUtil.ReadShellExtensions();
            AssociateNxmUrl = UrlAssociationUtil.IsUrlAssociated("nxm");
        }

        public override bool Save()
        {
            if (UacUtil.IsElevated)
            {
                foreach (var fasFileAssociation in FileAssociations)
                {
                    if (fasFileAssociation.IsAssociated)
                    {
                        AssociateFile(fasFileAssociation);
                    }
                    else
                    {
                        UnassociateFile(fasFileAssociation);
                    }
                }

                if (AssociateNxmUrl)
                {
                    UrlAssociationUtil.AssociateUrl("nxm", "Nexus Mod");
                }
                else
                {
                    UrlAssociationUtil.UnassociateUrl("nxm");
                }

                if (AddShellExtensions)
                {
                    if (!ShellExtensionUtil.AddShellExtensions())
                    {
                        MessageBox.Show("Couldn't add shell extensions.\nCheck TraceLog for more info.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                    }
                }
                else
                {
                    if (!ShellExtensionUtil.RemoveShellExtensions())
                    {
                        MessageBox.Show("Couldn't remove shell extensions.\nCheck TraceLog for more info.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Add a possible client programme file association.
        /// </summary>
        /// <param name="extension">The extension to allow to be associated with the client.</param>
        /// <param name="description">A description of the file type.</param>
        public void AddFileAssociation(string extension, string description)
        {
            ((List<FileAssociationSetting>)FileAssociations).Add(new FileAssociationSetting(extension, description, IsAssociated(extension)));
        }

        /// <summary>
        /// Determines if the specified file type is associated with the client.
        /// </summary>
        /// <param name="extension">The extension of the file type for which it is to be determined
        /// whether it is associated with the client.</param>
        /// <returns><c>true</c> if the file type is associated with the client;
        /// <c>false</c> otherwise.</returns>
        protected bool IsAssociated(string extension)
        {
            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            var fileId = extension.TrimStart('.').ToUpperInvariant() + "_File_Type";

            var key = Registry.GetValue(@"HKEY_CLASSES_ROOT\" + extension, null, null) as string;

            return fileId.Equals(key);
        }

        /// <summary>
        /// Associates the specifed file type with the client.
        /// </summary>
        /// <param name="fileAssociation">The description of the file type association to create.</param>
        /// <exception cref="InvalidOperationException">Thrown if the user does not have sufficient priviledges
        /// to create the association.</exception>
        protected void AssociateFile(FileAssociationSetting fileAssociation)
        {
            if (!UacUtil.IsElevated)
            {
                throw new InvalidOperationException("You must have administrative privileges to change file associations.");
            }

            var fileId = fileAssociation.Extension.TrimStart('.').ToUpperInvariant() + "_File_Type";

            try
            {
                Registry.SetValue(@"HKEY_CLASSES_ROOT\" + fileAssociation.Extension, null, fileId);
                Registry.SetValue(@"HKEY_CLASSES_ROOT\" + fileId, null, fileAssociation.Description, RegistryValueKind.String);
                Registry.SetValue(@"HKEY_CLASSES_ROOT\" + fileId + @"\DefaultIcon", null, Application.ExecutablePath + ",0", RegistryValueKind.String);
                Registry.SetValue(@"HKEY_CLASSES_ROOT\" + fileId + @"\shell\open\command", null, "\"" + Application.ExecutablePath + "\" \"%1\"", RegistryValueKind.String);
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Something (usually your antivirus) is preventing the program from interacting with the registry and changing the file associations.");
            }
        }

        /// <summary>
        /// Removes the association of the specifed file type with the client.
        /// </summary>
        /// <param name="fileAssociation">The description of the file type association to remove.</param>
        /// <exception cref="InvalidOperationException">Thrown if the user does not have sufficient priviledges
        /// to remove the association.</exception>
        protected void UnassociateFile(FileAssociationSetting fileAssociation)
        {
            if (!UacUtil.IsElevated)
            {
                throw new InvalidOperationException("You must have administrative privileges to change file associations.");
            }

            var fileId = fileAssociation.Extension.TrimStart('.').ToUpperInvariant() + "_File_Type";
            var keys = Registry.ClassesRoot.GetSubKeyNames();

            if (Array.IndexOf(keys, fileId) != -1)
            {
                Registry.ClassesRoot.DeleteSubKeyTree(fileId);
                Registry.ClassesRoot.DeleteSubKeyTree(fileAssociation.Extension);
            }
        }
    }
}
