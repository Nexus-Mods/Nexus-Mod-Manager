using System.Collections.Generic;

namespace Nexus.Client.Mods
{
	/// <summary>
	/// Optional view of the actual logical archive entries available to scripted installers.
	/// The ordinary IMod.GetFileList inventory intentionally hides the fomod metadata subtree;
	/// XML installer file/folder sources can legitimately refer to assets within that subtree.
	/// </summary>
	public interface IModInstallerSourceInventory
	{
		/// <summary>
		/// Enumerates actual archive files, including assets under fomod, using paths that
		/// can be passed to IMod.GetFileStream. This must not synthesize missing entries.
		/// A null folderPath requests the complete logical archive inventory.
		/// </summary>
		List<string> GetInstallerSourceFiles(string folderPath, bool recurse);
	}
}
