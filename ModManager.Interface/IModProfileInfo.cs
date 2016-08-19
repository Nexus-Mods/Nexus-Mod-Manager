using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.ModManagement
{
	[DataContract]
	public class IModProfileInfo
	{
		#region Properties

		[DataMember(Name = "name")]
		public string Name { get; set; }

		[DataMember(Name = "date")]
		public DateTime Date { get; set; }

		[DataMember(Name = "public")]
		public int Public { get; set; }

		[DataMember(Name = "image")]
		public string Image { get; set; }

		[DataMember(Name = "version")]
		public int Version { get; set; }

		[DataMember(Name = "author_name")]
		public string Author { get; set; }

		[DataMember(Name = "author_id")]
		public string AuthorID { get; set; }

		[DataMember(Name = "files")]
		public int ActiveMods { get; set; }

		[DataMember(Name = "works_with_saves")]
		public int WorksWithSaves { get; set; }
		

		#endregion
	}
}
