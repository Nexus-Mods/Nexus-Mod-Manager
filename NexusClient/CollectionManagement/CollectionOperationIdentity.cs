using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one logical collection-level operation across preparation, native children and recovery.
	/// </summary>
	public sealed class CollectionOperationIdentity : IEquatable<CollectionOperationIdentity>
	{
		private CollectionOperationIdentity(Guid operationId)
		{
			if (operationId == Guid.Empty)
				throw new ArgumentException("A non-empty collection operation identifier is required.", nameof(operationId));

			OperationId = operationId;
		}

		/// <summary>
		/// Gets the stable logical collection-operation identifier.
		/// </summary>
		public Guid OperationId { get; }

		/// <summary>
		/// Creates an identity from a persisted operation identifier.
		/// </summary>
		public static CollectionOperationIdentity From(Guid operationId)
		{
			return new CollectionOperationIdentity(operationId);
		}

		/// <summary>
		/// Creates a new logical collection operation.
		/// </summary>
		public static CollectionOperationIdentity CreateNew()
		{
			return new CollectionOperationIdentity(Guid.NewGuid());
		}

		/// <inheritdoc />
		public bool Equals(CollectionOperationIdentity other)
		{
			return !ReferenceEquals(other, null) && OperationId == other.OperationId;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionOperationIdentity);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return OperationId.GetHashCode();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return OperationId.ToString("D");
		}
	}
}
