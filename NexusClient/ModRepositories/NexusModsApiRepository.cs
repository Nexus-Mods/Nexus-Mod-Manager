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

		private enum ModHashLookupStatus
		{
			Match,
			NoMatch,
			CannotHash,
			RequestFailed,
			RateLimitExceeded
		}

		private sealed class ModHashLookupOutcome
		{
			public ModHashLookupOutcome(
			 ModHashLookupStatus status,
			 ModHashResult result = null)
			{
				Status = status;
				Result = result;
			}

			public ModHashLookupStatus Status { get; }

			public ModHashResult Result { get; }
		}

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
		/// <inheritdoc cref="IModRepository"/>
		public IModInfo GetModInfoForFile(string fileName)
		{
			try
			{
				var hashLookup =
				 GetModHashLookupForFile(fileName);

				if (hashLookup.Status ==
				  ModHashLookupStatus.Match &&
				 hashLookup.Result?.Mod != null)
				{
					var hashModInfo =
					 CreateModInfoFromHashResult(
					  hashLookup.Result);

					TraceModRecognition(
					 fileName,
					 "MD5",
					 "Match",
					 hashModInfo);

					return hashModInfo;
				}

				/*
				 * Do not issue additional Nexus API calls after
				 * the request limit has been exceeded.
				 */
				if (hashLookup.Status ==
				 ModHashLookupStatus.RateLimitExceeded)
				{
					TraceModRecognition(
					 fileName,
					 "MD5",
					 "RateLimitExceeded",
					 null);

					return null;
				}

				/*
				 * These conditions proceed to legacy recognition:
				 *
				 * - Nexus returned no MD5 match.
				 * - The archive does not exist or cannot be read.
				 * - The MD5 endpoint failed without reporting a
				 *   rate-limit condition.
				 */
				if (string.IsNullOrWhiteSpace(fileName))
				{
					TraceModRecognition(
					 fileName,
					 "FilenameFallback",
					 "InvalidFilename",
					 null);

					return null;
				}

				var parsedModId =
				 ParseModIdFromFilename(fileName);

				if (string.IsNullOrEmpty(parsedModId))
				{
					TraceModRecognition(
					 fileName,
					 "FilenameFallback",
					 hashLookup.Status.ToString(),
					 null);

					return null;
				}

				var parsedModInfo =
				 GetModInfo(parsedModId);

				if (parsedModInfo == null)
				{
					TraceModRecognition(
					 fileName,
					 "FilenameFallback",
					 "ModNotFound",
					 null);

					return null;
				}

				/*
				 * This helper performs filename matching only.
				 * It does not calculate or search the MD5 again.
				 */
				var parsedFileInfo =
				 GetFileInfoByFilename(
				  fileName,
				  parsedModId);

				IModInfo result;
				if (parsedFileInfo == null)
				{
					var unresolvedFileInfo = new ModInfo(parsedModInfo)
					{
						HumanReadableVersion = null,
						LastKnownVersion = null,
						MachineVersion = null
					};
					result = unresolvedFileInfo;
				}
				else
				{
					result = AutoTagger.CombineInfo(
					 parsedModInfo,
					 parsedFileInfo);
				}

				TraceModRecognition(
				 fileName,
				 "FilenameFallback",
				 parsedFileInfo == null
				  ? "ModMatch"
				  : "ModAndFileMatch",
				 result);

				return result;
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
			var hashLookup =
			 GetModHashLookupForFile(fileName);

			return hashLookup.Status ==
			  ModHashLookupStatus.Match &&
			 hashLookup.Result?.File != null
			  ? new ModFileInfo(
			   hashLookup.Result.File)
			  : null;
		}

		private ModHashLookupOutcome GetModHashLookupForFile(string fileName)
		{
			if (string.IsNullOrWhiteSpace(fileName) ||
			 !File.Exists(fileName))
			{
				return new ModHashLookupOutcome(
				 ModHashLookupStatus.CannotHash);
			}

			string hash;

			try
			{
				hash = Md5.CalculateMd5(fileName);
			}
			catch (Exception ex)
			{
				Trace.TraceWarning(
				 "Could not calculate MD5 for mod " +
				 "archive \"{0}\".",
				 GetSafeFileName(fileName));

				TraceUtil.TraceException(ex);

				return new ModHashLookupOutcome(
				 ModHashLookupStatus.CannotHash);
			}

			try
			{
				var hashResults =
				 _apiCallManager.Mods?
				  .GetModsByFileHash(
				   GameDomainName,
				   hash)?
				  .Result;

				var hashResult =
				 hashResults?
				  .FirstOrDefault(
				   result =>
					result?.Mod != null ||
					result?.File != null);

				return hashResult == null
				 ? new ModHashLookupOutcome(
				  ModHashLookupStatus.NoMatch)
				 : new ModHashLookupOutcome(
				  ModHashLookupStatus.Match,
				  hashResult);
			}
			catch (AggregateException a)
			{
				TraceUtil.TraceAggregateException(a);

				if (IsRateLimitException(a))
				{
					RateLimitExceeded?.Invoke(
					 this,
					 new RateLimitExceededArgs(
					  RateLimit));

					return new ModHashLookupOutcome(
					 ModHashLookupStatus
					  .RateLimitExceeded);
				}

				/*
				 * This can represent an isolated failure of the
				 * MD5 endpoint. GetModInfoForFile will continue
				 * with filename/ID recognition.
				 */
				return new ModHashLookupOutcome(
				 ModHashLookupStatus.RequestFailed);
			}
			catch (Exception ex)
			{
				TraceUtil.TraceException(ex);

				return new ModHashLookupOutcome(
				 ModHashLookupStatus.RequestFailed);
			}
		}

		private static bool IsRateLimitException(AggregateException exception)
		{
			if (exception == null)
			{
				return false;
			}

			return exception
			 .Flatten()
			 .InnerExceptions
			 .Any(
			  innerException =>
			  {
				  if (innerException == null)
				  {
					  return false;
				  }

				  if (innerException.Message.IndexOf(
		 "Too Many Requests",
		 StringComparison
		  .OrdinalIgnoreCase) >= 0)
				  {
					  return true;
				  }

				  var apiException =
		innerException as
		 Pathoschild.Http.Client
		  .ApiException;

				  return apiException != null &&
		apiException.Status ==
		 System.Net.HttpStatusCode
		  .Forbidden &&
		apiException.Message.IndexOf(
		 "Mod not available",
		 StringComparison
		  .OrdinalIgnoreCase) < 0;
			  });
		}

		private static IModInfo CreateModInfoFromHashResult(ModHashResult hashResult)
		{
			if (hashResult?.Mod == null)
			{
				return null;
			}

			var modInfo =
			 new ModInfo(hashResult.Mod);

			var fileInfo = hashResult.File == null
			 ? null
			 : new ModFileInfo(hashResult.File);

			if (fileInfo == null)
			{
				modInfo.HumanReadableVersion = null;
				modInfo.LastKnownVersion = null;
				modInfo.MachineVersion = null;
				return modInfo;
			}

			/*
			 * This produces the same combined information the
			 * previous AutoTagger path produced, without making
			 * another MD5 request.
			 */
			return AutoTagger.CombineInfo(
			 modInfo,
			 fileInfo);
		}

		private static void TraceModRecognition(string fileName, string source, string status, IModInfo modInfo)
		{
			Trace.TraceInformation(
			 "Get Mod Info: filename=\"{0}\", " +
			 "source={1}, status={2}, " +
			 "modId={3}, fileId={4}",
			 GetSafeFileName(fileName),
			 source ?? "unknown",
			 status ?? "unknown",
			 modInfo?.Id ?? "unknown",
			 modInfo?.DownloadId ?? "unknown");
		}

		private static string GetSafeFileName(string fileName)
		{
			try
			{
				return Path.GetFileName(fileName) ??
				 string.Empty;
			}
			catch (Exception)
			{
				return string.Empty;
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
					string currentFilename = ParseFilename(mod);
					int numericModId = Convert.ToInt32(modId);

					if (modRequests <= 10)
						Task.Delay(50);
					else
					{
						modRequests = 1;
						Task.Delay(250);
					}

					modRequests++;
					var nexusMod = _apiCallManager.Mods?.GetMod(GameDomainName, numericModId).Result;

					if (nexusMod == null)
					{
						list.Add(new ModInfo());
						continue;
					}

					ModInfo modInfo = new ModInfo(nexusMod);
					bool fileMetadataResolved = false;
					Task.Delay(50);
					var nexusFiles = _apiCallManager.ModFiles?.GetModFiles(GameDomainName, numericModId, new FileCategory[0]).Result;
					int currentFileId = 0;

					if (ModFileIdentity.IsUsableRepositoryId(downloadId))
					{
						Int32.TryParse(downloadId, out currentFileId);
					}
					else if (!string.IsNullOrWhiteSpace(currentFilename) && nexusFiles?.Files != null)
					{
						var currentFile = nexusFiles.Files.FirstOrDefault(file =>
							string.Equals(file.FileName, currentFilename, StringComparison.OrdinalIgnoreCase));

						if (currentFile != null)
							currentFileId = currentFile.FileID;
						else if (nexusFiles.FileUpdates != null)
						{
							var filenameUpdate = nexusFiles.FileUpdates.FirstOrDefault(update =>
								string.Equals(update.OldFileName, currentFilename, StringComparison.OrdinalIgnoreCase)
								|| string.Equals(update.NewFileName, currentFilename, StringComparison.OrdinalIgnoreCase));

							if (filenameUpdate != null)
							{
								currentFileId = string.Equals(filenameUpdate.OldFileName, currentFilename, StringComparison.OrdinalIgnoreCase)
									? filenameUpdate.OldFileID
									: filenameUpdate.NewFileID;
							}
						}
					}

					int latestFileId = currentFileId;
					if (latestFileId > 0 && nexusFiles?.FileUpdates != null)
					{
						var visitedFileIds = new HashSet<int>();
						while (visitedFileIds.Add(latestFileId))
						{
							var fileUpdate = nexusFiles.FileUpdates.FirstOrDefault(update => update.OldFileID == latestFileId);
							if (fileUpdate == null || fileUpdate.NewFileID <= 0 || fileUpdate.NewFileID == latestFileId)
								break;

							latestFileId = fileUpdate.NewFileID;
						}
					}

					if (latestFileId > 0)
					{
						var latestFile = nexusFiles?.Files?.FirstOrDefault(file => file.FileID == latestFileId);
						if (latestFile == null)
						{
							Task.Delay(50);
							latestFile = _apiCallManager.ModFiles?.GetModFile(GameDomainName, numericModId, latestFileId).Result;
						}

						if (latestFile != null)
						{
							modInfo = new ModInfo(AutoTagger.CombineInfo(modInfo, new ModFileInfo(latestFile)));
							fileMetadataResolved = true;
						}
						else
						{
							modInfo.DownloadId = latestFileId.ToString();
						}
					}

					if (!fileMetadataResolved)
					{
						modInfo.HumanReadableVersion = null;
						modInfo.LastKnownVersion = null;
						modInfo.MachineVersion = null;
					}

					if (string.IsNullOrWhiteSpace(modInfo.FileName) && !string.IsNullOrWhiteSpace(currentFilename))
						modInfo.FileName = currentFilename;

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
				}
				catch (Exception ex)
				{
					Trace.TraceError($"Exception while parsing mod ID from mod \"{mod}\".");
					TraceUtil.TraceException(ex);
					list.Add(new ModInfo());
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
		public async Task<bool?> ToggleEndorsement(string modId, int localState, string version)
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
					await Task.Delay(250);
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
				var modFile = _apiCallManager.ModFiles?.GetModFile(GameDomainName, Convert.ToInt32(modId), Convert.ToInt32(fileId)).Result;
				return modFile == null ? null : new ModFileInfo(modFile);
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
		/// <inheritdoc cref="IModRepository"/>
		public IModFileInfo GetFileInfoForFile(string fileName)
		{
			try
			{
				var hashLookup =
				 GetModHashLookupForFile(fileName);

				if (hashLookup.Status ==
				  ModHashLookupStatus.Match &&
				 hashLookup.Result?.File != null)
				{
					return new ModFileInfo(
					 hashLookup.Result.File);
				}

				if (hashLookup.Status ==
				  ModHashLookupStatus
				   .RateLimitExceeded ||
				 string.IsNullOrWhiteSpace(fileName))
				{
					return null;
				}

				var modId =
				 ParseModIdFromFilename(fileName);

				/*
				 * This call does not calculate the MD5 again.
				 */
				return GetFileInfoByFilename(
				 fileName,
				 modId);
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

		private IModFileInfo GetFileInfoByFilename(string fileName, string modId)
		{
			if (string.IsNullOrWhiteSpace(fileName) ||
			 string.IsNullOrWhiteSpace(modId))
			{
				return null;
			}

			var filename =
			 Path.GetFileName(fileName);

			if (string.IsNullOrWhiteSpace(filename))
			{
				return null;
			}

			/*
			 * Keep Old and Deleted in the requested categories.
			 * This allows legacy matching for files that Nexus
			 * still exposes but no longer lists as active files.
			 */
			var modFilesResult =
			 _apiCallManager.ModFiles?
			  .GetModFiles(
			   GameDomainName,
			   Convert.ToInt32(modId),
			   FileCategory.Main,
			   FileCategory.Miscellaneous,
			   FileCategory.Optional,
			   FileCategory.Update,
			   FileCategory.Deleted,
			   FileCategory.Old)
			  .Result;

			var files = modFilesResult?.Files;

			if (files == null)
			{
				return null;
			}

			var fileInfo =
			 files.Find(
			  file => string.Equals(
			   file.FileName,
			   filename,
			   StringComparison
				.OrdinalIgnoreCase)) ??

			 files.Find(
			  file => string.Equals(
			   file.Name,
			   filename,
			   StringComparison
				.OrdinalIgnoreCase)) ??

			 files.Find(
			  file => string.Equals(
			   file.Name?.Replace(
				' ',
				'_'),
			   filename,
			   StringComparison
				.OrdinalIgnoreCase)) ??

			 files.Find(
			  file => string.Equals(
			   file.Name?.Replace(
				' ',
				'-'),
			   filename,
			   StringComparison
				.OrdinalIgnoreCase));

			/*
			 * The old code returned new ModFileInfo(null),
			 * which produced a non-null object containing only
			 * null properties. Return a real null on no match.
			 */
			return fileInfo == null
			 ? null
			 : new ModFileInfo(fileInfo);
		}

		/// <summary>
		/// Parses out the mod id from the given mod file name.
		/// </summary>
		/// <param name="filePath">The filePath from which to parse the mod's id.</param>
		/// <returns>The mod's id, if one was found; null otherwise.</returns>
		private string ParseModIdFromFilename(string filePath)
		{
			var filename = Path.GetFileName(filePath);
			string newNexusModId;
			if (TryParseNewNexusArchiveModIdFromFilename(filePath, out newNexusModId))
			{
				var newNexusModInfo = GetModInfo(newNexusModId);

				if (newNexusModInfo != null)
				{
					return newNexusModInfo.Id;
				}
			}

			IModInfo modInfo = null;
			var filenameWords = filename.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
			var candidates = new List<KeyValuePair<int, IModInfo>>();

			foreach (var id in GetModIdCandidatesFromFilename(filePath))
			{
				// get the mod info to make sure the id is valid, and not
				// just some random match from elsewhere in the filePath
				var infoCandidate = GetModInfo(id);

				if (infoCandidate != null)
				{
					var files = GetModFileInfo(id);

					if (files == null)
					{
						continue;
					}

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

		private static List<string> GetModIdCandidatesFromFilename(string filePath)
		{
			var ids = new List<string>();
			var modIdRegex = new Regex(@"-((\d+)[-\.])+");
			var numberOfDashes = filePath.Count(c => c == '-');
			var filename = Path.GetFileName(filePath);
			Match modId;

			if (numberOfDashes > 3)
			{
				var strCheckName = Path.GetFileName(filePath);
				strCheckName = strCheckName.Substring(strCheckName.IndexOf('-'));
				modId = modIdRegex.Match(strCheckName);
			}
			else
			{
				modId = modIdRegex.Match(filename);
			}

			if (modId.Success)
			{
				foreach (Capture match in modId.Groups[2].Captures)
				{
					AddModIdCandidate(ids, match.Value);
				}
			}

			return ids;
		}

		private static void AddModIdCandidate(List<string> ids, string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id))
			{
				ids.Add(id);
			}
		}

		private static bool TryParseNewNexusArchiveModIdFromFilename(string filePath, out string modId)
		{
			modId = null;

			var filename = Path.GetFileNameWithoutExtension(filePath);
			if (string.IsNullOrWhiteSpace(filename))
			{
				return false;
			}

			var tokens = filename.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

			for (var i = 0; i < tokens.Length; i++)
			{
				if (!Regex.IsMatch(tokens[i], @"^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}Z$", RegexOptions.IgnoreCase))
				{
					continue;
				}

				if (i < 2 || !Regex.IsMatch(tokens[i - 2], @"^\d+$"))
				{
					continue;
				}

				modId = tokens[i - 2];
				return true;
			}

			return false;
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
		/// Catch'em all failsafe to try and avoid idiotic crashes when the downloadId is borked.
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

		/// <summary>
		/// Catch'em all failsafe to try and avoid idiotic crashes when the filename is borked.
		/// </summary>
		/// <param name="modSearchString"></param>
		/// <returns></returns>
		private string ParseFilename(string modSearchString)
		{
			string filename = string.Empty;

			if (!string.IsNullOrEmpty(modSearchString))
			{
				var modInfo = modSearchString.Split('|');
				filename = modInfo.Length > 2 ? modInfo[3] : string.Empty;
			}

			return filename;
		}
	}
}
