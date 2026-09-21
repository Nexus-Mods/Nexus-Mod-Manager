using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes one completed C6.13 detach-tracking operation.
	/// </summary>
	public sealed class CollectionDetachResult
	{
		internal CollectionDetachResult(CollectionOperation operation, CollectionTargetAssociation detachedAssociation,
			IEnumerable<CollectionMemberBinding> detachedBindings, IEnumerable<NativeModProvenance> standaloneProvenance)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			DetachedAssociation = detachedAssociation ?? throw new ArgumentNullException(nameof(detachedAssociation));
			DetachedBindings = new ReadOnlyCollection<CollectionMemberBinding>(
				new List<CollectionMemberBinding>(detachedBindings ?? throw new ArgumentNullException(nameof(detachedBindings))));
			StandaloneProvenance = new ReadOnlyCollection<NativeModProvenance>(
				new List<NativeModProvenance>(standaloneProvenance ?? throw new ArgumentNullException(nameof(standaloneProvenance))));
		}

		/// <summary>Gets the durable completed DetachTracking operation journal record.</summary>
		public CollectionOperation Operation { get; }

		/// <summary>Gets the exact association baseline that was detached.</summary>
		public CollectionTargetAssociation DetachedAssociation { get; }

		/// <summary>Gets the member bindings removed from Collection tracking by this detach.</summary>
		public ReadOnlyCollection<CollectionMemberBinding> DetachedBindings { get; }

		/// <summary>Gets the distinct native mods explicitly preserved as standalone user use.</summary>
		public ReadOnlyCollection<NativeModProvenance> StandaloneProvenance { get; }
	}

	/// <summary>
	/// Implements C6.13 safe Detach: remove one Collection association while preserving native mods/effects.
	/// </summary>
	/// <remarks>
	/// Detach is intentionally feature-metadata-only. Every native mod referenced by the detached association is promoted to
	/// explicit standalone provenance before the association/bindings are removed in the same short Collections transaction.
	/// It never calls native install, uninstall, deployment, plugin or configuration mutation services.
	/// </remarks>
	public sealed class CollectionDetachCoordinator
	{
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionTargetIdentity _target;

		/// <summary>Creates a C6.13 detach coordinator for one exact native target.</summary>
		public CollectionDetachCoordinator(CollectionsAssociationStore associationStore, CollectionTargetIdentity target)
		{
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_target = target ?? throw new ArgumentNullException(nameof(target));
		}

		/// <summary>
		/// Detaches one persisted association without uninstalling or otherwise changing any native effect.
		/// </summary>
		/// <remarks>
		/// Recovery/in-flight work blocks detach. Saved Collection/revision metadata and retained content are deliberately left
		/// intact; deleting saved Collections/content and uninstalling effects are separate operations.
		/// </remarks>
		public CollectionDetachResult Detach(Guid associationId)
		{
			if (associationId == Guid.Empty)
				throw new ArgumentException("A non-empty Collection association identifier is required.", nameof(associationId));

			CollectionsAssociationDetachRecord detached = _associationStore.DetachAssociation(associationId, _target,
				CollectionOperationIdentity.CreateNew());
			return new CollectionDetachResult(detached.Operation, detached.Association, detached.Bindings, detached.StandaloneProvenance);
		}
	}
}
