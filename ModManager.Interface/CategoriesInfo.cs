namespace Nexus.Client.ModManagement
{
    using System.Runtime.Serialization;
    using Pathoschild.FluentNexus.Models;

    [DataContract]
	public class CategoriesInfo
	{
        public CategoriesInfo(GameCategory gameCategory)
        {
            Id = gameCategory.ID;
            ParentId = gameCategory.ParentCategory;
            Name = gameCategory.Name;
        }

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

