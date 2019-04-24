namespace Nexus.Client.ModRepositories.NexusModsApi.DataContracts
{
    using System.Runtime.Serialization;

    [DataContract]
    public class ColourSchemeDataContract
    {
        [DataMember(Name = "id")]
        public int Id { get; private set; }

        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "primary_colour")]
        public string PrimaryColour { get; private set; }

        [DataMember(Name = "secondary_colour")]
        public string SecondaryColour { get; private set; }

        [DataMember(Name = "darker_colour")]
        public string DarkerColour { get; private set; }
    }
}
