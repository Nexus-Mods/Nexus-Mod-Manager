using System;
using System.Runtime.Serialization;

namespace Nexus.Client.ModRepositories
{
	/// <summary>
	/// The exception that is thrown if a repository is not available.
	/// </summary>
	[Serializable]
	public class RepositoryUnavailableException : Exception
	{
		/// <summary>
		/// The default constructor.
		/// </summary>
		public RepositoryUnavailableException()
		{
		}

		/// <summary>
		/// A simple contructor that sets the exception's message.
		/// </summary>
		/// <param name="message">The exception's message.</param>
		public RepositoryUnavailableException(string message)
			: base(message)
		{
		}

		/// <summary>
		/// A simple constructor the sets the exception's message and inner exception.
		/// </summary>
		/// <param name="message">The exception's message.</param>
		/// <param name="inner">The ineer exception.</param>
		public RepositoryUnavailableException(string message, Exception inner)
			: base(message, inner)
		{
		}
		
		/// <summary>
		/// The serializing constructor.
		/// </summary>
		/// <param name="info">The info from which to deserialize the object.</param>
		/// <param name="context">The context from which to deserialize.</param>
		protected RepositoryUnavailableException(SerializationInfo info, StreamingContext context)
			: base(info, context)
		{
		}
	}
}
