using System;
using System.Collections.Generic;
using NUnit.Framework;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.Tests
{
	[TestFixture]
	public class CollectionOperationContractsTests
	{
		[Test]
		public void OperationIdentity_IsStableValueAndRejectsEmptyGuid()
		{
			Guid id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
			CollectionOperationIdentity first = CollectionOperationIdentity.From(id);
			CollectionOperationIdentity same = CollectionOperationIdentity.From(id);

			Assert.That(first, Is.EqualTo(same));
			Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
			Assert.Throws<ArgumentException>(() => CollectionOperationIdentity.From(Guid.Empty));
			Assert.That(CollectionOperationIdentity.CreateNew().OperationId, Is.Not.EqualTo(Guid.Empty));
		}

		[Test]
		public void MemberReference_PinsExactRevisionAndMember()
		{
			CollectionRevisionIdentity revision = CreateRevision("772530", 100);
			CollectionMemberKey member = CollectionMemberKey.FromProvider("member-1");
			CollectionOperationMemberReference reference = new CollectionOperationMemberReference(revision, member);

			Assert.That(reference.Revision, Is.SameAs(revision));
			Assert.That(reference.MemberKey, Is.SameAs(member));
			Assert.That(reference, Is.EqualTo(new CollectionOperationMemberReference(CreateRevision("772530", 100),
				CollectionMemberKey.FromProvider("member-1"))));
			Assert.Throws<ArgumentNullException>(() => new CollectionOperationMemberReference(null, member));
			Assert.Throws<ArgumentNullException>(() => new CollectionOperationMemberReference(revision, null));
		}

		[Test]
		public void NativeChild_RequiresPersistedSafetyOrderingBeforeTerminalResult()
		{
			ModOperationIdentity native = CreateNativeIdentity(ModOperationOrigin.Collection, "target-A");
			CollectionOperationMemberReference member = CreateMemberReference();
			ModOperationResult result = new ModOperationResult(native, ModOperationReportedStatus.Succeeded,
				ModOperationDurability.VerifiedCommitted, null);

			CollectionNativeChildOperation intent = new CollectionNativeChildOperation(1, member,
				CollectionNativeChildAction.ActivateOrReinstall, native,
				CollectionNativeChildCheckpoint.IntentPersisted, null);
			CollectionNativeChildOperation prepared = new CollectionNativeChildOperation(1, member,
				CollectionNativeChildAction.ActivateOrReinstall, native,
				CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
			CollectionNativeChildOperation terminal = new CollectionNativeChildOperation(1, member,
				CollectionNativeChildAction.ActivateOrReinstall, native,
				CollectionNativeChildCheckpoint.NativeTerminalObserved, result);

			Assert.That(intent.HasCrossedNativeBoundary, Is.False);
			Assert.That(prepared.HasCrossedNativeBoundary, Is.False);
			Assert.That(terminal.HasCrossedNativeBoundary, Is.True);
			Assert.That(terminal.HasVerifiedCommittedNativeState, Is.True);
			Assert.That(terminal.IsReconciled, Is.False);
			Assert.Throws<ArgumentNullException>(() => new CollectionNativeChildOperation(1, member,
				CollectionNativeChildAction.ActivateOrReinstall, native,
				CollectionNativeChildCheckpoint.NativeTerminalObserved, null));
			Assert.Throws<ArgumentException>(() => new CollectionNativeChildOperation(1, member,
				CollectionNativeChildAction.ActivateOrReinstall, native,
				CollectionNativeChildCheckpoint.RecoveryInputsReady, result));
		}

		[Test]
		public void NativeChild_PreservesReportedFailureSeparateFromVerifiedCommit()
		{
			ModOperationIdentity native = CreateNativeIdentity(ModOperationOrigin.Collection, "target-A");
			ModOperationResult failedAfterCommit = new ModOperationResult(native, ModOperationReportedStatus.Failed,
				ModOperationDurability.VerifiedCommitted, "Post-commit publication failed.");
			CollectionNativeChildOperation child = new CollectionNativeChildOperation(1, CreateMemberReference(),
				CollectionNativeChildAction.ActivateOrReinstall, native,
				CollectionNativeChildCheckpoint.Reconciled, failedAfterCommit);

			Assert.That(child.NativeResult.ReportedStatus, Is.EqualTo(ModOperationReportedStatus.Failed));
			Assert.That(child.HasVerifiedCommittedNativeState, Is.True);
			Assert.That(child.HasUnknownNativeDurability, Is.False);
		}

		[Test]
		public void NativeChild_RejectsMismatchedAttemptAndNonCollectionOrigin()
		{
			ModOperationIdentity native = CreateNativeIdentity(ModOperationOrigin.Collection, "target-A");
			ModOperationIdentity otherAttempt = native.CreateNextAttempt();
			ModOperationResult otherResult = new ModOperationResult(otherAttempt, ModOperationReportedStatus.Failed,
				ModOperationDurability.Unknown, null);
			CollectionOperationMemberReference member = CreateMemberReference();

			Assert.Throws<ArgumentException>(() => new CollectionNativeChildOperation(1, member,
				CollectionNativeChildAction.ActivateOrReinstall, native,
				CollectionNativeChildCheckpoint.NativeTerminalObserved, otherResult));
			Assert.Throws<ArgumentException>(() => new CollectionNativeChildOperation(1, member,
				CollectionNativeChildAction.ActivateOrReinstall,
				CreateNativeIdentity(ModOperationOrigin.Manual, "target-A"),
				CollectionNativeChildCheckpoint.IntentPersisted, null));
		}

		[Test]
		public void Operation_CancelledBeforeApplyCannotHideSubmittedNativeChild()
		{
			CollectionNativeChildOperation submitted = CreateChild(1, ModOperationOrigin.Collection,
				CollectionNativeChildCheckpoint.NativeSubmitted, null, "target-A");

			Assert.Throws<ArgumentException>(() => CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.Completed,
				CollectionOperationResultState.CancelledBeforeApply,
				new[] { submitted }));

			CollectionOperation cancelled = CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.Completed,
				CollectionOperationResultState.CancelledBeforeApply,
				new[]
				{
					CreateChild(1, ModOperationOrigin.Collection,
						CollectionNativeChildCheckpoint.RecoveryInputsReady, null, "target-A")
				});

			Assert.That(cancelled.HasCrossedNativeBoundary, Is.False);
			Assert.That(cancelled.IsTerminal, Is.True);
			Assert.That(cancelled.IsSuccessful, Is.False);
		}

		[Test]
		public void Operation_StoppedPartialPreservesCommittedChildDurability()
		{
			CollectionNativeChildOperation committed = CreateTerminalChild(1, ModOperationOrigin.Collection,
				ModOperationReportedStatus.Failed, ModOperationDurability.VerifiedCommitted,
				CollectionNativeChildCheckpoint.Reconciled, "target-A");
			CollectionOperation operation = CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.Completed,
				CollectionOperationResultState.StoppedPartial,
				new[] { committed });

			Assert.That(operation.HasVerifiedCommittedNativeChild, Is.True);
			Assert.That(operation.ResultState, Is.EqualTo(CollectionOperationResultState.StoppedPartial));
			Assert.That(operation.IsSuccessful, Is.False);
		}

		[Test]
		public void Operation_RecoveryRequiredIsExplicitAndNonTerminal()
		{
			CollectionNativeChildOperation ambiguous = CreateTerminalChild(1, ModOperationOrigin.Collection,
				ModOperationReportedStatus.Failed, ModOperationDurability.Unknown,
				CollectionNativeChildCheckpoint.NativeTerminalObserved, "target-A");
			CollectionOperation operation = CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.RecoveryRequired,
				new[] { ambiguous });

			Assert.That(operation.RequiresRecovery, Is.True);
			Assert.That(operation.IsTerminal, Is.False);
			Assert.That(operation.HasUnknownNativeDurability, Is.True);
			Assert.That(operation.HasUnreconciledNativeChild, Is.True);
			Assert.Throws<ArgumentException>(() => CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.Pending,
				new[] { ambiguous }));
		}

		[Test]
		public void Operation_CommittedRequiresSubmittedChildrenToBeReconciled()
		{
			CollectionNativeChildOperation terminal = CreateTerminalChild(1, ModOperationOrigin.Collection,
				ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted,
				CollectionNativeChildCheckpoint.NativeTerminalObserved, "target-A");
			CollectionNativeChildOperation reconciled = CreateTerminalChild(1, ModOperationOrigin.Collection,
				ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted,
				CollectionNativeChildCheckpoint.Reconciled, "target-A");

			Assert.Throws<ArgumentException>(() => CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.Completed,
				CollectionOperationResultState.Committed,
				new[] { terminal }));

			CollectionOperation committed = CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.Completed,
				CollectionOperationResultState.Committed,
				new[] { reconciled });
			Assert.That(committed.IsSuccessful, Is.True);
			Assert.That(committed.HasUnreconciledNativeChild, Is.False);
		}

		[Test]
		public void Operation_EnforcesTargetOriginAndUniqueNativeCorrelation()
		{
			CollectionNativeChildOperation wrongTarget = CreateChild(1, ModOperationOrigin.Collection,
				CollectionNativeChildCheckpoint.IntentPersisted, null, "target-B");
			Assert.Throws<ArgumentException>(() => CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.Preparing,
				CollectionOperationResultState.Pending,
				new[] { wrongTarget }));

			CollectionNativeChildOperation localRestore = CreateChild(1, ModOperationOrigin.LocalRestore,
				CollectionNativeChildCheckpoint.IntentPersisted, null, "target-A");
			Assert.Throws<ArgumentException>(() => CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.Preparing,
				CollectionOperationResultState.Pending,
				new[] { localRestore }));
			Assert.DoesNotThrow(() => CreateOperation(
				CollectionOperationKind.RestoreLocalCapture,
				CollectionOperationPhase.Preparing,
				CollectionOperationResultState.Pending,
				new[] { localRestore }));

			ModOperationIdentity native = CreateNativeIdentity(ModOperationOrigin.Collection, "target-A");
			CollectionNativeChildOperation first = new CollectionNativeChildOperation(1, CreateMemberReference(),
				CollectionNativeChildAction.ActivateOrReinstall, native,
				CollectionNativeChildCheckpoint.IntentPersisted, null);
			CollectionNativeChildOperation duplicateLogical = new CollectionNativeChildOperation(2, CreateMemberReference("member-2"),
				CollectionNativeChildAction.ActivateOrReinstall, native.CreateNextAttempt(),
				CollectionNativeChildCheckpoint.IntentPersisted, null);
			Assert.Throws<ArgumentException>(() => CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.Preparing,
				CollectionOperationResultState.Pending,
				new[] { first, duplicateLogical }));
		}

		[Test]
		public void Operation_DefensivelyCopiesAndOrdersChildrenByJournalSequence()
		{
			List<CollectionNativeChildOperation> children = new List<CollectionNativeChildOperation>
			{
				CreateChild(2, ModOperationOrigin.Collection, CollectionNativeChildCheckpoint.IntentPersisted, null, "target-A", "member-2"),
				CreateChild(1, ModOperationOrigin.Collection, CollectionNativeChildCheckpoint.IntentPersisted, null, "target-A", "member-1")
			};
			CollectionOperation operation = CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.Preparing,
				CollectionOperationResultState.Pending,
				children);
			children.Clear();

			Assert.That(operation.NativeChildren.Count, Is.EqualTo(2));
			Assert.That(operation.NativeChildren[0].Sequence, Is.EqualTo(1));
			Assert.That(operation.NativeChildren[1].Sequence, Is.EqualTo(2));
			Assert.Throws<NotSupportedException>(() => ((IList<CollectionNativeChildOperation>)operation.NativeChildren).Clear());
		}

		[Test]
		public void Operation_ValidatesPhaseResultRevisionPlanAndModelsRemainImmutable()
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("2210");
			CollectionIdentity otherCollection = CollectionIdentity.FromNexus("9999");
			CollectionRevisionIdentity foreignRevision = CollectionRevisionIdentity.FromNexus(otherCollection, "1", 1);

			Assert.Throws<ArgumentException>(() => new CollectionOperation(
				CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ApplyResolvedPlan,
				collection, CollectionTargetIdentity.FromFingerprint("target-A"), foreignRevision, null, 0,
				CollectionOperationPhase.Created, CollectionOperationResultState.Pending,
				new CollectionNativeChildOperation[0]));
			Assert.Throws<ArgumentException>(() => new CollectionOperation(
				CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ApplyResolvedPlan,
				collection, CollectionTargetIdentity.FromFingerprint("target-A"), null,
				CollectionPlanIdentity.From(Guid.NewGuid(), 1), 0,
				CollectionOperationPhase.Created, CollectionOperationResultState.Pending,
				new CollectionNativeChildOperation[0]));
			Assert.Throws<ArgumentException>(() => CreateOperation(
				CollectionOperationKind.ApplyResolvedPlan,
				CollectionOperationPhase.Preparing,
				CollectionOperationResultState.Committed,
				new CollectionNativeChildOperation[0]));

			AssertNoPublicSetters(typeof(CollectionOperationIdentity));
			AssertNoPublicSetters(typeof(CollectionOperationMemberReference));
			AssertNoPublicSetters(typeof(CollectionNativeChildOperation));
			AssertNoPublicSetters(typeof(CollectionOperation));
		}

		private static CollectionOperation CreateOperation(CollectionOperationKind kind,
			CollectionOperationPhase phase, CollectionOperationResultState result,
			IEnumerable<CollectionNativeChildOperation> children)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("2210");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "772530", 100);
			return new CollectionOperation(
				CollectionOperationIdentity.From(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
				kind,
				collection,
				CollectionTargetIdentity.FromFingerprint("target-A"),
				revision,
				CollectionPlanIdentity.From(Guid.Parse("11111111-2222-3333-4444-555555555555"), 1),
				7,
				phase,
				result,
				children);
		}

		private static CollectionNativeChildOperation CreateChild(int sequence, ModOperationOrigin origin,
			CollectionNativeChildCheckpoint checkpoint, ModOperationResult result, string target,
			string memberKey = "member-1")
		{
			ModOperationIdentity native = result == null ? CreateNativeIdentity(origin, target) : result.Identity;
			return new CollectionNativeChildOperation(sequence, CreateMemberReference(memberKey),
				CollectionNativeChildAction.ActivateOrReinstall, native, checkpoint, result);
		}

		private static CollectionNativeChildOperation CreateTerminalChild(int sequence, ModOperationOrigin origin,
			ModOperationReportedStatus status, ModOperationDurability durability,
			CollectionNativeChildCheckpoint checkpoint, string target)
		{
			ModOperationIdentity native = CreateNativeIdentity(origin, target);
			ModOperationResult result = new ModOperationResult(native, status, durability, null);
			return new CollectionNativeChildOperation(sequence, CreateMemberReference("member-" + sequence),
				CollectionNativeChildAction.ActivateOrReinstall, native, checkpoint, result);
		}

		private static ModOperationIdentity CreateNativeIdentity(ModOperationOrigin origin, string target)
		{
			ModInstallContext context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot);
			return ModOperationIdentity.CreateNew(origin,
				new ModOperationFingerprint(target, context, "recipe-v1"));
		}

		private static CollectionOperationMemberReference CreateMemberReference(string memberKey = "member-1")
		{
			return new CollectionOperationMemberReference(CreateRevision("772530", 100),
				CollectionMemberKey.FromProvider(memberKey));
		}

		private static CollectionRevisionIdentity CreateRevision(string revisionId, long revisionNumber)
		{
			return CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("2210"), revisionId, revisionNumber);
		}

		private static void AssertNoPublicSetters(Type type)
		{
			foreach (var property in type.GetProperties())
				Assert.That(property.GetSetMethod(), Is.Null, type.Name + "." + property.Name + " must remain immutable.");
		}
	}
}
