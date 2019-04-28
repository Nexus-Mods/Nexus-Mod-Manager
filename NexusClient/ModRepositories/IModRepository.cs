namespace Nexus.Client.ModRepositories
{
    using System;
    using System.Collections.Generic;
    using ModManagement;
    using Mods;
    using Pathoschild.FluentNexus.Models;

    /// <summary>
	/// Describes the methods and properties of a mod repository.
	/// </summary>
	/// <remarks>
	/// A mod repository provides access to mods, and mod information.
	/// </remarks>
	public interface IModRepository
	{
		#region Custom Events

		event EventHandler UserStatusUpdate;

		#endregion

		#region Properties

		/// <summary>
		/// Gets the id of the mod repository.
		/// </summary>
		/// <value>The id of the mod repository.</value>
		string Id { get; }

		/// <summary>
		/// Gets the name of the mod repository.
		/// </summary>
		/// <value>The name of the mod repository.</value>
		string Name { get; }

        /// <summary>
        /// Gets the user membership status.
        /// </summary>
        /// <value>The user membership status.</value>

        User UserStatus { get; }

		/// <summary>
		/// Gets the User Agent used for the mod repository.
		/// </summary>
		/// <value>The User Agent.</value>
		string UserAgent { get; }

		/// <summary>
		/// Gets whether the repository is in a forced offline mode.
		/// </summary>
		/// <value>Whether the repository is in a forced offline mode.</value>
		bool IsOffline { get; }

		/// <summary>
		/// Gets whether the repository supports unauthenticated downloads.
		/// </summary>
		/// <value>Whether the repository supports unauthenticated downloads.</value>
		bool SupportsUnauthenticatedDownload { get; }

		/// <summary>
		/// Gets the repository's file server zones.
		/// </summary>
		/// <value>the repository's file server zones.</value>
		List<FileServerZone> FileServerZones { get; }

		/// <summary>
		/// Gets the number allowed connections.
		/// </summary>
		/// <value>The number allowed connections.</value>
		Int32 AllowedConnections { get; }

		/// <summary>
		/// Gets the number of maximum allowed concurrent downloads.
		/// </summary>
		/// <value>The number of maximum allowed concurrent downloads.</value>
		Int32 MaxConcurrentDownloads { get; }

		string GameModeWebsite { get; }

		/// <summary>
		/// Gets the remote id of the mod repository.
		/// </summary>
		/// <value>The id of the mod repository.</value>
		int RemoteGameId { get; }

		#endregion

		#region Account Management

        /// <summary>
        /// Verifies the given API key is valid.
        /// </summary>
        /// <returns>True if valid, otherwise false.</returns>
        bool Authenticate();

		/// <summary>
		/// Logs the user out of the mod repository.
		/// </summary>
		void Logout();

		#endregion

		/// <summary>
		/// Gets the mod info for the mod to which the specified download file belongs.
		/// </summary>
		/// <param name="fileName">The name of the file whose mod's info is to be returned..</param>
		/// <returns>The info for the mod to which the specified file belongs.</returns>
		IModInfo GetModInfoForFile(string fileName);

		/// <summary>
		/// Gets the info for the specifed mod.
		/// </summary>
		/// <param name="p_strModId">The id of the mod info is be retrieved.</param>
		/// <returns>The info for the specifed mod.</returns>
		IModInfo GetModInfo(string p_strModId);

		/// <summary>
		/// Gets the info for the specifed mod list.
		/// </summary>
		/// <param name="modIdList">The mod list to submit.</param>
		/// <returns>The update mods' list.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		List<IModInfo> GetModListInfo(List<string> modIdList);

		/// <summary>
		/// Gets the info for the specifed file list.
		/// </summary>
		/// <param name="modFileList">The file list to submit.</param>
		/// <returns>The update mods' list.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		List<IModInfo> GetFileListInfo(List<string> modFileList);

		/// <summary>
		/// Toggles the mod Endorsement state.
		/// </summary>
		/// <param name="modId">The mod ID.</param>
		/// <param name="localState">The local Endorsement state.</param>
		/// <returns>The updated online Endorsement state.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		bool ToggleEndorsement(string modId, int localState);

		/// <summary>
		/// Gets the list of files for the specified mod.
		/// </summary>
		/// <param name="modId">The id of the mod whose list of files is to be returned.</param>
		/// <returns>The list of files for the specified mod.</returns>
		IList<IModFileInfo> GetModFileInfo(string modId);

		/// <summary>
		/// Gets the URLs of the file parts for the specified download file of the specified mod.
		/// </summary>
		/// <param name="modId">The id of the mod whose download file's parts' URLs are to be retrieved.</param>
		/// <param name="fileId">The id of the download file whose parts' URLs are to be retrieved.</param>
		/// <returns>The URLs of the file parts for the specified download file.</returns>
		Uri[] GetFilePartUrls(string modId, string fileId);

		/// <summary>
		/// Gets the FileserverInfo for the default download file of the specified mod.
		/// </summary>
		/// <param name="modId">The id of the mod whose default download file's parts' URLs are to be retrieved.</param>
		/// <param name="fileId">The id of the file whose parts' URLs are to be retrieved.</param>
		/// <param name="userLocation">The preferred user location.</param>
		/// <param name="repositoryMessage">Custom repository message, if needed.</param>
		/// <returns>The FileserverInfo of the file parts for the default download file.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		List<FileserverInfo> GetFilePartInfo(string modId, string fileId, string userLocation, out string repositoryMessage);

		/// <summary>
		/// Gets the file info for the specified download file of the specified mod.
		/// </summary>
		/// <param name="modId">The id of the mod the whose file's metadata is to be retrieved.</param>
		/// <param name="fileId">The id of the download file whose metadata is to be retrieved.</param>
		/// <returns>The file info for the specified download file of the specified mod.</returns>
		IModFileInfo GetFileInfo(string modId, string fileId);

		/// <summary>
		/// Gets the file info for the specified download file.
		/// </summary>
		/// <param name="fileName">The name of the file whose info is to be returned..</param>
		/// <returns>The file info for the specified download file.</returns>
		IModFileInfo GetFileInfoForFile(string fileName);

		/// <summary>
		/// Gets the file info for the default file of the specified mod.
		/// </summary>
		/// <param name="modId">The id of the mod the whose default file's metadata is to be retrieved.</param>
		/// <returns>The file info for the default file of the specified mod.</returns>
		IModFileInfo GetDefaultFileInfo(string modId);

		/// <summary>
		/// Finds the mods containing the given search terms.
		/// </summary>
		/// <param name="modNameSearchString">The terms to use to search for mods.</param>
		/// <param name="includeAllTerms">Whether the returned mods' names should include all of
		/// the given search terms.</param>
		/// <returns>The mod info for the mods matching the given search criteria.</returns>
		IList<IModInfo> FindMods(string modNameSearchString, bool includeAllTerms);

		/// <summary>
		/// Finds the mods by Author name.
		/// </summary>
		/// <param name="modNameSearchString">The terms to use to search for mods.</param>
		/// <param name="authorSearchString">The Author to use to search for mods.</param>
		/// <returns>The mod info for the mods matching the given search criteria.</returns>
		IList<IModInfo> FindMods(string modNameSearchString, string authorSearchString);

		/// <summary>
		/// Finds the mods containing the given search terms.
		/// </summary>
		/// <param name="modNameSearchString">The terms to use to search for mods.</param>
		/// <param name="modAuthor">The Mod author.</param>
		/// <param name="includeAllTerms">Whether the returned mods' names should include all of
		/// the given search terms.</param>
		/// <returns>The mod info for the mods matching the given search criteria.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		IList<IModInfo> FindMods(string modNameSearchString, string modAuthor, bool includeAllTerms);

		/// <summary>
		/// Gets the Categories array.
		/// </summary>
		/// <returns>The Categories array..</returns>
		List<CategoriesInfo> GetCategories(int gameId);
	}
}
