using System;
using System.Net;
using System.Runtime.Serialization;

namespace Nexus.Client.ModRepositories.Nexus
{
	/// <summary>
	/// Describes the metadata of a file of a mod in the Nexus repository.
	/// </summary>
	[DataContract]
	public class NexusModFileInfo : IModFileInfo
	{
		private string m_strModName;

		#region Properties

		/// <summary>
		/// Gets or sets the file id.
		/// </summary>
		/// <value>The file id.</value>
		[DataMember(Name = "id")]
		public string Id { get; set; }

		/// <summary>
		/// Gets or sets the id of the mod to which the file belongs.
		/// </summary>
		/// <value>The id of the mod to which the file belongs.</value>
		[DataMember(Name = "mod_id")]
		public Int32 ModId { get; set; }

		/// <summary>
		/// Gets or sets the owner of the file.
		/// </summary>
		/// <value>The owner of the file.</value>
		[DataMember(Name = "owner_id")]
		public Int32 OwnerId { get; set; }

		/// <summary>
		/// Gets or sets the file cateogry.
		/// </summary>
		/// <value>The file cateogry.</value>
		[DataMember(Name = "category_id")]
		public ModFileCategory Category { get; set; }

		/// <summary>
		/// Gets or sets the display name of the file.
		/// </summary>
		/// <value>The display name of the file.</value>
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
		/// Gets or sets the description of the file.
		/// </summary>
		/// <value>The description of the file.</value>
		[DataMember(Name = "description")]
		public string Description { get; set; }

		/// <summary>
		/// Gets or sets the filename.
		/// </summary>
		/// <value>The filename.</value>
		[DataMember(Name = "uri")]
		public string Filename { get; set; }

		/// <summary>
		/// Gets or sets the file size.
		/// </summary>
		/// <value>The file size.</value>
		[DataMember(Name = "size")]
		public UInt32 Size { get; set; }

		/// <summary>
		/// Gets or sets the human readable version of the mod to which the file belongs.
		/// </summary>
		/// <value>The human readable version of the mod to which the file belongs.</value>
		[DataMember(Name = "version")]
		public string HumanReadableVersion { get; set; }

		/// <summary>
		/// Gets or sets the date the file was loaded into the repository.
		/// </summary>
		/// <value>The date the file was loaded into the repository.</value>
		[DataMember(Name = "date")]
		public DateTime Date { get; set; }

		#endregion
	}
}
