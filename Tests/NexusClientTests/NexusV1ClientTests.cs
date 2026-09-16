using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.OnlineServices.Infrastructure;
using Nexus.Client.OnlineServices.NexusMods;
using Nexus.Client.OnlineServices.NexusMods.V1;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>
    /// Verifies the NMM-owned Nexus Mods REST v1 client against deterministic response fixtures.
    /// </summary>
    public class NexusV1ClientTests
    {
        /// <summary>
        /// Ensures mod metadata uses the legacy v1 route and preserves fields consumed by NMM.
        /// </summary>
        [Test]
        public void GetMod_UsesV1RouteAndDeserializesLegacyFields()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(ModFixture, request => capture = RequestCapture.From(request)))
            {
                service.ReplaceCredentials(NexusCredentials.FromApiKey("test-key"));
                NexusV1Mod mod = service.V1.GetModAsync("skyrimspecialedition", 42).GetAwaiter().GetResult();

                Assert.AreEqual("/v1/games/skyrimspecialedition/mods/42.json", capture.PathAndQuery);
                Assert.AreEqual("test-key", capture.ApiKey);
                Assert.AreEqual(42, mod.ModId);
                Assert.AreEqual("Fixture Mod", mod.Name);
                Assert.AreEqual("2.5", mod.Version);
                Assert.AreEqual(7, mod.CategoryId);
                Assert.AreEqual(NexusV1EndorsementStatus.Endorsed, mod.Endorsement.Status);
            }
        }

        /// <summary>
        /// Ensures file-list parsing preserves file update chains and legacy category-filter behavior.
        /// </summary>
        [Test]
        public void GetModFiles_PreservesLegacyCategoryFilteringAndFileUpdates()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(ModFilesFixture, request => capture = RequestCapture.From(request)))
            {
                NexusV1ModFileList files = service.V1.GetModFilesAsync(
                    "skyrim",
                    12,
                    NexusV1FileCategory.Main,
                    NexusV1FileCategory.Deleted,
                    NexusV1FileCategory.Old,
                    NexusV1FileCategory.Main).GetAwaiter().GetResult();

                Assert.AreEqual("/v1/games/skyrim/mods/12/files.json?category=main,old_version", capture.PathAndQuery);
                Assert.AreEqual(2, files.Files.Length);
                Assert.AreEqual(100, files.Files[0].FileId);
                Assert.AreEqual("1.0", files.Files[0].ModVersion);
                Assert.AreEqual(200, files.FileUpdates[0].NewFileId);
                Assert.AreEqual("old.zip", files.FileUpdates[0].OldFileName);
            }
        }

        /// <summary>
        /// Ensures a Deleted-only legacy filter is omitted, matching the previous v1 fetch-all behavior.
        /// </summary>
        [Test]
        public void GetModFiles_DeletedOnlyFilterIsOmitted()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(ModFilesFixture, request => capture = RequestCapture.From(request)))
            {
                service.V1.GetModFilesAsync("skyrim", 12, NexusV1FileCategory.Deleted).GetAwaiter().GetResult();
                Assert.AreEqual("/v1/games/skyrim/mods/12/files.json", capture.PathAndQuery);
            }
        }

        /// <summary>
        /// Ensures individual file metadata uses the legacy v1 route and fields needed by NMM.
        /// </summary>
        [Test]
        public void GetModFile_UsesV1RouteAndDeserializesFileMetadata()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(ModFileFixture, request => capture = RequestCapture.From(request)))
            {
                NexusV1ModFile file = service.V1.GetModFileAsync("skyrim", 12, 200).GetAwaiter().GetResult();

                Assert.AreEqual("/v1/games/skyrim/mods/12/files/200.json", capture.PathAndQuery);
                Assert.AreEqual(200, file.FileId);
                Assert.AreEqual("new.zip", file.FileName);
                Assert.AreEqual("2.0", file.ModVersion);
            }
        }

        /// <summary>
        /// Ensures MD5 lookup retains both mod and file identity without additional requests.
        /// </summary>
        [Test]
        public void FindModsByMd5_DeserializesModAndFileDetails()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(HashFixture, request => capture = RequestCapture.From(request)))
            {
                NexusV1ModHashResult[] results = service.V1.FindModsByMd5Async("fallout4", "ABC123").GetAwaiter().GetResult();

                Assert.AreEqual("/v1/games/fallout4/mods/md5_search/ABC123.json", capture.PathAndQuery);
                Assert.AreEqual(1, results.Length);
                Assert.AreEqual(77, results[0].Mod.ModId);
                Assert.AreEqual(9001, results[0].File.FileId);
                Assert.AreEqual("abc123", results[0].File.Md5);
            }
        }

        /// <summary>
        /// Ensures updated-mod requests keep the legacy period query and deserialize update records.
        /// </summary>
        [Test]
        public void GetUpdatedMods_UsesPeriodQuery()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(UpdatesFixture, request => capture = RequestCapture.From(request)))
            {
                NexusV1ModUpdate[] updates = service.V1.GetUpdatedModsAsync("skyrim", "1m").GetAwaiter().GetResult();

                Assert.AreEqual("/v1/games/skyrim/mods/updated.json?period=1m", capture.PathAndQuery);
                Assert.AreEqual(123, updates[0].ModId);
                Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1700000000), updates[0].LatestFileUpdate);
            }
        }

        /// <summary>
        /// Ensures the v1 game's historical false parent-category value remains compatible with legacy parsing.
        /// </summary>
        [Test]
        public void GetGame_NormalizesFalseParentCategoryToNull()
        {
            using (NexusModsService service = CreateService(GameFixture))
            {
                NexusV1Game game = service.V1.GetGameAsync("skyrim").GetAwaiter().GetResult();

                Assert.AreEqual(110, game.Id);
                Assert.AreEqual(2, game.Categories.Length);
                Assert.IsNull(game.Categories[0].ParentCategory);
                Assert.AreEqual(10, game.Categories[1].ParentCategory);
            }
        }

        /// <summary>
        /// Ensures Premium download resolution uses the credentialed route without website key parameters.
        /// </summary>
        [Test]
        public void GetDownloadLinks_PremiumFlowUsesBareRouteAndDeserializesSources()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(DownloadLinksFixture, request => capture = RequestCapture.From(request)))
            {
                service.ReplaceCredentials(NexusCredentials.FromApiKey("test-key"));
                NexusV1DownloadLink[] links = service.V1.GetDownloadLinksAsync("skyrim", 12, 200).GetAwaiter().GetResult();

                Assert.AreEqual(HttpMethod.Get, capture.Method);
                Assert.AreEqual("/v1/games/skyrim/mods/12/files/200/download_link.json", capture.PathAndQuery);
                Assert.AreEqual("test-key", capture.ApiKey);
                Assert.AreEqual(2, links.Length);
                Assert.AreEqual("nexuscdn", links[0].CdnShortName);
                Assert.AreEqual(new Uri("https://cdn.example.test/file.zip"), links[0].Uri);
            }
        }

        /// <summary>
        /// Ensures free-account download resolution forwards the website-issued key and expiry exactly once.
        /// </summary>
        [Test]
        public void GetDownloadLinks_FreeFlowUsesKeyAndExpiryQuery()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(DownloadLinksFixture, request => capture = RequestCapture.From(request)))
            {
                service.V1.GetDownloadLinksAsync("fallout4", 77, 9001, "key + value", 1700000123).GetAwaiter().GetResult();

                Assert.AreEqual(HttpMethod.Get, capture.Method);
                Assert.AreEqual("/v1/games/fallout4/mods/77/files/9001/download_link.json?key=key + value&expires=1700000123", capture.PathAndQuery);
            }
        }

        /// <summary>
        /// Ensures owned v1 download sources preserve the repository URI and CDN display name.
        /// </summary>
        [Test]
        public void V1Mapper_DownloadLinkPreservesRepositorySemantics()
        {
            var source = new NexusV1DownloadLink
            {
                CdnName = "Nexus CDN",
                CdnShortName = "nexuscdn",
                Uri = new Uri("https://cdn.example.test/file.zip")
            };

            RepositoryDownloadLink result = (RepositoryDownloadLink)InvokeV1Mapper("ToRepositoryDownloadLink", source);

            Assert.AreEqual(source.Uri, result.Uri);
            Assert.AreEqual("nexuscdn", result.SourceName);
        }

        /// <summary>
        /// Ensures temporary free-download authorization keys are redacted from diagnostics.
        /// </summary>
        [Test]
        public void DownloadKey_IsMarkedSensitiveForDiagnostics()
        {
            Uri uri = new Uri("https://api.nexusmods.com/v1/games/skyrim/mods/1/files/2/download_link.json?key=temporary-secret&expires=1700000123");
            string sanitized = ApiDiagnosticSanitizer.SanitizeUri(uri, NexusRequestPolicy.SensitiveQueryParameterNames);

            StringAssert.DoesNotContain("temporary-secret", sanitized);
            StringAssert.Contains("redacted", sanitized);
            StringAssert.Contains("expires=1700000123", sanitized);
        }

        /// <summary>
        /// Ensures the free-download overload preserves the legacy empty-key request shape for provider-side validation.
        /// </summary>
        [Test]
        public void GetDownloadLinks_FreeFlowAllowsEmptyKeyForProviderValidation()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(DownloadLinksFixture, request => capture = RequestCapture.From(request)))
            {
                service.V1.GetDownloadLinksAsync("skyrim", 12, 200, string.Empty, -1).GetAwaiter().GetResult();
                Assert.AreEqual("/v1/games/skyrim/mods/12/files/200/download_link.json?key=&expires=-1", capture.PathAndQuery);
            }
        }

        /// <summary>
        /// Ensures endorsement uses the legacy action route and case-sensitive Version JSON property.
        /// </summary>
        [Test]
        public void Endorse_UsesPostAndLegacyVersionBody()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService("{}", request => capture = RequestCapture.From(request)))
            {
                service.V1.EndorseAsync("skyrim", 42, "2.5").GetAwaiter().GetResult();

                Assert.AreEqual(HttpMethod.Post, capture.Method);
                Assert.AreEqual("/v1/games/skyrim/mods/42/endorse.json", capture.PathAndQuery);
                Assert.AreEqual("{\"Version\":\"2.5\"}", capture.Body);
            }
        }

        /// <summary>
        /// Ensures unendorsement preserves the legacy abstain action and request body.
        /// </summary>
        [Test]
        public void Unendorse_UsesAbstainRoute()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService("{}", request => capture = RequestCapture.From(request)))
            {
                service.V1.UnendorseAsync("skyrim", 42, "2.5").GetAwaiter().GetResult();

                Assert.AreEqual(HttpMethod.Post, capture.Method);
                Assert.AreEqual("/v1/games/skyrim/mods/42/abstain.json", capture.PathAndQuery);
                Assert.AreEqual("{\"Version\":\"2.5\"}", capture.Body);
            }
        }

        /// <summary>
        /// Ensures mutations are never automatically retried after a provider failure.
        /// </summary>
        [Test]
        public void Endorse_ServerFailureIsNotRetried()
        {
            int calls = 0;
            var handler = new StubHttpMessageHandler((request, token) =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"message\":\"failure\"}")
                });
            });
            using (var service = new NexusModsService(new ApiTransport(handler, TimeSpan.FromSeconds(5)), "NMM-Test/1.0", "NMM-Test", "1.0"))
            {
                ApiException exception = Assert.Throws<ApiException>(() => service.V1.EndorseAsync("skyrim", 42, "2.5").GetAwaiter().GetResult());

                Assert.AreEqual(ApiErrorKind.ServerError, exception.ErrorKind);
                Assert.AreEqual(1, calls);
            }
        }

        /// <summary>
        /// Ensures owned v1 mod metadata maps to the same repository fields expected by existing consumers.
        /// </summary>
        [Test]
        public void V1Mapper_ModPreservesLegacyRepositorySemantics()
        {
            var source = new NexusV1Mod
            {
                ModId = 42,
                Name = "Fixture Mod",
                Description = "Description",
                DomainName = "skyrimspecialedition",
                CategoryId = 7,
                Version = "v2.5-beta",
                Author = "Author",
                Endorsement = new NexusV1Endorsement { Status = NexusV1EndorsementStatus.Endorsed }
            };

            ModInfo result = (ModInfo)InvokeV1Mapper("ToModInfo", source);

            Assert.AreEqual("42", result.Id);
            Assert.AreEqual("Fixture Mod", result.ModName);
            Assert.AreEqual("v2.5-beta", result.HumanReadableVersion);
            Assert.AreEqual("v2.5-beta", result.LastKnownVersion);
            Assert.AreEqual(new Version(2, 5), result.MachineVersion);
            Assert.AreEqual(true, result.IsEndorsed);
            Assert.AreEqual(7, result.CategoryId);
            Assert.AreEqual("Author", result.Author);
            Assert.AreEqual(new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/42"), result.Website);
        }

        /// <summary>
        /// Ensures owned v1 file metadata keeps the legacy repository identity and mod-version semantics.
        /// </summary>
        [Test]
        public void V1Mapper_FilePreservesLegacyRepositorySemantics()
        {
            var source = new NexusV1ModFile
            {
                FileId = 200,
                FileName = "specific-file.7z",
                Name = "Specific File",
                FileVersion = "file-version",
                ModVersion = "1.2"
            };

            ModFileInfo result = (ModFileInfo)InvokeV1Mapper("ToModFileInfo", source);

            Assert.AreEqual("200", result.Id);
            Assert.AreEqual("specific-file.7z", result.Filename);
            Assert.AreEqual("Specific File", result.Name);
            Assert.AreEqual("1.2", result.HumanReadableVersion);
        }

        /// <summary>
        /// Ensures complete owned quota observations map directly into repository rate-limit state.
        /// </summary>
        [Test]
        public void V1Mapper_CompleteRateLimitSnapshotMapsRepositoryState()
        {
            DateTimeOffset dailyReset = new DateTimeOffset(2030, 1, 2, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset hourlyReset = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
            var owned = new NexusRateLimitSnapshot(2500, 1900, dailyReset, 500, 320, hourlyReset);

            RepositoryRateLimit result = (RepositoryRateLimit)InvokeV1Mapper("ToRepositoryRateLimit", owned);

            Assert.AreEqual(2500, result.DailyLimit);
            Assert.AreEqual(1900, result.DailyRemaining);
            Assert.AreEqual(500, result.HourlyLimit);
            Assert.AreEqual(320, result.HourlyRemaining);
            Assert.AreEqual(dailyReset, result.DailyReset);
            Assert.AreEqual(hourlyReset, result.HourlyReset);
        }

        /// <summary>
        /// Ensures incomplete quota observations remain unknown instead of inventing concrete reset data.
        /// </summary>
        [Test]
        public void V1Mapper_IncompleteRateLimitSnapshotReturnsNull()
        {
            var owned = new NexusRateLimitSnapshot(null, null, null, 500, 320, null);

            RepositoryRateLimit result = (RepositoryRateLimit)InvokeV1Mapper("ToRepositoryRateLimit", owned);

            Assert.IsNull(result);
        }

        /// <summary>
        /// Ensures owned v1 category metadata preserves nullable parent-category semantics.
        /// </summary>
        [Test]
        public void V1Mapper_CategoryPreservesLegacyRepositorySemantics()
        {
            CategoriesInfo result = (CategoriesInfo)InvokeV1Mapper("ToCategoriesInfo", new NexusV1GameCategory
            {
                Id = 20,
                ParentCategory = 10,
                Name = "Child"
            });

            Assert.AreEqual(20, result.Id);
            Assert.AreEqual(10, result.ParentId);
            Assert.AreEqual("Child", result.Name);
        }

        /// <summary>
        /// Ensures account validation uses the legacy v1 route and preserves the account flags NMM consumes.
        /// </summary>
        [Test]
        public void ValidateUser_UsesV1RouteAndDeserializesAccountFlags()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(UserFixture, request => capture = RequestCapture.From(request)))
            {
                service.ReplaceCredentials(NexusCredentials.FromApiKey("test-key"));
                NexusV1User user = service.V1.ValidateUserAsync().GetAwaiter().GetResult();

                Assert.AreEqual("/v1/users/validate.json", capture.PathAndQuery);
                Assert.AreEqual("test-key", capture.ApiKey);
                Assert.AreEqual("FixtureUser", user.Name);
                Assert.IsTrue(user.IsPremium);
                Assert.IsFalse(user.IsSupporter);
            }
        }

        /// <summary>
        /// Ensures account DTO mapping preserves the existing repository-facing user contract.
        /// </summary>
        [Test]
        public void V1Mapper_UserPreservesRepositoryAccountSemantics()
        {
            var source = new NexusV1User
            {
                Name = "FixtureUser",
                IsPremium = true,
                IsSupporter = false
            };

            RepositoryUserStatus result = (RepositoryUserStatus)InvokeV1Mapper("ToRepositoryUserStatus", source);

            Assert.AreEqual("FixtureUser", result.Name);
            Assert.IsTrue(result.IsPremium);
            Assert.IsFalse(result.IsSupporter);
        }

        /// <summary>
        /// Ensures owned authentication failures retain the repository's legacy result categories.
        /// </summary>
        [Test]
        public void RepositoryAuthenticationErrorMapping_PreservesLegacyStatuses()
        {
            Assert.AreEqual(AuthenticationStatus.InvalidKey, InvokeAuthenticationErrorMapper(new ApiException(ApiErrorKind.Authentication, "invalid")));
            Assert.AreEqual(AuthenticationStatus.NetworkError, InvokeAuthenticationErrorMapper(new ApiException(ApiErrorKind.Network, "network")));
            Assert.AreEqual(AuthenticationStatus.NetworkError, InvokeAuthenticationErrorMapper(new ApiException(ApiErrorKind.Timeout, "timeout")));
            Assert.AreEqual(AuthenticationStatus.Unknown, InvokeAuthenticationErrorMapper(new ApiException(ApiErrorKind.Forbidden, "forbidden")));
        }

        /// <summary>
        /// Ensures invalid API keys are classified from HTTP status rather than provider message text.
        /// </summary>
        [Test]
        public void ValidateUser_UnauthorizedIsAuthenticationError()
        {
            using (NexusModsService service = CreateService("{\"code\":401,\"message\":\"Please provide a valid API Key\"}", statusCode: HttpStatusCode.Unauthorized))
            {
                service.ReplaceCredentials(NexusCredentials.FromApiKey("invalid-key"));
                ApiException exception = Assert.Throws<ApiException>(() => service.V1.ValidateUserAsync().GetAwaiter().GetResult());
                Assert.AreEqual(ApiErrorKind.Authentication, exception.ErrorKind);
            }
        }

        /// <summary>
        /// Ensures invalid JSON is surfaced through the NMM-owned invalid-response error category.
        /// </summary>
        [Test]
        public void InvalidJson_IsMappedToInvalidResponse()
        {
            using (NexusModsService service = CreateService("{not-json"))
            {
                ApiException exception = Assert.Throws<ApiException>(() => service.V1.GetModAsync("skyrim", 1).GetAwaiter().GetResult());
                Assert.AreEqual(ApiErrorKind.InvalidResponse, exception.ErrorKind);
                Assert.AreEqual(HttpStatusCode.OK, exception.StatusCode);
            }
        }

        /// <summary>
        /// Ensures v1 responses flow through the shared provider service and update only the v1 quota snapshot.
        /// </summary>
        [Test]
        public void ReadRequest_UpdatesV1RateLimitSnapshot()
        {
            var headers = new Dictionary<string, string>
            {
                { "x-rl-hourly-limit", "500" },
                { "x-rl-hourly-remaining", "321" },
                { "x-rl-hourly-reset", "2030-01-01T01:00:00Z" }
            };
            using (NexusModsService service = CreateService(ModFixture, headers: headers))
            {
                service.V1.GetModAsync("skyrim", 42).GetAwaiter().GetResult();

                Assert.AreEqual(321, service.RateLimits.GetSnapshot(NexusApiSurface.V1).HourlyRemaining);
                Assert.IsNull(service.RateLimits.GetSnapshot(NexusApiSurface.GraphQl).HourlyRemaining);
            }
        }

        /// <summary>
        /// Creates a service backed by one deterministic HTTP response fixture.
        /// </summary>
        private static NexusModsService CreateService(string responseJson, Action<HttpRequestMessage> requestObserver = null, IDictionary<string, string> headers = null, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var handler = new StubHttpMessageHandler((request, token) =>
            {
                requestObserver?.Invoke(request);
                var response = new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(responseJson)
                };
                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> header in headers)
                        response.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                return Task.FromResult(response);
            });
            return new NexusModsService(new ApiTransport(handler, TimeSpan.FromSeconds(5)), "NMM-Test/1.0", "NMM-Test", "1.0");
        }

        /// <summary>
        /// Invokes the internal repository mapper without widening its production visibility.
        /// </summary>
        private static object InvokeV1Mapper(string methodName, params object[] values)
        {
            Type mapperType = typeof(NexusModsApiRepository).Assembly.GetType("Nexus.Client.ModRepositories.NexusV1Mapper", true);
            MethodInfo method = mapperType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method);
            return method.Invoke(null, values);
        }

        /// <summary>
        /// Invokes the repository's private authentication-error compatibility mapper.
        /// </summary>
        private static AuthenticationStatus InvokeAuthenticationErrorMapper(ApiException exception)
        {
            MethodInfo method = typeof(NexusModsApiRepository).GetMethod("MapAuthenticationError", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);
            return (AuthenticationStatus)method.Invoke(null, new object[] { exception });
        }

        /// <summary>
        /// Captures request information before the service disposes its request message.
        /// </summary>
        private sealed class RequestCapture
        {
            public HttpMethod Method { get; private set; }

            public string PathAndQuery { get; private set; }

            public string ApiKey { get; private set; }

            public string Body { get; private set; }

            /// <summary>
            /// Copies the request values relevant to v1 route and authentication assertions.
            /// </summary>
            public static RequestCapture From(HttpRequestMessage request)
            {
                request.Headers.TryGetValues("apikey", out IEnumerable<string> apiKeys);
                return new RequestCapture
                {
                    Method = request.Method,
                    PathAndQuery = request.RequestUri.AbsolutePath + Uri.UnescapeDataString(request.RequestUri.Query),
                    ApiKey = apiKeys == null ? null : string.Join(",", apiKeys),
                    Body = request.Content == null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                };
            }
        }

        /// <summary>
        /// Provides deterministic HTTP responses without external Nexus Mods access.
        /// </summary>
        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

            /// <summary>
            /// Initializes the handler with the deterministic response function.
            /// </summary>
            public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
            {
                _send = send ?? throw new ArgumentNullException(nameof(send));
            }

            /// <summary>
            /// Delegates request execution to the configured fixture function.
            /// </summary>
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _send(request, cancellationToken);
            }
        }

        private const string UserFixture = "{\"user_id\":123,\"name\":\"FixtureUser\",\"is_premium\":true,\"is_supporter\":false}";
        private const string ModFixture = "{\"mod_id\":42,\"name\":\"Fixture Mod\",\"description\":\"Fixture description\",\"domain_name\":\"skyrimspecialedition\",\"category_id\":7,\"version\":\"2.5\",\"author\":\"Fixture Author\",\"endorsement\":{\"endorse_status\":\"Endorsed\",\"version\":\"2.5\"}}";
        private const string ModFileFixture = "{\"file_id\":200,\"name\":\"New File\",\"file_name\":\"new.zip\",\"mod_version\":\"2.0\",\"category_id\":1,\"uploaded_timestamp\":1700000000}";
        private const string ModFilesFixture = "{\"files\":[{\"file_id\":100,\"name\":\"Old File\",\"file_name\":\"old.zip\",\"mod_version\":\"1.0\",\"category_id\":4,\"uploaded_timestamp\":1600000000},{\"file_id\":200,\"name\":\"New File\",\"file_name\":\"new.zip\",\"mod_version\":\"2.0\",\"category_id\":1,\"uploaded_timestamp\":1700000000}],\"file_updates\":[{\"old_file_id\":100,\"old_file_name\":\"old.zip\",\"new_file_id\":200,\"new_file_name\":\"new.zip\"}]}";
        private const string HashFixture = "[{\"mod\":{\"mod_id\":77,\"name\":\"Hash Mod\",\"domain_name\":\"fallout4\",\"category_id\":1,\"version\":\"3.0\",\"author\":\"Author\"},\"file_details\":{\"file_id\":9001,\"name\":\"Hash File\",\"file_name\":\"hash.zip\",\"mod_version\":\"3.1\",\"category_id\":1,\"uploaded_timestamp\":1700000000,\"md5\":\"abc123\"}}]";
        private const string UpdatesFixture = "[{\"mod_id\":123,\"latest_file_update\":1700000000,\"latest_mod_activity\":1700000100}]";
        private const string GameFixture = "{\"id\":110,\"domain_name\":\"skyrim\",\"name\":\"Skyrim\",\"categories\":[{\"category_id\":10,\"name\":\"Root\",\"parent_category\":false},{\"category_id\":11,\"name\":\"Child\",\"parent_category\":10}]}";
        private const string DownloadLinksFixture = "[{\"name\":\"Nexus CDN\",\"short_name\":\"nexuscdn\",\"URI\":\"https://cdn.example.test/file.zip\"},{\"name\":\"Mirror\",\"short_name\":\"mirror\",\"URI\":\"https://mirror.example.test/file.zip\"}]";
    }
}
