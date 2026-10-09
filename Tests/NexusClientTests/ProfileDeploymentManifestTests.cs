using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Regression coverage for distinct profile identities and recovery of duplicate-ID snapshots.</summary>
	[TestFixture]
	public class ProfileDeploymentManifestTests
	{
		[Test]
		public void Capture_ClaimsEachPreviousIdentityOnlyOnce()
		{
			var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string first = ClaimId("existing-id", used);
			string second = ClaimId("EXISTING-ID", used);
			string third = ClaimId(null, used);
			Assert.AreEqual("existing-id", first);
			Assert.AreNotEqual(first, second);
			Assert.AreNotEqual(second, third);
			Assert.AreEqual(3, used.Count);
			Assert.AreEqual("other-existing-id", ClaimId("other-existing-id", used));
		}

		[Test]
		public void Capture_RecoversMixedInstallIdentitiesWithoutReusingAmbiguousOwnerReferences()
		{
			WithManifest(true, path =>
			{
				byte[] original = File.ReadAllBytes(path);
				ProfileDeploymentManifest recovered = Read(path, true);
				Assert.AreEqual(2, recovered.Mods.Count);
				Assert.AreEqual(2, recovered.Mods.Select(x => x.ProfileModId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
				Assert.AreEqual("shared-id", recovered.Mods[0].ProfileModId);
				Assert.AreEqual("one.7z", recovered.Mods[0].FileName);
				Assert.AreEqual("two.7z", recovered.Mods[1].FileName);
				Assert.AreEqual(ModInstallMethod.Virtual, recovered.Mods[0].Method);
				Assert.AreEqual(ModInstallMethod.Direct, recovered.Mods[1].Method);
				Assert.AreEqual(ModInstallRoot.GameRoot, recovered.Mods[1].InstallRoot);
				Assert.IsEmpty(recovered.Targets, "Capture must rebuild owners from native state, not guess between duplicate IDs.");
				CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
				CollectionAssert.AreEqual(original, File.ReadAllBytes(path + ".duplicate-identities.bak"));

				// Fresh native ownership can reference both distinct recovered IDs and round-trip strictly.
				var target = new ProfileDeploymentTargetState
				{
					Target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "shared.txt")
				};
				target.Owners.Add(new ProfileDeploymentOwner { IsOriginal = true });
				foreach (ProfileDeploymentMod mod in recovered.Mods)
					target.Owners.Add(new ProfileDeploymentOwner { ProfileModId = mod.ProfileModId });
				recovered.Targets.Add(target);
				string repaired = Path.Combine(Path.GetDirectoryName(path), "repaired.xml");
				Write(repaired, recovered);
				ProfileDeploymentManifest roundTrip = Read(repaired, false);
				Assert.AreEqual(2, roundTrip.Mods.Count);
				CollectionAssert.AreEqual(recovered.Mods.Select(x => x.ProfileModId).ToArray(),
					roundTrip.Targets.Single().Owners.Where(x => !x.IsOriginal).Select(x => x.ProfileModId).ToArray());
			});
		}

		[Test]
		public void Capture_BackupIsIdempotentAndDoesNotOverwriteAnEarlierDifferentBackup()
		{
			WithManifest(true, path =>
			{
				byte[] earlier = Encoding.UTF8.GetBytes("earlier manifest");
				File.WriteAllBytes(path + ".duplicate-identities.bak", earlier);
				byte[] original = File.ReadAllBytes(path);
				Read(path, true);
				Read(path, true);
				CollectionAssert.AreEqual(earlier, File.ReadAllBytes(path + ".duplicate-identities.bak"));
				CollectionAssert.AreEqual(original, File.ReadAllBytes(path + ".duplicate-identities.1.bak"));
				Assert.AreEqual(2, Directory.GetFiles(Path.GetDirectoryName(path), "*.bak").Length);
			});
		}

		[Test]
		public void Restore_RejectsDuplicateIdentitiesAndDoesNotModifyTheProfile()
		{
			WithManifest(true, path =>
			{
				byte[] original = File.ReadAllBytes(path);
				TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => Read(path, false));
				Assert.IsInstanceOf<InvalidDataException>(error.InnerException);
				CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
				Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(path), "*.bak"));
			});
		}

		[Test]
		public void Capture_LeavesValidIdsAndOwnerStacksUnchanged()
		{
			WithManifest(false, path =>
			{
				ProfileDeploymentManifest manifest = Read(path, true);
				CollectionAssert.AreEqual(new[] { "shared-id", "second-id" }, manifest.Mods.Select(x => x.ProfileModId).ToArray());
				Assert.AreEqual("shared-id", manifest.Targets.Single().Owners.Last().ProfileModId);
				Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(path), "*.bak"));
			});
		}

		[Test]
		public void Capture_DoesNotTreatAnUnsupportedSchemaAsDuplicateIdRecovery()
		{
			WithManifest(true, path =>
			{
				XDocument document = XDocument.Load(path);
				document.Root.SetAttributeValue("fileVersion", "99.0.0.0");
				document.Save(path);
				TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => Read(path, true));
				Assert.IsInstanceOf<InvalidDataException>(error.InnerException);
				Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(path), "*.bak"));
			});
		}

		/// <summary>Invokes the private allocator through the same seam used by live profile capture.</summary>
		private static string ClaimId(string preferred, ISet<string> used)
		{
			return (string)typeof(ProfileManager).GetMethod("ClaimProfileModId", BindingFlags.Static | BindingFlags.NonPublic)
				.Invoke(null, new object[] { preferred, used });
		}

		/// <summary>Reads a persisted profile in strict restore mode or active-profile capture mode.</summary>
		private static ProfileDeploymentManifest Read(string path, bool captureCurrent)
		{
			return (ProfileDeploymentManifest)typeof(ProfileManager).GetMethod("ReadDeploymentManifest", BindingFlags.Static | BindingFlags.NonPublic)
				.Invoke(null, new object[] { path, captureCurrent });
		}

		/// <summary>Serializes the reconstructed profile using the production manifest writer.</summary>
		private static void Write(string path, ProfileDeploymentManifest manifest)
		{
			var document = (XDocument)typeof(ProfileManager).GetMethod("WriteDeploymentManifest", BindingFlags.Static | BindingFlags.NonPublic)
				.Invoke(null, new object[] { manifest });
			document.Save(path);
		}

		/// <summary>Creates an isolated profile manifest with shared Nexus identity metadata and optional duplicate profile IDs.</summary>
		private static void WithManifest(bool duplicate, Action<string> action)
		{
			string directory = Path.Combine(Path.GetTempPath(), "NmmProfileIdentities-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				string path = Path.Combine(directory, "deployment.xml");
				var mods = new XElement("mods");
				mods.Add(new XElement("mod", new XAttribute("profileId", "shared-id"), new XAttribute("fileName", "one.7z"),
					new XAttribute("modId", "123"), new XAttribute("version", "1.0"), new XAttribute("installMethod", "Virtual"), new XAttribute("installRoot", "Data")));
				mods.Add(new XElement("mod", new XAttribute("profileId", duplicate ? "SHARED-ID" : "second-id"), new XAttribute("fileName", "two.7z"),
					new XAttribute("modId", "123"), new XAttribute("version", "1.0"), new XAttribute("installMethod", "Direct"), new XAttribute("installRoot", "GameRoot")));
				var targets = new XElement("targets", new XElement("file", new XAttribute("root", "Data"), new XAttribute("path", "shared.txt"),
					new XElement("owners", new XElement("original"), new XElement("mod", new XAttribute("profileId", "shared-id")))));
				new XDocument(new XElement("deployment", new XAttribute("fileVersion", "1.0.0.0"), mods, targets)).Save(path);
				action(path);
			}
			finally { Directory.Delete(directory, true); }
		}
	}
}
