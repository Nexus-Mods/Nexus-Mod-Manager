using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.InstallationLog
{
	public partial class InstallLog
	{
		/// <summary>
		/// A dummy mod that can be used as a placeholder.
		/// </summary>
		public class DummyMod : ObservableObject, IMod
		{
			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_strName">The name of the dummy mod.</param>
			/// <param name="p_strFileName">The filename of the dummy mod.</param>
			public DummyMod(string p_strName, string p_strFileName)
			{
				ModName = p_strName;
				Filename = p_strFileName;
			}

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_strName">The name of the dummy mod.</param>
			/// <param name="p_strFileName">The filename of the dummy mod.</param>
			/// <param name="p_strHumanReadableVersion">The human readable form of the mod's version.</param>
			/// <param name="p_strLastKnownVersion">The the last known mod version.</param>
			/// <param name="p_verMachineVersion">The version of the mod.</param>
			/// <param name="p_strInstallDate">The install date of the mod.</param>
			public DummyMod(string p_strName, string p_strFileName, Version p_verMachineVersion, string p_strHumanReadableVersion, string p_strLastKnownVersion, string p_strInstallDate)
			{
				ModName = p_strName;
				Filename = p_strFileName;
				MachineVersion = p_verMachineVersion;
				HumanReadableVersion = p_strHumanReadableVersion;
				LastKnownVersion = p_strLastKnownVersion;
				InstallDate = p_strInstallDate;
			}

			#endregion

			#region IMod Members

			/// <summary>
			/// Gets the number of steps that need to be performed to put the Mod into read-only mode.
			/// </summary>
			/// <value>The number of steps that need to be performed to put the Mod into read-only mode.</value>
			public Int32 ReadOnlyInitStepCount
			{
				get
				{
					return 0;
				}
			}

			/// <summary>
			/// Gets the filename of the mod.
			/// </summary>
			/// <value>The filename of the mod.</value>
			public string Filename { get; private set; }

			/// <summary>
			/// Gets the path to the mod archive.
			/// </summary>
			/// <value>The path to the mod archive.</value>
			public string ModArchivePath 
			{ 
				get
				{
					return Filename;
				}
			}

			/// <summary>
			/// Gets the format of the mod.
			/// </summary>
			/// <value>The format of the mod.</value>
			public IModFormat Format
			{
				get
				{
					return null;
				}
			}

			/// <summary>
			/// Gets the internal path to the screenshot.
			/// </summary>
			/// <remarks>
			/// This property return a path that can be passed to the <see cref="GetFile(string)"/>
			/// method.
			/// </remarks>
			/// <value>The internal path to the screenshot.</value>
			public string ScreenshotPath
			{
				get
				{
					return null;
				}
			}

			#region Read Transactions

			/// <summary>
			/// Raised to update listeners on the progress of the read-only initialization.
			/// </summary>
			public event CancelProgressEventHandler ReadOnlyInitProgressUpdated = delegate { };

			/// <summary>
			/// Starts a read-only transaction.
			/// </summary>
			/// <remarks>
			/// This puts the Mod into read-only mode.
			/// 
			/// Read-only mode can greatly increase the speed at which multiple file are extracted.
			/// </remarks>
			public void BeginReadOnlyTransaction(FileUtil p_futFileUtil)
			{
				ReadOnlyInitProgressUpdated(this, new CancelProgressEventArgs(1f));
			}

			/// <summary>
			/// Ends a read-only transaction.
			/// </summary>
			/// <remarks>
			/// This takes the Mod out of read-only mode.
			/// 
			/// Read-only mode can greatly increase the speed at which multiple file are extracted.
			/// </remarks>
			public void EndReadOnlyTransaction()
			{
			}

			#endregion

			#region File Management

			/// <summary>
			/// Retrieves the specified file from the mod.
			/// </summary>
			/// <param name="p_strFile">The file to retrieve.</param>
			/// <returns>The requested file data.</returns>
			/// <exception cref="FileNotFoundException">Thrown if the specified file
			/// is not in the mod.</exception>
			public byte[] GetFile(string p_strFile)
			{
				throw new FileNotFoundException("This mod contains no files.");
			}

            /// <summary>
			/// Retrieves a FileStream of the specified file from the mod.
			/// </summary>
			/// <param name="p_strFile">The file to retrieve stream for.</param>
			/// <returns>The requested file stream.</returns>
            public FileStream GetFileStream(string p_strFile)
            {
                throw new FileNotFoundException("This mod contains no files.");
            }

			/// <summary>
			/// Retrieves the list of files in this Mod.
			/// </summary>
			/// <returns>The list of files in this Mod.</returns>
			public List<string> GetFileList()
			{
				return new List<string>();
			}

			/// <summary>
			/// Determines if last known version is the same as the current version.
			/// </summary>
			/// <returns><c>true</c> if the versions are the same;
			/// <c>false</c> otherwise.</returns>
			public bool IsMatchingVersion()
			{
				return true;
			}

			/// <summary>
			/// Retrieves the list of all files in the specified Mod folder.
			/// </summary>
			/// <param name="p_strFolderPath">The Mod folder whose file list is to be retrieved.</param>
			/// <param name="p_booRecurse">Whether to return files that are in subdirectories of the given directory.</param>
			/// <returns>The list of all files in the specified Mod folder.</returns>
			public List<string> GetFileList(string p_strFolderPath, bool p_booRecurse)
			{
				return new List<string>();
			}

			#endregion

			#endregion

			#region IModInfo Members

			#region Properties

			/// <summary>
			/// Gets or sets the Id of the mod.
			/// </summary>
			/// <remarks>The id of the mod</remarks>
			public string Id { get; set; }

			/// <summary>
			/// Gets or sets the DownloadId of the mod.
			/// </summary>
			/// <remarks>The DownloadId of the mod</remarks>
			public string DownloadId { get; set; }

            /// <summary>
            /// Gets or sets the Download date of the mod.
            /// </summary>
            /// <remarks>The Download date of the mod</remarks>
            public DateTime? DownloadDate { get; set; }

            /// <summary>
            /// Gets or sets the name of the mod.
            /// </summary>
            /// <value>The name of the mod.</value>
            public string ModName { get; set; }

			/// <summary>
			/// Gets or sets the filename of the mod.
			/// </summary>
			/// <value>The filename of the mod.</value>
			public string FileName { get; set; }

			/// <summary>
			/// Gets or sets the human readable form of the mod's version.
			/// </summary>
			/// <value>The human readable form of the mod's version.</value>
			public string HumanReadableVersion { get; set; }

			/// <summary>
			/// Gets or sets the last known mod version.
			/// </summary>
			/// <value>The last known mod version.</value>
			public string LastKnownVersion { get; set; }

			/// <summary>
			/// Gets or sets the Endorsement state of the mod.
			/// </summary>
			/// <value>The Endorsement state of the mod.</value>
			public bool? IsEndorsed { get; set; }

			/// <summary>
			/// Gets or sets the version of the mod.
			/// </summary>
			/// <value>The version of the mod.</value>
			public Version MachineVersion { get; set; }

			/// <summary>
			/// Gets or sets the author of the mod.
			/// </summary>
			/// <value>The author of the mod.</value>
			public string Author { get; set; }

			/// <summary>
			/// Gets the CategoryId of the mod.
			/// </summary>
			/// <value>The CategoryId of the mod.</value>
			public Int32 CategoryId { get; set; }

			/// <summary>
			/// Gets the user custom CategoryId of the mod.
			/// </summary>
			/// <value>The user custom CategoryId of the mod.</value>
			public Int32 CustomCategoryId { get; set; }

			/// <summary>
			/// Gets or sets the description of the mod.
			/// </summary>
			/// <value>The description of the mod.</value>
			public string Description { get; set; }

			/// <summary>
			/// Gets or sets the install date of the mod.
			/// </summary>
			/// <value>The install date of the mod.</value>
			public string InstallDate { get; set; }

			/// <summary>
			/// Gets or sets the website of the mod.
			/// </summary>
			/// <value>The website of the mod.</value>
			public Uri Website { get; set; }

			/// <summary>
			/// Gets or sets the mod's screenshot.
			/// </summary>
			/// <value>The mod's screenshot.</value>
			public ExtendedImage Screenshot { get; set; }

			/// <summary>
			/// Gets or sets whether the user wants to be warned about new versions.
			/// </summary>
			/// <value>Whether the user wants to be warned about new versions</value>
			public bool UpdateWarningEnabled { get; set; }

			/// <summary>
			/// Gets or sets whether the user wants for the program to check for this mod's update and perform the automatic rename.
			/// </summary>
			/// <value>Whether the user wants for the program to check for this mod's update and perform the automatic rename.</value>
			public bool UpdateChecksEnabled { get; set; }

			public int PlaceInModLoadOrder { get; set; }

            public int NewPlaceInModLoadOrder { get; set; }

			#endregion

			/// <summary>
			/// Updates the object's proerties to the values of the
			/// given <see cref="IModInfo"/>.
			/// </summary>
			/// <param name="modInfo">The <see cref="IModInfo"/> whose values
			/// are to be used to update this object's properties.</param>
			/// <param name="overwriteAllValues">Whether to overwrite the current info values,
			/// or just the empty ones.</param>
			public void UpdateInfo(IModInfo modInfo, bool? overwriteAllValues)
			{
				if ((overwriteAllValues == true) || String.IsNullOrEmpty(Id))
					Id = modInfo.Id;
				if ((overwriteAllValues == true) || String.IsNullOrEmpty(DownloadId))
					DownloadId = modInfo.DownloadId;
				if ((overwriteAllValues == true) || String.IsNullOrEmpty(ModName))
					ModName = modInfo.ModName;
				if ((overwriteAllValues == true) || String.IsNullOrEmpty(HumanReadableVersion))
					HumanReadableVersion = modInfo.HumanReadableVersion;
				if ((overwriteAllValues == true) || String.IsNullOrEmpty(LastKnownVersion))
					LastKnownVersion = modInfo.LastKnownVersion;
				if ((overwriteAllValues == true) || (IsEndorsed != modInfo.IsEndorsed))
					IsEndorsed = modInfo.IsEndorsed;
				if ((overwriteAllValues == true) || (MachineVersion == null))
					MachineVersion = modInfo.MachineVersion;
				if ((overwriteAllValues == true) || String.IsNullOrEmpty(Author))
					Author = modInfo.Author;
				if ((overwriteAllValues == true) || (CategoryId != modInfo.CategoryId))
					CategoryId = modInfo.CategoryId;
				if ((overwriteAllValues == true) || (CustomCategoryId != modInfo.CustomCategoryId))
					CustomCategoryId = modInfo.CustomCategoryId;
				if ((overwriteAllValues == true) || String.IsNullOrEmpty(Description))
					Description = modInfo.Description;
				if ((overwriteAllValues == true) || String.IsNullOrEmpty(InstallDate))
					InstallDate = modInfo.InstallDate;
				if ((overwriteAllValues == true) || (Website == null))
					Website = modInfo.Website;
				if ((overwriteAllValues == true) || (Screenshot == null))
					Screenshot = modInfo.Screenshot;
				if ((overwriteAllValues == true) || (UpdateWarningEnabled != modInfo.UpdateWarningEnabled))
					UpdateWarningEnabled = modInfo.UpdateWarningEnabled;
				if ((overwriteAllValues == true) || (UpdateChecksEnabled != modInfo.UpdateChecksEnabled))
					UpdateChecksEnabled = modInfo.UpdateChecksEnabled;
			}

			#endregion

			#region IScriptedMod Members

			/// <summary>
			/// Gets whether the mod has a custom install script.
			/// </summary>
			/// <value>Whether the mod has a custom install script.</value>
			public bool HasInstallScript
			{
				get
				{
					return false;
				}
			}

			/// <summary>
			/// Gets or sets the mod's install script.
			/// </summary>
			/// <value>The mod's install script.</value>
			public IScript InstallScript
			{
				get
				{
					return null;
				}
				set
				{
				}
			}

			#endregion
		}
	}
}