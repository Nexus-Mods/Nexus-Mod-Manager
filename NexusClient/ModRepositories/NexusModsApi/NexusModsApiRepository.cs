namespace Nexus.Client.ModRepositories.NexusModsApi
{
    using System;
    using System.Collections.Generic;
    using EndPoints.User;
    using ModManagement;
    using Mods;
    using Util;

    public class NexusModsApiRepository : IModRepository
    {
        #region Events
        
        /// <inheritdoc cref="IModRepository"/>
        public event EventHandler UserStatusUpdate;

        #endregion

        private User _userStatus;

        #region Properties

        /// <inheritdoc cref="IModRepository"/>
        public string Id { get; }

        /// <inheritdoc cref="IModRepository"/>
        public string Name { get; }

        /// <inheritdoc cref="IModRepository"/>
        public User UserStatus
        {
            get => _userStatus;
            private set
            {
                if (_userStatus != value)
                {
                    _userStatus = value;
                    UserStatusUpdate?.Invoke(this, EventArgs.Empty);
                }
                
            }
        }

        /// <summary>
        /// Game Domain E.g. 'skyrim'
        /// </summary>
        public string GameDomainName { get; }

        /// <inheritdoc cref="IModRepository"/>
        public string UserAgent => ApiCallManager.UserAgent;

        /// <inheritdoc cref="IModRepository"/>
        public bool IsOffline => UserStatus == null;

        /// <inheritdoc cref="IModRepository"/>
        public bool SupportsUnauthenticatedDownload => false;

        /// <inheritdoc />
        public List<FileServerZone> FileServerZones { get; private set; }

        /// <inheritdoc cref="IModRepository"/>
        public int AllowedConnections { get; private set; }

        /// <inheritdoc cref="IModRepository"/>
        public int MaxConcurrentDownloads { get; private set; }

        /// <inheritdoc cref="IModRepository"/>
        public string GameModeWebsite { get; }

        /// <inheritdoc cref="IModRepository"/>
        public int RemoteGameId { get; }

        /// <summary>
        /// Gets the repository's file server zones.
        /// </summary>
        /// <value>the repository's file server zones.</value>
        private List<FileServerZone> RepositoryFileServerZones { get; set; }

        #endregion

        private readonly ApiCallManager _apiCallManager;

        public NexusModsApiRepository(string currentGameDomain, ApiCallManager apiCallManager)
        {
            GameDomainName = currentGameDomain;
            _apiCallManager = apiCallManager;
            SetFileServerZones();
        }

        public bool Authenticate(string apiKey)
        {
            UserStatus = _apiCallManager.User.ValidateUser(apiKey);

            if (UserStatus == null)
            {
                AllowedConnections = 1;
                MaxConcurrentDownloads = 5;

                return false;
            }

            AllowedConnections = UserStatus.IsPremium ? 4 : 1;
            MaxConcurrentDownloads = UserStatus.IsPremium ? 10 : 5;

            return true;
        }

        /// <inheritdoc cref="IModRepository"/>
        public void Logout()
        {
            UserStatus = null;
            UserStatusUpdateEvent();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModInfo GetModInfoForFile(string fileName)
        {
            var hash = Md5.CalculateMd5(fileName);

            // TODO: Should probably handle cases with multiple hits.
            return _apiCallManager.Mods.SearchByMd5(GameDomainName, hash)[0].Mod;
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModInfo GetModInfo(string modId)
        {
            return _apiCallManager.Mods.GetById(GameDomainName, modId);
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
        public IList<IModFileInfo> GetModFileInfo(string modId)
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

        private void UserStatusUpdateEvent()
        {
            UserStatusUpdate?.Invoke(this, new EventArgs());
        }

        #region Helpers

        #endregion
        protected void SetFileServerZones()
        {
            FileServerZones = new List<FileServerZone>
            {
                new FileServerZone(),
                new FileServerZone("na.ca", "N.A. - North America Premium", 2, Properties.Resources.us, true),
                new FileServerZone("eu.p1", "E.U. - UK Premium", 1, Properties.Resources.europeanunion, true),
                new FileServerZone("eu.fr", "E.U. - Europe Premium", 2, Properties.Resources.europeanunion, true)
            };

            RepositoryFileServerZones = new List<FileServerZone>
            {
                new FileServerZone(),
                new FileServerZone("na.ca", "N.A. - North America Premium", 2, Properties.Resources.us, true),
                new FileServerZone("eu.p1", "E.U. - UK Premium", 1, Properties.Resources.europeanunion, true),
                new FileServerZone("eu.fr", "E.U. - Europe Premium", 1, Properties.Resources.europeanunion, true)
            };
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
