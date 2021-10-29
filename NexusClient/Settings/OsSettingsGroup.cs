namespace Nexus.Client.Settings
{
	using System;
	using System.Collections.Generic;
	using System.Windows.Forms;

	using Microsoft.Win32;

	using Util;

	public class OsSettingsGroup : SettingsGroup
	{
		private bool _associateNxmUrls = true;

		private bool _addShellExtensionZip = true;
		private bool _addShellExtensionRar = true;
		private bool _addShellExtension7z = true;

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
		/// Gets or sets whether the client should integrate with the explorer shell (right-click menu) for .zip files.
		/// </summary>
		public bool AddShellExtensionZip
		{
			get => _addShellExtensionZip;
			set
			{
				SetPropertyIfChanged(ref _addShellExtensionZip, value, () => AddShellExtensionZip);
			}
		}

		/// <summary>
		/// Gets or sets whether the client should integrate with the explorer shell (right-click menu) for .rar files.
		/// </summary>
		public bool AddShellExtensionRar
		{
			get => _addShellExtensionRar;
			set
			{
				SetPropertyIfChanged(ref _addShellExtensionRar, value, () => AddShellExtensionRar);
			}
		}

		/// <summary>
		/// Gets or sets whether the client should integrate with the explorer shell (right-click menu) for .7z files.
		/// </summary>
		public bool AddShellExtension7z
		{
			get => _addShellExtension7z;
			set
			{
				SetPropertyIfChanged(ref _addShellExtension7z, value, () => AddShellExtension7z);
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
			foreach (FileAssociationSetting fasFileAssociation in FileAssociations)
			{
				if (fasFileAssociation != null && !string.IsNullOrEmpty(fasFileAssociation.Extension))
					fasFileAssociation.IsAssociated = IsAssociated(fasFileAssociation.Extension);
			}

			foreach (string extension in ShellExtensionUtil.Extensions)
			{
				if (EnvironmentInfo.Settings.AddShellExtensions[extension] && !ShellExtensionUtil.ReadShellExtension(extension))
				{
					if (UacUtil.IsElevated)
					{
						var reply = MessageBox.Show($"Shell extension association for .{extension} has been removed by some other process.\n\n" +
													"Do you want to restore it?",
													"Association removed by another process", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

						if (reply == DialogResult.Yes)
						{
							if (!ShellExtensionUtil.AddShellExtension(extension))
							{
								MessageBox.Show($"Unable to enable shell extension for .{extension}.", "Unknown error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
							}
						}
						else
						{
							EnvironmentInfo.Settings.AddShellExtensions[extension] = false;
						}
					}
					else
					{
						var removeSetting = MessageBox.Show($"Shell extension association for .{extension} has been removed by some other process.\n" +
															$"If you want to restore it you have to run {CommonData.ModManagerName} as Administrator.\n\n" +
															"Do you want to disable the setting instead?",
							"Association removed by another process", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

						if (removeSetting == DialogResult.Yes)
						{
							EnvironmentInfo.Settings.AddShellExtensions[extension] = false;
						}
					}
				}
				else
				{
					EnvironmentInfo.Settings.AddShellExtensions[extension] = ShellExtensionUtil.ReadShellExtension(extension);
				}
			}

			AddShellExtensionZip = EnvironmentInfo.Settings.AddShellExtensions["zip"];
			AddShellExtensionRar = EnvironmentInfo.Settings.AddShellExtensions["rar"];
			AddShellExtension7z = EnvironmentInfo.Settings.AddShellExtensions["7z"];

			if (EnvironmentInfo.Settings.AssociateWithUrl && !UrlAssociationUtil.IsUrlAssociated("nxm"))
			{
				if (UacUtil.IsElevated)
				{
					var reply = MessageBox.Show("NXM URL association has been removed by some other process.\n\n" +
												"Do you want to restore it?",
						"Association removed by another process", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

					if (reply == DialogResult.Yes)
					{
						UrlAssociationUtil.AssociateUrl("nxm", "Nexus Mod");
					}
					else
					{
						EnvironmentInfo.Settings.AssociateWithUrl = false;
					}
				}
				else
				{
					var removeSetting = MessageBox.Show("NXM URL association has been removed by some other process.\n\n" +
														$"If you want to restore it you have to run {CommonData.ModManagerName} as Administrator.\n\n" +
														"Do you want to disable the setting instead?",
						"Association removed by another process", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

					if (removeSetting == DialogResult.Yes)
					{
						EnvironmentInfo.Settings.AssociateWithUrl = false;
					}
				}
			}

			AssociateNxmUrl = UrlAssociationUtil.IsUrlAssociated("nxm");
		}

		public override bool Save()
		{
			EnvironmentInfo.Settings.AssociateWithUrl = _associateNxmUrls;
			EnvironmentInfo.Settings.AddShellExtensions["zip"] = _addShellExtensionZip;
			EnvironmentInfo.Settings.AddShellExtensions["rar"] = _addShellExtensionRar;
			EnvironmentInfo.Settings.AddShellExtensions["7z"] = _addShellExtension7z;

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

				foreach (var extension in ShellExtensionUtil.Extensions)
				{
					if (EnvironmentInfo.Settings.AddShellExtensions[extension])
					{
						if (!ShellExtensionUtil.AddShellExtension(extension))
						{
							MessageBox.Show($"Couldn't add shell extension for .{extension} files.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
						}
					}
					else
					{
						if (!ShellExtensionUtil.RemoveShellExtension(extension))
						{
							MessageBox.Show($"Couldn't remove shell extension for .{extension} files.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
						}
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
			try
			{
				if (!extension.StartsWith("."))
				{
					extension = "." + extension;
				}

				string fileId = extension.TrimStart('.').ToUpperInvariant() + "_File_Type";

				string key = Registry.GetValue(@"HKEY_CLASSES_ROOT\" + extension, null, null) as string;

				return fileId.Equals(key);
			}
			catch (NullReferenceException)
			{
				return false;
			}
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
