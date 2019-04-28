namespace Nexus.Client.ModRepositories.NexusModsApi.DataContracts.Games
{
    using System.Runtime.Serialization;

    [DataContract]
    public class GameCategory
    {
        [DataMember(Name = "category_id")]
        public int Id { get; private set; }

        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "parent_category")]
        public int ParentCategory { get; private set; }
    }
}
