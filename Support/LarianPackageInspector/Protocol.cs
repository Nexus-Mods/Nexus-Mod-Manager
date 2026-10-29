using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.LarianPackageInspector;

/// <summary>Typed wire messages matching NMM's version-one package-inspection boundary.</summary>
internal static class Protocol
{
	internal const string Provider = "larian-module-settings-v1";
	internal static readonly JsonSerializerOptions Json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		MaxDepth = 32
	};

	/// <summary>Reads bounded UTF-8 input and validates a unique, flat observed package inventory.</summary>
	internal static Request ReadRequest()
	{
		return ParseRequest(ReadBounded(Console.In, 4 * 1024 * 1024));
	}

	/// <summary>Validates the inventory once at the start of either a legacy request or a framed inspection session.</summary>
	internal static Request ParseRequest(string input)
	{
		if (input.Length > 4 * 1024 * 1024) throw new InvalidDataException("Inspection request exceeds its size limit.");
		using JsonDocument document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 32 });
		RejectDuplicateKeys(document.RootElement);
		Request request = JsonSerializer.Deserialize<Request>(input, Json);
		if (request.SchemaVersion != 1 || request.Provider != Provider || !Path.IsPathFullyQualified(request.Root) ||
			request.Files == null || request.Files.Length == 0 || request.Files.Length > 10000)
			throw new InvalidDataException("Unsupported request or package inventory.");
		request.Root = Path.GetFullPath(request.Root);
		FileName(request.FileName);
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ObservedFile file in request.Files)
		{
			if (file == null) throw new InvalidDataException("Null package inventory entry.");
			FileName(file.FileName);
			if (!names.Add(file.FileName) || file.Length < 0 || file.LastWriteTimeUtcTicks < 0 || file.LastWriteTimeUtcTicks > DateTime.MaxValue.Ticks)
				throw new InvalidDataException("Invalid or duplicate package inventory entry.");
		}
		request.FilesByName = request.Files.ToDictionary(file => file.FileName, StringComparer.OrdinalIgnoreCase);
		if (!names.Contains(request.FileName)) throw new InvalidDataException("The input is absent from its observed inventory.");
		return request;
	}

	/// <summary>Reads one bounded UTF-8 frame, allowing clean EOF only between requests.</summary>
	internal static string ReadFrame(Stream input)
	{
		int first = input.ReadByte();
		if (first < 0) return null;
		byte[] header = new byte[4]; header[0] = (byte)first;
		ReadExact(input, header, 1, 3);
		int length = header[0] | header[1] << 8 | header[2] << 16 | header[3] << 24;
		if (length < 1 || length > 16 * 1024 * 1024) throw new InvalidDataException("Invalid inspection frame length.");
		byte[] bytes = new byte[length]; ReadExact(input, bytes, 0, length);
		return new System.Text.UTF8Encoding(false, true).GetString(bytes);
	}

	/// <summary>Writes one bounded response without waiting for process exit or closing stdout.</summary>
	internal static void WriteFrame(Stream output, string text)
	{
		byte[] bytes = new System.Text.UTF8Encoding(false, true).GetBytes(text);
		if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Inspection response exceeds its frame limit.");
		output.WriteByte((byte)bytes.Length); output.WriteByte((byte)(bytes.Length >> 8));
		output.WriteByte((byte)(bytes.Length >> 16)); output.WriteByte((byte)(bytes.Length >> 24));
		output.Write(bytes); output.Flush();
	}

	/// <summary>Rejects truncated frames rather than treating a partial message as a package result.</summary>
	private static void ReadExact(Stream input, byte[] buffer, int offset, int count)
	{
		while (count > 0)
		{
			int read = input.Read(buffer, offset, count);
			if (read == 0) throw new EndOfStreamException("Truncated inspection frame.");
			offset += read; count -= read;
		}
	}

	/// <summary>Prevents traversal, device names and alternate streams in sibling filenames.</summary>
	internal static void FileName(string name)
	{
		if (String.IsNullOrWhiteSpace(name) || name.Length > 255 || name != Path.GetFileName(name) ||
			name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(' ') || name.EndsWith('.') ||
			!Path.GetExtension(name).Equals(".pak", StringComparison.OrdinalIgnoreCase) ||
			System.Text.RegularExpressions.Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
			throw new InvalidDataException("A safe top-level .pak filename is required.");
	}

	/// <summary>Bounds decoded text before parsing package metadata or process input.</summary>
	internal static string ReadBounded(TextReader reader, int maximum)
	{
		var text = new System.Text.StringBuilder();
		char[] buffer = new char[4096];
		int count;
		while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
		{
			if (text.Length + count > maximum) throw new InvalidDataException("Inspection text exceeds its size limit.");
			text.Append(buffer, 0, count);
		}
		return text.ToString();
	}

	/// <summary>Rejects ambiguous JSON properties recursively before deserialization.</summary>
	private static void RejectDuplicateKeys(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (JsonProperty property in element.EnumerateObject())
			{
				if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property.");
				RejectDuplicateKeys(property.Value);
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
			foreach (JsonElement child in element.EnumerateArray()) RejectDuplicateKeys(child);
	}
}

/// <summary>A single package read request; all paths are supplied by NMM's observed inventory.</summary>
internal sealed class Request
{
	[JsonRequired] public int SchemaVersion { get; set; }
	[JsonRequired] public string Provider { get; set; }
	[JsonRequired] public string Root { get; set; }
	[JsonRequired] public string FileName { get; set; }
	[JsonRequired] public ObservedFile[] Files { get; set; }
	internal Dictionary<string, ObservedFile> FilesByName { get; set; }
}

/// <summary>Filesystem metadata used to detect changes around the actual read.</summary>
internal sealed class ObservedFile
{
	[JsonRequired] public string FileName { get; set; }
	[JsonRequired] public long Length { get; set; }
	[JsonRequired] public long LastWriteTimeUtcTicks { get; set; }
}

/// <summary>Positive feature observations, serialized using the existing protocol's enum names.</summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<Features>))]
internal enum Features { None = 0, LuaScripts = 1, OsirisScripts = 2, ScriptExtender = 4, EmbeddedModFixer = 8, GameFileOverrides = 16 }

/// <summary>Response envelope kept independent of the .NET Framework application's assemblies.</summary>
internal sealed class Response
{
	public int SchemaVersion { get; set; } = 1;
	public string Provider { get; set; } = Protocol.Provider;
	public string FileName { get; set; }
	public string Status { get; set; } = "Complete";
	public string Kind { get; set; } = "Unknown";
	public Features Features { get; set; }
	public List<string> ReadFiles { get; set; } = new();
	public List<Module> Modules { get; set; } = new();
	public string PrimaryFileName { get; set; }
	public string TechnicalDetails { get; set; }
}

/// <summary>One module's own metadata and scoped content observations.</summary>
internal sealed class Module
{
	public string Identity { get; set; }
	public string Name { get; set; }
	public string Folder { get; set; }
	public string Version { get; set; }
	public string Kind { get; set; } = "AddOn";
	public Features Features { get; set; }
	public int? RequiredScriptExtenderVersion { get; set; }
	public string Author { get; set; }
	public string Description { get; set; }
	public string Md5 { get; set; }
	public string PublishHandle { get; set; }
	public string[] Tags { get; set; } = Array.Empty<string>();
	public Reference[] Dependencies { get; set; } = Array.Empty<Reference>();
	public Reference[] Conflicts { get; set; } = Array.Empty<Reference>();
}

/// <summary>A declared dependency or conflict without inferred compatibility decisions.</summary>
internal sealed class Reference
{
	public string Identity { get; set; }
	public string Name { get; set; }
	public string Version { get; set; }
}
