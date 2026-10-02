using System;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>
	/// User-facing filters for the Collection member list. Filtering is presentation-only and must never change
	/// the effective optional-member selection or installed Collection intent.
	/// </summary>
	internal enum CollectionMemberListFilterKind
	{
		All = 0,
		Selected = 1,
		Unselected = 2,
		Required = 3,
		Optional = 4,
		NeedsAttention = 5,
		ChangedLocally = 6
	}

	/// <summary>
	/// Pure C9 member-list filtering used by the WinForms surface and unit tests.
	/// </summary>
	internal static class CollectionMemberListPresentationFilter
	{
		internal static bool Matches(CollectionMemberListFilterKind filter, string searchText,
			string displayName, string memberToken, string artifactText, string nativeModKey, string managedStateText,
			bool isRequired, bool isSelected, CollectionCompatibilityStatus compatibility,
			bool hasDetectedDrift, bool hasExplicitLocalDecision)
		{
			if (!Enum.IsDefined(typeof(CollectionMemberListFilterKind), filter))
				throw new ArgumentOutOfRangeException(nameof(filter));

			bool matchesKind;
			switch (filter)
			{
				case CollectionMemberListFilterKind.All:
					matchesKind = true;
					break;
				case CollectionMemberListFilterKind.Selected:
					matchesKind = isSelected;
					break;
				case CollectionMemberListFilterKind.Unselected:
					matchesKind = !isSelected;
					break;
				case CollectionMemberListFilterKind.Required:
					matchesKind = isRequired;
					break;
				case CollectionMemberListFilterKind.Optional:
					matchesKind = !isRequired;
					break;
				case CollectionMemberListFilterKind.NeedsAttention:
					matchesKind = compatibility != CollectionCompatibilityStatus.Supported || hasDetectedDrift;
					break;
				case CollectionMemberListFilterKind.ChangedLocally:
					matchesKind = hasDetectedDrift || hasExplicitLocalDecision;
					break;
				default:
					matchesKind = false;
					break;
			}

			if (!matchesKind)
				return false;

			string query = (searchText ?? String.Empty).Trim();
			if (query.Length == 0)
				return true;

			return Contains(displayName, query) || Contains(memberToken, query) || Contains(artifactText, query) ||
				Contains(nativeModKey, query) || Contains(managedStateText, query);
		}

		private static bool Contains(string value, string query)
		{
			return !String.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
		}
	}
}
