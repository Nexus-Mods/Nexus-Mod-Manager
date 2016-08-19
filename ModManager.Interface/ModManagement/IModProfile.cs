using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// A contract specifying an object that contains information about a mods profile.
	/// </summary>
	public interface IModProfile
	{
		#region Properties
		/// <summary>
		/// Gets or sets the Id of the profile.
		/// </summary>
		/// <remarks>The id of the profile</remarks>
		string Id { get; }

		/// <summary>
		/// Gets or sets the name of the profile.
		/// </summary>
		/// <value>The name of the profile.</value>
		string Name { get; set; }

		/// <summary>
		/// Gets or sets the GameModeId of the profile.
		/// </summary>
		/// <remarks>The GameModeId of the profile</remarks>
		string GameModeId { get; set; }

		/// <summary>
		/// Gets or sets whether this profile is the default one.
		/// </summary>
		/// <remarks>Whether this profile is the default one.</remarks>
		bool IsDefault { get; set; }

		/// <summary>
		/// Gets or sets the number of active mods in this profile.
		/// </summary>
		/// <remarks>The number of active mods in this profile.</remarks>
		Int32 ModCount { get; }

		/// <summary>
		/// Gets or sets the profile loadorder (if present).
		/// </summary>
		/// <value>The profile loadorder (if present).</value>
		Dictionary<string, string> LoadOrder { get; }

		/// <summary>
		/// Gets or sets active mod link list.
		/// </summary>
		/// <value>The active mod link list.</value>
		List<IVirtualModLink> ModFileList { get; }

		/// <summary>
		/// Gets or sets active mod list.
		/// </summary>
		/// <value>The active mod list.</value>
		List<IVirtualModInfo> ModList { get; }

		/// <summary>
		/// Gets or sets the Online Id of the profile.
		/// </summary>
		/// <remarks>The Online Id of the profile</remarks>
		string OnlineID { get; }

		/// <summary>
		/// Gets or sets the Online Name of the profile.
		/// </summary>
		/// <remarks>The Online Name of the profile</remarks>
		string OnlineName { get; }

		/// <summary>
		/// Gets or sets the Backup Date.
		/// </summary>
		/// <remarks>The BackupDate of the profile</remarks>
		string BackupDate { get; }

		/// <summary>
		/// Gets or sets the Version of the profile.
		/// </summary>
		/// <remarks>The Version of the profile</remarks>
		int Version { get; }

		/// <summary>
		/// Gets or sets the Author of the profile.
		/// </summary>
		/// <remarks>The Author of the profile</remarks>
		string Author { get; }

		/// <summary>
		/// Gets or sets the Works With Saves flag of the profile.
		/// </summary>
		/// <remarks>The Works With Saves flag of the profile</remarks>
		int WorksWithSaves { get; }

		/// <summary>
		/// Gets or sets the Screenshot of the profile.
		/// </summary>
		/// <remarks>The Screenshot of the profile</remarks>
		string Screenshot { get; }

		/// <summary>
		/// Gets whether the profile is online (backed or shared).
		/// </summary>
		/// <remarks>Whether the profile is online (backed or shared)</remarks>
		bool IsOnline { get; }

		/// <summary>
		/// Gets or sets the Shared flag of the profile.
		/// </summary>
		/// <remarks>The Shared flag of the profile</remarks>
		bool IsShared { get; }

		/// <summary>
		/// Gets or sets the Edited flag of the profile.
		/// </summary>
		/// <remarks>The Shared flag of the profile</remarks>
		bool IsEdited { get; set; }

		void UpdateLists(List<IVirtualModLink> p_lstVirtualModLink, List<IVirtualModInfo> p_lstVirtualModList);
		#endregion
	}
}
