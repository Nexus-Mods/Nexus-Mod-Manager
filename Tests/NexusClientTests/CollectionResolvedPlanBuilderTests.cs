using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.15.4 production additive resolved-plan construction and pre-review journaling coverage.
	/// </summary>
	[TestFixture]
	public class CollectionResolvedPlanBuilderTests
	{
		[Test]
		public void Build_CreatesExactAdditivePlanAndDurablePreparingOperation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "resolved-plan", true, true);
				CollectionNativeStateIndex state = CreateState(fixture.Target);
				CollectionResolvedPlanBuilder builder = CreateBuilder(fixture);

				CollectionAdditivePlanBuildResult result = builder.Build(fixture.EffectiveSelection, fixture.Target, state);

				Assert.That(result.Operation.Phase, Is.EqualTo(CollectionOperationPhase.Preparing));
				Assert.That(result.Operation.Revision, Is.EqualTo(fixture.Revision.Identity));
				Assert.That(result.Operation.PlanIdentity, Is.Null,
					"C6.15.4 creates a plan snapshot but C6.5 persists/attaches it only at the ready-review boundary.");
				Assert.That(result.Plan.Identity.Version, Is.EqualTo(1));
				Assert.That(result.Plan.Target, Is.EqualTo(fixture.Target));
				Assert.That(result.Plan.Policy.Kind, Is.EqualTo(CollectionExecutionPolicyKind.InstallIntoCurrentSetup));
				Assert.That(result.Plan.CurrentStateFingerprint, Is.EqualTo(state.Fingerprint));
				Assert.That(result.NativeState, Is.SameAs(state));
				Assert.That(result.Plan.SelectedMembers.Count, Is.EqualTo(2));
				Assert.That(result.Plan.SelectedMembers.All(x => x.ArtifactChoice.Kind == CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact), Is.True);
				Assert.That(result.Plan.SelectedMembers.All(x => x.ArtifactChoice.RequestedArtifact.Equals(x.ArtifactChoice.SelectedArtifact)), Is.True);

				CollectionOperation durable = new CollectionsOperationStore(fixture.Store).GetOperation(result.Operation.Identity);
				Assert.That(durable, Is.Not.Null);
				Assert.That(durable.Phase, Is.EqualTo(CollectionOperationPhase.Preparing));
				Assert.That(durable.Revision, Is.EqualTo(fixture.Revision.Identity));
				Assert.That(new CollectionsResolvedPlanStore(fixture.Store).GetPlan(result.Plan.Identity), Is.Null,
					"C6.15.4 must not persist a reviewed-plan payload before C6.3/C6.4 review exists.");
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Build_MissingRetainedRevisionSourceFailsBeforeOperationJournalWrite()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "missing-source", false, true);
				CollectionResolvedPlanBuilder builder = CreateBuilder(fixture);

				Assert.Throws<FileNotFoundException>(() => builder.Build(fixture.EffectiveSelection,
					fixture.Target, CreateState(fixture.Target)));
				Assert.That(new CollectionsOperationStore(fixture.Store).GetIncompleteOperations(), Is.Empty);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Build_ActionRequiredSelectionFailsBeforeOperationJournalWrite()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "action-required", true, false);
				CollectionResolvedPlanBuilder builder = CreateBuilder(fixture);

				Assert.That(fixture.EffectiveSelection.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.ActionRequired));
				Assert.Throws<InvalidOperationException>(() => builder.Build(fixture.EffectiveSelection,
					fixture.Target, CreateState(fixture.Target)));
				Assert.That(new CollectionsOperationStore(fixture.Store).GetIncompleteOperations(), Is.Empty);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Build_RejectsStateCapturedForDifferentTargetBeforeOperationJournalWrite()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "wrong-target", true, true);
				CollectionTargetIdentity otherTarget = CanonicalTarget('b');
				CollectionResolvedPlanBuilder builder = CreateBuilder(fixture);

				Assert.Throws<ArgumentException>(() => builder.Build(fixture.EffectiveSelection,
					fixture.Target, CreateState(otherTarget)));
				Assert.That(new CollectionsOperationStore(fixture.Store).GetIncompleteOperations(), Is.Empty);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Build_RejectsNonCanonicalTargetBeforeOperationJournalWrite()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "noncanonical-target", true, true);
				CollectionTargetIdentity noncanonical = CollectionTargetIdentity.FromFingerprint("test-target");
				CollectionResolvedPlanBuilder builder = CreateBuilder(fixture);

				Assert.Throws<ArgumentException>(() => builder.Build(fixture.EffectiveSelection,
					noncanonical, CreateState(noncanonical)));
				Assert.That(new CollectionsOperationStore(fixture.Store).GetIncompleteOperations(), Is.Empty);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionResolvedPlanBuilder CreateBuilder(Fixture fixture)
		{
			var operationStore = new CollectionsOperationStore(fixture.Store);
			var coordinator = new CollectionOperationCoordinator(operationStore, new CollectionsResolvedPlanStore(fixture.Store));
			return new CollectionResolvedPlanBuilder(new CollectionsCatalogStore(fixture.Store),
				new CollectionsRevisionSourceStore(fixture.Store), coordinator);
		}

		private static Fixture CreateFixture(string root, string suffix, bool retainSource, bool supportedSelection)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			CollectionIdentity collection = CollectionIdentity.FromNexus("c6154-" + suffix);
			var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "revision-one", 1),
				"Revision 1", null, 2);
			var catalog = new CollectionsCatalogStore(store);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, suffix, null, null), revision);

			byte[] sourceBytes = Encoding.UTF8.GetBytes("{\"revision\":\"" + suffix + "\"}");
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(ComputeHash(sourceBytes),
				sourceBytes.LongLength, "test.collection.schema/1", "test-normalizer/1");
			NormalizedCollectionMember required = CreateMember(0, "required", CollectionMemberRequirement.Required,
				CollectionMemberSelection.Selected);
			NormalizedCollectionMember optional = CreateMember(1, "optional", CollectionMemberRequirement.Optional,
				CollectionMemberSelection.Unselected);
			var manifest = new NormalizedCollectionManifest(revision.Identity, source,
				CollectionManifestMemberSetCompleteness.Complete, null, new[] { required, optional });

			IEnumerable<CollectionCapabilityIssue> declaredIssues = supportedSelection
				? null
				: new[]
				{
					CollectionCapabilityIssue.ForMember(CollectionCompatibilityStatus.ActionRequired,
						"member.source-policy-needs-resolution", "The selected source policy is not concrete.", required, "source.updatePolicy")
				};
			CollectionCapabilityReport normalizedReport = CollectionCapabilityReport.Create(manifest, declaredIssues);
			CollectionEffectiveSelection effectiveSelection = new CollectionEffectiveSelectionBuilder().Build(normalizedReport,
				supportedSelection
					? new[] { new CollectionOptionalMemberSelection(optional.IdentityResolution.Key, CollectionMemberSelection.Selected) }
					: new CollectionOptionalMemberSelection[0]);

			if (retainSource)
			{
				new CollectionsRevisionSourceStore(store).RetainManifest(manifest, CollectionRevisionSourceInputKind.RawManifest,
					source.ContentHash, sourceBytes.LongLength, "collection.json", sourceBytes);
			}

			return new Fixture(store, revision, effectiveSelection, CanonicalTarget('a'));
		}

		private static NormalizedCollectionMember CreateMember(int ordinal, string key,
			CollectionMemberRequirement requirement, CollectionMemberSelection selection)
		{
			return new NormalizedCollectionMember(ordinal,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(key)), requirement, selection,
				new CollectionArtifactReference("nexus-mod-file", "skyrimse/" + (100 + ordinal) + "/" + (200 + ordinal), null),
				CollectionRecipeIdentity.FromFingerprint("recipe-" + key), key, ordinal);
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0],
				new CollectionNativeModState[0], new CollectionNativeFileState[0], new CollectionNativeIniState[0],
				new CollectionNativeGameValueState[0], new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
		}

		private static CollectionTargetIdentity CanonicalTarget(char value)
		{
			return CollectionTargetIdentity.FromFingerprint("target-sha256:" + new string(value, 64));
		}

		private static CollectionContentHash ComputeHash(byte[] bytes)
		{
			using (SHA256 sha256 = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", String.Empty).ToLowerInvariant());
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c6-15-4-plan-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private sealed class Fixture
		{
			public Fixture(CollectionsStore store, CollectionRevision revision,
				CollectionEffectiveSelection effectiveSelection, CollectionTargetIdentity target)
			{
				Store = store;
				Revision = revision;
				EffectiveSelection = effectiveSelection;
				Target = target;
			}

			public CollectionsStore Store { get; }
			public CollectionRevision Revision { get; }
			public CollectionEffectiveSelection EffectiveSelection { get; }
			public CollectionTargetIdentity Target { get; }
		}
	}
}
