using System.Text;
using System.Text.Json;

namespace Nexus.LarianPackageInspector;

/// <summary>One-request, read-only package helper. It never extracts files, executes scripts or starts children.</summary>
internal static class Program
{
	/// <summary>Reserves stdout for the bounded protocol response; malformed requests fail on stderr.</summary>
	private static int Main()
	{
		Console.InputEncoding = new UTF8Encoding(false, true);
		Console.OutputEncoding = new UTF8Encoding(false);
		Request request;
		try { request = Protocol.ReadRequest(); }
		catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
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
		Console.Out.Write(json);
		return 0;
	}
}
