namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.Mods
{
    using System.Runtime.Serialization;

    [DataContract]
    public class ModEndorsement
    {
        [DataMember(Name = "endorse_status")]
        public string Status { get; private set; }

        [DataMember(Name = "timestamp")]
        public long Timestamp { get; private set; }

        [DataMember(Name = "version")]
        public string Version { get; private set; }
    }
}
