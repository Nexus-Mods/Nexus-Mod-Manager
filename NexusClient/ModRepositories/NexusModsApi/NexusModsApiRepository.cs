namespace Nexus.Client.ModRepositories.NexusModsApi
{
    using System;
    using System.Collections.Generic;
    using ModManagement;
    using Mods;
    using Util;

    public class NexusModsApiRepository : IModRepository
    {
        #region Events
        
        /// <inheritdoc cref="IModRepository"/>
        public event EventHandler UserStatusUpdate;

        #endregion

        #region Properties

        /// <inheritdoc cref="IModRepository"/>
        public string Id { get; }

        /// <inheritdoc cref="IModRepository"/>
        public string Name { get; }

        /// <inheritdoc cref="IModRepository"/>
        public string[] UserStatus { get; }

        /// <inheritdoc cref="IModRepository"/>
        public string UserAgent { get; }

        /// <inheritdoc cref="IModRepository"/>
        public bool IsOffline { get; }

        /// <inheritdoc cref="IModRepository"/>
        public bool SupportsUnauthenticatedDownload { get; }

        /// <inheritdoc cref="IModRepository"/>
        public List<FileServerZone> FileServerZones { get; }

        /// <inheritdoc cref="IModRepository"/>
        public int AllowedConnections { get; }

        /// <inheritdoc cref="IModRepository"/>
        public int MaxConcurrentDownloads { get; }

        /// <inheritdoc cref="IModRepository"/>
        public string GameModeWebsite { get; }

        /// <inheritdoc cref="IModRepository"/>
        public int RemoteGameId { get; }

        #endregion

        private readonly ApiCallManager _apiCallManager;
        private readonly string _currentGame;

        public NexusModsApiRepository(string currentGame, ApiCallManager apiCallManager)
        {
            _currentGame = currentGame;
            _apiCallManager = apiCallManager;
        }

        /// <inheritdoc cref="IModRepository"/>
        public bool Login(string p_strUsername, string p_strPassword, out Dictionary<string, string> p_dicTokens)
        {
            // TODO: Remove unecessary parameters & functions from IModRepository.
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public bool Login(Dictionary<string, string> p_dicTokens)
        {
            // TODO: Remove unecessary parameters & functions from IModRepository.
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public void Logout()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModInfo GetModInfoForFile(string fileName)
        {
            var hash = Md5.CalculateMd5(fileName);
            return _apiCallManager.SearchForModByMd5(_currentGame, hash)[0].Mod;
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModInfo GetModInfo(string modId)
        {
            return _apiCallManager.SearchForModById(_currentGame, modId);
        }

        /// <inheritdoc cref="IModRepository"/>
        public List<IModInfo> GetModListInfo(List<string> modIdList)
        {
            var result = new List<IModInfo>();

            foreach (var modId in modIdList)
            {
                result.Add(GetModInfo(modId));
            }

            return result;
        }

        /// <inheritdoc cref="IModRepository"/>
        public List<IModInfo> GetFileListInfo(List<string> modFileList)
        {
            var result = new List<IModInfo>();

            foreach (var modId in modFileList)
            {
                result.Add(GetModInfoForFile(modId));
            }

            return result;
        }

        /// <inheritdoc cref="IModRepository"/>
        public bool ToggleEndorsement(string modId, int localState)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IList<IModFileInfo> GetModFileInfo(string p_strModId)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public Uri[] GetFilePartUrls(string p_strModId, string p_strFileId)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public List<FileserverInfo> GetFilePartInfo(string p_strModId, string p_strFileId, string p_strUserLocation,
            out string p_strRepositoryMessage)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModFileInfo GetFileInfo(string p_strModId, string p_strFileId)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModFileInfo GetFileInfoForFile(string p_strFilename)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModFileInfo GetDefaultFileInfo(string p_strModId)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public List<CategoriesInfo> GetCategories(int p_intGameId)
        {
            throw new NotImplementedException();
        }

        #region Deprecated

        /// <inheritdoc cref="IModRepository"/>
        public IList<IModInfo> FindMods(string p_strModNameSearchString, bool p_booIncludeAllTerms)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IList<IModInfo> FindMods(string p_strModNameSearchString, string p_strAuthorSearchString)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IList<IModInfo> FindMods(string p_strModNameSearchString, string p_strModAuthor, bool p_booIncludeAllTerms)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
