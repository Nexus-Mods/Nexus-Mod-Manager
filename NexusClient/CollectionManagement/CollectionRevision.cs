using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Represents one immutable remote baseline or one sealed Local Collection revision.
	/// </summary>
	/// <remarks>
	/// This model intentionally contains only revision-level metadata. Normalized members, recipe decisions, raw bundle
	/// retention and execution policy are introduced by later C1 patches and must not be inferred from display metadata.
	/// </remarks>
	public sealed class CollectionRevision : IEquatable<CollectionRevision>
	{
		/// <summary>
		/// Creates a collection revision metadata snapshot.
		/// </summary>
		/// <param name="identity">The exact collection revision identity.</param>
		/// <param name="revisionLabel">An optional user-facing revision label.</param>
		/// <param name="notes">Optional curator/local revision notes preserved verbatim.</param>
		/// <param name="declaredMemberCount">An optional provider/local declared member count.</param>
		public CollectionRevision(CollectionRevisionIdentity identity, string revisionLabel, string notes, int? declaredMemberCount)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));
			if (declaredMemberCount.HasValue && declaredMemberCount.Value < 0)
				throw new ArgumentOutOfRangeException(nameof(declaredMemberCount), "A declared member count cannot be negative.");

			Identity = identity;
			RevisionLabel = CollectionDomainValidation.OptionalDisplayValue(revisionLabel, nameof(revisionLabel));
			Notes = notes;
			DeclaredMemberCount = declaredMemberCount;
		}

		/// <summary>
		/// Gets the exact immutable revision identity.
		/// </summary>
		public CollectionRevisionIdentity Identity { get; }

		/// <summary>
		/// Gets the owning collection lineage.
		/// </summary>
		public CollectionIdentity Collection
		{
			get { return Identity.Collection; }
		}

		/// <summary>
		/// Gets whether this revision is an immutable Nexus remote baseline.
		/// </summary>
		public bool IsRemoteBaseline
		{
			get { return Collection.Origin == CollectionOrigin.NexusMods; }
		}

		/// <summary>
		/// Gets whether this revision is an NMM-owned sealed Local Collection revision.
		/// </summary>
		public bool IsSealedLocalRevision
		{
			get { return Collection.Origin == CollectionOrigin.Local; }
		}

		/// <summary>
		/// Gets an optional display label for the exact revision.
		/// </summary>
		public string RevisionLabel { get; }

		/// <summary>
		/// Gets optional curator/local notes exactly as supplied.
		/// </summary>
		public string Notes { get; }

		/// <summary>
		/// Gets the optional member count declared by provider/local metadata.
		/// </summary>
		/// <remarks>
		/// This is descriptive metadata, not proof that a later normalized manifest is complete.
		/// </remarks>
		public int? DeclaredMemberCount { get; }

		/// <inheritdoc />
		public bool Equals(CollectionRevision other)
		{
			return !ReferenceEquals(other, null) && Equals(Identity, other.Identity);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionRevision);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return Identity.GetHashCode();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return string.IsNullOrEmpty(RevisionLabel) ? Identity.ToString() : RevisionLabel + " (" + Identity + ")";
		}
	}
}
