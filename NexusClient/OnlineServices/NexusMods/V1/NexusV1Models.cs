using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Nexus.Client.OnlineServices.NexusMods.V1
{
    /// <summary>
    /// Represents the Nexus Mods v1 endorsement state returned with mod metadata.
    /// </summary>
    public enum NexusV1EndorsementStatus
    {
        None,
        Undecided,
        Abstained,
        Endorsed
    }

    /// <summary>
    /// Represents a Nexus Mods v1 file category.
    /// </summary>
    public enum NexusV1FileCategory
    {
        Main = 1,
        Update = 2,
        Optional = 3,
        Old = 4,
        Miscellaneous = 5,
        Deleted = 6,
        Archived = 7
    }

    /// <summary>
    /// Contains the Nexus Mods v1 account metadata used by NMM.
    /// </summary>
    public sealed class NexusV1User
    {
        public string Name { get; set; }

        [JsonProperty("is_premium")]
        public bool IsPremium { get; set; }

        [JsonProperty("is_supporter")]
        public bool IsSupporter { get; set; }
    }

    /// <summary>
    /// Contains the subset of Nexus Mods v1 mod metadata used by NMM.
    /// </summary>
    public sealed class NexusV1Mod
    {
        [JsonProperty("mod_id")]
        public int ModId { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        [JsonProperty("domain_name")]
        public string DomainName { get; set; }

        [JsonProperty("category_id")]
        public int CategoryId { get; set; }

        public string Version { get; set; }

        public string Author { get; set; }

        public NexusV1Endorsement Endorsement { get; set; }
    }

    /// <summary>
    /// Contains Nexus Mods v1 endorsement metadata used by NMM.
    /// </summary>
    public sealed class NexusV1Endorsement
    {
        [JsonProperty("endorse_status")]
        public NexusV1EndorsementStatus Status { get; set; }

        public string Version { get; set; }
    }

    /// <summary>
    /// Contains Nexus Mods v1 file metadata used by NMM.
    /// </summary>
    public class NexusV1ModFile
    {
        [JsonProperty("file_id")]
        public int FileId { get; set; }

        public string Name { get; set; }

        [JsonProperty("file_name")]
        public string FileName { get; set; }

        [JsonProperty("version")]
        public string FileVersion { get; set; }

        [JsonProperty("mod_version")]
        public string ModVersion { get; set; }

        [JsonProperty("category_id")]
        public NexusV1FileCategory Category { get; set; }

        [JsonProperty("uploaded_timestamp")]
        [JsonConverter(typeof(UnixDateTimeConverter))]
        public DateTimeOffset UploadedTimestamp { get; set; }
    }

    /// <summary>
    /// Contains one Nexus Mods v1 mod-file download source.
    /// </summary>
    public sealed class NexusV1DownloadLink
    {
        [JsonProperty("name")]
        public string CdnName { get; set; }

        [JsonProperty("short_name")]
        public string CdnShortName { get; set; }

        [JsonProperty("URI")]
        public Uri Uri { get; set; }
    }

    /// <summary>
    /// Contains Nexus Mods v1 file metadata returned by MD5 lookup, including the hash.
    /// </summary>
    public sealed class NexusV1ModFileWithHash : NexusV1ModFile
    {
        public string Md5 { get; set; }
    }

    /// <summary>
    /// Represents an update relationship between two Nexus Mods v1 files.
    /// </summary>
    public sealed class NexusV1ModFileUpdate
    {
        [JsonProperty("old_file_id")]
        public int OldFileId { get; set; }

        [JsonProperty("old_file_name")]
        public string OldFileName { get; set; }

        [JsonProperty("new_file_id")]
        public int NewFileId { get; set; }

        [JsonProperty("new_file_name")]
        public string NewFileName { get; set; }
    }

    /// <summary>
    /// Contains the Nexus Mods v1 file list and its update relationships.
    /// </summary>
    public sealed class NexusV1ModFileList
    {
        public NexusV1ModFile[] Files { get; set; }

        [JsonProperty("file_updates")]
        public NexusV1ModFileUpdate[] FileUpdates { get; set; }
    }

    /// <summary>
    /// Contains one Nexus Mods v1 MD5 lookup result.
    /// </summary>
    public sealed class NexusV1ModHashResult
    {
        public NexusV1Mod Mod { get; set; }

        [JsonProperty("file_details")]
        public NexusV1ModFileWithHash File { get; set; }
    }

    /// <summary>
    /// Contains one Nexus Mods v1 updated-mod record.
    /// </summary>
    public sealed class NexusV1ModUpdate
    {
        [JsonProperty("mod_id")]
        public int ModId { get; set; }

        [JsonProperty("latest_file_update")]
        [JsonConverter(typeof(UnixDateTimeConverter))]
        public DateTimeOffset LatestFileUpdate { get; set; }

        [JsonProperty("latest_mod_activity")]
        [JsonConverter(typeof(UnixDateTimeConverter))]
        public DateTimeOffset LatestModActivity { get; set; }
    }

    /// <summary>
    /// Contains the Nexus Mods v1 game metadata needed by NMM.
    /// </summary>
    public sealed class NexusV1Game
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("domain_name")]
        public string DomainName { get; set; }

        public string Name { get; set; }

        public NexusV1GameCategory[] Categories { get; set; }
    }

    /// <summary>
    /// Contains one Nexus Mods v1 game category.
    /// </summary>
    public sealed class NexusV1GameCategory
    {
        [JsonProperty("category_id")]
        public int Id { get; set; }

        public string Name { get; set; }

        [JsonProperty("parent_category")]
        [JsonConverter(typeof(NexusV1NullableIntConverter))]
        public int? ParentCategory { get; set; }
    }

    /// <summary>
    /// Preserves the legacy v1 behavior where Nexus can return false instead of null for nullable integer fields.
    /// </summary>
    internal sealed class NexusV1NullableIntConverter : JsonConverter
    {
        /// <summary>
        /// Gets whether this converter handles nullable integers.
        /// </summary>
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(int?);
        }

        /// <summary>
        /// Reads a nullable integer while treating boolean values as null.
        /// </summary>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null || reader.TokenType == JsonToken.Boolean)
                return null;

            try
            {
                return Convert.ToInt32(reader.Value);
            }
            catch (Exception ex)
            {
                throw new JsonSerializationException("The Nexus Mods v1 response contains an invalid nullable integer value.", ex);
            }
        }

        /// <summary>
        /// Writes a nullable integer using the default JSON scalar representation.
        /// </summary>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(value);
        }
    }
}
