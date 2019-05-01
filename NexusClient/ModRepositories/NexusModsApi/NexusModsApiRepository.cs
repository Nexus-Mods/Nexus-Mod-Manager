namespace Nexus.Client.ModRepositories.NexusModsApi
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using ModManagement;
    using Mods;
    using Pathoschild.FluentNexus.Models;
    using Util;
    using Util.Collections;

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
        
        public string GameDomainName { get; }

        /// <inheritdoc cref="IModRepository"/>
        public string UserAgent => ApiCallManager.UserAgent;

        /// <inheritdoc cref="IModRepository"/>
        public bool IsOffline => UserStatus == null;

        /// <inheritdoc cref="IModRepository"/>
        public bool SupportsUnauthenticatedDownload => false;

        /// <inheritdoc cref="IModRepository"/>
        public int AllowedConnections { get; private set; }

        /// <inheritdoc cref="IModRepository"/>
        public int MaxConcurrentDownloads { get; private set; }

        /// <inheritdoc cref="IModRepository"/>
        public string GameModeWebsite { get; }

        /// <summary>
        /// Game Domain E.g. 'skyrim'
        /// </summary>
        /// <inheritdoc cref="IModRepository"/>
        public string RemoteGameId { get; }

        #endregion

        private readonly ApiCallManager _apiCallManager;

        public NexusModsApiRepository(string currentGameDomain, ApiCallManager apiCallManager)
        {
            GameDomainName = HandleGameDomainName(currentGameDomain);
            _apiCallManager = apiCallManager;
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
            throw new NotImplementedException("This might not be possible with the new API?");
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
        public List<ModFileDownloadLink> GetFilePartInfo(string modId, string fileId, string key = "", int expiry = -1)
        {
            var mod = Convert.ToInt32(modId);
            var file = Convert.ToInt32(fileId);

            try
            {
                var downloadUris = UserStatus.IsPremium ? 
                    _apiCallManager.ModFiles.GetDownloadLinks(GameDomainName, mod, file).Result : 
                    _apiCallManager.ModFiles.GetDownloadLinks(GameDomainName, mod, file, key, expiry).Result;
                return downloadUris.ToList();
            }
            catch (AggregateException a)
            {
                TraceUtil.TraceAggregateException(a);
                return null;
            }
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
                return null;
            }
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModFileInfo GetFileInfoForFile(string fileName)
        {
            var modId = ParseModIdFromFilename(fileName);

            if (modId == null)
            {
                return null;
            }

            var filename = Path.GetFileName(fileName);
            var files = _apiCallManager.ModFiles.GetModFiles(GameDomainName, Convert.ToInt32(modId), null).Result.Files;
            var fileInfo = files.Find(x => x.Name.Equals(filename, StringComparison.OrdinalIgnoreCase)) ??
                           files.Find(x => x.Name.Replace(' ', '_').Equals(filename, StringComparison.OrdinalIgnoreCase));

            return new ModFileInfo(fileInfo);
        }

        /// <summary>
		/// Parses out the mod id from the given mod file name.
		/// </summary>
		/// <param name="filePath">The filePath from which to parse the mod's id.</param>
		/// <returns>The mod's id, if one was found; null otherwise.</returns>
		protected string ParseModIdFromFilename(string filePath)
        {
            var modIdRegex = new Regex(@"-((\d+)[-\.])+");

            var numberOfDashes = filePath.Count(c => c == '-');

            var filename = Path.GetFileName(filePath);

            Match modId;

            if (numberOfDashes > 3)
            {
                var strCheckName = Path.GetFileName(filePath);
                strCheckName = strCheckName.Substring(strCheckName.IndexOf('-'));
                modId = modIdRegex.Match(strCheckName);

                if (!modId.Success)
                {
                    return null;
                }
            }
            else
            {
                modId = modIdRegex.Match(filename);

                if (!modId.Success)
                {
                    return null;
                }
            }

            IModInfo modInfo = null;
            var filenameWords = filename.Split(new [] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var candidates = new List<KeyValuePair<int, IModInfo>>();

            foreach (Capture match in modId.Groups[2].Captures)
            {
                var id = match.Value;

                //get the mod info to make sure the id is valid, and not
                // just some random match from elsewhere in the filePath
                var infoCandidate = GetModInfo(id);

                if (infoCandidate != null)
                {
                    var files = GetModFileInfo(id);
                    var bestFoundWordCount = 0;
                    var validWordCount = 0;

                    foreach (var mfiFile in files)
                    {
                        if (mfiFile.Filename.Equals(filename, StringComparison.OrdinalIgnoreCase) ||
                            mfiFile.Filename.Replace(' ', '_').Equals(filename, StringComparison.OrdinalIgnoreCase))
                        {
                            modInfo = infoCandidate;
                            modInfo.HumanReadableVersion = mfiFile.HumanReadableVersion;
                            break;
                        }

                        var foundWordCount = 0;

                        foreach (var word in filenameWords)
                        {
                            if (word.Length > 2)
                            {
                                validWordCount++;

                                if (mfiFile.Filename.IndexOf(word, StringComparison.OrdinalIgnoreCase) > -1)
                                {
                                    foundWordCount++;
                                }
                            }
                        }

                        if (foundWordCount > bestFoundWordCount)
                        {
                            bestFoundWordCount = foundWordCount;
                        }
                    }

                    if (modInfo != null)
                    {
                        break;
                    }

                    if (bestFoundWordCount > 0)
                    {
                        var words = validWordCount / 2;

                        if ((filenameWords.Length == 1) || (validWordCount == 1) || (bestFoundWordCount > words))
                        {
                            candidates.Add(new KeyValuePair<int, IModInfo>(bestFoundWordCount, infoCandidate));
                        }
                    }
                }
            }

            if (modInfo == null && !candidates.IsNullOrEmpty())
            {
                candidates.Sort((x, y) => -x.Key.CompareTo(y.Key));
                modInfo = candidates[0].Value;
            }

            return modInfo?.Id;
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModFileInfo GetDefaultFileInfo(string modId)
        {
            var mfiFiles = _apiCallManager.ModFiles.GetModFiles(GameDomainName, Convert.ToInt32(modId), FileCategory.Main).Result.Files;

            var mfiDefault = (from f in mfiFiles
                                 orderby f.UploadedTimestamp descending
                                 select f).FirstOrDefault() ?? (from f in mfiFiles
                                 orderby f.UploadedTimestamp descending
                                 select f).FirstOrDefault();

            return new ModFileInfo(mfiDefault);
        }

        /// <inheritdoc cref="IModRepository"/>
        public List<CategoriesInfo> GetCategories(string gameId)
        {
            try
            {
                var categories = _apiCallManager.Games.GetGame(gameId).Result.Categories;
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

        #region Deprecated

        /// <inheritdoc cref="IModRepository"/>
        public IList<IModInfo> FindMods(string modNameSearchString, bool includeAllTerms)
        {
            throw new NotImplementedException("This might not be possible with the new API?");
        }

        /// <inheritdoc cref="IModRepository"/>
        public IList<IModInfo> FindMods(string modNameSearchString, string authorSearchString)
        {
            throw new NotImplementedException("This might not be possible with the new API?");
        }

        /// <inheritdoc cref="IModRepository"/>
        public IList<IModInfo> FindMods(string modNameSearchString, string modAuthor, bool includeAllTerms)
        {
            throw new NotImplementedException("This might not be possible with the new API?");
        }

        #endregion
    }
}
