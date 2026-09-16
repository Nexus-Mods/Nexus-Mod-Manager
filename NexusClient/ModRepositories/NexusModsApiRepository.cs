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
	using Nexus.Client.OnlineServices.Infrastructure;
	using Nexus.Client.OnlineServices.NexusMods;
	using Nexus.Client.OnlineServices.NexusMods.V1;
	using Mods;
	using Util;
	using Util.Collections;

	public class NexusModsApiRepository : IModRepository
	{
		private RepositoryUserStatus _userStatus;

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
		public RepositoryUserStatus UserStatus
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
		public RepositoryRateLimit RateLimit
		{
			get
			{
				var owned = _apiCallManager.NexusService.RateLimits.GetSnapshot(NexusApiSurface.V1);
				return NexusV1Mapper.ToRepositoryRateLimit(owned);
			}
		}

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
			 NexusV1ModHashResult result = null)
			{
				Status = status;
				Result = result;
			}

			public ModHashLookupStatus Status { get; }

			public NexusV1ModHashResult Result { get; }
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

			// A credential replacement invalidates account-scoped state before validation begins.
			UserStatus = null;
			AllowedConnections = 1;
			MaxConcurrentDownloads = 5;

			try
			{
				NexusV1Client client = _apiCallManager.V1;
				if (client == null)
					return AuthenticationStatus.Unknown;

				UserStatus = NexusV1Mapper.ToRepositoryUserStatus(client.ValidateUserAsync().GetAwaiter().GetResult());
			}
			catch (ApiException ex)
			{
				Trace.TraceError("Error encountered while validating API key.");
				TraceUtil.TraceException(ex);

				AuthenticationStatus status = MapAuthenticationError(ex);
				if (status == AuthenticationStatus.InvalidKey)
					_apiCallManager.ClearApiKey();

				return status;
			}
			catch (Exception ex)
			{
				Trace.TraceError("Unexpected error encountered while validating API key.");
				TraceUtil.TraceException(ex);
				return AuthenticationStatus.Unknown;
			}

			if (UserStatus == null)
				return AuthenticationStatus.Unknown;

			AllowedConnections = UserStatus.IsPremium ? 2 : 1;
			MaxConcurrentDownloads = UserStatus.IsPremium ? 10 : 5;

			return AuthenticationStatus.Successful;
		}

		/// <summary>
		/// Maps owned API failures to the repository's legacy authentication result contract.
		/// </summary>
		private static AuthenticationStatus MapAuthenticationError(ApiException exception)
		{
			if (exception == null)
				return AuthenticationStatus.Unknown;

			switch (exception.ErrorKind)
			{
				case ApiErrorKind.Authentication:
					return AuthenticationStatus.InvalidKey;
				case ApiErrorKind.Network:
				case ApiErrorKind.Timeout:
					return AuthenticationStatus.NetworkError;
				default:
					return AuthenticationStatus.Unknown;
			}
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
			catch (ApiException ex)
			{
				ReactToApiException(ex);
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
			  ? NexusV1Mapper.ToModFileInfo(
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
				var client = _apiCallManager.V1;
				if (client == null)
					return new ModHashLookupOutcome(ModHashLookupStatus.NoMatch);

				var hashResults = client.FindModsByMd5Async(GameDomainName, hash).GetAwaiter().GetResult();
				var hashResult = hashResults?.FirstOrDefault(result => result?.Mod != null || result?.File != null);

				return hashResult == null
				 ? new ModHashLookupOutcome(ModHashLookupStatus.NoMatch)
				 : new ModHashLookupOutcome(ModHashLookupStatus.Match, hashResult);
			}
			catch (ApiException ex)
			{
				if (ReactToApiException(ex))
					return new ModHashLookupOutcome(ModHashLookupStatus.RateLimitExceeded);

				/*
				 * This can represent an isolated failure of the
				 * MD5 endpoint. GetModInfoForFile will continue
				 * with filename/ID recognition.
				 */
				return new ModHashLookupOutcome(ModHashLookupStatus.RequestFailed);
			}
			catch (Exception ex)
			{
				TraceUtil.TraceException(ex);

				return new ModHashLookupOutcome(ModHashLookupStatus.RequestFailed);
			}
		}

		private static IModInfo CreateModInfoFromHashResult(NexusV1ModHashResult hashResult)
		{
			if (hashResult?.Mod == null)
			{
				return null;
			}

			var modInfo =
			 NexusV1Mapper.ToModInfo(hashResult.Mod);

			var fileInfo = hashResult.File == null
			 ? null
			 : NexusV1Mapper.ToModFileInfo(hashResult.File);

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
				var client = _apiCallManager.V1;
				if (client == null)
					return null;

				string id = ParseModId(modId);
				var nexusMod = client.GetModAsync(GameDomainName, Convert.ToInt32(id)).GetAwaiter().GetResult();
				return NexusV1Mapper.ToModInfo(nexusMod);
			}
			catch (ApiException ex)
			{
				ReactToApiException(ex);
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
					var client = _apiCallManager.V1;
					var nexusMod = client == null ? null : client.GetModAsync(GameDomainName, numericModId).GetAwaiter().GetResult();

					if (nexusMod == null)
					{
						list.Add(new ModInfo());
						continue;
					}

					ModInfo modInfo = NexusV1Mapper.ToModInfo(nexusMod);
					bool fileMetadataResolved = false;
					Task.Delay(50);
					var nexusFiles = client.GetModFilesAsync(GameDomainName, numericModId, new NexusV1FileCategory[0]).GetAwaiter().GetResult();
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
							currentFileId = currentFile.FileId;
						else if (nexusFiles.FileUpdates != null)
						{
							var filenameUpdate = nexusFiles.FileUpdates.FirstOrDefault(update =>
								string.Equals(update.OldFileName, currentFilename, StringComparison.OrdinalIgnoreCase)
								|| string.Equals(update.NewFileName, currentFilename, StringComparison.OrdinalIgnoreCase));

							if (filenameUpdate != null)
							{
								currentFileId = string.Equals(filenameUpdate.OldFileName, currentFilename, StringComparison.OrdinalIgnoreCase)
									? filenameUpdate.OldFileId
									: filenameUpdate.NewFileId;
							}
						}
					}

					int latestFileId = ResolveLatestFileId(currentFileId, nexusFiles?.FileUpdates);

					if (latestFileId > 0)
					{
						var latestFile = nexusFiles?.Files?.FirstOrDefault(file => file.FileId == latestFileId);
						if (latestFile == null)
						{
							Task.Delay(50);
							latestFile = client.GetModFileAsync(GameDomainName, numericModId, latestFileId).GetAwaiter().GetResult();
						}

						if (latestFile != null)
						{
							modInfo = new ModInfo(AutoTagger.CombineInfo(modInfo, NexusV1Mapper.ToModFileInfo(latestFile)));
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
				catch (ApiException ex)
				{
					list.Add(new ModInfo());
					if (ReactToApiException(ex))
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

		/// <summary>
		/// Follows Nexus file-update relationships to the newest reachable file while safely stopping on cycles.
		/// </summary>
		private static int ResolveLatestFileId(int currentFileId, IEnumerable<NexusV1ModFileUpdate> fileUpdates)
		{
			if (currentFileId <= 0 || fileUpdates == null)
				return currentFileId;

			int latestFileId = currentFileId;
			var visitedFileIds = new HashSet<int>();
			while (visitedFileIds.Add(latestFileId))
			{
				var fileUpdate = fileUpdates.FirstOrDefault(update => update.OldFileId == latestFileId);
				if (fileUpdate == null || fileUpdate.NewFileId <= 0 || fileUpdate.NewFileId == latestFileId)
					break;

				latestFileId = fileUpdate.NewFileId;
			}

			return latestFileId;
		}

		/// <inheritdoc cref="IModRepository"/>
		public List<string> GetUpdated(string period)
		{
			List<string> updatedMods = new List<string>();

			try
			{
				var client = _apiCallManager.V1;
				if (client == null)
					return updatedMods;

				NexusV1ModUpdate[] updates = client.GetUpdatedModsAsync(GameDomainName, period).GetAwaiter().GetResult();
				if (updates.Length > 0)
					updatedMods = updates.Select(x => x.ModId.ToString()).ToList();
			}
			catch (ApiException ex)
			{
				ReactToApiException(ex);
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
				var client = _apiCallManager.V1;
				if (client == null)
					return null;

				Task action = null;

				switch (localState)
				{
					case -1:
					case 0:
						// -1 is abstained, 0 is null. Toggling these states will endorse the mod.
						action = client.EndorseAsync(GameDomainName, id, version);
						break;
					case 1:
						// 1 is endorsed, toggling this state will abstain from endorsing the mod.
						action = client.UnendorseAsync(GameDomainName, id, version);
						break;
				}

				if (action == null)
					return null;

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

				await action;
				return localStateAfterCompletion;
			}
			catch (ApiException ex)
			{
				if (ReactToApiException(ex))
					return !localStateAfterCompletion;

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
				var client = _apiCallManager.V1;
				if (client == null)
					return null;

				var result = client.GetModFilesAsync(
					GameDomainName,
					Convert.ToInt32(modId),
					NexusV1FileCategory.Main,
					NexusV1FileCategory.Miscellaneous,
					NexusV1FileCategory.Optional,
					NexusV1FileCategory.Update,
					NexusV1FileCategory.Deleted,
					NexusV1FileCategory.Old).GetAwaiter().GetResult();
				return result.Files.Select(NexusV1Mapper.ToModFileInfo).Cast<IModFileInfo>().ToList();
			}
			catch (ApiException ex)
			{
				ReactToApiException(ex);
				return null;
			}
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
                return null;
            }
		}

		/// <inheritdoc cref="IModRepository"/>
		public List<RepositoryDownloadLink> GetFilePartInfo(string modId, string fileId, string key = "", int expiry = -1)
		{
			var mod = Convert.ToInt32(modId);
			var file = Convert.ToInt32(fileId);

			try
			{
				var client = _apiCallManager.V1;
				if (client == null)
					return null;

				var downloadUris = UserStatus.IsPremium ?
					client.GetDownloadLinksAsync(GameDomainName, mod, file).GetAwaiter().GetResult() :
					client.GetDownloadLinksAsync(GameDomainName, mod, file, key, expiry).GetAwaiter().GetResult();
				return downloadUris.Select(NexusV1Mapper.ToRepositoryDownloadLink).ToList();
			}
			catch (ApiException ex)
			{
				ReactToApiException(ex);
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
				var client = _apiCallManager.V1;
				if (client == null)
					return null;

				var modFile = client.GetModFileAsync(GameDomainName, Convert.ToInt32(modId), Convert.ToInt32(fileId)).GetAwaiter().GetResult();
				return NexusV1Mapper.ToModFileInfo(modFile);
			}
			catch (ApiException ex)
			{
				ReactToApiException(ex);
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
					return NexusV1Mapper.ToModFileInfo(
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
			catch (ApiException ex)
			{
				ReactToApiException(ex);
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

			var client = _apiCallManager.V1;
			if (client == null)
				return null;

			/*
			 * Keep Old and Deleted in the requested categories.
			 * The v1 client intentionally omits Deleted from the
			 * category query, matching the previous v1 behavior.
			 */
			var modFilesResult = client.GetModFilesAsync(
			 GameDomainName,
			 Convert.ToInt32(modId),
			 NexusV1FileCategory.Main,
			 NexusV1FileCategory.Miscellaneous,
			 NexusV1FileCategory.Optional,
			 NexusV1FileCategory.Update,
			 NexusV1FileCategory.Deleted,
			 NexusV1FileCategory.Old).GetAwaiter().GetResult();

			var files = modFilesResult?.Files;

			if (files == null)
			{
				return null;
			}

			var fileInfo =
			 files.FirstOrDefault(
			  file => string.Equals(
			   file.FileName,
			   filename,
			   StringComparison.OrdinalIgnoreCase)) ??

			 files.FirstOrDefault(
			  file => string.Equals(
			   file.Name,
			   filename,
			   StringComparison.OrdinalIgnoreCase)) ??

			 files.FirstOrDefault(
			  file => string.Equals(
			   file.Name?.Replace(
				' ',
				'_'),
			   filename,
			   StringComparison.OrdinalIgnoreCase)) ??

			 files.FirstOrDefault(
			  file => string.Equals(
			   file.Name?.Replace(
				' ',
				'-'),
			   filename,
			   StringComparison.OrdinalIgnoreCase));

			/*
			 * The old code returned new ModFileInfo(null),
			 * which produced a non-null object containing only
			 * null properties. Return a real null on no match.
			 */
			return fileInfo == null
			 ? null
			 : NexusV1Mapper.ToModFileInfo(fileInfo);
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
				var client = _apiCallManager.V1;
				if (client == null)
					return null;

				var mfiFiles = client.GetModFilesAsync(GameDomainName, Convert.ToInt32(modId), NexusV1FileCategory.Main).GetAwaiter().GetResult().Files;

				var mfiDefault = (from f in mfiFiles
									  orderby f.UploadedTimestamp descending
									  select f).FirstOrDefault() ?? (from f in mfiFiles
																	 orderby f.UploadedTimestamp descending
																	 select f).FirstOrDefault();

				return NexusV1Mapper.ToModFileInfo(mfiDefault);
			}
			catch (ApiException ex)
			{
				ReactToApiException(ex);
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
				var client = _apiCallManager.V1;
				if (client == null)
					return null;

				var categories = client.GetGameAsync(gameId).GetAwaiter().GetResult().Categories;
				return categories?.Select(NexusV1Mapper.ToCategoriesInfo).ToList();
			}
			catch (ApiException ex)
			{
				ReactToApiException(ex);
				return null;
			}
            catch (Exception ex)
            {
                TraceUtil.TraceException(ex);
                return null;
            }
		}

		/// <summary>
		/// Reacts to an NMM-owned API failure from migrated Nexus operations.
		/// </summary>
		/// <param name="exception">API failure to process.</param>
		/// <returns>Whether the failure represents an actual Nexus rate-limit response.</returns>
		private bool ReactToApiException(ApiException exception)
		{
			TraceUtil.TraceException(exception);

			if (exception?.ErrorKind != ApiErrorKind.RateLimit)
				return false;

			RateLimitExceeded?.Invoke(this, new RateLimitExceededArgs(RateLimit));
			return true;
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
