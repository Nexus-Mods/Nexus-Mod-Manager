using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Nexus.Client.Util.Localization
{
	/// <summary>
	/// Represents an external NMM UI language pack.
	/// </summary>
	[DataContract]
	public sealed class LanguagePack
	{
		/// <summary>
		/// Gets or sets the language pack format version.
		/// </summary>
		[DataMember(Name = "formatVersion", Order = 1, IsRequired = true)]
		public int FormatVersion { get; set; }

		/// <summary>
		/// Gets or sets the stable language identifier (for example, it-IT).
		/// </summary>
		[DataMember(Name = "id", Order = 2, IsRequired = true)]
		public string Id { get; set; }

		/// <summary>
		/// Gets or sets the display name shown to the user.
		/// </summary>
		[DataMember(Name = "name", Order = 3, IsRequired = true)]
		public string Name { get; set; }

		/// <summary>
		/// Gets or sets the optional culture associated with the pack.
		/// </summary>
		[DataMember(Name = "culture", Order = 4, EmitDefaultValue = false)]
		public string Culture { get; set; }

		/// <summary>
		/// Gets or sets the optional language pack author.
		/// </summary>
		[DataMember(Name = "author", Order = 5, EmitDefaultValue = false)]
		public string Author { get; set; }

		/// <summary>
		/// Gets or sets the translated UI strings, keyed by the stable NMM localization key.
		/// </summary>
		[DataMember(Name = "strings", Order = 6, IsRequired = true)]
		public Dictionary<string, string> Strings { get; set; }
	}
}
