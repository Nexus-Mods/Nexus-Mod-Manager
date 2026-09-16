namespace Nexus.Client.ModManagement
{
    using System.Runtime.Serialization;

    [DataContract]
	public class CategoriesInfo
	{
		#region Properties

		[DataMember(Name = "id")]
		public int Id { get; set; }

		[DataMember(Name = "parent_id")]
		public int? ParentId { get; set; }

		[DataMember(Name = "name")]
		public string Name { get; set; }
		
		#endregion
	}
}

