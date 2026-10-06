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

	/// <summary>
	/// Exposes a lightweight format probe backed by cached archive metadata.
	/// </summary>
	public interface IModFormatCacheProbe
	{
		/// <summary>
		/// Attempts to determine format compliance without opening the mod archive.
		/// </summary>
		/// <param name="p_strPath">The path to the original mod archive.</param>
		/// <param name="p_fcfConfidence">The cached format confidence when available.</param>
		/// <returns><c>true</c> if a valid cached result was found; otherwise, <c>false</c>.</returns>
		bool TryGetCachedFormatConfidence(string p_strPath, out FormatConfidence p_fcfConfidence);
	}
	/// <summary>Describes whether one persisted mod-format screenshot override belongs to the archive bytes currently present at its path.</summary>
	public enum ModFormatScreenshotOverrideReadState
	{
		None = 0,
		Current = 1,
		Stale = 2,
		ArchiveUnavailable = 3
	}

	/// <summary>Immutable logical export of one generated screenshot override owned by a mod format.</summary>
	public sealed class ModFormatScreenshotOverrideRecord
	{
		private readonly byte[] _screenshotData;

		public ModFormatScreenshotOverrideRecord(long archiveLength, long archiveWriteTimeUtcTicks, string screenshotPath,
			byte[] screenshotData, long updatedUtcTicks)
		{
			if (archiveLength < 0)
				throw new System.ArgumentOutOfRangeException(nameof(archiveLength));
			if (System.String.IsNullOrWhiteSpace(screenshotPath))
				throw new System.ArgumentException("A screenshot override path is required.", nameof(screenshotPath));
			if (screenshotData == null || screenshotData.Length == 0)
				throw new System.ArgumentException("A screenshot override requires non-empty bytes.", nameof(screenshotData));
			ArchiveLength = archiveLength;
			ArchiveWriteTimeUtcTicks = archiveWriteTimeUtcTicks;
			ScreenshotPath = screenshotPath;
			_screenshotData = (byte[])screenshotData.Clone();
			UpdatedUtcTicks = updatedUtcTicks;
		}

		public long ArchiveLength { get; }
		public long ArchiveWriteTimeUtcTicks { get; }
		public string ScreenshotPath { get; }
		public byte[] ScreenshotData { get { return (byte[])_screenshotData.Clone(); } }
		public long UpdatedUtcTicks { get; }
	}

	/// <summary>Result of logically reading one archive's generated screenshot override.</summary>
	public sealed class ModFormatScreenshotOverrideReadResult
	{
		public ModFormatScreenshotOverrideReadResult(ModFormatScreenshotOverrideReadState state, ModFormatScreenshotOverrideRecord record)
		{
			if (!System.Enum.IsDefined(typeof(ModFormatScreenshotOverrideReadState), state))
				throw new System.ArgumentOutOfRangeException(nameof(state));
			if (state == ModFormatScreenshotOverrideReadState.None && record != null)
				throw new System.ArgumentException("A missing screenshot override cannot carry a persisted record.", nameof(record));
			if (state != ModFormatScreenshotOverrideReadState.None && record == null)
				throw new System.ArgumentNullException(nameof(record));
			State = state;
			Record = record;
		}

		public ModFormatScreenshotOverrideReadState State { get; }
		public ModFormatScreenshotOverrideRecord Record { get; }
	}

	/// <summary>Exposes non-rebuildable logical user metadata through the shared mod-format contract.</summary>
	/// <remarks>
	/// Mod formats are discovered with <c>Assembly.LoadFile</c>, so application code must use this shared interface rather than
	/// concrete format types when accessing instances from the registry.
	/// </remarks>
	public interface IModFormatUserMetadata
	{
		bool IsUserMetadataUsable { get; }
		ModFormatScreenshotOverrideReadResult ReadScreenshotOverride(string archivePath);
		void RestoreScreenshotOverride(string archivePath, string screenshotPath, byte[] screenshotData, long updatedUtcTicks);
		void RemoveScreenshotOverride(string archivePath);
	}

}
