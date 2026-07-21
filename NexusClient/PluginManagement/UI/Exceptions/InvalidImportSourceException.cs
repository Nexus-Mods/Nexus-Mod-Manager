using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.PluginManagement.UI
{
	/// <summary>
	/// The exception that is thrown when a load order import source is invalid.
	/// </summary>
	public class InvalidImportSourceException : Exception
	{
		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="InvalidImportSourceException"/> class with the given message.
		/// </summary>
		/// <param name="message">The message that describes the error.</param>
		public InvalidImportSourceException(string message)
			: base(message)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="InvalidImportSourceException"/> class with the given message and inner exception.
		/// </summary>
		/// <param name="message">The message that describes the error.</param>
		/// <param name="innerException">The exception that is the cause of the current exception.</param>
		public InvalidImportSourceException(string message, Exception innerException)
			: base(message, innerException)
		{
		}

		#endregion
	}
}
