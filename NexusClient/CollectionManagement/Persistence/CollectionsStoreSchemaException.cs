using System;
using System.IO;
using System.Runtime.Serialization;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Classifies an incompatible or invalid Collections feature-store schema.
	/// </summary>
	public enum CollectionsStoreSchemaFailureKind
	{
		Invalid = 1,
		NewerThanSupported = 2,
		MigrationUnavailable = 3
	}

	/// <summary>
	/// Raised when an existing Collections feature store cannot be interpreted safely by this NMM version.
	/// </summary>
	[Serializable]
	public sealed class CollectionsStoreSchemaException : IOException
	{
		private const string FailureKindSerializationKey = "CollectionsStoreSchemaFailureKind";
		private const string DetectedVersionSerializationKey = "CollectionsStoreDetectedSchemaVersion";

		public CollectionsStoreSchemaException(string message)
			: this(CollectionsStoreSchemaFailureKind.Invalid, null, message, null)
		{
		}

		public CollectionsStoreSchemaException(string message, Exception innerException)
			: this(CollectionsStoreSchemaFailureKind.Invalid, null, message, innerException)
		{
		}

		public CollectionsStoreSchemaException(CollectionsStoreSchemaFailureKind failureKind,
			int? detectedSchemaVersion, string message)
			: this(failureKind, detectedSchemaVersion, message, null)
		{
		}

		public CollectionsStoreSchemaException(CollectionsStoreSchemaFailureKind failureKind,
			int? detectedSchemaVersion, string message, Exception innerException)
			: base(message, innerException)
		{
			FailureKind = failureKind;
			DetectedSchemaVersion = detectedSchemaVersion;
		}

		private CollectionsStoreSchemaException(SerializationInfo info, StreamingContext context)
			: base(info, context)
		{
			try
			{
				FailureKind = (CollectionsStoreSchemaFailureKind)info.GetInt32(FailureKindSerializationKey);
				DetectedSchemaVersion = (int?)info.GetValue(DetectedVersionSerializationKey, typeof(int?));
			}
			catch (SerializationException)
			{
				FailureKind = CollectionsStoreSchemaFailureKind.Invalid;
				DetectedSchemaVersion = null;
			}
		}

		public CollectionsStoreSchemaFailureKind FailureKind { get; }
		public int? DetectedSchemaVersion { get; }

		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			if (info == null)
				throw new ArgumentNullException(nameof(info));

			info.AddValue(FailureKindSerializationKey, (int)FailureKind);
			info.AddValue(DetectedVersionSerializationKey, DetectedSchemaVersion, typeof(int?));
			base.GetObjectData(info, context);
		}
	}
}
