using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one immutable resolved-plan snapshot and its explicit version.
	/// </summary>
	/// <remarks>
	/// The coordinator owns plan ID creation. Replanning after changed choices or prerequisites keeps the logical plan ID
	/// only when that relationship is deliberate and increments <see cref="Version"/>; this type never silently mutates a plan.
	/// </remarks>
	public sealed class CollectionPlanIdentity : IEquatable<CollectionPlanIdentity>
	{
		private CollectionPlanIdentity(Guid planId, int version)
		{
			if (planId == Guid.Empty)
				throw new ArgumentException("A non-empty plan identifier is required.", nameof(planId));
			if (version <= 0)
				throw new ArgumentOutOfRangeException(nameof(version), "Plan version must be greater than zero.");

			PlanId = planId;
			Version = version;
		}

		/// <summary>
		/// Gets the logical plan identifier.
		/// </summary>
		public Guid PlanId { get; }

		/// <summary>
		/// Gets the immutable snapshot version within <see cref="PlanId"/>.
		/// </summary>
		public int Version { get; }

		/// <summary>
		/// Creates an explicit plan identity.
		/// </summary>
		public static CollectionPlanIdentity From(Guid planId, int version)
		{
			return new CollectionPlanIdentity(planId, version);
		}

		/// <summary>
		/// Creates the next version of the same logical plan.
		/// </summary>
		public CollectionPlanIdentity NextVersion()
		{
			if (Version == Int32.MaxValue)
				throw new InvalidOperationException("The collection plan version cannot be incremented further.");

			return new CollectionPlanIdentity(PlanId, Version + 1);
		}

		/// <inheritdoc />
		public bool Equals(CollectionPlanIdentity other)
		{
			return !ReferenceEquals(other, null) && PlanId == other.PlanId && Version == other.Version;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionPlanIdentity);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return (PlanId.GetHashCode() * 397) ^ Version;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return PlanId.ToString("D") + "@" + Version;
		}
	}
}
