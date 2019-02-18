﻿namespace Nexus.Client.ModRepositories.NexusModsApi.ApiObjects
{
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    public class ModDataContract
    {
        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "Summary")]
        public string Summary { get; private set; }

        [DataMember(Name = "description")]
        public string Description { get; private set; }

        [DataMember(Name = "picture_url")]
        public string PictureUrl { get; private set; }

        [DataMember(Name = "mod_id")]
        public int Id { get; private set; }

        [DataMember(Name = "game_id")]
        public int GameId { get; private set; }

        [DataMember(Name = "category_id")]
        public int CategoryId { get; private set; }

        [DataMember(Name = "version")]
        public string Version { get; private set; }

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
        public bool Available{ get; private set; }

        [DataMember(Name = "user")]
        public ModUserDataContract User { get; private set; }

        [DataMember(Name = "endorsement")]
        public ModEndorsementDataContract Endorsement { get; private set; }
    }

    [DataContract]
    public class ModUserDataContract
    {
        [DataMember(Name = "member_id")]
        public int Id { get; private set; }

        [DataMember(Name = "member_group_id")]
        public int GroupId { get; private set; }

        [DataMember(Name = "name")]
        public string Name { get; private set; }
    }

    [DataContract]
    public class ModEndorsementDataContract
    {
        [DataMember(Name = "endorse_status")]
        public string Status { get; private set; }

        [DataMember(Name = "timestamp")]
        public long Timestamp{ get; private set; }

        [DataMember(Name = "version")]
        public string Version { get; private set; }
    }
}