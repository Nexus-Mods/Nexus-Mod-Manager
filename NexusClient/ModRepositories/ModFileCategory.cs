
namespace Nexus.Client.ModRepositories
{
	/// <summary>
	/// The mod file categories.
	/// </summary>
	public enum ModFileCategory
	{
		/// <summary>
		/// Indicates the file ia a main file in the mod.
		/// </summary>
		MainFiles=1,

		/// <summary>
		/// Indicates the files is an update for the mod.
		/// </summary>
		Updates=2,

		/// <summary>
		/// Indicates teh file is optional for the mod.
		/// </summary>
		OptionalFiles=3,

		/// <summary>
		/// Indicates the file is for an old version of the mod.
		/// </summary>
		OldVersions=4,

		/// <summary>
		/// Indicates the files is a support file for the mod.
		/// </summary>
		Misc=5
	}
}
