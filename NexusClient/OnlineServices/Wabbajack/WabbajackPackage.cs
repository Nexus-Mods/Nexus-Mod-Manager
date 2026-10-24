using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.OnlineServices.Wabbajack
{
	/// <summary>The original compiled installation recipe, retained separately from any future NMM Collection translation.</summary>
	public sealed class WabbajackPackage
	{
		public string Name { get; set; }
		public string Author { get; set; }
		public string Description { get; set; }
		public string GameType { get; set; }
		public string Version { get; set; }
		public string WabbajackVersion { get; set; }
		public List<WabbajackArchive> Archives { get; set; } = new List<WabbajackArchive>();
		public List<WabbajackDirective> Directives { get; set; } = new List<WabbajackDirective>();
		[JsonIgnore] public string FilePath { get; internal set; }
		[JsonIgnore] public string RawManifest { get; internal set; }
		[JsonExtensionData] public IDictionary<string, JToken> AdditionalData { get; set; }

		/// <summary>Reads either manifest entry name accepted by Wabbajack without extracting or executing the package.</summary>
		public static WabbajackPackage Read(string filePath)
		{
			using (FileStream stream = File.OpenRead(filePath))
			using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
			{
				ZipArchiveEntry entry = archive.GetEntry("modlist") ?? archive.GetEntry("modlist.json");
				if (entry == null) throw new InvalidDataException(LanguageManager.Get("Wabbajack.MissingManifest", "The Wabbajack package contains no modlist manifest."));
				string json;
				using (StreamReader reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();
				WabbajackPackage package = JsonConvert.DeserializeObject<WabbajackPackage>(json, WabbajackClient.JsonSettings);
				if (package == null || package.Archives == null || package.Directives == null)
					throw new InvalidDataException(LanguageManager.Get("Wabbajack.InvalidManifest", "The Wabbajack modlist manifest could not be read."));
				package.FilePath = Path.GetFullPath(filePath);
				package.RawManifest = json;
				return package;
			}
		}
	}

	/// <summary>One exact source, which may be a download or an existing game file rather than an installable mod.</summary>
	public sealed class WabbajackArchive
	{
		public string Name { get; set; }
		public string Hash { get; set; }
		public long Size { get; set; }
		public string Meta { get; set; }
		public WabbajackDownloadState State { get; set; }
	}

	/// <summary>Common download-source fields; provider-specific fields remain available for later acquisition adapters.</summary>
	public sealed class WabbajackDownloadState
	{
		[JsonProperty("$type")] public string Type { get; set; }
		public string GameName { get; set; }
		public long ModId { get; set; }
		public long FileId { get; set; }
		public string Url { get; set; }
		public string Game { get; set; }
		public string GameFile { get; set; }
		[JsonExtensionData] public IDictionary<string, JToken> AdditionalData { get; set; }
	}

	/// <summary>One compiled output instruction, including its original type and any currently untranslated fields.</summary>
	public sealed class WabbajackDirective
	{
		[JsonProperty("$type")] public string Type { get; set; }
		public string To { get; set; }
		public string Hash { get; set; }
		public long Size { get; set; }
		public string[] ArchiveHashPath { get; set; }
		public string SourceDataId { get; set; }
		public string PatchId { get; set; }
		[JsonExtensionData] public IDictionary<string, JToken> AdditionalData { get; set; }
	}
}
