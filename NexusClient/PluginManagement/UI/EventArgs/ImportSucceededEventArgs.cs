using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Nexus.Client.PluginManagement.UI
{
	/// <summary>
	/// An event arguments class that indicates that an import operation succeeded.
	/// </summary>
	public sealed class ImportSucceededEventArgs : EventArgs
	{
		#region Constructor

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_intTotalPluginCount">The total number of plugins that were found on the clipboard.</param>
		/// <param name="p_intImportedPluginCount">The number of plugins that were imported.</param>
		/// <param name="p_lstNotImported">The list of plugins that were not found in the <see cref="PluginManager.ManagedPlugins"/> collection.</param>
		public ImportSucceededEventArgs(int p_intTotalPluginCount, int p_intImportedPluginCount, List<string> p_lstNotImported)
			: this(null, false, p_intTotalPluginCount, p_intImportedPluginCount, p_lstNotImported)
		{
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order was imported from.</param>
		/// <param name="p_intTotalPluginCount">The total number of plugins that were found in the specified import source.</param>
		/// <param name="p_intImportedPluginCount">The number of plugins that were imported.</param>
		/// <param name="p_lstNotImported">The list of plugins that were not found in the <see cref="PluginManager.ManagedPlugins"/> collection.</param>
		public ImportSucceededEventArgs(string p_strFilename, int p_intTotalPluginCount, int p_intImportedPluginCount, List<string> p_lstNotImported)
			: this(p_strFilename, false, p_intTotalPluginCount, p_intImportedPluginCount, p_lstNotImported)
		{
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_blnPartialSuccess">Whether or not the import was only partially successful.</param>
		/// <param name="p_intTotalPluginCount">The total number of plugins that were found on the clipboard.</param>
		/// <param name="p_intImportedPluginCount">The number of plugins that were imported.</param>
		/// <param name="p_lstNotImported">The list of plugins that were not found in the <see cref="PluginManager.ManagedPlugins"/> collection.</param>
		public ImportSucceededEventArgs(bool p_blnPartialSuccess, int p_intTotalPluginCount, int p_intImportedPluginCount, List<string> p_lstNotImported)
			: this(null, p_blnPartialSuccess, p_intTotalPluginCount, p_intImportedPluginCount, p_lstNotImported)
		{
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order was imported from.</param>
		/// <param name="p_blnPartialSuccess">Whether or not the import was only partially successful.</param>
		/// <param name="p_intTotalPluginCount">The total number of plugins that were found in the specified import source.</param>
		/// <param name="p_intImportedPluginCount">The number of plugins that were imported.</param>
		/// <param name="p_lstNotImported">The list of plugins that were not found in the <see cref="PluginManager.ManagedPlugins"/> collection.</param>
		public ImportSucceededEventArgs(string p_strFilename, bool p_blnPartialSuccess, int p_intTotalPluginCount, int p_intImportedPluginCount, List<string> p_lstNotImported)
		{
			this.Filename = p_strFilename;
			this.PartialSuccess = p_blnPartialSuccess;
			this.TotalPluginCount = p_intTotalPluginCount;
			this.ImportedPluginCount = p_intImportedPluginCount;
			this.PluginsNotImported = new ReadOnlyCollection<string>(p_lstNotImported);
		}

		#endregion

		#region Properties

		/// <summary>
		/// Determines whether or not the import was only partially successful.
		/// </summary>
		public bool PartialSuccess { get; private set; }

		/// <summary>
		/// The filename that the load order was imported from.
		/// </summary>
		public string Filename { get; private set; }

		/// <summary>
		/// The number of plugins that were imported.
		/// </summary>
		public int ImportedPluginCount { get; private set; }

		/// <summary>
		/// Gets the collection of plugins that were not imported due to not being found in the
		/// <see cref="PluginManager.ManagedPlugins"/> collection.
		/// </summary>
		public ReadOnlyCollection<string> PluginsNotImported { get; private set; }

		/// <summary>
		/// The total number of plugins that were found in the import source.
		/// </summary>
		public int TotalPluginCount { get; private set; }

		#endregion
	}
}
