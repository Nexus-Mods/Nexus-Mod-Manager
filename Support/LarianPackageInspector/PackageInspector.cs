using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LSLib.LS;
using LSLib.LS.Enums;

namespace Nexus.LarianPackageInspector;

/// <summary>Reads package indexes and bounded metadata in memory, preserving ambiguity as partial evidence.</summary>
internal static class PackageInspector
{
	/// <summary>Checks actual package headers and all declared parts before opening package content.</summary>
	internal static Response Inspect(Request request)
	{
		string path = Path.Combine(request.Root, request.FileName);
		CheckObserved(request, request.FileName);
		using Package header = ReadHeader(path);
		if (header == null) return ReadContinuation(request);
		List<string> readFiles = Parts(request, header, path);
		if (header.Metadata.Flags.HasFlag(PackageFlags.Solid))
			return new Response { FileName = request.FileName, Status = "Partial", ReadFiles = readFiles,
				TechnicalDetails = "Solid archives require whole-archive decompression and are not inspected by this bounded reader." };
		using Package package = new PackageReader().Read(path);
		if (package.Files.Count > 1000000) throw new InvalidDataException("Package index exceeds the entry limit.");
		var response = new Response { FileName = request.FileName, ReadFiles = readFiles };
		Dictionary<string, PackagedFileInfo> entries = new(StringComparer.OrdinalIgnoreCase);
		foreach (PackagedFileInfo entry in package.Files.Where(entry => !entry.IsDeletion()))
		{
			string name = entry.Name.Replace('\\', '/');
			if (name.Split('/').Any(part => part == ".." || part == ".") || name.StartsWith('/') || name.Contains(':') || !entries.TryAdd(name, entry))
				throw new InvalidDataException("Ambiguous or unsafe package entry name.");
		}
		long textBudget = 16 * 1024 * 1024;
		foreach (var meta in entries.Where(item => Regex.IsMatch(item.Key, @"^Mods/[^/]+/meta\.lsx$", RegexOptions.IgnoreCase)))
		{
			if (response.Modules.Count >= 256) throw new InvalidDataException("Too many modules in one package.");
			XDocument xml = ReadXml(ReadText(meta.Value, ref textBudget));
			XElement info = xml.Descendants("node").SingleOrDefault(node => (string)node.Attribute("id") == "ModuleInfo")
				?? throw new InvalidDataException("Module metadata has no unique ModuleInfo node.");
			var module = new Module
			{
				Identity = Uuid(Attribute(info, "UUID")), Name = Required(Attribute(info, "Name"), 1024),
				Folder = Required(Attribute(info, "Folder"), 1024), Version = Integer(Attribute(info, "Version64") ?? Attribute(info, "Version")),
				Author = Attribute(info, "Author"), Description = Attribute(info, "Description"), Md5 = Attribute(info, "MD5"),
				PublishHandle = Attribute(info, "PublishHandle"),
				Kind = Attribute(info, "Type") == "Adventure" ? "Campaign" : "AddOn",
				Tags = (Attribute(info, "Tags") ?? String.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
				Dependencies = References(xml, "Dependencies"), Conflicts = References(xml, "Conflicts")
			};
			if (module.PublishHandle != null) Integer(module.PublishHandle);
			string moduleRoot = meta.Key[..meta.Key.LastIndexOf('/')];
			module.Features = DetectFeatures(entries.Keys.Where(name => name.StartsWith(moduleRoot + "/", StringComparison.OrdinalIgnoreCase)));
			string configPath = moduleRoot + "/ScriptExtender/Config.json";
			if (entries.TryGetValue(configPath, out PackagedFileInfo config))
			{
				using JsonDocument document = JsonDocument.Parse(ReadText(config, ref textBudget), new JsonDocumentOptions { MaxDepth = 32 });
				if (document.RootElement.TryGetProperty("RequiredVersion", out JsonElement version))
				{
					int required = version.GetInt32();
					if (required < 0) throw new InvalidDataException("Negative Script Extender requirement.");
					module.RequiredScriptExtenderVersion = required;
				}
				module.Features |= Features.ScriptExtender;
			}
			response.Modules.Add(module);
		}
		response.Features = DetectFeatures(entries.Keys);
		foreach (var goal in entries.Where(item => item.Key.Contains("/Story/RawFiles/Goals/", StringComparison.OrdinalIgnoreCase)))
		{
			try
			{
				string text = ReadText(goal.Value, ref textBudget);
				if (!text.Contains("NRD_KillStory", StringComparison.Ordinal) && !text.Contains("NRD_BadCall", StringComparison.Ordinal)) continue;
				response.Features |= Features.EmbeddedModFixer;
				foreach (Module module in response.Modules.Where(module => goal.Key.StartsWith("Mods/" + module.Folder + "/", StringComparison.OrdinalIgnoreCase)))
					module.Features |= Features.EmbeddedModFixer;
			}
			catch (InvalidDataException) { response.Status = "Partial"; response.TechnicalDetails = "Some script evidence exceeded inspection limits."; }
		}
		if (response.Modules.Select(module => module.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count() != response.Modules.Count)
			throw new InvalidDataException("Multiple metadata files declare the same UUID.");
		if (response.Modules.Count != 0) response.Kind = "Modules";
		else if (response.Features.HasFlag(Features.GameFileOverrides)) response.Kind = "Override";
		else { response.Status = "Partial"; response.TechnicalDetails = "No module metadata or recognized base-game override content was found."; }
		foreach (string name in readFiles) CheckObserved(request, name);
		return response;
	}

	/// <summary>Checks a continuation against a real primary header; numbered filenames alone are not sufficient.</summary>
	private static Response ReadContinuation(Request request)
	{
		Match match = Regex.Match(request.FileName, @"^(.*)_([1-9][0-9]*)\.pak$", RegexOptions.IgnoreCase);
		if (!match.Success || !Int32.TryParse(match.Groups[2].Value, out int part)) throw new InvalidDataException("No supported package header was found.");
		string primaryName = match.Groups[1].Value + ".pak";
		CheckObserved(request, primaryName);
		string primaryPath = Path.Combine(request.Root, primaryName);
		using Package primary = ReadHeader(primaryPath) ?? throw new InvalidDataException("The proposed primary package has no valid header.");
		if (part >= primary.Metadata.NumParts || !Path.GetFileName(Package.MakePartFilename(primaryPath, part)).Equals(request.FileName, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("The primary package does not declare this continuation.");
		return new Response { FileName = request.FileName, Kind = "Continuation", PrimaryFileName = primaryName, ReadFiles = Parts(request, primary, primaryPath) };
	}

	/// <summary>Reads only the header so split membership and solid compression can be checked before decompression.</summary>
	private static Package ReadHeader(string path)
	{
		try { return new PackageReader().Read(path, metadataOnly: true); }
		catch (NotAPackageException) { return null; }
	}

	/// <summary>Checks every part declared by the header against the caller's observed inventory.</summary>
	private static List<string> Parts(Request request, Package header, string primaryPath)
	{
		if (header.Metadata.NumParts < 1 || header.Metadata.NumParts > 1024) throw new InvalidDataException("Unsupported package part count.");
		var files = new List<string>();
		for (int part = 0; part < header.Metadata.NumParts; part++)
		{
			string name = Path.GetFileName(part == 0 ? primaryPath : Package.MakePartFilename(primaryPath, part));
			CheckObserved(request, name); files.Add(name);
		}
		return files;
	}

	/// <summary>Preserves NMM file-link compatibility while rechecking the exact observed filenames and metadata.</summary>
	private static void CheckObserved(Request request, string name)
	{
		request.FilesByName ??= request.Files.ToDictionary(file => file.FileName, StringComparer.OrdinalIgnoreCase);
		if (!request.FilesByName.TryGetValue(name, out ObservedFile expected)) throw new InvalidDataException("A package part was not in the observed inventory.");
		var file = new FileInfo(Path.Combine(request.Root, expected.FileName));
		if (!file.Exists || file.Length != expected.Length || file.LastWriteTimeUtc.Ticks != expected.LastWriteTimeUtcTicks)
			throw new InvalidDataException("A package is missing or changed since enumeration.");
	}

	/// <summary>Reports positive script/override evidence; custom Public folders are not presumed to be overrides.</summary>
	private static Features DetectFeatures(IEnumerable<string> names)
	{
		Features features = Features.None;
		foreach (string name in names)
		{
			if (name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) features |= Features.LuaScripts;
			if (name.Contains("/Story/RawFiles/Goals/", StringComparison.OrdinalIgnoreCase)) features |= Features.OsirisScripts;
			if (name.EndsWith("/ScriptExtender/Config.json", StringComparison.OrdinalIgnoreCase)) features |= Features.ScriptExtender;
			if (name.EndsWith("/Story/RawFiles/Goals/ForceRecompile.txt", StringComparison.OrdinalIgnoreCase)) features |= Features.EmbeddedModFixer;
			if (Regex.IsMatch(name, @"^(Mods|Public)/(Shared|SharedDev|Gustav|GustavDev|GustavX)/", RegexOptions.IgnoreCase)) features |= Features.GameFileOverrides;
		}
		return features;
	}

	/// <summary>Bounds metadata before invoking decompression, and never writes archive content to disk.</summary>
	private static string ReadText(PackagedFileInfo file, ref long budget)
	{
		ulong size = file.Size();
		if (size > 1024 * 1024 || size > (ulong)Math.Max(budget, 0)) throw new InvalidDataException("Metadata or script inspection limit reached.");
		budget -= (long)size;
		using Stream stream = file.CreateContentReader();
		using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
		return Protocol.ReadBounded(reader, 1024 * 1024);
	}

	/// <summary>Disables DTDs and external XML resolution for package metadata.</summary>
	private static XDocument ReadXml(string text)
	{
		using XmlReader reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
		{ DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
		return XDocument.Load(reader);
	}

	/// <summary>Reads only a node's direct attributes, avoiding accidental capture of dependency values.</summary>
	private static string Attribute(XElement node, string id) => (string)node.Elements("attribute").SingleOrDefault(item => (string)item.Attribute("id") == id)?.Attribute("value");

	/// <summary>Reads declared relationships without inventing compatibility or download rules.</summary>
	private static Reference[] References(XDocument document, string container)
	{
		Reference[] references = document.Descendants("node").Where(node => (string)node.Attribute("id") == container)
			.SelectMany(node => node.Descendants("node").Where(item => (string)item.Attribute("id") == "ModuleShortDesc"))
			.Select(node => new Reference { Identity = Uuid(Attribute(node, "UUID")), Name = Attribute(node, "Name"),
				Version = Attribute(node, "Version64") ?? Attribute(node, "Version") }).Take(4097).ToArray();
		if (references.Length > 4096) throw new InvalidDataException("Too many declared relationships.");
		foreach (Reference reference in references) if (reference.Version != null) Integer(reference.Version);
		return references;
	}

	/// <summary>Canonicalizes valid nonempty module UUIDs.</summary>
	private static string Uuid(string value) => Guid.TryParse(value, out Guid guid) && guid != Guid.Empty ? guid.ToString("D") : throw new InvalidDataException("Invalid module UUID.");
	/// <summary>Retains large integer versions exactly.</summary>
	private static string Integer(string value) => UInt64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? value : throw new InvalidDataException("Invalid module version or publishing handle.");
	/// <summary>Bounds required metadata without inventing fallback identities or names.</summary>
	private static string Required(string value, int limit) => !String.IsNullOrWhiteSpace(value) && value.Length <= limit ? value : throw new InvalidDataException("Missing or oversized module metadata.");
}
