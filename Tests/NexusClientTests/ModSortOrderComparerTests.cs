namespace NexusClientTests
{
	using Nexus.Client.ModManagement;
	using NUnit.Framework;

	/// <summary>
	/// Verifies the shared nullable numeric Sort comparison contract used by both Mods frontends.
	/// </summary>
	public class ModSortOrderComparerTests
	{
		/// <summary>
		/// Verifies that numeric Sort values follow the requested ascending or descending direction.
		/// </summary>
		[TestCase(-1, 2, false, -1)]
		[TestCase(10, 2, false, 1)]
		[TestCase(10, 2, true, -1)]
		[TestCase(2, 10, true, 1)]
		[TestCase(2, 2, false, 0)]
		[TestCase(2, 2, true, 0)]
		public void NumericValuesFollowRequestedDirection(int left, int right, bool descending, int expectedSign)
		{
			Assert.That(System.Math.Sign(ModSortOrderComparer.Compare(left, right, descending)), Is.EqualTo(expectedSign));
		}

		/// <summary>
		/// Verifies that blank Sort values remain after numeric values in both directions.
		/// </summary>
		[TestCase(false)]
		[TestCase(true)]
		public void BlankAlwaysSortsAfterNumeric(bool descending)
		{
			Assert.That(ModSortOrderComparer.Compare(null, 0, descending), Is.GreaterThan(0));
			Assert.That(ModSortOrderComparer.Compare(0, null, descending), Is.LessThan(0));
			Assert.That(ModSortOrderComparer.Compare(null, null, descending), Is.EqualTo(0));
		}

		/// <summary>
		/// Verifies that the signed 32-bit extremes are ordinary numeric values rather than null sentinels.
		/// </summary>
		[Test]
		public void SignedIntExtremesRemainOrdinaryNumericValues()
		{
			Assert.That(ModSortOrderComparer.Compare(int.MinValue, int.MaxValue, false), Is.LessThan(0));
			Assert.That(ModSortOrderComparer.Compare(int.MinValue, int.MaxValue, true), Is.GreaterThan(0));
		}
	}
}
