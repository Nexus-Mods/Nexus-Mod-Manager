using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.14 safe uninstall planning/persistence coverage without invoking the native uninstaller.</summary>
	[TestFixture]
	public class CollectionUninstallEffectsCoordinatorTests
	{
		[Test]
		public void Plan_RemovesOnlyNativeModWithVerifiedNoStandaloneUseAndNoSurvivingAssociation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "remove-only");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				CollectionUninstallEffectsPlan plan = BuildPlan(fixture.Associations, fixture.Association,
					CreateState(fixture, true, null, null));

				Assert.AreEqual(1, plan.Impacts.Count);
				Assert.AreEqual(CollectionUninstallNativeDisposition.RemoveNativeMod, plan.Impacts[0].Disposition);
				Assert.IsTrue(plan.RequiresNativeMutation);
				Assert.IsFalse(plan.HasBlockedImpacts);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_UnknownStandaloneProvenanceIsProtective()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "unknown-provenance");
				CollectionUninstallEffectsPlan plan = BuildPlan(fixture.Associations, fixture.Association,
					CreateState(fixture, true, null, null));

				Assert.AreEqual(CollectionUninstallNativeDisposition.PreserveUnknownProvenance, plan.Impacts.Single().Disposition);
				Assert.IsFalse(plan.RequiresNativeMutation);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_ExplicitStandaloneUseIsPreserved()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "explicit-standalone");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.ExplicitStandaloneUse));

				CollectionUninstallEffectsPlan plan = BuildPlan(fixture.Associations, fixture.Association,
					CreateState(fixture, true, null, null));

				Assert.AreEqual(CollectionUninstallNativeDisposition.PreserveStandalone, plan.Impacts.Single().Disposition);
				Assert.IsFalse(plan.RequiresNativeMutation);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_SharedNativeModIsPreservedEvenWhenStandaloneUseIsVerifiedAbsent()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "shared");
				CollectionTargetAssociation other = SeedAssociation(fixture.Store, "other-shared", fixture.Target);
				var otherBinding = new CollectionMemberBinding(other, CollectionMemberKey.FromProvider("other-member"),
					fixture.NativeMod, fixture.Binding.VerifiedRecipe, CollectionMemberBindingKind.AdoptedExisting);
				fixture.Associations.SaveBinding(otherBinding);
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));

				CollectionUninstallEffectsPlan plan = BuildPlan(fixture.Associations, fixture.Association,
					CreateState(fixture, true, new[] { other }, new[] { otherBinding }));

				CollectionUninstallNativeImpact impact = plan.Impacts.Single();
				Assert.AreEqual(CollectionUninstallNativeDisposition.PreserveSharedCollection, impact.Disposition);
				CollectionAssert.Contains(impact.SurvivingAssociationIds, other.AssociationId);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_SharedCustomizedMemberBecomesStandaloneProtectedInsteadOfLosingUserIntent()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "shared-customized");
				CollectionTargetAssociation other = SeedAssociation(fixture.Store, "shared-customized-other", fixture.Target);
				var otherBinding = new CollectionMemberBinding(other, CollectionMemberKey.FromProvider("other-member"),
					fixture.NativeMod, fixture.Binding.VerifiedRecipe, CollectionMemberBindingKind.AdoptedExisting);
				fixture.Associations.SaveBinding(otherBinding);
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				var requirement = new CollectionRequirementReference(fixture.Association, fixture.Binding.MemberKey,
					CollectionRequirementAspect.MemberEnabledState, null);
				fixture.Associations.SaveOverride(new UserOverride(Guid.NewGuid(), requirement,
					CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Absent(), "Keep disabled"));
				fixture.Associations.SaveAssociation(fixture.Association.WithState(CollectionAssociationState.Modified));
				CollectionTargetAssociation modified = fixture.Associations.GetAssociation(fixture.Association.AssociationId);

				CollectionUninstallEffectsPlan plan = BuildPlan(fixture.Associations, modified,
					CreateState(fixture, true, new[] { other }, new[] { otherBinding }));

				Assert.AreEqual(CollectionUninstallNativeDisposition.PreserveCustomized, plan.Impacts.Single().Disposition);
				CollectionAssert.Contains(plan.Impacts.Single().SurvivingAssociationIds, other.AssociationId);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_DeliberateMemberOverrideProtectsNativeModFromAutomaticRemoval()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "customized");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				var requirement = new CollectionRequirementReference(fixture.Association, fixture.Binding.MemberKey,
					CollectionRequirementAspect.MemberEnabledState, null);
				fixture.Associations.SaveOverride(new UserOverride(Guid.NewGuid(), requirement,
					CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Absent(), "Keep disabled"));
				fixture.Associations.SaveAssociation(fixture.Association.WithState(CollectionAssociationState.Modified));
				CollectionTargetAssociation modified = fixture.Associations.GetAssociation(fixture.Association.AssociationId);

				CollectionUninstallEffectsPlan plan = BuildPlan(fixture.Associations, modified,
					CreateState(fixture, true, null, null));

				Assert.AreEqual(CollectionUninstallNativeDisposition.PreserveCustomized, plan.Impacts.Single().Disposition);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_IncompleteNativeCoverageBlocksAutomaticRemoval()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "incomplete-coverage");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				var issue = new CollectionNativeStateIssue(CollectionNativeStateIssueKind.UnresolvedOwner,
					"Data\\example.dds", "Owner identity could not be resolved.");

				CollectionUninstallEffectsPlan plan = BuildPlan(fixture.Associations, fixture.Association,
					CreateState(fixture, true, null, null, new[] { issue }));

				Assert.AreEqual(CollectionUninstallNativeDisposition.BlockedNativeState, plan.Impacts.Single().Disposition);
				Assert.IsTrue(plan.HasBlockedImpacts);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_AlreadyAbsentCurrentOnlyMemberNeedsNoNativeMutation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "already-absent");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));

				CollectionUninstallEffectsPlan plan = BuildPlan(fixture.Associations, fixture.Association,
					CreateState(fixture, false, null, null));

				Assert.AreEqual(CollectionUninstallNativeDisposition.AlreadyAbsent, plan.Impacts.Single().Disposition);
				Assert.IsFalse(plan.RequiresNativeMutation);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Plan_MissingSharedNativeModBlocksInsteadOfSilentlyDroppingOtherCollectionRequirement()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "missing-shared");
				CollectionTargetAssociation other = SeedAssociation(fixture.Store, "other-missing", fixture.Target);
				var otherBinding = new CollectionMemberBinding(other, CollectionMemberKey.FromProvider("other-member"),
					fixture.NativeMod, fixture.Binding.VerifiedRecipe, CollectionMemberBindingKind.AdoptedExisting);
				fixture.Associations.SaveBinding(otherBinding);

				CollectionUninstallEffectsPlan plan = BuildPlan(fixture.Associations, fixture.Association,
					CreateState(fixture, false, new[] { other }, new[] { otherBinding }));

				Assert.AreEqual(CollectionUninstallNativeDisposition.BlockedSharedMissing, plan.Impacts.Single().Disposition);
				Assert.IsTrue(plan.HasBlockedImpacts);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Submission_SharedBindingAppearingAfterPreviewRejectsBeforeNativeBoundary()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "submission-race");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				CollectionOperation operation = CreateOperation(fixture.Association, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, 1, new CollectionNativeChildOperation[0]);
				InvokeStore(fixture.Associations, "BeginUninstallEffects", fixture.Association, operation);

				CollectionTargetAssociation other = SeedAssociation(fixture.Store, "submission-race-other", fixture.Target);
				fixture.Associations.SaveBinding(new CollectionMemberBinding(other, CollectionMemberKey.FromProvider("other-member"),
					fixture.NativeMod, fixture.Binding.VerifiedRecipe, CollectionMemberBindingKind.AdoptedExisting));
				CollectionOperation submitted = CreateSubmittedOperation(operation, fixture.Binding);

				TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
					InvokeStore(fixture.Associations, "SaveUninstallChildSubmission", fixture.Association.AssociationId,
						submitted, fixture.NativeMod, new[] { fixture.Binding.MemberKey }));
				Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
				Assert.AreEqual(CollectionAssociationState.Applied,
					fixture.Associations.GetAssociation(fixture.Association.AssociationId).State);
				CollectionOperation persisted = new CollectionsOperationStore(fixture.Store).GetOperation(operation.Identity);
				Assert.AreEqual(0, persisted.NativeChildren.Count);
				Assert.IsFalse(persisted.HasCrossedNativeBoundary);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Submission_CustomizationAppearingAfterPreviewRejectsBeforeNativeBoundary()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "customization-race");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				CollectionOperation operation = CreateOperation(fixture.Association, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, 1, new CollectionNativeChildOperation[0]);
				InvokeStore(fixture.Associations, "BeginUninstallEffects", fixture.Association, operation);
				var requirement = new CollectionRequirementReference(fixture.Association, fixture.Binding.MemberKey,
					CollectionRequirementAspect.MemberEnabledState, null);
				fixture.Associations.SaveOverride(new UserOverride(Guid.NewGuid(), requirement,
					CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Absent(), "late customization"));
				CollectionOperation submitted = CreateSubmittedOperation(operation, fixture.Binding);

				TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
					InvokeStore(fixture.Associations, "SaveUninstallChildSubmission", fixture.Association.AssociationId,
						submitted, fixture.NativeMod, new[] { fixture.Binding.MemberKey }));
				Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
				CollectionOperation persisted = new CollectionsOperationStore(fixture.Store).GetOperation(operation.Identity);
				Assert.AreEqual(0, persisted.NativeChildren.Count);
				Assert.IsFalse(persisted.HasCrossedNativeBoundary);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Submission_ModifiedAssociationAllowsUncustomizedNativeMemberOnly()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "modified-subset");
				var nativeMod2 = new NativeModInstanceIdentity(fixture.Target, "native-2");
				var binding2 = new CollectionMemberBinding(fixture.Association, CollectionMemberKey.FromProvider("member-2"),
					nativeMod2, CollectionRecipeIdentity.FromFingerprint("recipe-2"), CollectionMemberBindingKind.InstalledForCollection);
				fixture.Associations.SaveBinding(binding2);
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(nativeMod2,
					StandaloneModUse.NoStandaloneUseVerified));

				var requirement = new CollectionRequirementReference(fixture.Association, fixture.Binding.MemberKey,
					CollectionRequirementAspect.MemberEnabledState, null);
				fixture.Associations.SaveOverride(new UserOverride(Guid.NewGuid(), requirement,
					CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Absent(), "Keep first member disabled"));
				fixture.Associations.SaveAssociation(fixture.Association.WithState(CollectionAssociationState.Modified));
				CollectionTargetAssociation modified = fixture.Associations.GetAssociation(fixture.Association.AssociationId);
				CollectionOperation operation = CreateOperation(modified, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, 1, new CollectionNativeChildOperation[0]);
				InvokeStore(fixture.Associations, "BeginUninstallEffects", modified, operation);
				CollectionOperation submitted = CreateSubmittedOperation(operation, binding2);

				Assert.DoesNotThrow(() => InvokeStore(fixture.Associations, "SaveUninstallChildSubmission",
					modified.AssociationId, submitted, nativeMod2, new[] { binding2.MemberKey }));
				Assert.AreEqual(CollectionAssociationState.Incomplete,
					fixture.Associations.GetAssociation(modified.AssociationId).State);
				Assert.AreEqual(CollectionNativeChildCheckpoint.NativeSubmitted,
					new CollectionsOperationStore(fixture.Store).GetOperation(operation.Identity).NativeChildren.Single().Checkpoint);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Reconciliation_UnexpectedLateSharedBindingIsInvalidatedWhenNativeRemovalAlreadyCommitted()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "late-shared");
				fixture.Associations.SaveNativeModProvenance(new NativeModProvenance(fixture.NativeMod,
					StandaloneModUse.NoStandaloneUseVerified));
				CollectionOperation operation = CreateOperation(fixture.Association, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, 1, new CollectionNativeChildOperation[0]);
				InvokeStore(fixture.Associations, "BeginUninstallEffects", fixture.Association, operation);
				CollectionOperation submitted = CreateSubmittedOperation(operation, fixture.Binding);
				InvokeStore(fixture.Associations, "SaveUninstallChildSubmission", fixture.Association.AssociationId,
					submitted, fixture.NativeMod, new[] { fixture.Binding.MemberKey });

				CollectionTargetAssociation other = SeedAssociation(fixture.Store, "late-shared-other", fixture.Target);
				fixture.Associations.SaveBinding(new CollectionMemberBinding(other, CollectionMemberKey.FromProvider("other-member"),
					fixture.NativeMod, fixture.Binding.VerifiedRecipe, CollectionMemberBindingKind.AdoptedExisting));
				CollectionNativeChildOperation submittedChild = submitted.NativeChildren.Single();
				var nativeResult = new ModOperationResult(submittedChild.NativeOperation, ModOperationReportedStatus.Succeeded,
					ModOperationDurability.VerifiedCommitted, "removed");
				var reconciledChild = new CollectionNativeChildOperation(submittedChild.Sequence, submittedChild.Member,
					submittedChild.Action, submittedChild.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, nativeResult);
				var reconciled = new CollectionOperation(submitted.Identity, submitted.Kind, submitted.Collection, submitted.Target,
					submitted.Revision, submitted.PlanIdentity, checked(submitted.CheckpointSequence + 1), submitted.Phase,
					submitted.ResultState, new[] { reconciledChild });

				InvokeStore(fixture.Associations, "SaveUninstallChildReconciliation", fixture.Association.AssociationId,
					reconciled, fixture.NativeMod, true);

				Assert.AreEqual(CollectionAssociationState.Incomplete, fixture.Associations.GetAssociation(other.AssociationId).State);
				Assert.AreEqual(0, fixture.Associations.GetBindings(fixture.Association.AssociationId).Count);
				Assert.AreEqual(1, fixture.Associations.GetBindings(other.AssociationId).Count);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void OverrideDecision_WhileUninstallOperationIsIncompleteIsRejectedWithoutChangingUserIntent()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "override-race");
				CollectionOperation operation = CreateOperation(fixture.Association, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, 1, new CollectionNativeChildOperation[0]);
				InvokeStore(fixture.Associations, "BeginUninstallEffects", fixture.Association, operation);
				var coordinator = new CollectionPinOverrideCoordinator(fixture.Associations, fixture.Target);
				var requirement = new CollectionRequirementReference(fixture.Association, fixture.Binding.MemberKey,
					CollectionRequirementAspect.MemberEnabledState, null);

				Assert.Throws<InvalidOperationException>(() => coordinator.RecordDecision(requirement,
					CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Absent(),
					CollectionRequirementState.Absent(), "Keep disabled"));
				Assert.AreEqual(0, fixture.Associations.GetOverrides(fixture.Association.AssociationId).Count);
				Assert.AreEqual(0, fixture.Associations.GetDriftObservations(fixture.Association.AssociationId).Count);
				Assert.AreEqual(CollectionAssociationState.Applied,
					fixture.Associations.GetAssociation(fixture.Association.AssociationId).State);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void AppliedAssociationPublication_IsBlockedWhileUninstallOwnsTarget()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "publish-race");
				CollectionTargetAssociation other = SeedAssociation(fixture.Store, "publish-race-other", fixture.Target);
				var otherBinding = new CollectionMemberBinding(other, CollectionMemberKey.FromProvider("other-member"),
					fixture.NativeMod, fixture.Binding.VerifiedRecipe, CollectionMemberBindingKind.AdoptedExisting);
				CollectionOperation operation = CreateOperation(fixture.Association, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, 1, new CollectionNativeChildOperation[0]);
				InvokeStore(fixture.Associations, "BeginUninstallEffects", fixture.Association, operation);

				TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
					InvokeStore(fixture.Associations, "SaveAppliedAssociation", other, new[] { otherBinding },
						new NativeModProvenance[0]));
				Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
				Assert.AreEqual(0, fixture.Associations.GetBindings(other.AssociationId).Count);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Completion_PreservesRetainedModAsStandaloneAndDeletesOnlyCollectionTrackingAtomically()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "complete-metadata");
				var operationStore = new CollectionsOperationStore(fixture.Store);
				CollectionOperation operation = CreateOperation(fixture.Association, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, 1, new CollectionNativeChildOperation[0]);
				InvokeStore(fixture.Associations, "BeginUninstallEffects", fixture.Association, operation);
				CollectionOperation completed = CreateOperation(fixture.Association, CollectionOperationPhase.Completed,
					CollectionOperationResultState.Committed, 2, new CollectionNativeChildOperation[0], operation.Identity);
				InvokeStore(fixture.Associations, "CompleteUninstallEffects", fixture.Association.AssociationId, completed,
					new[] { fixture.NativeMod }, new NativeModInstanceIdentity[0]);

				Assert.IsNull(fixture.Associations.GetAssociation(fixture.Association.AssociationId));
				Assert.AreEqual(0, fixture.Associations.GetBindings(fixture.Association.AssociationId).Count);
				Assert.AreEqual(StandaloneModUse.ExplicitStandaloneUse,
					fixture.Associations.GetNativeModProvenance(fixture.NativeMod).StandaloneUse);
				Assert.AreEqual(CollectionOperationResultState.Committed,
					operationStore.GetOperation(operation.Identity).ResultState);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Completion_JournalFailureRollsBackAssociationDeletionAndStandaloneProvenance()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "completion-atomic");
				CollectionOperation operation = CreateOperation(fixture.Association, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, 1, new CollectionNativeChildOperation[0]);
				InvokeStore(fixture.Associations, "BeginUninstallEffects", fixture.Association, operation);
				using (SQLiteConnection connection = OpenDatabase(fixture.Store.DatabasePath))
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.CommandText = @"
CREATE TRIGGER fail_c614_completion
BEFORE UPDATE ON collection_operations
WHEN NEW.kind = 5 AND NEW.phase = 14
BEGIN
	SELECT RAISE(ABORT, 'forced C6.14 completion failure');
END;";
					command.ExecuteNonQuery();
				}
				CollectionOperation completed = CreateOperation(fixture.Association, CollectionOperationPhase.Completed,
					CollectionOperationResultState.Committed, 2, new CollectionNativeChildOperation[0], operation.Identity);

				TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
					InvokeStore(fixture.Associations, "CompleteUninstallEffects", fixture.Association.AssociationId, completed,
						new[] { fixture.NativeMod }, new NativeModInstanceIdentity[0]));
				Assert.IsInstanceOf<SQLiteException>(error.InnerException);
				Assert.IsNotNull(fixture.Associations.GetAssociation(fixture.Association.AssociationId));
				Assert.AreEqual(1, fixture.Associations.GetBindings(fixture.Association.AssociationId).Count);
				Assert.AreEqual(StandaloneModUse.Unknown,
					fixture.Associations.GetNativeModProvenance(fixture.NativeMod).StandaloneUse);
			}
			finally { Directory.Delete(root, true); }
		}

		private static CollectionUninstallEffectsPlan BuildPlan(CollectionsAssociationStore associations,
			CollectionTargetAssociation association, CollectionNativeStateIndex state)
		{
			MethodInfo method = typeof(CollectionUninstallEffectsCoordinator).GetMethod("BuildPlanForState",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			return (CollectionUninstallEffectsPlan)method.Invoke(null, new object[] { associations, association, state });
		}

		private static object InvokeStore(CollectionsAssociationStore store, string name, params object[] args)
		{
			MethodInfo method = typeof(CollectionsAssociationStore).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(method, name);
			return method.Invoke(store, args);
		}

		private static CollectionOperation CreateSubmittedOperation(CollectionOperation operation, CollectionMemberBinding binding)
		{
			ModOperationIdentity nativeOperation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(operation.Target.Fingerprint,
					new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), null));
			var child = new CollectionNativeChildOperation(1,
				new CollectionOperationMemberReference(operation.Revision, binding.MemberKey),
				CollectionNativeChildAction.Deactivate, nativeOperation, CollectionNativeChildCheckpoint.NativeSubmitted, null);
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
				operation.ResultState, new[] { child });
		}

		private static CollectionOperation CreateOperation(CollectionTargetAssociation association, CollectionOperationPhase phase,
			CollectionOperationResultState result, long checkpoint, IEnumerable<CollectionNativeChildOperation> children,
			CollectionOperationIdentity identity = null)
		{
			return new CollectionOperation(identity ?? CollectionOperationIdentity.CreateNew(),
				CollectionOperationKind.UninstallCollectionEffects, association.Revision.Collection, association.Target,
				association.Revision, null, checkpoint, phase, result, children);
		}

		private static CollectionNativeStateIndex CreateState(Fixture fixture, bool includeNativeMod,
			IEnumerable<CollectionTargetAssociation> extraAssociations, IEnumerable<CollectionMemberBinding> extraBindings,
			IEnumerable<CollectionNativeStateIssue> issues = null)
		{
			var associations = new List<CollectionTargetAssociation> { fixture.Association };
			if (extraAssociations != null) associations.AddRange(extraAssociations);
			var bindings = new List<CollectionMemberBinding> { fixture.Binding };
			if (extraBindings != null) bindings.AddRange(extraBindings);
			return new CollectionNativeStateIndex(fixture.Target, new CollectionNativeRootState[0],
				includeNativeMod ? new[] { fixture.NativeState } : new CollectionNativeModState[0],
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				associations, bindings, new UserOverride[0], CollectionNativeStateCoverage.Complete,
				issues ?? new CollectionNativeStateIssue[0], 0);
		}

		private static Fixture CreateFixture(string root, string collectionId)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c614-" + collectionId);
			CollectionTargetAssociation association = SeedAssociation(store, collectionId, target);
			var associations = new CollectionsAssociationStore(store);
			var nativeMod = new NativeModInstanceIdentity(target, "native-1");
			var binding = new CollectionMemberBinding(association, CollectionMemberKey.FromProvider("member-1"), nativeMod,
				CollectionRecipeIdentity.FromFingerprint("recipe-1"), CollectionMemberBindingKind.InstalledForCollection);
			associations.SaveBinding(binding);
			var nativeState = new CollectionNativeModState(nativeMod, "C:\\Mods\\Example.7z", "Example.7z", "100", "200",
				"1.0", "1.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
			return new Fixture(store, associations, target, association, binding, nativeMod, nativeState);
		}

		private static CollectionTargetAssociation SeedAssociation(CollectionsStore store, string collectionId,
			CollectionTargetIdentity target)
		{
			var catalog = new CollectionsCatalogStore(store);
			CollectionIdentity collection = CollectionIdentity.FromNexus(collectionId);
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-1", 1);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, collectionId, null, null),
				new CollectionRevision(revision, "Revision", null, 1));
			var association = new CollectionTargetAssociation(Guid.NewGuid(), revision, target, CollectionAssociationState.Applied);
			new CollectionsAssociationStore(store).SaveAssociation(association);
			return association;
		}

		private static SQLiteConnection OpenDatabase(string databasePath)
		{
			var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
			{
				DataSource = databasePath,
				ForeignKeys = true,
				Pooling = false,
				FailIfMissing = true
			}.ConnectionString);
			connection.Open();
			return connection;
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c614-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private sealed class Fixture
		{
			public Fixture(CollectionsStore store, CollectionsAssociationStore associations, CollectionTargetIdentity target,
				CollectionTargetAssociation association, CollectionMemberBinding binding, NativeModInstanceIdentity nativeMod,
				CollectionNativeModState nativeState)
			{
				Store = store;
				Associations = associations;
				Target = target;
				Association = association;
				Binding = binding;
				NativeMod = nativeMod;
				NativeState = nativeState;
			}

			public CollectionsStore Store { get; }
			public CollectionsAssociationStore Associations { get; }
			public CollectionTargetIdentity Target { get; }
			public CollectionTargetAssociation Association { get; }
			public CollectionMemberBinding Binding { get; }
			public NativeModInstanceIdentity NativeMod { get; }
			public CollectionNativeModState NativeState { get; }
		}
	}
}
