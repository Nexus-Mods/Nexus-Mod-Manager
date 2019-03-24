namespace Nexus.Client.ModRepositories.NexusModsApi.ApiObjects
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    [DataContract]
    public class GameDataContract
    {
        [DataMember(Name = "id")]
        public int Id { get; private set; }

        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "forum_url")]
        public string ForumUrl { get; private set; }

        [DataMember(Name = "nexusmods_url")]
        public string NexusModsUrl { get; private set; }

        [DataMember(Name = "genre")]
        public string Genre { get; private set; }

        [DataMember(Name = "file_count")]
        public int FileCount { get; private set; }

        [DataMember(Name = "downloads")]
        public int Downloads { get; private set; }

        [DataMember(Name = "domain_name")]
        public string DomainName { get; private set; }

        [DataMember(Name = "approved_date")]
        public DateTime ApprovedDate { get; private set; }

        [DataMember(Name = "file_views")]
        public int FileViews{ get; private set; }

        [DataMember(Name = "authors")]
        public int Authors { get; private set; }

        [DataMember(Name = "file_endorsements")]
        public int FileEndorsements { get; private set; }

        [DataMember(Name = "mods")]
        public int Mods { get; private set; }

        [DataMember(Name = "categories")]
        public List<GameCategoryDataContract> Categories { get; private set; }
    }

    [DataContract]
    public class GameCategoryDataContract
    {
        [DataMember(Name = "category_id")]
        public int Id { get; private set; }

        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "parent_category")]
        public int ParentCategory { get; private set; }
    }
}
