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

		/// <summary>Decodes only the canonical member-enabled state representation owned by this adapter.</summary>
		internal static bool TryGetEnabled(CollectionRequirementState state, out bool enabled)
		{
			enabled = false;
			if (state == null || state.Kind != CollectionRequirementStateKind.Present ||
				!StringComparer.Ordinal.Equals(state.FormatVersion, EnabledFormat))
				return false;
			if (StringComparer.Ordinal.Equals(state.Fingerprint, "enabled"))
			{
				enabled = true;
				return true;
			}
			if (StringComparer.Ordinal.Equals(state.Fingerprint, "disabled"))
				return true;
			return false;
		}

		internal static bool IsIgnorableMemberDifference(CollectionRequirementAspect aspect)
		{
			return aspect == CollectionRequirementAspect.MemberParticipation ||
				aspect == CollectionRequirementAspect.MemberEnabledState;
		}
	}
}
