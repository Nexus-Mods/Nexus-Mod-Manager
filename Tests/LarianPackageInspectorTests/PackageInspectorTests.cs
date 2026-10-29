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
	[Test]
	public async Task RecognizesBaseGameOverrideEvidence()
	{
		using var fixture = new PackageFixture();
		fixture.Add("Public/Shared/Example.txt", "fixture");
		Response response = PackageInspector.Inspect(await fixture.Pack());
		Assert.That(response.Kind, Is.EqualTo("Override"));
		Assert.That(response.Features.HasFlag(Features.GameFileOverrides), Is.True);
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
		/// <summary>Creates one source file in the isolated fixture tree.</summary>
		internal void Add(string relative, string text)
		{
			string path = Path.Combine(_root, "Source", relative);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, text);
		}
		/// <summary>Builds a real package fixture through the pinned package library.</summary>
		internal async Task<Request> Pack(string name = "Example.pak")
		{
			string path = Path.Combine(_root, name);
			await new Packager().CreatePackage(path, Path.Combine(_root, "Source"), new PackageBuildData());
			var info = new FileInfo(path);
			return new Request { SchemaVersion = 1, Provider = Protocol.Provider, Root = _root, FileName = name,
				Files = new[] { new ObservedFile { FileName = name, Length = info.Length, LastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks } } };
		}
		/// <summary>Removes only this fixture's temporary directory.</summary>
		public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
	}
}
