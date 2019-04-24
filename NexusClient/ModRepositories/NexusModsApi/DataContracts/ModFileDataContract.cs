namespace Nexus.Client.ModRepositories.NexusModsApi.DataContracts
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    [DataContract]
    public class ModFileDataContract
    {
        [DataMember(Name = "file_id")]
        public int Id { get; private set; }

        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "version")]
        public string Version { get; private set; }

        [DataMember(Name = "category_id")]
        public int CategoryId { get; private set; }

        [DataMember(Name = "category_name")]
        public string CategoryName { get; private set; }

        [DataMember(Name = "is_primary")]
        public bool IsPrimary { get; private set; }

        [DataMember(Name = "size")]
        public int Size { get; private set; }

        [DataMember(Name = "file_name")]
        public string FileName { get; private set; }

        [DataMember(Name = "uploaded_timestamp")]
        public long UploadedTimestamp { get; private set; }

        [DataMember(Name = "uploaded_time")]
        public DateTime? UploadedTime { get; private set; }

        [DataMember(Name = "mod_version")]
        public string ModVersion { get; private set; }

        [DataMember(Name = "external_virus_scan_url")]
        public Uri ExternalVirusScanUrl { get; private set; }

        [DataMember(Name = "description")]
        public string Description { get; private set; }

        [DataMember(Name = "size_kb")]
        public int SizeKb { get; private set; }

        [DataMember(Name = "changelog_html")]
        public string ChangeLogHtml { get; private set; }
    }

    [DataContract]
    public class ModFileUpdateDataContract
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

    [DataContract]
    public class GetModFilesResponseDataContract
    {
        [DataMember(Name = "files")]
        public List<ModFileDataContract> Files { get; private set; }

        [DataMember(Name = "file_updates")]
        public List<ModFileUpdateDataContract> FileUpdates { get; private set; }
    }
}
