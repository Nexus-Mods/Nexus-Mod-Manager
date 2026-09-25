using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Scripting;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C7.4 scripted replay/generated-payload retention characterization.
	/// </summary>
	public class CollectionScriptedReplayCaptureServiceTests
	{
		[Test]
		public void Capture_CompleteReplayRetainsXmlGeneratedPayloadAndVerifiedRecipeAssociation()
		{
			string root = CreateTemporaryDirectory("nmm-c74-complete-");
			try
			{
				string replayPath = Path.Combine(root, "InstallInfo", "Scripted", "Example.xml");
				var cache = new ScriptedFileSelectionCache(replayPath);
				cache.RecordSelection("archive\\one.bin", "Data\\one.bin");
				cache.RecordGeneratedFile("Data\\generated.bin", Encoding.UTF8.GetBytes("generated-content"));

				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c74-complete");
				InstallLogReadSnapshot install = CreateInstall("native-a", "Example.zip", true);
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install,
					new[] { CreateReplayReference("native-a", replayPath) });
				CollectionInstalledMemberProvenance provenance = CreateProvenance(target);
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, install,
					new[] { CreateInstalledMod("native-a", "Example.zip", true, new[] { provenance }) });
				CollectionsStore store;
				CollectionScriptedReplayCaptureService service = CreateService(root, out store);
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(
					Guid.Parse("20000000-0000-0000-0000-000000000001"));

				CollectionScriptedReplaySnapshot snapshot = service.Capture(target, captureIdentity,
					nativeState, identities, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.Coverage);
				Assert.AreEqual(1, snapshot.ArtifactSets.Count);
				CollectionScriptedReplayArtifactSet artifactSet = snapshot.ArtifactSets[0];
				Assert.AreEqual("native-a", artifactSet.NativeSnapshotKey);
				Assert.IsTrue(artifactSet.CompleteReplayFormat);
				Assert.IsTrue(artifactSet.ReplayPlanValidated);
				Assert.AreEqual(2, artifactSet.ReplayOperationCount);
				Assert.AreEqual(1, artifactSet.GeneratedPayloads.Count);
				Assert.AreEqual(1, artifactSet.GeneratedPayloads[0].ReplayOperationIndex);
				Assert.AreEqual("generated.bin", artifactSet.GeneratedPayloads[0].PayloadFileName);
				Assert.AreEqual("recipe-c74", artifactSet.Provenance.Single().VerifiedRecipe.Fingerprint);
				Assert.AreEqual(2, new CollectionsRetainedArtifactReferenceStore(store)
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString()).Count);
				Assert.IsTrue(new CollectionsRetainedArtifactStore(store).VerifyArtifact(artifactSet.ReplayXml.StableArtifactId));
				AssertRetainedText(store, artifactSet.GeneratedPayloads[0].RetainedArtifact, "generated-content");
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_LegacyReplayRetainsXmlButMarksCapturePartial()
		{
			string root = CreateTemporaryDirectory("nmm-c74-legacy-");
			try
			{
				string replayPath = Path.Combine(root, "InstallInfo", "Scripted", "Legacy.xml");
				Directory.CreateDirectory(Path.GetDirectoryName(replayPath));
				File.WriteAllText(replayPath,
					"<FileList><File FileFrom=\"archive\\one.bin\" FileTo=\"Data\\one.bin\" /></FileList>", Encoding.UTF8);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c74-legacy");
				InstallLogReadSnapshot install = CreateInstall("native-legacy", "Legacy.zip", true);
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install,
					new[] { CreateReplayReference("native-legacy", replayPath) });
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, install,
					new[] { CreateInstalledMod("native-legacy", "Legacy.zip", true, new CollectionInstalledMemberProvenance[0]) });
				CollectionsStore store;
				CollectionScriptedReplayCaptureService service = CreateService(root, out store);

				CollectionScriptedReplaySnapshot snapshot = service.Capture(target,
					LocalCaptureIdentity.From(Guid.Parse("20000000-0000-0000-0000-000000000002")),
					nativeState, identities, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.Coverage);
				CollectionScriptedReplayArtifactSet artifactSet = snapshot.ArtifactSets.Single();
				Assert.IsFalse(artifactSet.CompleteReplayFormat);
				Assert.IsTrue(artifactSet.ReplayPlanValidated);
				Assert.AreEqual(1, artifactSet.ReplayOperationCount);
				Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionScriptedReplayIssueKind.ReplayFormatIncomplete));
				Assert.IsTrue(new CollectionsRetainedArtifactStore(store).VerifyArtifact(artifactSet.ReplayXml.StableArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_LegacyReplayPathCollisionDoesNotAliasDistinctNativeMembers()
		{
			string root = CreateTemporaryDirectory("nmm-c74-collision-");
			try
			{
				string replayPath = Path.Combine(root, "InstallInfo", "Scripted", "Same.xml");
				var cache = new ScriptedFileSelectionCache(replayPath);
				cache.RecordSelection("one.bin", "Data\\one.bin");
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c74-collision");
				InstallLogReadSnapshot install = CreateInstall(new[]
				{
					CreateInstallMod("native-a", "Same.zip", true),
					CreateInstallMod("native-b", "Same.7z", true)
				});
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install, new[]
				{
					CreateReplayReference("native-a", replayPath),
					CreateReplayReference("native-b", replayPath)
				});
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, install, new[]
				{
					CreateInstalledMod("native-a", "Same.zip", true, new CollectionInstalledMemberProvenance[0]),
					CreateInstalledMod("native-b", "Same.7z", true, new CollectionInstalledMemberProvenance[0])
				});
				CollectionsStore store;
				CollectionScriptedReplayCaptureService service = CreateService(root, out store);
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(
					Guid.Parse("20000000-0000-0000-0000-000000000003"));

				CollectionScriptedReplaySnapshot snapshot = service.Capture(target, captureIdentity,
					nativeState, identities, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.Coverage);
				Assert.AreEqual(0, snapshot.ArtifactSets.Count);
				Assert.AreEqual(2, snapshot.Issues.Count(x => x.Kind == CollectionScriptedReplayIssueKind.ReplayPathCollision));
				Assert.AreEqual(0, new CollectionsRetainedArtifactReferenceStore(store)
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString()).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_MissingGeneratedPayloadRetainsReplayXmlButMarksPlanPartial()
		{
			string root = CreateTemporaryDirectory("nmm-c74-missing-payload-");
			try
			{
				string replayPath = Path.Combine(root, "InstallInfo", "Scripted", "Generated.xml");
				var cache = new ScriptedFileSelectionCache(replayPath);
				cache.RecordGeneratedFile("Data\\generated.bin", Encoding.UTF8.GetBytes("gone"));
				Directory.Delete(ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath), true);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c74-missing-payload");
				InstallLogReadSnapshot install = CreateInstall("native-generated", "Generated.zip", true);
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install,
					new[] { CreateReplayReference("native-generated", replayPath) });
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, install,
					new[] { CreateInstalledMod("native-generated", "Generated.zip", true, new CollectionInstalledMemberProvenance[0]) });
				CollectionsStore store;
				CollectionScriptedReplayCaptureService service = CreateService(root, out store);
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(
					Guid.Parse("20000000-0000-0000-0000-000000000004"));

				CollectionScriptedReplaySnapshot snapshot = service.Capture(target, captureIdentity,
					nativeState, identities, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.Coverage);
				CollectionScriptedReplayArtifactSet artifactSet = snapshot.ArtifactSets.Single();
				Assert.IsTrue(artifactSet.CompleteReplayFormat);
				Assert.IsFalse(artifactSet.ReplayPlanValidated);
				Assert.AreEqual(0, artifactSet.GeneratedPayloads.Count);
				Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionScriptedReplayIssueKind.GeneratedPayloadMissing));
				Assert.AreEqual(1, new CollectionsRetainedArtifactReferenceStore(store)
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString()).Count);
				Assert.IsTrue(new CollectionsRetainedArtifactStore(store).VerifyArtifact(artifactSet.ReplayXml.StableArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_SameReplayRoleRejectsChangedXmlInsteadOfSilentlyRebinding()
		{
			string root = CreateTemporaryDirectory("nmm-c74-rebind-");
			try
			{
				string replayPath = Path.Combine(root, "InstallInfo", "Scripted", "Stable.xml");
				new ScriptedFileSelectionCache(replayPath).RecordSelection("one.bin", "Data\\one.bin");
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c74-rebind");
				InstallLogReadSnapshot install = CreateInstall("native-stable", "Stable.zip", true);
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install,
					new[] { CreateReplayReference("native-stable", replayPath) });
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, install,
					new[] { CreateInstalledMod("native-stable", "Stable.zip", true, new CollectionInstalledMemberProvenance[0]) });
				CollectionsStore store;
				CollectionScriptedReplayCaptureService service = CreateService(root, out store);
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(
					Guid.Parse("20000000-0000-0000-0000-000000000005"));
				service.Capture(target, captureIdentity, nativeState, identities, CancellationToken.None);

				File.Delete(replayPath);
				new ScriptedFileSelectionCache(replayPath).RecordSelection("two.bin", "Data\\two.bin");

				InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
					service.Capture(target, captureIdentity, nativeState, identities, CancellationToken.None));
				StringAssert.Contains("already bound to different immutable bytes", error.Message);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static InstallLogReadSnapshot CreateInstall(string nativeKey, string fileName, bool hasInstallScript)
		{
			return CreateInstall(new[] { CreateInstallMod(nativeKey, fileName, hasInstallScript) });
		}

		private static InstallLogReadSnapshot CreateInstall(IEnumerable<InstallLogReadMod> mods)
		{
			return new InstallLogReadSnapshot("original-values", 31, mods, new InstallLogReadFile[0],
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0], new InstallLogReadDeploymentTarget[0]);
		}

		private static InstallLogReadMod CreateInstallMod(string nativeKey, string fileName, bool hasInstallScript)
		{
			return new InstallLogReadMod(nativeKey, fileName, fileName, "1", "2", "1", "1", hasInstallScript,
				ModInstallRoot.Data, ModInstallMethod.Virtual, false);
		}

		private static NativeStateCaptureReplayReference CreateReplayReference(string nativeKey, string replayPath)
		{
			return new NativeStateCaptureReplayReference(nativeKey, replayPath,
				ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath));
		}

		private static NativeStateCaptureSnapshot CreateNativeState(InstallLogReadSnapshot install,
			IEnumerable<NativeStateCaptureReplayReference> replayReferences)
		{
			return new NativeStateCaptureSnapshot(install, new VirtualModReadSnapshot(new VirtualModReadLink[0]),
				new NativeStateCaptureVirtualPayloadSource[0], new NativeStateCaptureRoot[0],
				new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable,
				replayReferences, new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.NotApplicable,
				new NativeStateCaptureIssue[0]);
		}

		private static CollectionInstalledIdentitySnapshot CreateIdentities(CollectionTargetIdentity target,
			InstallLogReadSnapshot install, IEnumerable<CollectionInstalledModIdentity> mods)
		{
			return new CollectionInstalledIdentitySnapshot(target, install.DeploymentCommitSequence, mods,
				NativeStateCaptureCoverage.Complete, new CollectionInstalledIdentityIssue[0]);
		}

		private static CollectionInstalledModIdentity CreateInstalledMod(string nativeKey, string fileName,
			bool hasInstallScript, IEnumerable<CollectionInstalledMemberProvenance> provenance)
		{
			return new CollectionInstalledModIdentity(nativeKey,
				new CollectionInstalledArchiveReference(fileName, fileName, false, null), "1", "2", "1", "1",
				hasInstallScript, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), provenance);
		}

		private static CollectionInstalledMemberProvenance CreateProvenance(CollectionTargetIdentity target)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c74");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c74", 4);
			return new CollectionInstalledMemberProvenance(Guid.Parse("21000000-0000-0000-0000-000000000001"), revision,
				CollectionAssociationState.Applied, CollectionMemberKey.FromProvider("member-c74"),
				CollectionRecipeIdentity.FromFingerprint("recipe-c74"), CollectionMemberBindingKind.InstalledForCollection);
		}

		private static CollectionScriptedReplayCaptureService CreateService(string root, out CollectionsStore store)
		{
			store = new CollectionsStore(Path.Combine(root, "Collections"));
			store.CreateNew();
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) => null);
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) => null);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) => null);
			var nativeReader = new NativeStateCaptureReader(installLog, virtualModActivator, null, null, gameMode);
			var identityReader = new CollectionInstalledIdentityCaptureReader(nativeReader,
				(Func<CollectionTargetIdentity, CollectionsAssociationTargetSnapshot>)null, null);
			return new CollectionScriptedReplayCaptureService(nativeReader, identityReader,
				new CollectionsRetainedArtifactStore(store), new CollectionsRetainedArtifactReferenceStore(store));
		}

		private static string CreateTemporaryDirectory(string prefix)
		{
			string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static void AssertRetainedText(CollectionsStore store, RetainedArtifactReference retained, string expected)
		{
			var artifacts = new CollectionsRetainedArtifactStore(store);
			Assert.IsTrue(artifacts.VerifyArtifact(retained.StableArtifactId));
			using (var reader = new StreamReader(artifacts.OpenRead(retained.StableArtifactId), Encoding.UTF8))
				Assert.AreEqual(expected, reader.ReadToEnd());
		}
	}
}
