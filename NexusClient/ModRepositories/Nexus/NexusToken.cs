using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.ModRepositories.Nexus
{
	[DataContract]
	public class NexusToken
	{

		#region Properties

		[DataMember(Name = "r")]
		public string R { get; set; }

		[DataMember(Name = "p")]
		public string P { get; set; }

		[DataMember(Name = "c")]
		public string C { get; set; }

		[DataMember(Name = "m")]
		public string M { get; set; }

		[DataMember(Name = "error")]
		public string ErrorMessage { get; set; }
		
		public byte[] L { get; set; }
		
		#endregion
	}
}
