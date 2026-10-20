using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Setup archive destination proposals, byte-preserving presets and unsafe archive rejection.</summary>
	[TestFixture]
	public class CollectionSetupPackageTests
	{
		/// <summary>A WhiteWolf-style wrapper resolves all four groups while leaving personal presets unchecked.</summary>
		[Test]
		public void WrappedPackage_MapsKnownTargetsAndPreservesRepeatedBindings()
		{
			using (var temporary = new TemporaryDirectory())
			{
				string path = Path.Combine(temporary.Path, "setup.zip");
				byte[] bindings = Encoding.UTF8.GetBytes("[Combat]\r\nIK_LeftMouse=(Action=Attack)\r\nIK_LeftMouse=(Action=Hold,State=Duration)\r\n");
				using (var output = new FileStream(path, FileMode.Create))
				using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
				{
					Write(zip, "All In One/User Settings/input.settings", bindings);
					Write(zip, "All In One/User Settings/dx12user.settings", Encoding.UTF8.GetBytes("[Viewport]\nResolution=2560x1440"));
					Write(zip, "All In One/Mod Load Orders/mods.settings", Encoding.UTF8.GetBytes("[modExample]\nEnabled=0\nPriority=1"));
					Write(zip, "All In One/Dx12filelist/dx12filelist.txt", Encoding.UTF8.GetBytes("menu.xml;"));
					Write(zip, "All In One/Script Merger Merges/mod0000_MergedFiles/content/scripts/game/example.ws", new byte[] { 1, 2, 3 });
					Write(zip, "All In One/User Settings/readme instruction.txt", Encoding.GetEncoding(1252).GetBytes("Copy to Documents/The Witcher 3. Don’t use the author's drive."));
				}
				CollectionSetupPackage package = CollectionSetupPackageReader.Read(path);
				Assert.AreEqual(5, package.Files.Count);
				CollectionSetupFile input = package.Files.Single(x => x.RelativePath == "input.settings");
				Assert.AreEqual(CollectionSetupRoot.Documents, input.Root); Assert.IsFalse(input.Selected);
				CollectionAssert.AreEqual(bindings, input.Content);
				Assert.IsFalse(package.Files.Single(x => x.RelativePath == "dx12user.settings").Selected);
				Assert.IsTrue(package.Files.Single(x => x.RelativePath == "mods.settings").Selected);
				Assert.AreEqual(CollectionSetupRoot.Game, package.Files.Single(x => x.RelativePath.EndsWith("dx12filelist.txt")).Root);
				Assert.AreEqual(@"Mods\mod0000_MergedFiles\content\scripts\game\example.ws", package.Files.Single(x => x.Source.EndsWith("example.ws")).RelativePath);
				StringAssert.Contains("Don’t", package.Readmes);
			}
		}

		/// <summary>Arbitrary README prose does not authorize an absolute destination or execute a command.</summary>
		[Test]
		public void UnrecognizedPayload_RemainsUnmapped()
		{
			using (var temporary = new TemporaryDirectory())
			{
				string path = Path.Combine(temporary.Path, "unknown.zip");
				using (var output = new FileStream(path, FileMode.Create))
				using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
				{
					Write(zip, "Preset/readme.txt", Encoding.UTF8.GetBytes("Run install.cmd and copy the output to C:/Windows."));
					Write(zip, "Preset/install.cmd", Encoding.UTF8.GetBytes("echo ignored"));
				}
				CollectionSetupFile file = CollectionSetupPackageReader.Read(path).Files.Single();
				Assert.AreEqual(CollectionSetupRoot.Unmapped, file.Root); Assert.IsFalse(file.Selected);
			}
		}

		/// <summary>Windows traversal, device aliases and alternate data streams cannot become setup destinations.</summary>
		[TestCase("../mods/file.ws")]
		[TestCase("/mods/file.ws")]
		[TestCase("mods/file.ws:payload")]
		[TestCase("mods/CON.txt")]
		[TestCase("mods/file. ")]
		public void UnsafePaths_AreRejected(string path)
		{
			Assert.Throws<InvalidDataException>(() => CollectionSetupPackageReader.Relative(path));
		}

		/// <summary>Case-colliding ZIP entries cannot be published as ambiguous Windows files.</summary>
		[Test]
		public void DuplicateArchivePaths_AreRejected()
		{
			using (var temporary = new TemporaryDirectory())
			{
				string path = Path.Combine(temporary.Path, "duplicate.zip");
				using (var output = new FileStream(path, FileMode.Create))
				using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
				{ Write(zip, "Preset/input.settings", new byte[] { 1 }); Write(zip, "preset/INPUT.settings", new byte[] { 2 }); }
				Assert.Throws<InvalidDataException>(() => CollectionSetupPackageReader.Read(path));
			}
		}

		/// <summary>Writes an isolated fixture archive member.</summary>
		private static void Write(ZipArchive zip, string name, byte[] bytes) { using (Stream output = zip.CreateEntry(name).Open()) output.Write(bytes, 0, bytes.Length); }
	}
}
