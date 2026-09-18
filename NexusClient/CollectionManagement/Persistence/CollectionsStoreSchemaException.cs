using System;
using System.IO;
using System.Runtime.Serialization;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Raised when an existing Collections feature store cannot be interpreted safely by this NMM version.
	/// </summary>
	[Serializable]
	public sealed class CollectionsStoreSchemaException : IOException
	{
		public CollectionsStoreSchemaException(string message)
			: base(message)
		{
		}

		public CollectionsStoreSchemaException(string message, Exception innerException)
			: base(message, innerException)
		{
		}

		private CollectionsStoreSchemaException(SerializationInfo info, StreamingContext context)
			: base(info, context)
		{
		}
	}
}
