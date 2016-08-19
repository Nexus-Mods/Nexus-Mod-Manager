using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.ModManagement
{
	[DataContract]
	public class CategoriesInfo
	{
		#region Properties

		[DataMember(Name = "id")]
		public int Id { get; set; }

		[DataMember(Name = "parent_id")]
		public int ParentId { get; set; }

		[DataMember(Name = "name")]
		public string Name { get; set; }
		
		#endregion
	}
}

