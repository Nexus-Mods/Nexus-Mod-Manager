using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.15.11 reviewed file-winner reconciliation and C6.16-E failure/restart coverage.</summary>
	[TestFixture]
	public class CollectionReviewedFileWinnerReconciliationCoordinatorTests
	{
		private const string Sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void PromotedWinner_PersistsIntentBeforeNativeSwitchAndMarksVerifiedAfterReRead()
		{
			using (Fixture fixture = CreateFixture(true))
			{
				int deploymentSwitches = 0;
				bool sawDurableIntentBeforeSwitch = false;
				fixture.DeploymentManager = InterfaceStub<IModDeploymentManager>.Create((method, args) =>
				{
					if (method.Name == "SwitchPromotedOwner")
					{
						deploymentSwitches++;
						sawDurableIntentBeforeSwitch = fixture.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Operation,
							fixture.Operation.Identity.ToString()).Any(x => x.Role.StartsWith("file-winner-intent-", StringComparison.Ordinal));
						fixture.SetOwner("owner-b");
					}
					return null;
				});
				fixture.VirtualService = InterfaceStub<IVirtualDeploymentService>.Create((method, args) =>
				{
					throw new AssertionException("Promoted reconciliation must not use the ordinary Virtual owner switch.");
				});
				CollectionReviewedFileWinnerReconciliationResult result = fixture.CreateCoordinator().ReconcileValidated(
					fixture.Operation.Identity, fixture.Plan, fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None);

				Assert.AreEqual(1, deploymentSwitches);
				Assert.IsTrue(sawDurableIntentBeforeSwitch);
				Assert.AreEqual(1, fixture.ProfileUpdates);
				Assert.AreEqual(CollectionReviewedFileWinnerOutcome.SwitchedAndVerified, result.Winners.Single().Outcome);
				Assert.AreEqual("owner-b", result.Winners.Single().DesiredOwnerKey);
				IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> refs = fixture.References.GetReferencesForOwner(
					CollectionsRetainedArtifactOwnerKind.Operation, fixture.Operation.Identity.ToString());
				Assert.AreEqual(2, refs.Count(x => x.Role.StartsWith("file-winner-", StringComparison.Ordinal)));
				Assert.AreEqual(1, refs.Select(x => x.ArtifactId).Distinct(StringComparer.Ordinal).Count());
			}
		}

		[Test]
		public void OrdinaryVirtualWinner_UsesVirtualServiceAndUpdatesProfileMetadata()
		{
			using (Fixture fixture = CreateFixture(false))
			{
				int virtualSwitches = 0;
				fixture.DeploymentManager = InterfaceStub<IModDeploymentManager>.Create((method, args) =>
				{
					if (method.Name == "SwitchPromotedOwner") throw new AssertionException("Ordinary Virtual reconciliation must not use promoted switching.");
					return null;
				});
				fixture.VirtualService = InterfaceStub<IVirtualDeploymentService>.Create((method, args) =>
				{
					if (method.Name == "SwitchFileOwner")
					{
						virtualSwitches++;
						Assert.AreEqual(fixture.FileTarget.RelativePath, args[0]);
						Assert.AreEqual("owner-b", args[1]);
						fixture.SetOwner("owner-b");
						return VirtualFileOwnerSwitchResult.Succeeded((string)args[0], (string)args[1]);
					}
					return null;
				});

				CollectionReviewedFileWinnerReconciliationResult result = fixture.CreateCoordinator().ReconcileValidated(
					fixture.Operation.Identity, fixture.Plan, fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None);

				Assert.AreEqual(1, virtualSwitches);
				Assert.AreEqual(1, fixture.ProfileUpdates);
				Assert.AreEqual(CollectionReviewedFileWinnerDispatchKind.Virtual, result.Winners.Single().DispatchKind);
			}
		}

		[Test]
		public void FailureBeforeWinnerSwitch_RestartRetriesPersistedIntentFromExactPreimageOnce()
		{
			using (Fixture fixture = CreateFixture(true))
			{
				int switchCalls = 0;
				int nativeMutations = 0;
				bool failBeforeMutation = true;
				fixture.DeploymentManager = InterfaceStub<IModDeploymentManager>.Create((method, args) =>
				{
					if (method.Name == "SwitchPromotedOwner")
					{
						switchCalls++;
						if (failBeforeMutation) throw new IOException("simulated failure before winner mutation");
						nativeMutations++;
						fixture.SetOwner("owner-b");
					}
					return null;
				});
				fixture.VirtualService = InterfaceStub<IVirtualDeploymentService>.Create((method, args) => null);

				Assert.Throws<IOException>(() => fixture.CreateCoordinator().ReconcileValidated(fixture.Operation.Identity, fixture.Plan,
					fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None));
				Assert.AreEqual(1, switchCalls);
				Assert.AreEqual(0, nativeMutations);
				Assert.AreEqual("owner-a", fixture.CurrentState.Files[fixture.FileTarget].EffectiveOwnerKey);
				IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> beforeRestart = fixture.References.GetReferencesForOwner(
					CollectionsRetainedArtifactOwnerKind.Operation, fixture.Operation.Identity.ToString());
				Assert.AreEqual(1, beforeRestart.Count(x => x.Role.StartsWith("file-winner-intent-", StringComparison.Ordinal)));
				Assert.AreEqual(0, beforeRestart.Count(x => x.Role.StartsWith("file-winner-verified-", StringComparison.Ordinal)));

				failBeforeMutation = false;
				CollectionReviewedFileWinnerReconciliationResult recovered = fixture.CreateRestartedCoordinator().ReconcileValidated(
					fixture.Operation.Identity, fixture.Plan, fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None);

				Assert.AreEqual(2, switchCalls, "Restart may retry the persisted intent only because native reality still equals the exact durable preimage.");
				Assert.AreEqual(1, nativeMutations, "Only one native winner mutation may occur across failure and restart.");
				Assert.AreEqual("owner-b", fixture.CurrentState.Files[fixture.FileTarget].EffectiveOwnerKey);
				Assert.AreEqual(CollectionReviewedFileWinnerOutcome.SwitchedAndVerified, recovered.Winners.Single().Outcome);
			}
		}

		[Test]
		public void Restart_AfterNativeSwitchBeforeCheckpoint_ReconcilesRealityWithoutSecondSwitch()
		{
			using (Fixture fixture = CreateFixture(true))
			{
				int switches = 0;
				bool failProfileUpdate = true;
				fixture.ProfileUpdate = () =>
				{
					fixture.ProfileUpdates++;
					if (failProfileUpdate) throw new IOException("simulated profile persistence interruption");
				};
				fixture.DeploymentManager = InterfaceStub<IModDeploymentManager>.Create((method, args) =>
				{
					if (method.Name == "SwitchPromotedOwner") { switches++; fixture.SetOwner("owner-b"); }
					return null;
				});
				fixture.VirtualService = InterfaceStub<IVirtualDeploymentService>.Create((method, args) => null);
				CollectionReviewedFileWinnerReconciliationCoordinator coordinator = fixture.CreateCoordinator();

				Assert.Throws<IOException>(() => coordinator.ReconcileValidated(fixture.Operation.Identity, fixture.Plan,
					fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None));
				Assert.AreEqual(1, switches);
				Assert.AreEqual("owner-b", fixture.CurrentState.Files[fixture.FileTarget].EffectiveOwnerKey);
				Assert.AreEqual(1, fixture.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Operation,
					fixture.Operation.Identity.ToString()).Count(x => x.Role.StartsWith("file-winner-intent-", StringComparison.Ordinal)));

				Assert.Throws<IOException>(() => fixture.CreateRestartedCoordinator().ReconcileValidated(fixture.Operation.Identity,
					fixture.Plan, fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None));
				Assert.AreEqual(1, switches, "A reconciliation failure after the native switch must not replay the winner mutation.");
				Assert.AreEqual(0, fixture.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Operation,
					fixture.Operation.Identity.ToString()).Count(x => x.Role.StartsWith("file-winner-verified-", StringComparison.Ordinal)));

				failProfileUpdate = false;
				CollectionReviewedFileWinnerReconciliationResult recovered = fixture.CreateRestartedCoordinator().ReconcileValidated(
					fixture.Operation.Identity, fixture.Plan, fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None);

				Assert.AreEqual(1, switches, "Restart must reconcile the already-switched native reality, not replay the mutation.");
				Assert.AreEqual(CollectionReviewedFileWinnerOutcome.RecoveredCommitted, recovered.Winners.Single().Outcome);
				Assert.AreEqual(2, fixture.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Operation,
					fixture.Operation.Identity.ToString()).Count(x => x.Role.StartsWith("file-winner-", StringComparison.Ordinal)));
			}
		}

		[Test]
		public void Restart_ThirdOwnerAfterPersistedIntent_RequiresRecoveryInsteadOfGuessing()
		{
			using (Fixture fixture = CreateFixture(true))
			{
				int switches = 0;
				fixture.DeploymentManager = InterfaceStub<IModDeploymentManager>.Create((method, args) =>
				{
					if (method.Name == "SwitchPromotedOwner")
					{
						switches++;
						throw new IOException("simulated switch failure");
					}
					return null;
				});
				fixture.VirtualService = InterfaceStub<IVirtualDeploymentService>.Create((method, args) => null);
				CollectionReviewedFileWinnerReconciliationCoordinator coordinator = fixture.CreateCoordinator();
				Assert.Throws<IOException>(() => coordinator.ReconcileValidated(fixture.Operation.Identity, fixture.Plan,
					fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None));

				fixture.SetOwner("owner-c", true);
				Assert.Throws<CollectionReviewedFileWinnerRecoveryRequiredException>(() => fixture.CreateRestartedCoordinator().ReconcileValidated(
					fixture.Operation.Identity, fixture.Plan, fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None));
				Assert.AreEqual(1, switches, "Divergent native reality must block restart before any second winner switch.");
			}
		}

		[Test]
		public void Restart_ChangedOwnerStackWithSamePreimageWinner_RequiresRecoveryInsteadOfRetrying()
		{
			using (Fixture fixture = CreateFixture(true))
			{
				int switches = 0;
				fixture.DeploymentManager = InterfaceStub<IModDeploymentManager>.Create((method, args) =>
				{
					if (method.Name == "SwitchPromotedOwner")
					{
						switches++;
						throw new IOException("simulated switch failure");
					}
					return null;
				});
				fixture.VirtualService = InterfaceStub<IVirtualDeploymentService>.Create((method, args) => null);
				CollectionReviewedFileWinnerReconciliationCoordinator coordinator = fixture.CreateCoordinator();
				Assert.Throws<IOException>(() => coordinator.ReconcileValidated(fixture.Operation.Identity, fixture.Plan,
					fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None));

				fixture.SetOwner("owner-a", true);
				Assert.Throws<CollectionReviewedFileWinnerRecoveryRequiredException>(() => fixture.CreateRestartedCoordinator().ReconcileValidated(
					fixture.Operation.Identity, fixture.Plan, fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None));
				Assert.AreEqual(1, switches, "A changed durable preimage must block retry before a second native switch.");
			}
		}

		[Test]
		public void AlreadyReviewedWinner_IsNoOpAndDoesNotCreateMutationIntent()
		{
			using (Fixture fixture = CreateFixture(true))
			{
				fixture.SetOwner("owner-b");
				fixture.DeploymentManager = InterfaceStub<IModDeploymentManager>.Create((method, args) =>
				{
					if (method.Name == "SwitchPromotedOwner") throw new AssertionException("Already satisfied winner must not switch.");
					return null;
				});
				fixture.VirtualService = InterfaceStub<IVirtualDeploymentService>.Create((method, args) => null);

				CollectionReviewedFileWinnerReconciliationResult result = fixture.CreateCoordinator().ReconcileValidated(
					fixture.Operation.Identity, fixture.Plan, fixture.Matches, fixture.ImpactPlan, System.Threading.CancellationToken.None);

				Assert.AreEqual(CollectionReviewedFileWinnerOutcome.AlreadySatisfied, result.Winners.Single().Outcome);
				Assert.AreEqual(0, fixture.ProfileUpdates);
				Assert.AreEqual(0, fixture.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Operation,
					fixture.Operation.Identity.ToString()).Count(x => x.Role.StartsWith("file-winner-", StringComparison.Ordinal)));
			}
		}

		private static Fixture CreateFixture(bool promoted)
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-c6-15-11-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			var store = new CollectionsStore(root);
			store.CreateNew();
			var catalog = new CollectionsCatalogStore(store);
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-winner");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-winner", 1);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "Winner", null, null), new CollectionRevision(revision, "Revision", null, 1));
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-winner-" + Guid.NewGuid().ToString("N"));
			NormalizedCollectionMember memberA = CreateMember(0, "member-a", "recipe-a", "artifact-a");
			NormalizedCollectionMember memberB = CreateMember(1, "member-b", "recipe-b", "artifact-b");
			var manifest = new NormalizedCollectionManifest(revision,
				new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(Sha256), 10,
					NexusCollectionManifestNormalizer.SchemaIdentity, NexusCollectionManifestNormalizer.NormalizerVersion),
				CollectionManifestMemberSetCompleteness.Complete, null, new[] { memberA, memberB });
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			ModDeploymentTarget fileTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "meshes\\winner.bin");
			CollectionNativeStateIndex initialState = BuildState(target, fileTarget, promoted, "owner-a", false);
			CollectionPlanIdentity planIdentity = CollectionPlanIdentity.From(Guid.NewGuid(), 1);
			var plan = new ResolvedCollectionPlan(planIdentity, target, CollectionExecutionPolicy.InstallIntoCurrentSetup(), initialState.Fingerprint,
				report, new[]
				{
					new ResolvedCollectionMemberPlan(memberA, CollectionResolvedArtifactChoice.Exact(memberA.Artifact)),
					new ResolvedCollectionMemberPlan(memberB, CollectionResolvedArtifactChoice.Exact(memberB.Artifact))
				});
			CollectionMemberMatchSet matches = BuildMatches(plan, initialState, memberA, memberB);
			var impactPlan = new CollectionConflictImpactPlan(plan, initialState,
				new[] { new CollectionFileImpact(fileTarget, new[] { memberA.IdentityResolution.Key, memberB.IdentityResolution.Key }, memberB.IdentityResolution.Key, "owner-a", new Guid[0]) },
				new CollectionPluginImpact[0], new CollectionConfigurationImpact[0], new CollectionAssociationImpact[0], new CollectionConflictImpactIssue[0]);
			CollectionDependencyPhasePlan dependencyPlan = new CollectionDependencyPhasePlanner().Plan(plan, matches);
			Assert.IsTrue(dependencyPlan.IsReady);
			Assert.IsTrue(impactPlan.IsReady);
			CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(plan, dependencyPlan, impactPlan, new PreparedCollectionNativeRecipe[0]);
			var planStore = new CollectionsResolvedPlanStore(store);
			planStore.SavePlan(plan, CollectionReviewedWorkflowSnapshotCodec.PayloadFormat, CollectionReviewedWorkflowSnapshotCodec.Serialize(snapshot));
			var operationStore = new CollectionsOperationStore(store);
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ApplyResolvedPlan,
				collection, target, revision, plan.Identity, 1, CollectionOperationPhase.ApplyingNativeChildren,
				CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
			operationStore.SaveOperation(operation);
			var associations = new CollectionsAssociationStore(store);
			var artifacts = new CollectionsRetainedArtifactStore(store);
			var references = new CollectionsRetainedArtifactReferenceStore(store);
			return new Fixture(root, target, plan, operation, operationStore, planStore, associations, artifacts, references,
				fileTarget, memberA, memberB, promoted, initialState, matches, impactPlan);
		}

		private static CollectionNativeStateIndex BuildState(CollectionTargetIdentity target, ModDeploymentTarget fileTarget,
			bool promoted, string ownerKey, bool includeThirdOwner)
		{
			var modA = new CollectionNativeModState(new NativeModInstanceIdentity(target, "owner-a"), "a.zip", "a.zip", "1", "1", "1", "1", ModInstallRoot.Data, ModInstallMethod.Virtual);
			var modB = new CollectionNativeModState(new NativeModInstanceIdentity(target, "owner-b"), "b.zip", "b.zip", "2", "2", "1", "1", ModInstallRoot.Data, ModInstallMethod.Virtual);
			var mods = new List<CollectionNativeModState> { modA, modB };
			if (includeThirdOwner) mods.Add(new CollectionNativeModState(new NativeModInstanceIdentity(target, "owner-c"), "c.zip", "c.zip", "3", "3", "1", "1", ModInstallRoot.Data, ModInstallMethod.Virtual));
			var ownerA = new CollectionNativeOwnerState("owner-a", null, CollectionNativeOwnerKind.NativeMod, true, 2, "a");
			var ownerB = new CollectionNativeOwnerState("owner-b", null, CollectionNativeOwnerKind.NativeMod, true, 1, "b");
			var ownerC = new CollectionNativeOwnerState("owner-c", null, CollectionNativeOwnerKind.NativeMod, true, 0, "c");
			var allOwners = new List<CollectionNativeOwnerState> { ownerA, ownerB };
			if (includeThirdOwner) allOwners.Add(ownerC);
			CollectionNativeOwnerState effectiveOwner = allOwners.Single(x =>
				x.OwnerKey.Equals(ownerKey, StringComparison.OrdinalIgnoreCase));
			allOwners.Remove(effectiveOwner);
			allOwners.Add(effectiveOwner);
			CollectionNativeOwnerState[] ownerStack = allOwners.ToArray();
			var file = new CollectionNativeFileState(fileTarget, "C:\\Game\\Data\\meshes\\winner.bin", promoted, promoted, !promoted, ownerKey,
				new CollectionNativeOwnerState[0], promoted ? ownerStack : new CollectionNativeOwnerState[0], promoted ? new CollectionNativeOwnerState[0] : ownerStack);
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], mods, new[] { file },
				new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
				CollectionNativeStateCoverage.NotApplicable, new CollectionTargetAssociation[0], new CollectionMemberBinding[0],
				new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
		}

		private static CollectionMemberMatchSet BuildMatches(ResolvedCollectionPlan plan, CollectionNativeStateIndex state,
			NormalizedCollectionMember memberA, NormalizedCollectionMember memberB)
		{
			CollectionNativeModState modA = state.Mods.Values.Single(x => x.Identity.NativeModKey == "owner-a");
			CollectionNativeModState modB = state.Mods.Values.Single(x => x.Identity.NativeModKey == "owner-b");
			ResolvedCollectionMemberPlan planA = plan.SelectedMembers.Single(x => x.MemberKey.Equals(memberA.IdentityResolution.Key));
			ResolvedCollectionMemberPlan planB = plan.SelectedMembers.Single(x => x.MemberKey.Equals(memberB.IdentityResolution.Key));
			return new CollectionMemberMatchSet(plan, state, new[]
			{
				new CollectionMemberMatchResult(planA, CollectionMemberMatchDisposition.InstalledCompatible, CollectionMemberMatchReason.ExistingVerifiedBinding,
					new[] { modA }, new CollectionMemberBinding[0], null),
				new CollectionMemberMatchResult(planB, CollectionMemberMatchDisposition.InstalledCompatible, CollectionMemberMatchReason.ExistingVerifiedBinding,
					new[] { modB }, new CollectionMemberBinding[0], null)
			});
		}

		private static NormalizedCollectionMember CreateMember(int ordinal, string key, string recipe, string artifact)
		{
			return new NormalizedCollectionMember(ordinal, CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(key)),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", artifact, null), CollectionRecipeIdentity.FromFingerprint(recipe), key, 0);
		}

		private sealed class Fixture : IDisposable
		{
			private readonly bool _promoted;
			private readonly NormalizedCollectionMember _memberA;
			private readonly NormalizedCollectionMember _memberB;

			public Fixture(string root, CollectionTargetIdentity target, ResolvedCollectionPlan plan, CollectionOperation operation,
				CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore, CollectionsAssociationStore associations,
				CollectionsRetainedArtifactStore artifacts, CollectionsRetainedArtifactReferenceStore references,
				ModDeploymentTarget fileTarget, NormalizedCollectionMember memberA, NormalizedCollectionMember memberB, bool promoted,
				CollectionNativeStateIndex initialState, CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan)
			{
				Root = root; Target = target; Plan = plan; Operation = operation; OperationStore = operationStore; PlanStore = planStore;
				Associations = associations; Artifacts = artifacts; References = references; FileTarget = fileTarget;
				_memberA = memberA; _memberB = memberB; _promoted = promoted; CurrentState = initialState; Matches = matches; ImpactPlan = impactPlan;
			}

			public string Root { get; private set; }
			public CollectionTargetIdentity Target { get; private set; }
			public ResolvedCollectionPlan Plan { get; private set; }
			public CollectionOperation Operation { get; private set; }
			public CollectionsOperationStore OperationStore { get; private set; }
			public CollectionsResolvedPlanStore PlanStore { get; private set; }
			public CollectionsAssociationStore Associations { get; private set; }
			public CollectionsRetainedArtifactStore Artifacts { get; private set; }
			public CollectionsRetainedArtifactReferenceStore References { get; private set; }
			public ModDeploymentTarget FileTarget { get; private set; }
			public CollectionNativeStateIndex CurrentState { get; private set; }
			public CollectionMemberMatchSet Matches { get; private set; }
			public CollectionConflictImpactPlan ImpactPlan { get; private set; }
			public IModDeploymentManager DeploymentManager { get; set; }
			public IVirtualDeploymentService VirtualService { get; set; }
			public int ProfileUpdates { get; set; }
			public Action ProfileUpdate { get; set; }

			public void SetOwner(string ownerKey) { SetOwner(ownerKey, false); }
			public void SetOwner(string ownerKey, bool includeThirdOwner)
			{
				CurrentState = BuildState(Target, FileTarget, _promoted, ownerKey, includeThirdOwner);
			}

			public CollectionReviewedFileWinnerReconciliationCoordinator CreateCoordinator()
			{
				return CreateCoordinator(OperationStore, PlanStore, Associations, Artifacts, References);
			}

			public CollectionReviewedFileWinnerReconciliationCoordinator CreateRestartedCoordinator()
			{
				var restartedStore = new CollectionsStore(Root);
				restartedStore.OpenExisting();
				return CreateCoordinator(new CollectionsOperationStore(restartedStore), new CollectionsResolvedPlanStore(restartedStore),
					new CollectionsAssociationStore(restartedStore), new CollectionsRetainedArtifactStore(restartedStore),
					new CollectionsRetainedArtifactReferenceStore(restartedStore));
			}

			private CollectionReviewedFileWinnerReconciliationCoordinator CreateCoordinator(CollectionsOperationStore operationStore,
				CollectionsResolvedPlanStore planStore, CollectionsAssociationStore associations,
				CollectionsRetainedArtifactStore artifacts, CollectionsRetainedArtifactReferenceStore references)
			{
				Action update = ProfileUpdate ?? (() => ProfileUpdates++);
				return new CollectionReviewedFileWinnerReconciliationCoordinator(operationStore, planStore, associations, artifacts, references,
					DeploymentManager ?? InterfaceStub<IModDeploymentManager>.Create((method, args) => null),
					VirtualService ?? InterfaceStub<IVirtualDeploymentService>.Create((method, args) => null),
					target => CurrentState, update);
			}

			public void Dispose()
			{
				try { Directory.Delete(Root, true); } catch { }
			}
		}
	}
}
