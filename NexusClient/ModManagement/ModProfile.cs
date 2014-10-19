using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nexus.Client.ModManagement
{
	public partial class ModProfile : IModProfile
	{
		private Int32 m_intModCount = 0;

		#region Properties
		/// <summary>
		/// Gets or sets the Id of the profile.
		/// </summary>
		/// <remarks>The id of the profile</remarks>
		public string Id { get; private set; }

		/// <summary>
		/// Gets or sets the name of the profile.
		/// </summary>
		/// <value>The name of the profile.</value>
		public string Name { get; set; }

		/// <summary>
		/// Gets or sets the GameModeId of the profile.
		/// </summary>
		/// <remarks>The GameModeId of the profile</remarks>
		public string GameModeId { get; set; }

		/// <summary>
		/// Gets or sets whether this profile is the default one.
		/// </summary>
		/// <remarks>Whether this profile is the default one.</remarks>
		public bool IsDefault { get; set; }

		/// <summary>
		/// Gets or sets the number of active mods in this profile.
		/// </summary>
		/// <remarks>The number of active mods in this profile.</remarks>
		public Int32 ModCount 
		{
			get
			{
				if ((ModList != null) && (ModList.Count > 0))
				{
					return ModList.Count();
				}
				else
					return m_intModCount;
			}
			set
			{
				m_intModCount = value;
			}
		}

		/// <summary>
		/// Gets or sets the profile loadorder (if present).
		/// </summary>
		/// <value>The profile loadorder (if present).</value>
		public Dictionary<string, string> LoadOrder { get; private set; }

		/// <summary>
		/// Gets or sets active mod list.
		/// </summary>
		/// <value>The active mod list.</value>
		public List<IVirtualModLink> ModFileList { get; private set; }

		/// <summary>
		/// Gets or sets active mod list.
		/// </summary>
		/// <value>The active mod list.</value>
		public List<IVirtualModInfo> ModList { get; private set; }

		#endregion

		#region Constructors

		public ModProfile(string p_strName, string p_strGameModeId, Dictionary<string, string> p_dicLoadOrder, List<IVirtualModLink> p_ivaVirtualModLink, List<IVirtualModInfo> p_ivaVirtualModList)
		{
			Id = String.Format("{0}-{1}", Path.GetRandomFileName(), p_strName);
			Name = p_strName;
			GameModeId = p_strGameModeId;
			if ((p_dicLoadOrder != null) && (p_dicLoadOrder.Count > 0))
				LoadOrder = p_dicLoadOrder;
			if ((p_ivaVirtualModLink != null) && (p_ivaVirtualModLink.Count > 0))
				ModFileList = p_ivaVirtualModLink;
			if ((p_ivaVirtualModList != null) && (p_ivaVirtualModList.Count > 0))
				ModList = p_ivaVirtualModList;
		}

		public ModProfile(string p_strProfileId, string p_strName, string p_strGameModeId, Int32 p_intModCount)
		{
			Id = p_strProfileId;
			Name = p_strName;
			GameModeId = p_strGameModeId;
			m_intModCount = p_intModCount;
		}

		public ModProfile(string p_strProfileId, string p_strName, string p_strGameModeId, Int32 p_intModCount, bool p_booIsDefault)
		{
			Id = p_strProfileId;
			Name = p_strName;
			GameModeId = p_strGameModeId;
			m_intModCount = p_intModCount;
			IsDefault = p_booIsDefault;
		}

		#endregion

		public void UpdateLists(List<IVirtualModLink> p_lstVirtualModLink, List<IVirtualModInfo> p_lstVirtualModList)
		{
			if ((p_lstVirtualModLink != null) && (p_lstVirtualModLink.Count > 0))
				ModFileList = p_lstVirtualModLink;
			if ((p_lstVirtualModList != null) && (p_lstVirtualModList.Count > 0))
				ModList = p_lstVirtualModList;
		}
	}
}
