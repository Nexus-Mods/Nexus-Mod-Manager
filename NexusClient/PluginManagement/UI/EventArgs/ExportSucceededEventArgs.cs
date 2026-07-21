using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.PluginManagement.UI
{
	/// <summary>
	/// An event arguments class that indicates that an export operation succeeded.
	/// </summary>
	public sealed class ExportSucceededEventArgs : EventArgs
	{
		#region Constructor

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_intExportedPluginCount">The number of plaugins that were exported.</param>
		/// <remarks>
		/// This form of the constructor is used when exporting to the clipboard.
		/// </remarks>
		public ExportSucceededEventArgs(int p_intExportedPluginCount)
		{
			this.ExportedPluginCount = p_intExportedPluginCount;
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order was exported to.</param>
		/// <param name="p_intExportedPluginCount">The number of plaugins that were exported.</param>
		public ExportSucceededEventArgs(string p_strFilename, int p_intExportedPluginCount)
		{
			this.Filename = p_strFilename;
			this.ExportedPluginCount = p_intExportedPluginCount;
		}

		#endregion

		#region Properties

		/// <summary>
		/// The filename that the load order was exported to.
		/// </summary>
		public string Filename { get; private set; }

		/// <summary>
		/// The number of plugins that were exported.
		/// </summary>
		public int ExportedPluginCount { get; private set; }

		#endregion
	}
}
