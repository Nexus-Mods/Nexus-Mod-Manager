using System;
using System.Runtime.Serialization;
using Nexus.Client.Mods;

namespace Nexus.Client.ModRepositories
{
	/// <summary>
	/// Describes the metadata of a fileserver.
	/// </summary>
	[DataContract]
	public class FileserverInfo
	{
		#region Properties

		/// <summary>
		/// Gets the download link.
		/// </summary>
		/// <value>The download link.</value>
		[DataMember(Name = "URI")]
		public string DownloadLink { get; set; }

		/// <summary>
		/// Gets whether the server is Premium or Normal.
		/// </summary>
		/// <value>True if the server is Premium.</value>
		[DataMember(Name = "IsPremium")]
		public bool IsPremium { get; set; }

		/// <summary>
		/// Gets the fileserver name.
		/// </summary>
		/// <value>The fileserver name.</value>
		[DataMember(Name = "Name")]
		public string Name { get; set; }

		/// <summary>
		/// Gets the fileserver country.
		/// </summary>
		/// <value>The fileserver country.</value>
		[DataMember(Name = "Country")]
		public string Country { get; set; }

		/// <summary>
		/// Gets the number of users currently connected to the server.
		/// </summary>
		/// <value>The number of users currently connected to the server.</value>
		[DataMember(Name = "ConnectedUsers")]
		public Int32 ConnectedUsers { get; set; }

		#endregion
	}
}
