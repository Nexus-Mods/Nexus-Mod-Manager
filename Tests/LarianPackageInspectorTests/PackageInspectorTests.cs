using LSLib.LS;
using Nexus.LarianPackageInspector;
using NUnit.Framework;

namespace LarianPackageInspectorTests;

/// <summary>Isolated package fixtures. Building and running these tests is reserved for the maintainer.</summary>
[TestFixture]
public sealed class PackageInspectorTests
{
	/// <summary>Multiple modules retain distinct relationships and per-module script evidence.</summary>
	[Test]
	public async Task ReadsMultipleModulesAndScopedScriptFeatures()
	{
		using var fixture = new PackageFixture();
		fixture.Add("Mods/First/meta.lsx", Metadata("First", "11111111-1111-1111-1111-111111111111"));
		fixture.Add("Mods/Second/meta.lsx", Metadata("Second", "22222222-2222-2222-2222-222222222222"));
		fixture.Add("Mods/First/ScriptExtender/Config.json", "{\"RequiredVersion\":29,\"FeatureFlags\":[\"Lua\"]}");
		fixture.Add("Mods/First/ScriptExtender/Lua/BootstrapServer.lua", "-- fixture");
		fixture.Add("Mods/Second/Story/RawFiles/Goals/ForceRecompile.txt", "// fixture");
		Response response = PackageInspector.Inspect(await fixture.Pack());
		Assert.That(response.Status, Is.EqualTo("Complete"));
		Assert.That(response.Modules.Count, Is.EqualTo(2));
		Module first = response.Modules.Single(module => module.Folder == "First");
		Module second = response.Modules.Single(module => module.Folder == "Second");
		Assert.That(first.RequiredScriptExtenderVersion, Is.EqualTo(29));
		Assert.That(first.Features.HasFlag(Features.LuaScripts), Is.True);
		Assert.That(second.Features.HasFlag(Features.LuaScripts), Is.False);
		Assert.That(second.Features.HasFlag(Features.EmbeddedModFixer), Is.True);
		Assert.That(first.Version, Is.EqualTo("18446744073709551615"));
	}

	/// <summary>Framed sessions send their inventory once and continue after a package-local parse failure.</summary>
	[Test]
	public async Task SessionContinuesAfterMalformedPackage()
	{
		using var fixture = new PackageFixture();
		fixture.Add("Mods/First/meta.lsx", Metadata("First", "11111111-1111-1111-1111-111111111111"));
		Request request = await fixture.Pack();
		string goodName = request.FileName;
		ObservedFile broken = fixture.CorruptPackage();
		request.Files = request.Files.Append(broken).ToArray(); request.FileName = broken.FileName;
		using var input = new MemoryStream(); using var output = new MemoryStream();
		Protocol.WriteFrame(input, System.Text.Json.JsonSerializer.Serialize(request, Protocol.Json));
		Protocol.WriteFrame(input, goodName); input.Position = 0;
		Nexus.LarianPackageInspector.Program.RunSession(input, output); output.Position = 0;
		Assert.That(Protocol.ReadFrame(output), Is.EqualTo("NMM-PAK-SESSION-1"));
		using var failed = System.Text.Json.JsonDocument.Parse(Protocol.ReadFrame(output));
		using var success = System.Text.Json.JsonDocument.Parse(Protocol.ReadFrame(output));
		Assert.That(failed.RootElement.GetProperty("status").GetString(), Is.EqualTo("Failed"));
		Assert.That(success.RootElement.GetProperty("status").GetString(), Is.EqualTo("Complete"));
		Assert.That(success.RootElement.GetProperty("fileName").GetString(), Is.EqualTo(goodName));
		Assert.That(Protocol.ReadFrame(output), Is.Null);
	}

	/// <summary>Framing must reject oversized or truncated messages before allocating arbitrary process output.</summary>
	[Test]
	public void SessionRejectsInvalidFrames()
	{
		Assert.Throws<InvalidDataException>(() => Protocol.ReadFrame(new MemoryStream(new byte[] { 0, 0, 0, 127 })));
		Assert.Throws<EndOfStreamException>(() => Protocol.ReadFrame(new MemoryStream(new byte[] { 4, 0, 0, 0, 65 })));
		Assert.Throws<EndOfStreamException>(() => Protocol.ReadFrame(new MemoryStream(new byte[] { 4, 0 })));
	}

	/// <summary>Missing metadata alone is not sufficient evidence of a valid override package.</summary>
	[Test]
	public async Task UnclassifiedContentRemainsPartial()
	{
		using var fixture = new PackageFixture();
		fixture.Add("Public/UnknownModule/Example.txt", "fixture");
		Response response = PackageInspector.Inspect(await fixture.Pack());
		Assert.That(response.Status, Is.EqualTo("Partial"));
		Assert.That(response.Kind, Is.EqualTo("Unknown"));
	}

	/// <summary>Recognized base-game paths distinguish actual override evidence from metadata absence.</summary>
	[TestCase("Public/Shared/Example.txt")]
	[TestCase("Public/Game/GUI/Widgets/Dialogue.xaml")]
	public async Task RecognizesBaseGameOverrideEvidence(string path)
	{
		using var fixture = new PackageFixture();
		fixture.Add(path, "fixture");
		Response response = PackageInspector.Inspect(await fixture.Pack());
		Assert.That(response.Status, Is.EqualTo("Complete"));
		Assert.That(response.Kind, Is.EqualTo("Override"));
		Assert.That(response.Features.HasFlag(Features.GameFileOverrides), Is.True);
	}

	/// <summary>Empty optional publishing handles do not invalidate otherwise complete module metadata.</summary>
	[TestCase("", null)]
	[TestCase(" ", null)]
	[TestCase("0", "0")]
	[TestCase("18446744073709551615", "18446744073709551615")]
	public async Task ReadsOptionalPublishingHandles(string value, string expected)
	{
		using var fixture = new PackageFixture();
		fixture.Add("Mods/First/meta.lsx", Metadata("First", "11111111-1111-1111-1111-111111111111")
			.Replace("</node>", $"<attribute id=\"PublishHandle\" value=\"{value}\"/></node>"));
		Response response = PackageInspector.Inspect(await fixture.Pack());
		Assert.That(response.Status, Is.EqualTo("Complete"));
		Assert.That(response.Modules.Single().PublishHandle, Is.EqualTo(expected));
		Assert.That(response.Modules.Single().Version, Is.EqualTo("18446744073709551615"));
	}

	/// <summary>Optional handles still reject nonnumeric, negative and overflowing populated values.</summary>
	[TestCase("invalid")]
	[TestCase("-1")]
	[TestCase("18446744073709551616")]
	public async Task RejectsInvalidPublishingHandles(string value)
	{
		using var fixture = new PackageFixture();
		fixture.Add("Mods/First/meta.lsx", Metadata("First", "11111111-1111-1111-1111-111111111111")
			.Replace("</node>", $"<attribute id=\"PublishHandle\" value=\"{value}\"/></node>"));
		Request request = await fixture.Pack();
		Assert.Throws<InvalidDataException>(() => PackageInspector.Inspect(request));
	}

	/// <summary>Repeated assets and editor files do not obscure a unique module record or script observations.</summary>
	[TestCase("Mods/First/GUI/Library/Elements.xaml")]
	[TestCase("Mods/First/mod_publish_logo.png")]
	[TestCase(".vscode/settings.json")]
	public async Task ReadsModulesWithDuplicateUninspectedEntries(string path)
	{
		using var fixture = new PackageFixture();
		fixture.Add("Mods/First/meta.lsx", Metadata("First", "11111111-1111-1111-1111-111111111111"));
		fixture.Add("Mods/First/ScriptExtender/Lua/BootstrapServer.lua", "-- fixture");
		fixture.AddIndexed(path, "first asset");
		fixture.AddIndexed(path.ToUpperInvariant().Replace('/', '\\'), "second asset");
		Response response = PackageInspector.Inspect(await fixture.Pack());
		Assert.That(response.Status, Is.EqualTo("Complete"));
		Assert.That(response.Kind, Is.EqualTo("Modules"));
		Assert.That(response.Modules.Single().Folder, Is.EqualTo("First"));
		Assert.That(response.Modules.Single().Features.HasFlag(Features.LuaScripts), Is.True);
	}

	/// <summary>Inspected metadata and extender configuration remain ambiguous when indexed more than once.</summary>
	[TestCase("Mods/First/meta.lsx")]
	[TestCase("Mods/First/ScriptExtender/Config.json")]
	public async Task RejectsDuplicateInspectedMetadata(string path)
	{
		using var fixture = new PackageFixture();
		fixture.Add("Mods/First/meta.lsx", Metadata("First", "11111111-1111-1111-1111-111111111111"));
		fixture.Add("Mods/First/ScriptExtender/Config.json", "{\"RequiredVersion\":29}");
		fixture.AddIndexed(path.ToUpperInvariant(), "ambiguous content");
		Request request = await fixture.Pack();
		Assert.Throws<InvalidDataException>(() => PackageInspector.Inspect(request));
	}

	/// <summary>Duplicate story text remains partial evidence instead of choosing an arbitrary indexed entry.</summary>
	[Test]
	public async Task DuplicateScriptEvidenceRemainsPartial()
	{
		using var fixture = new PackageFixture();
		fixture.Add("Mods/First/meta.lsx", Metadata("First", "11111111-1111-1111-1111-111111111111"));
		fixture.AddIndexed("Mods/First/Story/RawFiles/Goals/Example.txt", "NRD_KillStory");
		fixture.AddIndexed("Mods/First/Story/RawFiles/Goals/Example.txt", "// different content");
		Response response = PackageInspector.Inspect(await fixture.Pack());
		Assert.That(response.Status, Is.EqualTo("Partial"));
		Assert.That(response.TechnicalDetails, Does.Contain("Ambiguous"));
		Assert.That(response.Features.HasFlag(Features.OsirisScripts), Is.True);
		Assert.That(response.Features.HasFlag(Features.EmbeddedModFixer), Is.False);
	}

	/// <summary>Accepting repeated assets does not relax indexed path validation.</summary>
	[TestCase("../Other/Example.txt")]
	[TestCase("Public/../Shared/Example.txt")]
	[TestCase("/Public/Shared/Example.txt")]
	[TestCase("C:/Public/Shared/Example.txt")]
	public async Task RejectsUnsafeIndexedPaths(string path)
	{
		using var fixture = new PackageFixture();
		fixture.AddIndexed(path, "fixture");
		Request request = await fixture.Pack();
		Assert.Throws<InvalidDataException>(() => PackageInspector.Inspect(request));
	}

	/// <summary>Untrusted XML cannot resolve files or external entities.</summary>
	[Test]
	public async Task RejectsMetadataWithDtd()
	{
		using var fixture = new PackageFixture();
		fixture.Add("Mods/First/meta.lsx", "<!DOCTYPE save [<!ENTITY test 'blocked'>]><save>&test;</save>");
		Request request = await fixture.Pack();
		Assert.Throws<System.Xml.XmlException>(() => PackageInspector.Inspect(request));
	}

	/// <summary>A numbered standalone package is not mistaken for a continuation without a declaring primary header.</summary>
	[Test]
	public async Task NumberedStandalonePackageKeepsItsOwnMetadata()
	{
		using var fixture = new PackageFixture();
		fixture.Add("Mods/First/meta.lsx", Metadata("First", "11111111-1111-1111-1111-111111111111"));
		Response response = PackageInspector.Inspect(await fixture.Pack("Example_1.pak"));
		Assert.That(response.Kind, Is.EqualTo("Modules"));
		Assert.That(response.PrimaryFileName, Is.Null);
	}

	/// <summary>Provides exact unsigned version metadata without a floating-point representation.</summary>
	private static string Metadata(string folder, string uuid) => $"<save><node id=\"ModuleInfo\"><attribute id=\"UUID\" value=\"{uuid}\"/><attribute id=\"Name\" value=\"{folder}\"/><attribute id=\"Folder\" value=\"{folder}\"/><attribute id=\"Version64\" value=\"18446744073709551615\"/></node></save>";

	/// <summary>Owns only uniquely named temporary inputs and packages.</summary>
	private sealed class PackageFixture : IDisposable
	{
		private readonly string _root = Path.Combine(Path.GetTempPath(), "nmm-pak-fixture-" + Guid.NewGuid().ToString("N"));
		private readonly List<PackageBuildInputFile> _indexedFiles = new();
		/// <summary>Creates one source file in the isolated fixture tree.</summary>
		internal void Add(string relative, string text)
		{
			string path = Path.Combine(_root, "Source", relative);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, text);
		}
		/// <summary>Adds an exact index entry, including names that cannot be represented as distinct filesystem inputs.</summary>
		internal void AddIndexed(string relative, string text)
		{
			_indexedFiles.Add(PackageBuildInputFile.CreateFromBlob(System.Text.Encoding.UTF8.GetBytes(text), relative));
		}
		/// <summary>Adds one malformed observed PAK without invoking a process.</summary>
		internal ObservedFile CorruptPackage()
		{
			string path = Path.Combine(_root, "Broken.pak"); File.WriteAllText(path, "not a package");
			var info = new FileInfo(path);
			return new ObservedFile { FileName = info.Name, Length = info.Length, LastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks };
		}
		/// <summary>Builds a real package fixture through the pinned package library.</summary>
		internal async Task<Request> Pack(string name = "Example.pak")
		{
			string path = Path.Combine(_root, name);
			string source = Path.Combine(_root, "Source");
			Directory.CreateDirectory(source);
			await new Packager().CreatePackage(path, source, new PackageBuildData { Files = _indexedFiles.ToList() });
			var info = new FileInfo(path);
			return new Request { SchemaVersion = 1, Provider = Protocol.Provider, Root = _root, FileName = name,
				Files = new[] { new ObservedFile { FileName = name, Length = info.Length, LastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks } } };
		}
		/// <summary>Removes only this fixture's temporary directory.</summary>
		public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
	}
}
