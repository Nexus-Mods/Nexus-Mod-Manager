using System;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.Sorter
{
	/// <summary>
	/// The exception that is thrown if an error occurs with SORTER.
	/// </summary>
	public class SorterException : Exception
	{
		/// <summary>
		/// The default constructor.
		/// </summary>
		public SorterException()
		{
		}

		/// <summary>
		/// A simple contructor that sets the exception's message.
		/// </summary>
		/// <param name="message">The exception's message.</param>
		public SorterException(string message)
			: base(message)
		{
		}

		/// <summary>
		/// A simple constructor the sets the exception's message and inner exception.
		/// </summary>
		/// <param name="message">The exception's message.</param>
		/// <param name="inner">The ineer exception.</param>
		public SorterException(string message, Exception inner)
			: base(message, inner)
		{
		}
	}
}
