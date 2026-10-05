using System;
using System.IO;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	[Category("CollectionsC12Compatibility")]
	public class VortexBinaryPatchTests
	{
		[Test]
		public void Bsdiff40_AppliesKnownPatchAndValidatesCrc32()
		{
			byte[] source = Encoding.ASCII.GetBytes("abc");
			byte[] patch = Convert.FromBase64String("QlNESUZGNDApAAAAAAAAACUAAAAAAAAAAwAAAAAAAABCWmg5MUFZJlNZ/6MlEwAAAmAASAAIACAAMMwM9QXOLuSKcKEh/0ZKJkJaaDkxQVkmU1lMWp9IAAAAQABgACAAIQCCjF3JFOFCQTFqfSBCWmg5F3JFOFCQAAAAAA==");

			Assert.AreEqual("352441C2", VortexCrc32.ComputeUpperHex(source));
			CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("abd"), VortexBsdiffPatchApplier.Apply(source, patch));
		}

		[Test]
		public void Normalize_CharacterizedBinaryPatchIsSupportedAndCanonicalized()
		{
			string json = "{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/a\",\"name\":\"Example\",\"description\":\"Example\",\"domainName\":\"skyrim\"}," +
				"\"mods\":[{\"name\":\"Patched Mod\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20},\"patches\":{\"textures/foo.bin\":\"352441c2\"}}]," +
				"\"modRules\":[]}";
			CollectionIdentity collection = CollectionIdentity.FromNexus("2210");
			var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "772530", 100), null, null, 1);

			NexusCollectionManifestNormalizationResult result = new NexusCollectionManifestNormalizer().Normalize(Encoding.UTF8.GetBytes(json), revision);

			Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
			Assert.IsTrue(result.Manifest.Members[0].HasVortexBinaryPatches);
			Assert.AreEqual("textures\\foo.bin", result.Manifest.Members[0].VortexBinaryPatches.Items[0].DestinationPath);
			Assert.AreEqual("352441C2", result.Manifest.Members[0].VortexBinaryPatches.Items[0].SourceCrc32);
		}

		[TestCase("../escape.bin")]
		[TestCase("C:\\escape.bin")]
		[TestCase("/rooted.bin")]
		public void BinaryPatch_RejectsUnsafeDestination(string path)
		{
			Assert.Throws<InvalidDataException>(() => new CollectionVortexBinaryPatch(path, "352441C2"));
		}
	}
}
