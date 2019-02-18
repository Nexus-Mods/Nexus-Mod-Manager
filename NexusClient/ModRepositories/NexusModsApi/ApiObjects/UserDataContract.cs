namespace Nexus.Client.ModRepositories.NexusModsApi.ApiObjects
{
    using System.Runtime.Serialization;

    [DataContract]
    public class UserDataContract
    {
        [DataMember(Name = "user_id")]
        public int Id { get; private set; }

        [DataMember(Name = "key")]
        public string ApiKey { get; private set; }

        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "is_premium")]
        public bool IsPremium { get; private set; }

        [DataMember(Name = "is_supporter")]
        public bool IsSupporter { get; private set; }

        [DataMember(Name = "email")]
        public string Email { get; private set; }

        [DataMember(Name = "profile_url")]
        public string ProfileUrl { get; private set; }
    }
}
