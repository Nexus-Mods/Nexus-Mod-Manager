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
		private string m_strName = string.Empty;
		private string m_strOnlineName = string.Empty;

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
		public string Name
		{
			get
			{
				return (string.IsNullOrEmpty(m_strName) ? m_strName : m_strName.Replace("|", string.Empty));
			}
			set
			{
				m_strName = (string.IsNullOrEmpty(value) ? value : value.Replace("|", string.Empty));
			}
		}

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

		/// <summary>
		/// Gets or sets the Online Id of the profile.
		/// </summary>
		/// <remarks>The GameModeId of the profile</remarks>
		public string OnlineID { get; private set; }

		/// <summary>
		/// Gets or sets the Online Name of the profile.
		/// </summary>
		/// <remarks>The GameModeId of the profile</remarks>
		public string OnlineName
		{
			get
			{
				return (string.IsNullOrEmpty(m_strOnlineName) ? m_strOnlineName : m_strOnlineName.Replace("|", string.Empty));
			}
			private set
			{
				m_strOnlineName = (string.IsNullOrEmpty(value) ? value : value.Replace("|", string.Empty));
			}
		}

		/// <summary>
		/// Gets whether the profile is online (backed or shared).
		/// </summary>
		/// <remarks>Whether the profile is online (backed or shared)</remarks>
		public bool IsOnline
		{
			get
			{
				DateTime dtDate;
				return (IsShared || (!string.IsNullOrEmpty(BackupDate) && DateTime.TryParse(BackupDate, out dtDate)));
			}
		}

		/// <summary>
		/// Gets or sets the Backup Date.
		/// </summary>
		/// <remarks>The BackupDate of the profile</remarks>
		public string BackupDate { get; private set; }

		/// <summary>
		/// Gets or sets the Version of the profile.
		/// </summary>
		/// <remarks>The Version of the profile</remarks>
		public int Version { get; private set; }

		/// <summary>
		/// Gets or sets the Author of the profile.
		/// </summary>
		/// <remarks>The Author of the profile</remarks>
		public string Author { get; private set; }

		/// <summary>
		/// Gets or sets the Works With Saves flag of the profile.
		/// </summary>
		/// <remarks>The  Works With Saves flag of the profile</remarks>
		public int WorksWithSaves { get; private set; }

		/// <summary>
		/// Gets or sets the Screenshot of the profile.
		/// </summary>
		/// <remarks>The Screenshot of the profile</remarks>
		public string Screenshot { get; private set; }

		/// <summary>
		/// Gets or sets the Backed Up Date of the profile.
		/// </summary>
		/// <remarks>The GameModeId of the profile</remarks>
		public bool IsShared { get; private set; }

		/// <summary>
		/// Gets or sets the Edited flag of the profile.
		/// </summary>
		/// <remarks>The GameModeId of the profile</remarks>
		public bool IsEdited { get; set; }

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

			Author = "";
			OnlineID = "";
		}

		public ModProfile(string p_strProfileId, string p_strName, string p_strGameModeId, Int32 p_intModCount)
		{
			Id = p_strProfileId;
			Name = p_strName;
			GameModeId = p_strGameModeId;
			m_intModCount = p_intModCount;
			Author = "";
			OnlineID = "";
		}

		public ModProfile(string p_strProfileId, string p_strName, string p_strGameModeId, Int32 p_intModCount, bool p_booIsDefault)
		{
			Id = p_strProfileId;
			Name = p_strName;
			GameModeId = p_strGameModeId;
			m_intModCount = p_intModCount;
			IsDefault = p_booIsDefault;
			Author = "";
			OnlineID = "";
		}

		public ModProfile(string p_strProfileId, string p_strName, string p_strGameModeId, Int32 p_intModCount, bool p_booIsDefault, string p_strOnlineId, string p_strOnlineName, string p_strIsBackedUp, bool p_booIsShared, string p_strProfileVersion, string p_strProfileAuthor, int p_intWorksWithSaves, bool p_booIsEdited)
		{
			Id = p_strProfileId;
			Name = p_strName;
			GameModeId = p_strGameModeId;
			m_intModCount = p_intModCount;
			IsDefault = p_booIsDefault;
			OnlineID = p_strOnlineId;
			OnlineName = p_strOnlineName;
			BackupDate = p_strIsBackedUp;
			IsShared = p_booIsShared;
			IsEdited = p_booIsEdited;
			
			Version = p_strProfileVersion == "" ? 0 : int.Parse(p_strProfileVersion);
			Author = p_strProfileAuthor;


			WorksWithSaves = p_intWorksWithSaves;
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
