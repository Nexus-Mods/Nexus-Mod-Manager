namespace Nexus.Client.Mods
{
	/// <summary>Optional read-only view of archive paths before a mod format's virtual folder restructuring.</summary>
	public interface IModArchiveSourcePathProvider
	{
		/// <summary>Gets the original relative archive path for a logical file returned by IMod.GetFileList.</summary>
		/// <remarks>File reads must still use the logical path; this view only preserves package layout evidence.</remarks>
		string GetArchiveSourcePath(string logicalPath);
	}
}
