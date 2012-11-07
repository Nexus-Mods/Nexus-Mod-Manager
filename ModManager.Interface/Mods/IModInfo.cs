using System;
using System.Xml;
using Nexus.Client.Util;

namespace Nexus.Client.Mods
{
	/// <summary>
	/// A contract specifying an object that contains information about a mod.
	/// </summary>
	public interface IModInfo
	{
		#region Properties

		/// <summary>
		/// Gets or sets the Id of the mod.
		/// </summary>
		/// <remarks>The id of the mod</remarks>
		string Id { get; }

		/// <summary>
		/// Gets or sets the name of the mod.
		/// </summary>
		/// <value>The name of the mod.</value>
		string ModName { get; }

		/// <summary>
		/// Gets or sets the human readable form of the mod's version.
		/// </summary>
		/// <value>The human readable form of the mod's version.</value>
		string HumanReadableVersion { get; }

		/// <summary>
		/// Gets or sets the last known mod version.
		/// </summary>
		/// <value>The the last known mod version.</value>
		string LastKnownVersion { get; }

		/// <summary>
		/// Gets or sets the version of the mod.
		/// </summary>
		/// <value>The version of the mod.</value>
		Version MachineVersion { get; }

		/// <summary>
		/// Gets or sets the author of the mod.
		/// </summary>
		/// <value>The author of the mod.</value>
		string Author { get; }

		/// <summary>
		/// Gets or sets the description of the mod.
		/// </summary>
		/// <value>The description of the mod.</value>
		string Description { get; }

		 /// <summary>
		/// Gets or sets the install date of the mod.
		/// </summary>
		/// <value>The install date of the mod.</value>
		string InstallDate { get; }

		/// <summary>
		/// Gets or sets the website of the mod.
		/// </summary>
		/// <value>The website of the mod.</value>
		Uri Website { get; }

		/// <summary>
		/// Gets or sets the mod's screenshot.
		/// </summary>
		/// <value>The mod's screenshot.</value>
		ExtendedImage Screenshot { get; }

		#endregion

		/// <summary>
		/// Updates the object's proerties to the values of the
		/// given <see cref="IModInfo"/>.
		/// </summary>
		/// <param name="p_mifInfo">The <see cref="IModInfo"/> whose values
		/// are to be used to update this object's properties.</param>
		/// <param name="p_booOverwriteAllValues">Whether to overwrite the current info values,
		/// or just the empty ones.</param>
		void UpdateInfo(IModInfo p_mifInfo, bool p_booOverwriteAllValues);
	}
}
