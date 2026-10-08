using System;
using NUnit.Framework;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.UI;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.Tests
{
	[TestFixture]
	public class CollectionUiContextTests
	{
		[Test]
		public void Factories_KeepActionScopesAndExactTargetIdentitySeparate()
		{
			CollectionRevisionIdentity remote = CreateNexusRevision("collection-a", "revision-a", 10);
			CollectionRevisionIdentity local = CreateLocalRevision();
			LocalCaptureIdentity capture = LocalCaptureIdentity.From(Guid.Parse("11111111-2222-3333-4444-555555555555"));
			Guid association = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

			CollectionUiContext incoming = CollectionUiContext.Incoming(7, remote, null);
			CollectionUiContext installed = CollectionUiContext.Installed(7, remote, association);
			CollectionUiContext savedLocal = CollectionUiContext.SavedLocal(7, local, capture);
			CollectionUiContext currentSetup = CollectionUiContext.CurrentSetup(7);

			Assert.That(incoming.Kind, Is.EqualTo(CollectionUiContextKind.IncomingCollection));
			Assert.That(incoming.Revision, Is.SameAs(remote));
			Assert.That(installed.Kind, Is.EqualTo(CollectionUiContextKind.InstalledCollection));
			Assert.That(installed.AssociationId, Is.EqualTo(association));
			Assert.That(savedLocal.Kind, Is.EqualTo(CollectionUiContextKind.SavedLocalCollection));
			Assert.That(savedLocal.LocalCapture, Is.SameAs(capture));
			Assert.That(currentSetup.Kind, Is.EqualTo(CollectionUiContextKind.CurrentSetup));
			Assert.That(currentSetup.Revision, Is.Null);
		}

		[Test]
		public void IncomingContext_CanGainExactRevisionAndOperationWithoutChangingGeneration()
		{
			CollectionUiContext loading = CollectionUiContext.Incoming(3, null, null);
			CollectionRevisionIdentity revision = CreateNexusRevision("collection-a", "revision-a", 10);
			CollectionOperationIdentity operation = CollectionOperationIdentity.From(Guid.Parse("12345678-1234-1234-1234-1234567890ab"));

			CollectionUiContext resolved = loading.WithRevision(revision).WithOperation(operation);

			Assert.That(resolved.Generation, Is.EqualTo(loading.Generation));
			Assert.That(resolved.Kind, Is.EqualTo(CollectionUiContextKind.IncomingCollection));
			Assert.That(resolved.Revision, Is.SameAs(revision));
			Assert.That(resolved.Operation, Is.SameAs(operation));
		}

		[Test]
		public void Generation_DistinguishesCurrentAndSupersededSurfaceWork()
		{
			CollectionUiContext oldContext = CollectionUiContext.Incoming(12, null, null);

			Assert.That(oldContext.IsCurrentGeneration(12), Is.True);
			Assert.That(oldContext.IsCurrentGeneration(13), Is.False);
		}

		[Test]
		public void InstalledAndSavedLocalContexts_RejectIncompleteOrWrongIdentity()
		{
			CollectionRevisionIdentity remote = CreateNexusRevision("collection-a", "revision-a", 10);
			CollectionRevisionIdentity local = CreateLocalRevision();
			LocalCaptureIdentity capture = LocalCaptureIdentity.From(Guid.Parse("11111111-2222-3333-4444-555555555555"));

			Assert.Throws<ArgumentException>(() => CollectionUiContext.Incoming(1, local, null));
			Assert.Throws<ArgumentException>(() => CollectionUiContext.Installed(1, remote, Guid.Empty));
			Assert.Throws<ArgumentNullException>(() => CollectionUiContext.SavedLocal(1, local, null));
			Assert.Throws<ArgumentException>(() => CollectionUiContext.SavedLocal(1, remote, capture));
			Assert.Throws<InvalidOperationException>(() => CollectionUiContext.CurrentSetup(1).WithOperation(CollectionOperationIdentity.CreateNew()));
		}

		/// <summary>Both upgrade and downgrade candidates must enter revision review instead of additive preparation.</summary>
		[TestCase(6, 5)]
		[TestCase(5, 6)]
		public void RevisionChangeCandidate_AcceptsDifferentRevisionsInEitherDirection(long installedNumber, long incomingNumber)
		{
			CollectionRevisionIdentity installed = CreateNexusRevision("collection-a", "revision-" + installedNumber, installedNumber);
			CollectionRevisionIdentity incoming = CreateNexusRevision("collection-a", "revision-" + incomingNumber, incomingNumber);

			Assert.That(CollectionsPreviewControl.IsRevisionChangeCandidate(incoming, installed), Is.True);
			Assert.That(CollectionsPreviewControl.IsRevisionChangeCandidate(installed, installed), Is.False);
			Assert.That(CollectionsPreviewControl.IsRevisionChangeCandidate(
				CreateNexusRevision("collection-b", "revision-other", incomingNumber), installed), Is.False);
			Assert.That(CollectionsPreviewControl.IsRevisionChangeCandidate(null, installed), Is.False);
			Assert.That(CollectionsPreviewControl.IsRevisionChangeCandidate(incoming, null), Is.False);
		}

		/// <summary>Revision review may supersede additive preparation without abandoning another pending workflow.</summary>
		[TestCase(CollectionOperationKind.ApplyResolvedPlan, true)]
		[TestCase(CollectionOperationKind.ReplaceCurrentManagedSetup, false)]
		[TestCase(CollectionOperationKind.RestoreLocalCapture, false)]
		[TestCase(CollectionOperationKind.UpdateRevision, false)]
		public void RevisionReview_SupersedesOnlyUnusedAdditivePreparation(CollectionOperationKind kind, bool expected)
		{
			CollectionRevisionIdentity revision = CreateNexusRevision("collection-a", "revision-a", 5);
			CollectionOperation operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), kind,
				revision.Collection, CollectionTargetIdentity.FromFingerprint("target-ui-review"), revision, null, 3,
				CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);

			Assert.That(CollectionsPreviewControl.CanSupersedePreparationForRevisionChange(operation), Is.EqualTo(expected));
		}

		/// <summary>Changing revision must not conceal an additive operation whose native child was submitted.</summary>
		[Test]
		public void RevisionReview_DoesNotSupersedeSubmittedNativeWork()
		{
			CollectionRevisionIdentity revision = CreateNexusRevision("collection-a", "revision-a", 5);
			ModInstallContext installContext = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-ui-review", installContext, "recipe-v1"));
			CollectionNativeChildOperation submitted = new CollectionNativeChildOperation(1,
				new CollectionOperationMemberReference(revision, CollectionMemberKey.FromProvider("member-1")),
				CollectionNativeChildAction.ActivateOrReinstall, native, CollectionNativeChildCheckpoint.NativeSubmitted, null);
			CollectionOperation operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(),
				CollectionOperationKind.ApplyResolvedPlan, revision.Collection,
				CollectionTargetIdentity.FromFingerprint("target-ui-review"), revision, CollectionPlanIdentity.From(Guid.NewGuid(), 1), 9,
				CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending, new[] { submitted });

			Assert.That(operation.HasCrossedNativeBoundary, Is.True);
			Assert.That(CollectionsPreviewControl.CanSupersedePreparationForRevisionChange(operation), Is.False);
		}

		/// <summary>Pending revision presentation must remain bound to the reviewed association, old revision and target.</summary>
		[TestCase("matching", true)]
		[TestCase("other-association", false)]
		[TestCase("other-target", false)]
		[TestCase("other-collection", false)]
		[TestCase("other-old-revision", false)]
		[TestCase("same-revision", false)]
		[TestCase("other-operation", false)]
		[TestCase("terminal", false)]
		public void PendingRevisionPresentation_RequiresExactReviewedScope(string scope, bool expected)
		{
			CollectionRevisionIdentity oldRevision = CreateNexusRevision("collection-a", "revision-6", 6);
			CollectionRevisionIdentity candidate = scope == "other-collection" ? CreateNexusRevision("collection-b", "revision-5", 5)
				: scope == "same-revision" ? oldRevision : CreateNexusRevision("collection-a", "revision-5", 5);
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-ui-pending");
			var association = new CollectionTargetAssociation(Guid.NewGuid(), oldRevision, target, CollectionAssociationState.Applied);
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(),
				scope == "other-operation" ? CollectionOperationKind.ApplyResolvedPlan : CollectionOperationKind.UpdateRevision,
				candidate.Collection, scope == "other-target" ? CollectionTargetIdentity.FromFingerprint("other-target") : target, candidate,
				CollectionPlanIdentity.From(Guid.NewGuid(), 1), 50,
				scope == "terminal" ? CollectionOperationPhase.Completed : CollectionOperationPhase.ObsoleteRevisionEffectsVerified,
				scope == "terminal" ? CollectionOperationResultState.Committed : CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
			Guid reviewedId = scope == "other-association" ? Guid.NewGuid() : association.AssociationId;
			CollectionRevisionIdentity reviewedOld = scope == "other-old-revision" ? CreateNexusRevision("collection-a", "revision-4", 4) : oldRevision;

			Assert.That(CollectionManagementApplicationService.MatchesPendingRevision(association, operation, reviewedId, reviewedOld), Is.EqualTo(expected));
			Assert.That(association.State, Is.EqualTo(CollectionAssociationState.Applied));
			Assert.That(association.Revision, Is.SameAs(oldRevision));
		}

		private static CollectionRevisionIdentity CreateNexusRevision(string collectionId, string revisionId, long revisionNumber)
		{
			return CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus(collectionId), revisionId, revisionNumber);
		}

		private static CollectionRevisionIdentity CreateLocalRevision()
		{
			return CollectionRevisionIdentity.FromLocal(CollectionIdentity.FromLocal(Guid.Parse("99999999-8888-7777-6666-555555555555")),
				Guid.Parse("44444444-3333-2222-1111-000000000000"));
		}
	}
}
