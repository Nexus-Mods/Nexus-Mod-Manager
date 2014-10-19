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

		void UpdateLists(List<IVirtualModLink> p_lstVirtualModLink, List<IVirtualModInfo> p_lstVirtualModList);
		#endregion
	}
}
