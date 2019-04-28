namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.ModFiles
{
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    public class ModFileUpdate
    {
        [DataMember(Name = "old_file_id")]
        public int OldFileId { get; private set; }

        [DataMember(Name = "old_file_name")]
        public string OldFileName { get; private set; }

        [DataMember(Name = "new_file_id")]
        public int NewFileId { get; private set; }

        [DataMember(Name = "new_file_name")]
        public string NewFileName { get; private set; }

        [DataMember(Name = "uploaded_timestamp")]
        public long UploadedTimestamp { get; private set; }

        [DataMember(Name = "uploaded_time")]
        public DateTime? UploadedTime { get; private set; }
    }
}
