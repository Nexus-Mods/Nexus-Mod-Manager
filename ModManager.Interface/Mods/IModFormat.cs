using Nexus.Client.Games;

namespace Nexus.Client.Mods
{
	/// <summary>
	/// Enumerates the level of confidence that a file is of a specific <see cref="IModFormat"/>.
	/// </summary>
	public enum FormatConfidence
	{
		/// <summary>
		/// Indicates the file is definitively of the specific <see cref="IModFormat"/>.
		/// </summary>
		Match = 3,

		/// <summary>
		/// Indicates the file is combatible with the specific <see cref="IModFormat"/>.
		/// </summary>
		Compatible = 2,

		/// <summary>
		/// Indicates the file can be converted to the specific <see cref="IModFormat"/>.
		/// </summary>
		Convertible = 1,

		/// <summary>
		/// Indicates the file is incompatible with the specific <see cref="IModFormat"/>.
		/// </summary>
		Incompatible = 0
	}

	/// <summary>
	/// Describes the properties and methods of a mod format.
	/// </summary>
	public interface IModFormat
	{
		#region Properties

		/// <summary>
		/// Gets the name of the mod format.
		/// </summary>
		/// <value>The name of the mod format.</value>
		string Name { get; }

		/// <summary>
		/// Gets the unique identifier of the mod format.
		/// </summary>
		/// <value>The unique identifier of the mod format.</value>
		string Id { get; }

		/// <summary>
		/// Gets the extension used for mods of this type.
		/// </summary>
		/// <value>The extension used for mods of this type.</value>
		string Extension { get; }

		/// <summary>
		/// Gets whether the mod format can compress mods from source files.
		/// </summary>
		/// <value>Whether the mod format can compress mods from source files.</value>
		bool SupportsModCompression { get; }

		#endregion

		/// <summary>
		/// Determines if the specified file is a mod that conforms to the current format.
		/// </summary>
		/// <param name="p_strPath">The path of the file for which it is to be determined whether it confroms
		/// to the current format.</param>
		/// <returns>A <see cref="FormatConfidence"/> indicating how much the specified file conforms
		/// to the current format.</returns>
		FormatConfidence CheckFormatCompliance(string p_strPath);

		/// <summary>
		/// Creates a mod from the specified file.
		/// </summary>
		/// <remarks>
		/// The specified file must be in the current format.
		/// </remarks>
		/// <param name="p_strPath">The path of the file from which to create an <see cref="IMod"/>.</param>
		/// <param name="p_gmdGameMode">The game mode for which to create the plugin.</param>
		///	<param name="isResetCachePath">Whether to reset the cache path.</param>
		/// <returns>A mod from the specified file.</returns>
		IMod CreateMod(string p_strPath, IGameMode p_gmdGameMode, bool isResetCachePath);

		/// <summary>
		/// Gets a <see cref="IModCompressor"/> that can compress a source folder into
		/// a mod of the current format.
		/// </summary>
		/// <returns>A <see cref="IModCompressor"/> that can compress a source folder into
		/// a mod of the current format.</returns>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		IModCompressor GetModCompressor(IEnvironmentInfo p_eifEnvironmentInfo);
	}
}
