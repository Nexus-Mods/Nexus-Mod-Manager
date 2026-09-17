namespace Nexus.Client.ModRepositories
{
    using System;
    using System.Collections.Generic;
	using System.Threading.Tasks;
	using ModManagement;
    using Mods;

    /// <summary>
	/// Describes the methods and properties of a mod repository.
	/// </summary>
	/// <remarks>
	/// A mod repository provides access to mods, and mod information.
	/// </remarks>
	public interface IModRepository
	{
		#region Custom Events

        /// <summary>
        /// Invoked when UserStatus changes.
        /// </summary>
		event EventHandler UserStatusUpdate;

        /// <summary>
        /// Invoked when the rate limit has been exceeded.
        /// </summary>
        event EventHandler<RateLimitExceededArgs> RateLimitExceeded;

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

        RepositoryUserStatus UserStatus { get; }

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
		/// Gets the number allowed connections.
		/// </summary>
		/// <value>The number allowed connections.</value>
		int AllowedConnections { get; }

		/// <summary>
		/// Gets the number of maximum allowed concurrent downloads.
		/// </summary>
		/// <value>The number of maximum allowed concurrent downloads.</value>
		int MaxConcurrentDownloads { get; }

        /// <summary>
        /// Game Domain E.g. 'skyrim'
        /// </summary>
        string GameDomainName { get; }

        /// <summary>
        /// Gets the current rate limits.
        /// </summary>
        RepositoryRateLimit RateLimit { get; }

        #endregion

        #region Account Management

        /// <summary>
        /// Verifies the given API key is valid.
        /// </summary>
        /// <returns>True if valid, otherwise false.</returns>
        AuthenticationStatus Authenticate();

        /// <summary>
        /// Verifies the configured API key and optionally clears the originating credential generation if authentication fails.
        /// </summary>
        /// <param name="clearCredentialsOnFailure">Whether failed authentication should clear the credentials used by that attempt.</param>
        /// <returns>The authentication result.</returns>
        AuthenticationStatus Authenticate(bool clearCredentialsOnFailure);

		/// <summary>
		/// Logs the user out of the mod repository.
		/// </summary>
		void Logout();

		#endregion

		/// <summary>
		/// Gets the mod info for the mod to which the specified download file belongs.
		/// </summary>
		/// <param name="fileName">The name of the file whose mod's info is to be returned.</param>
		/// <returns>The info for the mod to which the specified file belongs.</returns>
		IModInfo GetModInfoForFile(string fileName);

        /// <summary>
        /// Gets the mod file info which the specified download file belongs.
        /// </summary>
        /// <param name="fileName">The name of the file whose info is to be returned.</param>
        /// <returns>The info for the mod specified file.</returns>
        IModFileInfo GetModFileInfoForFile(string fileName);

        /// <summary>
        /// Gets the info for the specified mod.
        /// </summary>
        /// <param name="modId">The id of the mod info is be retrieved.</param>
        /// <returns>The info for the specified mod.</returns>
        IModInfo GetModInfo(string modId);

		/// <summary>
		/// Gets repository category IDs for the specified Nexus mod IDs without resolving file metadata.
		/// </summary>
		/// <param name="modIds">The Nexus mod IDs whose repository categories should be retrieved.</param>
		/// <returns>A map of normalized Nexus mod ID to repository category ID for identities resolved successfully.</returns>
		Dictionary<string, int> GetModCategoryIds(IEnumerable<string> modIds);

		/// <summary>
		/// Gets the info for the specified file list.
		/// </summary>
		/// <param name="modFileList">The file list to submit.</param>
		/// <returns>The update mods' list.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		List<IModInfo> GetFileListInfo(List<string> modFileList);

		/// <summary>
		/// Gets update metadata while reusing persisted file-resolution results whose provider freshness timestamp is unchanged.
		/// </summary>
		/// <param name="modFileList">The file list to submit.</param>
		/// <param name="providerUpdates">The provider freshness records for the current update-check period.</param>
		/// <returns>The aligned metadata and checkpoint candidates.</returns>
		RepositoryFileListInfoResult GetFileListInfoWithFreshness(List<string> modFileList, IEnumerable<RepositoryModUpdate> providerUpdates);

		/// <summary>
		/// Publishes file-resolution checkpoints after the corresponding local metadata application completed successfully.
		/// </summary>
		/// <param name="checkpoints">The successfully applied checkpoint candidates.</param>
		/// <returns>Whether the checkpoint data was persisted successfully.</returns>
		bool CommitFileUpdateCheckpoints(IEnumerable<RepositoryFileUpdateCheckpoint> checkpoints);

        /// <summary>
        /// Toggles the mod Endorsement state.
        /// </summary>
        /// <param name="modId">The mod ID.</param>
        /// <param name="localState">The local Endorsement state.</param>
        /// <param name="version">Version of the mod to endorse.</param>
        /// <returns>The updated online Endorsement state.</returns>
        Task<bool?> ToggleEndorsement(string modId, int localState, string version);

		/// <summary>
		/// Gets the list of files for the specified mod.
		/// </summary>
		/// <param name="modId">The id of the mod whose list of files is to be returned.</param>
		/// <returns>The list of files for the specified mod.</returns>
		IList<IModFileInfo> GetModFileInfo(string modId);

        /// <summary>
        /// Gets the FileServerInfo for the default download file of the specified mod.
        /// </summary>
        /// <param name="modId">The id of the mod whose default download file's parts' URLs are to be retrieved.</param>
        /// <param name="fileId">The id of the file whose parts' URLs are to be retrieved.</param>
        /// <returns>The FileServerInfo of the file parts for the default download file.</returns>
        /// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
        List<RepositoryDownloadLink> GetFilePartInfo(string modId, string fileId, string key = "", int expiry = -1);

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
		/// Gets the list of mods that were updated during the given period.
		/// </summary>
		/// <param name="period">The time period: 1d, 1w, 1m.</param>
		/// <returns>List of mod IDs.</returns>
		List<string> GetUpdated(string period);

		/// <summary>
		/// Gets the updated mods together with the provider freshness timestamps returned for the period.
		/// </summary>
		/// <param name="period">The time period: 1d, 1w, 1m.</param>
		/// <returns>The updated repository mod records.</returns>
		List<RepositoryModUpdate> GetUpdatedWithMetadata(string period);

		/// <summary>
		/// Gets the file info for the default file of the specified mod.
		/// </summary>
		/// <param name="modId">The id of the mod the whose default file's metadata is to be retrieved.</param>
		/// <returns>The file info for the default file of the specified mod.</returns>
		IModFileInfo GetDefaultFileInfo(string modId);

		/// <summary>
		/// Gets the Categories array.
		/// </summary>
		/// <returns>The Categories array..</returns>
		List<CategoriesInfo> GetCategories(string gameDomainName);
	}
}
