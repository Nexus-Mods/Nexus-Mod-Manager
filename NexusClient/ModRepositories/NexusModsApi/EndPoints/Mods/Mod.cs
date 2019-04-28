namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.Mods
{
    using System;
    using System.Runtime.Serialization;
    using global::Nexus.Client.Mods;
    using Util;

    [DataContract]
    public class Mod : IModInfo
    {
        public string LastKnownVersion { get; }

        public bool? IsEndorsed => Endorsement.Status.Equals("endorsed", StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public Version MachineVersion => new Version(HumanReadableVersion);
        
        [DataMember(Name = "Summary")]
        public string Summary { get; private set; }

        [DataMember(Name = "picture_url")]
        public string PictureUrl { get; private set; }

        [DataMember(Name = "mod_id")]
        public string Id { get; set; }

        /// <inheritdoc />
        public string DownloadId { get; set; }

        public DateTime? DownloadDate { get; set; }

        [DataMember(Name = "name")]
        public string ModName { get; }

        public string FileName { get; }
        
        /// <inheritdoc />
        [DataMember(Name = "version")]
        public string HumanReadableVersion { get; set; }

        [DataMember(Name = "game_id")]
        public int GameId { get; private set; }

        [DataMember(Name = "category_id")]
        public int CategoryId { get; private set; }

        /// <inheritdoc />
        public int CustomCategoryId { get; }

        [DataMember(Name = "created_timestamp")]
        public long CreatedTimestamp { get; private set; }

        [DataMember(Name = "created_time")]
        public DateTime Created { get; private set; }

        [DataMember(Name = "updated_timestamp")]
        public long UpdatedTimestamp { get; private set; }

        [DataMember(Name = "updated_time")]
        public DateTime Updated { get; private set; }

        [DataMember(Name = "author")]
        public string Author { get; private set; }

        [DataMember(Name = "uploaded_by")]
        public string Uploader { get; private set; }

        [DataMember(Name = "uploaded_users_profile_url")]
        public string UploadedUsersProfileUrl { get; private set; }

        [DataMember(Name = "contains_adult_content")]
        public bool ContainsAdultContent { get; private set; }

        [DataMember(Name = "status")]
        public string Status { get; private set; }

        [DataMember(Name = "available")]
        public bool Available { get; private set; }

        [DataMember(Name = "user")]
        public ModUserData User { get; private set; }

        [DataMember(Name = "endorsement")]
        public ModEndorsement Endorsement { get; private set; }

        [DataMember(Name = "description")]
        public string Description { get; private set; }

        /// <inheritdoc />
        public string InstallDate { get; set; }

        /// <inheritdoc />
        public Uri Website { get; }
        
        /// <inheritdoc />
        public ExtendedImage Screenshot { get; }

        /// <inheritdoc />
        public bool UpdateWarningEnabled { get; }

        /// <inheritdoc />
        public bool UpdateChecksEnabled { get; }
        
        /// <inheritdoc />
        public int PlaceInModLoadOrder { get; set; }

        /// <inheritdoc />
        public int NewPlaceInModLoadOrder { get; set; }

        /// <inheritdoc />
        public void UpdateInfo(IModInfo p_mifInfo, bool? p_booOverwriteAllValues)
        {
            throw new NotImplementedException();
        }
    }
}
