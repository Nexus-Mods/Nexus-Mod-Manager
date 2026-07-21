using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.PluginManagement.UI
{
	/// <summary>
	/// An event arguments class that indicates that an import operation failed.
	/// </summary>
	public sealed class ImportFailedEventArgs : EventArgs
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_exError">The error that occurred.</param>
		/// <remarks>
		/// This form is used when importing from the clipboard.
		/// 
		/// The value of the <see cref="Message"/> is initially set to the given exception's <see cref="Exception.Message"/>.
		/// </remarks>
		public ImportFailedEventArgs(Exception p_exError)
		{
			this.Error = p_exError;
			this.Message = p_exError.Message;
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strMessage">A message describing why the import failed.</param>
		/// <remarks>
		/// This form is used when importing from the clipboard.
		/// </remarks>
		public ImportFailedEventArgs(string p_strMessage)
		{
			this.Message = p_strMessage;
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order failed to import from.</param>
		/// <param name="p_exError">The error that occurred.</param>
		/// <remarks>
		/// The value of the <see cref="Message"/> is initially set to the given exception's <see cref="Exception.Message"/>.
		/// </remarks>
		public ImportFailedEventArgs(string p_strFilename, Exception p_exError)
		{
			this.Filename = p_strFilename;
			this.Error = p_exError;
			this.Message = p_exError.Message;
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order failed to import from.</param>
		/// <param name="p_strMessage">A message describing why the import failed.</param>
		public ImportFailedEventArgs(string p_strFilename, string p_strMessage)
		{
			this.Filename = p_strFilename;
			this.Error = null;
			this.Message = p_strMessage;
		}

		#endregion

		#region Properties

		/// <summary>
		/// The filename that the load order failed to import from.
		/// </summary>
		public string Filename { get; private set; }

		/// <summary>
		/// The error that occurred.
		/// </summary>
		public Exception Error { get; private set; }

		/// <summary>
		/// A message describing why the Import failed.
		/// </summary>
		public string Message { get; private set; }

		#endregion
	}
}
