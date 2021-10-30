namespace Nexus.Client.ModRepositories
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Net.Http;
	using System.Text.RegularExpressions;
	using System.Threading;
	using System.Threading.Tasks;
	using ModManagement;
	using Mods;
	using Pathoschild.FluentNexus;
	using Pathoschild.FluentNexus.Models;
	using Util;
	using Util.Collections;

	public class NexusModsApiRepository : IModRepository
	{
		private User _userStatus;

		/// <inheritdoc cref="IModRepository"/>
		public event EventHandler UserStatusUpdate;

		/// <inheritdoc cref="IModRepository"/>
		public event EventHandler<RateLimitExceededArgs> RateLimitExceeded;

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

		private readonly string _gameDomain;

		/// <inheritdoc cref="IModRepository"/>
		public string GameDomainName => string.IsNullOrEmpty(_gameDomain) ? string.Empty : _gameDomain.ToLower();

        /// <inheritdoc cref="IModRepository"/>
		public IRateLimitManager RateLimit => _apiCallManager.RateLimit;

		#endregion

		private readonly ApiCallManager _apiCallManager;

		/// <summary>
		/// Creates a new instance of the <see cref="NexusModsApiRepository"/>.
		/// </summary>
		/// <param name="currentGameDomain">Currently selected game.</param>
		/// <param name="apiCallManager"><see cref="ApiCallManager"/> to use for API calls.</param>
		public NexusModsApiRepository(string currentGameDomain, ApiCallManager apiCallManager)
		{
			_gameDomain = GameDomainTranslator.DetermineGameDomain(currentGameDomain);
			_apiCallManager = apiCallManager;
		}

		/// <inheritdoc />
		public AuthenticationStatus Authenticate()
		{
			_apiCallManager.UpdateNexusClient();

			var status = AuthenticationStatus.Unknown;

			try
			{
				UserStatus = _apiCallManager.Users?.ValidateAsync().Result;
			}
			catch (AggregateException a)
			{
				Trace.TraceError("Error encountered while validating API key.");
				TraceUtil.TraceAggregateException(a);

				if (a.InnerExceptions.Any(ex => ex.GetType() == typeof(HttpRequestException)))
				{
					status = AuthenticationStatus.NetworkError;
				}
				else if (a.InnerExceptions.Any(ex => ex.Message.Contains("Please provide a valid API Key")))
				{
					status = AuthenticationStatus.InvalidKey;
				}
			}

			if (UserStatus == null)
			{
				AllowedConnections = 1;
				MaxConcurrentDownloads = 5;

				return status;
			}

			AllowedConnections = UserStatus.IsPremium ? 2 : 1;
			MaxConcurrentDownloads = UserStatus.IsPremium ? 10 : 5;

			return AuthenticationStatus.Successful;
		}

		/// <inheritdoc cref="IModRepository"/>
		public void Logout()
		{
			UserStatus = null;
			_apiCallManager.ClearApiKey();
			UserStatusUpdate?.Invoke(this, new EventArgs());
		}

		/// <inheritdoc cref="IModRepository"/>
		public IModInfo GetModInfoForFile(string fileName)
		{
            try
            {
                var hash = Md5.CalculateMd5(fileName);

                // TODO: Probably need to handle cases with multiple hits.
                var mod = _apiCallManager.Mods?.GetModsByFileHash(GameDomainName, hash)?.Result[0]?.Mod;

                return mod == null ? new ModInfo() : new ModInfo(mod);
            }
            catch (AggregateException a)
            {
                ReactToAggregateException(a);
                return null;
            }
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
                return null;
            }
		}

        /// <inheritdoc cref="IModRepository"/>
        public IModFileInfo GetModFileInfoForFile(string fileName)
        {
            try
            {
                var hash = Md5.CalculateMd5(fileName);

                // TODO: Probably need to handle cases with multiple hits.
                return new ModFileInfo(_apiCallManager.Mods?.GetModsByFileHash(GameDomainName, hash).Result[0].File);
            }
            catch (AggregateException a)
            {
                ReactToAggregateException(a);
                return null;
            }
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
                return null;
            }
        }

        /// <inheritdoc cref="IModRepository"/>
        public IModInfo GetModInfo(string modId)
		{
			try
			{
				string id = ParseModId(modId);
				return new ModInfo(_apiCallManager.Mods?.GetMod(GameDomainName, Convert.ToInt32(id)).Result);
			}
			catch (AggregateException a)
			{
				ReactToAggregateException(a);
				return null;
			}
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
                return null;
            }
		}

		/// <inheritdoc cref="IModRepository"/>
		public List<IModInfo> GetFileListInfo(List<string> modFileList)
		{
			var list = new List<IModInfo>();
			int modRequests = 0;

			foreach (var mod in modFileList)
			{
                try
                {
                    string modId = ParseModId(mod);
					string downloadId = ParseDownloadId(mod);
                    int mid = Convert.ToInt32(modId);
					string newFileName = string.Empty;

					if (modRequests <= 10)
						Task.Delay(50);
					else
					{
						modRequests = 1;
						Task.Delay(250);
					}
                    var tmpMod = _apiCallManager.Mods?.GetMod(GameDomainName, mid).Result;

                    if (tmpMod == null)
                    {
						list.Add(new ModInfo());
                        continue;
                    }

					if (!string.IsNullOrEmpty(downloadId) && !downloadId.Equals("0"))
					{
						int did = Convert.ToInt32(downloadId);
						Task.Delay(50);
						var tmpModFile = _apiCallManager.ModFiles?.GetModFiles(GameDomainName, mid, new FileCategory[0]).Result;

						int newFileId = 0;
						int tempNewFileId = did;

						if (tmpModFile != null)
						{
							while (newFileId == 0)
							{
								var fileUpdate = tmpModFile.FileUpdates.Where(u => u.OldFileID == tempNewFileId).FirstOrDefault();

								if (fileUpdate != null)
									tempNewFileId = fileUpdate.NewFileID;
								else
									newFileId = tempNewFileId;
							}
						}

						if (newFileId != did)
						{
							Task.Delay(50);
							var newModFile = _apiCallManager.ModFiles?.GetModFile(GameDomainName, mid, newFileId).Result;

							if (newModFile != null)
							{
								tmpMod.Version = newModFile.FileVersion;
								newFileName = newModFile.FileName;
							}
						}
					}

					ModInfo modInfo = new ModInfo(tmpMod);
					if (!string.IsNullOrEmpty(newFileName))
						modInfo.FileName = newFileName;

                    list.Add(modInfo);
                }
                catch (AggregateException a)
                {
					
					list.Add(new ModInfo());
					if (ReactToAggregateException(a))
					{
						// Breaking the foreach will cause the updated list and the base list to lose their alignment.
						break;
					}
					else
						continue;
				}
                catch (Exception ex)
                {
                    Trace.TraceError($"Exception while parsing mod ID from mod \"{mod}\".");
                    TraceUtil.TraceException(ex);
					list.Add(new ModInfo());
                    continue;
                }
			}

			return list;
		}

		/// <inheritdoc cref="IModRepository"/>
		public List<string> GetUpdated(string period)
		{
			List<string> updatedMods = new List<string>();

			try
			{
				ModUpdate[] updates = _apiCallManager.Mods.GetUpdated(GameDomainName, period).Result;
				if (updates.Length > 0)
					updatedMods = updates.Select(x => x.ModID.ToString()).ToList();
			}
			catch (AggregateException a)
			{
				ReactToAggregateException(a);
			}
			catch (Exception ex)
			{
				TraceUtil.TraceException(ex);
			}

			return updatedMods;
		}

		/// <inheritdoc cref="IModRepository"/>
		public bool? ToggleEndorsement(string modId, int localState, string version)
		{
			var id = Convert.ToInt32(modId);
			var localStateAfterCompletion = localState != 1;

			try
			{
				Task action = null;

				switch (localState)
				{
					case -1:
					case 0:
						// -1 is abstained, 0 is null. Toggling these states will endorse the mod.
						action = _apiCallManager.Mods?.Endorse(GameDomainName, id, version);
						break;
					case 1:
						// 1 is endorsed, toggling this state will abstain from endorsing the mod.
						action = _apiCallManager.Mods?.Unendorse(GameDomainName, id, version);
						break;
				}

				var timeout = 5000;

				while (!action.IsCompleted)
				{
					Thread.Sleep(250);
					timeout -= 250;

					if (timeout <= 0)
					{
						Trace.TraceError("Timed out waiting for endorsement toggle to complete.");
						return null;
					}
				}

				if (action.Status != TaskStatus.Faulted)
				{
					// We'll trust that if nothing went wrong we can figure out the new state.
					return localStateAfterCompletion;
				}

				if (ReactToAggregateException(action.Exception))
				{
					return !localStateAfterCompletion;
				}

				Trace.TraceError($"Endorsement Toggle for mod {modId}, result: {action.Status}");
				return null;
			}
			catch (AggregateException a)
			{
				ReactToAggregateException(a);
				return null;
			}
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
                return null;
            }
		}

		/// <inheritdoc cref="IModRepository"/>
		public IList<IModFileInfo> GetModFileInfo(string modId)
		{
			try
			{
				var modFiles = _apiCallManager.ModFiles?.GetModFiles(GameDomainName, Convert.ToInt32(modId), FileCategory.Main, FileCategory.Miscellaneous, FileCategory.Optional, FileCategory.Update, FileCategory.Deleted, FileCategory.Old).Result.Files;
				return modFiles.Select(modFileInfo => new ModFileInfo(modFileInfo)).Cast<IModFileInfo>().ToList();
			}
			catch (AggregateException a)
			{
				ReactToAggregateException(a);
				return null;
			}
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
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
					_apiCallManager.ModFiles?.GetDownloadLinks(GameDomainName, mod, file).Result :
					_apiCallManager.ModFiles?.GetDownloadLinks(GameDomainName, mod, file, key, expiry).Result;
				return downloadUris.ToList();
			}
			catch (AggregateException a)
			{
				ReactToAggregateException(a);
				return null;
			}
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
                return null;
            }
		}

		/// <inheritdoc cref="IModRepository"/>
		public IModFileInfo GetFileInfo(string modId, string fileId)
		{
			try
			{
				return new ModFileInfo(_apiCallManager.ModFiles?.GetModFile(GameDomainName, Convert.ToInt32(modId), Convert.ToInt32(fileId)).Result);
			}
			catch (AggregateException a)
			{
				ReactToAggregateException(a);
				return null;
			}
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
                return null;
            }
		}

		/// <inheritdoc cref="IModRepository"/>
		public IModFileInfo GetFileInfoForFile(string fileName)
		{
			try
			{
				var modId = ParseModIdFromFilename(fileName);

				if (modId == null)
				{
					return null;
				}

				var filename = Path.GetFileName(fileName);
				var files = _apiCallManager.ModFiles?.GetModFiles(GameDomainName, Convert.ToInt32(modId), FileCategory.Main, FileCategory.Miscellaneous, FileCategory.Optional, FileCategory.Update, FileCategory.Deleted, FileCategory.Old).Result.Files;
                var fileInfo = (files.Find(x => x.Name.Equals(filename, StringComparison.OrdinalIgnoreCase)) ?? 
                               files.Find(x => x.Name.Replace(' ', '_').Equals(filename, StringComparison.OrdinalIgnoreCase))) ??
                               files.Find(x => x.Name.Replace(' ', '-').Equals(filename, StringComparison.OrdinalIgnoreCase));

                return new ModFileInfo(fileInfo);
			}
			catch (AggregateException a)
			{
				ReactToAggregateException(a);
				return null;
			}
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
                return null;
            }
		}

		/// <summary>
		/// Parses out the mod id from the given mod file name.
		/// </summary>
		/// <param name="filePath">The filePath from which to parse the mod's id.</param>
		/// <returns>The mod's id, if one was found; null otherwise.</returns>
		private string ParseModIdFromFilename(string filePath)
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
			var filenameWords = filename.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
			var candidates = new List<KeyValuePair<int, IModInfo>>();

			foreach (Capture match in modId.Groups[2].Captures)
			{
				var id = match.Value;

				// get the mod info to make sure the id is valid, and not
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
			try
			{
				var mfiFiles = _apiCallManager.ModFiles?.GetModFiles(GameDomainName, Convert.ToInt32(modId), FileCategory.Main).Result.Files;

				var mfiDefault = (from f in mfiFiles
								  orderby f.UploadedTimestamp descending
								  select f).FirstOrDefault() ?? (from f in mfiFiles
																 orderby f.UploadedTimestamp descending
																 select f).FirstOrDefault();

				return new ModFileInfo(mfiDefault);
			}
			catch (AggregateException a)
			{
				ReactToAggregateException(a);
				return null;
			}
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
                return null;
            }
		}

		/// <inheritdoc cref="IModRepository"/>
		public List<CategoriesInfo> GetCategories(string gameId)
		{
			try
			{
				var categories = _apiCallManager.Games?.GetGame(gameId).Result.Categories;
				return categories.Select(category => new CategoriesInfo(category)).ToList();
			}
			catch (AggregateException a)
			{
				ReactToAggregateException(a);
				return null;
			}
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
                return null;
            }
		}

		/// <summary>
		/// Checks and reacts to contents of an AggregateException.
		/// </summary>
		/// <param name="a">AggregateException to react to.</param>
		/// <returns>A value indicating whether or not the rate limit has been exceeded.</returns>
		private bool ReactToAggregateException(AggregateException a)
		{
			TraceUtil.TraceAggregateException(a);

			if (a.InnerExceptions.Any(ex => ex.Message.Contains("Too Many Requests") || (a.InnerExceptions.Count > 0 && ((Pathoschild.Http.Client.ApiException)a.InnerException).Status == System.Net.HttpStatusCode.Forbidden && ((Pathoschild.Http.Client.ApiException)a.InnerException).Message.IndexOf("Mod not available", StringComparison.OrdinalIgnoreCase) < 0)))
			{
				RateLimitExceeded?.Invoke(this, new RateLimitExceededArgs(RateLimit));
				return true;
			}

			return false;
		}

		/// <summary>
		/// Catch'em all failsafe to try and avoid idiotic crashes when the modId is borked.
		/// </summary>
		/// <param name="modSearchString"></param>
		/// <returns></returns>
		private string ParseModId(string modSearchString)
		{
			string parsedId = "0";

			if (!string.IsNullOrEmpty(modSearchString))
			{
				var modInfo = modSearchString.Split('|');
				parsedId = Regex.Replace(modInfo.Length == 1 ? modInfo[0] : modInfo[1], "[^0-9]", "");
			}

			return parsedId;
		}

		/// <summary>
		/// Catch'em all failsafe to try and avoid idiotic crashes when the modId is borked.
		/// </summary>
		/// <param name="modSearchString"></param>
		/// <returns></returns>
		private string ParseDownloadId(string modSearchString)
		{
			string parsedId = "0";

			if (!string.IsNullOrEmpty(modSearchString))
			{
				var modInfo = modSearchString.Split('|');
				parsedId = Regex.Replace(modInfo.Length == 1 ? modInfo[0] : modInfo[2], "[^0-9]", "");
			}

			return parsedId;
		}
	}
}
