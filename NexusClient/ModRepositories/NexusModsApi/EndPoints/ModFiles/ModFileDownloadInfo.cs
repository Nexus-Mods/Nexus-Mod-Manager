namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.ModFiles
{
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    public class ModFileDownloadInfo
    {
        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "short_name")]
        public string ShortName { get; private set; }

        [DataMember(Name = "URI")]
        public Uri Uri { get; private set; }
    }
}
