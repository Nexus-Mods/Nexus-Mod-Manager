using System.Text;
using System.Text.Json;

namespace Nexus.LarianPackageInspector;

/// <summary>Read-only package helper with legacy one-shot and bounded session transports.</summary>
internal static class Program
{
	/// <summary>Reserves stdout for the bounded protocol response; malformed requests fail on stderr.</summary>
	private static int Main(string[] args)
	{
		Console.InputEncoding = new UTF8Encoding(false, true);
		Console.OutputEncoding = new UTF8Encoding(false);
		if (args.Length == 1 && args[0] == "--session-v1")
		{
			try { RunSession(Console.OpenStandardInput(), Console.OpenStandardOutput()); return 0; }
			catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
		}
		if (args.Length != 0) { Console.Error.WriteLine("Unsupported inspection mode."); return 1; }
		Request request;
		try { request = Protocol.ReadRequest(); }
		catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
		Console.Out.Write(Inspect(request));
		return 0;
	}

	/// <summary>Reuses one validated observed inventory; subsequent frames contain only a sibling filename.</summary>
	internal static void RunSession(Stream input, Stream output)
	{
		Protocol.WriteFrame(output, "NMM-PAK-SESSION-1");
		string initial = Protocol.ReadFrame(input) ?? throw new InvalidDataException("Missing session inventory.");
		Request request = Protocol.ParseRequest(initial);
		var names = request.Files.Select(file => file.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
		for (int count = 0; ; count++)
		{
			Protocol.WriteFrame(output, Inspect(request));
			string name = Protocol.ReadFrame(input);
			if (name == null) return;
			if (count >= 9999) throw new InvalidDataException("Inspection session exceeds its package limit.");
			Protocol.FileName(name);
			if (!names.Contains(name)) throw new InvalidDataException("The input is absent from the session inventory.");
			request.FileName = name;
		}
	}

	/// <summary>Keeps a malformed package local to its response, without terminating the remaining scan.</summary>
	private static string Inspect(Request request)
	{
		Response response;
		try { response = PackageInspector.Inspect(request); }
		catch (Exception error)
		{
			string details = error.Message.Replace('\0', '\uFFFD');
			response = new Response { FileName = request.FileName, Status = "Failed", TechnicalDetails = details[..Math.Min(details.Length, 8192)] };
		}
		string json = JsonSerializer.Serialize(response, Protocol.Json);
		if (json.Length > 4 * 1024 * 1024)
			json = JsonSerializer.Serialize(new Response { FileName = request.FileName, Status = "Failed", TechnicalDetails = "Package metadata exceeds the response limit." }, Protocol.Json);
		return json;
	}
}
