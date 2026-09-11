using System.Collections.Generic;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Provides access to the persisted file selections produced by a scripted installer.
	/// </summary>
	public interface IScriptedFileSelectionCache
	{
		/// <summary>
		/// Gets the path of the scripted file-selection cache.
		/// </summary>
		/// <value>The full cache file path.</value>
		string FilePath { get; }

		/// <summary>
		/// Gets whether the scripted file-selection cache currently exists.
		/// </summary>
		/// <value><c>true</c> if the cache exists; otherwise, <c>false</c>.</value>
		bool Exists { get; }

		/// <summary>
		/// Records a source and destination mapping selected by the scripted installer.
		/// </summary>
		/// <param name="p_strFrom">The source path inside the mod archive.</param>
		/// <param name="p_strTo">The destination path selected by the installer.</param>
		void RecordSelection(string p_strFrom, string p_strTo);

		/// <summary>
		/// Loads the ordered source and destination mappings stored in the cache.
		/// </summary>
		/// <returns>The cached mappings, or <c>null</c> when no usable mappings are available.</returns>
		List<KeyValuePair<string, string>> LoadSelections();
	}
}
