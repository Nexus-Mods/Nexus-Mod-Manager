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
	using Nexus.Client.OnlineServices.NexusMods.GraphQl;
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
		public RepositoryUserStatus UserStatus => _userStatus;

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

		private const int FileResolutionMaxConcurrency = 4;

		private readonly ApiCallManager _apiCallManager;
		private readonly NexusFileUpdateCheckpointStore _fileUpdateCheckpointStore;

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
		/// Captures one parsed update-check row while preserving its original position.
		/// </summary>
		private sealed class FileListInfoRequest
		{
			public FileListInfoRequest(int originalIndex, string source, int modId, string downloadId, string currentFilename)
			{
				OriginalIndex = originalIndex;
				Source = source;
				ModId = modId;
				DownloadId = downloadId;
				CurrentFilename = currentFilename;
			}

			public FileListInfoRequest(int originalIndex, string source, Exception parseException)
			{
				OriginalIndex = originalIndex;
				Source = source;
				ParseException = parseException;
			}

			public int OriginalIndex { get; }

			public string Source { get; }

			public int ModId { get; }

			public string DownloadId { get; }

			public string CurrentFilename { get; }

			public Exception ParseException { get; }
		}

		/// <summary>
		/// Stores one operation-scoped provider result, including a failure that must not be retried for duplicate rows.
		/// </summary>
		private sealed class OperationRequestResult<T> where T : class
		{
			private OperationRequestResult(T value, ApiException apiException, Exception exception)
			{
				Value = value;
				ApiException = apiException;
				UnexpectedException = exception;
			}

			public T Value { get; }

			public ApiException ApiException { get; }

			public Exception UnexpectedException { get; }

			public bool FailureHandled { get; set; }

			public bool HasFailure => ApiException != null || UnexpectedException != null;

			public static OperationRequestResult<T> Capture(Func<T> request)
			{
				try
				{
					return new OperationRequestResult<T>(request(), null, null);
				}
				catch (ApiException ex)
				{
					return new OperationRequestResult<T>(null, ex, null);
				}
				catch (Exception ex)
				{
					return new OperationRequestResult<T>(null, null, ex);
				}
			}

			/// <summary>
			/// Captures one asynchronous provider result without losing its typed API failure.
			/// </summary>
			public static async Task<OperationRequestResult<T>> CaptureAsync(Func<Task<T>> request)
			{
				try
				{
					return new OperationRequestResult<T>(await request().ConfigureAwait(false), null, null);
				}
				catch (ApiException ex)
				{
					return new OperationRequestResult<T>(null, ex, null);
				}
				catch (Exception ex)
				{
					return new OperationRequestResult<T>(null, null, ex);
				}
			}
		}

		/// <summary>
		/// Associates one bounded file-resolution request with the session generation that produced it.
		/// </summary>
		private sealed class FileResolutionRequestOutcome
		{
			public FileResolutionRequestOutcome(
				long generation,
				int modId,
				OperationRequestResult<NexusV1ModFileList> result,
				Dictionary<int, OperationRequestResult<NexusV1ModFile>> successorResults)
			{
				Generation = generation;
				ModId = modId;
				Result = result;
				SuccessorResults = successorResults ?? new Dictionary<int, OperationRequestResult<NexusV1ModFile>>();
			}

			public long Generation { get; }

			public int ModId { get; }

			public OperationRequestResult<NexusV1ModFileList> Result { get; }

			public Dictionary<int, OperationRequestResult<NexusV1ModFile>> SuccessorResults { get; }
		}

		/// <summary>
		/// Collects the completed work and observed concurrency of one independently safe file-resolution segment.
		/// </summary>
		private sealed class FileResolutionSegmentResult
		{
			public List<FileResolutionRequestOutcome> Outcomes { get; } = new List<FileResolutionRequestOutcome>();

			public int PeakConcurrency { get; set; }

			public bool RateLimitObserved { get; set; }
		}

		/// <summary>
		/// Records provider request counts produced while preparing bounded file-resolution work.
		/// </summary>
		private sealed class FileListPreparationSummary
		{
			public int ParentRequests { get; set; }

			public int FileListRequests { get; set; }

			public int BoundedFileListRequests { get; set; }

			public int FileResolutionSegments { get; set; }

			public int SuccessorFileRequests { get; set; }

			public int PeakFileResolutionConcurrency { get; set; }
		}

		/// <summary>
		/// Creates a new instance of the <see cref="NexusModsApiRepository"/>.
		/// </summary>
		/// <param name="currentGameDomain">Currently selected game.</param>
		/// <param name="apiCallManager"><see cref="ApiCallManager"/> to use for API calls.</param>
		public NexusModsApiRepository(string currentGameDomain, ApiCallManager apiCallManager)
			: this(currentGameDomain, apiCallManager, null)
		{
		}

		/// <summary>
		/// Creates a Nexus repository with optional per-game persistent file-update checkpoints.
		/// </summary>
		/// <param name="currentGameDomain">Currently selected game.</param>
		/// <param name="apiCallManager"><see cref="ApiCallManager"/> to use for API calls.</param>
		/// <param name="installInfoDirectory">The game install-info directory used for derived update-check checkpoint data.</param>
		public NexusModsApiRepository(string currentGameDomain, ApiCallManager apiCallManager, string installInfoDirectory)
		{
			_gameDomain = GameDomainTranslator.DetermineGameDomain(currentGameDomain);
			_apiCallManager = apiCallManager;
			_fileUpdateCheckpointStore = new NexusFileUpdateCheckpointStore(installInfoDirectory);
		}

		/// <inheritdoc />
		public AuthenticationStatus Authenticate()
		{
			return Authenticate(false);
		}

		/// <inheritdoc />
		public AuthenticationStatus Authenticate(bool clearCredentialsOnFailure)
		{
			NexusSessionContext authenticationSession = _apiCallManager.BeginAuthenticationSession();

			// A credential replacement invalidates account-scoped state before validation begins.
			bool userStatusChanged = false;
			if (!_apiCallManager.TryUpdateAuthenticationState(authenticationSession, () =>
			{
				userStatusChanged = SetUserStatusWithoutNotification(null);
				AllowedConnections = 1;
				MaxConcurrentDownloads = 5;
			}))
				return AuthenticationStatus.Unknown;

			if (userStatusChanged)
				OnUserStatusUpdate();

			try
			{
				NexusV1Client client = _apiCallManager.V1;
				if (client == null)
					return CompleteAuthenticationFailure(AuthenticationStatus.Unknown, authenticationSession, clearCredentialsOnFailure);

				RepositoryUserStatus validatedUserStatus = NexusV1Mapper.ToRepositoryUserStatus(client.ValidateUserAsync().GetAwaiter().GetResult());
				if (validatedUserStatus == null)
					return CompleteAuthenticationFailure(AuthenticationStatus.Unknown, authenticationSession, clearCredentialsOnFailure);

				userStatusChanged = false;
				if (!_apiCallManager.TryUpdateAuthenticationState(authenticationSession, () =>
				{
					userStatusChanged = SetUserStatusWithoutNotification(validatedUserStatus);
					AllowedConnections = validatedUserStatus.IsPremium ? 2 : 1;
					MaxConcurrentDownloads = validatedUserStatus.IsPremium ? 10 : 5;
				}))
					return AuthenticationStatus.Unknown;

				if (userStatusChanged)
					OnUserStatusUpdate();

				// Notification callbacks may synchronously log out or replace credentials.
				// Never report this authentication attempt as successful once its generation is obsolete.
				if (!_apiCallManager.IsCurrentSession(authenticationSession))
					return AuthenticationStatus.Unknown;
			}
			catch (ApiException ex)
			{
				Trace.TraceError("Error encountered while validating API key.");
				TraceUtil.TraceException(ex);

				AuthenticationStatus status = MapAuthenticationError(ex);
				return CompleteAuthenticationFailure(status, authenticationSession, clearCredentialsOnFailure);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Unexpected error encountered while validating API key.");
				TraceUtil.TraceException(ex);
				return CompleteAuthenticationFailure(AuthenticationStatus.Unknown, authenticationSession, clearCredentialsOnFailure);
			}

			return AuthenticationStatus.Successful;
		}

		/// <summary>
		/// Completes one failed authentication attempt without allowing an obsolete generation to clear replacement credentials.
		/// </summary>
		private AuthenticationStatus CompleteAuthenticationFailure(AuthenticationStatus status, NexusSessionContext authenticationSession, bool clearCredentialsOnFailure)
		{
			if (clearCredentialsOnFailure || status == AuthenticationStatus.InvalidKey)
				return _apiCallManager.ClearApiKey(authenticationSession) ? status : AuthenticationStatus.Unknown;

			return _apiCallManager.IsCurrentSession(authenticationSession) ? status : AuthenticationStatus.Unknown;
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
			try
			{
				_apiCallManager.ClearApiKey(() =>
				{
					SetUserStatusWithoutNotification(null);
					AllowedConnections = 1;
					MaxConcurrentDownloads = 5;
				});
			}
			finally
			{
				// Preserve the repository's logout refresh while ensuring UI callbacks run after the lifecycle lock is released.
				OnUserStatusUpdate();
			}
		}

		/// <summary>
		/// Updates the cached repository user without raising UI-facing notifications.
		/// </summary>
		private bool SetUserStatusWithoutNotification(RepositoryUserStatus value)
		{
			if (_userStatus == value)
				return false;

			_userStatus = value;
			return true;
		}

		/// <summary>
		/// Raises the user-status notification after credential lifecycle synchronization has been released.
		/// </summary>
		private void OnUserStatusUpdate()
		{
			UserStatusUpdate?.Invoke(this, EventArgs.Empty);
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
		public Dictionary<string, int> GetModCategoryIds(IEnumerable<string> modIds)
		{
			var requestedIds = new List<int>();
			var seenIds = new HashSet<int>();
			int requestedCount = 0;
			if (modIds != null)
			{
				foreach (string modId in modIds)
				{
					requestedCount++;
					int numericModId;
					if (Int32.TryParse(modId, out numericModId) && numericModId > 0 && seenIds.Add(numericModId))
						requestedIds.Add(numericModId);
				}
			}

			var categories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			if (requestedIds.Count == 0)
				return categories;

			NexusRateLimitSnapshot graphQlQuotaBefore = _apiCallManager.NexusService.RateLimits.GetSnapshot(NexusApiSurface.GraphQl);
			NexusRateLimitSnapshot v1QuotaBefore = _apiCallManager.NexusService.RateLimits.GetSnapshot(NexusApiSurface.V1);
			NexusLegacyModLookupResult graphQlParents = ResolveGraphQlParentMetadata(requestedIds);
			var client = _apiCallManager.V1;
			int graphQlCategoryRows = 0;
			int v1ParentRequests = 0;
			int unresolved = 0;

			foreach (int modId in requestedIds)
			{
				try
				{
					long generation = _apiCallManager.NexusService.CaptureSession().Generation;
					NexusLegacyModMetadata graphQlParent;
					if (graphQlParents != null
						&& graphQlParents.Generation == generation
						&& graphQlParents.Mods.TryGetValue(modId, out graphQlParent)
						&& graphQlParent.CategoryId.HasValue)
					{
						categories[modId.ToString()] = graphQlParent.CategoryId.Value;
						graphQlCategoryRows++;
						continue;
					}

					if (client == null)
					{
						unresolved++;
						continue;
					}

					v1ParentRequests++;
					NexusV1Mod nexusMod = client.GetModAsync(GameDomainName, modId).GetAwaiter().GetResult();
					if (nexusMod == null)
					{
						unresolved++;
						continue;
					}

					categories[modId.ToString()] = nexusMod.CategoryId;
				}
				catch (ApiException ex)
				{
					unresolved++;
					if (ReactToApiException(ex))
						break;
				}
				catch (Exception ex)
				{
					unresolved++;
					Trace.TraceError("Exception while retrieving repository category for Nexus mod ID {0}.", modId);
					TraceUtil.TraceException(ex);
				}
			}

			NexusRateLimitSnapshot graphQlQuotaAfter = _apiCallManager.NexusService.RateLimits.GetSnapshot(NexusApiSurface.GraphQl);
			NexusRateLimitSnapshot v1QuotaAfter = _apiCallManager.NexusService.RateLimits.GetSnapshot(NexusApiSurface.V1);
			Trace.TraceInformation(
				"NMM category metadata coalescing completed: requested={0}, uniqueMods={1}, graphQlRequests={2}, graphQlCategoryRows={3}, v1ParentRequests={4}, resolved={5}, unresolved={6}, graphQlQuotaDelta={7}, v1QuotaDelta={8}.",
				requestedCount,
				requestedIds.Count,
				graphQlParents?.RequestCount ?? 0,
				graphQlCategoryRows,
				v1ParentRequests,
				categories.Count,
				unresolved,
				FormatQuotaDelta(graphQlQuotaBefore, graphQlQuotaAfter),
				FormatQuotaDelta(v1QuotaBefore, v1QuotaAfter));

			return categories;
		}

		/// <inheritdoc cref="IModRepository"/>
		public List<IModInfo> GetFileListInfo(List<string> modFileList)
		{
			return GetFileListInfoWithFreshness(modFileList, null).ModInfo;
		}

		/// <inheritdoc cref="IModRepository"/>
		public RepositoryFileListInfoResult GetFileListInfoWithFreshness(List<string> modFileList, IEnumerable<RepositoryModUpdate> providerUpdates)
		{
			List<FileListInfoRequest> requests = ParseFileListInfoRequests(modFileList);
			var list = new List<IModInfo>(requests.Count);
			var checkpoints = new List<RepositoryFileUpdateCheckpoint>(requests.Count);
			var providerUpdatesByModId = BuildProviderUpdateLookup(providerUpdates);
			var parentResults = new Dictionary<Tuple<long, string, int>, OperationRequestResult<NexusV1Mod>>();
			var fileListResults = new Dictionary<Tuple<long, string, int>, OperationRequestResult<NexusV1ModFileList>>();
			var successorResults = new Dictionary<Tuple<long, string, int, int>, OperationRequestResult<NexusV1ModFile>>();
			var uniqueModIds = new HashSet<int>(requests.Where(request => request.ParseException == null).Select(request => request.ModId));
			NexusRateLimitSnapshot graphQlQuotaBefore = _apiCallManager.NexusService.RateLimits.GetSnapshot(NexusApiSurface.GraphQl);
			NexusRateLimitSnapshot v1QuotaBefore = _apiCallManager.NexusService.RateLimits.GetSnapshot(NexusApiSurface.V1);
			NexusLegacyModLookupResult graphQlParents = ResolveGraphQlParentMetadata(uniqueModIds);
			NexusV1Client client = _apiCallManager.V1;
			FileListPreparationSummary preparation = client == null
				? new FileListPreparationSummary()
				: PrepareBoundedFileListRequests(
					requests,
					providerUpdatesByModId,
					graphQlParents,
					parentResults,
					fileListResults,
					successorResults,
					client);

			int parentRequests = preparation.ParentRequests;
			int fileListRequests = preparation.FileListRequests;
			int successorFileRequests = preparation.SuccessorFileRequests;
			int reusedParentResults = 0;
			int reusedFileListResults = 0;
			int graphQlParentRows = 0;
			int freshnessCacheHits = 0;
			int freshnessCacheMisses = 0;
			var consumedParentResults = new HashSet<Tuple<long, string, int>>();
			var consumedFileListResults = new HashSet<Tuple<long, string, int>>();

			foreach (FileListInfoRequest request in requests)
			{
				if (request.ParseException != null)
				{
					TraceFileListInfoException(request.Source, request.ParseException);
					list.Add(new ModInfo());
					checkpoints.Add(null);
					continue;
				}

				try
				{
					if (client == null)
					{
						list.Add(new ModInfo());
						checkpoints.Add(null);
						continue;
					}

					long parentGeneration = _apiCallManager.NexusService.CaptureSession().Generation;
					ModInfo modInfo = null;
					NexusLegacyModMetadata graphQlParent;
					if (graphQlParents != null && graphQlParents.Generation == parentGeneration && graphQlParents.Mods.TryGetValue(request.ModId, out graphQlParent))
					{
						modInfo = NexusGraphQlMapper.ToModInfo(graphQlParent);
						if (modInfo != null)
							graphQlParentRows++;
					}

					if (modInfo == null)
					{
						var parentKey = Tuple.Create(parentGeneration, GameDomainName, request.ModId);
						OperationRequestResult<NexusV1Mod> parentResult;
						if (parentResults.TryGetValue(parentKey, out parentResult))
						{
							if (!consumedParentResults.Add(parentKey))
								reusedParentResults++;
						}
						else
						{
							parentRequests++;
							parentResult = OperationRequestResult<NexusV1Mod>.Capture(() =>
								client.GetModAsync(GameDomainName, request.ModId).GetAwaiter().GetResult());
							parentResults[parentKey] = parentResult;
							consumedParentResults.Add(parentKey);
						}

						if (parentResult.HasFailure)
						{
							list.Add(new ModInfo());
							checkpoints.Add(null);
							if (HandleOperationRequestFailure(request.Source, parentResult))
								break;
							continue;
						}

						if (parentResult.Value == null)
						{
							list.Add(new ModInfo());
							checkpoints.Add(null);
							continue;
						}

						modInfo = NexusV1Mapper.ToModInfo(parentResult.Value);
					}

					RepositoryModUpdate providerUpdate;
					bool hasUsableFreshness = providerUpdatesByModId.TryGetValue(request.ModId, out providerUpdate)
						&& providerUpdate.LatestFileUpdateUtc != default(DateTimeOffset)
						&& _fileUpdateCheckpointStore.IsEnabled;
					RepositoryFileUpdateCheckpoint checkpoint;
					if (hasUsableFreshness && _fileUpdateCheckpointStore.TryGet(
						GameDomainName,
						request.ModId,
						providerUpdate.LatestFileUpdateUtc,
						request.DownloadId,
						request.CurrentFilename,
						out checkpoint))
					{
						freshnessCacheHits++;
						modInfo = ApplyFileUpdateCheckpoint(modInfo, checkpoint, request.CurrentFilename);
						list.Add(modInfo);
						checkpoints.Add(checkpoint);
						continue;
					}

					if (hasUsableFreshness)
						freshnessCacheMisses++;

					bool fileMetadataResolved = false;

					long fileListGeneration = _apiCallManager.NexusService.CaptureSession().Generation;
					var fileListKey = Tuple.Create(fileListGeneration, GameDomainName, request.ModId);
					OperationRequestResult<NexusV1ModFileList> fileListResult;
					if (fileListResults.TryGetValue(fileListKey, out fileListResult))
					{
						if (!consumedFileListResults.Add(fileListKey))
							reusedFileListResults++;
					}
					else
					{
						fileListRequests++;
						fileListResult = OperationRequestResult<NexusV1ModFileList>.Capture(() =>
							client.GetModFilesAsync(GameDomainName, request.ModId, new NexusV1FileCategory[0]).GetAwaiter().GetResult());
						fileListResults[fileListKey] = fileListResult;
						consumedFileListResults.Add(fileListKey);
					}

					if (fileListResult.HasFailure)
					{
						list.Add(new ModInfo());
						checkpoints.Add(null);
						if (HandleOperationRequestFailure(request.Source, fileListResult))
							break;
						continue;
					}

					NexusV1ModFileList nexusFiles = fileListResult.Value;
					int currentFileId = ResolveCurrentFileId(request, nexusFiles);

					int latestFileId = ResolveLatestFileId(currentFileId, nexusFiles?.FileUpdates);
					NexusV1ModFile latestFile = null;

					if (latestFileId > 0)
					{
						latestFile = nexusFiles?.Files?.FirstOrDefault(file => file.FileId == latestFileId);
						if (latestFile == null)
						{
							long successorGeneration = _apiCallManager.NexusService.CaptureSession().Generation;
							var successorKey = Tuple.Create(successorGeneration, GameDomainName, request.ModId, latestFileId);
							OperationRequestResult<NexusV1ModFile> successorResult;
							if (!successorResults.TryGetValue(successorKey, out successorResult))
							{
								successorFileRequests++;
								successorResult = OperationRequestResult<NexusV1ModFile>.Capture(() =>
									client.GetModFileAsync(GameDomainName, request.ModId, latestFileId).GetAwaiter().GetResult());
								successorResults[successorKey] = successorResult;
							}

							if (successorResult.HasFailure)
							{
								list.Add(new ModInfo());
								checkpoints.Add(null);
								if (HandleOperationRequestFailure(request.Source, successorResult))
									break;
								continue;
							}

							latestFile = successorResult.Value;
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

					if (string.IsNullOrWhiteSpace(modInfo.FileName) && !string.IsNullOrWhiteSpace(request.CurrentFilename))
						modInfo.FileName = request.CurrentFilename;

					checkpoint = hasUsableFreshness
						? CreateFileUpdateCheckpoint(request, providerUpdate, latestFile, latestFileId, fileMetadataResolved)
						: null;
					list.Add(modInfo);
					checkpoints.Add(checkpoint);
				}
				catch (Exception ex)
				{
					TraceFileListInfoException(request.Source, ex);
					list.Add(new ModInfo());
					checkpoints.Add(null);
				}
			}

			NexusRateLimitSnapshot graphQlQuotaAfter = _apiCallManager.NexusService.RateLimits.GetSnapshot(NexusApiSurface.GraphQl);
			NexusRateLimitSnapshot v1QuotaAfter = _apiCallManager.NexusService.RateLimits.GetSnapshot(NexusApiSurface.V1);
			int graphQlRequests = graphQlParents?.RequestCount ?? 0;
			Trace.TraceInformation(
				"NMM metadata coalescing completed: requested={0}, uniqueMods={1}, graphQlRequests={2}, graphQlParents={3}, graphQlParentRows={4}, v1ParentRequests={5}, fileListRequests={6}, boundedFileListRequests={7}, fileResolutionSegments={8}, peakFileResolutionConcurrency={9}, successorFileRequests={10}, reusedV1Parent={11}, reusedFileList={12}, freshnessCacheHits={13}, freshnessCacheMisses={14}, checkpointCandidates={15}, graphQlQuotaDelta={16}, v1QuotaDelta={17}.",
				requests.Count,
				uniqueModIds.Count,
				graphQlRequests,
				graphQlParents?.Mods.Count ?? 0,
				graphQlParentRows,
				parentRequests,
				fileListRequests,
				preparation.BoundedFileListRequests,
				preparation.FileResolutionSegments,
				preparation.PeakFileResolutionConcurrency,
				successorFileRequests,
				reusedParentResults,
				reusedFileListResults,
				freshnessCacheHits,
				freshnessCacheMisses,
				checkpoints.Count(candidate => candidate != null),
				FormatQuotaDelta(graphQlQuotaBefore, graphQlQuotaAfter),
				FormatQuotaDelta(v1QuotaBefore, v1QuotaAfter));

			return new RepositoryFileListInfoResult(list, checkpoints);
		}

		/// <inheritdoc cref="IModRepository"/>
		public bool CommitFileUpdateCheckpoints(IEnumerable<RepositoryFileUpdateCheckpoint> checkpoints)
		{
			try
			{
				return _fileUpdateCheckpointStore.Commit(checkpoints);
			}
			catch (Exception ex)
			{
				Trace.TraceWarning("Unable to persist Nexus file-update checkpoints; future update checks will refetch file metadata.");
				TraceUtil.TraceException(ex);
				return false;
			}
		}

		/// <summary>
		/// Builds a provider-update lookup without assuming Nexus returns each mod ID only once.
		/// </summary>
		private static Dictionary<int, RepositoryModUpdate> BuildProviderUpdateLookup(IEnumerable<RepositoryModUpdate> providerUpdates)
		{
			var lookup = new Dictionary<int, RepositoryModUpdate>();
			if (providerUpdates == null)
				return lookup;

			foreach (RepositoryModUpdate update in providerUpdates)
			{
				int modId;
				if (update == null || !Int32.TryParse(update.ModId, out modId) || modId <= 0)
					continue;

				RepositoryModUpdate current;
				if (!lookup.TryGetValue(modId, out current) || update.LatestFileUpdateUtc > current.LatestFileUpdateUtc)
					lookup[modId] = update;
			}

			return lookup;
		}

		/// <summary>
		/// Resolves REST parent fallbacks in row order and batches only the file-resolution work that is safe to overlap between those barriers.
		/// </summary>
		private FileListPreparationSummary PrepareBoundedFileListRequests(
			IList<FileListInfoRequest> requests,
			Dictionary<int, RepositoryModUpdate> providerUpdatesByModId,
			NexusLegacyModLookupResult graphQlParents,
			Dictionary<Tuple<long, string, int>, OperationRequestResult<NexusV1Mod>> parentResults,
			Dictionary<Tuple<long, string, int>, OperationRequestResult<NexusV1ModFileList>> fileListResults,
			Dictionary<Tuple<long, string, int, int>, OperationRequestResult<NexusV1ModFile>> successorResults,
			NexusV1Client client)
		{
			var summary = new FileListPreparationSummary();
			var resolutionRequestsByModId = new Dictionary<int, List<FileListInfoRequest>>();
			var orderedMods = new List<int>();

			foreach (FileListInfoRequest request in requests)
			{
				if (request.ParseException != null)
					continue;

				if (!resolutionRequestsByModId.ContainsKey(request.ModId))
				{
					resolutionRequestsByModId.Add(request.ModId, new List<FileListInfoRequest>());
					orderedMods.Add(request.ModId);
				}

				if (RequestNeedsFileListResolution(request, providerUpdatesByModId))
					resolutionRequestsByModId[request.ModId].Add(request);
			}

			var segment = new List<int>();
			bool stopAfterRateLimit = false;
			foreach (int modId in orderedMods)
			{
				long generation = _apiCallManager.NexusService.CaptureSession().Generation;
				NexusLegacyModMetadata graphQlParent;
				bool hasGraphQlParent = graphQlParents != null
					&& graphQlParents.Generation == generation
					&& graphQlParents.Mods.TryGetValue(modId, out graphQlParent)
					&& graphQlParent != null
					&& graphQlParent.HasRequiredParentMetadata;

				if (!hasGraphQlParent)
				{
					if (!FlushFileResolutionSegment(segment, resolutionRequestsByModId, client, fileListResults, successorResults, summary))
					{
						stopAfterRateLimit = true;
						break;
					}

					segment.Clear();
					generation = _apiCallManager.NexusService.CaptureSession().Generation;
					var parentKey = Tuple.Create(generation, GameDomainName, modId);
					OperationRequestResult<NexusV1Mod> parentResult;
					if (!parentResults.TryGetValue(parentKey, out parentResult))
					{
						summary.ParentRequests++;
						parentResult = OperationRequestResult<NexusV1Mod>.Capture(() =>
							client.GetModAsync(GameDomainName, modId).GetAwaiter().GetResult());
						parentResults[parentKey] = parentResult;
					}

					if (parentResult.ApiException?.ErrorKind == ApiErrorKind.RateLimit)
					{
						stopAfterRateLimit = true;
						break;
					}

					if (parentResult.HasFailure || parentResult.Value == null)
						continue;

					// Keep a REST-parent fallback as a complete ordering barrier: its file resolution
					// finishes before later GraphQL-resolved mods are allowed to overlap.
					if (resolutionRequestsByModId[modId].Count > 0
						&& !FlushFileResolutionSegment(new[] { modId }, resolutionRequestsByModId, client, fileListResults, successorResults, summary))
					{
						stopAfterRateLimit = true;
						break;
					}

					continue;
				}

				if (resolutionRequestsByModId[modId].Count > 0)
					segment.Add(modId);
			}

			if (!stopAfterRateLimit)
				FlushFileResolutionSegment(segment, resolutionRequestsByModId, client, fileListResults, successorResults, summary);

			return summary;
		}

		/// <summary>
		/// Returns whether one local archive lacks a reusable provider-fresh file checkpoint.
		/// </summary>
		private bool RequestNeedsFileListResolution(
			FileListInfoRequest request,
			Dictionary<int, RepositoryModUpdate> providerUpdatesByModId)
		{
			RepositoryModUpdate providerUpdate;
			bool hasUsableFreshness = providerUpdatesByModId.TryGetValue(request.ModId, out providerUpdate)
				&& providerUpdate.LatestFileUpdateUtc != default(DateTimeOffset)
				&& _fileUpdateCheckpointStore.IsEnabled;
			RepositoryFileUpdateCheckpoint checkpoint;
			return !hasUsableFreshness || !_fileUpdateCheckpointStore.TryGet(
				GameDomainName,
				request.ModId,
				providerUpdate.LatestFileUpdateUtc,
				request.DownloadId,
				request.CurrentFilename,
				out checkpoint);
		}

		/// <summary>
		/// Executes one row-ordered segment with a small fixed concurrency bound and stops starting new work after a rate-limit response.
		/// </summary>
		private bool FlushFileResolutionSegment(
			IList<int> modIds,
			Dictionary<int, List<FileListInfoRequest>> requestsByModId,
			NexusV1Client client,
			Dictionary<Tuple<long, string, int>, OperationRequestResult<NexusV1ModFileList>> fileListResults,
			Dictionary<Tuple<long, string, int, int>, OperationRequestResult<NexusV1ModFile>> successorResults,
			FileListPreparationSummary summary)
		{
			if (modIds == null || modIds.Count == 0)
				return true;

			FileResolutionSegmentResult segment = ExecuteFileResolutionSegmentAsync(modIds, requestsByModId, client).GetAwaiter().GetResult();
			summary.FileResolutionSegments++;
			summary.BoundedFileListRequests += segment.Outcomes.Count;
			summary.FileListRequests += segment.Outcomes.Count;
			summary.SuccessorFileRequests += segment.Outcomes.Sum(outcome => outcome.SuccessorResults.Count);
			summary.PeakFileResolutionConcurrency = Math.Max(summary.PeakFileResolutionConcurrency, segment.PeakConcurrency);

			foreach (FileResolutionRequestOutcome outcome in segment.Outcomes)
			{
				fileListResults[Tuple.Create(outcome.Generation, GameDomainName, outcome.ModId)] = outcome.Result;
				foreach (KeyValuePair<int, OperationRequestResult<NexusV1ModFile>> successor in outcome.SuccessorResults)
				{
					successorResults[Tuple.Create(outcome.Generation, GameDomainName, outcome.ModId, successor.Key)] = successor.Value;
				}
			}

			return !segment.RateLimitObserved;
		}

		/// <summary>
		/// Runs one independent file-resolution segment with FIFO dispatch and at most four active REST requests.
		/// </summary>
		private async Task<FileResolutionSegmentResult> ExecuteFileResolutionSegmentAsync(
			IList<int> modIds,
			Dictionary<int, List<FileListInfoRequest>> requestsByModId,
			NexusV1Client client)
		{
			var result = new FileResolutionSegmentResult();
			var queue = new Queue<int>(modIds);
			var active = new List<Task<FileResolutionRequestOutcome>>();
			int activeRequests = 0;
			int peakConcurrency = 0;
			bool stopScheduling = false;

			Func<int, Task<FileResolutionRequestOutcome>> start = modId => ResolveFileResolutionRequestAsync(
				modId,
				requestsByModId,
				client,
				() => Interlocked.Increment(ref activeRequests),
				() => Interlocked.Decrement(ref activeRequests),
				concurrency => UpdatePeakConcurrency(ref peakConcurrency, concurrency));

			while (active.Count < FileResolutionMaxConcurrency && queue.Count > 0)
				active.Add(start(queue.Dequeue()));

			while (active.Count > 0)
			{
				Task<FileResolutionRequestOutcome> completedTask = await Task.WhenAny(active).ConfigureAwait(false);
				active.Remove(completedTask);
				FileResolutionRequestOutcome outcome = await completedTask.ConfigureAwait(false);
				result.Outcomes.Add(outcome);

				if (HasRateLimit(outcome))
				{
					stopScheduling = true;
					result.RateLimitObserved = true;
				}

				if (!stopScheduling && queue.Count > 0)
					active.Add(start(queue.Dequeue()));
			}

			result.PeakConcurrency = peakConcurrency;
			return result;
		}

		/// <summary>
		/// Resolves the file list and any missing successor files for one unique mod.
		/// </summary>
		private async Task<FileResolutionRequestOutcome> ResolveFileResolutionRequestAsync(
			int modId,
			Dictionary<int, List<FileListInfoRequest>> requestsByModId,
			NexusV1Client client,
			Func<int> enter,
			Action exit,
			Action<int> observeConcurrency)
		{
			NexusSessionContext session = _apiCallManager.NexusService.CaptureSession();
			OperationRequestResult<NexusV1ModFileList> requestResult = await CaptureBoundedRequestAsync(
				() => client.GetModFilesAsync(GameDomainName, modId, session.Token, new NexusV1FileCategory[0]),
				enter,
				exit,
				observeConcurrency).ConfigureAwait(false);
			var successorOutcomes = new Dictionary<int, OperationRequestResult<NexusV1ModFile>>();

			if (!requestResult.HasFailure && requestResult.Value != null)
			{
				foreach (FileListInfoRequest request in requestsByModId[modId])
				{
					int currentFileId = ResolveCurrentFileId(request, requestResult.Value);
					int latestFileId = ResolveLatestFileId(currentFileId, requestResult.Value.FileUpdates);
					if (latestFileId <= 0
						|| requestResult.Value.Files?.Any(file => file.FileId == latestFileId) == true
						|| successorOutcomes.ContainsKey(latestFileId))
					{
						continue;
					}

					OperationRequestResult<NexusV1ModFile> successorResult = await CaptureBoundedRequestAsync(
						() => client.GetModFileAsync(GameDomainName, modId, latestFileId, session.Token),
						enter,
						exit,
						observeConcurrency).ConfigureAwait(false);
					successorOutcomes.Add(latestFileId, successorResult);
					if (successorResult.ApiException?.ErrorKind == ApiErrorKind.RateLimit)
						break;
				}
			}

			return new FileResolutionRequestOutcome(session.Generation, modId, requestResult, successorOutcomes);
		}

		/// <summary>
		/// Gets whether one completed unique-mod resolution observed a Nexus rate-limit response.
		/// </summary>
		private static bool HasRateLimit(FileResolutionRequestOutcome outcome)
		{
			if (outcome?.Result?.ApiException?.ErrorKind == ApiErrorKind.RateLimit)
				return true;

			return outcome?.SuccessorResults != null
				&& outcome.SuccessorResults.Values.Any(result => result?.ApiException?.ErrorKind == ApiErrorKind.RateLimit);
		}

		/// <summary>
		/// Captures one provider request while contributing to the operation-wide active-request counter.
		/// </summary>
		private static async Task<OperationRequestResult<T>> CaptureBoundedRequestAsync<T>(
			Func<Task<T>> request,
			Func<int> enter,
			Action exit,
			Action<int> observeConcurrency) where T : class
		{
			int active = enter();
			observeConcurrency(active);
			try
			{
				return await OperationRequestResult<T>.CaptureAsync(request).ConfigureAwait(false);
			}
			finally
			{
				exit();
			}
		}

		/// <summary>
		/// Atomically records the highest number of simultaneous file-list requests observed by this operation.
		/// </summary>
		private static void UpdatePeakConcurrency(ref int peakConcurrency, int activeRequests)
		{
			while (true)
			{
				int observed = peakConcurrency;
				if (activeRequests <= observed || Interlocked.CompareExchange(ref peakConcurrency, activeRequests, observed) == observed)
					return;
			}
		}

		/// <summary>
		/// Reconstructs the file-specific portion of one update result from a previously applied public-file checkpoint.
		/// </summary>
		private static ModInfo ApplyFileUpdateCheckpoint(ModInfo modInfo, RepositoryFileUpdateCheckpoint checkpoint, string currentFilename)
		{
			if (checkpoint.FileMetadataResolved)
			{
				modInfo = new ModInfo(AutoTagger.CombineInfo(modInfo, new ModFileInfo(
					checkpoint.ResolvedDownloadId,
					checkpoint.ResolvedFilename,
					checkpoint.ResolvedName,
					checkpoint.HumanReadableVersion)));
			}
			else
			{
				if (ModFileIdentity.IsUsableRepositoryId(checkpoint.ResolvedDownloadId))
					modInfo.DownloadId = checkpoint.ResolvedDownloadId;

				modInfo.HumanReadableVersion = null;
				modInfo.LastKnownVersion = null;
				modInfo.MachineVersion = null;
			}

			if (string.IsNullOrWhiteSpace(modInfo.FileName) && !string.IsNullOrWhiteSpace(currentFilename))
				modInfo.FileName = currentFilename;

			return modInfo;
		}

		/// <summary>
		/// Captures only the public file-resolution data required to reproduce the existing update result on an unchanged provider timestamp.
		/// </summary>
		private RepositoryFileUpdateCheckpoint CreateFileUpdateCheckpoint(
			FileListInfoRequest request,
			RepositoryModUpdate providerUpdate,
			NexusV1ModFile latestFile,
			int latestFileId,
			bool fileMetadataResolved)
		{
			return new RepositoryFileUpdateCheckpoint(
				GameDomainName,
				request.ModId,
				providerUpdate.LatestFileUpdateUtc,
				request.DownloadId,
				request.CurrentFilename,
				fileMetadataResolved && latestFile != null ? latestFile.FileId.ToString() : latestFileId > 0 ? latestFileId.ToString() : null,
				fileMetadataResolved && latestFile != null ? latestFile.FileName : null,
				fileMetadataResolved && latestFile != null ? latestFile.Name : null,
				fileMetadataResolved && latestFile != null ? latestFile.ModVersion : null,
				fileMetadataResolved);
		}

		/// <summary>
		/// Resolves parent metadata in bounded GraphQL batches while leaving missing, incomplete, or failed identities for REST v1 fallback.
		/// </summary>
		private NexusLegacyModLookupResult ResolveGraphQlParentMetadata(IEnumerable<int> uniqueModIds)
		{
			if (_apiCallManager.V1 == null)
				return null;

			NexusLegacyModLookupResult result = _apiCallManager.NexusService.LegacyModsGraphQl
				.GetModsByDomainAsync(GameDomainName, uniqueModIds)
				.GetAwaiter()
				.GetResult();

			if (result?.ApiException != null)
			{
				Trace.TraceWarning(
					"NMM GraphQL parent metadata unavailable; using REST v1 fallback. kind={0}, status={1}.",
					result.ApiException.ErrorKind,
					result.ApiException.StatusCode.HasValue ? ((int)result.ApiException.StatusCode.Value).ToString() : "none");
			}
			else if (result?.UnexpectedException != null)
			{
				Trace.TraceWarning("NMM GraphQL parent metadata failed unexpectedly; using REST v1 fallback.");
				TraceUtil.TraceException(result.UnexpectedException);
			}
			else if (result != null && result.IsIncomplete)
			{
				Trace.TraceWarning("NMM GraphQL parent metadata was incomplete; missing identities will use REST v1 fallback.");
			}

			return result;
		}

		/// <summary>
		/// Formats the observed quota consumption for one API surface without inventing missing provider values.
		/// </summary>
		private static string FormatQuotaDelta(NexusRateLimitSnapshot before, NexusRateLimitSnapshot after)
		{
			if (before == null || after == null)
				return "unknown";

			if (before.HourlyRemaining.HasValue && after.HourlyRemaining.HasValue)
			{
				int hourlyDelta = before.HourlyRemaining.Value - after.HourlyRemaining.Value;
				return hourlyDelta >= 0 ? hourlyDelta.ToString() : "unknown";
			}

			if (before.DailyRemaining.HasValue && after.DailyRemaining.HasValue)
			{
				int dailyDelta = before.DailyRemaining.Value - after.DailyRemaining.Value;
				return dailyDelta >= 0 ? dailyDelta.ToString() : "unknown";
			}

			return "unknown";
		}

		/// <summary>
		/// Parses all requested archive identities once while retaining malformed rows for aligned empty results.
		/// </summary>
		private List<FileListInfoRequest> ParseFileListInfoRequests(List<string> modFileList)
		{
			var requests = new List<FileListInfoRequest>(modFileList.Count);
			for (int index = 0; index < modFileList.Count; index++)
			{
				string source = modFileList[index];
				try
				{
					string modId = ParseModId(source);
					string downloadId = ParseDownloadId(source);
					string currentFilename = ParseFilename(source);
					requests.Add(new FileListInfoRequest(index, source, Convert.ToInt32(modId), downloadId, currentFilename));
				}
				catch (Exception ex)
				{
					requests.Add(new FileListInfoRequest(index, source, ex));
				}
			}

			return requests;
		}

		/// <summary>
		/// Applies the repository's existing API-failure reaction once for a coalesced provider request.
		/// </summary>
		private bool HandleOperationRequestFailure<T>(string source, OperationRequestResult<T> result) where T : class
		{
			if (result.FailureHandled)
				return false;

			result.FailureHandled = true;
			if (result.ApiException != null)
				return ReactToApiException(result.ApiException);

			TraceFileListInfoException(source, result.UnexpectedException);
			return false;
		}

		/// <summary>
		/// Preserves the legacy per-row diagnostic for malformed identities and unexpected metadata failures.
		/// </summary>
		private static void TraceFileListInfoException(string source, Exception exception)
		{
			Trace.TraceError($"Exception while parsing mod ID from mod \"{source}\".");
			TraceUtil.TraceException(exception);
		}

		/// <summary>
		/// Resolves the local archive's current legacy file identity from its stored ID or filename fallback.
		/// </summary>
		private static int ResolveCurrentFileId(FileListInfoRequest request, NexusV1ModFileList nexusFiles)
		{
			int currentFileId = 0;
			if (ModFileIdentity.IsUsableRepositoryId(request.DownloadId))
			{
				Int32.TryParse(request.DownloadId, out currentFileId);
			}
			else if (!string.IsNullOrWhiteSpace(request.CurrentFilename) && nexusFiles?.Files != null)
			{
				var currentFile = nexusFiles.Files.FirstOrDefault(file =>
					string.Equals(file.FileName, request.CurrentFilename, StringComparison.OrdinalIgnoreCase));

				if (currentFile != null)
					currentFileId = currentFile.FileId;
				else if (nexusFiles.FileUpdates != null)
				{
					var filenameUpdate = nexusFiles.FileUpdates.FirstOrDefault(update =>
						string.Equals(update.OldFileName, request.CurrentFilename, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(update.NewFileName, request.CurrentFilename, StringComparison.OrdinalIgnoreCase));

					if (filenameUpdate != null)
					{
						currentFileId = string.Equals(filenameUpdate.OldFileName, request.CurrentFilename, StringComparison.OrdinalIgnoreCase)
							? filenameUpdate.OldFileId
							: filenameUpdate.NewFileId;
					}
				}
			}

			return currentFileId;
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
			return GetUpdatedWithMetadata(period).Select(update => update.ModId).ToList();
		}

		/// <inheritdoc cref="IModRepository"/>
		public List<RepositoryModUpdate> GetUpdatedWithMetadata(string period)
		{
			List<RepositoryModUpdate> updatedMods = new List<RepositoryModUpdate>();

			try
			{
				var client = _apiCallManager.V1;
				if (client == null)
					return updatedMods;

				NexusV1ModUpdate[] updates = client.GetUpdatedModsAsync(GameDomainName, period).GetAwaiter().GetResult();
				if (updates != null && updates.Length > 0)
					updatedMods = updates.Select(NexusV1Mapper.ToRepositoryModUpdate).Where(update => update != null).ToList();
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

				Task completedTask = await Task.WhenAny(action, Task.Delay(5000));
				if (!ReferenceEquals(completedTask, action))
				{
					Trace.TraceError("Timed out waiting for endorsement toggle to complete.");
					return null;
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
