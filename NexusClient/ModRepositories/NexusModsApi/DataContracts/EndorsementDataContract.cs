namespace Nexus.Client.ModRepositories.NexusModsApi.DataContracts
{
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    public class EndorsementDataContract
    {
        [DataMember(Name = "mod_id")]
        public int ModId { get; private set; }

        [DataMember(Name = "domain_name")]
        public string DomainName { get; private set; }

        [DataMember(Name = "date")]
        public DateTime Date { get; private set; }

        [DataMember(Name = "version")]
        public string Version { get; private set; }

        [DataMember(Name = "status")]
        public string Status { get; private set; }
    }
}
