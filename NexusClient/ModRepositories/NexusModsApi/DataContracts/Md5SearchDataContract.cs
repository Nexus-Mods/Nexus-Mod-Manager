namespace Nexus.Client.ModRepositories.NexusModsApi.DataContracts
{
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    public class Md5SearchDataContract
    {
        [DataMember(Name = "mod")]
        public ModDataContract Mod { get; private set; }

        [DataMember(Name = "file_details")]
        public Md5SearchFileDetails FileDetails { get; private set; }
    }

    [DataContract]
    public class Md5SearchFileDetails
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

        /// <summary>
        /// File size, in kilobytes.
        /// </summary>
        [DataMember(Name = "size")]
        public int Size { get; private set; }

        [DataMember(Name = "file_name")]
        public string FileName { get; private set; }

        [DataMember(Name = "uploaded_timestamp")]
        public long UpdatedTimestamp { get; private set; }

        [DataMember(Name = "uploaded_time")]
        public DateTime UploadedTime { get; private set; }

        [DataMember(Name = "mod_version")]
        public string ModVersion { get; private set; }

        [DataMember(Name = "external_virus_scan_url")]
        public string ExternalVirusScanUrl { get; private set; }

        [DataMember(Name = "changelog_html")]
        public string ChangelogHtml { get; private set; }

        /// <summary>
        /// Checksum (MD5) of this mod file.
        /// </summary>
        [DataMember(Name = "md5")]
        public string Checksum { get; private set; }
    }
}
