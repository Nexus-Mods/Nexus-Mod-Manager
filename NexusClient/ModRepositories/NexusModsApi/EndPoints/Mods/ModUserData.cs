namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.Mods
{
    using System.Runtime.Serialization;

    [DataContract]
    public class ModUserData
    {
        [DataMember(Name = "member_id")]
        public int Id { get; private set; }

        [DataMember(Name = "member_group_id")]
        public int GroupId { get; private set; }

        [DataMember(Name = "name")]
        public string Name { get; private set; }
    }
}
