using System;
using System.IO;
using System.Runtime.Serialization;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Classifies an operational failure that prevents safe access to the Collections feature store.
	/// </summary>
	public enum CollectionsStoreAccessFailureKind
	{
		Busy = 1,
		ReadOnly = 2,
		Corrupt = 3,
		Unavailable = 4
	}

	/// <summary>
	/// Raised when the Collections feature store cannot be accessed safely for an operational reason.
	/// </summary>
	[Serializable]
	public sealed class CollectionsStoreAccessException : IOException
	{
		private const string FailureKindSerializationKey = "CollectionsStoreAccessFailureKind";

		public CollectionsStoreAccessException(CollectionsStoreAccessFailureKind failureKind, string message)
			: base(message)
		{
			FailureKind = failureKind;
		}

		public CollectionsStoreAccessException(CollectionsStoreAccessFailureKind failureKind, string message, Exception innerException)
			: base(message, innerException)
		{
			FailureKind = failureKind;
		}

		private CollectionsStoreAccessException(SerializationInfo info, StreamingContext context)
			: base(info, context)
		{
			FailureKind = (CollectionsStoreAccessFailureKind)info.GetInt32(FailureKindSerializationKey);
		}

		public CollectionsStoreAccessFailureKind FailureKind { get; }

		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			if (info == null)
				throw new ArgumentNullException(nameof(info));

			info.AddValue(FailureKindSerializationKey, (int)FailureKind);
			base.GetObjectData(info, context);
		}
	}
}
