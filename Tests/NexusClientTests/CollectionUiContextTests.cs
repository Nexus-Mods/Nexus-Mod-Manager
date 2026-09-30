using System;
using NUnit.Framework;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.UI;

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
