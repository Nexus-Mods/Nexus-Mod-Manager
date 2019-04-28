namespace Nexus.Client.ModRepositories.NexusModsApi
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using ModManagement;
    using Mods;
    using Pathoschild.FluentNexus.Models;
    using Pathoschild.Http.Client;
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
        public string Id => "Nexus";

        /// <inheritdoc cref="IModRepository"/>
        public string Name => "Nexus";

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
            GameDomainName = HandleGameDomainName(currentGameDomain);
            _apiCallManager = apiCallManager;
            SetFileServerZones();
        }

        /// <summary>
        /// Due to the hacky implementation of SkyrimVR and Fallout4VR, these domains need to be corrected.
        /// </summary>
        /// <param name="currentGameDomain">The input Current Game Domain.</param>
        /// <returns>The corrected game domain name, if applicable.</returns>
        private string HandleGameDomainName(string currentGameDomain)
        {
            if (currentGameDomain.Equals("fallout4vr", StringComparison.OrdinalIgnoreCase))
            {
                return "fallout4";
            }

            if (currentGameDomain.Equals("skyrimvr", StringComparison.OrdinalIgnoreCase))
            {
                return "skyrim";
            }

            return currentGameDomain;
        }
        
        /// <inheritdoc />
        public bool Authenticate()
        {
            _apiCallManager.UpdateNexusClient();

            try
            {
                UserStatus = _apiCallManager.Users.ValidateAsync().Result;
            }
            catch (AggregateException a)
            {
                Trace.TraceError("Error encountered while validating API key.");
                TraceUtil.TraceAggregateException(a);
            }

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
            _apiCallManager.ClearApiKey();
            UserStatusUpdateEvent();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModInfo GetModInfoForFile(string fileName)
        {
            try
            {
                var hash = Md5.CalculateMd5(fileName);
                
                // TODO: Probably need to handle cases with multiple hits.
                return new ModInfo(_apiCallManager.Mods.GetModsByFileHash(GameDomainName, hash).Result[0].Mod);
            }
            catch (AggregateException a)
            {
                TraceUtil.TraceAggregateException(a);
                return null;
            }
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModInfo GetModInfo(string modId)
        {
            try
            {
                return new ModInfo(_apiCallManager.Mods.GetMod(GameDomainName, Convert.ToInt32(modId)).Result);
            }
            catch (AggregateException a)
            {
                TraceUtil.TraceAggregateException(a);
                return null;
            }
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
            var id = Convert.ToInt32(modId);

            switch (localState)
            {
                case -1:
                    _apiCallManager.Mods.Unendorse(GameDomainName, id, "whatVersion?").RunSynchronously();
                    break;
                case 1:
                    _apiCallManager.Mods.Endorse(GameDomainName, id, "whatVersion?").RunSynchronously();
                    break;
            }

            // TODO: Can we get this state from the Endorse/Unendorse calls above?
            return _apiCallManager.Mods.GetMod(GameDomainName, id).Result.Endorsement.EndorseStatus.Equals(EndorsementStatus.Endorsed);
        }

        /// <inheritdoc cref="IModRepository"/>
        public IList<IModFileInfo> GetModFileInfo(string modId)
        {
            try
            {
                var modFiles = _apiCallManager.ModFiles.GetModFiles(GameDomainName, Convert.ToInt32(modId), null).Result.Files;
                return modFiles.Select(modFileInfo => new ModFileInfo(modFileInfo)).Cast<IModFileInfo>().ToList();
            }
            catch (AggregateException a)
            {
                TraceUtil.TraceAggregateException(a);
                return null;
            }
        }

        /// <inheritdoc cref="IModRepository"/>
        public Uri[] GetFilePartUrls(string modId, string fileId)
        {
            try
            {
                // TODO: Does NMM ever need to provide key/expiry for non-premium downloads like this?
                var urls = _apiCallManager.ModFiles.GetDownloadLinks(GameDomainName, Convert.ToInt32(modId), Convert.ToInt32(fileId)).Result;
                return urls.Select(url => url.Uri).ToArray();
            }
            catch (AggregateException a)
            {
                TraceUtil.TraceAggregateException(a);
                return null;
            }
        }

        /// <inheritdoc cref="IModRepository"/>
        public List<FileserverInfo> GetFilePartInfo(string modId, string fileId, string userLocation, out string repositoryMessage)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModFileInfo GetFileInfo(string modId, string fileId)
        {
            try
            {
                return new ModFileInfo(_apiCallManager.ModFiles.GetModFile(GameDomainName, Convert.ToInt32(modId), Convert.ToInt32(fileId)).Result);
            }
            catch (AggregateException e)
            {
                TraceUtil.TraceAggregateException(e);
                throw;
            }
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModFileInfo GetFileInfoForFile(string fileName)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModFileInfo GetDefaultFileInfo(string modId)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public List<CategoriesInfo> GetCategories(int gameId)
        {
            try
            {
                var categories = _apiCallManager.Games.GetGame(GameDomainName).Result.Categories;
                return categories.Select(category => new CategoriesInfo(category)).ToList();
            }
            catch (AggregateException a)
            {
                TraceUtil.TraceAggregateException(a);
                return null;
            }
        }

        private void UserStatusUpdateEvent()
        {
            UserStatusUpdate?.Invoke(this, new EventArgs());
        }

        #region Helpers

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

        #endregion

        #region Deprecated

        /// <inheritdoc cref="IModRepository"/>
        public IList<IModInfo> FindMods(string modNameSearchString, bool includeAllTerms)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IList<IModInfo> FindMods(string modNameSearchString, string authorSearchString)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc cref="IModRepository"/>
        public IList<IModInfo> FindMods(string modNameSearchString, string modAuthor, bool includeAllTerms)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
