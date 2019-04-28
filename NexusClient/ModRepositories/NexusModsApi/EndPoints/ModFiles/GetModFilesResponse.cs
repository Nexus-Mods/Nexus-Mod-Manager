namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.ModFiles
{
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    [DataContract]
    public class GetModFilesResponse
    {
        [DataMember(Name = "files")]
        public List<ModFile> Files { get; private set; }

        [DataMember(Name = "file_updates")]
        public List<ModFileUpdate> FileUpdates { get; private set; }
    }
}
