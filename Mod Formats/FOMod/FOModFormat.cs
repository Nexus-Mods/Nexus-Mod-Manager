using System;
using System.IO;
using System.Linq;
using Nexus.Client.Util;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.Scripting;
using System.Collections.Generic;

namespace Nexus.Client.Mods.Formats.FOMod
{
	/// <summary>
	/// Describes the FOMod mod format.
	/// </summary>
	/// <remarks>
	/// This is the mod format that is commonly used for Fallout 3 and Fallout: New Vegas mods. This
	/// format was introduced with the Fallout Mod Manager (FOMM).
	/// </remarks>
	public class FOModFormat : IModFormat
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
				return "FOMod";
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
				return "FOMod";
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
				return ".fomod";
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
				return true;
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
		public FOModFormat(IModCacheManager p_mcmModCacheManager, IScriptTypeRegistry p_stgScriptTypeRegistry)
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
			if (Directory.Exists(p_strPath))
			{
				var fileList = Directory.EnumerateFiles(p_strPath, "info.xml", SearchOption.AllDirectories);
				
				if (fileList.Count() > 0)
					return FormatConfidence.Match;
			}
			else
			{
				if (String.IsNullOrEmpty(p_strPath) || !File.Exists(p_strPath) || !Archive.IsArchive(p_strPath))
					return FormatConfidence.Incompatible;

				using (Archive arcMod = new Archive(p_strPath))
				{
					if (arcMod.GetFiles("fomod", true).Length > 0)
						return FormatConfidence.Match;
				}
			}

			return FormatConfidence.Compatible;
		}

		/// <summary>
		/// Creates a mod from the specified file.
		/// </summary>
		/// <remarks>
		/// The specified file must be in the current format.
		/// </remarks>
		/// <param name="p_strPath">The path of the file from which to create an <see cref="IMod"/>.</param>
		/// <returns>A mod from the specified file.</returns>
		public IMod CreateMod(string p_strPath, IGameMode p_gmdGameMode)
		{
			if (CheckFormatCompliance(p_strPath) <= FormatConfidence.Convertible)
				throw new ModFormatException(this);

			return new FOMod(p_strPath, this, p_gmdGameMode.StopFolders, Path.GetFileName(p_gmdGameMode.PluginDirectory), p_gmdGameMode.PluginExtensions, ModCacheManager, IScriptTypeRegistry, p_gmdGameMode.UsesPlugins, (p_gmdGameMode.ModeId.StartsWith("DragonAge", StringComparison.InvariantCultureIgnoreCase)));
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
			return new FOModModCompressor(p_eifEnvironmentInfo);
		}
	}
}
