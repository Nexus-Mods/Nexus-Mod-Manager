using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes one collection independently from any particular revision or installed target association.
	/// </summary>
	/// <remarks>
	/// Identity is authoritative. Display metadata may be refreshed or edited without changing the collection lineage.
	/// The definition intentionally contains no live installation state and owns no native mod effects.
	/// </remarks>
	public sealed class CollectionDefinition : IEquatable<CollectionDefinition>
	{
		/// <summary>
		/// Creates a collection definition.
		/// </summary>
		/// <param name="identity">The stable collection identity.</param>
		/// <param name="displayName">The optional user-facing collection name.</param>
		/// <param name="authorDisplayName">The optional user-facing author/curator name.</param>
		/// <param name="summary">Optional descriptive text. The text is preserved verbatim.</param>
		public CollectionDefinition(CollectionIdentity identity, string displayName, string authorDisplayName, string summary)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));

			Identity = identity;
			DisplayName = CollectionDomainValidation.OptionalDisplayValue(displayName, nameof(displayName));
			AuthorDisplayName = CollectionDomainValidation.OptionalDisplayValue(authorDisplayName, nameof(authorDisplayName));
			Summary = summary;
		}

		/// <summary>
		/// Gets the stable collection identity.
		/// </summary>
		public CollectionIdentity Identity { get; }

		/// <summary>
		/// Gets the collection origin from its stable identity.
		/// </summary>
		public CollectionOrigin Origin
		{
			get { return Identity.Origin; }
		}

		/// <summary>
		/// Gets the optional user-facing collection name.
		/// </summary>
		public string DisplayName { get; }

		/// <summary>
		/// Gets the optional user-facing author or curator name.
		/// </summary>
		public string AuthorDisplayName { get; }

		/// <summary>
		/// Gets optional descriptive text exactly as supplied by the owning provider or local editor.
		/// </summary>
		public string Summary { get; }

		/// <inheritdoc />
		public bool Equals(CollectionDefinition other)
		{
			return !ReferenceEquals(other, null) && Equals(Identity, other.Identity);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionDefinition);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return Identity.GetHashCode();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return string.IsNullOrEmpty(DisplayName) ? Identity.ToString() : DisplayName + " (" + Identity + ")";
		}
	}
}
