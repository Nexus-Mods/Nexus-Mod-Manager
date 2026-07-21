
namespace Nexus.Client.ModRepositories
{
	/// <summary>
	/// Describes the metadata of a file of a mod in a repository.
	/// </summary>
	public interface IModFileInfo
	{
		#region Properties

		/// <summary>
		/// Gets the file id.
		/// </summary>
		/// <value>The file id.</value>
		string Id { get; }
		
		/// <summary>
		/// Gets the filename.
		/// </summary>
		/// <value>The filename.</value>
		string Filename { get; }

		/// <summary>
		/// Gets the friendly name of the file.
		/// </summary>
		/// <value>The friendly name of the file.</value>
		string Name { get; }

		/// <summary>
		/// Gets or sets the human readable version of the mod to which the file belongs.
		/// </summary>
		/// <value>The human readable version of the mod to which the file belongs.</value>
		string HumanReadableVersion { get; }

		#endregion
	}
}
