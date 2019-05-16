namespace Nexus.Client.ModRepositories
{
    using System;
    using System.Diagnostics;
    using System.Text.RegularExpressions;
    using Mods;
    using Pathoschild.FluentNexus.Models;
    using Util;

    /// <summary>
	/// Describes the metadata of a mod in a repository.
	/// </summary>
	public class ModInfo : IModInfo
	{
		#region IModInfo Members

		#region Properties

		/// <summary>
		/// Gets or sets the Id of the mod.
		/// </summary>
		/// <remarks>The id of the mod.</remarks>
		public string Id { get; set; }

		/// <summary>
		/// Gets or sets the DownloadId of the mod.
		/// </summary>
		/// <remarks>The DownloadId of the mod.</remarks>
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
		public int CategoryId { get; set; }

		/// <summary>
		/// Gets the user custom CategoryId of the mod.
		/// </summary>
		/// <value>The user custom CategoryId of the mod.</value>
		public int CustomCategoryId { get; set; }

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
		/// <value>Whether the user wants to be warned about new versions.</value>
		public bool UpdateWarningEnabled{ get; set; }

		/// <summary>
		/// Gets or sets whether the user wants for the program to check for this mod's update and perform the automatic rename.
		/// </summary>
		/// <value>Whether the user wants for the program to check for this mod's update and perform the automatic rename.</value>
		public bool UpdateChecksEnabled { get; set; }

		/// <summary>
		/// Gets or sets the mod's current place in the mod load order
		/// </summary>
		/// <value>The mod's place in the load order</value>
		public int PlaceInModLoadOrder { get; set; }

        /// <summary>
        /// Gets or sets the mod's new place in the mod load order.
        /// </summary>
        /// <value>The mod's new place in the load order.</value>
        public int NewPlaceInModLoadOrder { get; set; }

		#endregion

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ModInfo()
		{
		}

        /// <summary>
        /// Creates a <see cref="ModInfo"/> from a <see cref="Mod"/>.
        /// </summary>
        /// <param name="result">Mod to get info from.</param>
        public ModInfo(Mod result)
        {
            bool? endorsementState = null;

            if (result.Endorsement == null || result.Endorsement.EndorseStatus == EndorsementStatus.Abstained)
            {
                endorsementState = false;
            }
            else if (result.Endorsement.EndorseStatus == EndorsementStatus.Endorsed)
            {
                endorsementState = true;
            }

            var properVersion = FindProperVersion(result.Version);

			// TODO: Figure out if the null values are required for NMM to work...
			SetAllInfo(true,
                result.ModID.ToString(),
                null,
                result.Name,
                null,
                result.Version,
                result.Version,
                endorsementState,
                properVersion,
                result.Author,
                result.CategoryID,
                -1,
                result.Description,
                null,
                new Uri($"https://www.nexusmods.com/{GameDomainTranslator.DetermineGameDomain(result.DomainName)}/mods/{result.ModID}"),
                null,
                false,
                false
                );
        }

		/// <summary>
		/// The copy constructor.
		/// </summary>
		/// <param name="original">The mod info to copy.</param>
		public ModInfo(IModInfo original)
		{
			if (original != null)
            {
                SetAllInfo(true, original.Id, original.DownloadId, original.ModName, original.FileName, original.HumanReadableVersion, original.LastKnownVersion, original.IsEndorsed, original.MachineVersion, original.Author, original.CategoryId, original.CustomCategoryId, original.Description, original.InstallDate, original.Website, original.Screenshot, original.UpdateWarningEnabled, original.UpdateChecksEnabled);
            }
        }

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="id">The id of the mod.</param>
		/// <param name="downloadId">The DownloadId of the mod.</param>
		/// <param name="modName">The name of the mod.</param>
		/// <param name="fileName">The filename of the mod?</param>
		/// <param name="humanReadableVersion">The human readable form of the mod's version.</param>
		/// <param name="lastKnownVersion">The last known mod version.</param>
		/// <param name="isEndorsed">The Endorsement state of the mod.</param>
		/// <param name="machineVersion">The version of the mod.</param>
		/// <param name="author">The author of the mod.</param>
		/// <param name="categoryId">The category of the mod.</param>
		/// <param name="customCategoryId">The custom category of the mod.</param>
		/// <param name="description">The description of the mod.</param>
		/// <param name="installDate">The install date of the mod.</param>
		/// <param name="website">The website of the mod.</param>
		/// <param name="screenshot">The mod's screenshot.</param>
		/// <param name="updateWarningEnabled">Whether update warning is enabled for this mod.</param>
		/// <param name="updateChecksEnabled">Whether update checks are enabled for this mod.</param>
		public ModInfo(string id, string downloadId, string modName, string fileName, string humanReadableVersion, string lastKnownVersion, bool? isEndorsed, Version machineVersion, string author, int categoryId, int customCategoryId, string description, string installDate, Uri website, ExtendedImage screenshot, bool updateWarningEnabled, bool updateChecksEnabled)
		{
			SetAllInfo(true, id, downloadId, modName, fileName, humanReadableVersion, lastKnownVersion, isEndorsed, machineVersion, author, categoryId, customCategoryId, description, installDate, website, screenshot, updateWarningEnabled, updateChecksEnabled);
		}

        #endregion

        /// <summary>
        /// Sets all of the properties of the object.
        /// </summary>
        /// <param name="overwriteAllValues">Whether to overwrite the current info values,
        /// or just the empty ones.</param>
        /// <param name="id">The id of the mod.</param>
        /// <param name="downloadId">The DownloadId of the mod.</param>
        /// <param name="modName">The name of the mod.</param>
        /// <param name="fileName">The filename of the mod?</param>
        /// <param name="humanReadableVersion">The human readable form of the mod's version.</param>
        /// <param name="lastKnownVersion">The last known mod version.</param>
        /// <param name="isEndorsed">The Endorsement state of the mod.</param>
        /// <param name="machineVersion">The version of the mod.</param>
        /// <param name="author">The author of the mod.</param>
        /// <param name="categoryId">The category of the mod.</param>
        /// <param name="customCategoryId">The custom category of the mod.</param>
        /// <param name="description">The description of the mod.</param>
        /// <param name="installDate">The install date of the mod.</param>
        /// <param name="website">The website of the mod.</param>
        /// <param name="screenshot">The mod's screenshot.</param>
        /// <param name="updateWarningEnabled">Whether update warning is enabled for this mod.</param>
        /// <param name="updateChecksEnabled">Whether update checks are enabled for this mod.</param>
        protected void SetAllInfo(bool overwriteAllValues, string id, string downloadId, string modName, string fileName, string humanReadableVersion, string lastKnownVersion, bool? isEndorsed, Version machineVersion, string author, int categoryId, int customCategoryId, string description, string installDate, Uri website, ExtendedImage screenshot, bool updateWarningEnabled, bool updateChecksEnabled)
		{
			if (overwriteAllValues || string.IsNullOrEmpty(Id))
            {
                Id = id;
            }

            if (overwriteAllValues || string.IsNullOrEmpty(DownloadId))
            {
                DownloadId = downloadId;
            }

            if (overwriteAllValues || string.IsNullOrEmpty(ModName))
            {
                ModName = modName;
            }

            if (overwriteAllValues || string.IsNullOrEmpty(FileName))
            {
                FileName = fileName;
            }

            if (overwriteAllValues || string.IsNullOrEmpty(HumanReadableVersion))
            {
                HumanReadableVersion = humanReadableVersion;
            }

            if (overwriteAllValues || string.IsNullOrEmpty(LastKnownVersion))
            {
                LastKnownVersion = lastKnownVersion;
            }

            if (overwriteAllValues || (IsEndorsed != isEndorsed))
            {
                IsEndorsed = isEndorsed;
            }

            if (overwriteAllValues || (MachineVersion == null))
            {
                MachineVersion = machineVersion;
            }

            if (overwriteAllValues || string.IsNullOrEmpty(Author))
            {
                Author = author;
            }

            if (overwriteAllValues || (CategoryId != categoryId))
            {
                CategoryId = categoryId;
            }

            if (overwriteAllValues || (CustomCategoryId != customCategoryId))
            {
                CustomCategoryId = customCategoryId;
            }

            if (overwriteAllValues || string.IsNullOrEmpty(Description))
            {
                Description = description;
            }

            if (overwriteAllValues || string.IsNullOrEmpty(InstallDate))
            {
                InstallDate = installDate;
            }

            if (overwriteAllValues || (Website == null))
            {
                Website = website;
            }

            if (overwriteAllValues || (UpdateWarningEnabled != updateWarningEnabled))
            {
                UpdateWarningEnabled = updateWarningEnabled;
            }

            if (overwriteAllValues || (UpdateChecksEnabled != updateChecksEnabled))
            {
                UpdateChecksEnabled = updateChecksEnabled;
            }
        }

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
			SetAllInfo(overwriteAllValues == true, modInfo.Id, modInfo.DownloadId, modInfo.ModName, modInfo.FileName, modInfo.HumanReadableVersion, modInfo.LastKnownVersion, modInfo.IsEndorsed, modInfo.MachineVersion, modInfo.Author, modInfo.CategoryId, modInfo.CustomCategoryId, modInfo.Description, modInfo.InstallDate, modInfo.Website, modInfo.Screenshot, modInfo.UpdateWarningEnabled, modInfo.UpdateChecksEnabled);
		}

        /// <summary>
        /// Figures out a clean version number from the real mod version.
        /// </summary>
        /// <remarks>Nexus allows the craziest things for version numbers.</remarks>
        /// <param name="input">The version number from Nexus.</param>
        /// <returns>A cleaned up <see cref="Version"/>.</returns>
        private Version FindProperVersion(string input)
        {
            var properVersion = new Version(0, 0);

            // Attempt to simply clean out the non-numbers/non-periods from the version.
            var version = Regex.Replace(input, "[^.0-9]", "");

            // If a version ends with a period, remove it.
            if (version.EndsWith("."))
            {
                version = version.TrimEnd('.');
            }

            try
            {
                // We've got something to work with.
                if (!string.IsNullOrEmpty(version))
                {
                    // Find the index of the first period.
                    var crazyVersioningCheck = version.IndexOf(".");

                    if (crazyVersioningCheck > 0)
                    {
                        // It's not the first character, let's go for it!
                        properVersion = new Version(version);
                    }
                    else if (crazyVersioningCheck == 0)
                    {
                        // It's the first character, we'll add a zero at the start.
                        properVersion = new Version("0" + version);
                    }
                    else
                    {
                        // There's no period, we'll add ".0" to the end to make a valid version.
                        properVersion = new Version(version + ".0");
                    }
                }
            }
            catch (Exception e)
            {
                Trace.TraceError($"Could not determine version from \"{input}\", falling back to default version.");
                TraceUtil.TraceException(e);

                return properVersion;
            }

            return properVersion;
        }
	}
}
