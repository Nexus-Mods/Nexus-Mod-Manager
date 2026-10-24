using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Nexus.Client.OnlineServices.Wabbajack
{
	/// <summary>Public gallery metadata, independent of the game currently open in NMM.</summary>
	public sealed class WabbajackList
	{
		public string Title { get; set; }
		public string Description { get; set; }
		public string Author { get; set; }
		public string Game { get; set; }
		public string Version { get; set; }
		public bool Official { get; set; }
		public bool Nsfw { get; set; }
		[JsonProperty("force_down")] public bool ForceDown { get; set; }
		[JsonProperty("utility_list")] public bool UtilityList { get; set; }
		public List<string> Tags { get; set; } = new List<string>();
		public WabbajackLinks Links { get; set; } = new WabbajackLinks();
		[JsonProperty("download_metadata")] public WabbajackDownloadMetadata DownloadMetadata { get; set; }
		public DateTime DateUpdated { get; set; }
		[JsonIgnore] public string Repository { get; internal set; }
		[JsonIgnore] public WabbajackListStatus Status { get; internal set; }
		[JsonIgnore] public string Id => Repository + "/" + Links.MachineUrl;
		[JsonIgnore] public bool IsUnavailable => ForceDown || (Status != null && (Status.Failed > 0 || Status.ModlistMissing));
	}

	/// <summary>Links supplied by the list author.</summary>
	public sealed class WabbajackLinks
	{
		public string Image { get; set; }
		public string Readme { get; set; }
		public string Download { get; set; }
		public string MachineUrl { get; set; }
		public string WebsiteUrl { get; set; }
		public string DiscordUrl { get; set; }
	}

	/// <summary>Published package, archive and installation sizes.</summary>
	public sealed class WabbajackDownloadMetadata
	{
		public string Hash { get; set; }
		public long Size { get; set; }
		public int NumberOfArchives { get; set; }
		public long SizeOfArchives { get; set; }
		public long SizeOfInstalledFiles { get; set; }
		public long TotalSize { get; set; }
	}

	/// <summary>Availability reported by Wabbajack's existing list validator.</summary>
	public sealed class WabbajackListStatus
	{
		public string MachineUrl { get; set; }
		public int Failed { get; set; }
		[JsonProperty("modlist_missing")] public bool ModlistMissing { get; set; }
	}

	/// <summary>Upstream index used by the gallery's included/excluded mod filter.</summary>
	public sealed class WabbajackSearchIndex
	{
		public List<string> AllMods { get; set; } = new List<string>();
		public Dictionary<string, HashSet<string>> ModsPerList { get; set; } = new Dictionary<string, HashSet<string>>();
	}

	/// <summary>One loaded gallery and its optional mod search index.</summary>
	public sealed class WabbajackCatalogue
	{
		public List<WabbajackList> Lists { get; } = new List<WabbajackList>();
		public WabbajackSearchIndex SearchIndex { get; internal set; } = new WabbajackSearchIndex();
		public int FailedRepositories { get; internal set; }
	}

	/// <summary>The gallery's game, text, tag, mod and size choices.</summary>
	public sealed class WabbajackGalleryFilter
	{
		public string Game { get; set; }
		public string Search { get; set; }
		public bool IncludeUnofficial { get; set; }
		public bool IncludeNsfw { get; set; }
		public long MinimumSize { get; set; }
		public long MaximumSize { get; set; } = long.MaxValue;
		public ICollection<string> Tags { get; set; } = new string[0];
		public ICollection<string> Mods { get; set; } = new string[0];
		public bool ExcludeMods { get; set; }

		/// <summary>Applies Wabbajack's gallery filter semantics without downloading individual packages.</summary>
		public bool Matches(WabbajackList list, WabbajackSearchIndex index)
		{
			if (!string.IsNullOrEmpty(Game) && !string.Equals(Game, list.Game, StringComparison.OrdinalIgnoreCase)) return false;
			if (!IncludeUnofficial && !list.Official || !IncludeNsfw && list.Nsfw) return false;
			if (!string.IsNullOrWhiteSpace(Search) && !Contains(list.Title, Search) && !Contains(list.Description, Search) &&
				!list.Tags.Any(tag => string.Equals(tag, Search, StringComparison.OrdinalIgnoreCase))) return false;
			long size = list.DownloadMetadata == null ? 0 : list.DownloadMetadata.TotalSize;
			if (size < MinimumSize || size > MaximumSize || !Tags.All(tag => list.Tags.Contains(tag))) return false;
			HashSet<string> mods;
			bool known = index.ModsPerList.TryGetValue(list.Id, out mods) || index.ModsPerList.TryGetValue(list.Links.MachineUrl, out mods);
			return Mods.Count == 0 || (ExcludeMods ? !known || !Mods.Any(mods.Contains) : known && Mods.All(mods.Contains));
		}

		/// <summary>Matches free text using the same case-insensitive title/description search as the upstream gallery.</summary>
		private static bool Contains(string text, string value)
		{
			return text != null && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
		}
	}
}
