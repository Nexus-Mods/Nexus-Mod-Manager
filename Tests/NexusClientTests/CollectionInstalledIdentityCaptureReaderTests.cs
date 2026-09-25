using System;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C7.2 native installed identity/context capture characterization.
	/// </summary>
	public class CollectionInstalledIdentityCaptureReaderTests
	{
		[Test]
		public void Capture_RecordsActiveNativeIdentityStableSourceAndExactInstallContext()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-c72-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			string directArchive = Path.Combine(root, "direct.zip");
			File.WriteAllText(directArchive, "archive");
			InstallLogReadSnapshot install = new InstallLogReadSnapshot("original-values", 17,
				new[]
				{
					new InstallLogReadMod("virtual-b", Path.Combine(root, "missing.zip"), "missing.zip", "0", "-1", "2", "2", false,
						ModInstallRoot.GameRoot, ModInstallMethod.Virtual, false),
					new InstallLogReadMod("direct-a", directArchive, "direct.zip", "42", "77", "1.2", "1.2.0", true,
						ModInstallRoot.Data, ModInstallMethod.Direct, false),
					new InstallLogReadMod("hidden-old", Path.Combine(root, "old.zip"), "old.zip", "9", "9", "0", "0", false,
						ModInstallRoot.Data, ModInstallMethod.Virtual, true)
				},
				new InstallLogReadFile[0], new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
				new InstallLogReadDeploymentTarget[0]);
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c72");
			var associations = new CollectionsAssociationTargetSnapshot(target, new CollectionTargetAssociation[0],
				new CollectionMemberBinding[0], new UserOverride[0]);
			CollectionInstalledIdentityCaptureReader reader = CreateReader(root, install, targetValue => associations,
				"SkyrimSpecialEdition");

			CollectionInstalledIdentitySnapshot snapshot = reader.Capture(target);

			Assert.AreEqual(target, snapshot.Target);
			Assert.AreEqual(17, snapshot.DeploymentCommitSequence);
			Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.ProvenanceCoverage);
			CollectionAssert.AreEqual(new[] { "direct-a", "virtual-b" }, snapshot.Mods.Select(x => x.NativeSnapshotKey).ToArray());
			CollectionInstalledModIdentity direct = snapshot.Mods[0];
			Assert.AreEqual(ModInstallMethod.Direct, direct.InstallContext.Method);
			Assert.AreEqual(ModInstallRoot.Data, direct.InstallContext.InstallRoot);
			Assert.IsTrue(direct.HasInstallScript);
			Assert.IsTrue(direct.Archive.ArchiveCurrentlyAvailable);
			Assert.IsNotNull(direct.Archive.SourceIdentity);
			Assert.AreEqual("nexus-mod-file", direct.Archive.SourceIdentity.Scheme);
			Assert.AreEqual("skyrimspecialedition/42/77", direct.Archive.SourceIdentity.StableId);
			Assert.IsFalse(snapshot.Mods[1].Archive.ArchiveCurrentlyAvailable);
			Assert.IsNull(snapshot.Mods[1].Archive.SourceIdentity);
		}

		[Test]
		public void Capture_PreservesManyToOneCollectionProvenanceAndReportsStaleBindings()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-c72-prov-" + Guid.NewGuid().ToString("N"));
			InstallLogReadSnapshot install = CreateSingleActiveInstall(root, "shared-native");
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c72-provenance");
			CollectionTargetAssociation first = CreateAssociation(target, Guid.Parse("00000000-0000-0000-0000-000000000001"),
				"collection-a", "revision-a", 1, CollectionAssociationState.Applied);
			CollectionTargetAssociation second = CreateAssociation(target, Guid.Parse("00000000-0000-0000-0000-000000000002"),
				"collection-b", "revision-b", 2, CollectionAssociationState.Modified);
			var bindings = new[]
			{
				new CollectionMemberBinding(first, CollectionMemberKey.FromProvider("member-a"),
					new NativeModInstanceIdentity(target, "shared-native"), CollectionRecipeIdentity.FromFingerprint("recipe-a"),
					CollectionMemberBindingKind.AdoptedExisting),
				new CollectionMemberBinding(second, CollectionMemberKey.FromProvider("member-b"),
					new NativeModInstanceIdentity(target, "shared-native"), CollectionRecipeIdentity.FromFingerprint("recipe-b"),
					CollectionMemberBindingKind.InstalledForCollection),
				new CollectionMemberBinding(first, CollectionMemberKey.FromProvider("stale-member"),
					new NativeModInstanceIdentity(target, "stale-native"), CollectionRecipeIdentity.FromFingerprint("recipe-stale"),
					CollectionMemberBindingKind.AdoptedExisting)
			};
			var associationSnapshot = new CollectionsAssociationTargetSnapshot(target, new[] { first, second }, bindings, new UserOverride[0]);
			CollectionInstalledIdentityCaptureReader reader = CreateReader(root, install, targetValue => associationSnapshot, "game");

			CollectionInstalledIdentitySnapshot snapshot = reader.Capture(target);

			CollectionInstalledModIdentity captured = snapshot.Mods.Single();
			Assert.AreEqual("shared-native", captured.NativeSnapshotKey);
			Assert.AreEqual(2, captured.Provenance.Count);
			CollectionAssert.AreEqual(new[] { "member-a", "member-b" }, captured.Provenance.Select(x => x.MemberKey.Value).ToArray());
			Assert.AreEqual(CollectionMemberBindingKind.AdoptedExisting, captured.Provenance[0].BindingKind);
			Assert.AreEqual(CollectionAssociationState.Modified, captured.Provenance[1].AssociationState);
			Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionInstalledIdentityIssueKind.StaleAssociationBinding && x.ResourceKey == "stale-native"));
		}

		[Test]
		public void Capture_WithoutAssociationStoreStillCapturesNativeRecipeIdentityContext()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-c72-noassoc-" + Guid.NewGuid().ToString("N"));
			InstallLogReadSnapshot install = CreateSingleActiveInstall(root, "native-a");
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c72-noassoc");
			CollectionInstalledIdentityCaptureReader reader = CreateReader(root, install, null, null);

			CollectionInstalledIdentitySnapshot snapshot = reader.Capture(target);

			Assert.AreEqual(1, snapshot.Mods.Count);
			Assert.AreEqual(NativeStateCaptureCoverage.Unavailable, snapshot.ProvenanceCoverage);
			Assert.AreEqual(0, snapshot.Mods[0].Provenance.Count);
			Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionInstalledIdentityIssueKind.AssociationStateUnavailable));
		}

		[Test]
		public void Capture_RejectsDuplicateActiveNativeSnapshotKeys()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-c72-duplicate-" + Guid.NewGuid().ToString("N"));
			InstallLogReadSnapshot install = new InstallLogReadSnapshot("original-values", 0,
				new[]
				{
					new InstallLogReadMod("same-key", "a.zip", "a.zip", "1", "1", "1", "1", false, ModInstallRoot.Data, ModInstallMethod.Virtual, false),
					new InstallLogReadMod("same-key", "b.zip", "b.zip", "2", "2", "1", "1", false, ModInstallRoot.Data, ModInstallMethod.Direct, false)
				},
				new InstallLogReadFile[0], new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
				new InstallLogReadDeploymentTarget[0]);
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c72-duplicate");
			CollectionInstalledIdentityCaptureReader reader = CreateReader(root, install, targetValue =>
				new CollectionsAssociationTargetSnapshot(target, new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0]), "game");

			InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => reader.Capture(target));
			StringAssert.Contains("duplicate active mod keys", error.Message);
		}

		private static InstallLogReadSnapshot CreateSingleActiveInstall(string root, string nativeKey)
		{
			return new InstallLogReadSnapshot("original-values", 3,
				new[] { new InstallLogReadMod(nativeKey, Path.Combine(root, "mod.zip"), "mod.zip", "11", "22", "1", "1", false,
					ModInstallRoot.Data, ModInstallMethod.Virtual, false) },
				new InstallLogReadFile[0], new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
				new InstallLogReadDeploymentTarget[0]);
		}

		private static CollectionTargetAssociation CreateAssociation(CollectionTargetIdentity target, Guid associationId,
			string collectionId, string revisionId, long revisionNumber, CollectionAssociationState state)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus(collectionId);
			return new CollectionTargetAssociation(associationId,
				CollectionRevisionIdentity.FromNexus(collection, revisionId, revisionNumber), target, state);
		}

		private static CollectionInstalledIdentityCaptureReader CreateReader(string root, InstallLogReadSnapshot install,
			Func<CollectionTargetIdentity, CollectionsAssociationTargetSnapshot> associations, string gameDomain)
		{
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
				method.Name == "GetCommittedStateSnapshot" ? install : null);
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
				method.Name == "GetReadSnapshot" ? new VirtualModReadSnapshot(new VirtualModReadLink[0]) : null);
			IGameModeEnvironmentInfo environment = InterfaceStub<IGameModeEnvironmentInfo>.Create((method, args) =>
				method.Name == "get_InstallInfoDirectory" ? Path.Combine(root, "InstallInfo") : null);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_InstallationPath": return root;
					case "get_PluginDirectory": return root;
					case "get_SecondaryInstallationPath": return null;
					case "get_HasSecondaryInstallPath": return false;
					case "get_UsesPlugins": return false;
					case "get_GameModeEnvironmentInfo": return environment;
					default: return null;
				}
			});
			var nativeReader = new NativeStateCaptureReader(installLog, virtualModActivator, null, null, gameMode);
			return new CollectionInstalledIdentityCaptureReader(nativeReader, associations, gameDomain);
		}
	}
}
