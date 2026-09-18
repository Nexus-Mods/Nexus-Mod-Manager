using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Nexus.Client.CollectionManagement;

namespace Nexus.Client.Tests
{
	[TestFixture]
	public class CollectionOverrideDriftContractsTests
	{
		[Test]
		public void RequirementReference_PinsExactAssociationRevisionTargetAndMember()
		{
			CollectionTargetAssociation association = CreateAssociation("2210", "772530", 100, "target-A");
			CollectionMemberKey member = CollectionMemberKey.FromProvider("member-1");
			CollectionRequirementReference requirement = new CollectionRequirementReference(
				association, member, CollectionRequirementAspect.ArtifactSelection, null);

			Assert.That(requirement.AssociationId, Is.EqualTo(association.AssociationId));
			Assert.That(requirement.BaselineRevision, Is.SameAs(association.Revision));
			Assert.That(requirement.Target, Is.SameAs(association.Target));
			Assert.That(requirement.MemberKey, Is.SameAs(member));
			Assert.That(requirement.Aspect, Is.EqualTo(CollectionRequirementAspect.ArtifactSelection));
			Assert.That(requirement.SubjectKey, Is.Null);
		}

		[Test]
		public void RequirementReference_RequiresMemberForMemberScopedAspects()
		{
			CollectionTargetAssociation association = CreateAssociation("2210", "772530", 100, "target-A");

			Assert.Throws<ArgumentNullException>(() => new CollectionRequirementReference(
				association, null, CollectionRequirementAspect.MemberParticipation, null));
			Assert.Throws<ArgumentNullException>(() => new CollectionRequirementReference(
				association, null, CollectionRequirementAspect.MemberEnabledState, null));
			Assert.Throws<ArgumentNullException>(() => new CollectionRequirementReference(
				association, null, CollectionRequirementAspect.ArtifactSelection, null));
			Assert.Throws<ArgumentNullException>(() => new CollectionRequirementReference(
				association, null, CollectionRequirementAspect.InstallerRecipe, null));
			Assert.Throws<ArgumentException>(() => new CollectionRequirementReference(
				association, CollectionMemberKey.FromProvider("member-1"), CollectionRequirementAspect.ArtifactSelection, "extra"));
		}

		[Test]
		public void RequirementReference_TargetScopedRequirementsUseStableSubjectIdentity()
		{
			CollectionTargetAssociation association = CreateAssociation("2210", "772530", 100, "target-A");
			CollectionRequirementReference fileWinner = new CollectionRequirementReference(
				association, CollectionMemberKey.FromProvider("member-1"), CollectionRequirementAspect.FileWinner,
				"Data/textures/example.dds");
			CollectionRequirementReference plugin = new CollectionRequirementReference(
				association, null, CollectionRequirementAspect.PluginState, "Example.esp");
			CollectionRequirementReference extra = new CollectionRequirementReference(
				association, null, CollectionRequirementAspect.AdditionalManagedContent, "native-mod:42");

			Assert.That(fileWinner.SubjectKey, Is.EqualTo("Data/textures/example.dds"));
			Assert.That(plugin.SubjectKey, Is.EqualTo("Example.esp"));
			Assert.That(extra.MemberKey, Is.Null);
			Assert.Throws<ArgumentException>(() => new CollectionRequirementReference(
				association, null, CollectionRequirementAspect.PluginState, " "));
			Assert.Throws<ArgumentException>(() => new CollectionRequirementReference(
				association, CollectionMemberKey.FromProvider("member-1"), CollectionRequirementAspect.AdditionalManagedContent,
				"native-mod:42"));
			Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionRequirementReference(
				association, null, CollectionRequirementAspect.Unknown, null));
		}

		[Test]
		public void RequirementReference_EqualityIncludesExactBaselineAndSubject()
		{
			Guid associationId = Guid.NewGuid();
			CollectionTargetAssociation firstAssociation = CreateAssociation(associationId, "2210", "772530", 100, "target-A");
			CollectionTargetAssociation sameAssociation = CreateAssociation(associationId, "2210", "772530", 100, "target-A");
			CollectionTargetAssociation differentRevision = CreateAssociation(associationId, "2210", "772531", 101, "target-A");

			CollectionRequirementReference first = new CollectionRequirementReference(
				firstAssociation, null, CollectionRequirementAspect.PluginState, "Example.esp");
			CollectionRequirementReference same = new CollectionRequirementReference(
				sameAssociation, null, CollectionRequirementAspect.PluginState, "Example.esp");
			CollectionRequirementReference newer = new CollectionRequirementReference(
				differentRevision, null, CollectionRequirementAspect.PluginState, "Example.esp");
			CollectionRequirementReference otherSubject = new CollectionRequirementReference(
				firstAssociation, null, CollectionRequirementAspect.PluginState, "Other.esp");

			Assert.That(first, Is.EqualTo(same));
			Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
			Assert.That(first, Is.Not.EqualTo(newer));
			Assert.That(first, Is.Not.EqualTo(otherSubject));
		}

		[Test]
		public void RequirementState_DistinguishesAbsentFromExactVersionedPresentValue()
		{
			CollectionRequirementState absent = CollectionRequirementState.Absent();
			CollectionRequirementState first = CollectionRequirementState.Present("recipe-v1", "sha256:ABC");
			CollectionRequirementState same = CollectionRequirementState.Present("recipe-v1", "sha256:ABC");
			CollectionRequirementState differentVersion = CollectionRequirementState.Present("recipe-v2", "sha256:ABC");

			Assert.That(absent.Kind, Is.EqualTo(CollectionRequirementStateKind.Absent));
			Assert.That(absent.FormatVersion, Is.Null);
			Assert.That(first.Kind, Is.EqualTo(CollectionRequirementStateKind.Present));
			Assert.That(first, Is.EqualTo(same));
			Assert.That(first, Is.Not.EqualTo(absent));
			Assert.That(first, Is.Not.EqualTo(differentVersion));
			Assert.Throws<ArgumentException>(() => CollectionRequirementState.Present(" ", "sha256:ABC"));
			Assert.Throws<ArgumentException>(() => CollectionRequirementState.Present("recipe-v1", " value "));
		}

		[Test]
		public void UserOverride_RecordsDeliberateDifferenceWithoutAuthorizingSilentRepair()
		{
			CollectionRequirementReference requirement = CreateMemberRequirement(CollectionRequirementAspect.MemberEnabledState);
			CollectionRequirementState baseline = CollectionRequirementState.Present("bool-v1", "enabled");
			CollectionRequirementState chosen = CollectionRequirementState.Present("bool-v1", "disabled");
			UserOverride userOverride = new UserOverride(Guid.NewGuid(), requirement, baseline, chosen,
				"Keep this member disabled for this setup");

			Assert.That(userOverride.Requirement, Is.SameAs(requirement));
			Assert.That(userOverride.BaselineState, Is.SameAs(baseline));
			Assert.That(userOverride.UserChosenState, Is.SameAs(chosen));
			Assert.That(userOverride.RequiresExplicitRepairDecision, Is.True);
			Assert.That(userOverride.Note, Is.EqualTo("Keep this member disabled for this setup"));
		}

		[Test]
		public void UserOverride_RejectsMissingOrNonDifferingDecision()
		{
			CollectionRequirementReference requirement = CreateMemberRequirement(CollectionRequirementAspect.InstallerRecipe);
			CollectionRequirementState baseline = CollectionRequirementState.Present("recipe-v1", "aaa");

			Assert.Throws<ArgumentException>(() => new UserOverride(Guid.Empty, requirement, baseline,
				CollectionRequirementState.Present("recipe-v1", "bbb"), null));
			Assert.Throws<ArgumentNullException>(() => new UserOverride(Guid.NewGuid(), null, baseline,
				CollectionRequirementState.Present("recipe-v1", "bbb"), null));
			Assert.Throws<ArgumentException>(() => new UserOverride(Guid.NewGuid(), requirement, baseline,
				CollectionRequirementState.Present("recipe-v1", "aaa"), null));
			Assert.Throws<ArgumentException>(() => new UserOverride(Guid.NewGuid(), requirement, baseline,
				CollectionRequirementState.Present("recipe-v1", "bbb"), " note "));
		}

		[Test]
		public void DriftObservation_IsNotUserIntentAndCannotRepresentNoChange()
		{
			CollectionRequirementReference requirement = CreateMemberRequirement(CollectionRequirementAspect.ArtifactSelection);
			CollectionRequirementState expected = CollectionRequirementState.Present("artifact-v1", "nexus-file:10");
			CollectionRequirementState observed = CollectionRequirementState.Present("artifact-v1", "nexus-file:11");
			CollectionDriftObservation drift = new CollectionDriftObservation(Guid.NewGuid(), requirement,
				expected, observed, "Ordinary mod update changed the pinned file");

			Assert.That(drift.ExpectedState, Is.SameAs(expected));
			Assert.That(drift.ObservedState, Is.SameAs(observed));
			Assert.That(drift.IsUserIntent, Is.False);
			Assert.That(drift.RequiresRepairPreview, Is.True);
			Assert.Throws<ArgumentException>(() => new CollectionDriftObservation(Guid.NewGuid(), requirement,
				expected, CollectionRequirementState.Present("artifact-v1", "nexus-file:10"), null));
		}

		[Test]
		public void CustomizationSnapshot_KeepsDeliberateOverridesAndDetectedDriftSeparate()
		{
			CollectionTargetAssociation association = CreateAssociation("2210", "772530", 100, "target-A");
			CollectionRequirementReference overrideRequirement = new CollectionRequirementReference(association,
				CollectionMemberKey.FromProvider("member-1"), CollectionRequirementAspect.MemberEnabledState, null);
			CollectionRequirementReference driftRequirement = new CollectionRequirementReference(association,
				null, CollectionRequirementAspect.PluginState, "Example.esp");
			UserOverride userOverride = new UserOverride(Guid.NewGuid(), overrideRequirement,
				CollectionRequirementState.Present("bool-v1", "enabled"),
				CollectionRequirementState.Present("bool-v1", "disabled"), null);
			CollectionDriftObservation drift = new CollectionDriftObservation(Guid.NewGuid(), driftRequirement,
				CollectionRequirementState.Present("plugin-v1", "enabled:index=4"),
				CollectionRequirementState.Present("plugin-v1", "disabled"), null);

			CollectionAssociationCustomization snapshot = new CollectionAssociationCustomization(association,
				new[] { userOverride }, new[] { drift });

			Assert.That(snapshot.HasUserOverrides, Is.True);
			Assert.That(snapshot.HasDetectedDrift, Is.True);
			Assert.That(snapshot.RequiresReview, Is.True);
			Assert.That(snapshot.UserOverrides.Single(), Is.SameAs(userOverride));
			Assert.That(snapshot.DriftObservations.Single(), Is.SameAs(drift));
		}

		[Test]
		public void CustomizationSnapshot_DriftAfterOverrideMustUseChosenStateAsExpectation()
		{
			CollectionTargetAssociation association = CreateAssociation("2210", "772530", 100, "target-A");
			CollectionRequirementReference requirement = new CollectionRequirementReference(association,
				CollectionMemberKey.FromProvider("member-1"), CollectionRequirementAspect.MemberEnabledState, null);
			CollectionRequirementState baseline = CollectionRequirementState.Present("bool-v1", "enabled");
			CollectionRequirementState chosen = CollectionRequirementState.Present("bool-v1", "disabled");
			UserOverride userOverride = new UserOverride(Guid.NewGuid(), requirement, baseline, chosen, null);
			CollectionDriftObservation validDrift = new CollectionDriftObservation(Guid.NewGuid(), requirement, chosen,
				CollectionRequirementState.Absent(), null);
			CollectionDriftObservation invalidDrift = new CollectionDriftObservation(Guid.NewGuid(), requirement, baseline,
				CollectionRequirementState.Absent(), null);

			Assert.DoesNotThrow(() => new CollectionAssociationCustomization(association,
				new[] { userOverride }, new[] { validDrift }));
			Assert.Throws<ArgumentException>(() => new CollectionAssociationCustomization(association,
				new[] { userOverride }, new[] { invalidDrift }));
		}

		[Test]
		public void CustomizationSnapshot_RejectsDuplicateRequirementsAndCrossAssociationRecords()
		{
			CollectionTargetAssociation association = CreateAssociation("2210", "772530", 100, "target-A");
			CollectionRequirementReference requirement = new CollectionRequirementReference(association,
				CollectionMemberKey.FromProvider("member-1"), CollectionRequirementAspect.ArtifactSelection, null);
			UserOverride first = new UserOverride(Guid.NewGuid(), requirement,
				CollectionRequirementState.Present("artifact-v1", "file:10"),
				CollectionRequirementState.Present("artifact-v1", "file:11"), null);
			UserOverride second = new UserOverride(Guid.NewGuid(), requirement,
				CollectionRequirementState.Present("artifact-v1", "file:10"),
				CollectionRequirementState.Present("artifact-v1", "file:12"), null);

			Assert.Throws<ArgumentException>(() => new CollectionAssociationCustomization(association,
				new[] { first, second }, new CollectionDriftObservation[0]));

			CollectionTargetAssociation other = CreateAssociation("2211", "800000", 7, "target-A");
			CollectionRequirementReference otherRequirement = new CollectionRequirementReference(other,
				CollectionMemberKey.FromProvider("member-1"), CollectionRequirementAspect.ArtifactSelection, null);
			UserOverride crossAssociation = new UserOverride(Guid.NewGuid(), otherRequirement,
				CollectionRequirementState.Present("artifact-v1", "file:10"),
				CollectionRequirementState.Present("artifact-v1", "file:11"), null);

			Assert.Throws<ArgumentException>(() => new CollectionAssociationCustomization(association,
				new[] { crossAssociation }, new CollectionDriftObservation[0]));
		}

		[Test]
		public void CustomizationSnapshot_DefensivelyCopiesInputCollections()
		{
			CollectionTargetAssociation association = CreateAssociation("2210", "772530", 100, "target-A");
			CollectionRequirementReference requirement = new CollectionRequirementReference(association,
				CollectionMemberKey.FromProvider("member-1"), CollectionRequirementAspect.MemberParticipation, null);
			List<UserOverride> overrides = new List<UserOverride>
			{
				new UserOverride(Guid.NewGuid(), requirement,
					CollectionRequirementState.Present("participation-v1", "included"),
					CollectionRequirementState.Present("participation-v1", "ignored"), null)
			};
			List<CollectionDriftObservation> drift = new List<CollectionDriftObservation>();

			CollectionAssociationCustomization snapshot = new CollectionAssociationCustomization(association, overrides, drift);
			overrides.Clear();
			drift.Add(new CollectionDriftObservation(Guid.NewGuid(), requirement,
				CollectionRequirementState.Present("participation-v1", "included"), CollectionRequirementState.Absent(), null));

			Assert.That(snapshot.UserOverrides.Count, Is.EqualTo(1));
			Assert.That(snapshot.DriftObservations.Count, Is.EqualTo(0));
		}

		[Test]
		public void EmptyCustomizationSnapshot_RequiresNoReview()
		{
			CollectionTargetAssociation association = CreateAssociation("2210", "772530", 100, "target-A");
			CollectionAssociationCustomization snapshot = new CollectionAssociationCustomization(association,
				new UserOverride[0], new CollectionDriftObservation[0]);

			Assert.That(snapshot.HasUserOverrides, Is.False);
			Assert.That(snapshot.HasDetectedDrift, Is.False);
			Assert.That(snapshot.RequiresReview, Is.False);
		}

		[Test]
		public void OverrideDriftContractsExposeNoPublicPropertySetters()
		{
			AssertNoPublicSetters(typeof(CollectionRequirementReference));
			AssertNoPublicSetters(typeof(CollectionRequirementState));
			AssertNoPublicSetters(typeof(UserOverride));
			AssertNoPublicSetters(typeof(CollectionDriftObservation));
			AssertNoPublicSetters(typeof(CollectionAssociationCustomization));
		}

		private static CollectionRequirementReference CreateMemberRequirement(CollectionRequirementAspect aspect)
		{
			return new CollectionRequirementReference(CreateAssociation("2210", "772530", 100, "target-A"),
				CollectionMemberKey.FromProvider("member-1"), aspect, null);
		}

		private static CollectionTargetAssociation CreateAssociation(string collectionId, string revisionId,
			long revisionNumber, string target)
		{
			return CreateAssociation(Guid.NewGuid(), collectionId, revisionId, revisionNumber, target);
		}

		private static CollectionTargetAssociation CreateAssociation(Guid associationId, string collectionId,
			string revisionId, long revisionNumber, string target)
		{
			return new CollectionTargetAssociation(associationId,
				CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus(collectionId), revisionId, revisionNumber),
				CollectionTargetIdentity.FromFingerprint(target), CollectionAssociationState.Applied);
		}

		private static void AssertNoPublicSetters(Type type)
		{
			foreach (var property in type.GetProperties())
				Assert.That(property.GetSetMethod(), Is.Null, type.Name + "." + property.Name + " must remain immutable.");
		}
	}
}
