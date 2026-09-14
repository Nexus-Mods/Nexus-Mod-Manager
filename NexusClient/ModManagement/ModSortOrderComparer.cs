namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Provides the shared nullable numeric comparison contract for mod Sort values.
	/// </summary>
	public static class ModSortOrderComparer
	{
		/// <summary>
		/// Compares two Sort values while keeping blank values last in both directions.
		/// </summary>
		public static int Compare(int? left, int? right, bool descending)
		{
			if (left.HasValue != right.HasValue)
			{
				return left.HasValue ? -1 : 1;
			}

			if (!left.HasValue)
			{
				return 0;
			}

			var comparison = left.Value.CompareTo(right.Value);
			return descending ? -comparison : comparison;
		}
	}
}
