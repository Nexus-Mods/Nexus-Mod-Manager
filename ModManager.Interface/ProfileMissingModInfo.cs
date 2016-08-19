using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.ModManagement
{
	[DataContract]
	public class ProfileMissingModInfo
	{
		#region Properties

		[DataMember(Name = "mod_id")]
		public int ModId { get; set; }

		[DataMember(Name = "file_id")]
		public int FileId { get; set; }

		[DataMember(Name = "new_file_id")]
		public int NewFileId { get; set; }

		[DataMember(Name = "mod_name")]
		public string ModName { get; set; }

		[DataMember(Name = "is_guess")]
		public bool IsGuess { get; set; }
		
		[DataMember(Name = "new_file_name")]
		public string NewFileName { get; set; }
		#endregion
	}
}
