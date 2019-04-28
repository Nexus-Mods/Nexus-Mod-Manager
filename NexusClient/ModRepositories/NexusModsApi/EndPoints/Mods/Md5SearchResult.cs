namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.Mods
{
    using System.Runtime.Serialization;

    [DataContract]
    public class Md5SearchResult
    {
        [DataMember(Name = "mod")]
        public Mod Mod { get; private set; }

        [DataMember(Name = "file_details")]
        public Md5SearchFileDetails FileDetails { get; private set; }
    }
}
