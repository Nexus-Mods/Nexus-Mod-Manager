using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Canonical state encodings for member-level participation/enabled decisions.</summary>
	internal static class CollectionMemberRequirementStates
	{
		internal const string ParticipationFormat = "participation-v1";
		internal const string EnabledFormat = "bool-v1";

		internal static CollectionRequirementState Included()
		{
			return CollectionRequirementState.Present(ParticipationFormat, "included");
		}

		internal static CollectionRequirementState Enabled(bool enabled)
		{
			return CollectionRequirementState.Present(EnabledFormat, enabled ? "enabled" : "disabled");
		}

		internal static bool IsIgnorableMemberDifference(CollectionRequirementAspect aspect)
		{
			return aspect == CollectionRequirementAspect.MemberParticipation ||
				aspect == CollectionRequirementAspect.MemberEnabledState;
		}
	}
}
