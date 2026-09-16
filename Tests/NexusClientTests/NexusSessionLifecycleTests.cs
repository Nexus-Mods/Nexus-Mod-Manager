namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Nexus.Client;
    using Nexus.Client.ModRepositories;
    using Nexus.Client.OnlineServices.Infrastructure;
    using Nexus.Client.OnlineServices.NexusMods;
    using Nexus.Client.OnlineServices.NexusMods.V1;
    using Nexus.Client.Settings;
    using NUnit.Framework;

    /// <summary>
    /// Verifies the NMM-owned Nexus credential and session lifecycle used by repository API calls.
    /// </summary>
    [TestFixture]
    public class NexusSessionLifecycleTests
    {
        /// <summary>
        /// Ensures credential replacement invalidates the previous session while logout clears owned authentication state.
        /// </summary>
        [Test]
        public void CredentialReplacementAndLogout_UpdateOwnedSessionState()
        {
            string apiKey = "first-api-key";
            int saveCount = 0;
            ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_ApiKey":
                        return apiKey;
                    case "set_ApiKey":
                        apiKey = (string)args[0];
                        return null;
                    case "Save":
                        saveCount++;
                        return null;
                    default:
                        return null;
                }
            });
            IEnvironmentInfo environmentInfo = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
                method.Name == "get_Settings" ? settings : null);

            ApiCallManager manager = CreateManager(environmentInfo);
            NexusModsService service = GetOwnedService(manager);
            try
            {
                NexusSessionContext firstSession = service.CaptureSession();
                Assert.AreEqual(NexusCredentialKind.ApiKey, firstSession.Credentials.Kind);
                Assert.AreEqual("first-api-key", GetRequestApiKey(service, firstSession));
                Assert.IsNotNull(GetOwnedV1Client(manager));

                settings.ApiKey = "replacement-api-key";
                manager.UpdateNexusClient();

                NexusSessionContext replacementSession = service.CaptureSession();
                Assert.Greater(replacementSession.Generation, firstSession.Generation);
                Assert.IsTrue(firstSession.Token.IsCancellationRequested);
                Assert.AreEqual(NexusCredentialKind.ApiKey, replacementSession.Credentials.Kind);
                Assert.AreEqual("replacement-api-key", GetRequestApiKey(service, replacementSession));
                Assert.IsNotNull(GetOwnedV1Client(manager));

                manager.ClearApiKey();

                NexusSessionContext loggedOutSession = service.CaptureSession();
                Assert.Greater(loggedOutSession.Generation, replacementSession.Generation);
                Assert.IsTrue(replacementSession.Token.IsCancellationRequested);
                Assert.AreEqual(NexusCredentialKind.None, loggedOutSession.Credentials.Kind);
                Assert.IsNull(GetRequestApiKey(service, loggedOutSession));
                Assert.IsNull(GetOwnedV1Client(manager));
                Assert.AreEqual(string.Empty, settings.ApiKey);
                Assert.AreEqual(1, saveCount);
            }
            finally
            {
                service.Dispose();
            }
        }

        /// <summary>
        /// Ensures a settings persistence failure cannot leave the owned Nexus service authenticated.
        /// </summary>
        [Test]
        public void ClearApiKey_SaveFailureStillInvalidatesOwnedCredentials()
        {
            string apiKey = "api-key";
            ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_ApiKey":
                        return apiKey;
                    case "set_ApiKey":
                        apiKey = (string)args[0];
                        return null;
                    case "Save":
                        throw new InvalidOperationException("Simulated settings persistence failure.");
                    default:
                        return null;
                }
            });
            IEnvironmentInfo environmentInfo = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
                method.Name == "get_Settings" ? settings : null);

            ApiCallManager manager = CreateManager(environmentInfo);
            NexusModsService service = GetOwnedService(manager);
            try
            {
                NexusSessionContext authenticatedSession = service.CaptureSession();

                Assert.Throws<InvalidOperationException>(() => manager.ClearApiKey());

                NexusSessionContext clearedSession = service.CaptureSession();
                Assert.Greater(clearedSession.Generation, authenticatedSession.Generation);
                Assert.IsTrue(authenticatedSession.Token.IsCancellationRequested);
                Assert.AreEqual(NexusCredentialKind.None, clearedSession.Credentials.Kind);
                Assert.AreEqual(string.Empty, apiKey);
                Assert.IsNull(GetOwnedV1Client(manager));
            }
            finally
            {
                service.Dispose();
            }
        }

        /// <summary>
        /// Ensures repository account state is cleared even when persisting the logout setting fails.
        /// </summary>
        [Test]
        public void RepositoryLogout_SaveFailureStillClearsPublishedAccountState()
        {
            string apiKey = "api-key";
            ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_ApiKey":
                        return apiKey;
                    case "set_ApiKey":
                        apiKey = (string)args[0];
                        return null;
                    case "Save":
                        throw new InvalidOperationException("Simulated settings persistence failure.");
                    default:
                        return null;
                }
            });
            IEnvironmentInfo environmentInfo = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
                method.Name == "get_Settings" ? settings : null);

            ApiCallManager manager = CreateManager(environmentInfo);
            NexusModsService service = GetOwnedService(manager);
            try
            {
                var repository = new NexusModsApiRepository("skyrim", manager);
                FieldInfo userStatusField = typeof(NexusModsApiRepository).GetField("_userStatus", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(userStatusField);
                userStatusField.SetValue(repository, new RepositoryUserStatus("Existing User", true, false));

                int updateCount = 0;
                repository.UserStatusUpdate += (sender, args) => updateCount++;

                Assert.Throws<InvalidOperationException>(() => repository.Logout());

                Assert.IsNull(repository.UserStatus);
                Assert.AreEqual(1, updateCount);
                Assert.AreEqual(string.Empty, apiKey);
                Assert.AreEqual(NexusCredentialKind.None, service.CaptureSession().Credentials.Kind);
            }
            finally
            {
                service.Dispose();
            }
        }

        /// <summary>
        /// Ensures repository authentication notifications are raised only after credential lifecycle synchronization is released.
        /// </summary>
        [Test]
        public void AuthenticationNotification_IsRaisedOutsideCredentialLifecycleLock()
        {
            string apiKey = "api-key";
            ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_ApiKey":
                        return apiKey;
                    case "set_ApiKey":
                        apiKey = (string)args[0];
                        return null;
                    case "Save":
                        return null;
                    default:
                        return null;
                }
            });
            IEnvironmentInfo environmentInfo = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
                method.Name == "get_Settings" ? settings : null);

            ApiCallManager manager = CreateManager(environmentInfo);
            NexusModsService service = GetOwnedService(manager);
            try
            {
                var repository = new NexusModsApiRepository("skyrim", manager);
                FieldInfo userStatusField = typeof(NexusModsApiRepository).GetField("_userStatus", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo credentialSyncField = typeof(ApiCallManager).GetField("_credentialSync", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(userStatusField);
                Assert.IsNotNull(credentialSyncField);
                userStatusField.SetValue(repository, new RepositoryUserStatus("Existing User", true, false));
                object credentialSync = credentialSyncField.GetValue(manager);

                bool lockHeldDuringNotification = true;
                repository.UserStatusUpdate += (sender, args) =>
                {
                    lockHeldDuringNotification = System.Threading.Monitor.IsEntered(credentialSync);
                    throw new InvalidOperationException("Stop before network validation.");
                };

                Assert.Throws<InvalidOperationException>(() => repository.Authenticate());
                Assert.IsFalse(lockHeldDuringNotification);
                Assert.IsNull(repository.UserStatus);
            }
            finally
            {
                service.Dispose();
            }
        }

        /// <summary>
        /// Ensures a logout triggered by the successful account-state notification invalidates that authentication result.
        /// </summary>
        [Test]
        public void AuthenticationSuccessNotification_LogoutPreventsSuccessfulCompletion()
        {
            string apiKey = "api-key";
            ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_ApiKey":
                        return apiKey;
                    case "set_ApiKey":
                        apiKey = (string)args[0];
                        return null;
                    case "Save":
                        return null;
                    default:
                        return null;
                }
            });
            IEnvironmentInfo environmentInfo = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
                method.Name == "get_Settings" ? settings : null);

            ApiCallManager manager = CreateManager(environmentInfo);
            NexusModsService originalService = GetOwnedService(manager);
            var handler = new StubHttpMessageHandler((request, token) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"name\":\"Authenticated User\",\"is_premium\":true,\"is_supporter\":false}")
                }));

            using (var transport = new ApiTransport(handler, TimeSpan.FromSeconds(5)))
            using (var service = new NexusModsService(transport, "NMM-Test/1.0", "NMM-Test", "1.0"))
            {
                ReplaceOwnedService(manager, service);
                originalService.Dispose();
                manager.UpdateNexusClient();

                var repository = new NexusModsApiRepository("skyrim", manager);
                bool successNotificationObserved = false;
                repository.UserStatusUpdate += (sender, args) =>
                {
                    if (repository.UserStatus == null)
                        return;

                    successNotificationObserved = true;
                    repository.Logout();
                };

                AuthenticationStatus result = repository.Authenticate();

                Assert.IsTrue(successNotificationObserved);
                Assert.AreEqual(AuthenticationStatus.Unknown, result);
                Assert.IsNull(repository.UserStatus);
                Assert.AreEqual(string.Empty, apiKey);
                Assert.AreEqual(NexusCredentialKind.None, service.CaptureSession().Credentials.Kind);
            }
        }

        /// <summary>
        /// Ensures an obsolete authentication generation cannot publish account state or clear replacement credentials.
        /// </summary>
        [Test]
        public void AuthenticationPublicationAndClear_RejectSupersededSession()
        {
            string apiKey = "first-api-key";
            int saveCount = 0;
            ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_ApiKey":
                        return apiKey;
                    case "set_ApiKey":
                        apiKey = (string)args[0];
                        return null;
                    case "Save":
                        saveCount++;
                        return null;
                    default:
                        return null;
                }
            });
            IEnvironmentInfo environmentInfo = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
                method.Name == "get_Settings" ? settings : null);

            ApiCallManager manager = CreateManager(environmentInfo);
            NexusModsService service = GetOwnedService(manager);
            try
            {
                NexusSessionContext obsoleteSession = service.CaptureSession();
                settings.ApiKey = "replacement-api-key";
                manager.UpdateNexusClient();

                bool published = false;
                bool stateUpdateAccepted = InvokeNonPublic<bool>(
                    manager,
                    "TryUpdateAuthenticationState",
                    new[] { typeof(NexusSessionContext), typeof(Action) },
                    obsoleteSession,
                    new Action(() => published = true));
                bool clearAccepted = InvokeNonPublic<bool>(
                    manager,
                    "ClearApiKey",
                    new[] { typeof(NexusSessionContext) },
                    obsoleteSession);

                NexusSessionContext replacementSession = service.CaptureSession();
                Assert.IsFalse(stateUpdateAccepted);
                Assert.IsFalse(published);
                Assert.IsFalse(clearAccepted);
                Assert.AreEqual("replacement-api-key", apiKey);
                Assert.AreEqual(NexusCredentialKind.ApiKey, replacementSession.Credentials.Kind);
                Assert.AreEqual("replacement-api-key", GetRequestApiKey(service, replacementSession));
                Assert.AreEqual(0, saveCount);
            }
            finally
            {
                service.Dispose();
            }
        }

        /// <summary>
        /// Creates an isolated API-call manager without touching its process-wide singleton instance.
        /// </summary>
        private static ApiCallManager CreateManager(IEnvironmentInfo environmentInfo)
        {
            ConstructorInfo constructor = typeof(ApiCallManager).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(IEnvironmentInfo) },
                null);
            Assert.IsNotNull(constructor);
            return (ApiCallManager)constructor.Invoke(new object[] { environmentInfo });
        }

        /// <summary>
        /// Replaces the manager's owned service so repository lifecycle behavior can be exercised over a deterministic transport.
        /// </summary>
        private static void ReplaceOwnedService(ApiCallManager manager, NexusModsService service)
        {
            FieldInfo field = typeof(ApiCallManager).GetField("_nexusService", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(manager, service);
        }

        /// <summary>
        /// Reads the internal owned service used for future REST v1 migration without widening the production API surface.
        /// </summary>
        private static NexusModsService GetOwnedService(ApiCallManager manager)
        {
            PropertyInfo property = typeof(ApiCallManager).GetProperty("NexusService", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(property);
            return (NexusModsService)property.GetValue(manager, null);
        }

        /// <summary>
        /// Reads the owned v1 compatibility boundary without widening its production visibility.
        /// </summary>
        private static NexusV1Client GetOwnedV1Client(ApiCallManager manager)
        {
            PropertyInfo property = typeof(ApiCallManager).GetProperty("V1", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(property);
            return (NexusV1Client)property.GetValue(manager, null);
        }

        /// <summary>
        /// Invokes one non-public API-call-manager method without widening its production visibility for tests.
        /// </summary>
        private static T InvokeNonPublic<T>(ApiCallManager manager, string methodName, Type[] parameterTypes, params object[] arguments)
        {
            MethodInfo method = typeof(ApiCallManager).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                parameterTypes,
                null);
            Assert.IsNotNull(method);
            return (T)method.Invoke(manager, arguments);
        }

        /// <summary>
        /// Materializes one approved request so the effective per-request API key can be compared without performing network I/O.
        /// </summary>
        private static string GetRequestApiKey(NexusModsService service, NexusSessionContext session)
        {
            using (HttpRequestMessage request = service.RequestPolicy.CreateRequest(
                HttpMethod.Get,
                new Uri("https://api.nexusmods.com/v1/games/skyrim.json"),
                session.Credentials))
            {
                return request.Headers.TryGetValues("apikey", out IEnumerable<string> values)
                    ? string.Join(",", values)
                    : null;
            }
        }

        /// <summary>
        /// Provides deterministic HTTP responses for repository authentication lifecycle tests.
        /// </summary>
        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

            /// <summary>
            /// Initializes the handler with the response callback used for each request.
            /// </summary>
            public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
            {
                _send = send ?? throw new ArgumentNullException(nameof(send));
            }

            /// <inheritdoc />
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _send(request, cancellationToken);
            }
        }
    }
}
