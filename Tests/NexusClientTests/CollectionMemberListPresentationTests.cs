using NUnit.Framework;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.UI;

namespace Nexus.Client.Tests
{
	[TestFixture]
	public class CollectionMemberListPresentationTests
	{
		[Test]
		public void SelectedAndUnselectedFilters_UseEffectiveSelectionRatherThanSourceRequirement()
		{
			Assert.That(Matches(CollectionMemberListFilterKind.Selected, true, true, false, false,
				CollectionCompatibilityStatus.Supported), Is.True);
			Assert.That(Matches(CollectionMemberListFilterKind.Selected, false, false, false, false,
				CollectionCompatibilityStatus.Supported), Is.False);
			Assert.That(Matches(CollectionMemberListFilterKind.Unselected, false, false, false, false,
				CollectionCompatibilityStatus.Supported), Is.True);
		}

		[Test]
		public void RequiredAndOptionalFilters_RemainIndependentFromSelection()
		{
			Assert.That(Matches(CollectionMemberListFilterKind.Required, true, false, false, false,
				CollectionCompatibilityStatus.Supported), Is.True);
			Assert.That(Matches(CollectionMemberListFilterKind.Optional, false, true, false, false,
				CollectionCompatibilityStatus.Supported), Is.True);
		}

		[Test]
		public void NeedsAttention_IncludesCapabilityProblemsAndDetectedDrift()
		{
			Assert.That(Matches(CollectionMemberListFilterKind.NeedsAttention, false, true, false, false,
				CollectionCompatibilityStatus.ActionRequired), Is.True);
			Assert.That(Matches(CollectionMemberListFilterKind.NeedsAttention, false, true, true, false,
				CollectionCompatibilityStatus.Supported), Is.True);
			Assert.That(Matches(CollectionMemberListFilterKind.NeedsAttention, false, true, false, false,
				CollectionCompatibilityStatus.Supported), Is.False);
		}

		[Test]
		public void ChangedLocally_IncludesOverridesOrDriftOnly()
		{
			Assert.That(Matches(CollectionMemberListFilterKind.ChangedLocally, false, true, false, true,
				CollectionCompatibilityStatus.Supported), Is.True);
			Assert.That(Matches(CollectionMemberListFilterKind.ChangedLocally, false, true, true, false,
				CollectionCompatibilityStatus.Supported), Is.True);
			Assert.That(Matches(CollectionMemberListFilterKind.ChangedLocally, false, true, false, false,
				CollectionCompatibilityStatus.Unsupported), Is.False);
		}

		[Test]
		public void Search_MatchesDisplayIdentityArtifactNativeKeyAndManagedStateCaseInsensitively()
		{
			Assert.That(CollectionMemberListPresentationFilter.Matches(CollectionMemberListFilterKind.All, "texture",
				"HD Texture Pack", "member-1", "artifact-44", "native-7", "installed for Collection", false, true,
				CollectionCompatibilityStatus.Supported, false, false), Is.True);
			Assert.That(CollectionMemberListPresentationFilter.Matches(CollectionMemberListFilterKind.All, "MEMBER-1",
				"HD Texture Pack", "member-1", "artifact-44", "native-7", "installed for Collection", false, true,
				CollectionCompatibilityStatus.Supported, false, false), Is.True);
			Assert.That(CollectionMemberListPresentationFilter.Matches(CollectionMemberListFilterKind.All, "artifact-44",
				"HD Texture Pack", "member-1", "artifact-44", "native-7", "installed for Collection", false, true,
				CollectionCompatibilityStatus.Supported, false, false), Is.True);
			Assert.That(CollectionMemberListPresentationFilter.Matches(CollectionMemberListFilterKind.All, "NATIVE-7",
				"HD Texture Pack", "member-1", "artifact-44", "native-7", "installed for Collection", false, true,
				CollectionCompatibilityStatus.Supported, false, false), Is.True);
			Assert.That(CollectionMemberListPresentationFilter.Matches(CollectionMemberListFilterKind.All, "installed for",
				"HD Texture Pack", "member-1", "artifact-44", "native-7", "installed for Collection", false, true,
				CollectionCompatibilityStatus.Supported, false, false), Is.True);
			Assert.That(CollectionMemberListPresentationFilter.Matches(CollectionMemberListFilterKind.All, "missing",
				"HD Texture Pack", "member-1", "artifact-44", "native-7", "installed for Collection", false, true,
				CollectionCompatibilityStatus.Supported, false, false), Is.False);
		}

		private static bool Matches(CollectionMemberListFilterKind filter, bool isRequired, bool isSelected,
			bool hasDrift, bool hasOverride, CollectionCompatibilityStatus compatibility)
		{
			return CollectionMemberListPresentationFilter.Matches(filter, null, "Member", "member-1", "artifact-1", null, null,
				isRequired, isSelected, compatibility, hasDrift, hasOverride);
		}
	}
}
