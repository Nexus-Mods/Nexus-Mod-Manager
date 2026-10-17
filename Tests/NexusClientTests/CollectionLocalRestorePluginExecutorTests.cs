using System;
using System.IO;
using System.Runtime.Serialization;
using Nexus.Client;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	[Category("CollectionsGateL")]
	public class CollectionLocalRestorePluginExecutorTests
	{
		/// <summary>Reproduces eager startup workflow construction for a game such as Cyberpunk 2077.</summary>
		[Test]
		public void PluginlessGame_CanConstructCompleteRestoreWorkflowWithoutCreatingAStore()
		{
			ServiceManager services = CreateServices(false, false);
			CollectionsStore store = CreateStore();
			var storage = new GameStorageService(Path.GetTempPath(), new Version(1, 0));

			Assert.DoesNotThrow(() => new CollectionLocalRestoreApplicationService(services, storage,
				new CollectionsOperationStore(store), new CollectionsAssociationStore(store),
				new CollectionsLocalCaptureStore(store), new CollectionsRetainedArtifactStore(store),
				new CollectionsRetainedArtifactReferenceStore(store)));
			Assert.IsFalse(Directory.Exists(store.StoreDirectory));
		}

		/// <summary>Preserves the native plugin-manager prerequisite for plugin games and unknown game modes.</summary>
		[TestCase(true)]
		[TestCase(null)]
		public void MissingPluginManager_IsRejectedUnlessGameExplicitlyDisablesPlugins(bool? usesPlugins)
		{
			Assert.Throws<InvalidOperationException>(() => CreateExecutor(CreateServices(usesPlugins, false)));
		}

		/// <summary>Preserves initialization for games with a live native plugin manager.</summary>
		[Test]
		public void PluginGame_WithNativePluginManager_CanConstructExecutor()
		{
			Assert.DoesNotThrow(() => CreateExecutor(CreateServices(true, true)));
		}

		/// <summary>A pluginless game still requires its live native mod manager.</summary>
		[Test]
		public void MissingModManager_IsRejected()
		{
			Assert.Throws<InvalidOperationException>(() => CreateExecutor(
				new ServiceManager(null, null, null, null, null, null, null, null)));
		}

		/// <summary>Excluded and non-applicable plugin scopes verify without touching a native plugin manager.</summary>
		[TestCase(true, NativeStateCaptureCoverage.NotApplicable)]
		[TestCase(false, NativeStateCaptureCoverage.Complete)]
		public void PluginlessGame_SkipsPluginVerificationWhenNotApplicable(bool includePluginScope, NativeStateCaptureCoverage coverage)
		{
			CollectionLocalRestorePluginExecutor executor = CreateExecutor(CreateServices(false, false));
			CollectionSealedCaptureSnapshot capture = CreateCapture(includePluginScope, coverage, false);
			Assert.DoesNotThrow(() => executor.VerifyFinalState(capture));
		}

		/// <summary>Applicable captured plugin state fails explicitly even when its desired list is empty.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void PluginlessGame_RejectsApplicableCapturedPluginState(bool includePlugin)
		{
			CollectionLocalRestorePluginExecutor executor = CreateExecutor(CreateServices(false, false));
			CollectionSealedCaptureSnapshot capture = CreateCapture(true, NativeStateCaptureCoverage.Complete, includePlugin);
			InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => executor.VerifyFinalState(capture));
			StringAssert.Contains("requires the live native plugin manager", error.Message);
		}

		/// <summary>Non-applicable capture coverage cannot conceal plugin records.</summary>
		[Test]
		public void PluginlessGame_RejectsPluginRecordsInNonApplicableCapture()
		{
			CollectionLocalRestorePluginExecutor executor = CreateExecutor(CreateServices(false, false));
			CollectionSealedCaptureSnapshot capture = CreateCapture(true, NativeStateCaptureCoverage.NotApplicable, true);
			Assert.Throws<InvalidDataException>(() => executor.VerifyFinalState(capture));
		}

		/// <summary>Creates isolated native service handles without initializing or changing any game files.</summary>
		private static ServiceManager CreateServices(bool? usesPlugins, bool withPluginManager)
		{
			ModManager manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
			IGameMode gameMode = usesPlugins.HasValue
				? InterfaceStub<IGameMode>.Create((method, args) => method.Name == "get_UsesPlugins" ? (object)usesPlugins.Value : null)
				: null;
			typeof(ModManager).GetProperty("GameMode").SetValue(manager, gameMode, null);
			typeof(ModManager).GetField("m_mdmDeploymentManager", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(manager, InterfaceStub<IModDeploymentManager>.Create((method, args) => null));
			IPluginManager plugins = withPluginManager ? InterfaceStub<IPluginManager>.Create((method, args) => null) : null;
			return new ServiceManager(null, null, null, null, manager, plugins, null, null);
		}

		/// <summary>Creates store paths only; none of these tests creates or opens a database.</summary>
		private static CollectionsStore CreateStore()
		{
			return new CollectionsStore(Path.Combine(Path.GetTempPath(), "nmm-pluginless-startup-" + Guid.NewGuid().ToString("N")));
		}

		/// <summary>Builds the production executor with isolated store and registry paths.</summary>
		private static CollectionLocalRestorePluginExecutor CreateExecutor(ServiceManager services)
		{
			CollectionsStore store = CreateStore();
			return new CollectionLocalRestorePluginExecutor(services,
				new GameStorageService(Path.GetTempPath(), new Version(1, 0)), new CollectionsOperationStore(store),
				new CollectionsRetainedArtifactStore(store), new CollectionsRetainedArtifactReferenceStore(store));
		}

		/// <summary>Creates a sealed empty setup with explicit plugin scope and native coverage.</summary>
		private static CollectionSealedCaptureSnapshot CreateCapture(bool includePluginScope, NativeStateCaptureCoverage coverage, bool includePlugin)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("pluginless-game-test");
			CollectionIdentity collection = CollectionIdentity.FromLocal(Guid.NewGuid());
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(collection, Guid.NewGuid());
			LocalCaptureIdentity identity = LocalCaptureIdentity.From(Guid.NewGuid());
			var scope = new LocalCaptureScope(LocalCaptureScope.CurrentVersion,
				new[] { includePluginScope ? LocalCaptureScopeArea.PluginState : LocalCaptureScopeArea.ManagedModState });
			var capture = new LocalCapture(identity, revision, target, new CollectionCurrentStateFingerprint("state-v1", "test"),
				scope, LocalCaptureCapability.LocallyRestorableWithinScope, new RetainedArtifactReference[0],
				new LocalCaptureExclusion[0], new LocalCaptureNativeRecordMapping[0]);
			var installed = new CollectionInstalledIdentitySnapshot(target, 1, new CollectionInstalledModIdentity[0],
				NativeStateCaptureCoverage.Complete, new CollectionInstalledIdentityIssue[0]);
			var owners = new CollectionOwnerPayloadSnapshot(target, identity, 1, new CollectionOwnerPayloadTarget[0],
				NativeStateCaptureCoverage.Complete, new CollectionOwnerPayloadIssue[0]);
			var replay = new CollectionScriptedReplaySnapshot(target, identity, 1, new CollectionScriptedReplayArtifactSet[0],
				NativeStateCaptureCoverage.Complete, new CollectionScriptedReplayIssue[0]);
			NativeStateCapturePlugin[] plugins = includePlugin
				? new[] { new NativeStateCapturePlugin("A.esm", true, 0, 0, "00", default(PluginParseStatus),
					default(PluginAddressClass), default(PluginHeaderFlags), default(PluginSpecialFlags), true, 0,
					new string[0], new NativeStateCapturePluginDiagnostic[0]) }
				: new NativeStateCapturePlugin[0];
			var effects = new CollectionNativeEffectSnapshot(target, 1, new CollectionCapturedIniEffect[0],
				NativeStateCaptureCoverage.Complete, new CollectionCapturedGameValueEffect[0], NativeStateCaptureCoverage.Complete,
				plugins, coverage, new CollectionNativeEffectIssue[0]);
			var metadata = new CollectionUserMetadataSnapshot(target, identity, 1, new CollectionCapturedSortAssignment[0],
				NativeStateCaptureCoverage.Complete, new CollectionCapturedScreenshotOverride[0], NativeStateCaptureCoverage.Complete,
				new CollectionUserMetadataIssue[0]);
			return new CollectionSealedCaptureSnapshot(capture, installed, new CollectionCapturedArchiveArtifact[0], owners, replay, effects, metadata);
		}

		[Test]
		public void ExactPluginState_DetectsActivationAndOrderChanges()
		{
			var captured = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\A.esm", true, 0, 0, "00"),
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\B.esp", false, 1, null, "")
			});
			var same = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry(@"c:\\game\\data\\A.esm", true, 0, 0, "00"),
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\B.esp", false, 1, null, "")
			});
			var activationChanged = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\A.esm", true, 0, 0, "00"),
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\B.esp", true, 1, 1, "01")
			});
			var orderChanged = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\B.esp", false, 0, null, ""),
				new CollectionLocalRestorePluginEntry(@"C:\\Game\\Data\\A.esm", true, 1, 0, "00")
			});

			Assert.IsTrue(captured.Equals(same));
			Assert.IsFalse(captured.Equals(activationChanged));
			Assert.IsFalse(captured.Equals(orderChanged));
		}

		[Test]
		public void ExactPluginState_DetectsAllocatedIndexOrModIndexChanges()
		{
			var captured = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry("A.esm", true, 0, 0, "00")
			});
			var allocatedChanged = new CollectionLocalRestorePluginState(new[]
			{
				new CollectionLocalRestorePluginEntry("A.esm", true, 0, 1, "01")
			});

			Assert.IsFalse(captured.Equals(allocatedChanged));
		}
	}
}
