using System;
using System.Net;
using System.Runtime.Serialization;

namespace Nexus.Client.ModRepositories.Nexus
{
	/// <summary>
	/// Describes the metadata of a mod in the Nexus repository.
	/// </summary>
	[DataContract]
	public class NexusModInfo
	{
		private string m_strModName;
		private string m_strFileName;

		#region Properties

		/// <summary>
		/// Gets or sets whether the mod contains adult material.
		/// </summary>
		/// <value>Whether the mod contains adult material.</value>
		[DataMember(Name = "adult")]
		public bool IsAdult { get; set; }

		/// <summary>
		/// Gets or sets the category of the mod.
		/// </summary>
		/// <value>The category of the mod.</value>
		[DataMember(Name = "category_id")]
		public Int32 CategoryId { get; set; }

		/// <summary>
		/// Gets or sets the description of the mod.
		/// </summary>
		/// <value>The description of the mod.</value>
		[DataMember(Name = "description")]
		public string Description { get; set; }

		/// <summary>
		/// Gets or sets the Id of the mod.
		/// </summary>
		/// <value>The Id of the mod.</value>
		[DataMember(Name = "id")]
		public string Id { get; set; }

		/// <summary>
		/// Gets or sets the DownloadId of the mod.
		/// </summary>
		/// <value>The DownloadId of the mod.</value>
		[DataMember(Name = "downloadID")]
		public string DownloadId { get; set; }

		/// <summary>
		/// Gets or sets the NewDownloadId of the mod.
		/// </summary>
		/// <value>The NewDownloadId of the mod.</value>
		[DataMember(Name = "new_downloadID")]
		public string NewDownloadId { get; set; }

		/// <summary>
		/// Gets or sets the NewFilename of the mod.
		/// </summary>
		/// <value>The NewFilename of the mod.</value>
		[DataMember(Name = "new_filename")]
		public string NewFilename { get; set; }

		/// <summary>
		/// Gets or sets the last updated date of the mod.
		/// </summary>
		/// <value>The last updated date of the mod.</value>
		[DataMember(Name = "lastupdate")]
		public DateTime? LastUpdated { get; set; }

		/// <summary>
		/// Gets or sets the name of the mod.
		/// </summary>
		/// <value>The name of the mod.</value>
		[DataMember(Name = "name")]
		public string Name
		{
			get
			{
				return WebUtility.HtmlDecode(m_strModName);
			}
			private set
			{
				m_strModName = value;
			}
		}

		/// <summary>
		/// Gets or sets the filename of the mod.
		/// </summary>
		/// <value>The filename of the mod.</value>
		[DataMember(Name = "filename")]
		public string Filename
		{
			get
			{
				return WebUtility.HtmlDecode(m_strFileName);
			}
			private set
			{
				m_strFileName = value;
			}
		}

		/// <summary>
		/// Gets or sets the author of the mod.
		/// </summary>
		/// <value>The author of the mod.</value>
		[DataMember(Name = "author")]
		public string Author { get; set; }

		/// <summary>
		/// Gets or sets the owner of the mod.
		/// </summary>
		/// <value>The owner of the mod.</value>
		[DataMember(Name = "owner_id")]
		public Int32 OwnerId { get; set; }

		/// <summary>
		/// Gets or sets the summary of the mod.
		/// </summary>
		/// <value>The summary of the mod.</value>
		[DataMember(Name = "summary")]
		public string Summary { get; set; }

		/// <summary>
		/// Gets or sets the human readable mod version.
		/// </summary>
		/// <value>The human readable mod version.</value>
		[DataMember(Name = "version")]
		public string HumanReadableVersion { get; set; }

		/// <summary>
		/// Gets or sets the endorsement state.
		/// </summary>
		/// <value>The endorsement state.</value>
		[DataMember(Name = "voted_by_user")]
		public bool? IsEndorsed { get; set; }

		/// <summary>
		/// Gets or sets the mod page URI.
		/// </summary>
		/// <value>The mod page URI.</value>
		[DataMember(Name = "mod_page_uri")]
		public string Website { get; set; }

		/// <summary>
		/// Gets or sets the Guess state.
		/// </summary>
		/// <value>The mod page URI.</value>
		[DataMember(Name = "is_guess")]
		public int IsGuess { get; set; }

		/// <summary>
		/// Gets or sets the Requested DownloadId.
		/// </summary>
		/// <value>The Requested DownloadId.</value>
		[DataMember(Name = "requested_downloadID")]
		public string RequestedDownloadId { get; set; }

		/// <summary>
		/// Gets or sets the Requested FileName.
		/// </summary>
		/// <value>The Requested FileName.</value>
		[DataMember(Name = "requested_filename")]
		public string RequestedFileName { get; set; }

		#endregion
	}
}
