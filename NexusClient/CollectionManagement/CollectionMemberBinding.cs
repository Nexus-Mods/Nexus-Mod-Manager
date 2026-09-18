using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Records how a native mod instance became associated with one collection member.
	/// </summary>
	public enum CollectionMemberBindingKind
	{
		/// <summary>
		/// No trusted association provenance has been established.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// The collection adopted a compatible native mod instance that already existed on the target.
		/// </summary>
		AdoptedExisting = 1,

		/// <summary>
		/// The native mod instance was installed/reinstalled as part of satisfying this collection association.
		/// </summary>
		InstalledForCollection = 2
	}

	/// <summary>
	/// Maps one member of an associated collection revision to the native mod instance that currently satisfies it.
	/// </summary>
	/// <remarks>
	/// This is provenance and verified-recipe mapping, not a second file-owner registry. Several compatible collection
	/// associations may bind to the same native mod instance. Native InstallLog/deployment stores remain authoritative
	/// for the actual effects owned by that instance.
	/// </remarks>
	public sealed class CollectionMemberBinding : IEquatable<CollectionMemberBinding>
	{
		/// <summary>
		/// Creates an immutable collection-member binding snapshot.
		/// </summary>
		public CollectionMemberBinding(CollectionTargetAssociation association, CollectionMemberKey memberKey,
			NativeModInstanceIdentity nativeMod, CollectionRecipeIdentity verifiedRecipe, CollectionMemberBindingKind bindingKind)
		{
			if (association == null)
				throw new ArgumentNullException(nameof(association));
			if (memberKey == null)
				throw new ArgumentNullException(nameof(memberKey));
			if (nativeMod == null)
				throw new ArgumentNullException(nameof(nativeMod));
			if (!association.Target.Equals(nativeMod.Target))
				throw new ArgumentException("The native mod instance must belong to the association target.", nameof(nativeMod));
			if (verifiedRecipe == null)
				throw new ArgumentNullException(nameof(verifiedRecipe));
			if (!Enum.IsDefined(typeof(CollectionMemberBindingKind), bindingKind) || bindingKind == CollectionMemberBindingKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(bindingKind));

			Association = association;
			MemberKey = memberKey;
			NativeMod = nativeMod;
			VerifiedRecipe = verifiedRecipe;
			BindingKind = bindingKind;
		}

		/// <summary>
		/// Gets the target association containing this collection member.
		/// </summary>
		public CollectionTargetAssociation Association { get; }

		/// <summary>
		/// Gets the stable member key within the associated revision.
		/// </summary>
		public CollectionMemberKey MemberKey { get; }

		/// <summary>
		/// Gets the target-scoped native mod instance currently satisfying the member.
		/// </summary>
		public NativeModInstanceIdentity NativeMod { get; }

		/// <summary>
		/// Gets the exact verified recipe fingerprint satisfied by <see cref="NativeMod"/>.
		/// </summary>
		/// <remarks>
		/// The identity is opaque in C1.3. Later normalization/planning owns its contents and must include all
		/// behavior relevant to deciding whether an existing instance can really be reused.
		/// </remarks>
		public CollectionRecipeIdentity VerifiedRecipe { get; }

		/// <summary>
		/// Gets whether the collection adopted an existing instance or installed it for this association.
		/// </summary>
		public CollectionMemberBindingKind BindingKind { get; }

		/// <inheritdoc />
		public bool Equals(CollectionMemberBinding other)
		{
			return !ReferenceEquals(other, null) &&
				Association.AssociationId == other.Association.AssociationId &&
				Equals(MemberKey, other.MemberKey);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionMemberBinding);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return (Association.AssociationId.GetHashCode() * 397) ^ MemberKey.GetHashCode();
			}
		}
	}
}
