using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace Nexus.Client.Tests
{
	[TestFixture]
	[Category("CollectionsGateA")]
	[Category("CollectionsManagement")]
	public class CollectionMemberRequirementStatesTests
	{
		[Test]
		public void MemberParticipationState_UsesSameDurableEncodingAsManualDriftTracking()
		{
			CollectionRequirementState included = CollectionMemberRequirementStates.Included();

			Assert.That(included.Kind, Is.EqualTo(CollectionRequirementStateKind.Present));
			Assert.That(included.FormatVersion, Is.EqualTo("participation-v1"));
			Assert.That(included.Fingerprint, Is.EqualTo("included"));
		}

		[TestCase(true, "enabled")]
		[TestCase(false, "disabled")]
		public void MemberEnabledState_UsesCanonicalBooleanEncoding(bool enabled, string expected)
		{
			CollectionRequirementState state = CollectionMemberRequirementStates.Enabled(enabled);

			Assert.That(state.Kind, Is.EqualTo(CollectionRequirementStateKind.Present));
			Assert.That(state.FormatVersion, Is.EqualTo("bool-v1"));
			Assert.That(state.Fingerprint, Is.EqualTo(expected));
		}

		[TestCase(CollectionRequirementAspect.MemberParticipation, true)]
		[TestCase(CollectionRequirementAspect.MemberEnabledState, true)]
		[TestCase(CollectionRequirementAspect.ArtifactSelection, false)]
		[TestCase(CollectionRequirementAspect.FileWinner, false)]
		public void IgnoreAction_IsLimitedToMemberPresenceAndEnabledDifferences(CollectionRequirementAspect aspect, bool expected)
		{
			Assert.That(CollectionMemberRequirementStates.IsIgnorableMemberDifference(aspect), Is.EqualTo(expected));
		}
	}
}
