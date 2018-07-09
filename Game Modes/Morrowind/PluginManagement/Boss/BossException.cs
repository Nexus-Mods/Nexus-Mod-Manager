using System;

namespace Nexus.Client.Games.Morrowind.PluginManagement.Boss
{
	/// <summary>
	/// The exception that is thrown if an error occurs with BOSS.
	/// </summary>
	public class BossException : Exception
	{
		/// <summary>
		/// The default constructor.
		/// </summary>
		public BossException()
		{
		}

		/// <summary>
		/// A simple contructor that sets the exception's message.
		/// </summary>
		/// <param name="message">The exception's message.</param>
		public BossException(string message)
			: base(message)
		{
		}

		/// <summary>
		/// A simple constructor the sets the exception's message and inner exception.
		/// </summary>
		/// <param name="message">The exception's message.</param>
		/// <param name="inner">The ineer exception.</param>
		public BossException(string message, Exception inner)
			: base(message, inner)
		{
		}
	}
}
