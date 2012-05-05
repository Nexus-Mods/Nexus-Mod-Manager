using System;
using Nexus.Client.Mods;
using Nexus.Client.Util;

namespace Nexus.Client.ModRepositories
{
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
		/// Gets or sets the name of the mod.
		/// </summary>
		/// <value>The name of the mod.</value>
		public string ModName { get; set; }

		/// <summary>
		/// Gets or sets the human readable form of the mod's version.
		/// </summary>
		/// <value>The human readable form of the mod's version.</value>
		public string HumanReadableVersion { get; set; }

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
		/// Gets or sets the description of the mod.
		/// </summary>
		/// <value>The description of the mod.</value>
		public string Description { get; set; }

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

		#endregion

		#endregion

		#region Constructors

		/// <summary>
		/// The default cosntructor.
		/// </summary>
		public ModInfo()
		{
		}

		/// <summary>
		/// The copy cosntructor.
		/// </summary>
		/// <param name="p_mifCopy">The mod info to copy.</param>
		public ModInfo(IModInfo p_mifCopy)
		{
			if (p_mifCopy != null)
				SetAllInfo(true, p_mifCopy.Id, p_mifCopy.ModName, p_mifCopy.HumanReadableVersion, p_mifCopy.MachineVersion, p_mifCopy.Author, p_mifCopy.Description, p_mifCopy.Website, p_mifCopy.Screenshot);
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strId">The id of the mod.</param>
		/// <param name="p_strModName">The name of the mod.</param>
		/// <param name="p_strHumanReadableVersion">The human readable form of the mod's version.</param>
		/// <param name="p_verMachineVersion">The version of the mod.</param>
		/// <param name="p_strAuthor">The author of the mod.</param>
		/// <param name="p_strDescription">The description of the mod.</param>
		/// <param name="p_uriWebsite">The website of the mod.</param>
		/// <param name="p_eimScreenshot">The mod's screenshot.</param>
		public ModInfo(string p_strId, string p_strModName, string p_strHumanReadableVersion, Version p_verMachineVersion, string p_strAuthor, string p_strDescription, Uri p_uriWebsite, ExtendedImage p_eimScreenshot)
		{
			SetAllInfo(true, p_strId, p_strModName, p_strHumanReadableVersion, p_verMachineVersion, p_strAuthor, p_strDescription, p_uriWebsite, p_eimScreenshot);
		}

		#endregion

		/// <summary>
		/// Sets all of the properties of the object.
		/// </summary>
		/// <param name="p_booOverwriteAllValues">Whether to overwrite the current info values,
		/// or just the empty ones.</param>
		/// <param name="p_strId">The id of the mod.</param>
		/// <param name="p_strModName">The name of the mod.</param>
		/// <param name="p_strHumanReadableVersion">The human readable form of the mod's version.</param>
		/// <param name="p_verMachineVersion">The version of the mod.</param>
		/// <param name="p_strAuthor">The author of the mod.</param>
		/// <param name="p_strDescription">The description of the mod.</param>
		/// <param name="p_uriWebsite">The website of the mod.</param>
		/// <param name="p_eimScreenshot">The mod's screenshot.</param>
		protected void SetAllInfo(bool p_booOverwriteAllValues, string p_strId, string p_strModName, string p_strHumanReadableVersion, Version p_verMachineVersion, string p_strAuthor, string p_strDescription, Uri p_uriWebsite, ExtendedImage p_eimScreenshot)
		{
			if (p_booOverwriteAllValues || String.IsNullOrEmpty(Id))
				Id = p_strId;
			if (p_booOverwriteAllValues || String.IsNullOrEmpty(ModName))
				ModName = p_strModName;
			if (p_booOverwriteAllValues || String.IsNullOrEmpty(HumanReadableVersion))
				HumanReadableVersion = p_strHumanReadableVersion;
			if (p_booOverwriteAllValues || (MachineVersion == null))
				MachineVersion = p_verMachineVersion;
			if (p_booOverwriteAllValues || String.IsNullOrEmpty(Author))
				Author = p_strAuthor;
			if (p_booOverwriteAllValues || String.IsNullOrEmpty(Description))
				Description = p_strDescription;
			if (p_booOverwriteAllValues || (Website == null))
				Website = p_uriWebsite;
			if (p_booOverwriteAllValues || (Screenshot == null))
				Screenshot = p_eimScreenshot;
		}

		/// <summary>
		/// Updates the object's proerties to the values of the
		/// given <see cref="IModInfo"/>.
		/// </summary>
		/// <param name="p_mifInfo">The <see cref="IModInfo"/> whose values
		/// are to be used to update this object's properties.</param>
		/// <param name="p_booOverwriteAllValues">Whether to overwrite the current info values,
		/// or just the empty ones.</param>
		public void UpdateInfo(IModInfo p_mifInfo, bool p_booOverwriteAllValues)
		{
			SetAllInfo(p_booOverwriteAllValues, p_mifInfo.Id, p_mifInfo.ModName, p_mifInfo.HumanReadableVersion, p_mifInfo.MachineVersion, p_mifInfo.Author, p_mifInfo.Description, p_mifInfo.Website, p_mifInfo.Screenshot);
		}
	}
}
