using System;
using System.IO;
using Nexus.Client.Util;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.Scripting;

namespace Nexus.Client.Mods.Formats.OMod
{
	/// <summary>
	/// Describes the OMod mod format.
	/// </summary>
	/// <remarks>
	/// This is the mod format that is commonly used for Oblivion mods. This
	/// format was introduced with the Oblivion Mod Manager (OBMM).
	/// </remarks>
	public class OModFormat : IModFormat
	{
		#region Properties

		/// <summary>
		/// Gets the name of the mod format.
		/// </summary>
		/// <value>The name of the mod format.</value>
		public string Name
		{
			get
			{
				return "OMod";
			}
		}

		/// <summary>
		/// Gets the unique identifier of the mod format.
		/// </summary>
		/// <value>The unique identifier of the mod format.</value>
		public string Id
		{
			get
			{
				return "OMod";
			}
		}

		/// <summary>
		/// Gets the extension used for mods of this type.
		/// </summary>
		/// <value>The extension used for mods of this type.</value>
		public string Extension
		{
			get
			{
				return ".omod";
			}
		}

		/// <summary>
		/// Gets whether the mod format can compress mods from source files.
		/// </summary>
		/// <value>Whether the mod format can compress mods from source files.</value>
		public bool SupportsModCompression
		{
			get
			{
				return false;
			}
		}

		/// <summary>
		/// Gets or sets the manager for the current game mode's mod cache.
		/// </summary>
		/// <value>The manager for the current game mode's mod cache.</value>
		protected IModCacheManager ModCacheManager { get; set; }

		/// <summary>
		/// Gets the registry of supported script types.
		/// </summary>
		/// <value>The registry of supported script types.</value>
		protected IScriptTypeRegistry IScriptTypeRegistry { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the required dependencies.
		/// </summary>
		/// <param name="p_mcmModCacheManager">The manager for the current game mode's mod cache.</param>
		/// <param name="p_stgScriptTypeRegistry">The registry of supported script types.</param>
		public OModFormat(IModCacheManager p_mcmModCacheManager, IScriptTypeRegistry p_stgScriptTypeRegistry)
		{
			ModCacheManager = p_mcmModCacheManager;
			IScriptTypeRegistry = p_stgScriptTypeRegistry;
		}

		#endregion

		/// <summary>
		/// Determines if the specified file in a mod that conforms to the current format.
		/// </summary>
		/// <param name="p_strPath">The path of the file for which it is to be determined whether it confroms
		/// to the current format.</param>
		/// <returns>A <see cref="FormatConfidence"/> indicating how much the specified file conforms
		/// to the current format.</returns>
		public FormatConfidence CheckFormatCompliance(string p_strPath)
		{
			if (String.IsNullOrEmpty(p_strPath) || !File.Exists(p_strPath) || !Archive.IsArchive(p_strPath))
				return FormatConfidence.Incompatible;
			using (Archive arcMod = new Archive(p_strPath))
			{
				//full-on OMod
				if (arcMod.ContainsFile("config") &&
						(arcMod.ContainsFile("plugins.crc") ||
						arcMod.ContainsFile("data.crc") ||
						arcMod.ContainsFile("image") ||
						arcMod.ContainsFile("readme") ||
						arcMod.ContainsFile("script")))
					return FormatConfidence.Match;
				//OMod-ready archive
				string[] strFiles= arcMod.GetFiles(null, "omod conversion data/config", true);
				if (strFiles.Length > 0)
					return FormatConfidence.Match;
			}
			return FormatConfidence.Incompatible;
		}

		/// <summary>
		/// Creates a mod from the specified file.
		/// </summary>
		/// <remarks>
		/// The specified file must be in the current format.
		/// </remarks>
		/// <param name="p_strPath">The path of the file from which to create an <see cref="IMod"/>.</param>
		/// <param name="p_gmdGameMode">The game mode creating the mod.</param>
		/// <returns>A mod from the specified file.</returns>
		public IMod CreateMod(string p_strPath, IGameMode p_gmdGameMode)
		{
			if (CheckFormatCompliance(p_strPath) <= FormatConfidence.Convertible)
				throw new ModFormatException(this);
			return new OMod(p_strPath, this, ModCacheManager, IScriptTypeRegistry);
		}

		/// <summary>
		/// Gets a <see cref="IModCompressor"/> that can compress a source folder into
		/// a mod of the current format.
		/// </summary>
		/// <returns>A <see cref="IModCompressor"/> that can compress a source folder into
		/// a mod of the current format.</returns>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public IModCompressor GetModCompressor(IEnvironmentInfo p_eifEnvironmentInfo)
		{
			return null;
		}
	}
}
