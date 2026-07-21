using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.PluginManagement.UI
{
	/// <summary>
	/// An event arguments class that indicates that an export operation failed.
	/// </summary>
	public sealed class ExportFailedEventArgs : EventArgs
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_exError">The error that occurred.</param>
		/// <remarks>
		/// This form is used when exporting to the clipboard.
		/// 
		/// The value of the <see cref="Message"/> is initially set to the given exception's <see cref="Exception.Message"/>.
		/// </remarks>
		public ExportFailedEventArgs(Exception p_exError)
		{
			this.Error = p_exError;
			this.Message = p_exError.Message;
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order failed to export to.</param>
		/// <param name="p_exError">The error that occurred.</param>
		/// <remarks>
		/// The value of the <see cref="Message"/> is initially set to the given exception's <see cref="Exception.Message"/>.
		/// </remarks>
		public ExportFailedEventArgs(string p_strFilename, Exception p_exError)
			: this(p_exError)
		{
			this.Filename = p_strFilename;
		}

		#endregion

		#region Properties

		/// <summary>
		/// The filename that the load order failed to export to.
		/// </summary>
		public string Filename { get; private set; }

		/// <summary>
		/// The error that occurred.
		/// </summary>
		public Exception Error { get; private set; }

		/// <summary>
		/// A message describing why the export failed.
		/// </summary>
		public string Message { get; private set; }

		#endregion
	}
}
