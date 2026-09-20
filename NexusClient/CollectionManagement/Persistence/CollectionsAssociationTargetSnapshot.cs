using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Immutable target-scoped read of persisted Collection associations, bindings and deliberate overrides.
	/// </summary>
	public sealed class CollectionsAssociationTargetSnapshot
	{
		/// <summary>Creates one immutable target-scoped association read.</summary>
		public CollectionsAssociationTargetSnapshot(CollectionTargetIdentity target,
			IEnumerable<CollectionTargetAssociation> associations, IEnumerable<CollectionMemberBinding> bindings,
			IEnumerable<UserOverride> overrides)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			Associations = Copy(associations, nameof(associations));
			Bindings = Copy(bindings, nameof(bindings));
			Overrides = Copy(overrides, nameof(overrides));
		}

		public CollectionTargetIdentity Target { get; }
		public ReadOnlyCollection<CollectionTargetAssociation> Associations { get; }
		public ReadOnlyCollection<CollectionMemberBinding> Bindings { get; }
		public ReadOnlyCollection<UserOverride> Overrides { get; }

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => ReferenceEquals(x, null)))
				throw new ArgumentException("A target association snapshot cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}
}
